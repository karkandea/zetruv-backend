#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

for script in scripts/bootstrap-runtime-env.sh scripts/deploy-runtime-env.sh scripts/install-runtime-nginx.sh scripts/bootstrap-vps-runtime-layout.sh scripts/smoke-runtime-separation.sh; do
  bash -n "$script"
done

TMP=$(mktemp -d)
trap 'rm -rf "$TMP"' EXIT
mkdir -p "$TMP/scripts"
cp docker-compose.yml "$TMP/docker-compose.yml"
cp scripts/bootstrap-runtime-env.sh "$TMP/scripts/bootstrap-runtime-env.sh"

check_env() {
  local env="$1" expected_project="$2" expected_port="$3" expected_db="$4" expected_aspnet="$5"
  rm -f "$TMP/.env"
  (cd "$TMP" && bash scripts/bootstrap-runtime-env.sh "$env" >/dev/null)
  grep -Fxq "ZETRUV_ENVIRONMENT=$env" "$TMP/.env"
  grep -Fxq "COMPOSE_PROJECT_NAME=$expected_project" "$TMP/.env"
  grep -Fxq "API_PORT=$expected_port" "$TMP/.env"
  grep -Fxq "POSTGRES_DB=$expected_db" "$TMP/.env"
  grep -Fxq "ASPNETCORE_ENVIRONMENT=$expected_aspnet" "$TMP/.env"
  [[ "$(stat -c '%a' "$TMP/.env")" == 600 ]]
  (cd "$TMP" && docker compose --project-name "$expected_project" --env-file .env config >/dev/null)
}

check_env dev zetruv-dev 8081 zetruv_dev Development
DEV_JWT=$(sed -n 's/^JWT_KEY=//p' "$TMP/.env")
DEV_DB_PASSWORD=$(sed -n 's/^POSTGRES_PASSWORD=//p' "$TMP/.env")
check_env staging zetruv-staging 8082 zetruv_staging Staging
STAGING_JWT=$(sed -n 's/^JWT_KEY=//p' "$TMP/.env")
STAGING_DB_PASSWORD=$(sed -n 's/^POSTGRES_PASSWORD=//p' "$TMP/.env")

[[ "$DEV_JWT" != "$STAGING_JWT" ]]
[[ "$DEV_DB_PASSWORD" != "$STAGING_DB_PASSWORD" ]]

echo 'PASS: DEV/STAGING scripts parse, generated env files are isolated, permissions are 600, and both Compose configs are valid.'
