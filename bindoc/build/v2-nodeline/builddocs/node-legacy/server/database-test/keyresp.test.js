import assert from "node:assert/strict";
import { mkdtemp, mkdir, readFile, rm, stat, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import test from "node:test";
import { SignJWT, importPKCS8 } from "jose";
import {
  generateHostKeyPair,
  loadTrustedPublicKeys,
  publicJwks,
  verifyHostToken,
} from "../src/keyresp.js";

test("generates a key pair that verifies issuer JWTs and publishes only the public key", async (t) => {
  const root = await mkdtemp(join(tmpdir(), "mentos-keyresp-"));
  t.after(() => rm(root, { recursive: true, force: true }));

  const generatedDir = join(root, "generated");
  const trustedDir = join(root, "trusted");
  const generated = await generateHostKeyPair(generatedDir);
  await mkdir(trustedDir);
  await writeFile(
    join(trustedDir, `${generated.kid}.pem`),
    await readFile(generated.publicPath),
  );

  const privateMode = (await stat(generated.privatePath)).mode & 0o777;
  assert.equal(privateMode, 0o600);

  const trustedKeys = await loadTrustedPublicKeys(trustedDir);
  const jwks = await publicJwks(trustedKeys);
  assert.equal(jwks.keys.length, 1);
  assert.equal(jwks.keys[0].kid, generated.kid);
  assert.equal("d" in jwks.keys[0], false);

  const privateKey = await importPKCS8(
    await readFile(generated.privatePath, "utf8"),
    "RS256",
  );
  const token = await new SignJWT({
    scope: "db:read",
    resource: "students",
    action: "read",
  })
    .setProtectedHeader({ alg: "RS256", kid: generated.kid, typ: "JWT" })
    .setIssuer("mentos-host")
    .setAudience("mentos-db")
    .setSubject("mentos-api")
    .setIssuedAt()
    .setExpirationTime("5m")
    .sign(privateKey);

  const verified = await verifyHostToken(token, trustedKeys, {
    issuer: "mentos-host",
    audience: "mentos-db",
  });
  assert.equal(verified.payload.sub, "mentos-api");
  assert.equal(verified.payload.scope, "db:read");
  await assert.rejects(
    verifyHostToken(token, trustedKeys, { issuer: "wrong-issuer", audience: "mentos-db" }),
  );
});
