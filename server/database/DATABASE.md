# Database configuration

Provision MariaDB from its CLI. These instructions assume MariaDB and the
database API run on the same host, with the API connecting to `127.0.0.1:3306`.
The `dbpals` account is for the API only; do not use MariaDB's `root` account
for API connections.

## Create the database and API user

Open an administrative MariaDB session:

```bash
sudo mariadb
```

At the MariaDB prompt, create the database and a least-privilege API account.
Replace the password placeholder with a unique, strong secret:

```sql
CREATE DATABASE IF NOT EXISTS palsysdb
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

CREATE USER 'dbpals'@'127.0.0.1'
  IDENTIFIED BY 'REPLACE_WITH_A_UNIQUE_STRONG_PASSWORD';

GRANT SELECT, INSERT, UPDATE, DELETE
  ON palsysdb.*
  TO 'dbpals'@'127.0.0.1';

SHOW GRANTS FOR 'dbpals'@'127.0.0.1';
```

Use the password manager or secret-management method approved for the server.
Do not put the real password in a shell command, source control, or a message.
`CREATE USER` is for first-time provisioning; if the account already exists,
use the reset procedure below instead. `FLUSH PRIVILEGES` is not needed after
`CREATE USER`, `GRANT`, or `ALTER USER`.

The API account has no schema-management privileges. Create and migrate tables
using a separate administrative or migration account. The API's
`DB_ALLOWED_TABLES` setting must list the tables that actually exist in
`palsysdb`.

If MariaDB is on another host, replace `'127.0.0.1'` with a narrowly scoped
MariaDB host value matching the API host, and set `DB_HOST` to the MariaDB
server's reachable address. Do not grant the account access from `'%'` unless
there is a deliberate, separately secured reason.

## Set or reset the password

To change the password for an existing account, connect as a MariaDB
administrator and run:

```sql
ALTER USER 'dbpals'@'127.0.0.1'
  IDENTIFIED BY 'REPLACE_WITH_A_NEW_UNIQUE_STRONG_PASSWORD';
```

After changing it in MariaDB, update `DB_PASSWORD` in `server/.env` to the
same value and restart DBSERV. Treat the `.env` file as a secret: keep it out
of source control and preserve restrictive permissions when editing or
deploying it.

## Configure DBSERV

The active server has one non-secret configuration file. From `server/`, create
the local files:

```bash
cp config.example.yaml config.yaml
cp .example.env .env
```

Edit `config.yaml` for the MariaDB connection and table allowlist:

```yaml
DB_HOST: 127.0.0.1
DB_PORT: 3306
DB_USER: dbpals
DB_NAME: palsysdb
```

Set `DB_PASSWORD`, `HOST_SHARED_KEY`, and `SHARED_SECRET` in `.env`.
`DB_PASSWORD` and the shared authentication secrets are deliberately ignored
in YAML. The API rejects resources not listed in `DB_ALLOWED_TABLES`.

> [!NOTE]
> The tables in `DB_ALLOWED_TABLES` are preset tables that are aligned with the current schema version. Use comma's to add more tables.

Before starting the API, verify the account can connect over TCP; `-p` prompts
for the password without placing it in the command:

```bash
mariadb --host=127.0.0.1 --port=3306 --user=dbpals --password palsysdb
```

Then start DBSERV from the `server/` directory:

```bash
./run
```

The .NET DBSERV host reads non-secret settings from `server/config.yaml` and
reads `DB_PASSWORD`, `HOST_SHARED_KEY`, and `SHARED_SECRET` from `server/.env`
or the process environment. Mentos reads the matching `HOST_SHARED_KEY` from
`mentos/.env`.
