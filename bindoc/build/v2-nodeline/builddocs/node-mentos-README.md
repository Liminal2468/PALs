# Mentos Learning Platform

Mentos is a learning-platform starter project. The frontend is served by Vite
from `public/`; the Flask application is in `api/`. A host-side DB client and
Node.js database API are scaffolded under `api/` and `../server/database`.
When data routes are wired, the Node.js service will validate short-lived
host-issued JWTs before querying MariaDB. MariaDB is provisioned separately.

## Frontend

- [`public/index.html`](public/index.html) now serves the PALs dashboard at `/`.
- The integrated dashboard assets are [`public/styles/pals.css`](public/styles/pals.css),
  [`public/scripts/pals.js`](public/scripts/pals.js), and [`public/pages/`](public/pages/).
  The PALs stylesheet and script were copied from `PALs/PALs/` without visual
  edits; the existing navigation loads each page into the dashboard.
- [`public/styles/base.css`](public/styles/base.css) and
  [`public/styles/values.css`](public/styles/values.css) remain available for
  the rest of the site.
- [`public/scripts/base.js`](public/scripts/base.js) remains available but is
  not loaded by the PALs dashboard entry page.
- [`public/components/login/modal.login.htm`](public/components/login/modal.login.htm) is the standalone login-modal preview. It loads enabled HOST providers from `/api/v1/auth/options` and submits admin credentials to the HOST API; provider login remains subject to deployment configuration.

The frontend entry route is `/`. There is no `game.html` route.
The former menu and generic modal demo pages were removed; the login component is the retained modal example.

## API and database client

- [`api/src/api/app.py`](api/src/api/app.py) creates the Waitress-served Flask HOST API at `/api/v1`; current routes are documented in [`api/README.md`](api/README.md).
- [`api/src/api/sessions.py`](api/src/api/sessions.py) stores opaque browser sessions server-side in MariaDB; provider login and admin credential routes are documented in [`api/README.md`](api/README.md).
- [`api/src/api/db/db.py`](api/src/api/db/db.py) provides JWT-authenticated create, read, update, aggregation, and delete requests.
- `api/.env` configures the database API origin as `DBSERV_URL` (without the `/db` suffix), JWT issuer/audience, private-key path, key ID, and token lifetime. The deprecated `HOST` origin setting remains a warning-producing fallback during migration.
- [`api/src/api/enrollment.py`](api/src/api/enrollment.py) contains placeholder functions for class/group enrollment, tutee sign-up, and tutor assignment.
- [`api/utils/clearcache.py`](api/utils/clearcache.py) clears Python cache files.

See [`../server/database/README.md`](../server/database/README.md) for the
database server setup, JWT key-rotation process, and request/response contract.
The API host and route prefixes are listed in [`../server/README.md`](../server/README.md).
Keep the host signing private key outside source control and configure
`JWT_PRIVATE_KEY_PATH` to its secure location.

## Development

Start the Vite frontend and HOST API as separate processes with one command:

```bash
cd mentos
npm install
uv sync --project api
./run
```

`run` launches `npm run dev` and `uv run --project api api` as separate
processes, forwarding both services' output to the terminal. Press Ctrl-C to
stop both. Vite serves the frontend on port 5173 and proxies `/api` requests to
the HOST API on `127.0.0.1:8000`.

To install or refresh Python dependencies separately:

```bash
uv sync --project api
```

Node.js 20+ and Python 3.12+ are required.

### Host API startup

Use `./run` for the one-command local development experience that starts both
Vite and the HOST API. The launcher checks the `cloudflared.service` state and
serves Vite on port 5000, binding to all interfaces for an active tunnel or to
loopback when the tunnel is inactive. The HOST API remains on
`127.0.0.1:8000`.
