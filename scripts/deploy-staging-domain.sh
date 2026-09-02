#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

bash ./scripts/bootstrap-staging-env.sh

get_env() {
  local key="$1"
  sed -n "s/^${key}=//p" .env | tail -n 1
}

[[ "$(get_env ASPNETCORE_ENVIRONMENT)" == "Staging" ]] || {
  echo 'ASPNETCORE_ENVIRONMENT must be Staging while mock providers are configured.' >&2
  exit 1
}
[[ "$(get_env FORWARDED_HEADERS_ENABLED)" == "true" ]] || {
  echo 'FORWARDED_HEADERS_ENABLED must be true behind the VPS Nginx proxy.' >&2
  exit 1
}
[[ "$(get_env FRONTEND_ORIGIN)" == "https://zetruv.dualangka.com" ]] || {
  echo 'FRONTEND_ORIGIN must be https://zetruv.dualangka.com for this staging deployment.' >&2
  exit 1
}

JWT_KEY=$(get_env JWT_KEY)
[[ ${#JWT_KEY} -ge 32 ]] || {
  echo 'JWT_KEY must contain at least 32 characters.' >&2
  exit 1
}

for key in POSTGRES_PASSWORD CMS_ADMIN_EMAIL CMS_ADMIN_PASSWORD PAYMENTS_MOCK_WEBHOOK_SECRET; do
  value=$(get_env "$key")
  [[ -n "$value" && "$value" != *change-me* && "$value" != replace-with-* ]] || {
    echo "$key is missing or still contains an example value." >&2
    exit 1
  }
done

echo '=== BUILD + START STAGING STACK ==='
docker compose up -d --build

API_PORT=$(get_env API_PORT)
API_PORT=${API_PORT:-8080}
for _ in $(seq 1 60); do
  if curl -fsS "http://127.0.0.1:$API_PORT/health" >/dev/null 2>&1; then
    break
  fi
  sleep 1
done

curl -fsS "http://127.0.0.1:$API_PORT/health"; echo

echo '=== STACK STATUS ==='
docker compose ps

API_CONTAINER=$(docker compose ps -q api)
BIND=$(docker port "$API_CONTAINER" 8080/tcp)
echo "API bind: $BIND"
[[ "$BIND" == "127.0.0.1:$API_PORT" ]] || {
  echo 'API must be published only on 127.0.0.1.' >&2
  exit 1
}

echo 'PASS: staging backend is healthy and localhost-only'
echo 'Next: install deploy/nginx/zetruv-api.conf inside the HTTPS server block for zetruv.dualangka.com, then run nginx -t and reload Nginx.'
