#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

CONTAINER=zetruv-pg-fulfillment
PORT=55434
API_PORT=18082
DB=zetruv_fulfillment_test
LOG=/tmp/zetruv-fulfillment-test.log
API_PID=""

cleanup() {
  if [[ -n "${API_PID:-}" ]]; then
    kill "$API_PID" >/dev/null 2>&1 || true
  fi
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "=== BUILD ==="
dotnet build src/Zetruv.Api/Zetruv.Api.csproj --configuration Release

echo "=== START FRESH POSTGRES ==="
docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
docker run -d --name "$CONTAINER" \
  -e POSTGRES_USER=zetruv \
  -e POSTGRES_PASSWORD=zetruvtest \
  -e POSTGRES_DB="$DB" \
  -p "127.0.0.1:${PORT}:5432" \
  postgres:17-alpine >/dev/null

until docker exec "$CONTAINER" pg_isready -U zetruv -d "$DB" >/dev/null 2>&1; do
  sleep 1
done

export ConnectionStrings__Postgres="Host=127.0.0.1;Port=${PORT};Database=${DB};Username=zetruv;Password=zetruvtest"
export Jwt__Key='0123456789abcdef0123456789abcdef'
export CmsAdmin__Email='smoke-admin@zetruv.test'
export CmsAdmin__Password='SmokeAdmin123!'
export Shipping__Provider='mock'
export GameAccountValidation__Provider='mock'
export Payments__Provider='mock'
export Payments__Mock__WebhookSecret='test-webhook-secret'
export ASPNETCORE_ENVIRONMENT='Staging'

rm -f "$LOG"
dotnet run --project src/Zetruv.Api/Zetruv.Api.csproj \
  --configuration Release \
  --no-build \
  --urls="http://127.0.0.1:${API_PORT}" \
  >"$LOG" 2>&1 &
API_PID=$!

for _ in $(seq 1 30); do
  if curl -fsS "http://127.0.0.1:${API_PORT}/health" >/dev/null; then
    break
  fi

  if ! kill -0 "$API_PID" >/dev/null 2>&1; then
    echo "=== API CRASHED ==="
    cat "$LOG"
    exit 1
  fi

  sleep 1
done

BASE="http://127.0.0.1:${API_PORT}"

echo "=== HEALTH ==="
curl -fsS "$BASE/health"
echo

echo "=== SEED ORDERS + SHIPMENTS ==="
docker exec -i "$CONTAINER" psql -v ON_ERROR_STOP=1 -U zetruv -d "$DB" <<'SQL'
INSERT INTO orders (
  "Id", "OrderNumber", "Status", "PaymentStatus",
  "Subtotal", "DiscountAmount", "ShippingAmount", "GrandTotal", "Currency",
  "CreatedAt", "UpdatedAt"
)
VALUES
(
  'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  'ZTR-FULFILL-001',
  'Processing',
  'Paid',
  200000, 0, 17000, 217000, 'IDR',
  NOW(), NOW()
),
(
  'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
  'ZTR-FULFILL-002',
  'Processing',
  'Paid',
  100000, 0, 17000, 117000, 'IDR',
  NOW(), NOW()
);

INSERT INTO shipments (
  "Id", "OrderId", "Status", "Provider", "ServiceCode", "ServiceName",
  "Cost", "Currency", "TotalWeightGrams",
  "RecipientName", "Phone", "AddressLine1", "District", "City", "Province", "PostalCode",
  "CreatedAt", "UpdatedAt"
)
VALUES
(
  'cccccccc-cccc-cccc-cccc-cccccccccccc',
  'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
  0, 'mock', 'REG', 'Regular',
  17000, 'IDR', 1000,
  'Smoke User', '08123456789', 'Jl. Test No. 1', 'Pesanggrahan', 'Jakarta Selatan', 'DKI Jakarta', '12320',
  NOW(), NOW()
),
(
  'dddddddd-dddd-dddd-dddd-dddddddddddd',
  'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
  0, 'mock', 'REG', 'Regular',
  17000, 'IDR', 500,
  'Cancel User', '08123456789', 'Jl. Test No. 2', 'Pesanggrahan', 'Jakarta Selatan', 'DKI Jakarta', '12320',
  NOW(), NOW()
);
SQL

TOKEN=""

request_json() {
  local method="$1"
  local label="$2"
  local url="$3"
  local payload="$4"
  local expected="${5:-2}"
  local output
  local status
  local -a curl_args

  output=$(mktemp)
  curl_args=(
    -sS
    -o "$output"
    -w '%{http_code}'
    -X "$method"
    "$url"
    -H 'Content-Type: application/json'
    -d "$payload"
  )

  if [[ -n "${TOKEN:-}" ]]; then
    curl_args+=( -H "Authorization: Bearer $TOKEN" )
  fi

  status=$(curl "${curl_args[@]}")

  if [[ "${status:0:1}" != "$expected" ]]; then
    echo "ERROR: $label returned HTTP $status" >&2
    cat "$output" >&2
    echo >&2
    echo "=== API LOG TAIL ===" >&2
    tail -n 120 "$LOG" >&2
    rm -f "$output"
    return 1
  fi

  cat "$output"
  rm -f "$output"
}

LOGIN_JSON=$(request_json POST "admin login" "$BASE/api/v1/cms/auth/login" '{"email":"smoke-admin@zetruv.test","password":"SmokeAdmin123!"}')
TOKEN=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["accessToken"])' <<< "$LOGIN_JSON")

echo "=== READY TO SHIP ==="
READY_JSON=$(request_json PUT "ready to ship" "$BASE/api/v1/cms/orders/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/shipment" '{"status":"ReadyToShip"}')
echo "$READY_JSON" | python3 -m json.tool

echo "=== SHIPPED ==="
SHIPPED_JSON=$(request_json PUT "shipped" "$BASE/api/v1/cms/orders/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/shipment" '{"status":"Shipped","trackingNumber":"JNE-SMOKE-123"}')
echo "$SHIPPED_JSON" | python3 -m json.tool

echo "=== DELIVERED ==="
DELIVERED_JSON=$(request_json PUT "delivered" "$BASE/api/v1/cms/orders/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/shipment" '{"status":"Delivered"}')
echo "$DELIVERED_JSON" | python3 -m json.tool

echo "=== INVALID BACKWARD TRANSITION ==="
INVALID_JSON=$(request_json PUT "invalid backward transition" "$BASE/api/v1/cms/orders/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa/shipment" '{"status":"ReadyToShip"}' 4)
echo "$INVALID_JSON" | python3 -m json.tool

echo "=== ORDER CANCELLATION SYNC ==="
request_json PUT "cancel order" "$BASE/api/v1/cms/orders/bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb/status" '{"status":"Cancelled"}' >/dev/null

echo "=== DATABASE CHECK ==="
docker exec "$CONTAINER" psql -U zetruv -d "$DB" -c '
SELECT
  o."OrderNumber",
  o."Status" AS "OrderStatus",
  s."Status" AS "ShipmentStatus",
  s."TrackingNumber",
  s."ShippedAt",
  s."DeliveredAt"
FROM orders o
JOIN shipments s ON s."OrderId" = o."Id"
ORDER BY o."OrderNumber";
'

echo "=== ASSERTIONS ==="
python3 - "$READY_JSON" "$SHIPPED_JSON" "$DELIVERED_JSON" "$INVALID_JSON" <<'PY'
import json
import sys

ready = json.loads(sys.argv[1])
shipped = json.loads(sys.argv[2])
delivered = json.loads(sys.argv[3])
invalid = json.loads(sys.argv[4])

assert ready["status"] == "ReadyToShip"
assert ready["shippedAt"] is None
assert shipped["status"] == "Shipped"
assert shipped["trackingNumber"] == "JNE-SMOKE-123"
assert shipped["shippedAt"] is not None
assert delivered["status"] == "Delivered"
assert delivered["trackingNumber"] == "JNE-SMOKE-123"
assert delivered["shippedAt"] is not None
assert delivered["deliveredAt"] is not None
assert "cannot transition" in invalid["message"].lower()

print("PASS: CMS shipment fulfillment transitions")
PY

CANCELLED_STATUS=$(docker exec "$CONTAINER" psql -U zetruv -d "$DB" -Atc \
  "SELECT \"Status\" FROM shipments WHERE \"OrderId\"='bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';")

if [[ "$CANCELLED_STATUS" != "4" ]]; then
  echo "ERROR: expected unshipped shipment to be Cancelled (4), got $CANCELLED_STATUS"
  exit 1
fi

echo "PASS: unshipped shipment cancelled with order"
echo "PASS: CMS shipment fulfillment transitions + order cancellation sync"
