# Mentos to DBSERV to MariaDB

This file combines the three-service path for an authenticated data request.
It shows where the browser cookie is used, where the private service headers
are added, and where the user/session data is resolved in MariaDB.

```mermaid
flowchart LR
    Browser["Browser\npage or frontend script"]
    HOST["Mentos HOST\nWebendController"]
    Cookie["pals_host_session\nopaque session key"]
    Client["HostDbservClient\nadds private headers"]
    API["DBSERV\nDataController"]
    Trust["X-Pals-Host-Key\nservice trust"]
    Session["X-Pals-Session\nuser session context"]
    Store["PalsDataStore\nparameterized SQL"]
    DB[("MariaDB\npalsysdb")]
    User["PALs account and role\nusers + user_roles"]
    Data["Requested feature rows"]

    Browser -->|"GET /api/v1/me\nGET /api/v1/resources\nor another protected route"| HOST
    HOST -->|"read cookie"| Cookie
    HOST --> Client
    Cookie -->|"opaque key"| Client
    Client -->|"private request"| API
    Client --> Trust
    Client --> Session
    API --> Trust
    API --> Session
    Session --> Store
    Store --> DB
    DB --> User
    DB --> Data
    User --> Store
    Data --> Store
    Store --> API
    API --> Client
    Client --> HOST
    HOST -->|"sanitized JSON"| Browser
```

## Login creates the state used by later requests

```mermaid
flowchart TB
    Credentials["Browser submits login"]
    HOST["Mentos /api/v1/auth/admin/login"]
    Private["DBSERV /internal/v1/auth/admin/login"]
    Identity["ASP.NET Identity\npassword and optional TOTP"]
    Account["PALs users and server role"]
    Session["web_sessions\nopaque key + expiry"]
    Cookie["Mentos sets\nHttpOnly pals_host_session"]
    Protected["Later protected API request"]

    Credentials --> HOST
    HOST --> Private
    Private --> Identity
    Identity --> Account
    Account --> Session
    Session -->|"sessionKey returned privately"| HOST
    HOST --> Cookie
    Cookie --> Protected
    Protected --> HOST
```

The browser does not receive the database connection, host shared key, or raw
database credentials. It receives only the opaque browser session cookie.
