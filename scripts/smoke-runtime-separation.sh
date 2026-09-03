#!/usr/bin/env bash
set -euo pipefail

DEV_DIR="${DEV_DIR:-/opt/zetruv-backend-dev}"
STAGING_DIR="${STAGING_DIR:-/opt/zetruv-backend-staging}"

for dir in "$DEV_DIR" "$STAGING_DIR"; do
  [[ -d "$dir/.git" && -f "$dir/.env" ]] || { echo "Missing initialized environment: $dir" >&2; exit 1; }
done

get_env() { local dir="$1" key="$2"; sed -n "s/^${key}=//p" "$dir/.env" | tail -n 1; }

DEV_PROJECT=$(get_env "$DEV_DIR" COMPOSE_PROJECT_NAME)
STAGING_PROJECT=$(get_env "$STAGING_DIR" COMPOSE_PROJECT_NAME)
DEV_PORT=$(get_env "$DEV_DIR" API_PORT)
STAGING_PORT=$(get_env "$STAGING_DIR" API_PORT)
DEV_DB=$(get_env "$DEV_DIR" POSTGRES_DB)
STAGING_DB=$(get_env "$STAGING_DIR" POSTGRES_DB)
DEV_JWT=$(get_env "$DEV_DIR" JWT_KEY)
STAGING_JWT=$(get_env "$STAGING_DIR" JWT_KEY)
DEV_DB_PASSWORD=$(get_env "$DEV_DIR" POSTGRES_PASSWORD)
STAGING_DB_PASSWORD=$(get_env "$STAGING_DIR" POSTGRES_PASSWORD)

[[ "$DEV_PROJECT" != "$STAGING_PROJECT" ]] || { echo 'Compose project names are not isolated.' >&2; exit 1; }
[[ "$DEV_PORT" != "$STAGING_PORT" ]] || { echo 'API ports are not isolated.' >&2; exit 1; }
[[ "$DEV_DB" != "$STAGING_DB" ]] || { echo 'Database names are not isolated.' >&2; exit 1; }
[[ "$DEV_JWT" != "$STAGING_JWT" ]] || { echo 'JWT keys must not be shared.' >&2; exit 1; }
[[ "$DEV_DB_PASSWORD" != "$STAGING_DB_PASSWORD" ]] || { echo 'PostgreSQL passwords must not be shared.' >&2; exit 1; }

curl -fsS "http://127.0.0.1:$DEV_PORT/health" >/dev/null || { echo 'DEV health failed.' >&2; exit 1; }
curl -fsS "http://127.0.0.1:$STAGING_PORT/health" >/dev/null || { echo 'STAGING health failed.' >&2; exit 1; }

docker volume inspect "${DEV_PROJECT}_postgres_data" >/dev/null
docker volume inspect "${STAGING_PROJECT}_postgres_data" >/dev/null

DEV_API=$(cd "$DEV_DIR" && docker compose --project-name "$DEV_PROJECT" --env-file .env ps -q api)
STAGING_API=$(cd "$STAGING_DIR" && docker compose --project-name "$STAGING_PROJECT" --env-file .env ps -q api)
[[ -n "$DEV_API" && -n "$STAGING_API" && "$DEV_API" != "$STAGING_API" ]] || { echo 'API containers are not independently running.' >&2; exit 1; }

DEV_BIND=$(docker port "$DEV_API" 8080/tcp)
STAGING_BIND=$(docker port "$STAGING_API" 8080/tcp)
[[ "$DEV_BIND" == "127.0.0.1:$DEV_PORT" ]] || { echo "Unexpected DEV bind: $DEV_BIND" >&2; exit 1; }
[[ "$STAGING_BIND" == "127.0.0.1:$STAGING_PORT" ]] || { echo "Unexpected STAGING bind: $STAGING_BIND" >&2; exit 1; }

echo "DEV: $DEV_PROJECT / $DEV_DB / 127.0.0.1:$DEV_PORT"
echo "STAGING: $STAGING_PROJECT / $STAGING_DB / 127.0.0.1:$STAGING_PORT"
echo 'PASS: DEV and STAGING are isolated at Git clone, Compose project, container, port, database volume, DB credential, and JWT levels.'
