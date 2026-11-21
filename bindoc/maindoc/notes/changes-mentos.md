# Mentos HOST change log

## 2026-11-21 — Componentized student login and temporary verification route

- **Frontend:** Moved the existing login dialog markup into `mentos/public/components/login/modal.login.htm` without changing the login styles or class names. Added `modal.verify.html` using the same modal classes and load both fragments from the landing page.
- **Verification flow:** After student sign-in, Mentos now opens the verification panel and calls `/api/v1/verify?code=...`. `WebendVerify` extends the Webend controller and forwards the request privately to DBSERV.
- **DBSERV:** Added the host-key-protected `/verify` route. It currently accepts only six-digit format-valid codes and returns a temporary success response.
- **TODO:** Replace format-only acceptance with the approved global TOTP generator and verification-email transaction after the bindoc/testdoc verification tests are ready. The temporary route is not proof of identity.
- **Verification:** `dotnet build --no-restore` passed for both projects and `node --check mentos/public/scripts/login.js` passed. No styles were changed and no deployment was performed.

## 2026-11-15 — Repair local executable permissions before startup

- **Files changed:** `mentos/bin/permchk`, `mentos/run`
- **Summary:** Added the Bash `perm_check(FILE)` helper and made the combined launcher repair missing execute bits for the installed Vite shims and the API virtual environment's `api` and `python` entry points before startup. The helper checks each supplied path and runs `chmod +x` only when it is not executable.
- **Verification:** `bash -n mentos/bin/permchk mentos/run`, helper smoke test with a temporary file, and `git diff --check` passed. No `mentos/public/` files were changed.
- **Defaults / unverified:** Dependency trees remain ignored local installation state; the launcher only repairs the known executable entry points and does not chmod every file under `node_modules` or `.venv`.

## 2026-11-03 — Normalize the DBSERV Serve origin

- **Files changed:** `mentos/api/.env.example`, `mentos/api/README.md`, `mentos/api/utils/dbflow.md`, `bindoc/filels.md`
- **Summary:** Set the documented HOST binding URL to `https://palsys.softshell-grue.ts.net` without a trailing slash, matching the requested address. The DB client still normalizes optional trailing slashes.
- **Verification:** Searched the HOST example and docs for the endpoint and ran `git diff --check`.
- **Defaults / unverified:** No real `.env` was modified; Serve reachability was not tested.

## 2026-11-02 — Update the DBSERV Serve origin

- **Files changed:** `mentos/api/.env.example`, `mentos/api/README.md`, `mentos/api/utils/dbflow.md`, `bindoc/filels.md`
- **Summary:** Replaced the prior DBSERV hostname and placeholder with `https://palsys.softshell-grue.ts.net/` in the HOST example and documentation. Clarified that the client normalizes the trailing slash and appends `/db/...`; described Serve rather than Funnel in the request-flow diagram.
- **Verification:** Searched Mentos and bindoc documentation for the previous hostname and placeholder; `git diff --check` passed.
- **Defaults / unverified:** Updated only the example configuration, not any real `.env` file. Serve reachability was not tested.

## 2026-11-02 — Rename the DBSERV origin setting

- **Files changed:** `mentos/api/src/api/db/db.py`, `mentos/api/tests/test_db_client.py`, `mentos/api/tests/tests-api.py`, `mentos/api/tests/test-ratelim-cap.py`, `mentos/api/README.md`, `mentos/api/.env.example`, `mentos/api/utils/dbflow.md`, `mentos/README.md`, `bindoc/maindoc/project.md`, `bindoc/filels.md`
- **Summary:** Renamed the HOST-side DBSERV origin to `DBSERV_URL`, kept `HOST` as a deprecated fallback that logs one warning, and added a safe example configuration. Added the HOST/DBSERV/CLIENT glossary and replaced the apmain host wording in the file map.
- **Verification:** `cd mentos/api && .venv/bin/python -m unittest tests.test_db_client` — 4 tests passed, including new- and legacy-setting coverage. `git diff --check` passed.
- **Defaults / unverified:** No DBSERV origin is supplied in code; the example uses the non-operational `dbserv.example.test` placeholder. Real `.env` values were not inspected.

## 2026-11-02 — Check DBSERV before HOST startup

- **Files changed:** `mentos/api/src/api/db/db.py`, `mentos/api/src/api/healthcheck.py`, `mentos/api/src/api/routes.py`, `mentos/api/src/api/app.py`, `mentos/api/tests/test_db_client.py`, `mentos/api/tests/test_host_api.py`, `mentos/bin/runner.js`, `mentos/README.md`, `mentos/api/README.md`
- **Summary:** Replaced eight local API prefix probes with DBSERV liveness plus authenticated database readiness checks through the Python DB client. `/api/v1/health` now returns only a generic `ready` or `degraded` status and keeps the upstream result in a short server-side cache.
- **Verification:** `cd mentos/api && .venv/bin/python -m unittest tests.test_db_client tests.test_host_api` — 11 tests passed. `node --check mentos/bin/runner.js` and Python syntax checks passed.
- **Defaults / unverified:** Health state is cached for 5 seconds; runner retries three times then waits 60 seconds. No live DBSERV connection was attempted; network readiness remains unverified.

## 2026-11-02 — Enforce active accounts and minimize CLIENT data

- **Files changed:** `mentos/api/src/api/routes.py`, `mentos/api/tests/test_host_api.py`, `mentos/api/README.md`
- **Summary:** Authenticated requests now reload the user and accept only `account_status == "active"`; stale/inactive sessions are cleared. `/me` and learning-request responses now use explicit allowlists instead of returning full DB records.
- **Verification:** `cd mentos/api && .venv/bin/python -m unittest tests.test_host_api tests.test_db_client` — 13 tests passed, including inactive-user rejection and private-field exclusion.
- **Defaults / unverified:** `/me` fields: user `id`, `display_name`; profile `grade_level`, `strand`, `timezone`, `learning_preferences`; role keys. Learning-request fields: `id`, `subject_id`, `topic_id`, `current_proficiency`, `requested_help`, `preferred_format`, `request_status`, `created_at`, `updated_at` (only available values are returned). The `schemas/` guardrail prevented checking the complete set of status values; only exact `active` is accepted.

## 2026-11-02 — Hash stored session keys and document cleanup

- **Files changed:** `mentos/api/src/api/sessions.py`, `mentos/api/tests/test_host_api.py`, `mentos/api/README.md`
- **Summary:** Session cookies retain the random raw key while the database stores its SHA-256 hex digest; existing raw-key rows are upgraded when presented. Added `Vary: Cookie` on session access and documented an optional SQL/systemd cleanup job without installing or enabling it.
- **Verification:** `cd mentos/api && .venv/bin/python -m unittest tests.test_host_api` — 9 tests passed, including expiry with `TZ=America/Los_Angeles`. Extracted example units passed `systemd-analyze verify`; the cleanup job was not run.
- **Defaults / unverified:** Session hashes are lowercase SHA-256 hex; cleanup example runs daily with a 15-minute random delay. The deployment DB credentials and maintenance permissions must be supplied and reviewed by the operator.

## 2026-11-02 — Harden HOST API edges

- **Files changed:** `mentos/api/pyproject.toml`, `mentos/api/uv.lock`, `mentos/api/.env.example`, `mentos/api/src/api/app.py`, `mentos/api/src/api/limiting.py`, `mentos/api/src/api/routes.py`, `mentos/api/tests/test_host_api.py`, `mentos/api/README.md`, `mentos/README.md`
- **Summary:** Mapped DBSERV timeouts, connection errors, 429s, and 5xx responses to stable sanitized 502/503 errors with detailed server-side logging. Added per-IP rate limits, opt-in one-proxy `ProxyFix`, no-store/nosniff headers, and Waitress startup while keeping the login route at 501.
- **Verification:** `cd mentos/api && .venv/bin/python -m unittest tests.test_host_api tests.test_db_client` — 18 tests passed. Python syntax checks passed. Official Flask-Limiter, Waitress, and Werkzeug proxy docs were checked; installed versions are Flask-Limiter 4.1.1, Waitress 3.0.2, and Werkzeug 3.1.9.
- **Defaults / unverified:** Limits are 120 API, 30 session, and 10 login requests per IP per 15 minutes using process-local memory storage. Proxy trust defaults off and assumes one trusted proxy when enabled. Multi-worker shared rate-limit storage and deployment proxy/header behavior remain unverified.

## 2026-11-02 — Remove duplicate session cookie constant

- **Files changed:** `mentos/api/src/api/sessions.py`, `mentos/api/tests/test_host_api.py`
- **Summary:** Removed the unused duplicate cookie-name constant; the Flask app configuration remains the single source of truth. Kept `api/back/` intact and audited its files and references.
- **Verification:** `cd mentos/api && .venv/bin/python -m unittest tests.test_host_api.HostApiTests.test_session_cookie_is_opaque_and_http_only` — passed. Search found no imports from `api/back/` in active `src/api/` code.
- **Defaults / unverified:** `api/back/` contains `auth/auth.py`, `auth/data.py`, `auth/encryter.py`, and `dbassets/mentos.db`, plus compiled `auth/__pycache__` files. The legacy `auth/data.py` imports `back.auth.auth`; `auth/auth.py` imports its encryption helper. The SQLite database contents were not read.

## 2026-11-02 — Refresh the machine map

- **Files changed:** `mentos/README.md`, `bindoc/filels.md`
- **Summary:** Updated the map for the Waitress-backed HOST app, new healthcheck and limiter modules, and runner checks against DBSERV liveness/readiness rather than eight Node prefixes.
- **Verification:** `cd mentos/api && .venv/bin/python -m unittest discover -s tests -p 'test_*.py'` — 18 tests passed; documentation references were searched for stale runner descriptions.
- **Defaults / unverified:** Documentation-only alignment; the runner was not pointed at a live DBSERV.

## 2026-11-03 — Scaffold provider login and dev flow

- **Files changed:** `mentos/api/src/api/auth_providers.py`, `mentos/api/src/api/app.py`, `mentos/api/src/api/routes.py`, `mentos/api/src/api/sessions.py`, `mentos/api/tests/test_host_api.py`
- **Summary:** Added a provider-adapter registry, environment-gated dev provider, OIDC authorization-code/PKCE adapter, auth options/start/callback routes, provider+subject account matching, pending-account creation, fresh authenticated session rotation, and minimized attempt logging with IPv4 /24 or IPv6 /64 client prefixes. Dev identities remain unavailable by default and startup rejects dev mode under the production marker.
- **Verification:** `cd mentos/api && .venv/bin/python -m unittest tests.test_host_api` — 17 tests passed, including dev-disabled, production refusal, successful seeded identity, pending signup, and login rate-limit coverage.
- **Defaults / unverified:** HOST production is marked by `APP_ENV=production` (also reject `NODE_ENV=production`); no explicit HOST marker existed previously. Demo identities use `example.test`. Persistence resources depend on the migration in a later step. OIDC network exchange/signature verification and a real identity provider were not exercised.

## 2026-11-03 — Add the manual dev-account seed and signup path

- **Files changed:** `mentos/api/src/api/auth_providers.py`, `mentos/api/src/api/demo_seed.py`, `mentos/api/src/api/routes.py`, `mentos/api/.env.example`, `mentos/api/README.md`, `mentos/api/tests/test_host_api.py`, `bindoc/maindoc/notes/auth-contract.md`
- **Summary:** Added five fake `example.test` identities, kept the dev provider off by default and unavailable in production, and added a manually invoked idempotent seed command that takes the admin password only from runtime environment. New external identities are stored pending; signup records terms but does not activate accounts.
- **Verification:** `cd mentos/api && .venv/bin/python -m unittest tests.test_host_api` — 23 tests passed, including disabled/production dev checks, seed idempotence, pending signup, and generic OIDC URL/redirect checks.
- **Defaults / unverified:** The demo admin username is `demo-admin`; its password must be supplied as `DEMO_ADMIN_PASSWORD` at seed time and is never printed. The database migration is not applied. No real provider was contacted.

## 2026-11-03 — Match provider identities and keep new accounts pending

- **Files changed:** `mentos/api/src/api/routes.py`, `mentos/api/src/api/auth_providers.py`, `bindoc/maindoc/notes/auth-contract.md`, `mentos/api/tests/test_host_api.py`
- **Summary:** Matched existing accounts only by `(provider, provider_subject)`, created/link pending users when no identity exists, resumed pending signup after later provider callbacks, and returned active accounts only through an authenticated session. The HOST OIDC verifier uses a fixed 10-second JWKS-fetch timeout and exact redirect allowlisting.
- **Verification:** `cd mentos/api && .venv/bin/python -m unittest tests.test_host_api` — 23 tests passed. PyJWT 2.15.1's installed `PyJWKClient` signature includes `timeout`; official PyJWT API documentation was checked.
- **Defaults / unverified:** New accounts do not receive roles or become active automatically. The provider exchange/ID-token verification path was not exercised against a real issuer; Google is deliberately reserved for DBSERV and not enabled in the HOST OIDC adapter.

## 2026-11-03 — Add throttled admin authentication

- **Files changed:** `mentos/api/src/api/passwords.py`, `mentos/api/src/api/demo_seed.py`, `mentos/api/src/api/app.py`, `mentos/api/src/api/routes.py`, `mentos/api/tests/test_host_api.py`, `mentos/api/README.md`, `bindoc/maindoc/notes/auth-contract.md`
- **Summary:** Added strict admin input validation, parameterized credential lookup, scrypt verification with dummy work for unknown usernames, generic 401 failures, active-account/admin-role checks, and successful session rotation. Applied five-per-15-minute limits by IP and normalized username.
- **Verification:** `cd mentos/api && .venv/bin/python -m unittest tests.test_host_api` — 23 tests passed, including unknown/wrong-password equivalence, suspended-account rejection, role checks, username throttling, and session rotation.
- **Defaults / unverified:** Scrypt uses `N=16384`, `r=8`, `p=1`, 16-byte random salts, and 64-byte derived keys. HOST rate-limit state is in-process; multi-worker shared storage remains unconfigured.

## 2026-11-03 — Audit provider, signup, and admin attempts

- **Files changed:** `mentos/api/src/api/routes.py`, `mentos/api/src/api/app.py`, `mentos/api/tests/test_host_api.py`, `bindoc/maindoc/notes/auth-contract.md`
- **Summary:** Recorded provider start/callback, signup, admin success/failure, invalid input, and throttled auth attempts with reason codes and optional user IDs. Stored only IPv4 `/24` or IPv6 `/64` client prefixes; passwords, tokens, usernames, and request bodies are not logged.
- **Verification:** `cd mentos/api && .venv/bin/python -m unittest tests.test_host_api` — 23 tests passed, including truncated-IP and rate-limit audit assertions.
- **Defaults / unverified:** Audit rows use the DBSERV `auth_attempts` resource and require the unrun schema v3 migration plus its DB allowlist addition.

## 2026-11-09 — Connect the real Google preview flow

- **Files changed:** `mentos/api/src/api/db/db.py`, `mentos/api/src/api/routes.py`, `mentos/api/tests/test_db_client.py`, `mentos/api/tests/test_host_api.py`, `mentos/public/components/login/modal.login.htm`, `bindoc/maindoc/notes/auth-contract.md`
- **Summary:** Added the narrowly scoped signed HOST calls to DBSERV provider config and exchange routes. HOST now starts real Google OAuth with session-bound state, nonce, and S256 PKCE, validates the callback state, and submits the code to DBSERV without ever receiving the client secret. The preview shows Google as available only when configured and Microsoft Entra as planned.
- **Verification:** `uv run --project mentos/api python -m unittest discover -s mentos/api/tests -p 'test_*.py'` — 33 tests passed; `cd mentos && npm run build` succeeded and copied the standalone preview; browser validation with mocked options confirmed configured Google is enabled while unconfigured Google and planned Entra are disabled.
- **Defaults / unverified:** Google remains disabled until credentials, a TLS-enabled HOST origin, and the exact callback URI are configured in DBSERV. The supplied HOST origin is HTTP; the documented future callback is `https://reime.softshell-grue.ts.net/api/v1/auth/google/callback`. No real provider or DBSERV network calls were made.

## 2026-11-09 — Document how to run Mentos

- **Files changed:** `bindoc/maindoc/notectx.md`
- **Summary:** Added local startup steps for the Waitress HOST API, Vite frontend, optional DBSERV-gated API runner, and login-preview/provider setup.
- **Verification:** Confirmed the commands and ports against the Mentos README and API documentation; `git diff --check` passed.
- **Defaults / unverified:** Vite uses its default port 5173 and proxies `/api` to HOST at `127.0.0.1:8000`. Real local environment values are deployment-specific and are not included.

## 2026-11-09 — Launch Vite and HOST API together

- **Files changed:** `mentos/run`, `mentos/README.md`, `bindoc/maindoc/notectx.md`
- **Summary:** Added a Bash launcher that starts `npm run dev` and the Waitress HOST API as separate processes, forwards their output, and stops both when either exits or the launcher receives Ctrl-C.
- **Verification:** `bash -n mentos/run` passed. `timeout --preserve-status --kill-after=3s --signal=INT 8s ./mentos/run` started Vite and the HOST API, sent an interrupt, and confirmed both ports were released afterward.
- **Defaults / unverified:** Uses Vite's default port `5173` and HOST port `8000`. Requires frontend dependencies from `npm install` and API dependencies from `uv sync --project api`. This combined local launcher does not gate API startup on DBSERV readiness.

## 2026-11-09 — Document the public DBSERV Funnel

- **Files changed:** `mentos/api/.env.example`, `mentos/api/README.md`, `mentos/api/utils/dbflow.md`, `bindoc/filels.md`, `bindoc/maindoc/notes/auth-contract.md`, `bindoc/maindoc/notectx.md`
- **Summary:** Documented the DBSERV origin as `https://palsys.softshell-grue.ts.net` and noted that the Funnel is public while DBSERV authentication routes require HOST JWTs.
- **Verification:** Checked that active setup docs and the example use the new origin; `git diff --check` passed.
- **Defaults / unverified:** Real `.env` files and Funnel configuration were not changed or inspected; public DNS and route availability remain unverified.

## 2026-11-09 — Separate HOST and DBSERV public URIs

- **Files changed:** `mentos/api/.env.example`, `mentos/api/README.md`, `mentos/api/utils/dbflow.md`, `bindoc/filels.md`, `bindoc/maindoc/notes/auth-contract.md`
- **Summary:** Set the HOST-to-DBSERV origin to `https://palsys.softshell-grue.ts.net`; documented `https://reime.softshell-grue.ts.net` as the browser-facing HOST callback origin. HOST calls DBSERV auth APIs over its signed server-to-server channel.
- **Verification:** Searched HOST examples and API docs for endpoint references and confirmed `git diff --check`.
- **Defaults / unverified:** Only examples and documentation changed. The real DBSERV URL and availability of both public routes were not verified or modified.

## 2026-11-09 — Use solaris as the HOST callback host

- **Files changed:** `bindoc/maindoc/notectx.md`, `bindoc/maindoc/notes/auth-contract.md`
- **Summary:** Replaced the previous `reime` HOST hostname with `solaris.softshell-grue.ts.net` for the laptop-hosted Mentos origin and Google callback. Kept the DBSERV server-to-server origin at `https://palsys.softshell-grue.ts.net`.
- **Verification:** Searched active HOST setup and API contract docs for the old hostname; `git diff --check` passed.
- **Defaults / unverified:** Documentation only; the laptop's actual DNS/Tailscale routing, HTTPS, and Google redirect registration are not configured or verified. Google remains disabled until HTTPS is available.
