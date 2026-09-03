# Zetruv backend environments

This VPS hosts non-production environments only. Final production remains on the client's server.

## Runtime topology

| Environment | Git source | VPS clone | Compose project | API bind | Database | Public domain |
| --- | --- | --- | --- | --- | --- | --- |
| DEV | `dev` | `/opt/zetruv-backend-dev` | `zetruv-dev` | `127.0.0.1:8081` | `zetruv_dev` | `api-dev.zetruv.dualangka.com` |
| STAGING | `main` or `release/*` | `/opt/zetruv-backend-staging` | `zetruv-staging` | `127.0.0.1:8082` | `zetruv_staging` | `api-staging.zetruv.dualangka.com` |

Each Compose project owns its own PostgreSQL container and named volume. Each clone owns a separate `.env`, PostgreSQL password, JWT signing key, CMS admin password, and webhook secret. Neither API port is exposed publicly; Nginx is the only public entry point.

The old `/opt/zetruv-backend` / port `8080` stack is intentionally left untouched while the new layout is introduced. Remove it only after both new environments have passed smoke tests and consumers have been switched.

## Branch / promotion model

1. Feature branches are opened from `dev` and merged back into `dev` after review/testing.
2. `dev` auto/manual deployment target is DEV (`api-dev...`). This environment may change frequently.
3. When a candidate is ready, promote `dev` into `main` through a PR. `main` is the STAGING source of truth.
4. Optional `release/*` branches may be used for release stabilization; the staging deploy guard accepts `main` or `release/*` only.
5. Production is not deployed from this VPS. A client production deployment should use a tested commit/tag from STAGING and production-only secrets/provider configuration.

Do not merge `main` back into `dev` by copying files manually. Use Git merges/PRs so commit ancestry remains auditable.

## Initial VPS setup

After this environment-separation change is merged to `main` and branch `dev` has been created from that merged commit:

```bash
bash scripts/bootstrap-vps-runtime-layout.sh
```

This creates the two clones if missing and generates independent `.env` files without touching the existing `/opt/zetruv-backend` stack.

Before running the Nginx installer, create DNS A records pointing to the current VPS:

- `api-dev.zetruv` -> VPS IPv4
- `api-staging.zetruv` -> VPS IPv4

## Deploy DEV

Sync the DEV clone explicitly to `origin/dev`, then deploy:

```bash
cd /opt/zetruv-backend-dev
git fetch origin dev
git reset --hard origin/dev
bash scripts/deploy-runtime-env.sh dev
sudo bash scripts/install-runtime-nginx.sh dev
```

## Deploy STAGING

Sync the STAGING clone explicitly to `origin/main`, then deploy:

```bash
cd /opt/zetruv-backend-staging
git fetch origin main
git reset --hard origin/main
bash scripts/deploy-runtime-env.sh staging
sudo bash scripts/install-runtime-nginx.sh staging
```

The Nginx installer is idempotent, validates public DNS, keeps a backup of an existing vhost, validates `nginx -t`, and uses Certbot when available.

## Isolation smoke

After both stacks are running:

```bash
bash /opt/zetruv-backend-staging/scripts/smoke-runtime-separation.sh
```

The smoke verifies different Compose projects, containers, localhost ports, PostgreSQL databases/volumes, DB credentials, and JWT keys, and confirms both `/health` endpoints respond.

## Secrets and provider policy

`.env` is never committed. Generated environment secrets remain mode `600` in each clone. DEV and STAGING must never share JWT, PostgreSQL, CMS admin, or webhook secrets.

The current backend still has external provider work pending, so DEV/STAGING may use mock providers. `ASPNETCORE_ENVIRONMENT=Production` remains reserved for the future client production environment, where mock providers are rejected and real provider configuration must be supplied.

When frontend environments are split later, change `FRONTEND_ORIGIN` and `CMS_ORIGIN` in each environment `.env` to the corresponding DEV/STAGING frontend/admin origins before cross-origin browser use.
