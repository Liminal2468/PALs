# Mentos API requests to DBSERV

This flow follows a request initiated by a page or script in `mentos/public/`.
The browser calls Mentos only. Mentos decides whether the request is public or
session-protected, then forwards the appropriate private operation to DBSERV.

```mermaid
sequenceDiagram
    participant UI as mentos/public JavaScript
    participant M as Mentos WebendController
    participant C as HostDbservClient
    participant S as DBSERV DataController
    participant DB as MariaDB

    UI->>M: GET/POST /api/v1/... with browser cookie
    M->>M: Route public or protected operation
    alt Protected operation
        M->>M: Read pals_host_session cookie
        M->>C: Forward method, path, body, session key
    else Public operation
        M->>C: Forward only when a DBSERV operation is required
    end
    C->>S: Private HTTP + X-Pals-Host-Key
    C->>S: Optional X-Pals-Session
    S->>S: Validate host trust and user session
    S->>DB: Parameterized query or command
    DB-->>S: Result rows or database error
    S-->>C: JSON status and body
    C-->>M: DbservResponse
    M-->>UI: Sanitized HTTP response
```

Examples of public-to-private mappings include:

| Browser-facing Mentos route | Private DBSERV route |
|---|---|
| `POST /api/v1/auth/admin/login` | `POST /internal/v1/auth/admin/login` |
| `GET /api/v1/session` | `GET /internal/v1/auth/session` |
| `GET /api/v1/me` | `GET /internal/v1/me` |
| `GET /api/v1/resources` | `GET /internal/v1/resources` |
| `GET /api/v1/schedule` | `GET /internal/v1/schedule` |
| `POST /api/v1/analytics/events` | `POST /internal/v1/analytics/events` |

Tutor recommendations are currently a deliberate HTTP 501 response from
Mentos; `/connect` matching is not part of this active request flow.
