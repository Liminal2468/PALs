# HOST Authentication API Contract

Base path: `/api/v1/auth`. All routes are served by Mentos HOST. CLIENTs must
not call DBSERV directly. All JSON response bodies use the existing `data` or
`error` envelope, except `GET /api/v1/session`.

## Provider options

`GET /api/v1/auth/options`

Returns the configured sign-in choices and the always-present admin option.
Google remains visible but disabled until DBSERV reports complete credentials
and a redirect allowlist:

```json
{
  "data": {
    "providers": [
      {
        "id": "google",
        "label": "Google OAuth2",
        "start_url": "/api/v1/auth/google/start",
        "available": false,
        "status": "not_configured"
      }
    ],
    "planned_providers": [
      {
        "id": "microsoft-entra",
        "label": "Microsoft Entra",
        "status": "planned"
      }
    ],
    "admin": {
      "id": "admin",
      "label": "Admin login",
      "url": "/api/v1/auth/admin/login"
    }
  }
}
```

`dev` is absent unless `DEV_LOGIN_ENABLED=true`; HOST startup rejects that
setting when `APP_ENV=production` or `NODE_ENV=production`. OIDC providers are
absent unless every required `OIDC_*` setting is supplied. Options must be
refetched when the HOST deployment configuration changes.
When TLS terminates at a trusted proxy, enable `TRUST_PROXY` only if that proxy
overwrites forwarded headers; the OIDC callback must be seen by HOST at the
exact HTTPS URI registered in the provider configuration.

## Provider login

`GET /api/v1/auth/google/start`

Starts the Google OAuth2 authorization-code flow only when DBSERV reports
Google configured. HOST obtains public authorization settings from
`GET /auth/v1/providers/google/config` over its signed `auth:provider` JWT
channel. The HOST callback URI is derived from the request and must exactly
match an HTTPS URI in DBSERV's `GOOGLE_REDIRECT_URIS` allowlist. HOST stores
`state`, `nonce`, the PKCE verifier, and callback URI in its server-side
session, then redirects to Google's authorization endpoint with `S256` PKCE.
The client secret stays in DBSERV and is never returned to HOST or CLIENT.

The Google button is marked `Preview` when configured; this is a real Google
flow, not a fake/demo identity. If credentials or an allowed redirect URI are
missing, options report `available: false`, `status: "not_configured"`, and
start returns `503 provider_unavailable`.

`GET /api/v1/auth/google/callback`

Consumes the one-time session flow, checks `state`, then sends the code,
verifier, nonce, and exact redirect URI to DBSERV's
`POST /auth/v1/providers/google/exchange`. DBSERV exchanges the code and
validates the Google ID-token signature, issuer, audience, expiry, nonce, and
verified email. HOST then performs provider-subject account matching and
creates the pending signup or authenticated session described below.
Provider errors and invalid state return a generic `401`; DBSERV timeouts or
connection failures return `503 provider_unavailable`, and upstream HTTP
failures return `502 provider_unavailable`.

`GET /api/v1/auth/{provider}/start`

Starts a browser redirect. HOST creates one-time `state`, `nonce`, and PKCE
verifier values in its server-side browser session. For `dev` only, an optional
`identity` query selects one of the five seeded demo identities:
`student`, `tutor`, `tutee`, `teacher`, or `admin`. The dev provider returns to
HOST's callback; real OIDC providers redirect to their configured authorization
endpoint.

`POST /api/v1/auth/login`

Equivalent start route for clients that submit a provider identifier. Google
uses the same real OAuth flow as its dedicated start route; only `dev` accepts
the optional demo identity:

```json
{ "provider": "dev", "identity": "student" }
```

The response is a `302` redirect, not JSON. Follow the returned `Location` in a
browser; do not fetch it from a cross-origin frontend request.

`GET|POST /api/v1/auth/{provider}/callback` (non-Google providers)

Provider callback query/form fields:

```text
state=<one-time-state>
code=<authorization-code>
```

HOST consumes and compares the stored state before authenticating the
authorization code. OIDC adapters also check an exact configured redirect URI,
PKCE, the ID-token signature, issuer, audience, expiry, nonce, subject, and
verified email. The adapter returns only a generic failure to CLIENTs.

An active account matched by `(provider, provider_subject)` returns `200` and
creates a fresh authenticated session:

```json
{ "data": { "status": "authenticated", "roles": ["tutee"] } }
```

A previously unseen identity is linked to a new `pending` account and returns
`202`:

```json
{
  "data": {
    "status": "signup_required",
    "provider": "dev"
  }
}
```

The pending signup is kept in HOST's server-side session; it is not an
authenticated CLIENT session. Email is not used as an identity-matching key.
An existing pending account can resume signup through provider login. Suspended
or disabled accounts cannot authenticate.

## Signup completion

`POST /api/v1/auth/signup` requires the pending signup session and accepts only:

```json
{ "terms_accepted": true, "terms_version": "terms-v1" }
```

Returns `202` with `{ "data": { "status": "pending_review" } }`. This records
terms acceptance but does not activate the account or grant an authenticated
session. Activation remains an administrative decision.

## Admin login

`POST /api/v1/auth/admin/login`

Request:

```json
{ "username": "demo-admin", "password": "<runtime demo password>" }
```

Username must be 3-64 ASCII letters, digits, dot, underscore, or hyphen, with
an alphanumeric first character. Password must be 12-128 printable ASCII
characters. Credentials are stored in `admin_credentials`, separate from
`users`, with a scrypt hash. A successful password is not sufficient by itself:
the linked user must be active and have the `admin` role. Success returns `200`
with the authenticated role list and rotates the server-side session ID.

Unknown username, wrong password, inactive account, and a missing admin role
all return the same `401`:

```json
{
  "error": {
    "code": "invalid_credentials",
    "message": "Username or password is incorrect."
  }
}
```

Admin login is limited to five requests per 15 minutes by client IP and five
requests per 15 minutes by normalized username. HOST limits use in-memory
storage and therefore are per worker.

## Session and error responses

`GET /api/v1/session` returns `{ "authenticated": false }` or an authenticated
session summary. `DELETE /api/v1/session` revokes the current session and returns
`204`.

Expected auth error codes:

| HTTP | Code | Meaning |
|---|---|---|
| 400 | `bad_request` / `invalid_request` | Missing or invalid input |
| 401 | `invalid_credentials` | Provider or admin authentication failed |
| 401 | `signup_required` | No pending signup is stored in this session |
| 403 | `account_inactive` | Provider identity belongs to a non-active account |
| 403 | `forbidden` | DBSERV auth endpoint received no valid HOST JWT or the token lacks a route capability |
| 404 | `provider_unavailable` | Provider is disabled or not configured |
| 409 | `signup_unavailable` | Pending account is no longer available for signup |
| 429 | `rate_limited` | HOST request limit reached |
| 502 | `provider_unavailable` | DBSERV returned an upstream HTTP failure |
| 503 | `provider_unavailable` | Provider is not configured or DBSERV is unavailable |

Unexpected database or server errors use the standard sanitized `internal_error`
envelope. Auth-attempt records contain the event/provider, optional user ID,
outcome, reason code, time, and IPv4 `/24` or IPv6 `/64` client prefix. They do
not contain usernames, request bodies, passwords, tokens, or provider secrets.

## Provider status and future work

The `dev` adapter is separate from Google and remains available only when
explicitly enabled outside production. Google OAuth2 uses real provider
credentials and server-side code exchange; it has no fake identity/demo mode.
The browser-facing HOST origin is now intended to be
`https://solaris.softshell-grue.ts.net` while the laptop hosts Mentos. Google
sign-in must stay disabled until HOST is actually served over HTTPS and its
reverse proxy preserves the original scheme: Google requires HTTPS redirect
URIs for non-local hosts. Once HTTPS is available, register
`https://solaris.softshell-grue.ts.net/api/v1/auth/google/callback` in the
Google console and set the exact same URI in DBSERV's `GOOGLE_REDIRECT_URIS`.
`https://palsys.softshell-grue.ts.net` is the DBSERV public Funnel endpoint,
not the HOST callback. Every `/auth/v1` DBSERV request—including health and
unknown routes—requires a valid HOST JWT; requests without one receive
`403 forbidden`. HOST must never receive the Google client key. Microsoft
Entra is planned but not enabled.
For future multi-tenant Entra support, issuer validation must account for the
tenant-specific issuer and follow Microsoft's current
[issuer validation guidance](https://learn.microsoft.com/en-us/entra/identity-platform/access-tokens#validate-the-issuer).
