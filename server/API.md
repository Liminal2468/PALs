# PALs active REST API contract

> [!NOTE] This document describes the active ASP.NET Core implementation as of 2026-11-18. It replaces the retired Node/JWT/Google-provider draft.

The contract is divided into two applications:

- `mentos/` is the browser-facing HOST and exposes /api/v1.
- `server/` is DBSERV and exposes host-only data routes under /internal/v1.

Browsers must call Mentos, not DBSERV. DBSERV also exposes service liveness
routes such as /db/health, /acc/health, and /connect/health; those health
routes do not represent implemented feature APIs.

## Request trust and sessions

Mentos authenticates the browser with the HttpOnly pals_host_session cookie.
The cookie contains an opaque DBSERV session key. DBSERV persists these keys in
web_sessions; the current implementation is not a HOST-memory session.

Mentos-to-DBSERV requests include:

```text
X-Pals-Host-Key: <private shared key>
X-Pals-Session: <opaque session key>   # required for user-scoped operations
```

The shared key is server-to-server configuration and must never be delivered to
browser JavaScript. Missing or invalid trust/session values return 401 or 403
as appropriate. DBSERV failures are returned by Mentos as a sanitized 502.

Common error shape:

```json
{ "error": { "code": "error_code", "message": "Human-readable message." } }
```

## Mentos HOST — /api/v1

These are the public application routes. Unless noted otherwise, protected
routes require the Mentos session cookie and a reachable DBSERV database.

### Health and authentication

| Method | Path | Status and behavior |
|---|---|---|
| GET | `/api/v1/health` | Implemented liveness response; no database query |
| GET | `/api/v1/frontend/status` | Implemented HOST status response |
| GET | `/api/v1/auth/options` | Returns local-school-account; terms are required |
| POST | `/api/v1/auth/login` | Validates LASTNAME.STUID@cubao.sti.edu.ph, requires accepted terms, provisions/finds the PALs account, and creates a DBSERV session |
| POST | /api/v1/auth/admin/login | Verifies an Identity-backed admin/developer password, then verifies `mfaCode` when TOTP is enabled, and creates a DBSERV session |
| GET | /api/v1/session | Returns the current persisted DBSERV session account |
| POST | /api/v1/auth/logout | Revokes the current DBSERV session and clears the cookie |
| DELETE | /api/v1/session | Logout alias; revokes the current DBSERV session |

The current student login is an email-and-terms provisioning facade. It is not
yet a password login, OAuth authorization server, or provider callback flow.
The local student identifier must contain at least six digits, enforced by the
HOST and DBSERV policy.

The admin/developer compatibility login uses PALs-owned ASP.NET Identity
credentials. If TOTP is enabled for the account, the same request must include
`mfaCode`; the response is `401 mfa_required` when the second factor is
missing. Google Authenticator is supported as a TOTP client, but Google Sign-In
is not used as an external identity provider.

Example privileged login body after TOTP enrollment:

```json
{
  "email": "reime@pals.me",
  "password": "entered interactively",
  "mfaCode": "123456"
}
```

Example student login:

```json
{
  "email": "SANTOS.123456@cubao.sti.edu.ph",
  "termsAccepted": true
}
```

### Accounts, pages, and learning data

| Method | Path | Status and behavior |
|---|---|---|
| GET | /api/v1/me | Returns the authenticated account/profile |
| PATCH | /api/v1/me | Updates display name, grade, section, bio, tutor/tutee selections, and subject preferences; profile creation requires grade and section |
| GET | /api/v1/users | Implemented; restricted to admin and developer roles; returns safe account summaries and roles |
| PATCH | /api/v1/users/{id} | Implemented; changes account status or tutor/tutee roles and records an account audit; privileged roles cannot be changed here |
| GET | /api/v1/pages | Implemented; returns database-backed safe page metadata filtered by account role |
| PATCH | /api/v1/pages/{id} | Implemented; administrator/developer page metadata update; file routes are immutable and allowlisted |
| GET | /api/v1/tutors | Returns active tutors and subject summaries |
| GET | /api/v1/tutees | Returns open learning requests/tutee entries |
| GET | /api/v1/learning-requests | Lists the current user's learning requests |
| POST | /api/v1/learning-requests | Creates a learning request |
| GET | /api/v1/sessions | Lists sessions visible to the current user |
| POST | /api/v1/sessions | Creates a one-to-one session record |
| PATCH | /api/v1/sessions/{id} | Updates an accessible session through validated status transitions and records a session audit |
| GET | /api/v1/dashboard | Returns live account dashboard aggregates |

### Calendar schedule

| Method | Path | Status and behavior |
|---|---|---|
| GET | /api/v1/schedule | Lists active events; optional from and to query values |
| POST | /api/v1/schedule | Creates an event with onDate, afterDate, eventName, description, and isActive |
| PATCH | /api/v1/schedule/{id} | Modifies an event; restricted to admin and developer |
| DELETE | /api/v1/schedule/{id} | Soft-deactivates an event; restricted to admin and developer |

Accepted date forms include MM-DD-YY:HH-mm, MM-DD-YY:HH-mm-, and ISO-like
UTC date-time forms. The database stores on_date and after_date as UTC
DATETIME(3) values and requires the end not to precede the start.

### Resources, feedback, support, and analytics

| Method | Path | Status and behavior |
|---|---|---|
| GET | /api/v1/resources | Lists visible resources and the current user's own resources |
| POST | /api/v1/resources | Creates a resource in pending moderation state |
| PATCH | /api/v1/resources/{id} | Updates an owned resource, returns it to pending moderation, and records an audit |
| DELETE | /api/v1/resources/{id} | Soft-removes an owned resource and records an audit |
| PATCH | /api/v1/resources/{id}/moderation | Administrator/developer moderation update; records an approval, flag, or removal audit |
| GET | /api/v1/feedback | Lists visible feedback posts and vote counts |
| POST | /api/v1/feedback | Creates a feedback post |
| POST | /api/v1/feedback/{id}/votes | Records an idempotent vote |
| POST | /api/v1/support | Creates a support ticket for the current account |
| POST | /api/v1/analytics/events | Records an allowlisted event only when active research consent exists; payload size and duration are bounded |
| POST | /api/v1/analytics/consent | Records or restores analytics consent |
| GET | /api/v1/analytics/summary | Returns the current user's consent-scoped aggregate summary |

The active frontend connects live account/profile hydration, dashboard
statistics, tutor/tutee lists, learning requests, sessions, resource listing
and creation, feedback voting, support, analytics consent, and consent-aware
page-view event emission. Resource update/delete controls and a dedicated
administrator management screen are not yet present in the preserved UI.

### Deferred authentication and matching routes

| Route | Status |
|---|---|
| Provider start/callback | Not implemented; no Google, Entra, or PALs OAuth callback route is active |
| Signup route | Not implemented; account provisioning currently occurs through the local school-email login façade |
| GET /api/v1/rec-peer-tutor | HTTP 501 connect_not_implemented |
| GET /api/v1/recommended-peer-tutors | HTTP 501 connect_not_implemented compatibility alias |
| GET /api/v1/tutors/recommended | HTTP 501 connect_not_implemented compatibility alias |

The /connect matching algorithm and recommendation persistence remain
deliberately deferred pending the approved matching design.

## DBSERV internal API — /internal/v1

These routes are not browser-facing. Every request requires a valid
X-Pals-Host-Key; user-scoped routes also require X-Pals-Session.

| Method | Path | Status and behavior |
|---|---|---|
| POST | /internal/v1/accounts/provision | Provisions a recognized school account |
| POST | /internal/v1/auth/login | Validates school-account format, provisions/finds the account, and creates a session |
| POST | /internal/v1/auth/admin/login | Verifies the Identity password and privileged role, verifies `mfaCode` when enabled, then creates a session |
| GET | /internal/v1/auth/session | Resolves the X-Pals-Session account |
| DELETE | /internal/v1/auth/session | Revokes the supplied session |
| GET/PATCH | /internal/v1/me | Reads or updates the current account/profile |
| GET | /internal/v1/dashboard | Returns account dashboard aggregates |
| GET | /internal/v1/users | Lists accounts for admin/developer users |
| PATCH | /internal/v1/users/{id} | Changes account status or tutor/tutee roles and records an account audit |
| GET | /internal/v1/pages | Lists safe database-backed page metadata; non-privileged users receive enabled pages allowed for their role |
| PATCH | /internal/v1/pages/{id} | Changes allowlisted page metadata for admin/developer users and records an audit |
| GET | /internal/v1/tutors | Lists active tutors |
| GET | /internal/v1/tutees | Lists open tutee requests |
| GET/POST | /internal/v1/learning-requests | Reads or creates learning requests |
| GET/POST/PATCH | /internal/v1/sessions | Reads, creates, or updates sessions |
| GET/POST/PATCH/DELETE | /internal/v1/schedule | Reads, creates, updates, or deactivates calendar events |
| GET/POST/PATCH/DELETE | /internal/v1/resources | Resource listing and ownership-checked mutations with audit records |
| PATCH | /internal/v1/resources/{id}/moderation | Admin/developer moderation mutation with audit record |
| GET/POST | /internal/v1/feedback | Feedback listing and creation |
| POST | /internal/v1/feedback/{id}/votes | Records a feedback vote |
| POST | /internal/v1/support | Creates a support ticket |
| POST | /internal/v1/analytics/events | Consent-gated event recording |
| POST | /internal/v1/analytics/consent | Records analytics consent |
| GET | /internal/v1/analytics/summary | Returns a consent-scoped summary |

## DBSERV service health routes

The active service controller provides unauthenticated liveness responses for
the known service names:

```text
GET /db/health
GET /acc/health
GET /sched/health
GET /fb/health
GET /adata/health
GET /connect/health
GET /chat/health
```

/connect/health means only that the service host is reachable. It does not
mean tutor matching or recommendations are implemented.

## Developer interface routes

The DBSERV developer interface has a separate Identity-cookie flow:

| Method | Path | Purpose |
|---|---|---|
| POST | /dev/login | Identity-backed developer login |
| POST | /dev/mfa/verify | Completes a developer login when ASP.NET Identity requires TOTP |
| GET | /dev/session | Reads the developer Identity session |
| POST | /dev/logout | Signs out the developer Identity cookie |
| GET | /dev/profile | Reads the authenticated developer profile |
| GET | /dev/mfa | Reads the current account's TOTP status |
| POST | /dev/mfa/setup | Creates an `otpauth://` setup URI and manual key for the authenticated account |
| POST | /dev/mfa/confirm | Verifies the setup code, enables TOTP, and returns one-time recovery codes |
| POST | /dev/mfa/disable | Re-authenticates with TOTP and disables the authenticator |
| GET | /dev/system/matching-algorithms | Returns the currently empty algorithm-control list |

## Database schemas and prerequisites

Active schema files are under database/schema/:

| Schema | Purpose |
|---|---|
| schema_v1.sql through schema_v4_developer_credentials.sql | Base PALs accounts, sessions, auth, and developer structures |
| schema_v5_identity.sql | ASP.NET Identity roles/users and migration history |
| schema_v6_frontend_features.sql | Resources, audits, feedback, votes, and support tickets |
| schema_v7_sessions_schedule.sql | Persistent HOST sessions and calendar events |
| schema_v8_mfa.sql | Short-lived hashed TOTP replay protection |
| schema_v9_profile_management.sql | Profile bio/section/preferences, account and session audits, and safe page metadata |

These scripts are not applied automatically. The runtime dbpals account should
retain application CRUD privileges; use a separate migration administrator to
apply schema changes. Identity login requires v5 and a seeded Identity account;
the persisted HOST session and schedule routes require v7; the complete
profile, management, audit, and page metadata routes require v9.

The v9 migration stores page metadata only. It does not permit arbitrary file
paths, HTML uploads, source-file reads, or public access to server internals.

Developer/admin accounts are seeded interactively from server/:

```bash
dotnet run --project Palsys.Server.csproj -- --seed-developer reime@pals.me developer
dotnet run --project Palsys.Server.csproj -- --seed-developer admin@pals.me admin
```

Passwords are entered interactively and stored as Identity hashes. They must
not be placed in SQL, YAML, .env, source code, or an archive.

## ISO/IEC 25010:2023 quality review — Mentos

This is an internal engineering assessment of the current Mentos HOST and
frontend. It is not an ISO certification or an independent conformance audit.
ISO/IEC 25010:2023 defines a product-quality model with nine characteristics
and related subcharacteristics for specifying and evaluating software quality.
Reference: https://www.iso.org/standard/78176.html

Scores are accomplishment ratings from 0.0 to 10.0, based on the current
source, build checks, route review, and documented local smoke tests. They do
not represent production measurements where no benchmark or end-to-end test
exists.

| Characteristic | Score | Evidence and remaining gap |
|---|---:|---|
| Functional suitability | 5.0 | Core account, learning, resource, feedback, analytics, and schedule flows exist; OAuth/signup, matching, and several UI actions remain incomplete |
| Performance efficiency | 4.5 | Async HTTP/database code and client timeout exist; no load, latency, capacity, or query-performance results are recorded |
| Compatibility | 6.0 | HOST/DBSERV separation, HTTP/JSON routes, YAML configuration, and Linux publishing are established; cross-platform and proxy matrices are incomplete |
| Interaction capability | 5.0 | Existing responsive interface, forms, status feedback, and some ARIA support exist; demo markup, legacy localStorage behavior, and accessibility testing remain |
| Reliability | 4.5 | Health routes, service supervision, persistent sessions, and sanitized DBSERV failures exist; schema deployment, recovery, failover, and soak testing remain |
| Security | 5.0 | HttpOnly sessions, shared-key trust, Identity hashing, role checks, ownership checks, and secret exclusions exist; student login is not yet password/OAuth-based and no formal security audit is complete |
| Maintainability | 6.5 | Separated HOST/DBSERV projects, schema versions, documentation, YAML configuration, and buildable .NET projects exist; legacy frontend fallback code and limited active-project automated tests remain |
| Flexibility | 6.0 | Relocatable publishing, environment/YAML configuration, and a façade allow changes; provider integration, matching, and some account/profile capabilities are not yet extensible implementations |
| Safety | 3.5 | Consent gating, ownership checks, support tickets, and moderation fields exist; education privacy review, content-safety workflow, incident response, and policy validation are incomplete |
| Overall average | 5.1 | Prototype/integration stage; not ready to claim production education-platform quality |

The largest quality risks are the incomplete authentication model, unfinished
matching flow, legacy placeholder/localStorage paths in the frontend, missing
performance/security evidence, and unapplied production database migrations.
