# Project context

PALs is a peer-assisted learning platform for STI College Cubao senior high
school students. The active implementation is split into two applications:

- **MENTOS HOST** — `mentos/`, an ASP.NET Core .NET 10 application serving the
  preserved frontend and the client-facing `/api/v1` contract.
- **DBSERV** — `server/`, a separate ASP.NET Core .NET 10 application for the
  general database service and preserved service prefixes.

MENTOS is the privacy boundary for browser clients. Browsers call MENTOS only;
MENTOS will call DBSERV through a restricted backend client. DBSERV must never
receive browser database credentials.

The database schemas are temporarily preserved in
[build/v2-nodeline/builddocs/schemas/](../build/v2-nodeline/builddocs/schemas/)
while the general database model is designed. The public route structures are
preserved during the migration.

For implementation research and unresolved product questions, see
[notes/projectnotes.md](notes/projectnotes.md).
