# Mentos webend

`webend/` is the HOST-side backend boundary for the static PALs frontend. The
browser may request `/api/v1/*`, but it cannot address C# source, project files,
database credentials, or the general database directly.

## File convention

Backend files use `*.{category}.cs` names:

- `frontend.controller.cs` — browser-facing route contract
- `frontend.contract.cs` — response records
- `isolation.middleware.cs` — source and response isolation policy

Future files should follow the same convention, for example
`account.service.cs`, `session.store.cs`, `resource.controller.cs`, and
`telemetry.service.cs`.

## Frontend integration boundary

The existing pages currently use static demo cards and `localStorage` for
profiles, favorites, votes, and session state. The migration should replace
those paths with same-origin requests to these backend resources:

| Frontend concern | HOST endpoint | Required backend layer |
|---|---|---|
| Current account | `GET/PATCH /api/v1/me` | Identity + profile store |
| Tutor/tutee lists | `GET /api/v1/tutors`, `/tutees` | authorized profile queries |
| Sessions | `GET/POST/PATCH /api/v1/sessions` | scheduling service |
| Resources | `GET /api/v1/resources` | allowlisted resource service |
| Feedback | `GET/POST /api/v1/feedback` | ownership and moderation rules |
| Analytics | `GET /api/v1/analytics` | consent-aware telemetry service |

Until those layers are connected, the API returns explicit authentication or
service-not-ready responses instead of presenting generated student data as
real accounts.

## Isolation model

ASP.NET Core uses `public/` as the web root. Only static files below that
directory are addressable. The isolation middleware also rejects requests that
look like source, build, credential, or project paths and applies browser
security headers. The frontend and backend therefore share an origin without
sharing the backend filesystem.
