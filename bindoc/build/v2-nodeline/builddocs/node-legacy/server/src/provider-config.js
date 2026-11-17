import { constants } from "node:fs";
import { open } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import dotenv from "dotenv";

const moduleDirectory = dirname(fileURLToPath(import.meta.url));
export const PROVIDER_ENV_PATH = resolve(moduleDirectory, "../.provider.env");

export async function readProviderConfig(filePath = PROVIDER_ENV_PATH) {
  let file;
  try {
    file = await open(filePath, constants.O_RDONLY | constants.O_NOFOLLOW);
  } catch (error) {
    if (error.code === "ENOENT") return {};
    throw new Error("Provider configuration cannot be opened securely.", { cause: error });
  }

  try {
    const metadata = await file.stat();
    if (!metadata.isFile()) {
      throw new Error("Provider configuration must be a regular file.");
    }
    if (metadata.size > 16_384) {
      throw new Error("Provider configuration exceeds the maximum supported size.");
    }
    if (process.platform !== "win32" && (metadata.mode & 0o077) !== 0) {
      throw new Error("Provider configuration permissions must be owner-only (0600).");
    }
    const contents = await file.readFile({ encoding: "utf8" });
    return dotenv.parse(contents);
  } finally {
    await file.close();
  }
}

export function getGoogleProviderConfig(values) {
  const clientId = values.TMP_GCLOUD_CLIENTID?.trim();
  const clientSecret = values.TMP_GCLOUD_CLIENTKEY;
  if (!clientId || !clientSecret?.trim()) return null;
  if (
    clientId.length > 256
    || clientSecret.length > 512
    || /[\u0000-\u001f\u007f]/.test(clientId)
    || /[\u0000-\u001f\u007f]/.test(clientSecret)
  ) {
    const error = new Error("OAuth client credentials have invalid values.");
    error.code = "PROVIDER_CONFIGURATION_ERROR";
    throw error;
  }

  const allowedRedirectUris = (
    values.GOOGLE_REDIRECT_URIS
    ?? process.env.GOOGLE_REDIRECT_URIS
    ?? ""
  )
    .split(",")
    .map((value) => value.trim())
    .filter(Boolean);
  if (allowedRedirectUris.length === 0 || allowedRedirectUris.some((value) => {
    try {
      const uri = new URL(value);
      return uri.protocol !== "https:"
        || uri.username !== ""
        || uri.password !== ""
        || uri.hash !== "";
    } catch {
      return true;
    }
  })) {
    const error = new Error("Configured Google redirect URIs must be absolute HTTPS URLs.");
    error.code = "PROVIDER_CONFIGURATION_ERROR";
    throw error;
  }

  return {
    providerId: "google",
    clientId,
    clientSecret,
    authorizationUrl: "https://accounts.google.com/o/oauth2/v2/auth",
    issuer: "https://accounts.google.com",
    tokenUrl: "https://oauth2.googleapis.com/token",
    jwksUrl: "https://www.googleapis.com/oauth2/v3/certs",
    allowedRedirectUris,
  };
}
