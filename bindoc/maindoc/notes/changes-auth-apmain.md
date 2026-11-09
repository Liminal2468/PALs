# DBSERV Authentication Changes

## 2026-11-09 — Add the HOST-facing Google provider settings endpoint

- Added `GET /auth/v1/providers/google/config`, protected by an RS256 HOST
  JWT with scope `auth:provider`, resource `authentication`, and action
  `provider_config`.
- Returned only the public Google client ID, fixed authorization endpoint,
  allowed scopes, and exact redirect URI allowlist. The client secret remains
  in DBSERV's private authentication configuration and is never returned.
- Missing credentials or redirect URIs report the provider as disabled, so
  HOST can show Google as unavailable without starting a fake/demo login.
- Updated the API and implementation documentation and added tests for the
  public response shape and the disabled state.
- **Verification:** `cd server && npm test` — 33 tests passed.
- **Defaults / unverified:** Redirect URIs must be HTTPS and exact-match
  allowlisted. The HOST origin supplied for now is HTTP, so Google remains
  disabled until TLS is available; the intended callback URI is
  `https://reime.softshell-grue.ts.net/api/v1/auth/google/callback`. No real
  provider calls were made; Google credentials and the schema v3 migration
  were not accessed or changed.

## Existing authentication implementation

DBSERV also provides the earlier protected Google code-exchange route and
separate admin-login router. The exchange verifies the Google ID-token
signature and claims server-side. Admin login uses strict input validation,
parameterized SQL, scrypt verification, active-account/admin-role checks,
throttling, and generic failures. Auth attempts keep only a truncated client
IP prefix and minimal reason/outcome metadata. The required schema v3 migration
has not been run.

Microsoft Entra is planned, not enabled. Its multi-tenant issuer validation
must follow Microsoft's current documentation when implemented.

## 2026-11-09 — Require HOST JWTs across the public auth prefix

The DBSERV public Funnel origin is `https://palsys.softshell-grue.ts.net`.
Every `/auth/v1` path, including health and unknown routes, now sits behind
HOST JWT verification. Missing or invalid tokens receive `403 forbidden`;
valid tokens still require the appropriate provider/admin capability.
