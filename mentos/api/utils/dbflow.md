```mermaid
flowchart LR
    subgraph Host["Mentos host"]
        Browser["Browser"]
        Vite["Vite frontend"]
        Flask["Mentos Flask API"]
        Client["api.db.DB"]
        PrivateKey["RS256 signing key<br/>api/.keys/"]
        Browser --> Vite
        Vite -->|"Proxy /api"| Flask
        Flask --> Client
        Client -.->|"sign short-lived JWT"| PrivateKey
    end

    subgraph PublicNetwork["Public internet"]
        Funnel["Tailscale Funnel<br/>HTTPS :443<br/>palsys.softshell-grue.ts.net"]
    end

    subgraph DatabaseHost["apmain"]
        NodeAPI["Node.js API server<br/>Express :8100"]
        JWT["Verify RS256 JWT<br/>iss, aud, exp, kid, scope"]
        MariaDB[("MariaDB :3306<br/>palsysdb")]
        PublicKey["Trusted public key<br/>database/keys/trusted/"]
        NodeAPI --> JWT
        PublicKey --> JWT
        JWT -->|"authorized query"| MariaDB
    end

    Client -->|"HTTPS REST /db/* + JWT"| Funnel
    Funnel -->|"forward to http://127.0.0.1:8100"| NodeAPI
    MariaDB -->|"query result"| NodeAPI
    NodeAPI -->|"HTTP response"| Funnel
    Funnel -->|"HTTPS response"| Client
```

The intended request/response path is Mentos `api.db.DB` → HTTPS on the public
Tailscale Funnel (port 443) → the Node API's private listener on `apmain:8100` → JWT
validation → MariaDB on `apmain:3306`; the response returns along the same
path. HOST configures `DBSERV_URL=https://palsys.softshell-grue.ts.net`;
the DB client normalizes the trailing slash and appends `/db/...`.
Funnel reachability and its forwarding target have not been tested or
configured by this project.

The host signs short-lived RS256 tokens with its private key; the database API
validates them using the matching trusted public key. See
`server/database/README.md` for key provisioning and the shared
POST/GET/PATCH/DELETE/aggregation request contract. Set `DBSERV_URL`, `JWT_ISSUER`,
`JWT_AUDIENCE`, `JWT_PRIVATE_KEY_PATH`, and `JWT_KEY_ID` in `api/.env` (the
legacy `api/src/api/db/.env.config` path is also supported) or the process
environment.
The detailed database server documentation is at `../../../server/database/README.md`.


```mermaid
flowchart TD
    START([Open Web App]) --> LANDING[Landing / Welcome Page]

    LANDING --> LOGIN[Login]
    LANDING --> REGISTER[Create Account]
    LANDING --> HELP[Help / Contact]

    %% =========================
    %% AUTHENTICATION
    %% =========================

    LOGIN --> CREDENTIALS[Enter Email / Username + Password]

    CREDENTIALS --> AUTH{Credentials Valid?}

    AUTH -->|No| LOGIN_FAIL[Authentication Failed]
    LOGIN_FAIL --> RETRY{Retry?}
    RETRY -->|Yes| CREDENTIALS
    RETRY -->|No| LANDING

    AUTH -->|Yes| MFA_CHECK{MFA Required?}

    MFA_CHECK -->|Yes| MFA[Enter Verification Code]
    MFA --> MFA_VALID{Code Valid?}

    MFA_VALID -->|No| MFA_FAIL[Invalid / Expired Code]
    MFA_FAIL --> MFA_RETRY{Retry / Resend?}
    MFA_RETRY -->|Retry| MFA
    MFA_RETRY -->|Resend| MFA_SEND[Send New Code]
    MFA_SEND --> MFA
    MFA_RETRY -->|Cancel| LANDING

    MFA_VALID -->|Yes| SESSION[Create Secure Session]

    MFA_CHECK -->|No| SESSION

    %% =========================
    %% ACCOUNT STATUS
    %% =========================

    SESSION --> ACCOUNT_STATUS{Account Status}

    ACCOUNT_STATUS -->|Unverified| VERIFY[Account Verification]
    VERIFY --> VERIFY_METHOD{Verification Method}

    VERIFY_METHOD -->|Email| EMAIL_VERIFY[Email Verification]
    VERIFY_METHOD -->|SMS| SMS_VERIFY[SMS Verification]
    VERIFY_METHOD -->|Identity| ID_VERIFY[Identity Verification]

    EMAIL_VERIFY --> VERIFIED{Verification Successful?}
    SMS_VERIFY --> VERIFIED
    ID_VERIFY --> VERIFIED

    VERIFIED -->|No| VERIFY_FAIL[Verification Failed]
    VERIFY_FAIL --> VERIFY_RETRY{Retry?}
    VERIFY_RETRY -->|Yes| VERIFY
    VERIFY_RETRY -->|No| LOGOUT[Logout]

    VERIFIED -->|Yes| AUTHORIZED[Account Authorized]

    ACCOUNT_STATUS -->|Active| AUTHORIZED

    ACCOUNT_STATUS -->|Locked| LOCKED[Account Locked]
    LOCKED --> RECOVERY[Account Recovery]

    AUTHORIZED --> ROLE{Determine User Role}

    ROLE -->|Client| CLIENT_APP[Client Interface]
    ROLE -->|Support| SUPPORT_APP[Support Interface]
    ROLE -->|Admin| ADMIN_APP[Administration Interface]

    %% =========================
    %% CLIENT
    %% =========================

    CLIENT_APP --> CLIENT_HOME[Client Main Menu]

    CLIENT_HOME --> CLIENT_DASH[Dashboard]
    CLIENT_HOME --> CLIENT_SUPPORT[Support]
    CLIENT_HOME --> CLIENT_TICKETS[My Requests]
    CLIENT_HOME --> CLIENT_MESSAGES[Messages]
    CLIENT_HOME --> CLIENT_NOTIFICATIONS[Notifications]
    CLIENT_HOME --> CLIENT_PROFILE[Profile]
    CLIENT_HOME --> CLIENT_SETTINGS[Settings]

    %% =========================
    %% SUPPORT
    %% =========================

    SUPPORT_APP --> SUPPORT_HOME[Support Main Menu]

    SUPPORT_HOME --> SUPPORT_DASH[Support Dashboard]
    SUPPORT_HOME --> QUEUE[Support Queue]
    SUPPORT_HOME --> CASES[Cases / Tickets]
    SUPPORT_HOME --> CONVERSATIONS[Conversations]
    SUPPORT_HOME --> CLIENTS[Clients]
    SUPPORT_HOME --> ESCALATIONS[Escalations]
    SUPPORT_HOME --> KNOWLEDGE[Knowledge Base]
    SUPPORT_HOME --> SUPPORT_NOTIFICATIONS[Notifications]
    SUPPORT_HOME --> SUPPORT_PROFILE[Profile]
    SUPPORT_HOME --> SUPPORT_SETTINGS[Settings]

    %% =========================
    %% SETTINGS
    %% =========================

    CLIENT_SETTINGS --> SETTINGS[Settings]

    SUPPORT_SETTINGS --> SETTINGS

    SETTINGS --> ACCOUNT_SETTINGS[Account]
    SETTINGS --> SECURITY_SETTINGS[Security]
    SETTINGS --> NOTIFICATION_SETTINGS[Notifications]
    SETTINGS --> PRIVACY_SETTINGS[Privacy]
    SETTINGS --> ACCESS_SETTINGS[Accessibility]
    SETTINGS --> APPEARANCE[Appearance]
    SETTINGS --> SESSIONS[Active Sessions]

    %% =========================
    %% SECURITY
    %% =========================

    SECURITY_SETTINGS --> CHANGE_PASSWORD[Change Password]
    SECURITY_SETTINGS --> MFA_SETTINGS[MFA Settings]
    SECURITY_SETTINGS --> RECOVERY_SETTINGS[Recovery Methods]

    CHANGE_PASSWORD --> PASSWORD_VERIFY[Verify Identity]
    PASSWORD_VERIFY --> NEW_PASSWORD[Set New Password]
    NEW_PASSWORD --> SECURITY_SETTINGS

    %% =========================
    %% CLIENT SUPPORT FLOW
    %% =========================

    CLIENT_SUPPORT --> NEW_REQUEST[Create Support Request]

    NEW_REQUEST --> CATEGORY[Select Healthcare Support Category]
    CATEGORY --> DETAILS[Enter Request Details]
    DETAILS --> ATTACHMENTS[Optional Attachments]
    ATTACHMENTS --> CONSENT[Consent / Privacy Confirmation]
    CONSENT --> SUBMIT[Submit Request]

    SUBMIT --> TICKET_CREATED[Support Case Created]
    TICKET_CREATED --> CLIENT_TICKETS

    %% =========================
    %% SUPPORT CASE FLOW
    %% =========================

    QUEUE --> CASE_ASSIGN[Assign / Claim Case]
    CASE_ASSIGN --> CASE_VIEW[Open Case]

    CASES --> CASE_VIEW
    CLIENTS --> CLIENT_VIEW[Client Profile]
    CLIENT_VIEW --> CASE_VIEW

    CASE_VIEW --> TRIAGE[Case Triage]

    TRIAGE --> PRIORITY[Determine Priority]
    PRIORITY --> ROUTING[Route to Appropriate Support]

    ROUTING --> CONVERSATION[Start / Continue Conversation]

    CONVERSATION --> RESOLVE_CHECK{Resolved?}

    RESOLVE_CHECK -->|No| ESCALATION_CHECK{Needs Escalation?}

    ESCALATION_CHECK -->|No| CONVERSATION
    ESCALATION_CHECK -->|Yes| ESCALATE[Escalate Case]

    ESCALATE --> SPECIALIST[Specialist / Supervisor]
    SPECIALIST --> CONVERSATION

    RESOLVE_CHECK -->|Yes| RESOLUTION[Document Resolution]
    RESOLUTION --> CLOSE[Close Case]

    CLOSE --> CLIENT_FEEDBACK[Client Feedback]
    CLIENT_FEEDBACK --> COMPLETE[Case Completed]

    %% =========================
    %% LOGOUT
    %% =========================

    CLIENT_HOME --> LOGOUT
    SUPPORT_HOME --> LOGOUT
    SETTINGS --> LOGOUT

    LOGOUT --> SESSION_END[Terminate Session]
    SESSION_END --> LANDING
```