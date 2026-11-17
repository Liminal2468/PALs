# Local .NET development context

The repository now contains two independent ASP.NET Core .NET 10 applications.
The required CLI version is `10.0.400`.

## MENTOS HOST

```bash
cd mentos
dotnet run --project Mentos.Host.csproj
```

Default listener: `http://127.0.0.1:8000`. The launcher equivalent is `./run`.

## DBSERV

```bash
cd server
dotnet run --project Palsys.Server.csproj
```

Default listener: `http://127.0.0.1:8432`. The launcher equivalent is `./run`.

Both applications serve only their own `public/` directory. Source files,
project files, credentials, and database files remain outside the web root.
The MENTOS `webend/` layer is the browser-facing backend boundary.

## Current migration state

The route shapes are preserved, but account, Identity, session, general
database, and feature-service implementations are not complete. The frontend
still contains demo content and browser-local state; it must be migrated to
same-origin `/api/v1` calls before real account data is displayed.

The previous Node/Python implementation is recoverable from
[build/v2-nodeline/](../build/v2-nodeline/).
