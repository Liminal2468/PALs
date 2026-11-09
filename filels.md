# PALs file map

<!-- TODO: NEEDS UPDATING -->

This is a source-oriented map of the two machine-side projects. **Mentos is
HOST; `server/` is DBSERV (`apmain`).** The
trees omit `.git/`, `node_modules/`, Python virtual environments, build output
(`dist/`), bytecode caches, local secret files (`.env`, `.keys/`), and other
generated dependency files. Dependency manifests and lockfiles are included.
Local environment values and signing keys are intentionally not listed.

## Mentos machine — frontend and Flask host API

```text
mentos/
├── README.md                         Project setup and developer workflow
├── .gitignore                        Excludes dependencies, build output and secrets
├── package.json                      Vite scripts and frontend dependency manifest
├── package-lock.json                 Locked frontend dependency versions
├── vite.config.js                    Serves public/, proxies /api to HOST :8000
├── bin/
│   ├── runner.js                     Checks DBSERV readiness, then starts the HOST API
│   ├── run                           Bash launcher
│   ├── run.sh                        POSIX sh launcher
│   ├── run.ps1                       PowerShell launcher
│   └── run.bin                       Windows cmd launcher
├── public/
│   ├── index.html                    Dashboard shell and navigation
│   ├── scripts/
│   │   ├── pals.js                   Loads selected dashboard pages into the shell
│   │   └── base.js                   Base frontend script; not loaded by the PALs shell
│   ├── styles/
│   │   ├── Pals.css                  Integrated dashboard styling
│   │   ├── base.css                  Shared base styles
│   │   └── values.css                Shared design values
│   ├── pages/
│   │   ├── home.html                 Dashboard page
│   │   ├── tutors.html               Tutor discovery page
│   │   ├── sessions.html             Sessions page
│   │   ├── resources.html            Resources page
│   │   ├── analytics.html            Analytics page
│   │   ├── settings.html             Settings page
│   │   ├── help.html                 Help page
│   │   └── feedback.html             Feedback page
│   └── components/
│       ├── menu/index.html           Menu component example
│       └── modal/index.html          Modal component example
├── api/
│   ├── README.md                     Flask API, DB client and session setup
│   ├── .gitignore                    Excludes local keys, environment and Python artifacts
│   ├── .env.example                  DBSERV URL, JWT, session and proxy-trust template
│   ├── pyproject.toml                Python package, dependencies and api command
│   ├── uv.lock                       Locked Python dependency versions
│   ├── requirements.txt              Alternate Python dependency list
│   ├── .python-version               Requested Python interpreter version
│   ├── main.py                       Legacy/direct Python entry point
│   ├── back/
│   │   ├── auth/
│   │   │   ├── auth.py               Earlier authentication implementation
│   │   │   ├── data.py               Earlier local auth data helpers
│   │   │   └── encryter.py           Earlier encryption helper
│   │   └── dbassets/mentos.db        Earlier local SQLite database asset
│   ├── src/api/
│   │   ├── __init__.py               Exposes the Flask command entry point
│   │   ├── app.py                    Flask factory, edge middleware and Waitress startup
│   │   ├── healthcheck.py             DBSERV liveness and signed database readiness probe
│   │   ├── limiting.py                Per-IP HOST API rate-limit configuration
│   │   ├── routes.py                 /api/v1 health, session, profile and learning-request routes
│   │   ├── sessions.py               MariaDB-backed opaque browser session interface
│   │   ├── enrollment.py             Unimplemented enrollment workflow placeholders
│   │   └── db/
│   │       ├── __init__.py           Exports the DB client class
│   │       ├── db.py                 Issues RS256 JWTs and calls the remote /db API
│   │       └── .env.config            Legacy DB-client configuration fallback
│   ├── tests/
│   │   ├── test_db_client.py         Unit tests for DB client and JWT behavior
│   │   ├── test_host_api.py          Flask route and server-side session tests
│   │   ├── tests-api.py              Five-sample health/JWKS response benchmark
│   │   └── test-ratelim-cap.py       Bounded user create/update/delete capacity test
│   └── utils/
│       ├── clearcache.py             Removes local Python cache files
│       └── dbflow.md                 Mermaid diagram of the cross-host request path
├── log/
│   ├── localreport.log               Local diagnostic output
│   └── rep.py                        Local reporting helper
```

Ignored local config/credential files exist outside the tree: `api/.env` and
the demo signing keypair in `api/.keys/`. Keep the private key local; only its
matching public key is installed on the API/database machine. Generated
frontend build output in `dist/` is also omitted.

### Mentos connections

1. `public/index.html` loads `styles/Pals.css` and `scripts/pals.js`.
   `pals.js` fetches the local `public/pages/*.html` fragments for navigation;
   it does **not** currently call the application API.
2. Vite serves `public/` during frontend development and proxies `/api/*` to
   the Flask process at `127.0.0.1:8000`.
3. `bin/runner.js` checks DBSERV's `/db/health` and authenticated
   `/db/health/database` through the Python client before starting Waitress.
   If checks fail after three attempts, it waits 60 seconds and checks again.
   Vite is started separately.
4. Flask routes are mounted at `/api/v1` and served by Waitress. They use
   `sessions.py` to store
   browser session data in MariaDB through the host-side `DB` client.
   Authentication is deliberately unfinished: `/api/v1/auth/login` returns
   `501` until the STI eLMS identity flow is decided.
5. `routes.py` calls `api.db.DB` for profile, role and learning-request data.
   The DB client reads `api/.env` (ignored) or the legacy `.env.config`,
   signs short-lived RS256 JWTs using `api/.keys/`, then sends requests to
   DBSERV's configured `DBSERV_URL` with `/db/...` appended.
6. The configured DBSERV public Funnel origin is
   `https://palsys.softshell-grue.ts.net`. Because Funnel is publicly
   reachable, every `/auth/v1` route requires a valid HOST JWT; this map does
   not assume Funnel reachability.

The `api/back/` files and local SQLite asset are earlier/legacy code, not
dependencies of the current `src/api/app.py` entry point.

## DBSERV machine — `apmain`

```text
server/
├── README.md                         Server setup, mounted APIs and runner instructions
├── API.md                            Draft feature API contracts and behavior
├── note.md                           Server-side project notes
├── .gitignore                        Excludes node_modules and database secrets/keys
├── package.json                      Node workspaces, scripts and Express/rate-limit dependencies
├── package-lock.json                 Locked Node dependency versions
├── setup.sh                          Installs dependencies and prepares DB configuration/key directories
├── run                               Root launcher delegates to src/run
├── palsys.service                    systemd unit starts run; MariaDB is a weak dependency
├── src/
│   ├── app.js                        Express middleware, proxy trust, rate limits and router mounts
│   ├── server.js                     Validates config, binds, and shuts down shared DB pool
│   ├── runner.js                     Development launcher; checks API health and DB connectivity
│   ├── runner-utils.js               Runner health URL and unexpected-exit helpers
│   ├── run                           Bash runner
│   ├── run.sh                        POSIX sh runner
│   ├── run.ps1                       PowerShell runner
│   └── run.bin                       Windows cmd runner
├── database/
│   ├── package.json                  Database router workspace manifest
│   ├── .env.example                  Bind address, port, MariaDB, allowlist and JWT template
│   ├── .gitignore                    Excludes credentials and generated/trusted keys
│   ├── setup.sh                      Delegates setup to the server workspace
│   ├── README.md                     DB API setup, JWT rotation, health and CRUD contract
│   ├── DATABASE.md                   MariaDB user, password and connection setup
│   ├── schemas/
│   │   ├── schema_v1.sql             PALs roles, users, profiles, matching, sessions, feedback, analytics and chat tables
│   │   └── schema_v2_sessions.sql    MariaDB-backed Flask browser session table
│   ├── keys/
│   │   ├── generated/.gitkeep        Placeholder for locally generated keys
│   │   └── trusted/.gitkeep          Placeholder for trusted host public keys
│   ├── src/
│   │   ├── routes.js                 Liveness, JWKS, protected DB health, CRUD and paginated reads
│   │   ├── hostauth.js               Verifies host JWTs, scopes and cached trusted keys
│   │   ├── db.js                     DB allowlist/schema helpers; re-exports shared pool
│   │   └── keyresp.js                Generates key pairs and loads/verifies trusted keys
│   └── test/                         API, auth, limits, paging and service tests
├── accounts/
│   ├── package.json                  Accounts API workspace manifest
│   └── src/routes.js                 /acc health route; business handlers are 501
├── scheduler/
│   ├── package.json                  Scheduling API workspace manifest
│   └── src/routes.js                 /sched health route; business handlers are 501
├── feedback/
│   ├── package.json                  Feedback API workspace manifest
│   └── src/routes.js                 /fb health route; business handlers are 501
├── analyst/
│   ├── package.json                  Analytics API workspace manifest
│   └── src/routes.js                 /adata health route; business handlers are 501
├── connect/
│   ├── package.json                  Matching API workspace manifest
│   └── src/
│       ├── routes.js                 /connect health, match and tutorreco routes
│       └── matching.js               Stateless provisional tutor/tutee ranking algorithm
├── authentication/
│   ├── package.json                  Authentication API workspace manifest
│   └── src/routes.js                 /auth/v1 skeleton; business handlers are 501
├── chat/
│   ├── package.json                  Messaging API workspace manifest
│   └── src/routes.js                 /chat/v1 skeleton; business handlers are 501
└── shared/
    ├── package.json                  Shared configuration and DB core workspace
    └── src/
        ├── config.js                 Loads database/.env and validates server config
        └── db.js                     Validates config before lazily creating MariaDB pool
```

Current DB/API tests include `api-error-handler.test.js`,
`api-prefixes.test.js`, `connect-api.test.js`, `keyresp.test.js`,
`rate-limits.test.js`, `routes.test.js`, `runner.test.js`,
`server-binding.test.js`, and `shared-core.test.js`. The schema and key
directories are structural references only; this map does not enumerate
their contents.

### API/server connections

1. `src/server.js` validates configuration, binds to `127.0.0.1:8100` by
   default (overridable with `BIND_ADDRESS` and `PORT`), and closes the shared MariaDB
   pool on shutdown. The systemd unit starts it directly.
2. `src/app.js` mounts the eight API routers in one Express process:
   `/db`, `/acc`, `/sched`, `/fb`, `/adata`, `/connect`, `/auth/v1`, and
   `/chat/v1`. Its in-memory limits are 120 non-`/db` requests per IP, 60
   unsuccessful `/db` responses per IP, and 3,000 authenticated `/db`
   requests per verified JWT `kid` per 15 minutes.
3. Express trusts `X-Forwarded-For` only from loopback proxies. A local HTTP
   proxy must sanitize/overwrite that header for limits to key on client IP.
   Actual forwarded-IP behavior for Tailscale Serve/Funnel HTTP proxying is
   unverified; neither route is configured here.
4. `/db/health` is unauthenticated liveness-only. `/db/health/database` checks
   MariaDB with `SELECT 1` after host-JWT verification. The development
   `src/runner.js` probes every API and checks MariaDB using the shared pool;
   `palsys.service` uses `Wants=`/`After=` rather than requiring MariaDB before
   the API starts.
5. `shared/` owns database configuration and the lazily created MariaDB pool.
   It loads the ignored `database/.env`; `database/src/db.js` adds the DB
   allowlist and table metadata helpers. Trusted host keys stay in the
   ignored `database/keys/trusted/` directory.
6. `database/src/hostauth.js` verifies host RS256 tokens, scope, resource and
   action, caching trusted keys for up to five seconds so new keys are picked
   up after cache expiry. The private signing key remains on Mentos.
7. `connect/` is the only feature workspace with more than a health skeleton:
   its current weighted baseline ranks caller-supplied candidates without
   loading identities from MariaDB or persisting results. Other feature routes
   still return `501` for business operations.

## Cross-machine request path

```text
Browser
  └─> Vite frontend on Mentos
       └─> /api/* proxy to Flask host API on Mentos :8000
            └─> DB client signs short-lived RS256 JWT with Mentos private key
                 └─> HTTPS to configured apmain API origin
                      └─> optional reverse proxy / Tailscale Serve / Funnel
                           └─> Express /db bound to 127.0.0.1:8100 by default
                                └─> validate JWT against trusted public key
                                     └─> shared pool -> MariaDB palsysdb :3306
```

The current frontend is not yet wired to these host API routes, and the eLMS
login flow is not implemented. The Tailscale reverse-proxy choice is open and
not configured by the server project. Health checks confirm service
availability, not completion of the planned feature APIs.
