# apmain API server change log

## 2026-11-02 — Bind the API to loopback by default

- **Files changed:** `server/database/src/config.js`, `server/src/server.js`, `server/database/.env.example`, `server/README.md`, `server/database/test/server-binding.test.js`
- **Summary:** Added a configurable `HOST` setting and made the server bind to `127.0.0.1` unless overridden. Documented the setting and added integration coverage for the default and an explicit loopback address.
- **Verification:** `cd server && npm test` — 7 tests passed.
- **Defaults / unverified:** Default bind address is `127.0.0.1`. Production reverse-proxy routing has not been exercised.

## 2026-11-02 — Rate-limit APIs and split liveness from database readiness

- **Files changed:** `server/package.json`, `server/package-lock.json`, `server/src/app.js`, `server/src/runner.js`, `server/database/src/routes.js`, `server/database/test/api-prefixes.test.js`, `server/database/test/routes.test.js`, `server/database/test/rate-limits.test.js`, `server/README.md`, `server/API.md`
- **Summary:** Added general and stricter database IP rate limits; trusted only loopback reverse proxies for forwarded client IPs. Made `/db/health` a no-query liveness endpoint, added JWT-protected `/db/health/database`, and had the runner check MariaDB directly using its local pool. Documented Tailscale Serve (tailnet-only) and Funnel (public) as unconfigured options.
- **Verification:** `cd server && npm test` — 9 tests passed, including independent forwarded-IP buckets, both limit thresholds, and unauthenticated versus authenticated database health.
- **Defaults / unverified:** `express-rate-limit@8.7.2`; 120 API requests and 60 `/db` requests per IP per 15 minutes; in-memory counters reset with the process. Express trusts `X-Forwarded-For` only from loopback. Tailscale documents client-IP forwarding over PROXY protocol for TCP, which this HTTP listener does not parse; actual `X-Forwarded-For` behavior of either Tailscale HTTP proxy mode is unverified. No Tailscale configuration was run.

## 2026-11-02 — Extract shared server core

- **Files changed:** `server/package.json`, `server/package-lock.json`, `server/shared/package.json`, `server/shared/src/config.js`, `server/shared/src/db.js`, `server/database/package.json`, `server/database/src/config.js` (moved to shared), `server/database/src/db.js`, `server/database/src/auth.js` (renamed to `server/database/src/hostauth.js`), `server/database/src/routes.js`, `server/src/server.js`, `server/src/runner.js`, `server/database/test/shared-core.test.js`
- **Summary:** Moved configuration and the MariaDB pool into `@mentos/shared-core`; retained database-specific table/query helpers in the database router workspace. Updated imports and workspace dependencies, and renamed JWT verification middleware to distinguish it from the authentication API.
- **Verification:** `cd server && npm install` refreshed the workspace lockfile; `cd server && npm test` — 10 tests passed, including a new assertion that database helpers share the pool exported by the shared core.
- **Defaults / unverified:** The shared config resolves `.env` and trusted-key paths relative to `server/database/`, preserving the prior locations. Actual `.env` contents and key files were intentionally not read; key generation and setup scripts were not run because they can create or modify protected config/key files.

## 2026-11-02 — Start the service directly and harden runner exits

- **Files changed:** `server/palsys.service`, `server/src/runner.js`, `server/src/runner-utils.js`, `server/database/test/runner.test.js`, `server/README.md`
- **Summary:** Changed systemd to start `src/server.js` directly, order after MariaDB without requiring it, and removed the runner-only health URL setting. Simplified runner health checks, retained the configured-port URL derivation, and made every unexpected child exit—including exit code 0—set a failing process exit status.
- **Verification:** `cd server && npm test` — 12 tests passed, including helper tests for port-derived URLs and unexpected exit codes. `systemd-analyze verify palsys.service` was attempted with installed systemd 262.
- **Defaults / unverified:** The deployment Node path remains `/usr/local/bin/node`. Local systemd verification could not complete because that path is absent on this workstation; the path is expected on the target server. The unit directives `Wants=`, `After=`, and `Requires=` were checked against the installed systemd manual; target-host verification remains unverified.

## 2026-11-02 — Bound DB reads and harden error/key handling

- **Files changed:** `server/shared/src/db.js`, `server/database/src/hostauth.js`, `server/database/src/routes.js`, `server/src/app.js`, `server/database/test/routes.test.js`, `server/database/test/shared-core.test.js`, `server/database/test/api-error-handler.test.js`, `server/database/README.md`, `server/API.md`
- **Summary:** Added parameterized list pagination with input validation; cached trusted host public keys with expiry and automatic refresh; mapped common 4xx errors to status-appropriate codes; and deferred MariaDB pool creation until after `validateConfig()` succeeds. Updated the relevant DB/API documentation.
- **Verification:** `cd server && npm test` — 15 tests passed, including default/maximum pagination validation, key rotation after cache expiry, common error status mapping, oversized JSON handling, and config-before-pool validation.
- **Defaults / unverified:** List page default is 100, maximum `limit` is 500, and default `offset` is 0; trusted key cache TTL is 5 seconds. SQL pagination was verified through query construction and mock-backed route tests, not against a live MariaDB instance. No `.env`, keys, or schema files were read.

## 2026-11-02 — Separate failed-IP and authenticated DBSERV rate limits

- **Files changed:** `server/src/app.js`, `server/database/src/hostauth.js`, `server/database/src/routes.js`, `server/database/test/rate-limits.test.js`
- **Summary:** The `/db` IP counter now skips successful responses, while invalid/unauthenticated requests remain subject to the 60-per-15-minute IP limit. After JWT verification, requests also use a separate counter keyed by the verified JWT `kid`.
- **Verification:** `cd server && npm test` — 15 tests passed, including failed-request IP limiting, successful response exclusion, per-key quota enforcement, and isolation between key IDs.
- **Defaults / unverified:** Authenticated quota is 3,000 requests per JWT `kid` per 15 minutes; counters use the existing in-memory store and reset on process restart. The HOST signing key is treated as one shared trusted caller identity; client-specific limits belong on HOST.

## 2026-11-02 — Rename the DBSERV bind setting

- **Files changed:** `server/shared/src/config.js`, `server/database/.env.example`, `server/README.md`, `server/database/README.md`, `server/database/test/server-binding.test.js`
- **Summary:** Renamed the bind-address setting to `BIND_ADDRESS` and retained `HOST` as a deprecated, warning-producing fallback. Updated the example and operational docs without accessing the real environment file.
- **Verification:** `cd server && node --test database/test/server-binding.test.js database/test/shared-core.test.js` — 4 tests passed, including the deprecated fallback warning.
- **Defaults / unverified:** `BIND_ADDRESS` defaults to `127.0.0.1`. The target host's actual environment configuration was not inspected.

## 2026-11-02 — Use UTC for MariaDB date conversion

- **Files changed:** `server/shared/src/config.js`, `server/shared/src/db.js`, `server/database/test/shared-core.test.js`, `server/database/README.md`
- **Summary:** The installed mysql2 pool previously used its documented local-time default, while HOST writes session expiries as UTC timestamps. Configured the shared pool to use mysql2's UTC timezone value and documented the alignment.
- **Verification:** `cd server && node --test database/test/shared-core.test.js` — 2 tests passed; mysql2 3.24.6 runtime accepted `timezone: "Z"`. The DB was not contacted.
- **Defaults / unverified:** Pool timezone is fixed to UTC (`Z`); actual DBSERV process timezone and MariaDB data were not inspected.

## 2026-11-02 — Align DBSERV rate-limit documentation

- **Files changed:** `server/README.md`, `server/API.md`, `bindoc/filels.md`
- **Summary:** Updated the API guidance and machine map for the failure-only `/db` IP counter, verified-key quota, and `BIND_ADDRESS` name.
- **Verification:** `cd server && npm test` — 16 tests passed; `git diff --check` passed.
- **Defaults / unverified:** No runtime behavior or defaults changed in this documentation-only alignment.

## 2026-11-03 — Run DBSERV on the requested loopback listener

- **Files changed:** `server/run`, `server/src/runner.js`, `server/src/runner-utils.js`, `server/shared/src/config.js`, `server/palsys.service`, `server/database/test/runner.test.js`, `server/README.md`, `bindoc/filels.md`
- **Summary:** Root `run` now delegates to `src/run` and its runner. The service sets `BIND_ADDRESS=127.0.0.7` and `PORT=8432`; the runner probes the configured host and port. Synced the launcher, unit, runner, and BIND_ADDRESS config to apmain, then reloaded and restarted `palsys.service`.
- **Verification:** Runner and binding tests passed locally (4 tests). Remote `systemd-analyze verify` completed with warnings only for unrelated Tailscale and snapd unit files. `palsys.service` is active; `ss` shows a listener on `127.0.0.7:8432`; `curl http://127.0.0.7:8432/db/health` returned HTTP 200 with database liveness JSON.
- **Defaults / unverified:** Listener is loopback-only at `127.0.0.7:8432`. The authenticated database-readiness endpoint was not queried; Serve/Tailscale routing was not tested or changed.

## 2026-11-03 — Use the primary loopback address

- **Files changed:** `server/palsys.service`, `server/README.md`
- **Summary:** Changed the service bind address from `127.0.0.7` to `127.0.0.1`, preserving port 8432. The prior address was an unnecessary loopback alias.
- **Verification:** Synced the service unit to apmain, installed it, ran `systemd-analyze verify`, reloaded systemd and restarted `palsys.service`. The service is active; `ss` shows `127.0.0.1:8432`; curl to `/db/health` returned HTTP 200, while the old `127.0.0.7:8432` address refused the connection.
- **Defaults / unverified:** Service remains bound to loopback only; Serve routing is not changed.

## 2026-11-03 — Add authentication persistence schema

- **Files changed:** `server/database/schemas/schema_v3_auth.sql`, `server/database/.env.example`, `server/database/test/auth-schema.test.js`
- **Summary:** Added provider+subject identities, separate admin credential storage, minimal auth-attempt auditing with truncated-IP storage, nullable legacy external subjects, non-unique user emails, and student/teacher roles. Documented the three auth resources that must be allowlisted.
- **Verification:** `cd server && npm test` — 18 tests passed, including schema/allowlist assertions. The migration was not run.
- **Defaults / unverified:** The HOST auth layer will write IPv4 /24 or IPv6 /64 prefixes. The migration assumes the exact v1 index names and has not been applied against a live MariaDB schema.

## 2026-11-09 — Add protected provider and admin authentication routes

- **Files changed:** `server/authentication/src/providers.js`, `server/authentication/src/admins.js`, `server/authentication/src/provider-config.js`, `server/authentication/src/auth-audit.js`, `server/authentication/src/routes.js`, `server/authentication/package.json`, `server/authentication/.provider.env` (ignored, empty values, mode `0600`), `server/authentication/.provider.env.example`, `server/authentication/.gitignore`, `server/.gitignore`, `server/database/test/authentication-pipes.test.js`, `server/database/test/api-prefixes.test.js`, `server/package-lock.json`, `server/API.md`, `server/README.md`, `server/implementation/authentication/IMPLEMENTATION.md`
- **Summary:** Added a HOST-JWT-scoped Google OAuth code exchange with PKCE and ID-token verification, plus a separate admin-login router with sanitized inputs, parameterized SQL, scrypt verification, generic failures, active/admin-role checks, IP and username limits, and minimal auth-attempt logging. Added an ignored local `.provider.env` with empty settings and enforced regular-file, no-symlink, owner-only permissions.
- **Verification:** `cd server && npm test` — 31 tests passed, including mocked exchange, local-key ID-token signature/claims checks, redirect allowlisting, scrypt compatibility, admin authorization, and throttling. `node --check` passed for changed JS files. `git check-ignore -v server/authentication/.provider.env` confirmed ignore; filesystem mode is `600`. `git diff --check` passed.
- **Defaults / unverified:** Provider configuration uses `TMP_GCLOUD_CLIENTID`, `TMP_GCLOUD_CLIENTKEY`, and HTTPS `GOOGLE_REDIRECT_URIS`; absent credentials disable the exchange. Google/network calls and schema migration were not run. HOST-side signing/calling of `auth:provider` and `auth:admin` scopes remains to be wired. No credential values are included in source or documentation.

## 2026-11-09 — Publish scoped Google provider settings to HOST

- **Files changed:** `server/authentication/src/providers.js`, `server/authentication/src/provider-config.js`, `server/database/test/authentication-pipes.test.js`, `server/API.md`, `server/implementation/authentication/IMPLEMENTATION.md`, `bindoc/maindoc/notes/changes-auth-apmain.md`
- **Summary:** Added HOST-JWT-protected `GET /auth/v1/providers/google/config` with a distinct `provider_config` action. It returns only the public client ID, fixed authorization endpoint, scopes, and exact redirect allowlist; incomplete settings explicitly report Google disabled.
- **Verification:** `cd server && npm test` — 33 tests passed; `node --check` passed for the modified provider modules. Tests use mock config and no provider network calls.
- **Defaults / unverified:** Redirect URIs remain HTTPS-only and exact-match allowlisted. No real credentials were read, set, or used; the schema migration remains unapplied.

## 2026-11-09 — Deploy authentication service updates to apmain

- **Files changed:** `server/` source, tests, package lock, and documentation were synchronized to `/opt/palsys/server`; `palsys.service` was restarted.
- **Summary:** Deployed the current DBSERV tree without deleting remote files. The sync excluded `.env` files, provider config, keys, schemas, logs, and `node_modules`; installed locked production dependencies and restarted the service.
- **Verification:** Remote `palsys.service` reports active; `http://127.0.0.1:8432/db/health` returned `{"service":"database","status":"alive"}`. Remote Node is v20.20.2 and npm is v10.8.2. The deployment included `GET /auth/v1/providers/google/config`; the schema migration was not synced or run.
- **Defaults / unverified:** Google remains disabled pending HTTPS HOST callback setup and provider configuration. Tailscale/Serve routing and real provider sign-in were not tested.

## 2026-11-09 — Gate all authentication API paths

- **Files changed:** `server/authentication/src/routes.js`, `server/authentication/src/providers.js`, `server/authentication/src/admins.js`, `server/database/test/api-prefixes.test.js`, `server/src/runner.js`, `server/database/test/runner.test.js`, `server/API.md`
- **Summary:** Added a HOST JWT gate before all `/auth/v1` routes, including health and unknown paths. Missing or malformed credentials now receive `403 forbidden`; existing provider/admin capability checks remain in place. Nested auth routers reuse the already verified request claims. Removed the public authentication health probe from the runner so its startup check does not treat the intentional 403 as a service failure.
- **Verification:** `cd server && npm test` — 35 tests passed, including anonymous/malformed-header requests to health, provider config, admin login, and unknown routes, and runner coverage for excluding the protected health route.
- **Defaults / unverified:** Unauthenticated requests are deliberately represented as `403 forbidden` per request; other DBSERV API prefixes retain their existing auth responses.

## 2026-11-09 — Document the HOST DBSERV public origin

- **Files changed:** `mentos/api/.env.example`, `mentos/api/README.md`, `mentos/api/utils/dbflow.md`, `bindoc/filels.md`, `bindoc/maindoc/notes/auth-contract.md`, `server/API.md`
- **Summary:** Changed the HOST example origin to `https://palsys.softshell-grue.ts.net` and documented it as a public Funnel address. Clarified that `/auth/v1` remains protected by HOST JWT verification.
- **Verification:** Searched current example and setup documentation for the previous origin; `git diff --check` passed.
- **Defaults / unverified:** Updated only the example and documentation, not any real `.env` or Funnel configuration. Public DNS/Funnel reachability is not verified.

## 2026-11-09 — Deploy auth-prefix protection

- **Files changed:** `server/authentication/src/routes.js`, `server/authentication/src/providers.js`, `server/authentication/src/admins.js`, `server/src/runner.js`, `server/database/test/api-prefixes.test.js`, `server/database/test/runner.test.js`, `server/API.md`, `server/implementation/authentication/IMPLEMENTATION.md`
- **Summary:** Synced the HOST-JWT gate for every `/auth/v1` route to `/opt/palsys/server`, preserved remote environment/configuration, keys, schemas, and dependencies, then restarted `palsys.service`. Removed the now-protected auth health route from the unauthenticated runner probes.
- **Verification:** `cd server && npm test` — 35 tests passed. On apmain, `palsys.service` is active, `/db/health` returns alive, and unauthenticated requests to `/auth/v1/health`, provider config, admin login, and an unknown auth path each return HTTP 403 with `forbidden`.
- **Defaults / unverified:** At deployment time, `https://apmain.softshell-grue.ts.net` refused HTTPS connections from this environment (curl connection failure). Tailscale Funnel was not configured or changed. Real `.env` files were not accessed.

## 2026-11-09 — Correct DBSERV public hostname

- **Files changed:** `mentos/api/.env.example`, `mentos/api/README.md`, `mentos/api/utils/dbflow.md`, `bindoc/filels.md`, `bindoc/maindoc/notes/auth-contract.md`, `server/API.md`
- **Summary:** Clarified that Mentos uses `https://palsys.softshell-grue.ts.net` as `DBSERV_URL` for server-to-server DB and auth API calls. The browser-facing HOST callback remains on `https://reime.softshell-grue.ts.net`.
- **Verification:** Searched the active setup and contract docs for the old HOST/DBSERV hostname mix-up; `git diff --check` passed.
- **Defaults / unverified:** This changes examples and documentation only. No real HOST `.env`, Funnel setting, or remote service configuration was changed or inspected.

## 2026-11-15 — Deploy developer API interface to apmain

- **Files synchronized:** Local `server/` directory to `root@apmain:/opt/palsys/server`, including the developer interface under `server/public`, Vite configuration, package metadata, authentication policy changes, and launcher updates. Local `node_modules`, environment files, provider configuration, and key directories were excluded; remote files were not deleted.
- **Launcher permissions:** Marked `server/run` and `server/src/run` executable so the existing `palsys.service` unit can spawn the runner.
- **Verification:** Restarted `palsys.service`; it is active with the runner and API child process. Remote `http://127.0.0.1:8432/db/health` returned `{"service":"database","status":"alive"}`.

## 2026-11-15 — Set developer interface port to 5060

- **Files changed:** `server/vite.config.js`
- **Summary:** Changed the developer Vite interface port from 5174 to 5060, synchronized the `server/` directory to apmain, and restarted `palsys.service`. The REST API systemd listener remains on 8432; the current Tailscale Serve mapping continues to proxy the tailnet hostname to 8432.
- **Verification:** The remote service is active, local API health returned alive, and `https://palsys.softshell-grue.ts.net/db/health` returned HTTP 200 with `{"service":"database","status":"alive"}`. The hostname root returns the expected API `not_found` response because no root REST route exists.

## 2026-11-15 — Document runner entrypoint and service ports

- **Files changed:** `server/src/runner.js`, `bindoc/maindoc/ports.md`
- **Summary:** Documented that the runner starts `src/server.js`, which imports and binds the Express app exported by `src/app.js`; `app.js` is not launched as a second process. Added the server and Mentos API/web application port table.
- **Verification:** `node --check server/src/runner.js` passed and the full server suite passed 36 tests. Synchronized `server/` to apmain, restarted `palsys.service`, confirmed it active with zero restarts, and verified both local and tailnet `/db/health` responses returned `{"service":"database","status":"alive"}`.

## 2026-11-15 — Run DBSERV API and developer Vite together

- **Files changed:** `server/run`, `server/package.json`, `server/vite.config.js`
- **Summary:** `server/run` now directly supervises `node src/runner.js` and `npm run dev`; `npm run dev` runs Vite on port 5060 and `dev:api` preserves the API watch command. Vite uses the runner config loader to avoid the read-only systemd `node_modules/.vite-temp` path and allows the APMAIN tailnet hostname.
- **Verification:** Synchronized `server/` and installed the locked Vite dependency on apmain. `palsys.service` is active with zero restarts; listeners are present on `127.0.0.1:5060` and `127.0.0.1:8432`; the APMAIN UI hostname returns HTTP 200 and the API health endpoint returns alive.

## 2026-11-15 — Run the developer UI as a separate systemd service

- **Files changed:** `server/palsys-web.service`, `server/vite.config.js`
- **Summary:** Added `palsys-web.service` for the Vite developer interface on port 5060, separate from the API-only `palsys.service`. The web service proxies API requests to APMAIN's 8432 listener through `DEV_API_TARGET`, while local Vite development continues to default to 8100.
- **Verification:** Installed and enabled the unit on apmain. Both `palsys.service` and `palsys-web.service` are active with zero web-service restarts; port 5060 serves the login page, `/db/health` proxies successfully, and `https://apmain.softshell-grue.ts.net/` returns HTTP 200.

## 2026-11-15 — Deploy developer login and webapp modules

- **Files synchronized:** Current `server/` source, public login UI, developer authentication route, telemetry/user-management modules, Vite configuration, and `palsys-web.service`. Environment files, keys, and dependency trees were excluded.
- **Summary:** Deployed the server-side `/dev/login` flow and `@pals.me` login allowance, along with the separated API/web runtime changes. Existing apmain environment configuration was preserved.
- **Verification:** Restarted `palsys.service` and `palsys-web.service`; both are active. API `/db/health`, the Vite UI on port 5060, and the APMAIN tailnet UI endpoint were verified after synchronization.
## 2026-11-17 — Deploy .NET DBSERV and feature façade

- **Deployment:** Synchronized the current `server/` source and self-contained `linux-x64` .NET publish to `/opt/palsys/server`, excluding remote environment files, keys, dependency trees, and storage.
- **Runtime:** Replaced the old Node `palsys.service` command with the self-contained ASP.NET Core DBSERV binary on `127.0.0.1:8432`. Installed and enabled the new unit; removed the obsolete `palsys-web.service` deployment. `palsys.service` is active.
- **Security:** Configured persistent Data Protection keys under the preserved remote storage path and retained the hardened service restrictions. No environment contents or credentials were printed.
- **Verification:** Remote `/db/health` returned HTTP 200; unauthenticated `/dev/session` returned HTTP 401; unauthenticated `/internal/v1/tutors` returned HTTP 403. No public Vite web unit is active on apmain.
- **Database blocker:** Applying `schema_v5_identity.sql` was rejected by MariaDB: the configured `dbpals` account lacks `CREATE` permission on `palsysdb.__EFMigrationsHistory`. The v5 and v6 schemas were not applied, so Identity and feature data routes require a DBA grant and a later migration run.
