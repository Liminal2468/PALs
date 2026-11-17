import assert from "node:assert/strict";
import test from "node:test";
import { app } from "../../src/app.js";

test("mounts the matching API at /connect", async (t) => {
  const server = app.listen(0);
  t.after(() => new Promise((resolve, reject) => {
    server.close((error) => error ? reject(error) : resolve());
  }));
  await new Promise((resolve) => server.once("listening", resolve));
  const baseUrl = `http://127.0.0.1:${server.address().port}`;

  const health = await fetch(`${baseUrl}/connect/health`);
  assert.equal(health.status, 200);
  assert.deepEqual(await health.json(), {
    service: "connect",
    status: "ready",
    algorithm: "weighted-baseline-v1-draft",
  });

  const request = {
    learning_request_id: 23,
    tutee_user_id: 100,
    roles: ["tutee"],
    subject_id: 5,
    topic_id: 2,
    current_proficiency: 2,
    preferred_format: "one_to_one",
    status: "open",
    availability: [{
      start: "2026-11-02T09:00:00Z",
      end: "2026-11-02T11:00:00Z",
    }],
    preferences: ["worked_examples", "visual"],
  };
  const tutors = [
    {
      tutor_user_id: 201,
      roles: ["tutor"],
      subject_id: 5,
      topic_id: 2,
      proficiency_level: 5,
      status: "active",
      formats: ["one_to_one"],
      availability: [{
        start: "2026-11-02T09:30:00Z",
        end: "2026-11-02T10:00:00Z",
      }],
      preferences: ["worked_examples"],
    },
    {
      tutor_user_id: 202,
      roles: ["tutor"],
      subject_id: 5,
      topic_id: 2,
      proficiency_level: 3,
      status: "active",
      formats: ["one_to_one"],
      availability: [{
        start: "2026-11-02T09:30:00Z",
        end: "2026-11-02T10:00:00Z",
      }],
      preferences: ["visual"],
    },
    {
      tutor_user_id: 203,
      roles: ["tutor"],
      subject_id: 6,
      topic_id: 2,
      proficiency_level: 5,
      status: "active",
      formats: ["one_to_one"],
      availability: [{
        start: "2026-11-02T09:30:00Z",
        end: "2026-11-02T10:00:00Z",
      }],
      preferences: [],
    },
  ];

  const response = await fetch(`${baseUrl}/connect/match`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ request, tutors }),
  });
  assert.equal(response.status, 200);
  const result = await response.json();
  assert.equal(result.algorithm, "weighted-baseline-v1-draft");
  assert.deepEqual(result.data.map((match) => match.tutor_user_id), ["201", "202"]);
  assert.ok(result.data[0].score > result.data[1].score);
  assert.ok(result.data[0].explanation.includes("subject_match"));
});

test("lets a tutor find eligible tutee requests and validates malformed input", async (t) => {
  const server = app.listen(0);
  t.after(() => new Promise((resolve, reject) => {
    server.close((error) => error ? reject(error) : resolve());
  }));
  await new Promise((resolve) => server.once("listening", resolve));
  const baseUrl = `http://127.0.0.1:${server.address().port}`;
  const tutor = {
    tutor_user_id: 301,
    roles: ["tutor"],
    subject_id: 5,
    topic_id: 2,
    proficiency_level: 4,
    status: "active",
    formats: ["one_to_one", "group"],
    availability: [{ start: "2026-11-02T09:00:00Z", end: "2026-11-02T10:00:00Z" }],
    preferences: ["visual"],
  };
  const requests = [{
    learning_request_id: 91,
    tutee_user_id: 401,
    roles: ["tutee"],
    subject_id: 5,
    topic_id: 2,
    current_proficiency: 2,
    preferred_format: "group",
    status: "open",
    availability: [{ start: "2026-11-02T09:15:00Z", end: "2026-11-02T09:45:00Z" }],
    preferences: ["visual"],
  }];
  const response = await fetch(`${baseUrl}/connect/tutorreco`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ tutor, requests }),
  });
  assert.equal(response.status, 200);
  assert.deepEqual((await response.json()).data.map((match) => match.learning_request_id), ["91"]);

  const invalid = await fetch(`${baseUrl}/connect/recommendations`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ request: {}, tutors: [] }),
  });
  assert.equal(invalid.status, 400);
  assert.equal((await invalid.json()).error.code, "bad_request");

  const wrongRole = await fetch(`${baseUrl}/connect/match`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      request: { ...requests[0], roles: ["tutor"] },
      tutors: [tutor],
    }),
  });
  assert.equal(wrongRole.status, 400);
});
