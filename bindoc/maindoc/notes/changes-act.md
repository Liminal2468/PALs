# ACT workspace change log

## 2026-11-17 — Add persistent sessions, admin login, dashboard, and schedule

- **Persistence:** Added `server/database/schema/schema_v7_sessions_schedule.sql` with `web_sessions` and `schedule_events`. Schedule API fields are `eventId`, `onDate`, `afterDate`, `eventName`, `description`, and `isActive`; date input accepts `MM-DD-YY:HH-mm` and ISO forms and is stored as UTC.
- **Authentication:** Replaced the Mentos in-memory session token with a DBSERV-backed opaque session key. Added internal login, session, logout, and admin-login routes. Mentos now forwards the session key to DBSERV for every protected data request.
- **Features:** Added dashboard statistics, schedule CRUD, admin-only schedule mutation, and the canonical `/api/v1/rec-peer-tutor` pending route while retaining the longer recommendation aliases.
- **Frontend:** Connected home statistics to `/api/v1/dashboard`; existing live page hydration now uses the persistent session path.
- **Scope:** This pass is local-only. No apmain synchronization, service restart, cloudflared check, or tailnet check was performed.
- **Verification:** Both .NET projects build with zero warnings/errors and the live frontend scripts pass syntax checks. Database migrations remain unapplied until the database administrator grants the required schema privileges.

## 2026-11-17 — Connect Mentos feature routes to DBSERV

- **Schema:** Added `server/database/schema/schema_v6_frontend_features.sql` for resources, resource audits, feedback posts, votes, and support tickets. The existing learning-request, session, session-feedback, and consent-aware analytics tables are reused.
- **DBSERV:** Added `server/Data/PalsDataStore.cs` and `server/Controllers/DataController.cs` for account provisioning, profiles, users, tutors, tutees, learning requests, sessions, resources, feedback, support, and analytics. Requests require the private `HOST_SHARED_KEY` and an active HOST user identity; ownership and privileged-role checks are applied in the controller.
- **HOST:** Replaced the in-memory data responses in `mentos/Controllers/WebendController.cs` with a private DBSERV façade using `DBSERV_URL` and `HOST_SHARED_KEY`. Added routes for profiles, tutors/tutees, learning requests, sessions, resources, feedback, support, and analytics.
- **Frontend:** Added `mentos/public/scripts/live.js`, connected to the existing PALs page shell through `pals:page-loaded`, and wired live account hydration, profile updates, tutor/tutee/session/resource/feedback/analytics loading, learning requests, feedback votes, and support submissions. The prior demo handlers remain in the legacy script but are intercepted for live forms and live-rendered data.
- **Deferred:** `/connect` and tutor recommendations intentionally return `501 connect_not_implemented`; no matching algorithm or fabricated recommendation data was added.
- **Configuration:** Added `HOST_SHARED_KEY` examples and expanded the database allowlist for the new feature tables. Passwords and real key values remain excluded.
- **Verification:** `dotnet build server/Palsys.Server.csproj`, `dotnet build mentos/Mentos.Host.csproj`, and JavaScript syntax checks passed. No schema was applied to a live database and no remote deployment was performed.

## 2026-11-17 — Verify and deploy phases 6–7

- **Testing:** Both .NET projects build with zero warnings/errors; live JavaScript syntax checks pass. Local HOST smoke checks confirmed anonymous `/pals.html` protection (`302`), invalid school-account rejection (`401`), and sanitized DBSERV-unavailable handling (`502`).
- **Deployment:** Published DBSERV self-contained for `linux-x64`, synchronized it to apmain, replaced the obsolete Node service command, and restarted `palsys.service`. The service is active on `127.0.0.1:8432`; the obsolete web unit is inactive.
- **Blocker:** Remote MariaDB rejected the reviewed v5 schema because `dbpals` lacks `CREATE` permission on `palsysdb`. No v5 or v6 database changes were applied. The deployed feature controllers are present but remain unavailable until the migration is run with a permitted database administrator account.

## 2026-11-17 — Add ASP.NET Identity persistence foundation

- **Files added:** `server/Identity/PalIdentityUser.cs`, `server/Identity/PalIdentityDbContext.cs`, `server/Identity/IdentityConnection.cs`, `server/Identity/DeveloperIdentitySeed.cs`, `server/Identity/Migrations/`, `server/database/schema/schema_v5_identity.sql`.
- **Files updated:** `server/Program.cs`, `server/Controllers/DeveloperController.cs`, `server/Identity/DeveloperIdentity.cs`, `server/Palsys.Server.csproj`, `server/README.md`, `bindoc/filels.md`.
- **Summary:** Added ASP.NET Core Identity with EF Core and the .NET 10 MySQL provider. Identity uses separate `identity_*` tables and links to the existing PALs `users` table through `pal_user_id`, preserving the existing PALs account and role structures. Developer login now verifies credentials through `UserManager` and issues the ASP.NET Identity application cookie instead of an in-memory session token.
- **Seeding:** Added the interactive `--seed-developer email role` command. It creates or updates the linked PALs account, Identity password hash, Identity role, and PALs role assignment without placing passwords in SQL or environment files.
- **Migration safety:** Generated EF migration files and a reviewed MariaDB script. The schema script was not applied to any database. No production deployment or remote service restart was performed.
- **Verification:** `dotnet build server/Palsys.Server.csproj` passed with zero warnings and errors. Local server smoke checks returned `200` for `/db/health` and `401` for unauthenticated `/dev/session`.

## 2026-11-15 — Install the updated public interface and local tunnel checks

- **Frontend:** Copied the updated PALs interface from `bindoc/corefile/PALs/PALs/` into `mentos/public/`, using `index.html` as the `Login.html` landing page. Lowercased copied filenames and all copied HTML asset references. Corrected the login script element to use `src` and the new `scripts/` path.
- **FAQ assets:** Extracted the FAQ page's embedded stylesheet and script into `mentos/public/styles/faq.css` and `mentos/public/scripts/faq.js`, then linked them from `faqs.html`.
- **Vite:** Added `pals.noteriety.top` to the explicit allowed-host list and added the copied public HTML pages to the MPA build inputs.
- **Launcher:** Updated `mentos/run` to inspect `cloudflared.service` before starting Vite on port 5000. An active service uses a wildcard bind for the tunnel; an inactive or unavailable service uses loopback. The existing permission check remains before dependency startup.
- **Runner cleanup:** Deleted only the legacy Mentos files `bin/run`, `bin/run.sh`, `bin/run.ps1`, `bin/run.bin`, and `bin/runner.js`. They were documentation-connected API-only launchers that performed DBSERV readiness checks and started the HOST API; they were not imported by application code. Current setup documentation and the file map were updated accordingly. Server-side `server/src/runner.js` and its platform wrappers were not changed.
- **Auth/account audit:** No login implementation was added in this change. Mentos has provider/session/admin plumbing, while the public page still presents Entra/admin UI behavior. The checked-in database contains account/auth table schemas and roles but no account records or database file to inspect; live account existence remains unverified. `server/authentication` currently implements Google provider exchange/configuration and admin routes, not a PALs-owned authorization server.
- **Verification:** Checked public filename/reference casing, verified FAQ embedded blocks are externalized, ran `bash -n mentos/run mentos/bin/permchk`, ran `node --check mentos/vite.config.js` and the copied scripts, and completed `npm run build --prefix mentos`. `git diff --check` reports CRLF/trailing-whitespace lines inherited from the supplied interface files, so those files were not reformatted. No `mentos/public/` styles from the previous interface were intentionally retained under uppercase names.

## 2026-11-15 — Restrict user provider identities to organization accounts

- **Files changed:** `server/authentication/src/account-policy.js`, `server/authentication/src/providers.js`, `server/database/test/authentication-pipes.test.js`, `server/authentication/test/account-policy.test.js`, `bindoc/maindoc/notes/auth-contract.md`
- **Summary:** Added the shared user-account policy requiring `LASTNAME.STUID@cubao.sti.edu.ph`, with a six-digit minimum STUID. Provider identity exchange now returns `401 account_not_recognized` to the HOST and records the sanitized failure reason before any identity can be accepted. The separate admin login route is unchanged.
- **Schema audit:** `server/database` already has `users.email`, `auth_identities`, roles, admin credentials, and audit tables. No local account rows or checked-in database file were present; no schema migration was needed for this validation-only boundary change. A local PALs credential table will be a separate OAuth authorization-server decision.
- **Verification:** Added policy and REST exchange tests covering valid organization accounts, personal accounts, malformed accounts, and five-digit STUIDs; the focused authentication run passed all 18 tests. The full `server/database` suite still has unrelated workspace failures because `server/node_modules/@mentos/database/src/hostauth.js` is missing, so its affected integration tests cannot start.

## 2026-11-15 — Copy the permission helper to DBSERV

- **Files changed:** `server/src/permchk`
- **Summary:** Copied the existing Mentos permission helper into `server/src/permchk` and made it executable. The DBSERV runner audit confirmed that `server/src/runner.js` already performs asynchronous health checks for `/db/health`, `/acc/health`, `/sched/health`, `/fb/health`, `/adata/health`, `/connect/health`, and `/chat/v1/health`, followed by a database `SELECT 1` check.
- **Runner status:** Kept `server/run`, `server/src/run`, `run.sh`, `run.ps1`, and `run.bin`; they are platform wrappers for the shared `runner.js`, not separate health-check implementations.
- **Clarification:** `server/database/src/hostauth.js` exists. The earlier full-suite failure comes from the installed workspace package namespace/link: authentication imports `../../database/src/hostauth.js` from `@mentos/database`, while the declared workspace package is `@mentos/database-api`. No source file was deleted.

## 2026-11-15 — Add the developer API interface to DBSERV

- **Developer UI:** Copied the existing `mentos/public/` interface into `server/public/` without changing Mentos styles. The server copy retains the existing shell and components, but its sidebar now contains only Dashboard, Database, and Resources.
- **Login:** Reduced the server copy's landing entry to the existing login dialog and developer/admin form. The student login form, Entra references, terms dialog, and landing content are not present in `server/public/index.html`.
- **Vite:** Added `server/vite.config.js`, `dev:ui`, and `build:ui`. Vite's root is `server/public`, `publicDir` is disabled, filesystem strict mode allows only that directory, and only existing API prefixes proxy to the Express API. Express was not changed to serve static files and account configuration was not changed.
- **Verification:** `node --check` passed for the server login script and Vite configuration. `npm run build:ui --prefix server` completed successfully. The three server sidebar entries and absence of student/Entra login remnants were audited with `rg`.
- **Boundary note:** Any file intentionally delivered by a browser over HTTP can also be requested by `curl`; this setup prevents Vite from exposing files outside `server/public` and prevents Express from serving source files, but it cannot make an HTTP-public asset inaccessible to HTTP clients.

## 2026-11-15 — Start the developer API UI from the DBSERV launcher

- **Summary:** `server/run` now starts the API runner and the server-side Vite UI together. The `server` `dev` script runs Vite on port 5060, while `dev:api` retains the API watch command. Vite's runtime config loader avoids systemd's read-only `node_modules` cache path and permits the APMAIN tailnet hostname.
- **Deployment:** Synchronized `server/` to apmain and restarted `palsys.service`. Both API and Vite listeners were verified, and `https://apmain.softshell-grue.ts.net/` returned HTTP 200.

## 2026-11-15 — Create local developer API credential placeholders

- **Files changed:** `server/database/.env`, `server/database/.env.example`
- **Summary:** Added ignored, mode-600 local environment placeholders for `reime@pals.me` and `indev.sys@pals.me`, with separate empty password variables. No password values were added and the local `.env` was not deployed.

## 2026-11-15 — Allow PALs developer email domain in API login

- **Files changed:** `server/public/scripts/login.js`, `server/public/index.html`
- **Summary:** Added `@pals.me` as an allowed API developer-login email domain alongside `@cubao.sti.edu.ph`. No environment values were printed or synchronized.
- **Deployment:** Synchronized the updated server UI and restarted `palsys.service`. The UI files are updated remotely; the API service still cannot remain active until the missing production database environment variables are restored.

## 2026-11-15 — Separate API runner from developer Vite and add webapp modules

- **Diagnosis:** The refresh loop came from `server/run` supervising both Vite and the API. When the API exited because production database configuration was missing, the launcher terminated Vite and systemd restarted both processes.
- **Runtime:** `server/run` now executes only `node src/runner.js`. Vite remains a separate `npm run dev` process on port 5060.
- **Developer backend:** Added `server/src/webapp/module/` with developer login, telemetry, request telemetry, database health/read/write/edit/update helpers, user history editing, tutor/tutee resource auditing, and match recording modules. Database mutation helpers require an injected pool and explicit allowlisted tables; user history is written to `server/.history` only when an edit is invoked.
- **Verification:** `bash -n server/run` and `node --check` passed for all new modules. No deployment or service restart was performed because apmain's production database environment is still incomplete.

## 2026-11-15 — Wire developer login to the server authentication module

- **Files changed:** `server/public/scripts/login.js`, `server/src/app.js`, `server/src/webapp/module/dev.login.js`, `server/vite.config.js`
- **Summary:** The API login form now submits to `/dev/login`; the server validates the configured `@pals.me` developer accounts using environment-backed passwords and redirects successful sessions to `pals.html`. Vite proxies `/dev` to the API.
- **Verification:** JavaScript syntax checks passed. A local endpoint check accepted a configured test account and rejected an incorrect password. No real password values were printed or used in testing. Deployment remains blocked by missing production database variables on apmain.

## 2026-11-15 — Build the API telemetry dashboard from existing PALs components

- **Files changed:** `server/public/pages/analytics.html`, `server/public/components/telemetry/`, `server/public/styles/pals.css`, `server/public/scripts/pals.js`
- **Summary:** Replaced the analytics placeholder with the existing home-page `stats-row`, `stat`, `home-section`, and `section-header` patterns. Added reusable metric-card, database-health, and request graph-table fragments under `server/public/components/telemetry`.
- **Behavior:** The dashboard loads the reusable fragments, checks `/db/health`, and displays live database status. Read/write/request graph values remain explicitly pending until the server telemetry middleware is connected to request handling.
- **Verification:** `node --check server/public/scripts/pals.js` passed and `npm run build:ui --prefix server` completed successfully.
## 2026-11-17 — Add YAML application configuration

- **Configuration:** Added `server/config.example.yaml` and
  `mentos/config.example.yaml` for non-secret application settings. Real
  `config.yaml` files are ignored by Git.
- **Secrets:** `DB_PASSWORD` is deliberately ignored by the server YAML
  loader and remains in `server/.env` or the process environment. Shared keys
  may be placed in the local Mentos YAML file but must not be committed.
- **Behavior:** Blank or omitted YAML values are ignored. Explicit process
  environment values take precedence; non-empty YAML values override legacy
  dotenv fallbacks. Both .NET applications now load YAML before startup.
- **Dependency:** Added `YamlDotNet` 18.1.0 to both .NET projects.
- **Verification:** Both projects build with zero warnings/errors. A local
  DBSERV smoke test loaded a YAML port override and returned HTTP 200 from
  `/db/health`; the temporary configuration and Data Protection key were
  removed afterward.

