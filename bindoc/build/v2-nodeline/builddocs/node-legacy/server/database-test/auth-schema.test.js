import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import test from "node:test";

const schemaPath = new URL("../schemas/schema_v3_auth.sql", import.meta.url);
const envExamplePath = new URL("../.env.example", import.meta.url);

test("auth migration separates provider identities and credentials from users", async () => {
  const schema = await readFile(schemaPath, "utf8");

  assert.match(schema, /MODIFY external_subject VARCHAR\(191\) NULL/);
  assert.match(schema, /DROP INDEX uq_users_external_subject/);
  assert.match(schema, /DROP INDEX uq_users_email/);
  assert.match(schema, /UNIQUE KEY uq_auth_identities_provider_subject \(provider, provider_subject\)/);
  assert.match(schema, /CREATE TABLE IF NOT EXISTS admin_credentials/);
  assert.match(schema, /password_hash VARCHAR\(255\)/);
  assert.match(schema, /CREATE TABLE IF NOT EXISTS auth_attempts/);
  assert.match(schema, /client_ip_prefix VARCHAR\(50\)/);
  assert.match(schema, /'student'/);
  assert.match(schema, /'teacher'/);
});

test("database example documents the auth table allowlist additions", async () => {
  const example = await readFile(envExamplePath, "utf8");

  assert.match(example, /auth_identities,admin_credentials,auth_attempts/);
});
