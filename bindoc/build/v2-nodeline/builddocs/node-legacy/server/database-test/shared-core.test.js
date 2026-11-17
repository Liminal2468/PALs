import assert from "node:assert/strict";
import test from "node:test";
import { config, validateConfig } from "@mentos/shared-core/config";
import { pool as sharedPool } from "@mentos/shared-core/db";
import { pool as databasePool } from "../src/db.js";

test("database router helpers use the shared config and MariaDB pool", () => {
  assert.equal(databasePool, sharedPool);
  assert.equal(typeof validateConfig, "function");
  assert.equal(typeof config.host, "string");
  assert.equal(config.database.timezone, "Z");
});

test("validates config before creating the shared database pool", () => {
  const allowedTables = config.database.allowedTables;
  config.database.allowedTables = [];
  try {
    assert.throws(() => sharedPool.query("SELECT 1"), /Missing database configuration/);
  } finally {
    config.database.allowedTables = allowedTables;
  }
});
