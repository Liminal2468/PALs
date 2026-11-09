import { spawn } from "node:child_process";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";

const mentosRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const maxAttempts = 3;
const retryDelayMs = 2_000;
const lifecheckDelayMs = 60_000;

const sleep = (milliseconds) => new Promise((resolveSleep) => {
  setTimeout(resolveSleep, milliseconds);
});

function runDbservHealthCheck() {
  const check = spawn("uv", ["run", "--project", "api", "python", "-m", "api.healthcheck"], {
    cwd: mentosRoot,
    stdio: "inherit",
  });
  return new Promise((resolveCheck, rejectCheck) => {
    check.once("error", rejectCheck);
    check.once("exit", (code, signal) => resolveCheck({ code, signal }));
  });
}

async function checkDbserv() {
  for (let attempt = 1; attempt <= maxAttempts; attempt += 1) {
    const result = await runDbservHealthCheck();
    if (result.code === 0) return true;
    console.warn(
      `DBSERV health check failed (code=${result.code}, signal=${result.signal ?? "none"}).`,
    );
    if (attempt < maxAttempts) await sleep(retryDelayMs);
  }
  return false;
}

async function waitForDbserv() {
  while (true) {
    if (await checkDbserv()) return;
    console.warn(`Mentos startup suspended; retrying DBSERV health in ${lifecheckDelayMs / 1000}s.`);
    await sleep(lifecheckDelayMs);
  }
}

await waitForDbserv();

const hostApi = spawn("uv", ["run", "--project", "api", "api"], {
  cwd: mentosRoot,
  stdio: "inherit",
});

let stopping = false;
for (const signal of ["SIGINT", "SIGTERM"]) {
  process.once(signal, () => {
    stopping = true;
    hostApi.kill(signal);
  });
}

hostApi.once("error", (error) => {
  console.error(`Could not start Mentos host API: ${error.message}`);
  process.exitCode = 1;
});
hostApi.once("exit", (code, signal) => {
  if (!stopping) {
    console.error(`Mentos host API exited (code=${code}, signal=${signal}).`);
    process.exitCode = code ?? 1;
  }
});
