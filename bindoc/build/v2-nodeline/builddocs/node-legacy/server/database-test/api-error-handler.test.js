import assert from "node:assert/strict";
import test from "node:test";
import { app, serializeApiError } from "../../src/app.js";

test("serializes common client error statuses with matching codes", () => {
  for (const [status, code] of [
    [400, "bad_request"],
    [401, "unauthorized"],
    [403, "forbidden"],
    [404, "not_found"],
    [413, "payload_too_large"],
  ]) {
    const response = serializeApiError({ status });
    assert.equal(response.status, status);
    assert.equal(response.body.error.code, code);
  }
  assert.deepEqual(serializeApiError(new Error("failure")), {
    status: 500,
    body: { error: { code: "internal_error", message: "Request failed." } },
  });
});

test("returns the payload-too-large error for oversized JSON requests", async (t) => {
  const server = app.listen(0, "127.0.0.1");
  t.after(() => new Promise((resolve, reject) => {
    server.close((error) => error ? reject(error) : resolve());
  }));
  await new Promise((resolve) => server.once("listening", resolve));
  const body = JSON.stringify({ value: "x".repeat(256 * 1024) });
  const response = await fetch(`http://127.0.0.1:${server.address().port}/acc/health`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body,
  });
  assert.equal(response.status, 413);
  assert.equal((await response.json()).error.code, "payload_too_large");
});
