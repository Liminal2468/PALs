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
# Set DB_PASSWORD, HOST_SHARED_KEY, and SHARED_SECRET in .env.
# Set SENDER_EMAIL and other non-secret values in config.yaml.
./run
```

The default listener is `http://127.0.0.1:8432`. Override it with
`BIND_ADDRESS` and `PORT`, or set them in `config.yaml`. Blank or omitted YAML
values are ignored; process environment values take precedence. `DB_PASSWORD`,
`HOST_SHARED_KEY`, and `SHARED_SECRET` are loaded only from `.env` or the
process environment; values for these secrets in YAML are ignored. The
`HOST_SHARED_KEY` value must match the one in `mentos/.env`.

Student login sends a context-bound, one-time TOTP code through AWS SES. Set
`SHARED_SECRET` to a private random master secret in `.env` and set
`SENDER_EMAIL` to an SES-verified sender identity in `config.yaml`; never
commit either value.
`STEP`, `ATOMIC_ALLOWEDWINDOW`, `CODE_LENGTH`, `MAX_ATTEMPTS`, and
`PENDING_TTL` default to `30`, `1`, `6`, `5`, and `300`. Email verification
codes are fixed at six digits and are bound to their pending login attempt. AWS
credentials must come from the AWS SDK default credential chain (for example,
an attached role or environment/profile credentials), not from the YAML file.
`AWS_REGION` defaults to `ap-southeast-2`.

The login attempt is held in memory and expires at the earlier of `PENDING_TTL`
or the TOTP code's validity window. A process restart invalidates pending
attempts. SES delivery failures return a service-unavailable response and do
not leave the attempt active. No verification code or AWS credential is logged.
The raw HTML message is loaded from
[`Identity/email/email.html`](Identity/email/email.html); only its
`{{ verification_code }}` placeholder is replaced.

An opt-in SES smoke sender is available under
[`server-tests/email-smoke/`](../server-tests/email-smoke/). It requires both
`PALS_EMAIL_SEND_CONFIRM=1` and an explicit `--send-to` recipient, uses the
AWS SDK default credential chain, and never prints the generated code. Do not
run it against a recipient without permission.

To install the systemd service for the directory containing this checkout,
run `sudo ./install-service.sh` from `server/`. The installer installs the
service through a stable launcher link that points to this directory. The
published executable can therefore be located under any checkout path; the
service does not assume `/opt/palsys`. Persistent Data Protection keys are
stored in `/var/lib/palsys/dataprotection`.

The apmain deployment uses `pals.service` from this directory and runs the
self-contained publish from `/opt/server`. It reads `config.yaml` and `.env`
from that directory, listens on `127.0.0.1:8432`, and keeps Data Protection
keys in `/var/lib/palsys/dataprotection`. The existing Tailscale Serve route
should continue to proxy to `http://127.0.0.1:8432`.

The developer interface remains under [`public/`](public/). It is static
content and is served by the ASP.NET Core host; the old Vite and Node runtime
files have been removed from the active server tree.

## Authentication reference projects

The original C# TOTP prototype and SES test sender are preserved under
[`authentication/`](authentication/):

- [`authentication/totp-cs/`](authentication/totp-cs/) contains the standalone
  TOTP implementation and its RFC/parity tests. Run them with
  `dotnet test authentication/totp-cs/tests/Totp.Tests/Totp.Tests.csproj`.
- [`authentication/vf-sender-cs/`](authentication/vf-sender-cs/) contains the
  standalone SES test sender. Build it with
  `dotnet build authentication/vf-sender-cs/vf-sender-cs.csproj`.

These are isolated reference projects, not the live login path. The sender
generates its own test code and uses `RECIPIENT_EMAIL`; the DBSERV login path
uses the integrated TOTP store and sends that exact code through
[`Identity/VerificationEmailSender.cs`](Identity/VerificationEmailSender.cs).
No `.env`, `.aws/credentials`, or build output was copied.

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
