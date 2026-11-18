# DBSERV and Mentos connection

This is the high-level topology for the active .NET implementation. Mentos is
the browser-facing HOST. `server/` is DBSERV and must remain private from
browser JavaScript. The diagrams use Mermaid flowchart syntax; see the
[Mermaid flowchart reference](https://mermaid.js.org/syntax/flowchart).

```mermaid
flowchart LR
    Browser["Browser\nmentos/public frontend"]
    Mentos["Mentos HOST\nASP.NET Core\n127.0.0.1:8000"]
    Server["DBSERV server\nASP.NET Core\n127.0.0.1:8432"]
    Database[("MariaDB\npalsysdb")]

    Browser -->|"Public HTTP\n/api/v1 and static files"| Mentos
    Mentos -->|"Private HTTP\nDBSERV_URL\nX-Pals-Host-Key"| Server
    Server -->|"SQL over configured DB connection"| Database
    Database -->|"Rows and status"| Server
    Server -->|"Sanitized JSON or error"| Mentos
    Mentos -->|"JSON, HTML, and HttpOnly cookie"| Browser
```

## Configuration relationship

```mermaid
flowchart TB
    ServerYaml["server/config.yaml\nnon-secret DBSERV settings"]
    ServerEnv["server/.env\nDB_PASSWORD only"]
    MentosYaml["mentos/config.yaml\nDBSERV_URL and HOST_SHARED_KEY"]
    ServerApp["DBSERV process"]
    MentosApp["Mentos process"]

    ServerYaml --> ServerApp
    ServerEnv --> ServerApp
    MentosYaml --> MentosApp
    ServerApp <-->|"HOST_SHARED_KEY must match"| MentosApp
    MentosApp -->|"DBSERV_URL selects private server origin"| ServerApp
```

The browser never receives `HOST_SHARED_KEY`, database credentials, ASP.NET
Data Protection keys, or the internal DBSERV route contract.
