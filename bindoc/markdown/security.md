# Security decisions

- Passwords are hashed with Argon2id and are never stored or returned in plaintext.
- Password hashes and refresh tokens are never placed in JWT payloads.
- Refresh tokens are stored as SHA-256 hashes and revoked on logout, rotation, password change, or account disablement.
- JWTs validate signature, issuer, audience, and expiration.
- Every authenticated request checks that the account remains enabled.
- Administrator routes require an enabled account with the `admin` role.
- Account deletion is implemented as disablement so sessions can be revoked and records retained.
- Secrets are supplied through environment variables; `.env` and `node_modules` are ignored.
