# PALs file map

The active applications are separate ASP.NET Core .NET 10 projects.

## Mentos HOST

```text
mentos/
├── Mentos.Host.csproj       .NET 10 HOST project
├── Program.cs               HOST startup and public-file hosting
├── Controllers/             HOST controllers
├── webend/                  frontend backend boundary (*.{category}.cs)
├── public/                  preserved browser interface only
└── api/.env                 local ignored configuration; never commit
```

### Mentos HOST route and frontend status

Implemented HOST routes are defined in `mentos/Controllers/HostController.cs`
and `mentos/Controllers/WebendController.cs`:

```text
/api/v1/health
/api/v1/auth/options
/api/v1/auth/login
/api/v1/auth/logout
/api/v1/session
/api/v1/me
/api/v1/users
/api/v1/pages
/api/v1/recommended-peer-tutors
/api/v1/tutors/recommended
/api/v1/frontend/status
```

The account/session implementation uses a DBSERV-backed opaque session cookie.
Student login remains a school-email-and-terms provisioning compatibility
facade; admin/developer login uses ASP.NET Identity. OAuth/OIDC, provider
callbacks, signup, `/connect`, and tutor recommendations remain deferred.

The public pages are served from `mentos/public/`. Login is connected to the
HOST and `/pals.html` plus `/pages/*` are session-protected. Live hydration
now covers profile fields, dashboard statistics, tutor/tutee records,
learning requests, sessions, resources, feedback, support, and consent-aware
analytics. Tutor recommendations remain deferred; a dedicated administrator
management screen and resource mutation controls are not yet present.

## DBSERV

```text
server/
├── Palsys.Server.csproj     .NET 10 database-service project
├── Program.cs               DBSERV startup and public-file hosting
├── Controllers/             DBSERV controllers
├── Data/                    parameterized PALs data-store boundary
├── Identity/                ASP.NET Identity models, seed, and migrations
├── src/gen                  local HOST_SHARED_KEY generator
├── public/                  preserved developer interface
├── config.yaml              local non-secret configuration; never commit
├── .env                     DB_PASSWORD only; never commit
└── run                      .NET launcher
```

The DBSERV health routes are exposed as `/db/health`, `/acc/health`,
`/sched/health`, `/fb/health`, `/adata/health`, `/connect/health`, and
`/chat/health`. The `/connect` matching implementation itself is not yet
available; only its health route and the developer-side empty algorithm list
are present.

Identity persistence is stored in `identity_*` tables linked to PALs `users`
through `identity_users.pal_user_id`. Apply
`server/database/schema/schema_v5_identity.sql` before using the interactive
developer seed command. Passwords are entered interactively and are never
stored in source files or schema SQL.

Frontend feature persistence is defined in
`server/database/schema/schema_v6_frontend_features.sql`. Mentos calls the
DBSERV `internal/v1` routes through its `/api/v1` façade using the private
`HOST_SHARED_KEY`. `/connect` and tutor recommendations remain intentionally
unimplemented until the matching algorithm is approved.

Persistent HOST sessions and calendar events are defined in
`server/database/schema/schema_v7_sessions_schedule.sql`. Schedule dates are
accepted as `MM-DD-YY:HH-mm` or ISO input and stored as UTC timestamps.

Profile bio/section/preferences, account and session audits, and safe page
metadata are defined in
`server/database/schema/schema_v9_profile_management.sql`. Page management
changes metadata only and cannot read or write arbitrary application files.

## Preserved Node-line build

The previous implementation is archived under
[build/v2-nodeline/](build/v2-nodeline/). Its schemas are temporarily under
[build/v2-nodeline/builddocs/schemas/](build/v2-nodeline/builddocs/schemas/).
The archive excludes credentials, private keys, dependency trees, virtual
environments, caches, and generated output.

## Active architecture flow maps

Mermaid diagrams for the active .NET services are under
`bindoc/maindoc/flows/`:

```text
flows/
├── server-mentos.md                         high-level HOST/DBSERV topology
├── server-restapi.md                         public/private REST boundary
├── server-database.md                        DBSERV persistence paths
├── mentos-server.md                          HOST request forwarding
├── mentos-server-database.md                 full request/data path
└── authentication-flow-pre-established.md    current and planned auth flows
```
