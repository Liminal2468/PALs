# Architecture

The service is a backend-only account and authentication service. Nginx terminates TLS and proxies private HTTP traffic to Node.js. Node.js owns request validation, authentication, authorization, account operations, and token lifecycle. The database owns durable account and session state.

Database access is isolated behind configuration, a connection pool, and repositories. The initial adapter is MySQL; application services do not issue database-specific queries directly.

There is no public registration endpoint. Administrators create accounts through the protected `/admin/accounts` API.

Access JWTs are short-lived. Refresh tokens are opaque, stored only as hashes, rotated on use, and revocable through the `sessions` table. A future WebSocket service can validate the same access JWT and use its `sub` claim as the authenticated account ID.
