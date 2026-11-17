# Major changes

## 2026 .NET migration baseline

- Archived the previous implementation as `bindoc/build/v2-nodeline/`.
- Moved the current SQL schemas temporarily to
  `bindoc/build/v2-nodeline/builddocs/schemas/`.
- Initialized separate .NET 10 projects for MENTOS HOST and DBSERV using CLI
  version `10.0.400`.
- Preserved `mentos/public/` and `server/public/` as the only static web roots.
- Added `mentos/webend/` as the frontend backend boundary using the
  `*.{category}.cs` filename convention.
- Removed the old active Node/Python runtime trees; their source remains
  recoverable in the Node-line archive.
- Purged `palsys.service` and `palsys-web.service` from APMAIN.
- Replaced the old `server/run` launcher with a .NET launcher.

The new route shells are intentionally incomplete. Identity, general database
access, sessions, authorization, and feature controllers must be implemented
before real account data is connected to the frontend.

Developer login no longer accepts environment-backed passwords. It now fails
closed until ASP.NET Identity verifies credentials from the general database.
