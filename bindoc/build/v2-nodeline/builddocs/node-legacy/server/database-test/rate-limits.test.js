import assert from "node:assert/strict";
import { mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test, { after, before } from "node:test";
import { SignJWT, importPKCS8 } from "jose";
import { generateHostKeyPair } from "../src/keyresp.js";

let app;
let pool;
let server;
let baseUrl;
let root;
let signingKeys;

before(async () => {
  root = await mkdtemp(join(tmpdir(), "mentos-rate-limits-"));
  const trustedDir = join(root, "trusted");
  await mkdir(trustedDir);
  signingKeys = [];

  for (let index = 0; index < 2; index += 1) {
    const generated = await generateHostKeyPair(join(root, `generated-${index}`));
    await writeFile(
      join(trustedDir, `${generated.kid}.pem`),
      await readFile(generated.publicPath),
    );
    signingKeys.push({
      kid: generated.kid,
      key: await importPKCS8(await readFile(generated.privatePath, "utf8"), "RS256"),
    });
  }

  process.env.JWT_TRUSTED_KEYS_DIR = trustedDir;
  process.env.JWT_ISSUER = "mentos-host";
  process.env.JWT_AUDIENCE = "mentos-db";
  ({ app } = await import("../../src/app.js"));
  ({ pool } = await import("../src/db.js"));
  pool.query = async () => [[{ 1: 1 }], []];

  server = app.listen(0, "127.0.0.1");
  await new Promise((resolve) => server.once("listening", resolve));
  baseUrl = `http://127.0.0.1:${server.address().port}`;
});

after(async () => {
  if (server) {
    await new Promise((resolve, reject) => {
      server.close((error) => error ? reject(error) : resolve());
    });
  }
  if (pool) await pool.end();
  if (root) await rm(root, { recursive: true, force: true });
});

async function tokenFor(key) {
  return new SignJWT({
    scope: "db:read",
    resource: "database",
    action: "health",
  })
    .setProtectedHeader({ alg: "RS256", kid: key.kid, typ: "JWT" })
    .setIssuer("mentos-host")
    .setAudience("mentos-db")
    .setSubject("mentos-api")
    .setIssuedAt()
    .setExpirationTime("5m")
    .sign(key.key);
}

async function request(path, address, token) {
  const headers = { "X-Forwarded-For": address };
  if (token) headers.Authorization = `Bearer ${token}`;
  const response = await fetch(`${baseUrl}${path}`, { headers });
  await response.arrayBuffer();
  return response.status;
}

test("the database IP limit counts failed and successful public checks", async () => {
  for (let count = 0; count < 60; count += 1) {
    assert.equal(
      await request("/db/health/database", "198.51.100.10"),
      401,
    );
  }
  assert.equal(await request("/db/health/database", "198.51.100.10"), 429);
  assert.equal(await request("/db/health", "198.51.100.11"), 200);
  for (let count = 0; count < 60; count += 1) {
    assert.equal(await request("/db/health", "198.51.100.12"), 200);
  }
  assert.equal(await request("/db/health", "198.51.100.12"), 429);
});

test("authenticated database traffic is bounded by the database IP quota", async () => {
  const [firstKey, secondKey] = signingKeys;
  const firstToken = await tokenFor(firstKey);
  const secondToken = await tokenFor(secondKey);

  for (let count = 0; count < 60; count += 1) {
    assert.equal(
      await request("/db/health/database", "198.51.100.20", firstToken),
      200,
    );
  }
  assert.equal(
    await request("/db/health/database", "198.51.100.20", secondToken),
    429,
  );
});
