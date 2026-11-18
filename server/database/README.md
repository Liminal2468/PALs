# PALs database API

> **Legacy Node-line reference:** this document describes the retired Node
> database module. The active .NET DBSERV uses `server/config.yaml` for
> non-secret settings and `server/.env` only for `DB_PASSWORD`; do not use the
> Node setup commands below for the current server.

This workspace module runs inside the single Node.js API process in `server/`,
beside MariaDB on the database host. MariaDB itself is provisioned and
configured separately. The module accepts short-lived RS256 JWTs issued by the
Mentos host, validates issuer, audience, expiration, signing key and scope, and
only exposes tables listed in `DB_ALLOWED_TABLES`.

## Setup

```bash
./setup.sh
```

The script requires Node.js 20+ and npm, runs `npm ci`, creates protected key
directories, and creates `.env` from `.env.example` only if one does not already
exist. It does not create or alter the MariaDB database. Set the MariaDB
connection values and an explicit comma-separated table allowlist in `.env`.
Use `BIND_ADDRESS` for the HTTP listener address. The old `HOST` bind setting
is accepted temporarily as a deprecated fallback and logs a warning; migrate
it to `BIND_ADDRESS`.
Install a trusted host public key as
`keys/trusted/<kid>.pem` before starting the service:

The shared mysql2 pool uses UTC (`timezone: "Z"`) for date conversion. HOST
session expiry values are stored as UTC timestamps, so interpreting SQL
`DATETIME` values in the DBSERV machine's local timezone would shift expiry
checks.

## Initial PALs schema

After creating `palsysdb`, apply
[`schemas/schema_v1.sql`](schemas/schema_v1.sql) using a MariaDB administrative
account, not the least-privilege API account. Apply the server-side session
migration immediately after the base schema:

```bash
sudo mariadb palsysdb < schemas/schema_v1.sql
sudo mariadb palsysdb < schemas/schema_v2_sessions.sql
```

The schema defines user accounts linked to an external eLMS identity (no local
password storage), multi-role grants (`tutor`, `tutee`, and `admin`), student
profiles, subjects/topics, tutor proficiency, learning requests, availability,
match recommendations, sessions, feedback, consent-gated pseudonymous research
analytics, and one-to-one/study-group messaging. A user may hold both the tutor
and tutee roles; roles are not mutually exclusive account types.

The schema is an initial baseline, not a migration framework. Review it against
the finalized school, consent, matching, and moderation requirements before
production use. API-managed tables need exactly one primary key because the
generic `/db` CRUD layer requires that shape. Configure the service's
`DB_ALLOWED_TABLES` intentionally; see [`../API.md`](../API.md) for the draft
API contracts and candidate resource list. The session table must be included
in the allowlist when the Mentos Flask host API uses MariaDB-backed sessions.

The schema constrains profiles to SHS grade levels 11 and 12 and validates
availability/session time ranges. Matching proficiency and feedback currently
use a 1–5 scale to match the prototype API; the proposal does not define or
validate that proficiency scale, so treat it as an implementation default and
revise the constraints if educators approve a different scale.

The server process is started from the `server/` workspace with `npm start`.

Do not expose MariaDB directly to the host application. Restrict its network
access to this service, require TLS between hosts (or place the service behind
a TLS-terminating reverse proxy), and use a least-privilege database account other than root. Refer to [database](DATABASE.md)

## Host JWT key provisioning and rotation

Generate an RSA signing key pair in the database workspace:

```bash
npm run keygen --workspace @mentos/database-api
```

The script writes a private key with restrictive permissions and a public key
under `keys/generated/`. Transfer the private key to the host using a secure
secret-provisioning channel, configure the host's `JWT_PRIVATE_KEY_PATH` and
`JWT_KEY_ID`, then install the matching public key as
`keys/trusted/<kid>.pem` on this server. Remove temporary private-key copies
from the database host after transfer. Never commit or serve private keys.

The `kid` is the RFC 7638 thumbprint of the public JWK. The printed SHA-256
fingerprint is for operator verification; keys are not hashed for signing or
verification. To rotate, provision a new key pair and trust its public key
before switching the host issuer to the new `kid`. Keep the old public key
trusted until all old short-lived tokens have expired, then remove it.
The verifier caches the trusted-key directory for up to five seconds, then
reloads it automatically. Adding a key takes effect after the cache expires;
the service does not need to restart.
`GET /db/.well-known/jwks.json` lists the currently trusted verification keys.

## Request contract

All database data endpoints require:

```http
Authorization: Bearer <short-lived RS256 JWT>
Content-Type: application/json
```

The unauthenticated `GET /health` endpoint is liveness-only and does not query
MariaDB. `GET /.well-known/jwks.json` publishes the trusted public keys.
`GET /health/database` requires a valid host JWT and checks MariaDB readiness.

Required JWT claims are `iss`, `aud`, `sub`, `iat`, `exp`, `scope`,
`resource`, and `action`. The token's `resource` and `action` must match the
requested table and operation. `scope` must include `db:read` for GET/aggregate
or `db:write` for mutations.
Only RS256 signatures are accepted, and token lifetime cannot exceed 15 minutes.

| Method | Path | Purpose |
|---|---|---|
| `POST` | `/db/:resource` | Insert a JSON record; returns `201` and `{ "data": { "<primary_key>": ..., "affected_rows": 1 } }` |
| `GET` | `/db/:resource` | List records; equality filters and optional `limit`/`offset` are query parameters |
| `GET` | `/db/:resource/:id` | Read one record |
| `PATCH` | `/db/:resource/:id` | Update supplied fields |
| `POST` | `/db/:resource/aggregate` | Aggregate using `group_by`, `metrics`, and optional equality `filters` |
| `DELETE` | `/db/:resource/:id` | Delete one record; returns `204` |

Successful JSON responses use a `{ "data": ... }` envelope except for delete.
Errors use `{ "error": { "code": "...", "message": "..." } }`.

Example aggregation body:

```json
{
  "group_by": "status",
  "metrics": [
    { "operation": "count", "field": "*", "alias": "total" }
  ],
  "filters": { "active": 1 }
}
```

Resource names must be in `DB_ALLOWED_TABLES`; fields are checked against the
selected table's MariaDB columns and values are parameterized. Tables must have
one primary key. List queries default to 100 rows (`limit=100`, `offset=0`),
with a maximum page size of 500; `offset` must be a non-negative safe integer.

Run the API and key-rotation tests from `server/` with:

```bash
npm test --workspace @mentos/database-api
```



<center>NOTES BY: KeiNerokami </center>
