#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

C=zetruv-pg-webhook-ledger
DB=zetruv_webhook_ledger_test
PORT=57446
API=18196
PID=""
cleanup(){
  [[ -n "$PID" ]] && kill "$PID" >/dev/null 2>&1 || true
  docker rm -fv "$C" >/dev/null 2>&1 || true
  rm -f /tmp/zetruv-webhook-ledger*.json
}
trap cleanup EXIT

dotnet build src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --nologo
docker rm -fv "$C" >/dev/null 2>&1 || true
docker run -d --name "$C" -e POSTGRES_USER=zetruv -e POSTGRES_PASSWORD=zetruvtest -e POSTGRES_DB="$DB" -p 127.0.0.1:$PORT:5432 postgres:17-alpine >/dev/null
until docker exec "$C" pg_isready -U zetruv -d "$DB" >/dev/null 2>&1; do sleep 1; done

export ConnectionStrings__Postgres="Host=127.0.0.1;Port=$PORT;Database=$DB;Username=zetruv;Password=zetruvtest"
export Jwt__Key='0123456789abcdef0123456789abcdef'
export Payments__Provider=mock
export Payments__Mock__WebhookSecret=smoke-secret
export Shipping__Provider=mock
export GameAccountValidation__Provider=mock
export ASPNETCORE_ENVIRONMENT=Staging
dotnet run --project src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --no-build --urls="http://127.0.0.1:$API" >/tmp/zetruv-webhook-ledger.log 2>&1 & PID=$!
for _ in $(seq 1 40); do
  curl -fsS "http://127.0.0.1:$API/health" >/dev/null 2>&1 && break
  sleep 1
done
curl -fsS "http://127.0.0.1:$API/health" >/dev/null

docker exec -i "$C" psql -v ON_ERROR_STOP=1 -U zetruv -d "$DB" <<'SQL'
INSERT INTO products ("Id","CategoryId","Name","Slug","Kind","FulfillmentMethod","RequiresGameAccountValidation","IsActive","IsFeatured","SortOrder","CreatedAt","UpdatedAt")
VALUES ('c1000000-0000-0000-0000-000000000001',(SELECT "Id" FROM catalog_categories WHERE "Key"='top_up_games'),'Webhook Ledger Product','webhook-ledger-product','TopUpGame','MANUAL',FALSE,TRUE,FALSE,0,NOW(),NOW());
INSERT INTO product_variants ("Id","ProductId","Name","Sku","Price","StockQuantity","IsActive","SortOrder","CreatedAt","UpdatedAt")
VALUES ('c2000000-0000-0000-0000-000000000001','c1000000-0000-0000-0000-000000000001','Default','WEBHOOK-LEDGER',100000,0,TRUE,0,NOW(),NOW());
INSERT INTO orders ("Id","OrderNumber","Status","PaymentStatus","Subtotal","DiscountAmount","ShippingAmount","GrandTotal","Currency","PaymentProvider","PaymentReference","CreatedAt","UpdatedAt")
VALUES ('c3000000-0000-0000-0000-000000000001','ZTR-WEBHOOK-001','Pending','Pending',100000,0,0,100000,'IDR','mock','REF-WEBHOOK-001',NOW(),NOW());
INSERT INTO order_items ("Id","OrderId","ProductId","ProductVariantId","ProductName","ProductSlug","ProductKind","FulfillmentMethod","FulfillmentStatus","VariantName","Sku","UnitPrice","Quantity","LineTotal","CreatedAt")
VALUES ('c4000000-0000-0000-0000-000000000001','c3000000-0000-0000-0000-000000000001','c1000000-0000-0000-0000-000000000001','c2000000-0000-0000-0000-000000000001','Webhook Ledger Product','webhook-ledger-product','TopUpGame','MANUAL','Pending','Default','WEBHOOK-LEDGER',100000,1,100000,NOW());
INSERT INTO payment_transactions ("Id","OrderId","Provider","ProviderReference","Type","Status","Amount","Currency","CreatedAt","UpdatedAt")
VALUES ('c5000000-0000-0000-0000-000000000001','c3000000-0000-0000-0000-000000000001','mock','REF-WEBHOOK-001','Payment','Pending',100000,'IDR',NOW(),NOW());
INSERT INTO inventory_reservations ("Id","OrderId","ProductVariantId","Quantity","Status","ExpiresAt","CreatedAt","UpdatedAt")
VALUES ('c6000000-0000-0000-0000-000000000001','c3000000-0000-0000-0000-000000000001','c2000000-0000-0000-0000-000000000001',1,'Active',NOW()+INTERVAL '1 hour',NOW(),NOW());
SQL

send_webhook(){
  local body="$1" expected="$2" out="$3"
  local sig code
  sig=$(python3 -c 'import hmac,hashlib,sys; print(hmac.new(b"smoke-secret",sys.argv[1].encode(),hashlib.sha256).hexdigest())' "$body")
  code=$(curl -sS -o "$out" -w '%{http_code}' -X POST "http://127.0.0.1:$API/api/v1/payments/webhooks/mock" -H 'Content-Type: application/json' -H "X-Mock-Signature: $sig" -d "$body")
  if [[ "$code" != "$expected" ]]; then
    echo "Expected HTTP $expected but got $code"
    cat "$out"; echo
    return 1
  fi
}
echo '=== FIRST EVENT ==='
BODY1='{"providerReference":"REF-WEBHOOK-001","status":"Paid","amount":100000,"currency":"IDR","eventId":"evt-paid-001"}'
send_webhook "$BODY1" 200 /tmp/zetruv-webhook-ledger-first.json

echo '=== EXACT EVENT REPLAY WITH DIFFERENT JSON ORDER ==='
BODY2='{"eventId":"evt-paid-001","currency":"IDR","amount":100000,"status":"Paid","providerReference":"REF-WEBHOOK-001"}'
send_webhook "$BODY2" 200 /tmp/zetruv-webhook-ledger-replay.json

echo '=== EVENT ID REUSE WITH DIFFERENT SEMANTICS ==='
BODY3='{"providerReference":"REF-WEBHOOK-001","status":"Failed","amount":100000,"currency":"IDR","eventId":"evt-paid-001"}'
send_webhook "$BODY3" 409 /tmp/zetruv-webhook-ledger-conflict.json
python3 -c 'import json,sys; x=json.load(open(sys.argv[1])); assert "different payload" in x["message"]' /tmp/zetruv-webhook-ledger-conflict.json

STATE=$(docker exec "$C" psql -At -F '|' -U zetruv -d "$DB" -c "SELECT o.\"PaymentStatus\",pt.\"Status\",ir.\"Status\",pv.\"StockQuantity\",we.\"Outcome\",we.\"DeliveryCount\",length(we.\"EventFingerprintSha256\"),we.\"CompletedAt\" IS NOT NULL FROM orders o JOIN payment_transactions pt ON pt.\"OrderId\"=o.\"Id\" JOIN inventory_reservations ir ON ir.\"OrderId\"=o.\"Id\" JOIN product_variants pv ON pv.\"Id\"=ir.\"ProductVariantId\" JOIN payment_webhook_events we ON we.\"OrderId\"=o.\"Id\" WHERE o.\"Id\"='c3000000-0000-0000-0000-000000000001';")
echo "STATE=$STATE"
[[ "$STATE" == 'Paid|Succeeded|Consumed|0|Applied|3|64|t' ]]
COUNT=$(docker exec "$C" psql -At -U zetruv -d "$DB" -c 'SELECT count(*) FROM payment_webhook_events;')
echo "LEDGER_COUNT=$COUNT"
[[ "$COUNT" == 1 ]]

echo 'PASS: provider event ledger applies once, deduplicates replay, and rejects semantic event-ID reuse'
