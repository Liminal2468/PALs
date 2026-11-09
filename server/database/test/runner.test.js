import assert from "node:assert/strict";
import test from "node:test";
import {
  getHealthBaseUrl,
  unexpectedChildExitCode,
} from "../../src/runner-utils.js";
import { readFile } from "node:fs/promises";

test("derives the runner health origin from the configured bind address and port", () => {
  assert.equal(getHealthBaseUrl("127.0.0.7", 8432), "http://127.0.0.7:8432");
  assert.equal(
    getHealthBaseUrl("127.0.0.7", 8432, "http://127.0.0.2:9000"),
    "http://127.0.0.2:9000",
  );
});

test("marks every unexpected child exit as a failure", () => {
  assert.equal(unexpectedChildExitCode(0), 1);
  assert.equal(unexpectedChildExitCode(7), 7);
  assert.equal(unexpectedChildExitCode(null), 1);
});

test("runner does not probe the JWT-protected authentication health path", async () => {
  const source = await readFile(new URL("../../src/runner.js", import.meta.url), "utf8");
  assert.doesNotMatch(source, /["']\/auth\/v1\/health["']/);
});
