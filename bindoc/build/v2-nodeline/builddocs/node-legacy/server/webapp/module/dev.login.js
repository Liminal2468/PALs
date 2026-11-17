import { createHash, timingSafeEqual } from "node:crypto";
import { Router } from "express";

const EMAIL_KEYS = [
  ["API_ACCESS_REIME_EMAIL", "API_ACCESS_REIME_PASSWORD"],
  ["API_ACCESS_INDEV_SYS_EMAIL", "API_ACCESS_INDEV_SYS_PASSWORD"],
];

function equalSecret(actual, expected) {
  if (typeof actual !== "string" || typeof expected !== "string") return false;
  const actualDigest = createHash("sha256").update(actual).digest();
  const expectedDigest = createHash("sha256").update(expected).digest();
  return timingSafeEqual(actualDigest, expectedDigest);
}

export function developerAccounts(env = process.env) {
  return EMAIL_KEYS
    .map(([emailKey, passwordKey]) => ({
      email: env[emailKey]?.trim().toLowerCase() ?? "",
      password: env[passwordKey] ?? "",
    }))
    .filter(({ email, password }) => email && password);
}

export function authenticateDeveloper(email, password, env = process.env) {
  const normalizedEmail = typeof email === "string" ? email.trim().toLowerCase() : "";
  const account = developerAccounts(env).find(({ email: allowedEmail }) => (
    equalSecret(normalizedEmail, allowedEmail)
  ));

  if (!account || !equalSecret(password, account.password)) return null;
  return { email: account.email };
}

export function createDeveloperLoginRouter({ env = process.env } = {}) {
  const router = Router();
  router.post("/", (request, response) => {
    const result = authenticateDeveloper(request.body?.email, request.body?.password, env);
    if (!result) {
      return response.status(401).json({
        error: { code: "invalid_credentials", message: "Developer email or password is incorrect." },
      });
    }
    return response.json({ data: { status: "authenticated", email: result.email } });
  });
  return router;
}

export const developerLoginRouter = createDeveloperLoginRouter();
