# Frontend-to-backend workflow for agents

Use this document whenever a new PALs frontend is added or an existing
interface is migrated.

## 1. Identify the application boundary

- `mentos/public/` is the client-facing frontend.
- `mentos/webend/` contains HOST-side backend files using
  `*.{category}.cs` names.
- `server/public/` is the restricted developer interface.
- `server/Controllers/` contains DBSERV controllers.
- Never place C#, database configuration, private keys, or service credentials
  inside either public directory.

## 2. Inspect before editing

Search the new frontend for:

- `localStorage` and `sessionStorage`;
- hardcoded names, emails, counts, cards, schedules, and ratings;
- `demo`, `sample`, `placeholder`, and `not connected` text;
- `fetch()` calls and form submit handlers;
- role-specific navigation and route names.

Record each item as one of:

1. live backend data;
2. an intentional empty state;
3. a frontend-only preference such as theme selection; or
4. an unimplemented contract that requires backend work.

## 3. Define the contract first

For every live feature, define the same-origin API method, path, request body,
response envelope, authentication requirement, role policy, error codes, and
empty-state behavior before changing the HTML or JavaScript.

The browser calls MENTOS only. MENTOS calls DBSERV through a server-side
client. The browser never receives database credentials or DBSERV signing keys.

## 4. Replace placeholders safely

- Do not convert sample students into fake production accounts.
- Replace fabricated records with an API-backed loading state and a truthful
  empty state until the data endpoint exists.
- Keep mock data only in explicitly named test fixtures outside the public web
  root.
- Every write must have server-side authentication, authorization, validation,
  ownership checks, and audit requirements defined first.

## 5. Role testing

Test each role with a real server-issued session or approved test fixture:

- Developer: restricted system and telemetry controls;
- Admin: administrative account and moderation controls;
- Tutor: tutor-owned resources and eligible requests;
- Tutee: tutee-owned requests and permitted resources.

Do not use a client-supplied role field as authorization. The server must
derive roles from the authenticated account.

## 6. Static-file isolation

Both .NET applications must configure `public/` as the web root. Verify that
requests for `.cs`, `.csproj`, `.env`, `.pem`, `Controllers`, `webend`, `bin`,
`obj`, and `.git` paths return `404`. Verify that required HTML, CSS,
JavaScript, and image assets return `200`.

## 7. Verification checklist

Run:

```bash
dotnet build mentos/Mentos.Host.csproj
dotnet build server/Palsys.Server.csproj
```

Then verify unauthenticated, authenticated, unauthorized, empty-data, and
error responses for every changed route. Never claim a frontend feature is
live based only on a successful static-file response.
