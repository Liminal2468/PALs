# Mentos HOST

Mentos is the PALs HOST application, now initialized as an ASP.NET Core .NET
10 project targeting CLI version `10.0.400`.

## Structure

- [`public/`](public/) contains static assets and login components.
- [`Views/Site/`](Views/Site/) and [`Views/Pals/`](Views/Pals/) contain the
  Razor-rendered site pages and allowlisted PALs partials.
- [`Program.cs`](Program.cs) configures MVC, session authentication, rate
  limiting, and static files.
- [`Controllers/`](Controllers/) exposes the `/api/v1` frontend façade. Business
  data is requested from DBSERV over the private `DBSERV_URL` connection and
  authenticated with `HOST_SHARED_KEY`.
- [`Mentos.Host.csproj`](Mentos.Host.csproj) is the .NET project entrypoint.

The old Python/Flask and Node/Vite runtime files were removed from the active
HOST tree after the Node-line archive was created.

## Run locally

```bash
cp config.example.yaml config.yaml
cp .example.env .env
# Set HOST_SHARED_KEY in .env to match server/.env.
./run
```

The default listener is `http://127.0.0.1:8000`. Override it with
`BIND_ADDRESS` and `PORT`, or set them in `config.yaml`. `DBSERV_URL` is read
from YAML; `HOST_SHARED_KEY` is read only from `.env` or the process
environment. Secret values in YAML are ignored, and process environment values
take precedence.

The browser routes are `/`, `/faqs`, `/contactsupport`, and `/pals`.
`/pals` and `/pals/partial/{name}` require a validated DBSERV session. The
partial route accepts only the fixed view names defined in `SiteController`.

The API route contract remains under `/api/v1`:

- `/api/v1/health`
- `/api/v1/session`
- `/api/v1/auth/*`
- `/api/v1/me`
- `/api/v1/learning-requests`
- `/api/v1/tutors` and `/api/v1/tutees`
- `/api/v1/sessions`
- `/api/v1/resources`
- `/api/v1/feedback` and `/api/v1/support`
- `/api/v1/analytics/*`
- `/api/v1/dashboard`
- `/api/v1/schedule`
- `/api/v1/connect/recommendations`
- `/api/v1/rec-peer-tutor` and `/api/v1/rec-peer-tutee`

Matching recommendations are ranked with the developer-selected algorithm and
returned without creating a connection. The legacy recommendation routes are
compatibility aliases; see [`../server/API.md`](../server/API.md) for the full
contract.

## Development login prototype

The student login flow accepts school accounts matching the documented email
policy in every environment. Accounts with an Identity password must enter
their password before completing six-digit email verification. Accounts
without a password verify their email first and can then optionally set a
password or skip setup. DBSERV sends codes through AWS SES; the code is not
printed or returned to the browser. The pending attempt ID stays in an HttpOnly
cookie, and successful verification creates the DBSERV session and records
terms acceptance. Password-only sign-in is not supported.
Student passwords require at least eight characters, including uppercase and
lowercase letters, a number, and a punctuation mark or symbol (spaces do not
count as symbols).

## Recovery

The previous HOST implementation is preserved in
[`bindoc/build/v2-nodeline/mentos.tar.gz`](../bindoc/build/v2-nodeline/mentos.tar.gz).
Its renamed documentation is under
[`bindoc/build/v2-nodeline/builddocs/`](../bindoc/build/v2-nodeline/builddocs/).
