#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

ENVIRONMENT="${1:-}"
case "$ENVIRONMENT" in
  dev|staging) ;;
  *) echo 'Usage: bash scripts/deploy-runtime-env.sh <dev|staging>' >&2; exit 2 ;;
esac

[[ -f .env ]] || { echo 'Missing .env. Run bootstrap-runtime-env.sh first.' >&2; exit 1; }
get_env() { sed -n "s/^${1}=//p" .env | tail -n 1; }

CONFIG_ENV=$(get_env ZETRUV_ENVIRONMENT)
[[ "$CONFIG_ENV" == "$ENVIRONMENT" ]] || { echo ".env belongs to '$CONFIG_ENV', not '$ENVIRONMENT'." >&2; exit 1; }

CURRENT_BRANCH=$(git branch --show-current)
if [[ "$ENVIRONMENT" == dev ]]; then
  [[ "$CURRENT_BRANCH" == dev ]] || { echo "DEV deployment must run from branch 'dev' (current: $CURRENT_BRANCH)." >&2; exit 1; }
else
  [[ "$CURRENT_BRANCH" == main || "$CURRENT_BRANCH" == release/* ]] || { echo "STAGING deployment must run from 'main' or 'release/*' (current: $CURRENT_BRANCH)." >&2; exit 1; }
fi

PROJECT=$(get_env COMPOSE_PROJECT_NAME)
API_PORT=$(get_env API_PORT)
DB_NAME=$(get_env POSTGRES_DB)
ASPNET_ENV=$(get_env ASPNETCORE_ENVIRONMENT)
FORWARDED=$(get_env FORWARDED_HEADERS_ENABLED)

[[ -n "$PROJECT" && -n "$API_PORT" && -n "$DB_NAME" ]] || { echo 'Required runtime values are missing from .env.' >&2; exit 1; }
[[ "$FORWARDED" == true ]] || { echo 'FORWARDED_HEADERS_ENABLED must be true behind Nginx.' >&2; exit 1; }
if [[ "$ENVIRONMENT" == dev ]]; then
  [[ "$ASPNET_ENV" == Development ]] || { echo 'DEV must use ASPNETCORE_ENVIRONMENT=Development.' >&2; exit 1; }
  [[ "$API_PORT" == 8081 ]] || { echo 'DEV API_PORT must remain 8081 on this VPS layout.' >&2; exit 1; }
else
  [[ "$ASPNET_ENV" == Staging ]] || { echo 'STAGING must use ASPNETCORE_ENVIRONMENT=Staging.' >&2; exit 1; }
  [[ "$API_PORT" == 8082 ]] || { echo 'STAGING API_PORT must remain 8082 on this VPS layout.' >&2; exit 1; }
fi

JWT_KEY=$(get_env JWT_KEY)
[[ ${#JWT_KEY} -ge 32 ]] || { echo 'JWT_KEY must be at least 32 characters.' >&2; exit 1; }
for key in POSTGRES_PASSWORD CMS_ADMIN_EMAIL CMS_ADMIN_PASSWORD PAYMENTS_MOCK_WEBHOOK_SECRET; do
  value=$(get_env "$key")
  [[ -n "$value" && "$value" != *change-me* && "$value" != replace-with-* ]] || { echo "$key is missing or unsafe." >&2; exit 1; }
done

SHA=$(git rev-parse --short HEAD)
echo "=== DEPLOY $ENVIRONMENT ==="
echo "Branch: $CURRENT_BRANCH"
echo "Commit: $SHA"
echo "Compose project: $PROJECT"
echo "Database: $DB_NAME"
echo "API bind: 127.0.0.1:$API_PORT"

docker compose --project-name "$PROJECT" --env-file .env up -d --build --remove-orphans

healthy=0
for _ in $(seq 1 90); do
  if curl -fsS "http://127.0.0.1:$API_PORT/health" >/dev/null 2>&1; then healthy=1; break; fi
  sleep 1
done
[[ "$healthy" == 1 ]] || { echo "$ENVIRONMENT API did not become healthy within 90s." >&2; docker compose --project-name "$PROJECT" --env-file .env ps; exit 1; }

API_CONTAINER=$(docker compose --project-name "$PROJECT" --env-file .env ps -q api)
POSTGRES_CONTAINER=$(docker compose --project-name "$PROJECT" --env-file .env ps -q postgres)
[[ -n "$API_CONTAINER" && -n "$POSTGRES_CONTAINER" ]] || { echo 'Expected API/Postgres containers are missing.' >&2; exit 1; }

BIND=$(docker port "$API_CONTAINER" 8080/tcp)
[[ "$BIND" == "127.0.0.1:$API_PORT" ]] || { echo "Unsafe/unexpected API bind: $BIND" >&2; exit 1; }

VOLUME="${PROJECT}_postgres_data"
docker volume inspect "$VOLUME" >/dev/null 2>&1 || { echo "Expected isolated volume missing: $VOLUME" >&2; exit 1; }

curl -fsS "http://127.0.0.1:$API_PORT/health"; echo
docker compose --project-name "$PROJECT" --env-file .env ps

echo "PASS: $ENVIRONMENT is healthy, branch-gated, localhost-only, and uses isolated PostgreSQL volume $VOLUME"
