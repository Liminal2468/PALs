# Authentication API implementation

The DBSERV authentication workspace provides the internal routes documented in
[`../../API.md`](../../API.md#authentication--authv1):

- `POST /auth/v1/providers/google/exchange` exchanges an authorization code,
  verifies the OIDC ID token, and returns a minimized provider identity.
- `GET /auth/v1/providers/google/config` returns the public Google client ID,
  authorization endpoint, scopes, and exact redirect allowlist to HOST. It
  never returns the OAuth client secret.
- `POST /auth/v1/admins/login` validates admin credentials, account status, and
  the admin role before returning the authenticated user ID and roles.

The `/auth/v1` router has a top-level JWT gate, including health and unknown
paths. A missing or invalid HOST bearer JWT returns `403 forbidden`; valid
tokens must also carry the route-specific capability. OAuth client credentials
are loaded only by the authentication service from the ignored
`authentication/.provider.env`, which must be owner-only (`0600`). The host
application starts and completes the real Google browser flow through the
provider config and exchange capabilities; the client secret never leaves
DBSERV. Microsoft Entra remains planned. The schema v3 migration must be
applied before admin login can query its credential and attempt tables.
