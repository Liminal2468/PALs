# HOST-to-DBSERV key generation and rotation

The new .NET services preserve the former trust boundary: MENTOS signs
short-lived service credentials and DBSERV verifies only the matching public
key. Private keys must stay outside source control, `public/`, and deployment
archives.

## Local HOST shared-key generator

The active project includes `server/src/gen` for generating the symmetric
`HOST_SHARED_KEY` used by Mentos and DBSERV during local development. It is
not an RSA/RS256 signing-key generator and must not be used for OAuth signing
keys.

From any directory, generate a 0600 key file under that directory's restricted
`.keygen/` folder:

```bash
./gen create key
```

The default source length is `48` bytes passed to Python's
`secrets.token_urlsafe()`. The filename is `<unix-time>-<first-five-sha256>.pem`.
The `.keygen/` directory is mode `0700`, and the file contains the URL-safe
shared-secret value followed by a newline with mode `0600`.

To print a key instead of creating a file, confirmation is required:

```bash
./gen create key -N 48 --print
```

Copy the printed value into the ignored `HOST_SHARED_KEY` setting in both
`server/config.yaml` and `mentos/config.yaml`. Never place it in
`mentos/public/`, source control, logs, or browser JavaScript.

The previous asymmetric-key procedure and contract are preserved as
[node-auth-contract.md](../build/v2-nodeline/builddocs/node-auth-contract.md)
and in the archived source.

The future implementation must provide:

- RSA/RS256 or another approved asymmetric signing method;
- short-lived tokens with issuer, audience, subject, issued-at, expiry, key ID,
  resource, action, and scope claims;
- public-key rotation with overlap between old and new trusted keys;
- owner-only private-key permissions;
- no private-key material in browser responses or logs.

Until that implementation exists, use only local development configuration and
do not enable remote database writes.
