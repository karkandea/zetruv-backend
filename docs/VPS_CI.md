# VPS-native CI and manual promotion

Zetruv backend verification runs directly on the Ubuntu VPS. **No GitHub Actions workflow or GitHub-hosted runner is used.** This repository does not configure unattended CI triggers; an authorized operator runs checks for a pinned PR commit and records results in the PR.

## Prepare an isolated checkout

Do not check out feature branches or write generated files in the active `/opt/zetruv-backend-{dev,staging}` clones. They contain private, environment-specific configuration.

```bash
workdir="$(mktemp -d -p /tmp zetruv-ci-XXXXXX)"
git clone --single-branch --branch security/require-postgres-password-20261028 https://github.com/karkandea/zetruv-backend.git "$workdir"
cd "$workdir"
git rev-parse HEAD
```

For other PRs, replace the branch with the intended revision. Always verify the full commit SHA before recording test evidence. Keep the temporary checkout separate from active Docker Compose projects and delete it only after checking the exact path and preserving useful diagnostics.

## Verify the .NET build

```bash
dotnet restore src/Zetruv.Api/Zetruv.Api.csproj
dotnet build src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --no-restore
```

Require zero build errors. A build is not an integration test.

## Verify fail-closed Compose configuration

```bash
# Must fail because no POSTGRES_PASSWORD exists:
env -u POSTGRES_PASSWORD docker compose --env-file /dev/null config --quiet

# Use disposable credentials; do not print fully expanded Compose config:
export POSTGRES_PASSWORD="$(openssl rand -hex 24)"
export JWT_KEY="$(openssl rand -hex 32)"
export CMS_ADMIN_EMAIL="admin-smoke@zetruv.local"
export CMS_ADMIN_PASSWORD="$(openssl rand -hex 20)"
export PAYMENTS_MOCK_WEBHOOK_SECRET="$(openssl rand -hex 20)"
docker compose --env-file /dev/null config --quiet
```

The first command must return a nonzero status; the second must succeed. Never paste secrets into PR comments or build logs.

## Optional isolated Docker smoke

`scripts/smoke-staging-compose.sh` creates a disposable Compose project named `zetruv-staging-smoke` and removes its volume on exit. **Before running it**, verify no existing containers or volumes use that name and that the operator has explicitly authorized Docker access. Do not use `sudo` to bypass Docker socket permissions without approval. Runtime credentials and existing PostgreSQL volumes in DEV/STAGING/LEGACY must remain untouched.

## Promotion

PR approval and Git merging are separate from deployment. A merge to `main` does **not** invoke deployment automation in this repository. A designated VPS operator may later run the existing branch-gated `scripts/deploy-runtime-commit.sh` for an exact, approved SHA after reviewing and backing up live runtime state. Never run a deployment as part of this read-only verification procedure.
