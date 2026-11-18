# DBSERV to MariaDB

DBSERV has two database access paths. PALs application data and HOST sessions
use the parameterized `PalsDataStore`; ASP.NET Identity uses EF Core through
`PalIdentityDbContext`. Both paths use the connection built from the active
server configuration.

```mermaid
flowchart TB
    Controller["DBSERV controllers"]
    DataStore["PalsDataStore\nparameterized SQL"]
    IdentityStore["ASP.NET Core Identity\nUserManager / SignInManager"]
    IdentityContext["PalIdentityDbContext\nEF Core MySQL provider"]
    Replay["MfaReplayStore\nshort-lived SHA-256 code digests"]
    Connection["IdentityConnection.Build()\nDB_HOST, DB_PORT, DB_USER, DB_NAME\nDB_PASSWORD from server/.env"]
    Maria[("MariaDB\npalsysdb")]

    Controller --> DataStore
    Controller --> IdentityStore
    Controller --> Replay
    IdentityStore --> IdentityContext
    DataStore --> Connection
    IdentityContext --> Connection
    Replay --> Connection
    Connection --> Maria
```

## Main table groups

```mermaid
flowchart LR
    DB[("palsysdb")]
    PALs["PALs account data\nusers, roles, user_roles\nstudent profiles and subjects"]
    Identity["Identity data\nidentity_users, identity_roles\nclaims, logins, tokens"]
    Sessions["HOST state\nweb_sessions\nschedule_events"]
    Features["Feature data\nresources, feedback, support\nanalytics and learning records"]
    MFA["MFA state\nidentity_mfa_replays"]

    DB --> PALs
    DB --> Identity
    DB --> Sessions
    DB --> Features
    DB --> MFA
    Identity -. "pal_user_id mapping" .-> PALs
    Sessions -. "session data resolves to PALs user" .-> PALs
    MFA -. "foreign key to identity_users" .-> Identity
```

Schema files are applied by a DBA or migration account. The runtime `dbpals`
account is intended for CRUD operations and must not be given permanent schema
management privileges.
