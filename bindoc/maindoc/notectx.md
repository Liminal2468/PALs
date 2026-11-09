# Running Mentos

Mentos has two development services: the Vite frontend and the Flask HOST API.
The `mentos/run` Bash launcher starts them as separate processes from one
terminal. Vite serves on port `5173` by default and proxies `/api` requests to
the API at `127.0.0.1:8000`.

## Requirements

- Node.js 20 or newer and npm
- Python 3.12 or newer and `uv`
- Access to a configured DBSERV instance for API health checks and database
  backed routes

## Start both services

Install frontend dependencies, then run the combined launcher:

```bash
cd mentos
npm install
uv sync --project api
./run
```

`run` starts `npm run dev` and `uv run --project api api` separately, forwarding
both outputs to the terminal. Press Ctrl-C to stop both. Vite normally serves
at `http://localhost:5173/`; the Flask HOST API uses Waitress on
`127.0.0.1:8000`.

Before starting, configure `DBSERV_URL`, the HOST JWT signing settings, and
other required settings using `api/.env.example` as a reference. Keep real
`.env` values and signing keys private; do not put them in source control. The
API health endpoint reports `ready` or `degraded` based on DBSERV reachability.

To install or refresh Python dependencies separately:

```bash
uv sync --project api
```

## Optional API-gated runner

To start only the HOST API after DBSERV health checks pass, use the
cross-platform runner from `mentos/`:

```bash
bash bin/run
```

Equivalent scripts are `sh bin/run.sh`, `.\bin\run.ps1`, and `bin\run.bin`
for POSIX `sh`, PowerShell, and Windows Command Prompt respectively. The
runner checks DBSERV liveness and signed database readiness, retries three
times, then waits 60 seconds between further attempts. It starts the HOST API
only. Use `./run` above when both local development services should run
together.

## Login preview and provider setup

The standalone preview is available at
`/components/login/modal.login.htm`. Google login uses a real provider flow,
not a demo identity, and remains disabled until DBSERV has Google credentials,
an exact redirect allowlist entry, and the HOST is reachable at the matching
HTTPS callback URI:

```text
https://solaris.softshell-grue.ts.net/api/v1/auth/google/callback
```

The browser-facing HOST origin is `https://solaris.softshell-grue.ts.net`. HOST
connects server-to-server to DBSERV using `DBSERV_URL`; the current example is
`https://palsys.softshell-grue.ts.net`. That DBSERV origin serves both `/db`
and `/auth/v1` routes. Do not use the DBSERV URI as Google's redirect URI.

The Google client secret belongs only in DBSERV's private authentication
configuration; never put it in Mentos, frontend files, or a public options
response. Microsoft Entra is planned but not enabled. The separate development
provider is disabled by default and must not be enabled in production.

For API routes, provider options, response envelopes, and error codes, see
[`notes/auth-contract.md`](./notes/auth-contract.md). For Mentos project
structure and setup context, see [`../../mentos/README.md`](../../mentos/README.md).
