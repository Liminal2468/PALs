#!/usr/bin/env bash
set -euo pipefail

SERVER_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
DATABASE_DIR="$SERVER_DIR/database"

if ! command -v node >/dev/null 2>&1 || ! command -v npm >/dev/null 2>&1; then
  printf 'Node.js and npm are required. Install Node.js 20 or newer first.\n' >&2
  exit 1
fi

if ! node -e 'process.exit(Number(process.versions.node.split(".")[0]) >= 20 ? 0 : 1)'; then
  printf 'Node.js 20 or newer is required; found %s.\n' "$(node --version)" >&2
  exit 1
fi

cd "$SERVER_DIR"

if [[ ! -f package-lock.json ]]; then
  printf 'package-lock.json is missing; cannot install locked dependencies.\n' >&2
  exit 1
fi

npm ci
mkdir -p "$DATABASE_DIR/keys/generated" "$DATABASE_DIR/keys/trusted"
chmod 700 "$DATABASE_DIR/keys/generated" "$DATABASE_DIR/keys/trusted"

if [[ ! -f "$DATABASE_DIR/.env" ]]; then
  install -m 600 "$DATABASE_DIR/.env.example" "$DATABASE_DIR/.env"
  printf 'Created database/.env from its example. Configure MariaDB before starting the API.\n'
else
  chmod 600 "$DATABASE_DIR/.env"
  printf 'Kept existing database/.env and set its permissions to 600.\n'
fi

printf 'Setup complete. Add trusted host public keys under database/keys/trusted/ before starting the API.\n'
