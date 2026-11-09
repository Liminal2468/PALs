# Merging a branch

This project uses `main` as the shared integration branch. Do feature work on a separate branch, update it from `main`, and merge through a reviewed pull request rather than pushing feature work directly to `main`.

## Update your feature branch

Replace `feature/my-change` with your branch name. If the repository uses a remote name other than `origin`, substitute that name in the commands.

```bash
git switch feature/my-change
git status
git fetch origin
git merge origin/main
```

If Git reports conflicts:

1. Run `git status` to see which files need attention.
2. Edit each conflicted file, keeping the intended changes from both branches and removing the conflict markers (`<<<<<<<`, `=======`, `>>>>>>>`).
3. Review the resolved files, then stage and complete the merge:

   ```bash
   git add path/to/resolved-file
   git commit
   ```

   Replace the example path with the actual resolved file paths.

4. Run the relevant tests and checks for the changes. If conflicts remain, do not push or open the pull request until they are resolved.

Push the updated branch:

```bash
git push origin feature/my-change
```

## Open and merge a pull request

1. Open a pull request with `main` as the base and your feature branch as the compare branch.
2. Describe the change, how it was verified, and any known limitations.
3. Review the diff and wait for required reviews and CI checks. Resolve requested changes on the same feature branch and push again.
4. Merge using the repository's configured merge method after approval and required checks pass.
5. After the merge, update your local `main` and optionally remove the feature branch:

   ```bash
   git switch main
   git pull --ff-only origin main
   git branch -d feature/my-change
   ```

If a pull request is unavailable and you have explicit permission to merge locally, switch to `main`, fast-forward it, merge the feature branch, run checks, then push:

```bash
git switch main
git pull --ff-only origin main
git merge --no-ff feature/my-change
# Run the relevant tests and checks before publishing.
git push origin main
```

Do not force-push shared branches or use `git push --force` to work around a rejected push. See [Create a branch](create-branch.md) for the branch setup workflow and [Basic CI/CD flow](basic-cicdflow.md) for the verification and release stages.