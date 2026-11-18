# Pre-established PALs authentication flows

This diagram documents what is active now and separates it from the planned
PALs OAuth/OIDC flow. Google Sign-In and Microsoft Entra are not active
providers. Google Authenticator is only a compatible TOTP client for the
PALs-owned Identity account.

## Current admin/developer flow through Mentos

```mermaid
flowchart TB
    Start["User opens Mentos login"]
    Credentials["Email + password\noptional TOTP code"]
    HOST["Mentos\nPOST /api/v1/auth/admin/login"]
    Private["DBSERV\nPOST /internal/v1/auth/admin/login"]
    Identity["ASP.NET Identity\npassword, lockout, role"]
    MFA{"TOTP enabled?"}
    Code["Verify mfaCode\nASP.NET Identity provider"]
    Account["Check PALs account active\nand privileged role"]
    Session["Create web_sessions row"]
    Cookie["Set pals_host_session\nHttpOnly cookie"]
    Failure["Sanitized authentication error"]
    Success["Protected Mentos API access"]

    Start --> Credentials --> HOST --> Private --> Identity
    Identity -->|"invalid"| Failure
    Identity --> MFA
    MFA -->|"no"| Account
    MFA -->|"yes"| Code
    Code -->|"invalid, missing, or replayed"| Failure
    Code -->|"valid"| Account
    Account -->|"inactive or wrong role"| Failure
    Account -->|"active and allowed"| Session --> Cookie --> Success
```

## Direct DBSERV developer interface flow

```mermaid
sequenceDiagram
    participant Browser as server/public interface
    participant Dev as DeveloperController
    participant Identity as ASP.NET Identity
    participant Replay as identity_mfa_replays

    Browser->>Dev: POST /dev/login
    Dev->>Identity: PasswordSignInAsync
    alt Password valid and TOTP disabled
        Identity-->>Dev: Authenticated
        Dev-->>Browser: Identity application cookie
    else Password valid and TOTP enabled
        Identity-->>Dev: mfa_required
        Browser->>Dev: POST /dev/mfa/verify
        Dev->>Identity: Verify authenticator code
        Dev->>Replay: Store short-lived code digest
        Replay-->>Dev: New code or replay rejected
        Dev->>Identity: Complete two-factor sign-in
        Identity-->>Dev: Authenticated
        Dev-->>Browser: Identity application cookie
    else Invalid credentials
        Dev-->>Browser: Generic authentication error
    end
```

## Current student compatibility flow

```mermaid
flowchart LR
    Student["Student browser"]
    HOST["Mentos\nPOST /api/v1/auth/login"]
    Format["Validate\nLASTNAME.STUID@cubao.sti.edu.ph\nSTUID >= 6 digits + terms"]
    Private["DBSERV\nPOST /internal/v1/auth/login"]
    Provision["Find or provision PALs account"]
    Session["Create web_sessions row"]
    Cookie["Set pals_host_session"]
    Warning["Compatibility facade\nnot proof of mailbox ownership"]

    Student --> HOST --> Format
    Format -->|"valid"| Private --> Provision --> Session --> Cookie
    Format -->|"invalid"| Warning
    Provision --> Warning
```

## Planned OAuth/OIDC flow — not implemented

```mermaid
flowchart LR
    Browser["Browser"]
    HOST["Mentos BFF\nauth-start + callback"]
    Auth["PALs authorization server\nprivate DBSERV-backed flow"]
    Identity["ASP.NET Identity\npassword + TOTP"]
    Code["Authorization code\nPKCE S256"]
    Session["DBSERV web session"]

    Browser --> HOST
    HOST -. "planned redirect" .-> Auth
    Auth -. "planned authentication" .-> Identity
    Identity -. "planned code" .-> Code
    Code -. "planned server-side exchange" .-> HOST
    HOST -. "planned session creation" .-> Session
    Session -. "planned opaque cookie" .-> Browser
```

The dashed flow is a design target from the OAuth/OIDC implementation plan. It
must not be treated as an active route until the authorization-server,
transaction-store, key, redirect, and end-to-end tests are implemented.
