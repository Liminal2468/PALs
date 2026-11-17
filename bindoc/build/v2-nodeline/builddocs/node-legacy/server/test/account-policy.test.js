import assert from "node:assert/strict";
import test from "node:test";
import { isOrganizationAccountEmail } from "../src/account-policy.js";

test("organization account policy accepts a six-digit-or-longer STUID", () => {
  assert.equal(isOrganizationAccountEmail("SANTOS.123456@cubao.sti.edu.ph"), true);
  assert.equal(isOrganizationAccountEmail("DELA-CRUZ.123456789@CUBAO.STI.EDU.PH"), true);
});

test("organization account policy rejects personal, malformed, and short-ID accounts", () => {
  assert.equal(isOrganizationAccountEmail("student@example.test"), false);
  assert.equal(isOrganizationAccountEmail("SANTOS.12345@cubao.sti.edu.ph"), false);
  assert.equal(isOrganizationAccountEmail("123456@cubao.sti.edu.ph"), false);
  assert.equal(isOrganizationAccountEmail("SANTOS.123456@other.sti.edu.ph"), false);
});
