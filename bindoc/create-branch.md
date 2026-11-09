# Creating a branch

Use a separate branch for each feature, fix, or documentation change. Do not make routine changes directly on `main`.

## Start from the latest `main`

Save or commit any work already in progress before switching branches. Then update your local `main`:

```bash
git switch main
git pull --ff-only origin main
```

Create and switch to a descriptive branch. Suggested prefixes are `feature/`, `fix/`, `docs/`, and `chore/`:

```bash
git switch -c docs/update-setup-guide
```

Use lowercase words separated by hyphens, and keep the name specific to the work.

## Work and publish

Check the current branch and working tree before editing:

```bash
git status --short --branch
```

After making and verifying your changes, stage only the intended files and commit:

```bash
git add path/to/changed-file
git commit -m "docs: update setup guide"
```

Replace the example path with the file or files you intend to commit.

Push the branch and set its upstream on the first push:

```bash
git push -u origin docs/update-setup-guide
```

Open a pull request with `main` as the base branch. Include a short summary and the checks you ran. Follow [Merging a branch](merge-branch.md) to update and merge your work.

If the repository's default branch or remote name differs from `main` or `origin`, confirm the correct names with the project maintainer before running the commands.
