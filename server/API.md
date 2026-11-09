# PALs REST API contract (v1 draft)

This document defines the first backend-facing contract based on the PALs
research proposal. It is a draft: business endpoints currently return HTTP
`501` until their handlers, authorization rules, and validation are
implemented. `GET /<prefix>/health` is available on each service.

All paths below are relative to the mounted prefix. JSON request/response
examples omit the common error envelope unless noted:

```json
{ "error": { "code": "bad_request", "message": "..." } }
```

The server applies an in-memory limit of 120 requests per client IP per
15-minute window to non-`/db` APIs. Under `/db`, the IP limit counts only
unsuccessful responses (60 per IP per 15 minutes); authenticated requests also
have a separate 3,000-per-15-minute quota keyed by the verified JWT `kid`.
Behind a local HTTP reverse proxy, the server trusts `X-Forwarded-For` only
from loopback peers; the proxy must overwrite or sanitize that header.
Global errors use `unauthorized` (401), `forbidden` (403), `not_found` (404),
and `payload_too_large` (413) for those statuses. JSON request bodies are
limited to 256 KiB.

IDs are opaque database IDs in these draft examples. A production public API
may use different identifiers. Authenticated endpoints require a validated
session/token once the login flow is decided. The generic `/db` API is an
internal service-to-service data-access layer, not a browser-facing API.
Feature handlers must enforce ownership, role, consent, and resource-level
authorization; a valid JWT at `/db` is not a substitute for those checks.

## Roles

The application has `tutor`, `tutee`, and `admin` roles. These are grants, not
mutually exclusive account types: a student can have both tutor and tutee
roles, and can request help in one subject while tutoring another. Admin is a
separately granted role and must not be self-assignable through profile or
registration endpoints.

## Accounts and profiles — `/acc`

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/me` | Get the authenticated user's profile and roles |
| `PATCH` | `/me` | Update allowed profile fields such as grade, strand, timezone, and learning preferences |
| `POST` | `/me/roles` | Opt into the `tutor` or `tutee` role |
| `GET` | `/subjects` | List active subjects and topics |
| `PUT` | `/me/tutor-subjects` | Replace the authenticated tutor's subject/proficiency declarations |
| `GET` | `/me/tutor-subjects` | List the authenticated tutor's subject declarations |
| `PUT` | `/admin/users/:userId/roles/:roleKey` | Grant a role as an authorized admin |
| `DELETE` | `/admin/users/:userId/roles/:roleKey` | Revoke a role as an authorized admin |

`PATCH /me` must not accept role grants, account status, external identity,
consent, or admin fields. Tutor subject declarations do not imply verified
proficiency; verification is an administrative workflow to be defined.
Self-service role opt-in is limited to tutor/tutee; only an authorized admin
may grant or revoke roles through the admin routes. Admin actions must be
audited and cannot remove the last active admin without a deliberate recovery
procedure.

## Matching and study requests — `/connect`

The proposal requires matching by subject/need, tutor proficiency, schedule,
and learner characteristics, while favoring a useful skill difference. The
final algorithm and weights remain to be researched; the current score formula
is a provisional prototype, not a stable public contract.

The current matching implementation is a **stateless, provisional baseline**.
It ranks only the candidates supplied in the request body; it does not fetch
student records, write recommendations, authenticate the student identities,
reserve tutor capacity, or perform group optimization. A trusted backend caller
must load authorized profiles and enforce the student's identity before using
these routes. Do not expose them directly to browsers until authentication,
ownership checks, and server-side candidate loading are integrated.

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/match` | Rank tutor candidates for a tutee learning request |
| `POST` | `/recommendations` | Rank tutor candidates for a tutee learning request |
| `POST` | `/tutorreco` | Rank open tutee requests for a tutor |
| `POST` | `/requests` | Create a tutee learning request |
| `GET` | `/requests` | List the authenticated user's requests |
| `GET` | `/requests/:requestId` | Read an owned request |
| `PATCH` | `/requests/:requestId` | Update or close an owned open request |
| `POST` | `/requests/:requestId/matches` | Generate recommendations for an eligible request |
| `GET` | `/requests/:requestId/matches` | List recommendations for an owned request |
| `POST` | `/matches/:matchId/accept` | Accept a recommendation, subject to availability |
| `POST` | `/matches/:matchId/decline` | Decline a recommendation |

Create-request body:

```json
{
  "subject_id": 12,
  "topic_id": 4,
  "current_proficiency": 2,
  "requested_help": "Review the topic and practice examples",
  "preferred_format": "one_to_one"
}
```

### Provisional recommendation input

`POST /connect/match` (also available as `/connect/recommendations`) accepts
`{ "request": ..., "tutors": [...],
"limit": 10 }`. The tutor-facing reverse route accepts
`{ "tutor": ..., "requests": [...], "limit": 10 }`. Both use the same
pair-scoring function. Each request includes its status, tutee role, subject,
current proficiency (prototype scale 1–5), preferred format, UTC availability
intervals, and optional preference tags. Each tutor includes active status,
tutor role, subject/topic, proficiency on the same prototype scale, supported
formats, UTC availability intervals, and optional preference tags. A tutor
may include `topic_ids` to declare supported topics under a subject.

The current `weighted-baseline-v1-draft` requires an active tutor, matching
subject/topic, tutor proficiency above the tutee level, a compatible format,
and at least one overlapping availability interval. Its provisional weights
are 0.35 proficiency gap, 0.35 availability, 0.20 explicit preference
similarity, and 0.10 format fit; availability reaches its maximum score at 30
overlapping minutes. Scores are rankings, not probabilities or validated
measures of learning effectiveness. These weights, the 30-minute cap, and the
1–5 scale are engineering defaults for the prototype, not values specified by
the proposal; validate or replace them with the research team.

Example request (interval timestamps must include an explicit timezone):

```json
{
  "request": {
    "learning_request_id": 23,
    "tutee_user_id": 100,
    "roles": ["tutee"],
    "subject_id": 5,
    "topic_id": 2,
    "current_proficiency": 2,
    "preferred_format": "one_to_one",
    "status": "open",
    "availability": [
      { "start": "2026-11-02T09:00:00Z", "end": "2026-11-02T11:00:00Z" }
    ],
    "preferences": ["worked_examples", "visual"]
  },
  "tutors": [
    {
      "tutor_user_id": 201,
      "roles": ["tutor"],
      "subject_id": 5,
      "topic_id": 2,
      "proficiency_level": 4,
      "status": "active",
      "formats": ["one_to_one", "group"],
      "availability": [
        { "start": "2026-11-02T09:30:00Z", "end": "2026-11-02T10:00:00Z" }
      ],
      "preferences": ["worked_examples"]
    }
  ],
  "limit": 10
}
```

Successful responses return ranked IDs, score components, and non-sensitive
reason codes. `limit` is optional and defaults to 10 (maximum 50). Malformed
input returns `400`. Requests with no eligible candidate return an empty
`data` array.

Recommendation results should expose an understandable, non-sensitive
explanation (for example subject fit and schedule overlap), not private profile
data or unsupported claims of fairness. The initial schema stores
`algorithm_version`, optional score components, and explanation so the method
can be audited and revised.

## Scheduling — `/sched`

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/availability` | List the authenticated user's weekly availability |
| `PUT` | `/availability` | Replace the authenticated user's availability slots |
| `GET` | `/sessions` | List sessions visible to the authenticated user |
| `POST` | `/sessions` | Request a session from an accepted match |
| `GET` | `/sessions/:sessionId` | Read a session if the user participates or is authorized staff |
| `PATCH` | `/sessions/:sessionId` | Confirm, reschedule, or cancel according to transition rules |

Session times are UTC instants in the API and database; availability includes
an IANA timezone and local recurring weekday/time. The service must reject
invalid transitions and overlapping bookings. Exact cancellation and
rescheduling policies remain to be set. A session has a participant list with
each participant assigned a tutor or tutee role, so group sessions can contain
multiple tutees (and, if policy permits, multiple tutors). The service must
validate role grants and participant limits; these constraints cannot be
expressed by the initial relational schema alone.

## Session feedback — `/fb`

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/sessions/:sessionId` | Submit one feedback response for a completed session |
| `GET` | `/sessions/:sessionId` | Read feedback visible to a participant or authorized staff |

Example request:

```json
{
  "rating": 5,
  "learning_helpfulness": 4,
  "comment": "The worked examples helped me understand the topic."
}
```

Ratings use 1–5 in the schema. Feedback must be limited to session participants;
moderation and visibility rules are not yet implemented.

## Research analytics — `/adata`

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/consent` | Read the authenticated user's research-monitoring consent state |
| `POST` | `/consent` | Record explicit consent for the one-month Phase 2 usage study |
| `DELETE` | `/consent` | Withdraw Phase 2 analytics consent |
| `GET` | `/admin/summary` | Return approved, aggregate research measures to authorized researchers/admins |

Analytics must be disabled unless consent is active. The server should create
events from validated application actions rather than trusting arbitrary
client-supplied event records. Do not put names, emails, message content,
free-form profile text, or other direct identifiers in analytics events.
Withdrawal, retention, deletion, participant linkage, and approved aggregate
reporting need to follow the research consent and school requirements.

## Authentication — `/auth/v1`

The authentication service is reachable at
`https://palsys.softshell-grue.ts.net/auth/v1` through the DBSERV public Funnel
hostname, but `/auth/v1` is HOST-only at the application layer. Every request
under this prefix—including `/health` and unknown paths—must include a valid
RS256 HOST JWT. Requests without a valid bearer token receive `403 forbidden`;
a valid token without the required route capability also receives
`403 forbidden`. CLIENTs must never call these routes directly.

The authentication service exposes these routes:

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/providers/google/config` | Return public Google authorization settings to a scoped HOST |
| `POST` | `/providers/google/exchange` | Exchange a HOST-supplied authorization code with Google and verify its OIDC ID token |
| `POST` | `/admins/login` | Verify a local admin username/password and return an active admin account identity |

All routes require an RS256 HOST JWT. Provider config requires scope
`auth:provider`, resource `authentication`, and action `provider_config`;
provider exchange uses action `provider_exchange`; admin login requires scope
`auth:admin`, resource `authentication`, and action `admin_login`. These
capabilities are separate from generic database CRUD scopes. The browser must
never call DBSERV or receive the provider client secret.

The provider config response contains only `provider`, `enabled`, public
`client_id`, the fixed Google authorization endpoint, exact allowed
`redirect_uris`, and scopes. With missing credentials or redirect URIs it
returns `{ "data": { "provider": "google", "enabled": false } }`. The HOST
compares its derived callback URL byte-for-byte against the allowlist before
redirecting the browser.

The provider exchange accepts only `code`, `code_verifier`, `nonce`, and
`redirect_uri`. The exact redirect URI must appear in `GOOGLE_REDIRECT_URIS` as a
comma-separated allowlist entry. The service submits the client secret only to
Google's token endpoint, then verifies signature, issuer, audience, expiry,
nonce, subject, and verified email using Google's JWKS. Responses contain only
provider, subject, verified email, and display name. Missing credentials or
redirect configuration disable the provider; invalid config returns a generic
unavailable response. OAuth tokens and secrets are never returned or logged.

Store `TMP_GCLOUD_CLIENTID` and `TMP_GCLOUD_CLIENTKEY` in the ignored
`authentication/.provider.env` file. Configure `GOOGLE_REDIRECT_URIS` in the
authentication service environment or the same private provider config.
Set its permissions with `chmod 600 authentication/.provider.env`; it must be
a regular, non-symlink file and the authentication service rejects broader
permissions. `.provider.env.example` has empty values only. Keep the real file
outside source control and outside any static web root. HOST now issues only the
specific provider config/exchange capabilities for these internal calls; its
public API handles browser redirects and sessions.

Admin login accepts only an ASCII username (3–64 characters) and a printable
ASCII password (12–128 characters). It reads `admin_credentials` separately
from `users`, compares the scrypt hash, and requires `account_status=active`
plus the `admin` role before success. Unknown username, wrong password,
inactive account, and a missing admin role share the same generic 401. Attempts
are rate-limited by IP and by normalized username, and audit records store only
an IPv4 `/24` or IPv6 `/64` prefix. Existing `users` still has no password
column.

Apply [`database/schemas/schema_v3_auth.sql`](database/schemas/schema_v3_auth.sql)
before enabling the endpoints. This migration has not been run by these
changes.

## Messaging — `/chat/v1`

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/conversations` | Create a one-to-one or study-group conversation with eligible members |
| `GET` | `/conversations` | List conversations for the authenticated user |
| `GET` | `/conversations/:conversationId/messages` | Read paginated message history for a member |
| `POST` | `/conversations/:conversationId/messages` | Send a message as the authenticated member |
| `POST` | `/conversations/:conversationId/members` | Add an eligible member where policy permits |
| `DELETE` | `/conversations/:conversationId/members/me` | Leave a conversation where policy permits |

REST is for conversation management and history; the realtime transport,
pagination shape, moderation workflow, retention, and reporting controls need
separate decisions. A moderation status in the schema does not mean AI
moderation is implemented or guarantees that harmful content is prevented.

## Database service — `/db`

The internal database API retains its existing generic CRUD contract and is
restricted to explicitly allowlisted resources. After applying
[`database/schemas/schema_v1.sql`](database/schemas/schema_v1.sql), configure
`DB_ALLOWED_TABLES` deliberately. Candidate v1 resource names are:

`GET /health` is an unauthenticated liveness check and does not query MariaDB.
`GET /health/database` requires a valid host JWT and checks MariaDB with
`SELECT 1`. The runner performs its own database check through its local
connection pool. List requests accept optional `limit` and `offset` query
parameters: the default page is 100 rows, the maximum `limit` is 500, and
`offset` defaults to 0.

```dotenv
DB_ALLOWED_TABLES=roles,users,user_roles,student_profiles,subjects,topics,tutor_subjects,learning_requests,availability_slots,match_recommendations,study_sessions,session_participants,session_match_recommendations,session_feedback,research_consents,research_participants,analytics_events,conversations,conversation_members,messages,web_sessions
```

Only expose tables the backend actually needs. In particular, keep the
`research_participants` identity mapping restricted to trusted backend code.
The database API is not a substitute for feature-level ownership checks, and
the public frontend must not receive database-service signing credentials.

The Mentos host API additionally requires the `web_sessions` resource for its
opaque MariaDB-backed browser sessions. Apply
[`database/schemas/schema_v2_sessions.sql`](database/schemas/schema_v2_sessions.sql)
before adding it to the database service allowlist.
