# Libraries

This file records runtime and development libraries as they are introduced.

| Library | Purpose | Why it was added |
|---|---|---|
| `express` | HTTP API routing and middleware | Provides the small, conventional Node.js API layer required for the service. |
| `mysql2` | MySQL connection pool and queries | Implements the initial MySQL database adapter while keeping access behind repositories. |
| `argon2` | Password hashing and verification | Provides Argon2id, an appropriate password-hashing algorithm. |
| `jose` | JWT signing and verification | Provides standards-oriented JWT operations with issuer, audience, and expiry validation. |
| `yaml` | YAML configuration parsing | Reads the required `config-db.yaml` file. |
| `zod` | Request and environment validation | Rejects malformed input and incomplete configuration at boundaries. |
| `helmet` | Secure HTTP response headers | Adds baseline browser-facing security headers. |
| `supertest` | HTTP integration testing | Exercises the Express API without requiring a listening public port. |
