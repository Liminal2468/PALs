import assert from "node:assert/strict";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test, { after, before } from "node:test";
import { setTimeout as delay } from "node:timers/promises";
import { SignJWT, importPKCS8 } from "jose";
import { generateHostKeyPair } from "../src/keyresp.js";

let app;
let pool;
let server;
let baseUrl;
let privateKey;
let kid;
let root;
let lastQuery;
let trustedKeyCacheTtlMs;

before(async () => {
  root = await mkdtemp(join(tmpdir(), "mentos-db-routes-"));
  const generatedDir = join(root, "generated");
  const trustedDir = join(root, "trusted");
  await mkdir(trustedDir);
  const generated = await generateHostKeyPair(generatedDir);
  kid = generated.kid;
  await writeFile(join(trustedDir, `${kid}.pem`), await readFile(generated.publicPath));
  privateKey = await importPKCS8(await readFile(generated.privatePath, "utf8"), "RS256");

  process.env.JWT_TRUSTED_KEYS_DIR = trustedDir;
  process.env.JWT_ISSUER = "mentos-host";
  process.env.JWT_AUDIENCE = "mentos-db";
  process.env.DB_ALLOWED_TABLES = "students";

  ({ TRUSTED_KEY_CACHE_TTL_MS: trustedKeyCacheTtlMs } = await import("../src/hostauth.js"));
  ({ app } = await import("../../src/app.js"));
  ({ pool } = await import("../src/db.js"));
  pool.query = async (sql) => {
    if (sql === "SELECT 1") return [[{ 1: 1 }], []];
    return [[
      { Field: "id", Key: "PRI" },
      { Field: "name", Key: "" },
      { Field: "status", Key: "" },
    ], []];
  };
  pool.execute = async (sql, values = []) => {
    lastQuery = { sql, values };
    if (sql.startsWith("INSERT")) return [{ insertId: 17, affectedRows: 1 }, []];
    if (sql.startsWith("DELETE")) return [{ affectedRows: 1 }, []];
    if (sql.startsWith("SELECT 1")) return [[], []];
    if (sql.startsWith("SELECT")) return [[{ id: 17, name: "Kai", status: "active" }], []];
    if (sql.startsWith("UPDATE")) return [{ affectedRows: 1 }, []];
    throw new Error(`Unexpected SQL in route test: ${sql} (${values.join(",")})`);
  };

  server = app.listen(0);
  await new Promise((resolve) => server.once("listening", resolve));
  baseUrl = `http://127.0.0.1:${server.address().port}`;
});

after(async () => {
  if (server) await new Promise((resolve, reject) => server.close((error) => error ? reject(error) : resolve()));
  if (pool) await pool.end();
  if (root) await rm(root, { recursive: true, force: true });
});

async function token(
  scope = "db:read",
  action = "read",
  resource = "students",
  signingKey = privateKey,
  keyId = kid,
) {
  return new SignJWT({ scope, action, resource })
    .setProtectedHeader({ alg: "RS256", kid: keyId, typ: "JWT" })
    .setIssuer("mentos-host")
    .setAudience("mentos-db")
    .setSubject("mentos-api")
    .setIssuedAt()
    .setExpirationTime("5m")
    .sign(signingKey);
}

test("publishes trusted keys and rejects missing or insufficient JWTs", async () => {
  const unauthenticatedHealth = await fetch(`${baseUrl}/db/health/database`);
  assert.equal(unauthenticatedHealth.status, 401);
  assert.equal((await unauthenticatedHealth.json()).error.code, "unauthorized");

  const databaseHealth = await fetch(`${baseUrl}/db/health/database`, {
    headers: { Authorization: `Bearer ${await token("db:read", "health", "database")}` },
  });
  assert.equal(databaseHealth.status, 200);
  assert.deepEqual(await databaseHealth.json(), { service: "database", status: "ready" });

  const generated = await generateHostKeyPair(join(root, "rotation"));
  const trustedDir = process.env.JWT_TRUSTED_KEYS_DIR;
  await writeFile(join(trustedDir, `${generated.kid}.pem`), await readFile(generated.publicPath));
  const rotatedPrivateKey = await importPKCS8(
    await readFile(generated.privatePath, "utf8"),
    "RS256",
  );
  await delay(trustedKeyCacheTtlMs + 50);

  const jwks = await fetch(`${baseUrl}/db/.well-known/jwks.json`);
  assert.equal(jwks.status, 200);
  const keys = (await jwks.json()).keys;
  assert.equal(keys.length, 2);
  assert.ok(keys.some((key) => key.kid === generated.kid));

  assert.equal((await fetch(`${baseUrl}/db/students`)).status, 401);
  assert.equal((await fetch(`${baseUrl}/api/students`)).status, 404);
  const insufficient = await fetch(`${baseUrl}/db/students`, {
    headers: {
      Authorization: `Bearer ${await token(
        "db:write",
        "read",
        "students",
        rotatedPrivateKey,
        generated.kid,
      )}`,
    },
  });
  assert.equal(insufficient.status, 403);
  assert.equal((await insufficient.json()).error.code, "forbidden");
});

test("serves the shared CRUD and aggregation route contract", async () => {
  const json = async (method, body, scope, action) => ({
    method,
    headers: {
      Authorization: `Bearer ${await token(scope, action)}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify(body),
  });

  const created = await fetch(
    `${baseUrl}/db/students`,
    await json("POST", { name: "Kai" }, "db:write", "create"),
  );
  assert.equal(created.status, 201);
  assert.deepEqual((await created.json()).data, { id: 17, affected_rows: 1 });

  const listed = await fetch(`${baseUrl}/db/students?status=active&limit=2&offset=4`, {
    headers: { Authorization: `Bearer ${await token("db:read", "read")}` },
  });
  assert.equal(listed.status, 200);
  assert.equal((await listed.json()).data[0].status, "active");
  assert.equal(lastQuery.sql, "SELECT * FROM `students` WHERE `status` = ? LIMIT ? OFFSET ?");
  assert.deepEqual(lastQuery.values, ["active", 2, 4]);

  const defaultPage = await fetch(`${baseUrl}/db/students`, {
    headers: { Authorization: `Bearer ${await token("db:read", "read")}` },
  });
  assert.equal(defaultPage.status, 200);
  assert.equal(lastQuery.sql, "SELECT * FROM `students` LIMIT ? OFFSET ?");
  assert.deepEqual(lastQuery.values, [100, 0]);

  for (const invalidQuery of [
    "limit=0",
    "limit=501",
    "offset=-1",
    "offset=9007199254740992",
    "limit=2&limit=3",
  ]) {
    const invalidPage = await fetch(`${baseUrl}/db/students?${invalidQuery}`, {
      headers: { Authorization: `Bearer ${await token("db:read", "read")}` },
    });
    assert.equal(invalidPage.status, 400, invalidQuery);
    assert.equal((await invalidPage.json()).error.code, "bad_request", invalidQuery);
  }

  const item = await fetch(`${baseUrl}/db/students/17`, {
    headers: { Authorization: `Bearer ${await token("db:read", "read")}` },
  });
  assert.equal((await item.json()).data.id, 17);

  const updated = await fetch(
    `${baseUrl}/db/students/17`,
    await json("PATCH", { name: "Lee" }, "db:write", "update"),
  );
  assert.equal(updated.status, 200);

  const aggregate = await fetch(
    `${baseUrl}/db/students/aggregate`,
    await json("POST", {
      group_by: "status",
      metrics: [{ operation: "count", field: "*", alias: "total" }],
    }, "db:read", "aggregate"),
  );
  assert.equal(aggregate.status, 200);

  const deleted = await fetch(`${baseUrl}/db/students/17`, {
    method: "DELETE",
    headers: { Authorization: `Bearer ${await token("db:write", "delete")}` },
  });
  assert.equal(deleted.status, 204);
});
