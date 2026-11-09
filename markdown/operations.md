# Database API operations

## Server setup

From `server/`, run `./setup.sh`. It installs the locked Node dependencies and
creates `database/.env` from the example if needed. Configure MariaDB and JWT
trust values in that file; database creation and migrations are managed
separately. Verify the API with `npm test`, then start it with `npm start`.

## CI/CD

CI should run `npm ci` and `npm test` using injected environment variables.
Deployment should expose Node.js only on a private interface behind Nginx/TLS.
Production credentials and JWT secrets must come from the CI/CD secret store,
never from repository files.
