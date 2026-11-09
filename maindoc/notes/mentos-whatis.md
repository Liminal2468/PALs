# What is Mentos?

Mentos is the PALs HOST: it serves the browser-facing application and its Flask API. It communicates with DBSERV over the configured server-to-server connection; clients should use the HOST rather than connect directly to DBSERV.

## Requirements

- Git access to the private PALs repository
- Node.js 20 or newer and npm
- Python 3.12 or newer and `uv`
- Access to the configured DBSERV API and PALs tailnet for database-backed features

## Clone and install

Obtain the private repository clone URL from the project owner, then clone and install the Mentos dependencies:

```bash
git clone "PASTE_THE_REPOSITORY_CLONE_URL"
cd REPOSITORY_FOLDER/mentos
npm install
uv sync --project api
```

Replace the clone URL and `REPOSITORY_FOLDER` with the URL and folder name for your checkout.

Use `api/.env.example` as a reference for HOST API configuration. Keep real `.env` values, signing keys, and provider credentials private; do not commit or share them.

## Run locally

From the `mentos/` directory, start the Vite frontend and Flask HOST API:

```bash
./run
```

The launcher runs both services as separate processes. Vite normally serves the frontend at `http://localhost:5173/`; the API listens on `127.0.0.1:8000`. Press Ctrl-C to stop them. On Windows, use WSL for this Bash launcher or consult the available platform-specific runner scripts described in [`notectx.md`](../notectx.md).

For additional startup configuration, login preview details, and service origins, see [`notectx.md`](../notectx.md). For tailnet installation and access guidance, see [`apmain-whatis.md`](./apmain-whatis.md).

## Git contribution workflow

Create a focused feature branch from the latest `main`, push it, and open a pull request targeting `main`. Update the feature branch from `main` and resolve conflicts before merging. See [Create a branch](../../create-branch.md), [Merging a branch](../../merge-branch.md), and [Basic CI/CD flow](../../basic-cicdflow.md).
