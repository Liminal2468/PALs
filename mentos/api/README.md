# Mentos HOST API migration

The HOST API is being migrated to ASP.NET Core .NET 10 and is now hosted by
the parent `Mentos.Host.csproj`. The preserved public contract remains under
`/api/v1`; implementation work will add ASP.NET Identity, general database
access, server-side sessions, and the existing provider policies.

The previous Python/Flask source and tests are preserved in the Node-line
baseline at [`bindoc/build/v2-nodeline/mentos.tar.gz`](../../bindoc/build/v2-nodeline/mentos.tar.gz).
Do not use the old `.env` or key files as source-control material.
