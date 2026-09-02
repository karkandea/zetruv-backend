#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

PROJECT=zetruv-staging-smoke
API_PORT=18086
ENV_FILE=$(mktemp)
API_CONTAINER=""

cleanup() {
  docker compose --env-file "$ENV_FILE" -p "$PROJECT" down -v --remove-orphans >/dev/null 2>&1 || true
  rm -f "$ENV_FILE"
}
trap cleanup EXIT

cat > "$ENV_FILE" <<EOF
ASPNETCORE_ENVIRONMENT=Staging
POSTGRES_DB=zetruv_smoke
POSTGRES_USER=zetruv
POSTGRES_PASSWORD=zetruv-smoke-password
JWT_KEY=0123456789abcdef0123456789abcdef
CMS_ADMIN_EMAIL=admin-smoke@zetruv.local
CMS_ADMIN_PASSWORD=smoke-admin-password
FRONTEND_ORIGIN=https://zetruv.dualangka.com
CMS_ORIGIN=https://zetruv.dualangka.com
API_PORT=$API_PORT
ORDER_ACCESS_TOKEN_LIFETIME_MINUTES=1440
FORWARDED_HEADERS_ENABLED=true
PAYMENTS_PROVIDER=mock
PAYMENTS_MOCK_WEBHOOK_SECRET=smoke-webhook-secret
GAME_ACCOUNT_VALIDATION_PROVIDER=mock
SHIPPING_PROVIDER=mock
EOF

echo '=== COMPOSE BUILD + START ==='
docker compose --env-file "$ENV_FILE" -p "$PROJECT" up -d --build

for _ in $(seq 1 60); do
  if curl -fsS "http://127.0.0.1:$API_PORT/health" >/dev/null 2>&1; then
    break
  fi
  sleep 1
done
curl -fsS "http://127.0.0.1:$API_PORT/health"; echo

echo '=== LOCALHOST-ONLY API BIND ==='
API_CONTAINER=$(docker compose --env-file "$ENV_FILE" -p "$PROJECT" ps -q api)
[[ -n "$API_CONTAINER" ]]
BIND=$(docker port "$API_CONTAINER" 8080/tcp)
echo "$BIND"
[[ "$BIND" == "127.0.0.1:$API_PORT" ]]
echo 'PASS: API is published only on host loopback'

echo '=== RESTART POLICY ==='
API_RESTART=$(docker inspect -f '{{.HostConfig.RestartPolicy.Name}}' "$API_CONTAINER")
PG_CONTAINER=$(docker compose --env-file "$ENV_FILE" -p "$PROJECT" ps -q postgres)
PG_RESTART=$(docker inspect -f '{{.HostConfig.RestartPolicy.Name}}' "$PG_CONTAINER")
echo "api=$API_RESTART postgres=$PG_RESTART"
[[ "$API_RESTART" == "unless-stopped" ]]
[[ "$PG_RESTART" == "unless-stopped" ]]
echo 'PASS: API and PostgreSQL have persistent restart policies'

echo '=== FORWARDED CLIENT IP RATE-LIMIT PARTITION ==='
BODY='{"orderNumber":"ZTR-NOT-FOUND","customerEmail":"smoke@example.com","customerPhone":null}'
for _ in $(seq 1 10); do
  CODE=$(curl -sS -o /dev/null -w '%{http_code}' \
    -X POST "http://127.0.0.1:$API_PORT/api/v1/orders/lookup" \
    -H 'Content-Type: application/json' \
    -H 'X-Forwarded-Proto: https' \
    -H 'X-Forwarded-For: 198.51.100.10' \
    -d "$BODY")
  [[ "$CODE" == "404" ]]
done

CODE=$(curl -sS -o /dev/null -w '%{http_code}' \
  -X POST "http://127.0.0.1:$API_PORT/api/v1/orders/lookup" \
  -H 'Content-Type: application/json' \
  -H 'X-Forwarded-Proto: https' \
  -H 'X-Forwarded-For: 198.51.100.10' \
  -d "$BODY")
[[ "$CODE" == "429" ]]

echo 'PASS: repeated requests from one forwarded client IP are rate limited'

CODE=$(curl -sS -o /dev/null -w '%{http_code}' \
  -X POST "http://127.0.0.1:$API_PORT/api/v1/orders/lookup" \
  -H 'Content-Type: application/json' \
  -H 'X-Forwarded-Proto: https' \
  -H 'X-Forwarded-For: 198.51.100.11' \
  -d "$BODY")
[[ "$CODE" == "404" ]]
echo 'PASS: forwarded client IPs use separate rate-limit partitions'

echo 'PASS: staging compose reverse-proxy deployment baseline'
