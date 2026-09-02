#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

CONTAINER=zetruv-pg-shipping
PORT=55433
API_PORT=18081
DB=zetruv_shipping_test
LOG=/tmp/zetruv-shipping-test.log
API_PID=""

cleanup() {
  if [[ -n "${API_PID:-}" ]]; then
    kill "$API_PID" >/dev/null 2>&1 || true
  fi
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
}
trap cleanup EXIT

echo "=== RECORD VALIDATION METADATA CHECK ==="
if git grep -nF '[property:' -- src/Zetruv.Api; then
  echo "ERROR: positional request records still contain property-targeted validation metadata."
  exit 1
fi
echo "OK"

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

echo "=== SEED MERCHANDISE ==="
docker exec -i "$CONTAINER" psql -v ON_ERROR_STOP=1 -U zetruv -d "$DB" <<'SQL'
INSERT INTO products (
  "Id", "CategoryId", "GameId", "Name", "Slug",
  "ShortDescription", "Description", "ThumbnailUrl",
  "Kind", "RequiresGameAccountValidation",
  "IsActive", "IsFeatured", "SortOrder", "CreatedAt", "UpdatedAt"
)
VALUES (
  '11111111-1111-1111-1111-111111111111',
  (SELECT "Id" FROM catalog_categories WHERE "Key"='merchandise'),
  NULL,
  'Smoke Test Jersey',
  'smoke-test-jersey',
  NULL, NULL, NULL,
  'Merchandise',
  FALSE,
  TRUE,
  FALSE,
  0,
  NOW(),
  NOW()
);

INSERT INTO product_variants (
  "Id", "ProductId", "Name", "Sku", "Price",
  "CompareAtPrice", "StockQuantity", "WeightGrams",
  "IsActive", "SortOrder", "CreatedAt", "UpdatedAt"
)
VALUES (
  '22222222-2222-2222-2222-222222222222',
  '11111111-1111-1111-1111-111111111111',
  'Size M',
  'SMOKE-JERSEY-M',
  100000,
  NULL,
  10,
  500,
  TRUE,
  0,
  NOW(),
  NOW()
);
SQL

request_json() {
  local label="$1"
  local url="$2"
  local payload="$3"
  local output
  local status

  output=$(mktemp)
  status=$(curl -sS -o "$output" -w '%{http_code}' -X POST "$url" \
    -H 'Content-Type: application/json' \
    -d "$payload")

  if [[ "$status" -lt 200 || "$status" -ge 300 ]]; then
    {
      echo "ERROR: $label returned HTTP $status"
      cat "$output"
      echo
      echo "=== API LOG TAIL ==="
      tail -n 120 "$LOG"
    } >&2
    rm -f "$output"
    return 1
  fi

  cat "$output"
  rm -f "$output"
}

QUOTE_JSON=$(request_json \
  "shipping quotes" \
  "$BASE/api/v1/shipping/quotes" \
  '{
    "address": {
      "recipientName": "Smoke Test",
      "phone": "08123456789",
      "addressLine1": "Jl. Test No. 1",
      "district": "Pesanggrahan",
      "city": "Jakarta Selatan",
      "province": "DKI Jakarta",
      "postalCode": "12320"
    },
    "items": [{
      "productVariantId": "22222222-2222-2222-2222-222222222222",
      "quantity": 2
    }]
  }')

echo "=== SHIPPING QUOTES ==="
echo "$QUOTE_JSON" | python3 -m json.tool
QUOTE_ID=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["rates"][0]["quoteId"])' <<< "$QUOTE_JSON")

CHECKOUT_JSON=$(request_json \
  "checkout" \
  "$BASE/api/v1/checkout/orders" \
  "{
    \"customerName\": \"Smoke Test\",
    \"customerEmail\": \"shipping-test@example.com\",
    \"items\": [{
      \"productVariantId\": \"22222222-2222-2222-2222-222222222222\",
      \"quantity\": 2
    }],
    \"shippingQuoteId\": \"$QUOTE_ID\"
  }")

echo "=== CHECKOUT ==="
echo "$CHECKOUT_JSON" | python3 -m json.tool
ORDER_NUMBER=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["orderNumber"])' <<< "$CHECKOUT_JSON")

LOOKUP_JSON=$(request_json \
  "order lookup" \
  "$BASE/api/v1/orders/lookup" \
  "{
    \"orderNumber\": \"$ORDER_NUMBER\",
    \"customerEmail\": \"shipping-test@example.com\"
  }")

echo "=== ORDER LOOKUP ==="
echo "$LOOKUP_JSON" | python3 -m json.tool

echo "=== DATABASE CHECK ==="
docker exec "$CONTAINER" psql -U zetruv -d "$DB" -c '
SELECT
  o."OrderNumber",
  o."Subtotal",
  o."ShippingAmount",
  o."GrandTotal",
  q."ConsumedAt",
  s."Provider",
  s."ServiceCode",
  s."Cost",
  s."TotalWeightGrams"
FROM orders o
LEFT JOIN shipping_quotes q ON q."OrderId" = o."Id"
LEFT JOIN shipments s ON s."OrderId" = o."Id";
'

echo "=== ASSERTIONS ==="
python3 - "$QUOTE_JSON" "$CHECKOUT_JSON" "$LOOKUP_JSON" <<'PY'
import json
import sys

quotes = json.loads(sys.argv[1])
checkout = json.loads(sys.argv[2])
lookup = json.loads(sys.argv[3])

assert len(quotes["rates"]) >= 2, "expected Regular and Express mock rates"
regular = quotes["rates"][0]
assert regular["provider"] == "mock"
assert regular["serviceCode"] == "REG"
assert float(regular["amount"]) == 17000
assert regular["totalWeightGrams"] == 1000

assert float(checkout["subtotal"]) == 200000
assert float(checkout["shippingAmount"]) == 17000
assert float(checkout["grandTotal"]) == 217000

shipment = lookup.get("shipment")
assert shipment is not None, "guest lookup must expose shipment metadata"
assert shipment["provider"] == "mock"
assert shipment["serviceCode"] == "REG"

print("PASS: shipping quote -> checkout -> shipment -> guest lookup")
PY
