import dotenv from "dotenv";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const csv = (value) => value.split(",").map((part) => part.trim()).filter(Boolean);
const sharedDirectory = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const databaseDirectory = resolve(sharedDirectory, "../database");

dotenv.config({ path: resolve(databaseDirectory, ".env") });

const legacyBindAddress = process.env.HOST;
if (process.env.BIND_ADDRESS === undefined && legacyBindAddress !== undefined) {
  console.warn("HOST is deprecated for the DBSERV bind address; use BIND_ADDRESS instead.");
}

export const config = {
  port: Number(process.env.PORT ?? 8100),
  host: process.env.BIND_ADDRESS ?? legacyBindAddress ?? "127.0.0.1",
  database: {
    host: process.env.DB_HOST ?? "127.0.0.1",
    port: Number(process.env.DB_PORT ?? 3306),
    user: process.env.DB_USER ?? "",
    password: process.env.DB_PASSWORD ?? "",
    name: process.env.DB_NAME ?? "",
    timezone: "Z",
    allowedTables: csv(process.env.DB_ALLOWED_TABLES ?? ""),
  },
  jwt: {
    issuer: process.env.JWT_ISSUER ?? "mentos-host",
    audience: process.env.JWT_AUDIENCE ?? "mentos-db",
    trustedKeysDir: resolve(
      databaseDirectory,
      process.env.JWT_TRUSTED_KEYS_DIR ?? "./keys/trusted",
    ),
    hostJwksUrl: process.env.JWT_HOST_JWKS_URL ?? "",
  },
};

export function validateConfig() {
  const missing = [];
  if (!config.database.host) missing.push("DB_HOST");
  if (!config.database.user) missing.push("DB_USER");
  if (!config.database.name) missing.push("DB_NAME");
  if (config.database.allowedTables.length === 0) missing.push("DB_ALLOWED_TABLES");
  if (!Number.isInteger(config.port) || config.port < 1 || config.port > 65535) {
    throw new Error("PORT must be an integer from 1 to 65535.");
  }
  if (
    !Number.isInteger(config.database.port)
    || config.database.port < 1
    || config.database.port > 65535
  ) {
    throw new Error("DB_PORT must be an integer from 1 to 65535.");
  }
  if (!config.jwt.issuer || !config.jwt.audience) {
    throw new Error("JWT_ISSUER and JWT_AUDIENCE must not be empty.");
  }
  if (missing.length > 0) {
    throw new Error(`Missing database configuration: ${missing.join(", ")}.`);
  }
  for (const table of config.database.allowedTables) {
    if (!/^[A-Za-z_][A-Za-z0-9_]*$/.test(table)) {
      throw new Error(`Invalid table name in DB_ALLOWED_TABLES: ${table}`);
    }
  }
}
