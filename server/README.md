# PALs general database API

`server/` is the ASP.NET Core .NET 10 DBSERV host. It preserves the existing
REST route structure while the Node/Express implementation is being replaced.
The project targets the installed .NET CLI version `10.0.400`.

## Route structure

| Prefix | Service |
|---|---|
| `/db` | General database service |
| `/acc` | Account/profile service |
| `/sched` | Scheduling service |
| `/fb` | Feedback service |
| `/adata` | Analytics service |
| `/connect` | Matching and connection service |
| `/chat` | Messaging service |
| `/auth/v1` | Authentication service |

The .NET host now has an ASP.NET Core Identity persistence foundation. Identity
uses `identity_*` tables linked to the existing PALs `users` table through
`identity_users.pal_user_id`; the PALs account and role tables are preserved.
The generated EF migration is under [`Identity/Migrations/`](Identity/Migrations/)
and the reviewed MariaDB script is
[`database/schema/schema_v5_identity.sql`](database/schema/schema_v5_identity.sql).
The frontend feature migration is
[`database/schema/schema_v6_frontend_features.sql`](database/schema/schema_v6_frontend_features.sql).
The TOTP replay-protection script is
[`database/schema/schema_v8_mfa.sql`](database/schema/schema_v8_mfa.sql).
The profile, account-audit, session-audit, and page-metadata migration is
[`database/schema/schema_v9_profile_management.sql`](database/schema/schema_v9_profile_management.sql).
These scripts are not applied automatically.

## Run locally

```bash
cp config.example.yaml config.yaml
cp .example.env .env
# Set DB_PASSWORD in .env and edit the non-secret values in config.yaml.
./run
```

The default listener is `http://127.0.0.1:8432`. Override it with
`BIND_ADDRESS` and `PORT`, or set them in `config.yaml`. Blank or omitted YAML
values are ignored; process environment values take precedence. `DB_PASSWORD`
is intentionally ignored in YAML and must remain in `.env` or the process
environment.

To install the systemd service for the directory containing this checkout,
run `sudo ./install-service.sh` from `server/`. The installer installs the
service through a stable launcher link that points to this directory. The
published executable can therefore be located under any checkout path; the
service does not assume `/opt/palsys`. Persistent Data Protection keys are
stored in `/var/lib/palsys/dataprotection`.

The developer interface remains under [`public/`](public/). It is static
content and is served by the ASP.NET Core host; the old Vite and Node runtime
files have been removed from the active server tree.

## Identity setup

After applying `schema_v5_identity.sql` to the selected development database,
seed a developer or administrator interactively:

```bash
dotnet run --project Palsys.Server.csproj -- --seed-developer reime@pals.me developer
dotnet run --project Palsys.Server.csproj -- --seed-developer admin@pals.me admin
```

The command prompts for the password and stores only an ASP.NET Identity hash.
It also creates or updates the linked PALs account and role. Do not put the
password in a command line, SQL file, `.env`, or source archive.

## Database schemas

The active schemas are under [`database/schema/`](database/schema/). The v1
through v4 PALs schemas preserve the general account and feature structures;
`schema_v5_identity.sql` adds the linked ASP.NET Identity persistence tables;
`schema_v6_frontend_features.sql` adds resources, feedback, and support tables;
`schema_v7_sessions_schedule.sql` adds persistent HOST sessions and calendar
events; `schema_v8_mfa.sql` adds short-lived hashed TOTP replay protection.
`schema_v9_profile_management.sql` adds complete profile preferences, account
and session audits, and safe page metadata. These scripts are not applied
automatically.

After the earlier schemas are applied, run the v9 migration against the
selected database with a migration administrator:

```bash
sudo mariadb palsysdb < database/schema/schema_v9_profile_management.sql
```
Earlier Node-line copies remain under
[`bindoc/build/v2-nodeline/builddocs/schemas/`](../bindoc/build/v2-nodeline/builddocs/schemas/)
as migration references.

Do not expose MariaDB/MySQL directly to HOST clients. The database layer
enforces parameterized queries, ownership checks, route-level authorization,
allowlisted profile roles, audited management mutations, and bounded analytics
events. Page management stores metadata only; it cannot read or write arbitrary
files under the application directories.

## TOTP developer MFA

PALs owns the authentication flow. Google Authenticator is supported as a
standard TOTP client; Google Sign-In is not used as an external identity
provider. ASP.NET Identity stores the authenticator configuration and the
server verifies the code. The short-lived replay table stores only a SHA-256
digest of a successfully used code.

After signing in to the developer interface, start setup and confirm the code
from the authenticator app:

```bash
curl -b cookies.txt -c cookies.txt -X POST http://127.0.0.1:8432/dev/mfa/setup
curl -b cookies.txt -c cookies.txt -X POST http://127.0.0.1:8432/dev/mfa/confirm -H 'Content-Type: application/json' -d '{"code":"123456"}'
```

The setup response contains an `otpauth://` URI and manual key. Recovery codes
are returned only once; store them securely and do not commit or log them.
Set `MFA_REQUIRE_PRIVILEGED: true` in local configuration only after the
privileged accounts have completed setup. The default is `false` to permit
controlled migration of existing development accounts.

## Node-line recovery

The removed implementation is recoverable from
[`bindoc/build/v2-nodeline/`](../bindoc/build/v2-nodeline/), which contains
source archives and renamed `node-` documentation.
