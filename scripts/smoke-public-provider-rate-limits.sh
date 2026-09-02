#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

PG_CONTAINER="zetruv-pg-rate-limit"
PG_PORT="55436"
API_PORT="18084"
API_LOG="/tmp/zetruv-rate-limit-test.log"
API_PID=""

cleanup() {
  if [[ -n "$API_PID" ]]; then
    kill "$API_PID" >/dev/null 2>&1 || true
  fi
  docker rm -f "$PG_CONTAINER" >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "=== BUILD ==="
dotnet build src/Zetruv.Api/Zetruv.Api.csproj --configuration Release

echo "=== START FRESH POSTGRES ==="
docker rm -f "$PG_CONTAINER" >/dev/null 2>&1 || true
docker run -d --name "$PG_CONTAINER" \
  -e POSTGRES_USER=zetruv \
  -e POSTGRES_PASSWORD=zetruvtest \
  -e POSTGRES_DB=zetruv_rate_limit_test \
  -p "127.0.0.1:${PG_PORT}:5432" \
  postgres:17-alpine >/dev/null

until docker exec "$PG_CONTAINER" pg_isready -U zetruv -d zetruv_rate_limit_test >/dev/null 2>&1; do
  sleep 1
done

export ASPNETCORE_ENVIRONMENT=Development
export ConnectionStrings__Postgres="Host=127.0.0.1;Port=${PG_PORT};Database=zetruv_rate_limit_test;Username=zetruv;Password=zetruvtest"
export Jwt__Key='0123456789abcdef0123456789abcdef'
export Shipping__Provider='mock'
export GameAccountValidation__Provider='mock'
export Payments__Provider='mock'
export Payments__Mock__WebhookSecret='test-webhook-secret'

dotnet run --project src/Zetruv.Api/Zetruv.Api.csproj \
  --configuration Release \
  --no-build \
  --urls="http://127.0.0.1:${API_PORT}" \
  >"$API_LOG" 2>&1 &
API_PID=$!

BASE="http://127.0.0.1:${API_PORT}"
for _ in {1..30}; do
  if curl -fsS "$BASE/health" >/dev/null 2>&1; then
    break
  fi
  if ! kill -0 "$API_PID" >/dev/null 2>&1; then
    cat "$API_LOG"
    exit 1
  fi
  sleep 1
done

curl -fsS "$BASE/health"
echo

echo "=== PAYMENT INITIATION LIMIT ==="
ORDER_ID="30000000-0000-0000-0000-000000009999"
for i in {1..10}; do
  code=$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/checkout/orders/$ORDER_ID/payment")
  if [[ "$code" == "429" ]]; then
    echo "FAIL: payment request $i was rate-limited too early"
    exit 1
  fi
done
payment_limited=$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/checkout/orders/$ORDER_ID/payment")
if [[ "$payment_limited" != "429" ]]; then
  echo "FAIL: expected payment request 11 to return 429, got $payment_limited"
  exit 1
fi
echo "PASS: payment initiation is limited to 10/min/IP"

echo "=== GAME ACCOUNT VALIDATION LIMIT ==="
PRODUCT_ID="10000000-0000-0000-0000-000000009999"
GAME_BODY="{\"productId\":\"$PRODUCT_ID\",\"fields\":{\"userId\":\"123456\"}}"
for i in {1..20}; do
  code=$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/game-account/validate" \
    -H 'Content-Type: application/json' \
    -d "$GAME_BODY")
  if [[ "$code" == "429" ]]; then
    echo "FAIL: game-account request $i was rate-limited too early"
    exit 1
  fi
done
game_limited=$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$BASE/api/v1/game-account/validate" \
  -H 'Content-Type: application/json' \
  -d "$GAME_BODY")
if [[ "$game_limited" != "429" ]]; then
  echo "FAIL: expected game-account request 21 to return 429, got $game_limited"
  exit 1
fi
echo "PASS: game-account validation is limited to 20/min/IP"

echo "PASS: public provider endpoint rate limits"
