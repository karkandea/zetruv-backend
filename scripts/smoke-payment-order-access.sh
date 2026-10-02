#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

C=zetruv-pg-payment-access
DB=zetruv_payment_access_test
PORT=55437
API=18085
PID=""
cleanup(){ [[ -n "$PID" ]] && kill "$PID" >/dev/null 2>&1 || true; docker rm -fv "$C" >/dev/null 2>&1 || true; }
trap cleanup EXIT

echo '=== BUILD ==='
dotnet build src/Zetruv.Api/Zetruv.Api.csproj --configuration Release

echo '=== START FRESH POSTGRES ==='
docker rm -fv "$C" >/dev/null 2>&1 || true
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
echo '=== PAYMENT READINESS: MOCK IS NEVER LIVE ==='
curl -fsS "http://127.0.0.1:$API/api/v1/checkout/payment-options" | python3 -c 'import json,sys; x=json.load(sys.stdin); assert x["status"]=="DemoOnly" and not x["canAcceptLivePayments"] and x["availableMethods"]==[],x; print("PASS: mock provider exposes zero payable methods")'

echo '=== SEED PRODUCT ==='
docker exec -i "$C" psql -v ON_ERROR_STOP=1 -U zetruv -d "$DB" <<'SQL'
INSERT INTO products ("Id","CategoryId","Name","Slug","Kind","FulfillmentMethod","RequiresGameAccountValidation","IsActive","IsFeatured","SortOrder","CreatedAt","UpdatedAt")
VALUES ('71000000-0000-0000-0000-000000000001',(SELECT "Id" FROM catalog_categories WHERE "Key"='top_up_games'),'Access Test Product','access-test-product','TopUpGame','AUTO_ID',FALSE,TRUE,FALSE,0,NOW(),NOW());
INSERT INTO product_variants ("Id","ProductId","Name","Sku","Price","StockQuantity","IsActive","SortOrder","CreatedAt","UpdatedAt")
VALUES ('72000000-0000-0000-0000-000000000001','71000000-0000-0000-0000-000000000001','Default','ACCESS-TEST',50000,10,TRUE,0,NOW(),NOW());
SQL

checkout(){
  local email="$1"
  curl -fsS -X POST "http://127.0.0.1:$API/api/v1/checkout/orders" \
    -H 'Content-Type: application/json' \
    -d "{\"customerName\":\"Access Smoke\",\"customerEmail\":\"$email\",\"customerPhone\":\"+6281234567890\",\"items\":[{\"productVariantId\":\"72000000-0000-0000-0000-000000000001\",\"quantity\":1}]}"
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

echo '=== PAYMENT STATUS POLL ACCESS ==='
ST_CODE=$(curl -sS -o /tmp/payment-status-response.json -w '%{http_code}' \
  "http://127.0.0.1:$API/api/v1/checkout/orders/$FIRST_ID/payment")
[[ "$ST_CODE" == 404 ]]
ST_CODE=$(curl -sS -o /tmp/payment-status-response.json -w '%{http_code}' \
  "http://127.0.0.1:$API/api/v1/checkout/orders/$SECOND_ID/payment" -H "X-Order-Access-Token: $FIRST_TOKEN")
[[ "$ST_CODE" == 404 ]]
INITIAL_STATUS=$(curl -fsS "http://127.0.0.1:$API/api/v1/checkout/orders/$FIRST_ID/payment" -H "X-Order-Access-Token: $FIRST_TOKEN")
python3 - "$INITIAL_STATUS" <<'PY'
import json,sys
x=json.loads(sys.argv[1])
assert x['state']=='NotStarted' and x['canRetry'] is True
assert x['hasActivePaymentSession'] is False
assert x['paymentUrl'] is None and x['qrString'] is None
PY
echo 'PASS: payment status protects order and exposes NotStarted'

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
if [[ "$CODE" != "200" ]]; then
  echo "FAIL: authorized payment returned HTTP $CODE"
  echo '=== API LOG (TAIL) ==='
  tail -n 160 /tmp/zetruv-payment-access.log || true
  echo '=== DB STATE: ORDER ==='
  docker exec "$C" psql -x -U zetruv -d "$DB" -c "SELECT \"Id\",\"OrderNumber\",\"Status\",\"PaymentStatus\",\"GrandTotal\",\"PaymentProvider\",\"PaymentReference\",\"UpdatedAt\" FROM orders WHERE \"Id\"='$FIRST_ID';" || true
  echo '=== DB STATE: INVENTORY RESERVATION ==='
  docker exec "$C" psql -x -U zetruv -d "$DB" -c "SELECT ir.\"Id\",ir.\"OrderId\",ir.\"ProductVariantId\",ir.\"Quantity\",ir.\"Status\",ir.\"ExpiresAt\",pv.\"StockQuantity\" FROM inventory_reservations ir JOIN product_variants pv ON pv.\"Id\"=ir.\"ProductVariantId\" WHERE ir.\"OrderId\"='$FIRST_ID';" || true
  echo '=== DB STATE: PAYMENT TRANSACTIONS ==='
  docker exec "$C" psql -x -U zetruv -d "$DB" -c "SELECT \"Id\",\"OrderId\",\"Provider\",\"ProviderReference\",\"Type\",\"Status\",\"Amount\",\"Currency\",\"CreatedAt\" FROM payment_transactions WHERE \"OrderId\"='$FIRST_ID';" || true
  exit 1
fi
python3 - <<'PY'
import json
with open('/tmp/payment-access-response.json') as f:
    d=json.load(f)
assert d['provider']=='mock'
assert d['amount']==50000
assert d['currency']=='IDR'
PY
TX_COUNT=$(docker exec "$C" psql -At -U zetruv -d "$DB" -c "SELECT COUNT(*) FROM payment_transactions WHERE \"OrderId\"='$FIRST_ID' AND \"Provider\"='mock' AND \"Status\"='Pending';")
[[ "$TX_COUNT" == "1" ]]
echo 'PASS: correct order access token authorizes payment initiation'
echo 'PASS: authorized payment inserts exactly one pending transaction'

echo '=== PAYMENT STATUS: PENDING / EXPIRED / RETRY ==='
PENDING_STATUS=$(curl -fsS "http://127.0.0.1:$API/api/v1/checkout/orders/$FIRST_ID/payment" -H "X-Order-Access-Token: $FIRST_TOKEN")
python3 - "$PENDING_STATUS" <<'PY'
import json,sys
x=json.loads(sys.argv[1])
assert x['state']=='Pending' and x['paymentStatus']=='Pending'
assert x['hasActivePaymentSession'] is True and x['canRetry'] is False
assert x['paymentUrl'].startswith('mock://payment/')
PY
docker exec "$C" psql -v ON_ERROR_STOP=1 -U zetruv -d "$DB" -c \
  "UPDATE payment_transactions SET \"ExpiresAt\" = NOW() - INTERVAL '1 minute' WHERE \"OrderId\"='$FIRST_ID' AND \"Status\"='Pending';" >/dev/null
EXPIRED_STATUS=$(curl -fsS "http://127.0.0.1:$API/api/v1/checkout/orders/$FIRST_ID/payment" -H "X-Order-Access-Token: $FIRST_TOKEN")
python3 - "$EXPIRED_STATUS" <<'PY'
import json,sys
x=json.loads(sys.argv[1])
assert x['state']=='Expired' and x['hasActivePaymentSession'] is False
assert x['canRetry'] is True and x['paymentUrl'] is None and x['qrString'] is None
PY
RETRY=$(curl -fsS -X POST "http://127.0.0.1:$API/api/v1/checkout/orders/$FIRST_ID/payment" \
  -H "X-Order-Access-Token: $FIRST_TOKEN")
python3 - "$RETRY" <<'PY'
import json,sys
x=json.loads(sys.argv[1])
assert x['orderId'] and x['isRecovery'] is False
PY
RETRY_PENDING=$(curl -fsS "http://127.0.0.1:$API/api/v1/checkout/orders/$FIRST_ID/payment" -H "X-Order-Access-Token: $FIRST_TOKEN")
python3 - "$RETRY_PENDING" <<'PY'
import json,sys
x=json.loads(sys.argv[1])
assert x['state']=='Pending' and x['hasActivePaymentSession'] is True
PY
echo 'PASS: Pending -> Expired -> same-order new payment attempt -> Pending'

echo '=== ORDER LOOKUP RECOVERY ==='
LOOKUP=$(curl -fsS -X POST "http://127.0.0.1:$API/api/v1/orders/lookup" \
  -H 'Content-Type: application/json' \
  -d "{\"orderNumber\":\"$FIRST_NO\",\"customerEmail\":\"access1@example.com\",\"customerPhone\":\"+6281234567890\"}")
echo "$LOOKUP" | python3 -m json.tool
LOOKUP_ID=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["orderId"])' <<<"$LOOKUP")
LOOKUP_TOKEN=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["orderAccessToken"])' <<<"$LOOKUP")
LOOKUP_EXP=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["orderAccessTokenExpiresAt"])' <<<"$LOOKUP")
[[ "$LOOKUP_ID" == "$FIRST_ID" ]]
[[ "$LOOKUP_TOKEN" == v1.* ]]
[[ -n "$LOOKUP_EXP" ]]
echo 'PASS: verified order lookup returns payment access token'

echo '=== PAYMENT STATUS: PROVIDER FAILURE / SUCCESS ==='
send_mock_webhook() {
  local reference="$1" event_status="$2" body sig
  body=$(python3 - "$reference" "$event_status" <<'PY'
import json,sys
print(json.dumps({"providerReference":sys.argv[1],"status":sys.argv[2],"amount":50000,"currency":"IDR"},separators=(',',':')))
PY
)
  sig=$(printf '%s' "$body" | openssl dgst -sha256 -hmac 'smoke-secret' | awk '{print $NF}')
  curl -fsS -X POST "http://127.0.0.1:$API/api/v1/payments/webhooks/mock" \
    -H 'Content-Type: application/json' -H "X-Mock-Signature: $sig" -d "$body" >/dev/null
}

RETRY_REF=$(python3 - "$RETRY_PENDING" <<'PY'
import json,sys
print(json.loads(sys.argv[1])['providerReference'])
PY
)
send_mock_webhook "$RETRY_REF" Paid
PAID_STATUS=$(curl -fsS "http://127.0.0.1:$API/api/v1/checkout/orders/$FIRST_ID/payment" -H "X-Order-Access-Token: $FIRST_TOKEN")
python3 - "$PAID_STATUS" <<'PY'
import json,sys
x=json.loads(sys.argv[1])
assert x['paymentStatus']=='Paid' and x['state']=='Paid' and x['canRetry'] is False
assert x['paymentUrl'] is None and x['qrString'] is None
PY

SECOND_PAYMENT=$(curl -fsS -X POST "http://127.0.0.1:$API/api/v1/checkout/orders/$SECOND_ID/payment" \
  -H "X-Order-Access-Token: $(python3 -c 'import json,sys; print(json.loads(sys.argv[1])["orderAccessToken"])' "$SECOND")")
SECOND_REF=$(python3 - "$SECOND_PAYMENT" <<'PY'
import json,sys
print(json.loads(sys.argv[1])['providerReference'])
PY
)
send_mock_webhook "$SECOND_REF" Failed
SECOND_TOKEN=$(python3 -c 'import json,sys; print(json.loads(sys.argv[1])["orderAccessToken"])' "$SECOND")
FAILED_STATUS=$(curl -fsS "http://127.0.0.1:$API/api/v1/checkout/orders/$SECOND_ID/payment" -H "X-Order-Access-Token: $SECOND_TOKEN")
python3 - "$FAILED_STATUS" <<'PY'
import json,sys
x=json.loads(sys.argv[1])
assert x['paymentStatus']=='Failed' and x['state']=='Failed'
assert x['canRetry'] is True and x['hasActivePaymentSession'] is False
PY
echo 'PASS: verified provider callbacks update Paid / Failed without unsafe mutation'

echo 'PASS: payment order ownership access flow'
