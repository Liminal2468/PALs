# Basic CI/CD flow

CI/CD means continuously integrating changes and, where deployment automation exists, delivering or deploying verified changes. Use this sequence for PALs:

1. **Branch:** create a focused feature or fix branch from the latest `main` ([Create a branch](create-branch.md)).
2. **Change and verify:** make a small, reviewable change. Run the relevant build, tests, and checks locally; document anything that could not be run.
3. **Commit and push:** commit the verified change and push the branch to the shared repository.
4. **Pull request:** open a pull request targeting `main`. Explain the change and verification, and review the complete diff.
5. **Continuous integration:** wait for the repository's configured CI checks and required reviews. Fix failures on the branch and push the follow-up commit; do not merge while required checks fail.
6. **Merge:** merge the approved pull request using the repository's configured method ([Merging a branch](merge-branch.md)).
7. **Delivery/deployment:** deploy only through a project-approved procedure after merge. Confirm the deployment is healthy and report or roll back according to the service's runbook if it is not.

## Current repository automation

No GitHub Actions workflow files are present in this checkout, so this document describes the workflow rather than claiming that CI checks or deployments run automatically. Confirm which checks and deployment steps are configured with the project maintainer before treating a pull request or merge as automatically verified or deployed.

>[!WARNING] **Never put `.env` files, private keys, provider credentials, or other secrets in commits or CI logs.**
