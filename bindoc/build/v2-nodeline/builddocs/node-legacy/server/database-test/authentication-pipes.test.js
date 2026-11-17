import assert from "node:assert/strict";
import { chmod, mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { scryptSync } from "node:crypto";
import express from "express";
import { createLocalJWKSet, exportJWK, generateKeyPair, SignJWT } from "jose";
import test from "node:test";
import { createAdminsRouter } from "../../authentication/src/admins.js";
import {
  createProvidersRouter,
  verifyGoogleIdToken,
} from "../../authentication/src/providers.js";
import {
  getGoogleProviderConfig,
  readProviderConfig,
} from "../../authentication/src/provider-config.js";

class AuthDatabase {
  constructor() {
    this.credential = {
      user_id: 24,
      password_hash: "private-hash",
      account_status: "active",
      has_admin_role: 1,
    };
    this.queries = [];
  }

  async execute(sql, values = []) {
    this.queries.push({ sql, values });
    if (sql.includes("INSERT INTO auth_attempts")) return [{ affectedRows: 1 }, []];
    if (sql.includes("FROM admin_credentials")) {
      const username = values[1];
      return [[this.credential && username === "sample-admin" ? this.credential : undefined]
        .filter(Boolean), []];
    }
    if (sql.includes("FROM user_roles")) return [[{ role_key: "admin" }], []];
    throw new Error(`Unexpected SQL in auth test: ${sql}`);
  }
}

function createTestApp(router, mountPath) {
  const app = express();
  app.use(express.json());
  app.use(mountPath, router);
  return app;
}

async function postJson(app, path, body) {
  const server = app.listen(0, "127.0.0.1");
  await new Promise((resolve) => server.once("listening", resolve));
  try {
    const response = await fetch(`http://127.0.0.1:${server.address().port}${path}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    });
    return {
      status: response.status,
      body: await response.json(),
    };
  } finally {
    await new Promise((resolve, reject) => {
      server.close((error) => error ? reject(error) : resolve());
    });
  }
}

async function getJson(app, path) {
  const server = app.listen(0, "127.0.0.1");
  await new Promise((resolve) => server.once("listening", resolve));
  try {
    const response = await fetch(`http://127.0.0.1:${server.address().port}${path}`);
    return {
      status: response.status,
      body: await response.json(),
    };
  } finally {
    await new Promise((resolve, reject) => {
      server.close((error) => error ? reject(error) : resolve());
    });
  }
}

function providerAuth(req, _res, next) {
  req.jwt = {
    scope: "auth:provider",
    resource: "authentication",
    action: req.path.endsWith("/config") ? "provider_config" : "provider_exchange",
  };
  return next();
}

function adminAuth(req, _res, next) {
  req.jwt = {
    scope: "auth:admin",
    resource: "authentication",
    action: "admin_login",
  };
  return next();
}

const providerRequest = {
  code: "authorization-code",
  code_verifier: "v".repeat(43),
  nonce: "n".repeat(24),
  redirect_uri: "https://host.example.test/api/v1/auth/google/callback",
};

const providerConfig = {
  TMP_GCLOUD_CLIENTID: "public-client-id",
  TMP_GCLOUD_CLIENTKEY: "private-client-secret",
  GOOGLE_REDIRECT_URIS: providerRequest.redirect_uri,
};

test("provider config returns only public authorization data to a scoped HOST", async () => {
  const router = createProvidersRouter({
    authenticateHost: providerAuth,
    loadConfig: async () => providerConfig,
  });
  const app = createTestApp(router, "/providers");

  const response = await getJson(app, "/providers/google/config");

  assert.equal(response.status, 200);
  assert.deepEqual(response.body.data, {
    provider: "google",
    enabled: true,
    client_id: "public-client-id",
    authorization_endpoint: "https://accounts.google.com/o/oauth2/v2/auth",
    redirect_uris: [providerRequest.redirect_uri],
    scopes: ["openid", "email", "profile"],
  });
  assert.equal(JSON.stringify(response.body).includes("private-client-secret"), false);
});

test("provider config is explicitly unavailable until credentials are configured", async () => {
  const router = createProvidersRouter({
    authenticateHost: providerAuth,
    loadConfig: async () => ({}),
  });
  const app = createTestApp(router, "/providers");

  const response = await getJson(app, "/providers/google/config");

  assert.equal(response.status, 200);
  assert.deepEqual(response.body.data, {
    provider: "google",
    enabled: false,
  });
});

test("provider code exchange is scope-protected and never returns client credentials", async () => {
  const database = new AuthDatabase();
  let exchangeSettings;
  const router = createProvidersRouter({
    authenticateHost: providerAuth,
    database,
    loadConfig: async () => providerConfig,
    exchangeCode: async (settings) => {
      exchangeSettings = settings;
      return {
        provider: "google",
        subject: "subject-123",
        email: "SANTOS.123456@cubao.sti.edu.ph",
        display_name: "Demo Student",
      };
    },
  });
  const app = createTestApp(router, "/providers");

  const response = await postJson(
    app,
    "/providers/google/exchange",
    providerRequest,
  );

  assert.equal(response.status, 200);
  assert.deepEqual(response.body.data, {
    provider: "google",
    subject: "subject-123",
    email: "SANTOS.123456@cubao.sti.edu.ph",
    display_name: "Demo Student",
  });
  assert.equal(exchangeSettings.clientSecret, "private-client-secret");
  assert.equal(JSON.stringify(response.body).includes("private-client-secret"), false);
  assert.equal(database.queries.at(-1).values[4], "identity_verified");
});

test("provider exchange rejects non-organization or short student IDs", async () => {
  const database = new AuthDatabase();
  const router = createProvidersRouter({
    authenticateHost: providerAuth,
    database,
    loadConfig: async () => providerConfig,
    exchangeCode: async () => ({
      provider: "google",
      subject: "subject-unrecognized",
      email: "student.12345@cubao.sti.edu.ph",
      display_name: "Unrecognized Student",
    }),
  });
  const app = createTestApp(router, "/providers");

  const response = await postJson(app, "/providers/google/exchange", providerRequest);

  assert.equal(response.status, 401);
  assert.deepEqual(response.body, {
    error: {
      code: "account_not_recognized",
      message: "Use an authorized STI College Cubao organization account.",
    },
  });
  assert.equal(database.queries.at(-1).values[4], "account_not_recognized");
});

test("provider exchange rejects unlisted redirects before code exchange", async () => {
  const database = new AuthDatabase();
  let exchangeCalled = false;
  const router = createProvidersRouter({
    authenticateHost: providerAuth,
    database,
    loadConfig: async () => providerConfig,
    exchangeCode: async () => {
      exchangeCalled = true;
      return {};
    },
  });
  const app = createTestApp(router, "/providers");

  const response = await postJson(app, "/providers/google/exchange", {
    ...providerRequest,
    redirect_uri: "https://attacker.example.test/callback",
  });

  test("provider route rejects host JWTs without the provider capability", async () => {
    const database = new AuthDatabase();
    const router = createProvidersRouter({
      authenticateHost: (req, _res, next) => {
        req.jwt = {
          scope: "db:write",
          resource: "authentication",
          action: "provider_exchange",
        };
        next();
      },
      database,
      loadConfig: async () => providerConfig,
      exchangeCode: async () => {
        throw new Error("Must not exchange without auth scope.");
      },
    });
    const app = createTestApp(router, "/providers");

    const response = await postJson(app, "/providers/google/exchange", providerRequest);

    assert.equal(response.status, 403);
    assert.equal(response.body.error.code, "forbidden");
    assert.equal(database.queries.length, 0);
  });

  assert.equal(response.status, 401);
  assert.equal(response.body.error.code, "provider_authentication_failed");
  assert.equal(exchangeCalled, false);
  assert.equal(database.queries.at(-1).values[4], "redirect_uri_not_allowed");
});

test("provider exchange stays unavailable without a complete client credential pair", async () => {
  const database = new AuthDatabase();
  const router = createProvidersRouter({
    authenticateHost: providerAuth,
    database,
    loadConfig: async () => ({
      TMP_GCLOUD_CLIENTID: "public-client-id",
      TMP_GCLOUD_CLIENTKEY: "",
    }),
    exchangeCode: async () => {
      throw new Error("Must not exchange without configured credentials.");
    },
  });
  const app = createTestApp(router, "/providers");

  const response = await postJson(app, "/providers/google/exchange", providerRequest);

  assert.equal(response.status, 503);
  assert.equal(response.body.error.code, "provider_unavailable");
});

test("provider env reader refuses permissive files and returns no secret in errors", async () => {
  const directory = await mkdtemp(join(tmpdir(), "provider-config-"));
  const filename = join(directory, ".provider.env");
  try {
    await writeFile(filename, "TMP_GCLOUD_CLIENTID=test-id\nTMP_GCLOUD_CLIENTKEY=test-secret\n", {
      mode: 0o600,
    });
    assert.deepEqual(await readProviderConfig(filename), {
      TMP_GCLOUD_CLIENTID: "test-id",
      TMP_GCLOUD_CLIENTKEY: "test-secret",
    });
    await chmod(filename, 0o644);
    await assert.rejects(readProviderConfig(filename), /owner-only/);
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
});

test("Google is unavailable until both OAuth credentials and an HTTPS redirect are configured", () => {
  assert.equal(getGoogleProviderConfig({}), null);
  assert.throws(() => getGoogleProviderConfig({
    TMP_GCLOUD_CLIENTID: "client-id",
    TMP_GCLOUD_CLIENTKEY: "client-secret",
    GOOGLE_REDIRECT_URIS: "http://host.example.test/callback",
  }), /HTTPS URLs/);
});

test("Google ID tokens require a trusted RS256 signature, verified claims, and matching nonce", async () => {
  const { privateKey, publicKey } = await generateKeyPair("RS256");
  const jwk = await exportJWK(publicKey);
  jwk.kid = "test-google-key";
  const localJwks = createLocalJWKSet({ keys: [jwk] });
  const settings = {
    clientId: "google-client-id",
    issuer: "https://accounts.google.com",
    jwksUrl: "https://unused.example.test/jwks",
  };
  const idToken = await new SignJWT({
    nonce: "expected-nonce-123456",
    email: "student@example.test",
    email_verified: true,
    name: "Demo Student",
  })
    .setProtectedHeader({ alg: "RS256", kid: jwk.kid })
    .setIssuer(settings.issuer)
    .setAudience(settings.clientId)
    .setSubject("google-subject-1")
    .setIssuedAt()
    .setExpirationTime("5m")
    .sign(privateKey);

  const identity = await verifyGoogleIdToken(
    settings,
    idToken,
    "expected-nonce-123456",
    localJwks,
  );
  assert.deepEqual(identity, {
    provider: "google",
    subject: "google-subject-1",
    email: "student@example.test",
    display_name: "Demo Student",
  });
  await assert.rejects(verifyGoogleIdToken(
    settings,
    idToken,
    "wrong-nonce-1234567",
    localJwks,
  ));
});

test("admin login checks sanitized credentials, active status, and admin role", async () => {
  const database = new AuthDatabase();
  const router = createAdminsRouter({
    authenticateHost: adminAuth,
    database,
    checkPassword: async (password, hash) => password === "correct-password-123" && hash === "private-hash",
    dummyWork: async () => false,
  });
  const app = createTestApp(router, "/admins");

  const success = await postJson(app, "/admins/login", {
    username: "sample-admin",
    password: "correct-password-123",
  });
  assert.equal(success.status, 200);
  assert.deepEqual(success.body.data, {
    status: "authenticated",
    user_id: 24,
    roles: ["admin"],
  });
  assert.equal(JSON.stringify(success.body).includes("private-hash"), false);
  const lookup = database.queries.find((query) => query.sql.includes("FROM admin_credentials"));
  assert.match(lookup.sql, /WHERE ac\.username = \?/);
  assert.deepEqual(lookup.values, ["admin", "sample-admin"]);

  const invalid = await postJson(app, "/admins/login", {
    username: "sample-admin' OR 1=1 --",
    password: "correct-password-123",
  });
  assert.equal(invalid.status, 400);
});

test("admin login verifies the stored Python-compatible scrypt hash", async () => {
  const database = new AuthDatabase();
  const password = "correct-password-123";
  const salt = Buffer.alloc(16, 0x03);
  const digest = scryptSync(password, salt, 64, {
    N: 16_384,
    r: 8,
    p: 1,
    maxmem: 64 * 1024 * 1024,
  });
  database.credential = {
    ...database.credential,
    password_hash: `scrypt$16384$8$1$${salt.toString("base64url")}$${digest.toString("base64url")}`,
  };
  const router = createAdminsRouter({
    authenticateHost: adminAuth,
    database,
  });
  const app = createTestApp(router, "/admins");

  const valid = await postJson(app, "/admins/login", {
    username: "sample-admin",
    password,
  });
  const invalid = await postJson(app, "/admins/login", {
    username: "sample-admin",
    password: "wrong-password-123",
  });

  assert.equal(valid.status, 200);
  assert.equal(invalid.status, 401);
});

test("admin login uses one generic unauthorized response for unknown and invalid credentials", async () => {
  const database = new AuthDatabase();
  const router = createAdminsRouter({
    authenticateHost: adminAuth,
    database,
    checkPassword: async () => false,
    dummyWork: async () => false,
  });
  const app = createTestApp(router, "/admins");
  const wrongPassword = await postJson(app, "/admins/login", {
    username: "sample-admin",
    password: "wrong-password-123",
  });
  const unknownUser = await postJson(app, "/admins/login", {
    username: "unknown-admin",
    password: "wrong-password-123",
  });

  assert.equal(wrongPassword.status, 401);
  assert.deepEqual(unknownUser, wrongPassword);
});

test("admin login denies inactive and non-admin accounts", async () => {
  const database = new AuthDatabase();
  const router = createAdminsRouter({
    authenticateHost: adminAuth,
    database,
    checkPassword: async () => true,
    dummyWork: async () => false,
  });
  const app = createTestApp(router, "/admins");

  database.credential = { ...database.credential, account_status: "suspended" };
  const inactive = await postJson(app, "/admins/login", {
    username: "sample-admin",
    password: "correct-password-123",
  });
  database.credential = { ...database.credential, account_status: "active", has_admin_role: 0 };
  const nonAdmin = await postJson(app, "/admins/login", {
    username: "sample-admin",
    password: "correct-password-123",
  });

  assert.equal(inactive.status, 401);
  assert.deepEqual(nonAdmin, inactive);
});

test("admin login rate-limits repeated attempts by client IP", async () => {
  const database = new AuthDatabase();
  const router = createAdminsRouter({
    authenticateHost: adminAuth,
    database,
    checkPassword: async () => false,
    dummyWork: async () => false,
  });

  test("admin route rejects host JWTs without the admin capability", async () => {
    const database = new AuthDatabase();
    const router = createAdminsRouter({
      authenticateHost: (req, _res, next) => {
        req.jwt = {
          scope: "auth:provider",
          resource: "authentication",
          action: "admin_login",
        };
        next();
      },
      database,
      checkPassword: async () => true,
    });
    const app = createTestApp(router, "/admins");

    const response = await postJson(app, "/admins/login", {
      username: "sample-admin",
      password: "correct-password-123",
    });

    assert.equal(response.status, 403);
    assert.equal(response.body.error.code, "forbidden");
    assert.equal(database.queries.length, 0);
  });
  const app = createTestApp(router, "/admins");

  for (let attempt = 0; attempt < 5; attempt += 1) {
    const response = await postJson(app, "/admins/login", {
      username: `sample-admin-${attempt}`,
      password: "wrong-password-123",
    });
    assert.equal(response.status, 401);
  }
  const limited = await postJson(app, "/admins/login", {
    username: "sample-admin",
    password: "wrong-password-123",
  });

  assert.equal(limited.status, 429);
  assert.equal(limited.body.error.code, "rate_limited");
});
