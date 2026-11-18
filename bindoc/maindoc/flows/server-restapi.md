# REST API communication between Mentos and DBSERV

Mentos exposes the public `/api/v1` contract. DBSERV exposes the private
`/internal/v1` contract. Mentos translates the public request into a private
DBSERV request through `HostDbservClient`.

```mermaid
flowchart LR
    Client["Browser JavaScript"]

    subgraph HOST["Mentos HOST :8000"]
        Webend["WebendController\n/api/v1"]
        ClientProxy["HostDbservClient"]
        Cookie["pals_host_session\nopaque HttpOnly cookie"]
    end

    subgraph DBSERV["Private DBSERV :8432"]
        Data["DataController\n/internal/v1"]
        Trust["TrustedHost()\nX-Pals-Host-Key check"]
        Session["SessionUser()\nX-Pals-Session lookup"]
    end

    Client -->|"HTTP request with cookie when protected"| Webend
    Webend -->|"Read cookie and route request"| Cookie
    Webend --> ClientProxy
    ClientProxy -->|"HTTP + X-Pals-Host-Key\noptional X-Pals-Session"| Data
    Data --> Trust
    Trust -->|"valid service trust"| Session
    Session -->|"authorized internal operation"| Data
    Data -->|"status, JSON, or sanitized error"| ClientProxy
    ClientProxy --> Webend
    Webend -->|"public response"| Client
```

## Request classes

```mermaid
flowchart TB
    Request["Incoming browser request"]
    Public["Public HOST route\nhealth, auth options, frontend status"]
    Auth["Authentication route\nlogin, admin login, logout"]
    Protected["Protected HOST route\nme, sessions, resources, schedule, analytics"]
    Internal["Private DBSERV route\n/internal/v1"]
    Reject["401 / 403 / 502 sanitized error"]
    Response["JSON response to browser"]

    Request --> Public
    Request --> Auth
    Request --> Protected
    Public --> Response
    Auth --> Internal
    Protected -->|"requires pals_host_session"| Internal
    Auth -->|"missing service trust or invalid credentials"| Reject
    Protected -->|"missing session or DBSERV failure"| Reject
    Internal --> Response
```

The shared host key authenticates Mentos as a service; it does not authorize a
user. User authorization comes from the persisted DBSERV session and its
server-derived account role.
