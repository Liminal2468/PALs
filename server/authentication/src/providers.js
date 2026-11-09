import { Router } from "express";
import { timingSafeEqual } from "node:crypto";
import rateLimit from "express-rate-limit";
import { createRemoteJWKSet, jwtVerify } from "jose";
import { recordAuthAttempt } from "./auth-audit.js";
import { getGoogleProviderConfig, readProviderConfig } from "./provider-config.js";

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

const PROVIDER_RATE_LIMIT = {
  windowMs: 15 * 60 * 1000,
  limit: 30,
  standardHeaders: "draft-8",
  legacyHeaders: false,
};
let googleJwks;

function providerScope(action) {
  return (req, res, next) => {
    const scopes = typeof req.jwt?.scope === "string" ? req.jwt.scope.split(/\s+/) : [];
    if (
      !scopes.includes("auth:provider")
      || req.jwt?.resource !== "authentication"
      || req.jwt?.action !== action
    ) {
      return res.status(403).json({
        error: { code: "forbidden", message: "Insufficient permission." },
      });
    }
    return next();
  };
}

function invalidProviderResponse(res) {
  return res.status(401).json({
    error: { code: "provider_authentication_failed", message: "Provider authentication failed." },
  });
}

function validRequestBody(body) {
  return body
    && typeof body === "object"
    && !Array.isArray(body)
    && Object.keys(body).sort().join(",") === "code,code_verifier,nonce,redirect_uri"
    && typeof body.code === "string"
    && body.code.length >= 1
    && body.code.length <= 2048
    && /^[\x21-\x7e]+$/.test(body.code)
    && typeof body.code_verifier === "string"
    && /^[A-Za-z0-9._~-]{43,128}$/.test(body.code_verifier)
    && typeof body.nonce === "string"
    && /^[A-Za-z0-9._~-]{16,256}$/.test(body.nonce)
    && typeof body.redirect_uri === "string"
    && body.redirect_uri.length <= 2048;
}

export function createProvidersRouter({
  authenticateHost: authenticate = authenticateHost,
  loadConfig = readProviderConfig,
  exchangeCode = exchangeGoogleCode,
  database: selectedDatabase,
} = {}) {
  const router = Router();
  const rateLimiter = rateLimit({
    ...PROVIDER_RATE_LIMIT,
    handler: (_req, res) => res.status(429).json({
      error: { code: "rate_limited", message: "Too many requests." },
    }),
  });

  router.get("/:provider/config", authenticate, providerScope("provider_config"), rateLimiter, async (req, res) => {
    if (req.params.provider !== "google") {
      return res.status(404).json({
        error: { code: "provider_unavailable", message: "Provider is not enabled." },
      });
    }

    try {
      const settings = getGoogleProviderConfig(await loadConfig());
      if (!settings) {
        return res.status(200).json({
          data: { provider: "google", enabled: false },
        });
      }
      return res.status(200).json({
        data: {
          provider: "google",
          enabled: true,
          client_id: settings.clientId,
          authorization_endpoint: settings.authorizationUrl,
          redirect_uris: settings.allowedRedirectUris,
          scopes: ["openid", "email", "profile"],
        },
      });
    } catch (error) {
      if (
        error?.code !== "PROVIDER_CONFIGURATION_ERROR"
        && !error?.message?.startsWith("Provider configuration")
      ) {
        throw error;
      }
      console.error("Provider configuration is invalid:", error?.name ?? "UnknownError");
      return res.status(503).json({
        error: { code: "provider_unavailable", message: "Provider is not configured." },
      });
    }
  });

  router.post("/:provider/exchange", authenticate, providerScope("provider_exchange"), rateLimiter, async (req, res) => {
    const database = selectedDatabase
      ?? (await import("../../shared/src/db.js")).pool;
    if (req.params.provider !== "google") {
      await recordAuthAttempt(database, req, {
        event: "provider_callback",
        provider: "unknown",
        outcome: "failure",
        reasonCode: "provider_unavailable",
      });
      return res.status(404).json({
        error: { code: "provider_unavailable", message: "Provider is not enabled." },
      });
    }
    if (!validRequestBody(req.body)) {
      await recordAuthAttempt(database, req, {
        event: "provider_callback",
        provider: "google",
        outcome: "failure",
        reasonCode: "invalid_request",
      });
      return res.status(400).json({
        error: { code: "bad_request", message: "Invalid provider exchange request." },
      });
    }

    try {
      const settings = getGoogleProviderConfig(await loadConfig());
      if (!settings) {
        await recordAuthAttempt(database, req, {
          event: "provider_callback",
          provider: "google",
          outcome: "failure",
          reasonCode: "provider_unavailable",
        });
        return res.status(503).json({
          error: { code: "provider_unavailable", message: "Provider is not configured." },
        });
      }
      if (!settings.allowedRedirectUris.includes(req.body.redirect_uri)) {
        await recordAuthAttempt(database, req, {
          event: "provider_callback",
          provider: "google",
          outcome: "failure",
          reasonCode: "redirect_uri_not_allowed",
        });
        return invalidProviderResponse(res);
      }
      const identity = await exchangeCode(settings, req.body);
      await recordAuthAttempt(database, req, {
        event: "provider_callback",
        provider: "google",
        outcome: "success",
        reasonCode: "identity_verified",
      });
      return res.status(200).json({ data: identity });
    } catch (error) {
      if (
        error?.code === "PROVIDER_CONFIGURATION_ERROR"
        || error?.message?.includes("Provider configuration")
      ) {
        await recordAuthAttempt(database, req, {
          event: "provider_callback",
          provider: "google",
          outcome: "failure",
          reasonCode: "provider_unavailable",
        });
        return res.status(503).json({
          error: { code: "provider_unavailable", message: "Provider is not configured." },
        });
      }
      console.error("Provider authentication failed:", error?.name ?? "UnknownError");
      await recordAuthAttempt(database, req, {
        event: "provider_callback",
        provider: "google",
        outcome: "failure",
        reasonCode: "provider_response_invalid",
      });
      return invalidProviderResponse(res);
    }
  });

  return router;
}

async function exchangeGoogleCode(settings, {
  code,
  code_verifier: codeVerifier,
  nonce,
  redirect_uri: redirectUri,
}) {
  const abortSignal = AbortSignal.timeout(10_000);
  const tokenResponse = await fetch(settings.tokenUrl, {
    method: "POST",
    headers: { "Content-Type": "application/x-www-form-urlencoded" },
    body: new URLSearchParams({
      grant_type: "authorization_code",
      code,
      redirect_uri: redirectUri,
      client_id: settings.clientId,
      client_secret: settings.clientSecret,
      code_verifier: codeVerifier,
    }),
    signal: abortSignal,
    redirect: "error",
  });
  if (!tokenResponse.ok) {
    throw new Error("OAuth token exchange rejected.");
  }

  const contentLength = Number(tokenResponse.headers.get("content-length") ?? 0);
  if (contentLength > 32_768) {
    throw new Error("OAuth token response exceeded the size limit.");
  }
  const responseBody = await tokenResponse.text();
  if (responseBody.length > 32_768) {
    throw new Error("OAuth token response exceeded the size limit.");
  }
  const tokenData = JSON.parse(responseBody);
  if (
    !tokenData
    || typeof tokenData !== "object"
    || typeof tokenData.id_token !== "string"
    || tokenData.id_token.length > 16_384
  ) {
    throw new Error("OAuth token response was invalid.");
  }

  const identity = await verifyGoogleIdToken(settings, tokenData.id_token, nonce);
  return identity;
}

export async function verifyGoogleIdToken(settings, idToken, nonce, keySet) {
  if (!keySet) {
    googleJwks ??= createRemoteJWKSet(new URL(settings.jwksUrl), {
      timeoutDuration: 10_000,
      cooldownDuration: 30_000,
      cacheMaxAge: 300_000,
    });
  }
  const { payload } = await jwtVerify(idToken, keySet ?? googleJwks, {
    issuer: settings.issuer,
    audience: settings.clientId,
    algorithms: ["RS256"],
    requiredClaims: ["exp", "iss", "aud", "sub", "nonce"],
    maxTokenAge: "10m",
  });

  if (
    (Array.isArray(payload.aud) && payload.aud.length > 1 && payload.azp !== settings.clientId)
    ||
    typeof payload.sub !== "string"
    || payload.sub.length < 1
    || payload.sub.length > 191
    || typeof payload.nonce !== "string"
    || !constantTimeStringEqual(payload.nonce, nonce)
    || typeof payload.email !== "string"
    || payload.email.length > 254
    || payload.email_verified !== true
    || typeof payload.name !== "string"
    || payload.name.trim().length < 1
    || payload.name.length > 120
  ) {
    throw new Error("Provider identity claims were invalid.");
  }

  return {
    provider: "google",
    subject: payload.sub,
    email: payload.email,
    display_name: payload.name.trim(),
  };
}

function constantTimeStringEqual(first, second) {
  const firstBytes = Buffer.from(first);
  const secondBytes = Buffer.from(second);
  return firstBytes.length === secondBytes.length
    && timingSafeEqual(firstBytes, secondBytes);
}

export const providersRouter = createProvidersRouter();
