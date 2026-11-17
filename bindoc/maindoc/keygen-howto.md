# HOST-to-DBSERV key generation and rotation

The new .NET services preserve the former trust boundary: MENTOS signs
short-lived service credentials and DBSERV verifies only the matching public
key. Private keys must stay outside source control, `public/`, and deployment
archives.

Key generation is not yet implemented in the new .NET projects. Until the
general database authentication layer is added, do not copy the old Node-line
key generator into the active tree. The previous procedure and contract are
preserved as [node-auth-contract.md](../build/v2-nodeline/builddocs/node-auth-contract.md)
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
