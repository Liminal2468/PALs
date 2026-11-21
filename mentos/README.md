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
# Set HOST_SHARED_KEY in config.yaml to match DBSERV.
./run
```

The default listener is `http://127.0.0.1:8000`. Override it with
`BIND_ADDRESS` and `PORT`, or set them in `config.yaml`. `DBSERV_URL` and
`HOST_SHARED_KEY` are also read from that file. Blank or omitted values are
ignored, and existing process environment values take precedence.

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
- `/api/v1/rec-peer-tutor`

Tutor recommendations remain deliberately unavailable until `/connect` and its
matching algorithm are approved. The longer recommendation paths remain
compatibility aliases for the canonical `/rec-peer-tutor` path.

## Development login prototype

The student login prototype is available only in a Debug build running with
`ASPNETCORE_ENVIRONMENT=Development`. It accepts the fixed account
`student.123456@cubao.sti.edu.ph`; the six-digit verification code is printed
to the server console. Code delivery is not connected to AWS SES yet. Release
builds and non-Development environments return an explicit unavailable
response rather than issuing a session. The attempt ID stays in an HttpOnly
cookie, and successful verification creates the DBSERV session and records
terms acceptance.

## Recovery

The previous HOST implementation is preserved in
[`bindoc/build/v2-nodeline/mentos.tar.gz`](../bindoc/build/v2-nodeline/mentos.tar.gz).
Its renamed documentation is under
[`bindoc/build/v2-nodeline/builddocs/`](../bindoc/build/v2-nodeline/builddocs/).
