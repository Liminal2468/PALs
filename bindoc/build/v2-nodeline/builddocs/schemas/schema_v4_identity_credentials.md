# Database-backed developer identity migration

The `users` and `roles` rows for `reime@pals.me` and `indev.sys@pals.me` do not
contain password credentials. An email plus a `developer` role is not enough to
authenticate a person.

The .NET developer login therefore fails closed until ASP.NET Identity is
configured against the general database. Passwords must be stored as Identity
password hashes in the Identity credential tables, never in `.env`, frontend
files, or SQL seed text.

Required migration work:

1. Add ASP.NET Core Identity and its MySQL/MariaDB EF Core provider.
2. Map Identity users to the existing PALs account records, or define an
   explicit `ApplicationUser`/`users` mapping after schema review.
3. Assign the `developer` role to the two existing accounts.
4. Create passwords through an administrative Identity operation, never by
   inserting plaintext passwords.
5. Issue the HttpOnly developer session only after Identity verifies the hash.

Until those steps are complete, `/dev/login` returns
`database_identity_not_ready` and no environment-backed developer password is
accepted.
