import assert from "node:assert/strict";
import express from "express";
import test from "node:test";
import { app } from "../../src/app.js";
import { createAuthenticationPermissionGate } from "../../authentication/src/routes.js";

test("authentication permission gate maps invalid JWTs to forbidden", async (t) => {
  const testApp = express();
  testApp.use("/auth/v1", createAuthenticationPermissionGate((_req, res) => (
    res.status(401).json({
      error: { code: "unauthorized", message: "Invalid or expired token." },
    })
  )));
  testApp.use("/auth/v1", (_req, res) => res.status(200).json({ ok: true }));
  const server = testApp.listen(0, "127.0.0.1");
  t.after(() => new Promise((resolve, reject) => {
    server.close((error) => error ? reject(error) : resolve());
  }));
  await new Promise((resolve) => server.once("listening", resolve));

  const response = await fetch(
    `http://127.0.0.1:${server.address().port}/auth/v1/health`,
    { headers: { Authorization: "Bearer invalid-token" } },
  );
  assert.equal(response.status, 403);
  assert.deepEqual(await response.json(), {
    error: { code: "forbidden", message: "Insufficient permission." },
  });
});

test("mounts each API under its assigned prefix", async (t) => {
  const server = app.listen(0);
  t.after(() => new Promise((resolve, reject) => {
    server.close((error) => error ? reject(error) : resolve());
  }));
  await new Promise((resolve) => server.once("listening", resolve));
  const baseUrl = `http://127.0.0.1:${server.address().port}`;

  const databaseHealth = await fetch(`${baseUrl}/db/health`);
  assert.equal(databaseHealth.status, 200);
  assert.deepEqual(await databaseHealth.json(), { service: "database", status: "alive" });

  const databaseReadiness = await fetch(`${baseUrl}/db/health/database`);
  assert.equal(databaseReadiness.status, 401);

  const services = [
    ["/acc/health", "accounts"],
    ["/sched/health", "scheduler"],
    ["/fb/health", "feedback"],
    ["/adata/health", "analyst"],
    ["/connect/health", "connect"],
    ["/chat/v1/health", "chat"],
  ];

  for (const [path, service] of services) {
    const response = await fetch(`${baseUrl}${path}`);
    assert.equal(response.status, 200, path);
    const expected = {
      service,
      status: service === "chat" ? "skeleton" : "ready",
    };
    if (service === "connect") {
      expected.algorithm = "weighted-baseline-v1-draft";
    }
    assert.deepEqual(await response.json(), expected);
  }

  const notImplemented = await fetch(`${baseUrl}/acc/profile`);
  assert.equal(notImplemented.status, 501);
  assert.equal((await notImplemented.json()).error.code, "not_implemented");

  const providerEnv = await fetch(`${baseUrl}/auth/v1/.provider.env`);
  assert.equal(providerEnv.status, 403);
  assert.deepEqual(await providerEnv.json(), {
    error: { code: "forbidden", message: "Insufficient permission." },
  });

  for (const path of [
    "/auth/v1/health",
    "/auth/v1/providers/google/config",
    "/auth/v1/admins/login",
    "/auth/v1/not-a-route",
  ]) {
    for (const headers of [{}, { Authorization: "Basic not-a-bearer-token" }]) {
      const response = await fetch(`${baseUrl}${path}`, { headers });
      assert.equal(response.status, 403, path);
      assert.deepEqual(await response.json(), {
        error: { code: "forbidden", message: "Insufficient permission." },
      });
    }
  }

  const oldDatabasePrefix = await fetch(`${baseUrl}/api/students`);
  assert.equal(oldDatabasePrefix.status, 404);
});
