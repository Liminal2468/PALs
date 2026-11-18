# PALs OAuth 2.0/OIDC implementation plan

Status: planned, not implemented

Date: 2026-11-18

## Objective

Replace the current Mentos email-and-terms provisioning façade with a
PALs-owned authentication flow that supports organization accounts, persistent
Identity credentials, role claims, and the existing DBSERV-backed HOST session.

The implementation should use OAuth 2.0 Authorization Code Flow with PKCE and
OpenID Connect claims. OAuth 2.0 provides authorization; OpenID Connect adds
the identity layer required for login. References:

- OAuth 2.0 Authorization Framework: https://www.rfc-editor.org/info/rfc6749/
- PKCE: https://www.rfc-editor.org/info/rfc7636/
- OpenID Connect Core: https://openid.net/specs/openid-connect-core-1_0.html

This is a PALs-owned flow. Google and Microsoft Entra are not prerequisites and
will not be enabled by this plan.

## Current starting point

- Mentos exposes POST /api/v1/auth/login and creates a DBSERV session after
  validating the school-email format.
- DBSERV has ASP.NET Identity persistence code and an interactive seed command.
- DBSERV persists HOST sessions in web_sessions.
- Student policy requires LASTNAME.STUID@cubao.sti.edu.ph with a six-digit
  minimum STUID.
- Admin/developer login already uses ASP.NET Identity passwords and roles.
- OAuth provider start, callback, signup, token, JWKS, and authorization-code
  persistence do not exist in the active .NET implementation.
- Schemas v5-v7 are present in the repository but are not automatically
  applied. The database migration account must be used because dbpals lacks
  schema-management privileges.

## Architecture decision gate

Before coding, confirm these decisions:

1. server/ remains the owner of Identity users, PALs users/roles, OAuth
   clients, authorization codes, and token persistence.
2. DBSERV remains private and is never called directly by browser JavaScript.
3. Mentos remains the public browser-facing HOST and acts as the BFF/client
   boundary.
4. The public authorization endpoint is either exposed through a narrowly
   scoped Mentos façade or through a separate PALs auth origin. Generic
   DBSERV access must not be made public merely to expose OAuth.
5. Mentos receives a browser session cookie; access and refresh tokens are not
   stored in browser localStorage.
6. PALs account roles are server-derived. Client-supplied role values are not
   trusted.

Recommended decision: keep DBSERV private, expose only the narrowly scoped
authorization flow through Mentos, and retain the existing pals_host_session
cookie as the browser session after successful code exchange.

## Phase 0 — Database and key prerequisites

Deliverables:

- Confirm MariaDB is reachable from server/.
- Apply the existing base schemas if missing.
- Apply schema v5 for ASP.NET Identity.
- Apply schema v7 for web_sessions.
- Seed development accounts interactively.
- Create a separate OAuth schema migration account or use a DBA account.
- Define the signing-key storage location outside source control.
- Define local callback origins and the later Cloudflare-tunnel callback
  origin separately.

Required OAuth persistence design:

- oauth_clients: client identifier, redirect URI allowlist, grant type,
  scope allowlist, status, and owner metadata.
- oauth_authorization_codes: one-time code hash, client, redirect URI,
  PKCE challenge, user, scopes, expiry, and consumed timestamp.
- oauth_refresh_tokens: hashed token or token family identifier, user,
  client, scopes, expiry, rotation/revocation state, and timestamps.
- oauth_consents: user, client, scopes, policy version, and decision time when
  consent is required by the approved policy.

Never store plaintext authorization codes or refresh tokens. Do not place
signing private keys, database passwords, or client secrets in Git, YAML
examples, browser code, or archives.

Gate: the schemas exist in a dedicated development database, Identity can
authenticate a seeded account, and web_sessions can create/revoke a session.

## Phase 1 — OAuth/OIDC server foundation in DBSERV

Deliverables:

- Add an OAuth authorization-server service with strict client registration.
- Add authorization-code creation and one-time consumption.
- Enforce exact redirect URI matching.
- Require PKCE S256 for the Mentos client.
- Generate and validate state and nonce values at the HOST boundary.
- Add short authorization-code expiry and replay protection.
- Add signing-key loading, key identifier handling, rotation metadata, and a
  JWKS response without exposing private material.
- Add scopes limited to the current first-party needs, initially openid,
  profile, email, and pals.roles where appropriate.
- Map the verified subject to the existing PALs users row and Identity user.
- Reject inactive, unrecognized, or role-ineligible accounts.

Initial server-side endpoints, subject to the architecture gate:

- GET /oauth/authorize
- POST /oauth/authorize/decision
- POST /oauth/token
- GET /oauth/userinfo
- GET /.well-known/openid-configuration
- GET /oauth/jwks
- POST /oauth/revoke

The implementation must not use the implicit grant or put tokens in URL
fragments.

Gate: unit tests cover valid code exchange, wrong client, wrong redirect URI,
PKCE mismatch, state/nonce mismatch, expired code, replayed code, revoked
refresh token, inactive account, and unauthorized role.

## Phase 2 — Mentos HOST integration

Deliverables:

- Add an auth-start route that creates a short-lived browser-bound state and
  PKCE verifier.
- Redirect the browser to the PALs authorization endpoint.
- Add the callback route and validate state before code exchange.
- Exchange the code server-side.
- Validate issuer, audience, signature, expiry, nonce, subject, and exact
  redirect URI.
- Resolve the PALs account and create the existing DBSERV web session.
- Set the existing HttpOnly, SameSite, secure-in-production cookie.
- Return sanitized authentication errors without leaking account existence.

The current POST /api/v1/auth/login route should remain available behind a
development compatibility flag during migration, then be removed or changed
to initiate the OAuth flow after acceptance testing.

Gate: a browser can complete login locally through Mentos, reach pals.html,
call /api/v1/session, access protected data, and log out with the DBSERV
session revoked.

## Phase 3 — Organization account and role policy

Deliverables:

- Preserve the six-digit STUID rule for student accounts.
- Keep LASTNAME.STUID as a format boundary, not as the identity key.
- Use the verified stable subject as the identity key.
- Keep admin/developer accounts separate from student self-service signup.
- Define tutor and tutee role assignment as server-side grants.
- Define whether a student may self-enroll as tutor, tutee, or both.
- Record account linking and role changes in an audit trail.

Signup, if approved, must create a pending account and must not grant admin,
developer, tutor, or tutee privileges from client input.

Gate: policy tests cover malformed email, five-digit STUID, inactive account,
duplicate subject, duplicate email, role escalation, and account linking.

## Phase 4 — Frontend transition

Deliverables:

- Replace the current email-only login form with the OAuth start action.
- Remove provider UI that implies Google or Entra availability.
- Preserve the existing admin login path until the shared OAuth/admin policy
  decision is complete.
- Remove or disable legacy localStorage authentication/session fallbacks.
- Add loading, cancellation, expired-state, denied-consent, and retry states.
- Verify that no access token, refresh token, code verifier, or client secret
  is exposed to page scripts.

Gate: the live frontend uses the new flow for a fresh browser profile and
does not silently fall back to demo credentials or localStorage session state.

## Phase 5 — Security, reliability, and integration testing

Test categories:

- OAuth protocol: redirect URI, state, nonce, PKCE, code replay, token
  expiry, refresh rotation, revocation, and JWKS key rotation.
- Identity: account mapping, disabled accounts, terms, role claims, and
  school-email/STUID policy.
- Session: cookie flags, fixation resistance, logout, expiry, concurrent
  sessions, and database failure behavior.
- Abuse controls: authorization endpoint rate limits, password-attempt limits,
  code brute force, oversized payloads, and generic error responses.
- Privacy: no passwords/tokens in logs, minimal claims, consent records, and
  safe account-not-recognized responses.
- Operations: startup without a database, startup with missing keys, database
  outage, key rotation, backup/restore, and service restart.
- End-to-end: local Mentos, DBSERV, MariaDB, browser login, protected API,
  logout, and seeded role scenarios.

Gate: all protocol/security integration tests pass against a disposable
development database. No production or APMAIN deployment is part of this
implementation phase.

## Phase 6 — Documentation and contract update

This phase is required before the OAuth implementation is considered complete.

Update server/API.md:

- Replace the current email-only login description in lines 55-58.
- Document authorization, callback, token, userinfo, JWKS, revoke, and
  discovery routes.
- Document the exact public HOST boundary and private DBSERV boundary.
- Document scopes, claims, session creation, error codes, and redirect rules.
- Mark the compatibility login route and all deferred routes accurately.
- Document the OAuth schema and migration prerequisite.

Update server/README.md:

- Add local MariaDB and schema prerequisites.
- Add key-generation and key-placement instructions without printing secrets.
- Add YAML configuration names and callback-origin examples.
- Add local run commands for DBSERV and Mentos.
- Add account seeding and role-testing commands.
- Add OAuth test commands and expected health/session checks.
- Document that the runtime database account is not the migration account.
- Document the Cloudflare-tunnel callback setup separately from local testing.

Also record the completed phase in
bindoc/maindoc/notes/changes-act.md and update any overlapping auth contract
documents so they do not describe the retired Node implementation.

Gate: documentation matches the route table and a new developer can run the
flow from the README without relying on undocumented secrets or paths.

## Completion criteria

OAuth/OIDC is ready for controlled development testing only when:

1. Identity and OAuth schemas are applied to the development database.
2. The PALs client, signing keys, redirect allowlist, and scopes are configured.
3. Authorization Code + PKCE login succeeds through Mentos.
4. The verified identity maps to the correct PALs account and server-derived
   roles.
5. Mentos creates and revokes the existing DBSERV session.
6. Replay, redirect, PKCE, nonce, state, expiry, revocation, and outage tests
   pass.
7. server/API.md and server/README.md are updated and reviewed.

The current recommended starting point is Phase 0. Code implementation can
begin before MariaDB is available using unit-test doubles, but end-to-end
OAuth login cannot be accepted until the database and schemas are active.
