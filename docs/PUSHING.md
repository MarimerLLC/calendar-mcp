# Preparing Changes for Push

This guide covers how to get local work committed and pushed safely. It reflects the state of this
fork, which tracks the upstream `MarimerLLC/calendar-mcp` repository and is not written to by the
upstream project.

## Table of Contents

- [Repository model](#repository-model)
- [Before you start](#before-you-start)
- [Branch workflow](#branch-workflow)
- [Secret audit](#secret-audit)
- [Verify the build](#verify-the-build)
- [Commit](#commit)
- [Push](#push)
- [Opening a pull request](#opening-a-pull-request)
- [Troubleshooting](#troubleshooting)

## Repository model

| Remote | URL | Push access |
|---|---|---|
| `origin` | `https://github.com/MarimerLLC/calendar-mcp.git` | None — upstream project |

Because `origin` is the upstream project, a plain `git push` will be rejected. Push to a fork:

```bash
git remote add fork https://github.com/<your-user>/calendar-mcp.git
git remote -v
```

Keep `origin` pointing at upstream so `git fetch origin` still tracks releases, and push to `fork`.

## Before you start

```bash
git status
git branch --show-current
git log --oneline -10
git fetch origin
```

Upstream history uses conventional commits with a pull-request number, for example
`Release 1.8.3 (#105)` and `Find moved messages: folder parameter, new IDs from moves, IMAP errors
(#102)`. Follow that style.

## Branch workflow

```bash
git checkout -b feature/ews-onprem-provider
```

Do not commit to `main` directly. Keep changes in focused branches so a pull request can be
reviewed as a unit.

## Secret audit

Run this before every commit:

```bash
git status --porcelain -uall
git check-ignore -v config/appsettings.json
```

Requirements:

- `config/appsettings.json` matches an ignore rule and is never staged;
- `config/keys/` and `config/logs/` are ignored;
- only `config/appsettings.example.json` appears under `config/`;
- no real hostnames, email addresses, or passwords appear in tracked files.

The ignore rules live in `.gitignore`:

```
config/appsettings.json
config/keys/
```

To confirm nothing sensitive is already tracked:

```bash
git ls-files config/
```

Any entry other than `appsettings.example.json` needs review before pushing.

## Verify the build

```bash
dotnet build calendar-mcp.slnx --configuration Release
dotnet test calendar-mcp.slnx --configuration Release
```

For the EWS provider, also confirm no vulnerable packages are pulled in:

```bash
dotnet list calendar-mcp.slnx package --vulnerable --include-transitive
```

## Commit

Stage intentionally — never `git add -A`:

```bash
git add .gitignore README.md docs/ src/
git status
git diff --cached --stat
```

Read the staged diff before committing, and check that no credentials are in it:

```bash
git diff --cached | rg -n -i "password|pwd=|secret|token"
```

Every hit needs to be a placeholder, a documentation description, or a variable name.

Commit message:

```bash
git commit -m "Add on-premises Exchange EWS provider with free/busy support"
```

## Push

```bash
git push -u fork feature/ews-onprem-provider
```

Never force-push a shared branch. If history needs rewriting locally, `git push --force-with-lease`
on a branch only you use is acceptable.

## Opening a pull request

```bash
gh pr create --base main --head feature/ews-onprem-provider \
  --title "Add on-premises Exchange (EWS) provider" \
  --body "Adds an EWS provider for on-premises Exchange, a find_available_times tool, and setup documentation."
```

Include in the PR description:

- the providers and tools added or changed;
- test and build results;
- documented limitations, such as attachment bytes not being supported for EWS;
- deployment notes from [DEPLOYMENT.md](DEPLOYMENT.md).

## Troubleshooting

**`fatal: unable to access ... Permission denied` on push**
The remote is upstream. Add a fork remote and push there instead.

**Commit rejected by a hook**
Fix the reported problem and create a new commit. Do not amend a failed commit and do not skip
hooks.

**A secret slipped into a commit**
Assume it is compromised: rotate the credential first, then remove it from history. Rewriting
history does not un-publish the old value.