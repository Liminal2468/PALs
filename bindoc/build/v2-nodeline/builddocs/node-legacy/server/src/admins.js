import { createHash, scrypt, timingSafeEqual } from "node:crypto";
import { Router } from "express";
import rateLimit from "express-rate-limit";
import { recordAuthAttempt } from "./auth-audit.js";

let hostJwtMiddleware;

async function authenticateHost(req, res, next) {
  if (req.jwt && req.jwtKeyId) return next();
  try {
    hostJwtMiddleware ??= import("../../database/src/hostauth.js")
      .then(({ requireJwt }) => requireJwt);
    return (await hostJwtMiddleware)(req, res, next);
  } catch (error) {
    return next(error);
  }
}

const SCRYPT_N = 16_384;
const SCRYPT_R = 8;
const SCRYPT_P = 1;
const SCRYPT_KEY_LENGTH = 64;
const SCRYPT_MAX_MEMORY = 64 * 1024 * 1024;
const USERNAME_PATTERN = /^[A-Za-z0-9][A-Za-z0-9._-]{2,63}$/;
const PASSWORD_PATTERN = /^[\x20-\x7e]{12,128}$/;

function adminScope(req, res, next) {
  const scopes = typeof req.jwt?.scope === "string" ? req.jwt.scope.split(/\s+/) : [];
  if (
    !scopes.includes("auth:admin")
    || req.jwt?.resource !== "authentication"
    || req.jwt?.action !== "admin_login"
  ) {
    return res.status(403).json({
      error: { code: "forbidden", message: "Insufficient permission." },
    });
  }
  return next();
}

function makeLimiter(keyGenerator) {
  return rateLimit({
    windowMs: 15 * 60 * 1000,
    limit: 5,
    ...(keyGenerator ? { keyGenerator } : {}),
    standardHeaders: "draft-8",
    legacyHeaders: false,
    handler: (_req, res) => res.status(429).json({
      error: { code: "rate_limited", message: "Too many requests." },
    }),
  });
}

function decodeBase64Url(value) {
  if (!/^[A-Za-z0-9_-]+$/.test(value)) throw new Error("Invalid hash encoding.");
  return Buffer.from(value, "base64url");
}

function scryptAsync(password, salt) {
  return new Promise((resolve, reject) => {
    scrypt(
      password,
      salt,
      SCRYPT_KEY_LENGTH,
      {
        N: SCRYPT_N,
        r: SCRYPT_R,
        p: SCRYPT_P,
        maxmem: SCRYPT_MAX_MEMORY,
      },
      (error, derivedKey) => error ? reject(error) : resolve(derivedKey),
    );
  });
}

async function verifyPassword(password, encodedHash) {
  try {
    const [algorithm, n, r, p, saltValue, expectedValue, ...extra] = encodedHash.split("$");
    if (
      extra.length
      || algorithm !== "scrypt"
      || n !== String(SCRYPT_N)
      || r !== String(SCRYPT_R)
      || p !== String(SCRYPT_P)
    ) {
      throw new Error("Unsupported scrypt hash.");
    }
    const salt = decodeBase64Url(saltValue);
    const expected = decodeBase64Url(expectedValue);
    if (salt.length !== 16 || expected.length !== SCRYPT_KEY_LENGTH) {
      throw new Error("Invalid scrypt hash size.");
    }
    const derived = await scryptAsync(password, salt);
    return timingSafeEqual(derived, expected);
  } catch {
    return performDummyPasswordWork(password);
  }
}

async function performDummyPasswordWork(password) {
  const dummySalt = Buffer.alloc(16, 0x5a);
  const expected = await scryptAsync(password, dummySalt);
  return timingSafeEqual(expected, Buffer.alloc(SCRYPT_KEY_LENGTH));
}

function invalidCredentials(res) {
  return res.status(401).json({
    error: {
      code: "invalid_credentials",
      message: "Username or password is incorrect.",
    },
  });
}

export function createAdminsRouter({
  authenticateHost: authenticate = authenticateHost,
  database: selectedDatabase,
  checkPassword = verifyPassword,
  dummyWork = performDummyPasswordWork,
} = {}) {
  const router = Router();
  const ipLimiter = makeLimiter();
  const usernameLimiter = makeLimiter((req) => {
    const username = typeof req.body?.username === "string"
      ? req.body.username.trim().toLowerCase()
      : "";
    return createHash("sha256").update(username).digest("hex");
  });

  router.post(
    "/login",
    authenticate,
    adminScope,
    ipLimiter,
    usernameLimiter,
    async (req, res) => {
      const database = selectedDatabase
        ?? (await import("../../shared/src/db.js")).pool;
      const body = req.body;
      if (
        !body
        || typeof body !== "object"
        || Array.isArray(body)
        || Object.keys(body).sort().join(",") !== "password,username"
        || typeof body.username !== "string"
        || !USERNAME_PATTERN.test(body.username)
        || typeof body.password !== "string"
        || !PASSWORD_PATTERN.test(body.password)
      ) {
        await recordAuthAttempt(database, req, {
          event: "admin_login",
          provider: "admin",
          outcome: "failure",
          reasonCode: "invalid_request",
        });
        return res.status(400).json({
          error: { code: "bad_request", message: "Invalid admin login input." },
        });
      }

      const username = body.username.toLowerCase();
      const [rows] = await database.execute(
        `SELECT ac.user_id, ac.password_hash, u.account_status,
          EXISTS (
            SELECT 1
            FROM user_roles ur
            JOIN roles r ON r.id = ur.role_id
            WHERE ur.user_id = ac.user_id AND r.role_key = ?
          ) AS has_admin_role
         FROM admin_credentials ac
         JOIN users u ON u.id = ac.user_id
         WHERE ac.username = ?
         LIMIT 1`,
        ["admin", username],
      );
      const credential = rows[0];
      const passwordValid = credential
        ? await checkPassword(body.password, credential.password_hash)
        : await dummyWork(body.password);

      if (!credential || !passwordValid) {
        await recordAuthAttempt(database, req, {
          event: "admin_login",
          provider: "admin",
          outcome: "failure",
          reasonCode: "invalid_credentials",
          userId: credential?.user_id ?? null,
        });
        return invalidCredentials(res);
      }

      if (
        credential.account_status !== "active"
        || Number(credential.has_admin_role) !== 1
      ) {
        await recordAuthAttempt(database, req, {
          event: "admin_login",
          provider: "admin",
          outcome: "failure",
          reasonCode: credential.account_status !== "active"
            ? "account_inactive"
            : "admin_role_required",
          userId: credential.user_id,
        });
        return invalidCredentials(res);
      }

      const [roleRows] = await database.execute(
        `SELECT r.role_key
         FROM user_roles ur
         JOIN roles r ON r.id = ur.role_id
         WHERE ur.user_id = ?
         ORDER BY r.role_key`,
        [credential.user_id],
      );
      const roles = roleRows.map((row) => row.role_key);
      await recordAuthAttempt(database, req, {
        event: "admin_login",
        provider: "admin",
        outcome: "success",
        reasonCode: "authenticated",
        userId: credential.user_id,
      });
      return res.status(200).json({
        data: {
          status: "authenticated",
          user_id: credential.user_id,
          roles,
        },
      });
    },
  );

  return router;
}

export const adminsRouter = createAdminsRouter();
