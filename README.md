# PALs


<div align="center">
  <img src="bindoc/corefile/assets/somshi.svg" width="100%" />
</div>
<p align="center"><sub>A Peer-Assisted Learning system web application for STI College Cubao's SHS student population.</sub></p>

This is private contributed from STI College Cubao. Also to prevent Overflow in the main branch. <sub>if you require help with merging through git please refer to [Merge Branch](bindoc/merge-branch.md)</sub>

The current application runs as two .NET services: DBSERV in [server/](server/)
and the browser-facing HOST in [mentos/](mentos/). The archived implementations
and historical documentation are preserved under [bindoc/](bindoc/).

> [!NOTE]
> THIS PROJECT REQUIRES INTERNET ACCESS TO REACT THE DATABASE SERVER <sup>[1](bindoc/maindoc/notes/apmain-whatis.md)</sup>

> [!IMPORTANT]
> The previous pals files are in [bindoc](bindoc/corefile)

## Installation

### Requirements

- Git
- .NET SDK `10.0.400` or newer
- MariaDB/MySQL access for database-backed features
- Access to the private PALs repository
- A private HOST-to-DBSERV shared key for local service-to-service requests

### Build


To build the Linux x64 DBSERV executable locally, run:

```bash
dotnet publish server/Palsys.Server.csproj -c Release -r linux-x64 --self-contained true -o server/publish -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
```

Then make it executable with: `chmod 0755 server/publish/Palsys.Server`

To install the service using the current project location, run:
```bash
sudo ./server/install-service.sh
```

To run the published executable directly from any checkout location, run: `./server/run-published`

```bash
git clone "PASTE_THE_REPOSITORY_CLONE_URL"
cd REPOSITORY_FOLDER
dotnet --version
```

Replace the clone URL and `REPOSITORY_FOLDER` with the URL and folder name for your checkout.

The project has two separate ASP.NET Core applications. Start DBSERV before Mentos because the HOST forwards authenticated data requests to it.

First configure and start DBSERV:

```bash
cp server/config.example.yaml server/config.yaml
cp server/.example.env server/.env
cp mentos/.example.env mentos/.env
# Edit server/config.yaml for non-secret settings.
# Set DB_PASSWORD, HOST_SHARED_KEY, and a private SHARED_SECRET in server/.env.

cd server
./run
```

DBSERV listens by default at `http://127.0.0.1:8432`. Apply the database schemas using a MariaDB administrator before testing persistent login, sessions, schedule, or feature data. The schema and least-privilege setup are documented in [`server/database/DATABASE.md`](server/database/DATABASE.md).

In a second terminal, configure and start Mentos:

```bash
cd REPOSITORY_FOLDER/mentos
cp config.example.yaml config.yaml
# Set HOST_SHARED_KEY in .env to match server/.env.
./run
```

Mentos listens by default at `http://127.0.0.1:8000/` and serves the frontend from `mentos/public/`. Press Ctrl-C in each terminal to stop the corresponding service.

Keep `.env` files, `config.yaml` files, shared keys, database passwords, and Data Protection keys out of source control. Both applications read non-empty values from their local YAML file; omitted or blank values are ignored, and existing process environment values take precedence. `DB_PASSWORD` is deliberately ignored in YAML and must remain in `server/.env` or the process environment. On Windows, use WSL or run the equivalent `dotnet run` commands from [Running Mentos](bindoc/maindoc/notes/mentos-whatis.md).

> [!NOTE]
> For Tailscale setup and access requirements, see [What is APMAIN?](bindoc/maindoc/notes/apmain-whatis.md). For creating a feature branch, merging work, and the basic CI/CD process, see [Create a branch](bindoc/create-branch.md), [Merge a branch](bindoc/merge-branch.md), and [Basic CI/CD flow](bindoc/basic-cicdflow.md).


> [!NOTE]
> The Tailscale is OPTIONAL as any other TLS can also be used as a means of Secure Data transport without the `HOST` interacting with the database directly.

**Notice**

Upon version 2.0 to 3.0 the development transitioned to a new backend.

### **Backend Stack**

| Category             | Technology              |
| -------------------- | ----------------------- |
| Framework            | ASP.NET Core MVC (.NET) |
| Programming Language | C#                      |
| ORM                  | Entity Framework Core   |
| Database             | MySQL, or other database|
| Authentication       | ASP.NET Core Identity   |

Some features remain under development. The active email-verification flow is
implemented in C# in DBSERV and sends six-digit codes through AWS SES; the
archived Python sender is only a historical reference, not part of the live
login path.

> [!IMPORTANT]
> PALs now runs as two ASP.NET Core applications; the archived Python and
> Node/Vite instructions are no longer the way to start the current app. Use
> the [.NET setup above](#installation): start DBSERV first, then Mentos in a
> second terminal. The default local URLs are `http://127.0.0.1:8432` for
> DBSERV and `http://127.0.0.1:8000` for Mentos.
>
> Before starting them, copy `server/config.example.yaml` to
> `server/config.yaml`, `server/.example.env` to `server/.env`, and
> `mentos/config.example.yaml` to `mentos/config.yaml`. Set database and
> authentication secrets only in the ignored `.env` files; set the matching
> HOST/DBSERV shared key on both applications. See [server setup](server/README.md)
> and [Mentos setup](mentos/README.md) for all settings. Never copy production
> secrets or production configuration into a local checkout.

---
**Current Contributors**

[![Liminal2468](https://img.shields.io/badge/GitHub-Liminal2468-181717?style=flat-square&logo=github)](https://github.com/Liminal2468)
[![KeiNerokami](https://img.shields.io/badge/GitHub-KeiNerokami-181717?style=flat-square&logo=github)](https://github.com/KeiNerokami)
