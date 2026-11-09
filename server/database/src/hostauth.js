import { config } from "@mentos/shared-core/config";
import { loadTrustedPublicKeys, verifyHostToken } from "./keyresp.js";

export const TRUSTED_KEY_CACHE_TTL_MS = 5_000;

let trustedKeysCache;
let trustedKeysExpiresAt = 0;
let trustedKeysLoad;

export async function getTrustedKeys() {
  if (trustedKeysCache && Date.now() < trustedKeysExpiresAt) {
    return trustedKeysCache;
  }

  if (!trustedKeysLoad) {
    trustedKeysLoad = loadTrustedPublicKeys(config.jwt.trustedKeysDir)
      .then((trustedKeys) => {
        if (trustedKeys.size === 0) {
          throw new Error(
            `No trusted host JWT public keys found in ${config.jwt.trustedKeysDir}.`,
          );
        }
        trustedKeysCache = trustedKeys;
        trustedKeysExpiresAt = Date.now() + TRUSTED_KEY_CACHE_TTL_MS;
        return trustedKeys;
      })
      .finally(() => {
        trustedKeysLoad = undefined;
      });
  }
  return trustedKeysLoad;
}

export async function requireJwt(req, res, next) {
  const [scheme, token, ...extra] = (req.get("authorization") ?? "").split(" ");
  if (scheme !== "Bearer" || !token || extra.length > 0) {
    return res.status(401).json({ error: { code: "unauthorized", message: "Bearer token required." } });
  }

  let trustedKeys;
  try {
    trustedKeys = await getTrustedKeys();
  } catch (error) {
    return next(error);
  }

  try {
    const verified = await verifyHostToken(token, trustedKeys, config.jwt);
    req.jwt = verified.payload;
    req.jwtKeyId = verified.protectedHeader.kid;
    return next();
  } catch {
    return res.status(401).json({ error: { code: "unauthorized", message: "Invalid or expired token." } });
  }
}

export function requireScope(scope, action) {
  return (req, res, next) => {
    const scopes = typeof req.jwt?.scope === "string" ? req.jwt.scope.split(/\s+/) : [];
    if (
      !scopes.includes(scope)
      || req.jwt?.resource !== req.params.resource
      || req.jwt?.action !== action
    ) {
      return res.status(403).json({ error: { code: "forbidden", message: "Insufficient permission." } });
    }
    return next();
  };
}
