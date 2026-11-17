# Mentos host API

The Flask HOST API is mounted at `/api/v1` and uses MariaDB-backed opaque
server-side browser sessions. Its health route returns only `{"status":
"ready"}` or `{"status":"degraded"}` for DBSERV reachability, caching that
state for five seconds. It provides provider options/start/callback, signup
completion, admin login, health/session routes, profile read/update, and tutee
learning-request list/create. See the [auth contract](../../../bindoc/maindoc/notes/auth-contract.md)
for request and response shapes.

Authenticated routes require the current `users.account_status` to be exactly
`active`. The `/me` response exposes only user `id` and `display_name`, profile
`grade_level`, `strand`, `timezone`, and `learning_preferences`, plus role keys.
Learning-request responses expose only `id`, `subject_id`, `topic_id`,
`current_proficiency`, `requested_help`, `preferred_format`, `request_status`,
`created_at`, and `updated_at` when those fields exist.

## Host API configuration and startup

Configure the local database API settings in `api/.env` (or environment
variables). The existing `src/api/db/.env.config` remains supported as a
legacy fallback. Copy [`./.env.example`](./.env.example) as a starting point.
Apply
[`../../server/database/schemas/schema_v2_sessions.sql`](../../server/database/schemas/schema_v2_sessions.sql)
and, for authentication, [`../../server/database/schemas/schema_v3_auth.sql`](../../server/database/schemas/schema_v3_auth.sql).
Add `web_sessions`, `auth_identities`, `admin_credentials`, and `auth_attempts`
to `DB_ALLOWED_TABLES` on the database API service. Also configure:

Set `DBSERV_URL` to the HTTPS public origin of DBSERV:
`https://palsys.softshell-grue.ts.net` (without a trailing slash or `/db`).
The DB client normalizes a trailing slash if supplied and adds `/db/...` to
the origin. The public Funnel hostname exposes DBSERV to the internet; every
`/auth/v1` route requires a valid HOST JWT and returns `403 forbidden` without
one. HTTPS normally uses external port 443;
Nginx, Apache, or a private tailnet proxy can forward to DBSERV's private
listener. The client does not need the proxy's internal upstream port in
`DBSERV_URL`. The deprecated `HOST` setting is still accepted as a fallback
and logs one warning; migrate it to `DBSERV_URL`.

```dotenv
DBSERV_URL=https://palsys.softshell-grue.ts.net
SESSION_LIFETIME_HOURS=12
SESSION_COOKIE_SECURE=true
APP_ENV=development
DEV_LOGIN_ENABLED=false
PORT=8000
```

The dev provider is omitted from `/api/v1/auth/options` unless
`DEV_LOGIN_ENABLED=true`; HOST startup refuses that flag when `APP_ENV` or
`NODE_ENV` is `production`. The general OIDC adapter is disabled until all
`OIDC_*` settings (including the exact `OIDC_REDIRECT_URI` and its comma-separated
allowlist) are supplied. Do not configure Google in this HOST adapter; Google
exchange and credential handling remain deferred to DBSERV authentication.

The demo seed is an explicit manual operation after applying schema v3 and
allowlisting its resources. It creates five fake active role accounts and uses
the runtime-only `DEMO_ADMIN_PASSWORD` for the demo admin:

```bash
cd mentos/api
read -rsp 'Demo admin password: ' DEMO_ADMIN_PASSWORD
export DEMO_ADMIN_PASSWORD
printf '\n'
.venv/bin/python -m api.demo_seed
unset DEMO_ADMIN_PASSWORD
```

The seed command is never run by app startup. It does not print the password;
use a temporary demo-only password and rotate it after the demonstration.
Admin credentials are stored outside `users` using scrypt, and only an active
account with the `admin` role can establish an authenticated session.

`SESSION_COOKIE_SECURE=true` requires HTTPS. For local-only Vite development
over HTTP, set it to `false`; do not use that setting in production. The cookie
is `HttpOnly`, `SameSite=Lax`, path `/`; the browser receives only a random
opaque session key. The cookie retains that raw value, while `web_sessions`
stores its SHA-256 hex digest. Existing raw-key rows are upgraded when a valid
cookie next presents them. User identity and session data are held in the
`web_sessions` table. Successful provider and admin logins call
`establish_authenticated_session()` after validating the external identity and
rotate the session ID. Responses that access the session vary on `Cookie`.

The HOST API applies an in-memory limit of 120 requests per client IP per
15 minutes, with 30 per 15 minutes on `/api/v1/session` and 10 per 15 minutes
on `/api/v1/auth/login`, and five per 15 minutes on admin login by both IP and
normalized username. These counters are process-local; use shared storage
before running multiple HOST workers. `TRUST_PROXY` defaults to `false`.
Enable `TRUST_PROXY=true` only when HOST is reachable exclusively through
exactly one trusted proxy that overwrites `X-Forwarded-For` and
`X-Forwarded-Proto`; this makes per-client limits use the forwarded client IP.
Without that explicit setting, forwarded headers are not trusted. All `/api/v1`
responses use `Cache-Control: no-store` and `X-Content-Type-Options: nosniff`.

DBSERV timeouts and connection failures return generic 503 errors; an upstream
429 also maps to 503, and upstream 5xx responses map to 502. Stable error codes
are `dbserv_timeout`, `dbserv_unavailable`, `dbserv_rate_limited`, and
`dbserv_error`. The detailed cause is logged server-side and is not included in
CLIENT responses.

Start the host API with:

```bash
cd mentos
uv run --project api api
```

The project command runs the WSGI app with Waitress on `127.0.0.1`; put the
trusted reverse proxy in front of it rather than exposing the development
server.

The Vite development server proxies `/api` to this host service. The Vite frontend should use `/api/v1/auth/options` to populate its provider
modal and follow the returned `start_url`. Provider callback responses either
establish an active account session or mark a new account `signup_required`;
signup completion records terms acceptance but leaves the account pending for
review.

Expired records are rejected when presented. An optional maintenance job can
remove abandoned expired rows. Run it on DBSERV with a restricted MariaDB
maintenance account, using a root-owned option file readable by the service
account. The following example is documentation only; do not install or enable
it without reviewing the client path, credentials, and account permissions.

```sql
DELETE FROM web_sessions WHERE expires_at < UTC_TIMESTAMP(3);
```

Example `/etc/systemd/system/mentos-session-cleanup.service`:

```ini
[Unit]
Description=Remove expired Mentos browser sessions

[Service]
Type=oneshot
User=palsys
ExecStart=/usr/bin/mariadb --defaults-extra-file=/etc/palsys/cleanup.cnf palsysdb --execute="DELETE FROM web_sessions WHERE expires_at < UTC_TIMESTAMP(3)"
```

Example `/etc/systemd/system/mentos-session-cleanup.timer`:

```ini
[Unit]
Description=Daily expired Mentos session cleanup

[Timer]
OnCalendar=daily
Persistent=true
RandomizedDelaySec=15m

[Install]
WantedBy=timers.target
```

`/usr/bin/mariadb`, the `palsys` operating-system account, the database option
file, and the least-privilege SQL account are deployment choices to verify on
DBSERV. The timer example is not enabled or run by this project.

Benchmark the live API health endpoints from `mentos/api` with:

```bash
uv run --project . python tests/tests-api.py
```

It makes five GET samples for each API health endpoint and the database
service's public JWKS endpoint. It logs each response's status, latency, body
size, and byte-per-second response speed, then prints per-endpoint and combined
averages. Failed requests are logged and cause the script to exit with an
error. The anonymous `/auth/v1/health` sample expects `403` because the
authentication router is protected by the HOST JWT gate; the other benchmark
health/JWKS samples expect `200`. It requires a working `api/.env` and network
access to the database API.

The API's `api/.env` is ignored by Git. Keep the matching signing private key
under the ignored `api/.keys/` directory, and install only its public key on
the database server. Demo keys are for development only; rotate and replace
them before production use.
