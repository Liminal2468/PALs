import { spawn } from "node:child_process";
import { config } from "@mentos/shared-core/config";
import { pool } from "@mentos/shared-core/db";
import { getHealthBaseUrl, unexpectedChildExitCode } from "./runner-utils.js";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

const serverRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const healthBaseUrl = getHealthBaseUrl(
  config.host,
  config.port,
  process.env.API_HEALTH_BASE_URL,
);
const endpoints = [
  "/db/health",
  "/acc/health",
  "/sched/health",
  "/fb/health",
  "/adata/health",
  "/connect/health",
  "/chat/v1/health",
];
const maxAttempts = 3;
const retryDelayMs = 2_000;

const sleep = (milliseconds) => new Promise((resolveSleep) => {
  setTimeout(resolveSleep, milliseconds);
});

async function checkEndpoint(endpoint) {
  let lastError;
  for (let attempt = 1; attempt <= maxAttempts; attempt += 1) {
    try {
      const response = await fetch(new URL(endpoint, healthBaseUrl), {
        signal: AbortSignal.timeout(5_000),
      });
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }
      return;
    } catch (error) {
      lastError = error;
      if (attempt < maxAttempts) await sleep(retryDelayMs);
    }
  }
  console.warn(`WARN: API unresponsive due to: ${endpoint} (${lastError.message})`);
}

const server = spawn(process.execPath, ["src/server.js"], {
  cwd: serverRoot,
  stdio: "inherit",
});

let stopping = false;
for (const signal of ["SIGINT", "SIGTERM"]) {
  process.once(signal, () => {
    stopping = true;
    server.kill(signal);
  });
}

server.once("error", (error) => {
  console.error(`Could not start API server: ${error.message}`);
  process.exitCode = 1;
});

server.once("exit", (code, signal) => {
  if (!stopping) {
    console.error(`API server exited unexpectedly (code=${code}, signal=${signal}).`);
    process.exitCode = unexpectedChildExitCode(code);
  }
});

await Promise.all(endpoints.map(checkEndpoint));

try {
  await pool.query("SELECT 1");
} catch (error) {
  console.warn(`WARN: API unresponsive due to: /db/health/database (${error.message})`);
} finally {
  try {
    await pool.end();
  } catch (error) {
    console.error("Could not close runner database pool:", error);
    process.exitCode = 1;
  }
}
