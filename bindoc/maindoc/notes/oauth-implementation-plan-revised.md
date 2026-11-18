# PALs OAuth 2.0 / OpenID Connect implementation plan

**Status:** Phase 0 in progress — first-party TOTP foundation implemented; OAuth/OIDC not implemented  
**Revision date:** 2026-11-18  
**Scope:** Local development only; no APMAIN deployment, remote service restart, or Cloudflare Tunnel operation.

## 1. Objective

Replace the current Mentos email-format-and-terms provisioning flow with a PALs-owned authentication system using OAuth 2.0 Authorization Code Flow with PKCE and OpenID Connect (OIDC) for authentication.

The intended result is:

- PALs owns authentication, client registration, authorization decisions, identity mapping, and role policy.
- ASP.NET Core Identity remains the credential and account-management foundation.
- DBSERV remains private and owns identity persistence, authorization-server state, and PALs session persistence.
- Mentos remains the browser-facing application and Backend-for-Frontend (BFF).
- The browser receives only an opaque HttpOnly PALs session cookie. OAuth tokens and client credentials remain server-side.
- Student identity is established through an explicit verification policy, not by parsing a school-email-shaped string.

This is a first-party PALs identity system. Google and Microsoft Entra are not prerequisites and must not be introduced by this plan.

## 1.1 Approved MFA direction

PALs will use ASP.NET Core Identity's standard TOTP provider as an additional
factor for PALs-owned accounts. Google Authenticator is an allowed TOTP client,
not the identity provider: PALs will not use Google Sign-In, Google OAuth, or
Google-hosted account records. Any compatible authenticator application may be
used when it supports the PALs `otpauth://` setup URI.

The initial policy is:

- Require TOTP for `admin` and `developer` accounts after their authenticator
  setup is complete.
- Require or strongly recommend TOTP for tutors before granting sensitive
  student-resource operations, subject to the approved school policy.
- Keep student/tutee enrollment separate from MFA enrollment; TOTP proves
  possession of the enrolled authenticator secret, not ownership of an STI
  email address.
- Use one-time recovery codes, rate-limited verification, persistent Data
  Protection keys, and short-lived hashed replay records.
- Do not use SMS, email codes, or Google Sign-In as a substitute for the
  first-party account-verification policy.

The active implementation now contains the Identity TOTP setup/status,
confirmation, disablement, developer-login challenge, HOST admin-login
challenge, and `schema_v8_mfa.sql` replay-protection foundation. The OAuth
authorization server, OIDC tokens, and browser BFF callback are still not
implemented.

OAuth 2.0 is an authorization framework; OIDC adds the authentication protocol and ID Token. The implementation must follow current OAuth security guidance, not only the original OAuth 2.0 and PKCE specifications.

## 2. Project constraints

These constraints are implementation requirements:

- Workspace: `/home/sol/ReiMe/gproj/web/act`.
- `server/` is DBSERV; `mentos/` is HOST/BFF.
- `mentos/public/` is preserved frontend content. Do not modify its styles unless explicitly authorized.
- DBSERV must not be directly exposed to browser JavaScript or made generally public.
- Local-only testing. Do not deploy to APMAIN, restart remote services, or operate Cloudflare Tunnel unless explicitly requested.
- Use ASP.NET Core / .NET 10, C#, MariaDB/MySQL, EF Core, ASP.NET Core Identity, and existing YamlDotNet configuration support.
- Mentos defaults to `127.0.0.1:8000`; DBSERV defaults to `127.0.0.1:8432`.
- Runtime DB account `dbpals` retains CRUD-only privileges. Apply migrations using a separate DBA/migration account; never grant permanent DDL privileges to `dbpals`.
- Real configuration and secrets stay out of Git. DB passwords remain in `server/.env` or process environment. Signing private keys and client secrets must not be placed in YAML examples, source, archives, logs, or chat.
- Environment variables override YAML. Blank or omitted YAML values are ignored.
- Do not implement `/connect`, tutor matching, recommendations, or unrelated deferred functionality as part of this work.
- Do not claim any planned route, schema, security control, or test is already implemented.

## 3. Current baseline (not yet the target design)

Mentos currently exposes:

- `GET /api/v1/auth/options`
- `POST /api/v1/auth/login`
- `POST /api/v1/auth/admin/login`
- `GET /api/v1/session`
- `POST /api/v1/auth/logout`
- `DELETE /api/v1/session`

The current student login accepts `LASTNAME.STUID@cubao.sti.edu.ph`, requires at least six STUID digits and `termsAccepted=true`, then provisions/finds a PALs account and creates a DBSERV session. This is not password authentication, OAuth, OIDC, or proof of school-account ownership.

Admin/developer login currently uses ASP.NET Core Identity and seeded roles. The browser receives an HttpOnly `pals_host_session` cookie containing an opaque DBSERV session key. DBSERV persists sessions in `web_sessions`; logout revokes the DBSERV session.

The current DBSERV internal API uses `X-Pals-Host-Key`, and user-scoped calls also use `X-Pals-Session`. These private service credentials must never be sent to browser code.

Relevant schema history currently documented by the project:

- v1-v4: PALs accounts, roles, sessions, authentication, and developer structures.
- v5: ASP.NET Identity tables and migration history.
- v6: frontend feature tables.
- v7: `web_sessions` and schedule events.

The runtime account lacks schema-management privileges. Prior v5 application attempts failed because `dbpals` lacked `CREATE`. Confirm actual applied migration state before relying on any schema.

## 4. Architecture decision

### 4.1 Selected topology

Use **DBSERV as the logical authorization server and identity authority, with Mentos as the only public HTTP boundary**.

Do not expose DBSERV directly. Do not create a broad reverse proxy. Mentos should implement a small, explicit OAuth/OIDC façade that forwards only the required operations to dedicated internal DBSERV handlers.

Logical ownership:

| Concern | Owner |
|---|---|
| Identity users, credentials, lockout, account status | DBSERV / ASP.NET Core Identity |
| PALs user mapping and server-side roles | DBSERV |
| OAuth client registry and redirect allowlists | DBSERV |
| Authorization decisions and consent policy | DBSERV, rendered through a narrowly scoped Mentos authorization UI |
| Authorization-code and refresh-token persistence | DBSERV |
| Signing keys and token issuance | DBSERV |
| Public browser-facing routes and redirects | Mentos |
| OAuth callback transaction, PKCE verifier, and browser binding | Mentos |
| Browser session cookie and BFF session boundary | Mentos |
| Persistent PALs session record and revocation | DBSERV |

This preserves one identity authority without making the private database service a public web application.

### 4.2 Public and private route boundary

The browser may reach only Mentos routes. Mentos validates request shape and transaction context, then calls the corresponding private DBSERV operation over the trusted local/service network using the HOST shared key.

Public Mentos routes (proposed):

- `GET /oauth/authorize` — browser navigation entry; validates request parameters through DBSERV and renders the PALs sign-in/consent experience.
- `POST /oauth/authorize/decision` — receives a same-origin, CSRF-protected user decision and forwards the decision to DBSERV.
- `POST /oauth/token` — public protocol endpoint, but token exchange is performed by Mentos-to-DBSERV server communication. It must not become a generic proxy.
- `GET /oauth/userinfo` — expose only if the design actually needs a bearer-token UserInfo endpoint. Prefer the BFF to validate the ID Token and create its session without returning tokens to browser JavaScript.
- `GET /.well-known/openid-configuration` — public, static/DBSERV-derived metadata with public Mentos endpoint URLs.
- `GET /oauth/jwks` — public signing-key set containing public keys only.
- `POST /oauth/revoke` — public revocation endpoint if refresh/access token revocation is supported.
- `GET /oauth/error` or an equivalent safe error page — optional browser-facing terminal error page.

Private DBSERV operations (proposed; exact paths may differ):

- Validate authorization request and client.
- Resolve authenticated Identity principal and account eligibility.
- Create and atomically consume authorization code.
- Exchange code and issue tokens.
- Validate/revoke tokens.
- Return signing metadata and public JWK set.
- Create/revoke PALs HOST sessions.
- Record security audit events.

Internal routes must continue to require `X-Pals-Host-Key`; user-scoped operations must also require a validated session or a narrowly defined internal authorization context. The shared key is service authentication, not a substitute for end-user authorization.

### 4.3 Authorization UI ownership

The browser must interact with a real PALs sign-in/authorization page. Mentos can render this page while DBSERV makes the authentication and authorization decisions.

Do not let a public request directly assert a user ID, role, consent decision, or account status. Mentos forwards a decision bound to a server-created authorization transaction; DBSERV independently verifies that transaction, authenticated subject, client, requested scopes, and decision.

### 4.4 OAuth client type

Register Mentos as a **confidential server-side web client** because the code exchange occurs between Mentos and DBSERV, not in browser JavaScript. Require PKCE S256 even for this confidential client.

- Use a distinct client ID per environment.
- Store the client secret as a protected secret outside Git/YAML and rotate it.
- Compare client authentication using a supported secure method; never log the secret.
- Do not embed a client secret in HTML, JavaScript, source maps, or frontend configuration.
- Do not enable dynamic client registration.
- Do not enable implicit, password, or client-credentials grants for user login.
- Initially allow only `authorization_code`; add `refresh_token` only if the BFF actually needs it.

If the eventual architecture changes to a public native/mobile client, register it separately as a public client and do not give it a secret.

## 5. Identity and account policy — blocking decision

### 5.1 Email syntax is not identity proof

`LASTNAME.STUID@cubao.sti.edu.ph` is a format policy only. It does not prove that the person controls the school mailbox, is currently enrolled, or is the student represented by the STUID.

Before student self-service login/signup is accepted, choose and document one verifiable enrollment/ownership mechanism, for example:

- administrator-provisioned accounts using a trusted roster;
- a school-controlled verification channel actually operated by STI and integrated with PALs; or
- a controlled invitation/verification process performed by authorized staff.

Do not describe a PALs-issued password as proof of STI account ownership. Do not send verification mail to an address unless PALs has a functioning, trusted mail delivery and verification process.

### 5.2 Stable identity

Use an immutable PALs subject identifier (`sub`) that maps to exactly one existing PALs user. Do not use email, lastname, STUID text, display name, or mutable profile attributes as the OIDC subject.

The database should enforce uniqueness for the subject mapping. Store email as a contact/claim attribute with an explicit verification state and timestamp. If the existing PALs users table already has an immutable primary key suitable for `sub`, document and use it; otherwise add a dedicated immutable public subject identifier.

### 5.3 Account linking

- Never auto-link accounts solely because email addresses match.
- Linking requires the user to authenticate to the existing PALs account and prove control of the new identity through the approved verification process, or requires an audited administrator action.
- Enforce unique constraints on both identity provider/issuer + external subject and PALs user mapping.
- Make linking transactional; reject collisions and ambiguous mappings.
- Record actor, target account, old/new subject reference, timestamp, reason, and outcome in an audit log. Do not log credentials or tokens.
- Do not permit a newly verified email claim to silently overwrite an existing account's verified email.
- Provide an explicit unlink/recovery policy that cannot orphan privileged accounts.

### 5.4 Roles and authorization

Roles are assigned and changed only by trusted server-side policy or audited administration.

- Never accept `role`, `isAdmin`, `isDeveloper`, tutor/tutee privileges, or equivalent privilege claims from browser input, signup fields, email local-parts, or token request parameters.
- Do not grant developer/admin roles through student signup or account linking.
- Tutor/tutee assignment must follow an explicit policy and server-side approval state.
- Define whether students may hold tutor, tutee, or both roles before emitting `pals.roles`.
- Re-check account active/disabled state and current role grants when creating a session. Do not treat a stale ID Token as the source of current authorization.
- Keep tokens' claims minimal. Do not expose internal database IDs unless there is a documented need.

## 6. OAuth/OIDC protocol requirements

### 6.1 Authorization request

Support only `response_type=code` and the registered Mentos client initially.

Validate:

- `client_id` exists, is active, and is permitted for the requested flow.
- `redirect_uri` exactly matches a registered URI, including scheme, host, port, path, and permitted query semantics. No wildcard, prefix, substring, open redirect, or user-supplied redirect.
- `scope` contains `openid`; reject unknown or disallowed scopes.
- `state` is present, sufficiently random, transaction-specific, and bound to the browser transaction.
- `nonce` is present for OIDC and bound to the same transaction.
- `code_challenge` is present and syntactically valid; `code_challenge_method` must be `S256`. Reject `plain`.
- `response_mode` is restricted to the supported query response for this server-rendered web flow unless another mode is deliberately implemented and tested.
- Request size and parameter counts are bounded; reject duplicate/ambiguous security-sensitive parameters.
- Authentication and consent decisions are bound to the server-side authorization transaction, not merely to browser-supplied values.

Use an opaque, cryptographically random transaction handle. Store state, nonce, client, redirect URI, requested scopes, PKCE challenge, browser binding, creation/expiry, and status server-side or in authenticated protected server-side state. Never place the PKCE verifier in the authorization request.

### 6.2 CSRF and browser binding

- Bind the authorization transaction to a short-lived, unpredictable browser cookie or equivalent server-side transaction.
- Protect sign-in, consent, and decision POSTs with ASP.NET Core antiforgery protection and same-origin checks.
- Use POST for state-changing decisions.
- Do not rely on `SameSite` alone as CSRF protection.
- Use separate transaction cookies from the authenticated PALs session cookie.
- Set appropriate `HttpOnly`, `Secure` in HTTPS environments, `SameSite`, `Path`, and short expiry attributes.
- Clear transaction state after success, denial, timeout, or terminal failure.
- Support multiple simultaneous login tabs without overwriting one global state/verifier slot.

### 6.3 Authorization code

Authorization codes must be:

- Cryptographically random and high entropy.
- Short-lived (recommended initial target: 60–120 seconds; make configurable within a strict upper bound).
- Single-use, client-bound, redirect-URI-bound, subject-bound, scope-bound, and PKCE-challenge-bound.
- Stored only as a cryptographic hash (use a keyed hash/HMAC if appropriate for the storage threat model); never store or log plaintext.
- Consumed atomically in a transaction so two concurrent token requests cannot both succeed.
- Rejected after expiry, prior consumption, client mismatch, redirect mismatch, or PKCE failure.
- Invalidated when the user/account becomes ineligible before exchange, according to the defined policy.

Return generic OAuth errors that do not disclose whether a username or account exists. Do not redirect errors to an unvalidated URI.

### 6.4 Token endpoint

- Accept only supported grant types and content types.
- Authenticate the confidential Mentos client.
- Require exact `redirect_uri` equality with the original authorization request.
- Verify the PKCE verifier against the stored S256 challenge.
- Atomically consume the code before issuing tokens; ensure failures cannot leave a reusable code.
- Rate-limit token requests and bound body size.
- Return `Cache-Control: no-store` and `Pragma: no-cache`.
- Do not put tokens in URLs, query strings, fragments, browser storage, or logs.
- Do not return refresh tokens unless the selected BFF lifecycle requires them.
- If access tokens are issued, define their audience and resource-server use. Do not issue a token with an ambiguous or overly broad audience.

### 6.5 ID Token validation at Mentos

Mentos must validate the ID Token using a maintained .NET OIDC/JWT validation library and fixed, trusted PALs metadata. Do not implement cryptographic JWT validation manually.

Require validation of:

- Signature against a currently trusted key from the configured PALs JWKS.
- Explicit algorithm allowlist; reject `none`, algorithm confusion, and unexpected key types.
- Exact `iss` match to the configured issuer.
- `aud` contains the Mentos client ID.
- If `aud` has multiple values, validate `azp` according to OIDC requirements and require it to identify Mentos.
- `exp`, `iat`, and any required `nbf` with small, documented clock-skew tolerance.
- Exact `nonce` match to the transaction's stored nonce.
- Non-empty, stable `sub`.
- `auth_time` / `max_age` when a re-authentication policy is requested.
- Any required `at_hash` or `c_hash` only if the selected response type/flow requires them.

Reject unexpected issuer, audience, subject, algorithm, signature, nonce, or token lifetime. Do not accept a token merely because it decodes successfully.

The authorization response should include the RFC 9207 `iss` parameter, and Mentos must compare it to the configured issuer before sending the code to the token endpoint. This is defense in depth against authorization-server mix-up.

### 6.6 Scopes and claims

Initial scopes should be deliberately minimal:

- `openid` — required for OIDC login.
- `profile` — only claims PALs actually needs.
- `email` — only if the email claim and its verification status are useful.
- `pals.roles` — custom scope only if the BFF requires role claims; roles remain server-authoritative.

Define the exact claims per scope. At minimum, document `iss`, `sub`, `aud`, `exp`, `iat`, `nonce`, and any emitted `auth_time`, `email`, `email_verified`, `name`, or `pals.roles`.

Do not emit `email_verified=true` unless PALs has actually verified control through its documented process. Avoid sending STUID or other sensitive identifiers unless required and justified.

### 6.7 Discovery, JWKS, UserInfo, and revocation

- Publish a stable issuer identifier and discovery metadata with exact public Mentos endpoint URLs.
- Metadata must accurately advertise supported response types, grant types, scopes, signing algorithms, token endpoint authentication methods, and PKCE support.
- `jwks_uri` returns public keys only. Never publish private key parameters.
- Keep key IDs (`kid`) unique and stable for each key.
- UserInfo is optional. If implemented, require a valid access token, validate its issuer/audience/expiry, and return only claims authorized by its scopes.
- Revocation must authenticate the client and revoke the identified token safely. Return protocol-compliant responses without revealing token validity.
- Token introspection is not required unless a real resource server needs online token status.
- Do not add dynamic client registration, device flow, PAR, DPoP, or other extensions in the first implementation unless a documented threat model or client requirement justifies them.

## 7. Mentos BFF and session lifecycle

### 7.1 Login transaction

1. Browser navigates to Mentos auth-start.
2. Mentos creates a unique state, nonce, PKCE verifier/challenge, and browser-bound transaction.
3. Mentos stores the verifier and transaction server-side with a short expiry; the browser receives only an opaque transaction cookie/handle.
4. Mentos redirects to the public PALs authorization endpoint.
5. The user authenticates and makes any required consent decision.
6. The authorization server redirects to the exact registered Mentos callback with code, state, and issuer.
7. Mentos validates browser binding, state, issuer, and transaction expiry before exchanging the code.
8. Mentos performs server-to-server token exchange.
9. Mentos validates the ID Token completely.
10. Mentos asks DBSERV to create a fresh PALs web session for the validated immutable subject.
11. Mentos replaces any pre-authentication session identifier with a new opaque session key, sets the browser cookie, and clears transaction state.
12. Browser accesses protected Mentos APIs using only the cookie.

### 7.2 Session cookie

Preserve the existing `pals_host_session` behavior during migration, unless a deliberate cookie-name change is separately approved.

Production/public HTTPS requirements:

- `HttpOnly`.
- `Secure`.
- `SameSite=Lax` by default for top-level OIDC callback compatibility; evaluate `Strict` only if the flow still works correctly.
- `Path=/`.
- No `Domain` attribute.
- Prefer a `__Host-` cookie name if changing the existing name is acceptable and all prefix requirements can be met.
- Opaque, high-entropy value; no user data or claims encoded in the cookie.
- No long-lived browser persistence unless remember-me is explicitly designed.
- Rotate the session identifier after authentication and privilege changes.
- Revoke the server-side session on logout, account disablement, and security events according to policy.

Local HTTP development may require a documented development-only Secure-cookie exception. It must be impossible to enable that exception in production configuration accidentally. Prefer local HTTPS when practical.

### 7.3 CSRF and session security

- Use antiforgery tokens for state-changing cookie-authenticated API operations.
- Validate `Origin`/`Referer` where appropriate as defense in depth.
- Do not treat HttpOnly as protection against CSRF.
- Reject unknown client-supplied session IDs; never adopt a session identifier supplied by a browser before authentication.
- Regenerate the session after login and privilege elevation.
- Set idle and absolute expiry; enforce expiry server-side.
- Define concurrent-session policy and session revocation behavior.
- Ensure logout is a POST or otherwise CSRF-protected state-changing action; revoke DBSERV state and expire the cookie.
- Return no-store headers for authentication and session responses.
- Avoid putting session keys in URLs, logs, analytics, or error reports.

### 7.4 BFF token handling

The browser must never receive the ID Token, access token, refresh token, client secret, PKCE verifier, signing key, or HOST shared key.

Preferred initial model: use the ID Token only for the immediate BFF identity assertion, create the DBSERV session, then discard token material unless a documented downstream API requirement exists.

If refresh tokens are later needed, keep them server-side, encrypt at rest using a key held outside the database, rotate on every use, and detect reuse of an invalidated predecessor. Reuse must revoke the active token family and trigger re-authentication. Store hashes for lookup/revocation where compatible with the encrypted-token design; document the distinction between lookup hash and recoverable encrypted token material.

## 8. Database design and migrations

The following are proposed tables, not existing implemented schemas. Inspect current schema names and constraints before creating migrations; avoid duplicate representations of existing users, roles, or sessions.

### 8.1 `oauth_clients`

Minimum fields:

- Internal primary key.
- Unique `client_id`.
- Secret hash or protected secret reference (never plaintext in the DB if avoidable).
- Client type and authentication method.
- Display name/owner metadata.
- Active/disabled status.
- Allowed grant types and response types.
- Allowed scopes.
- Created/updated timestamps and optional secret-rotation timestamps.

Use a child table such as `oauth_client_redirect_uris` rather than a delimiter-separated URI string. Enforce exact unique redirect URI records per client. Consider separate allowed post-logout redirect URIs only if logout redirection is implemented.

### 8.2 `oauth_authorization_transactions`

This may be a short-lived transaction table or equivalent protected state store. It should contain:

- Random transaction ID.
- Client ID.
- Exact redirect URI.
- Requested scopes.
- State hash or protected state reference.
- Nonce hash or protected nonce reference.
- PKCE S256 challenge and method.
- Browser-binding hash/reference.
- Authenticated user/subject once known.
- Authentication time and consent decision/version.
- Created, expires, completed, denied, and consumed timestamps/status.

Do not store the PKCE verifier at the authorization server. The verifier belongs to Mentos' confidential BFF transaction store.

### 8.3 `oauth_authorization_codes`

Minimum fields:

- Primary key and unique code hash.
- Client foreign key.
- PALs user/subject foreign key.
- Exact redirect URI.
- PKCE challenge and method.
- Granted scopes.
- Nonce reference/claim material needed for ID Token issuance.
- Authentication time and relevant authorization context.
- Created and expiry timestamps.
- Consumed timestamp/status.
- Optional security/audit correlation ID.

Enforce single-use consumption atomically. Add indexes for code hash, client, subject, and expiry cleanup.

### 8.4 `oauth_refresh_tokens` (only if refresh is implemented)

Minimum fields:

- Primary key and unique token hash/lookup hash.
- Client and subject/user foreign keys.
- Token-family identifier.
- Parent/predecessor token reference.
- Granted scopes and audience.
- Issued, expiry, consumed, and revoked timestamps.
- Reuse-detected timestamp/reason.
- Rotation counter/version.
- Optional encrypted token value only if the server must recover and return the token; protect it with a separately managed encryption key.

Enforce one active successor per rotation step. A replayed consumed token must revoke the entire family, not just the presented row.

### 8.5 `oauth_consents` (policy-dependent)

If user consent is required, record:

- User/subject and client.
- Granted scopes.
- Consent policy/version.
- Decision timestamp.
- Revocation timestamp.
- Optional presentation/localization version.

Do not add a consent table merely to imply consent is legally or institutionally required. Determine whether PALs is acting as a first-party service with a notice/terms acceptance model or a true delegated-consent model.

### 8.6 `oauth_signing_keys` (metadata only)

Private key material should live outside the database in a protected key store/file/secret manager. A metadata table may track:

- `kid`, algorithm, key type, public JWK or public-key reference.
- Activation, publication, signing-retirement, and validation-retirement times.
- Status and creation timestamp.

Never persist private key bytes in ordinary application tables or YAML.

### 8.7 `auth_audit_events`

Add a security audit stream/table if no suitable existing audit facility exists. Record event type, actor/subject references, client, correlation ID, outcome, timestamp, and safe reason code. Do not store passwords, authorization codes, raw tokens, session keys, PKCE verifiers, or client secrets.

### 8.8 Identity and migration rules

- Add a unique immutable OIDC subject identifier if the existing PALs user primary key is not suitable.
- Add explicit email verification status/time if missing.
- Add unique constraints for subject mapping and account-link mapping.
- Add foreign keys and indexes for client, user, code hash, token family, expiry, and audit queries.
- Use EF Core migrations or the repository's established SQL migration convention consistently; do not mix untracked schema edits with migration history.
- Apply migrations using a separate DBA/migration account.
- Keep `dbpals` CRUD-only.
- Apply to a disposable local development database first.
- Make migrations repeatable and review generated SQL before application.
- Define cleanup/retention jobs for expired codes, expired transactions, old revoked tokens, and audit records. Cleanup must not delete evidence needed for replay detection or incident review prematurely.

## 9. Signing-key management

### 9.1 Key generation and storage

- Use an approved asymmetric signing algorithm supported by the selected .NET validation libraries (prefer a modern, widely supported algorithm such as RS256 or ES256; choose one and document interoperability).
- Generate keys using a cryptographically secure platform/library implementation.
- Store private keys outside the repository and outside ordinary YAML configuration.
- Restrict filesystem/secret-store permissions to the DBSERV service identity.
- Never log, print, commit, archive, or transmit private key material.
- Configure key location by environment variable or a non-secret path in YAML; the key itself remains in protected storage.
- Fail closed if no active signing key exists. Do not silently generate a new key on every startup.

### 9.2 Rotation lifecycle

Define and implement these states:

1. **Prepared:** new key generated and securely stored; not yet used.
2. **Published:** public JWK is available before the key signs tokens, allowing validators to cache it.
3. **Active:** new key signs newly issued ID Tokens.
4. **Validation-only:** old public key remains in JWKS so unexpired tokens signed by it can still validate.
5. **Retired:** key is removed from JWKS only after the maximum token lifetime, clock-skew allowance, and documented cache window have elapsed.
6. **Compromised/revoked:** immediately stop signing with the key, remove or mark its public key unusable according to the incident plan, invalidate affected sessions/tokens as appropriate, and require re-authentication.

- Never reuse a `kid`.
- Publish overlapping old/new public keys during normal rotation.
- Keep ID Token lifetimes short and bounded.
- Define JWKS cache headers and validator refresh behavior for unknown `kid`.
- Avoid unbounded refresh loops when a token contains an unknown key ID.
- Test rotation with tokens issued before, during, and after the change.
- Back up keys securely and test recovery without exposing key contents.
- Document that a signing-key compromise may require session invalidation, not merely key replacement.

## 10. Local and later public callback environments

### 10.1 Local-only development

Use fixed, explicitly registered local origins, for example:

- Mentos origin: `http://127.0.0.1:8000` (or the selected local HTTPS origin).
- Callback: one exact path on that origin, such as `/oauth/callback`.
- Issuer and discovery URLs: stable local values configured consistently in Mentos and DBSERV.

Do not mix `localhost` and `127.0.0.1`; browsers treat them as different hosts for cookies/origin purposes. Do not accept arbitrary ports, host headers, forwarded hosts, or callback URLs.

For local HTTP:

- Clearly isolate development-only cookie behavior.
- Do not weaken redirect URI validation.
- Do not reuse local client secrets or signing keys in public testing.
- Use a disposable database and test accounts.
- Bind both services to loopback unless a specific local network test requires otherwise.

### 10.2 Later Cloudflare Tunnel/public testing

This is a later phase and is not part of the current execution.

Before enabling it:

- Choose one canonical HTTPS public Mentos origin and register the exact callback URI.
- Keep DBSERV bound to loopback/private service networking; do not publish its port or route it through the tunnel.
- Tunnel only the Mentos public listener and only after reviewing exposed routes.
- Configure ASP.NET forwarded-header handling for the trusted Cloudflare proxy path only. Do not blindly trust arbitrary `X-Forwarded-Host`, `X-Forwarded-Proto`, or client IP headers.
- Ensure generated redirects, issuer, discovery metadata, cookie security, and callback validation use the canonical HTTPS origin, not an untrusted Host header.
- Keep separate public-test client IDs, secrets, signing keys, and database.
- Require HTTPS and Secure cookies; configure HSTS only when the public origin is stable and HTTPS is enforced.
- Rate-limit authentication routes and monitor safe security events.
- Do not use the tunnel to expose DBSERV, MariaDB, Proxmox, or management endpoints.
- Do not start, configure, or operate the tunnel during this local implementation unless explicitly requested.

## 11. ASP.NET Core / .NET 10 implementation guidance

- Use ASP.NET Core Identity for password hashing, lockout, account status, and credential lifecycle; do not implement custom password hashing.
- Use supported ASP.NET Core authentication/authorization middleware and maintained OIDC/JWT libraries.
- Use EF Core entities/configurations and migrations consistent with the repository.
- Use `TimeProvider` or an injectable clock for expiry logic and deterministic tests.
- Use cryptographically secure random generation for state, nonce, transaction IDs, authorization codes, and session keys.
- Use constant-time comparison for secret/hash comparisons where applicable.
- Use database transactions and concurrency controls for code consumption and refresh-token rotation.
- Keep protocol handlers separate from controllers and database repositories.
- Validate configuration at startup; reject malformed issuer URLs, missing secrets, invalid redirect URIs, weak key configuration, or unsupported algorithms.
- Do not log request bodies on authentication/token endpoints.
- Redact `Authorization`, cookies, `code`, `state`, `nonce`, `code_verifier`, `client_secret`, and token values from logs and traces.
- Add health checks that do not disclose secrets or internal topology.
- Use generic public authentication errors while retaining safe internal correlation IDs.

## 12. Implementation phases and gates

### Phase 0 — Decisions, database, and key prerequisites

Deliverables:

- Approve the topology and public/private route map in Section 4.
- Decide and document how student status and school-email ownership are actually verified.
- Decide the first-party account credential/recovery policy.
- Decide whether consent is required and what the consent notice covers.
- Confirm MariaDB connectivity and inspect applied schema history.
- Apply existing base schemas, v5 Identity, and v7 sessions only where missing.
- Create a separate DBA/migration account or use the approved DBA account.
- Define local issuer, callback origin, and exact redirect URI.
- Define signing algorithm, key storage path, key permissions, and rotation schedule.
- Define token lifetimes, cookie lifetimes, idle/absolute session expiry, and concurrent-session policy.
- Define the TOTP enforcement policy, recovery process, authenticator setup
  workflow, and the point at which `MFA_REQUIRE_PRIVILEGED` is enabled.
- Seed development accounts interactively; never place passwords in SQL/YAML/source.

**Gate:** disposable local database has the required Identity, MFA replay, and
session schemas; seeded Identity login and TOTP challenge work; student
verification policy is written; signing key can be loaded securely;
`web_sessions` can create and revoke sessions.

### Phase 1 — DBSERV authorization-server core

Deliverables:

- Client registry and exact redirect URI validation.
- Authorization transaction and authorization-code persistence.
- Identity authentication/account eligibility checks.
- Server-side authorization/consent decision.
- PKCE S256 verification.
- Atomic one-time code consumption.
- ID Token signing and public JWKS.
- Discovery metadata generation.
- Optional refresh-token family/rotation implementation only if approved.
- Internal endpoints protected by HOST service authentication.
- Audit events for security-sensitive actions.

**Gate:** unit/integration tests cover client rejection, redirect mismatch, PKCE mismatch, expired/replayed code, inactive user, invalid scopes, and role-policy rejection. No public DBSERV route is required.

### Phase 2 — Mentos public façade and BFF transaction

Deliverables:

- Auth-start route that creates independent per-login state, nonce, PKCE verifier, and browser binding.
- Server-side storage for verifier and transaction with short expiry.
- Public authorization façade and PALs sign-in/consent UI.
- Callback route with state, issuer, browser-binding, and expiry validation.
- Server-to-server token exchange.
- Complete ID Token validation.
- DBSERV session creation for the validated subject.
- Session rotation and cookie issuance.
- CSRF protection for sign-in/consent and state-changing BFF routes.
- Safe error handling and transaction cleanup.

**Gate:** local browser can authenticate through Mentos, reach the existing PALs frontend, call `/api/v1/session`, access protected API data, and log out with DBSERV session revocation. Browser storage and network inspection show no OAuth tokens or service secrets.

### Phase 3 — Account policy, linking, and roles

Deliverables:

- Enforce student account verification policy.
- Preserve six-digit minimum STUID syntax where the policy requires it.
- Use immutable PALs subject, not email or lastname, as identity key.
- Implement safe account linking/recovery.
- Keep admin/developer provisioning separate from student self-service.
- Implement server-side tutor/tutee role grants and audit changes.
- Define how account disablement affects existing sessions and tokens.

**Gate:** tests cover unverified school address, malformed email, five-digit STUID, duplicate subject, duplicate email, ambiguous account mapping, linking collision, account recovery, disabled account, role escalation, and privileged-account protection.

### Phase 4 — Frontend transition

Deliverables:

- Replace email-format-only login action with Mentos auth-start.
- Remove UI implying Google/Entra login.
- Preserve admin login compatibility only until a separately approved migration decision.
- Remove/disable legacy `localStorage` authentication/session fallbacks in the active frontend.
- Remove demo credential fallback and ensure initial placeholder content cannot be mistaken for authenticated live data.
- Add loading, cancellation, expired transaction, denied authorization, and retry states.
- Keep `mentos/public/` styles unchanged unless separately authorized.
- Ensure profile and role UI never submits trusted role grants.
- Ensure browser scripts never receive tokens, verifier, client secret, or HOST key.

**Gate:** fresh browser profile completes the intended flow and no auth path silently falls back to demo/localStorage state.

### Phase 5 — Security, reliability, and integration testing

Run the test matrix in Section 13 against a disposable local database.

**Gate:** all required protocol, session, identity, abuse, and integration tests pass. No APMAIN deployment, remote service restart, or tunnel operation is part of acceptance.

### Phase 6 — Documentation and contract update

Update `server/API.md`:

- Public Mentos route table and private DBSERV internal route table.
- Authorization, callback, token, UserInfo (if implemented), JWKS, discovery, and revocation behavior.
- Issuer, client registration, redirect URI, PKCE, state, nonce, and error rules.
- Scopes and exact claims.
- Session creation, cookie policy, CSRF, expiry, and logout behavior.
- Schema/migration prerequisites and migration-account requirement.
- Account verification, linking, role policy, and disabled-account behavior.
- Current compatibility status of old login routes.
- Explicit deferred status of `/connect` and recommendation routes.

Update `server/README.md`:

- MariaDB prerequisites and disposable local DB setup.
- DBA/migration account instructions and schema application order.
- Runtime `dbpals` CRUD-only policy.
- Signing-key generation, secure placement, permissions, rotation, and recovery.
- YAML/environment configuration names and precedence.
- Local issuer/callback origins and local cookie behavior.
- Separate future Cloudflare Tunnel/public-test setup.
- Interactive account seeding and role testing.
- Local run commands for DBSERV and Mentos.
- OAuth test commands and expected health/session checks.
- Secret-handling and log-redaction rules.

Also update `bindoc/maindoc/notes/changes-act.md` when work is actually completed. Review overlapping documentation for stale Node-era authentication contracts. Do not mark phases complete before evidence exists.

**Gate:** docs match the implemented route map, schemas, settings, and tests; a new developer can reproduce local setup without undocumented secrets or paths.

## 13. Required test matrix

### Protocol and client validation

- Valid Authorization Code + PKCE S256 flow.
- Missing/invalid client.
- Disabled client.
- Wrong client authentication.
- Unknown, wildcard, prefix, or mismatched redirect URI.
- Open redirect and malicious Host-header attempts.
- Unsupported response type, grant type, or response mode.
- Missing `openid`, unknown scope, and unauthorized scope.
- Missing/invalid state, nonce, challenge, or challenge method.
- PKCE verifier mismatch.
- Authorization-server `iss` mismatch.
- Duplicate security-sensitive parameters.
- Oversized request and malformed content type.
- Authorization-code expiry and concurrent replay.
- Code bound to another client, redirect URI, subject, or PKCE challenge.
- Token endpoint rate limiting and brute-force resistance.

### ID Token and key validation

- Invalid signature.
- Unknown `kid`.
- Disallowed algorithm and `none`.
- Wrong issuer.
- Wrong audience.
- Multiple audiences with missing/wrong `azp`.
- Expired token, future `iat`, invalid `nbf`, and clock-skew boundary.
- Nonce mismatch/replay.
- Missing/empty subject.
- Key rotation with old unexpired tokens.
- Unknown-key refresh behavior and JWKS caching.
- Missing signing key and unreadable key file.
- Compromised-key emergency procedure.

### Identity, account linking, and roles

- Unverified school address.
- Malformed student email.
- Five-digit STUID.
- Duplicate email and duplicate subject.
- Account linking by matching email alone is rejected.
- Valid authenticated linking.
- Linking collision and concurrent linking.
- Disabled/inactive account.
- Account recovery and unlink policy.
- Student cannot grant self admin/developer/tutor privileges.
- Role removal takes effect according to session/token policy.
- Duplicate or conflicting role grants.
- Generic public errors do not reveal account existence.

### Session and browser security

- Session ID rotates after login and privilege elevation.
- Attacker-supplied/fixed session ID is rejected.
- Cookie flags and production HTTPS behavior.
- CSRF rejection for state-changing requests.
- Cross-origin callback and state mismatch.
- Multiple simultaneous login tabs.
- Expired/abandoned authorization transaction cleanup.
- Logout revokes DBSERV session and expires cookie.
- Revoked/expired session cannot access protected routes.
- Idle and absolute expiry.
- Concurrent-session policy.
- Account disablement and security-event revocation.
- No tokens, passwords, codes, verifier, cookie values, or service keys in browser storage, URLs, logs, or analytics.

### Refresh tokens (only if implemented)

- Rotation on every successful use.
- Reuse of consumed predecessor revokes the token family.
- Expired/revoked token rejected.
- Client and subject binding.
- Concurrent refresh race.
- Logout/revocation behavior.
- Encryption-key loss/recovery behavior if recoverable tokens are stored.

### Infrastructure and integration

- Database outage during authorize, token exchange, session creation, and logout.
- Missing/malformed configuration.
- Missing signing key.
- Migration applied with DBA account; runtime `dbpals` still cannot perform DDL.
- Startup/health behavior with DB unavailable.
- Local callback with exact configured origin.
- Untrusted forwarded headers rejected.
- HOST shared-key failure.
- No generic DBSERV route exposed through Mentos.
- No browser-to-DBSERV traffic.
- No private key, client secret, DB password, password hash, or token leakage in logs.
- Backup/restore and signing-key recovery procedure.
- Full local browser flow through Mentos, DBSERV, MariaDB, protected API, and logout.

## 14. Compatibility and deferred work

During migration:

- Keep the current `POST /api/v1/auth/login` only behind an explicit development compatibility flag, if needed for controlled transition tests.
- It must not be represented as secure school-account authentication.
- Remove or disable it after OAuth acceptance; do not leave it as an unnoticed fallback.
- Decide separately whether `/api/v1/auth/admin/login` remains a direct Identity login or is migrated into the PALs authorization flow. Do not silently merge the two authentication policies.
- Preserve current session endpoints only where their behavior matches the new session lifecycle.
- `/connect`, matching algorithms, recommendations, and recommendation endpoints remain deferred and may continue returning HTTP 501.
- No unrelated frontend redesign or style changes are included.

## 15. Completion criteria

OAuth/OIDC is accepted for **controlled local development testing only** when:

1. The architecture boundary is implemented: browser → Mentos → private DBSERV.
2. Student account verification and account recovery policy are implemented, not merely email syntax checks.
3. Identity and OAuth schemas are applied to a disposable development database.
4. The runtime DB account remains CRUD-only.
5. The Mentos confidential client, redirect allowlist, scopes, and secrets are configured securely.
6. Authorization Code + PKCE S256 succeeds end-to-end.
7. State, nonce, issuer, signature, audience, authorized-party, expiry, and subject validation pass.
8. The verified immutable subject maps to exactly one PALs account.
9. Roles are server-derived and cannot be escalated through client input or account linking.
10. Mentos creates and rotates the existing DBSERV-backed browser session.
11. Logout and account/security revocation invalidate the server-side session.
12. Required protocol, replay, session, CSRF, key rotation, outage, and abuse tests pass.
13. Browser inspection confirms no tokens or service secrets are exposed.
14. `server/API.md`, `server/README.md`, and the change log accurately reflect completed work.
15. No remote deployment, APMAIN restart, or Cloudflare Tunnel operation has occurred without explicit authorization.

## 16. Recommended implementation order

1. Resolve the student identity-verification and recovery policy. This is a product/security blocker, not a coding detail.
2. Approve the Mentos façade / private DBSERV authorization-server topology.
3. Inspect current schema and migration state; prepare a disposable local database.
4. Define exact issuer, client, redirect URI, claims, token lifetimes, cookie policy, and key lifecycle.
5. Implement and test DBSERV protocol/persistence primitives.
6. Implement Mentos transaction handling, callback, token exchange, and session rotation.
7. Integrate the frontend without changing preserved styles.
8. Complete the test matrix and documentation review.

Implementation may begin on isolated protocol/unit-test components before MariaDB is available. End-to-end acceptance must wait until the required schemas and local database are active.

## 17. Standards and security references

Use the current versions of these primary specifications and guidance during implementation:

- OAuth 2.0 Authorization Framework — RFC 6749: https://www.rfc-editor.org/rfc/rfc6749
- Proof Key for Code Exchange (PKCE) — RFC 7636: https://www.rfc-editor.org/rfc/rfc7636
- OpenID Connect Core 1.0: https://openid.net/specs/openid-connect-core-1_0.html
- OAuth 2.0 Security Best Current Practice — RFC 9700: https://www.rfc-editor.org/rfc/rfc9700
- OAuth 2.0 Authorization Server Metadata — RFC 8414: https://www.rfc-editor.org/rfc/rfc8414
- OAuth 2.0 Authorization Server Issuer Identification — RFC 9207: https://www.rfc-editor.org/rfc/rfc9207
- OAuth 2.0 Token Revocation — RFC 7009: https://www.rfc-editor.org/rfc/rfc7009
- TOTP: Time-Based One-Time Password Algorithm — RFC 6238: https://www.rfc-editor.org/info/rfc6238/
- OWASP Session Management Cheat Sheet: https://cheatsheetseries.owasp.org/cheatsheets/Session_Management_Cheat_Sheet.html
- OWASP Multifactor Authentication Cheat Sheet: https://cheatsheetseries.owasp.org/cheatsheets/Multifactor_Authentication_Cheat_Sheet.html
- OWASP Cross-Site Request Forgery Prevention Cheat Sheet: https://cheatsheetseries.owasp.org/cheatsheets/Cross-Site_Request_Forgery_Prevention_Cheat_Sheet.html

The standards define protocol requirements; PALs must still make and document its own account verification, consent, role, retention, and recovery policies.
