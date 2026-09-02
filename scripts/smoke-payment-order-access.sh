#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

C=zetruv-pg-payment-access
DB=zetruv_payment_access_test
PORT=55437
API=18085
PID=""
cleanup(){ [[ -n "$PID" ]] && kill "$PID" >/dev/null 2>&1 || true; docker rm -f "$C" >/dev/null 2>&1 || true; }
trap cleanup EXIT

echo '=== BUILD ==='
dotnet build src/Zetruv.Api/Zetruv.Api.csproj --configuration Release

echo '=== START FRESH POSTGRES ==='
docker rm -f "$C" >/dev/null 2>&1 || true
docker run -d --name "$C" -e POSTGRES_USER=zetruv -e POSTGRES_PASSWORD=zetruvtest -e POSTGRES_DB="$DB" -p 127.0.0.1:${PORT}:5432 postgres:17-alpine >/dev/null
until docker exec "$C" pg_isready -U zetruv -d "$DB" >/dev/null 2>&1; do sleep 1; done

export ConnectionStrings__Postgres="Host=127.0.0.1;Port=$PORT;Database=$DB;Username=zetruv;Password=zetruvtest"
export Jwt__Key='0123456789abcdef0123456789abcdef'
export Payments__Provider=mock
export Payments__Mock__WebhookSecret=smoke-secret
export Shipping__Provider=mock
export GameAccountValidation__Provider=mock
export ASPNETCORE_ENVIRONMENT=Staging

dotnet run --project src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --no-build --urls="http://127.0.0.1:$API" >/tmp/zetruv-payment-access.log 2>&1 & PID=$!
for _ in $(seq 1 30); do curl -fsS "http://127.0.0.1:$API/health" >/dev/null && break; sleep 1; done
curl -fsS "http://127.0.0.1:$API/health"; echo

echo '=== SEED PRODUCT ==='
docker exec -i "$C" psql -v ON_ERROR_STOP=1 -U zetruv -d "$DB" <<'SQL'
INSERT INTO products ("Id","CategoryId","Name","Slug","Kind","RequiresGameAccountValidation","IsActive","IsFeatured","SortOrder","CreatedAt","UpdatedAt")
VALUES ('71000000-0000-0000-0000-000000000001',(SELECT "Id" FROM catalog_categories WHERE "Key"='top_up_games'),'Access Test Product','access-test-product','TopUpGame',FALSE,TRUE,FALSE,0,NOW(),NOW());
INSERT INTO product_variants ("Id","ProductId","Name","Sku","Price","StockQuantity","IsActive","SortOrder","CreatedAt","UpdatedAt")
VALUES ('72000000-0000-0000-0000-000000000001','71000000-0000-0000-0000-000000000001','Default','ACCESS-TEST',50000,10,TRUE,0,NOW(),NOW());
SQL

checkout(){
  local email="$1"
  curl -fsS -X POST "http://127.0.0.1:$API/api/v1/checkout/orders" \
    -H 'Content-Type: application/json' \
    -d "{\"customerName\":\"Access Smoke\",\"customerEmail\":\"$email\",\"customerPhone\":null,\"items\":[{\"productVariantId\":\"72000000-0000-0000-0000-000000000001\",\"quantity\":1}]}"
}

echo '=== CHECKOUT TOKEN ==='
FIRST=$(checkout 'access1@example.com')
echo "$FIRST" | python3 -m json.tool
FIRST_ID=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])' <<<"$FIRST")
FIRST_NO=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["orderNumber"])' <<<"$FIRST")
FIRST_TOKEN=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["orderAccessToken"])' <<<"$FIRST")
FIRST_EXP=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["orderAccessTokenExpiresAt"])' <<<"$FIRST")
[[ "$FIRST_TOKEN" == v1.* ]]
[[ -n "$FIRST_EXP" ]]
echo 'PASS: checkout returns signed order access token'

SECOND=$(checkout 'access2@example.com')
SECOND_ID=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])' <<<"$SECOND")

echo '=== PAYMENT TOKEN ENFORCEMENT ==='
CODE=$(curl -sS -o /tmp/payment-access-response.json -w '%{http_code}' -X POST "http://127.0.0.1:$API/api/v1/checkout/orders/$FIRST_ID/payment")
[[ "$CODE" == "404" ]]

CODE=$(curl -sS -o /tmp/payment-access-response.json -w '%{http_code}' -X POST "http://127.0.0.1:$API/api/v1/checkout/orders/$FIRST_ID/payment" -H 'X-Order-Access-Token: v1.9999999999.invalid')
[[ "$CODE" == "404" ]]

CODE=$(curl -sS -o /tmp/payment-access-response.json -w '%{http_code}' -X POST "http://127.0.0.1:$API/api/v1/checkout/orders/$SECOND_ID/payment" -H "X-Order-Access-Token: $FIRST_TOKEN")
[[ "$CODE" == "404" ]]

echo 'PASS: missing, invalid, and cross-order tokens are rejected'

echo '=== AUTHORIZED PAYMENT ==='
CODE=$(curl -sS -o /tmp/payment-access-response.json -w '%{http_code}' -X POST "http://127.0.0.1:$API/api/v1/checkout/orders/$FIRST_ID/payment" -H "X-Order-Access-Token: $FIRST_TOKEN")
cat /tmp/payment-access-response.json | python3 -m json.tool
[[ "$CODE" == "200" ]]
python3 - <<'PY'
import json
with open('/tmp/payment-access-response.json') as f:
    d=json.load(f)
assert d['provider']=='mock'
assert d['amount']==50000
assert d['currency']=='IDR'
PY
echo 'PASS: correct order access token authorizes payment initiation'

echo '=== ORDER LOOKUP RECOVERY ==='
LOOKUP=$(curl -fsS -X POST "http://127.0.0.1:$API/api/v1/orders/lookup" \
  -H 'Content-Type: application/json' \
  -d "{\"orderNumber\":\"$FIRST_NO\",\"customerEmail\":\"access1@example.com\",\"customerPhone\":null}")
echo "$LOOKUP" | python3 -m json.tool
LOOKUP_ID=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["orderId"])' <<<"$LOOKUP")
LOOKUP_TOKEN=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["orderAccessToken"])' <<<"$LOOKUP")
LOOKUP_EXP=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["orderAccessTokenExpiresAt"])' <<<"$LOOKUP")
[[ "$LOOKUP_ID" == "$FIRST_ID" ]]
[[ "$LOOKUP_TOKEN" == v1.* ]]
[[ -n "$LOOKUP_EXP" ]]
echo 'PASS: verified order lookup returns payment access token'

echo 'PASS: payment order ownership access flow'
