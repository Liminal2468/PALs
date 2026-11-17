import assert from "node:assert/strict";
import { spawn } from "node:child_process";
import { createServer } from "node:net";
import { setTimeout as delay } from "node:timers/promises";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";
import test from "node:test";

const serverRoot = resolve(dirname(fileURLToPath(import.meta.url)), "../..");

async function availablePort(host) {
  const listener = createServer();
  await new Promise((resolveListen, reject) => {
    listener.once("error", reject);
    listener.listen(0, host, resolveListen);
  });
  const { port } = listener.address();
  await new Promise((resolveClose, reject) => {
    listener.close((error) => error ? reject(error) : resolveClose());
  });
  return port;
}

async function waitForHealth(child, baseUrl) {
  const deadline = Date.now() + 5_000;
  while (Date.now() < deadline) {
    if (child.exitCode !== null) {
      throw new Error(`API process exited before becoming ready (${child.exitCode}).`);
    }
    try {
      const response = await fetch(`${baseUrl}/acc/health`, {
        signal: AbortSignal.timeout(500),
      });
      if (response.ok) return;
    } catch {
      await delay(50);
    }
  }
  throw new Error(`API process did not become ready at ${baseUrl}.`);
}

async function assertServerBinding(t, host, configuredHost, legacy = false) {
  const port = await availablePort(host);
  const childEnv = {
    ...process.env,
    PORT: String(port),
    DB_HOST: "127.0.0.1",
    DB_PORT: "3306",
    DB_USER: "test",
    DB_PASSWORD: "test",
    DB_NAME: "test",
    DB_ALLOWED_TABLES: "students",
  };
  delete childEnv.BIND_ADDRESS;
  delete childEnv.HOST;
  if (configuredHost === undefined) {
    delete childEnv.BIND_ADDRESS;
    delete childEnv.HOST;
  } else if (legacy) {
    childEnv.HOST = configuredHost;
  } else {
    childEnv.BIND_ADDRESS = configuredHost;
  }

  const child = spawn(process.execPath, ["src/server.js"], {
    cwd: serverRoot,
    env: childEnv,
    stdio: ["ignore", "ignore", "pipe"],
  });
  let stderr = "";
  child.stderr.setEncoding("utf8");
  child.stderr.on("data", (chunk) => {
    stderr += chunk;
  });
  t.after(async () => {
    if (child.exitCode === null) {
      child.kill("SIGTERM");
      await new Promise((resolveExit) => child.once("exit", resolveExit));
    }
  });

  const baseUrl = `http://${host}:${port}`;
  await waitForHealth(child, baseUrl);
  const response = await fetch(`${baseUrl}/acc/health`);
  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { service: "accounts", status: "ready" });
  if (legacy) {
    assert.match(stderr, /HOST is deprecated.*BIND_ADDRESS/);
  }
}

test("binds to loopback by default and supports an explicit loopback host", async (t) => {
  await assertServerBinding(t, "127.0.0.1");
  await assertServerBinding(t, "127.0.0.2", "127.0.0.2");
});

test("supports the deprecated HOST bind setting with a warning", async (t) => {
  await assertServerBinding(t, "127.0.0.2", "127.0.0.2", true);
});
