#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

C=zetruv-pg-payment-reconciliation
DB=zetruv_payment_reconciliation_test
PORT=57445
API=18195
PID=""
cleanup(){
  [[ -n "$PID" ]] && kill "$PID" >/dev/null 2>&1 || true
  docker rm -fv "$C" >/dev/null 2>&1 || true
  rm -f /tmp/zetruv-payment-reconciliation*.json
}
trap cleanup EXIT

dotnet build src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --nologo
docker rm -fv "$C" >/dev/null 2>&1 || true
docker run -d --name "$C" -e POSTGRES_USER=zetruv -e POSTGRES_PASSWORD=zetruvtest -e POSTGRES_DB="$DB" -p 127.0.0.1:$PORT:5432 postgres:17-alpine >/dev/null
until docker exec "$C" pg_isready -U zetruv -d "$DB" >/dev/null 2>&1; do sleep 1; done

export ConnectionStrings__Postgres="Host=127.0.0.1;Port=$PORT;Database=$DB;Username=zetruv;Password=zetruvtest"
export Jwt__Key='0123456789abcdef0123456789abcdef'
export CmsAdmin__Email='payment-reconcile@zetruv.local'
export CmsAdmin__Password='PaymentReconcile123!'
export Payments__Provider=mock
export Payments__Mock__WebhookSecret=smoke-secret
export Shipping__Provider=mock
export GameAccountValidation__Provider=mock
export ASPNETCORE_ENVIRONMENT=Staging
export Payments__Reconciliation__PollIntervalSeconds=5
export Payments__Reconciliation__InitialDelaySeconds=5
export Payments__Reconciliation__BaseBackoffSeconds=5
export Payments__Reconciliation__MaxBackoffSeconds=20
export Payments__Reconciliation__MaxBatchSize=20

start_api(){
  local enabled="$1"
  export Payments__Reconciliation__Enabled="$enabled"
  dotnet run --project src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --no-build --urls="http://127.0.0.1:$API" >/tmp/zetruv-payment-reconciliation.log 2>&1 & PID=$!
  for _ in $(seq 1 40); do
    curl -fsS "http://127.0.0.1:$API/health" >/dev/null 2>&1 && break
    sleep 1
  done
  curl -fsS "http://127.0.0.1:$API/health" >/dev/null
}

stop_api(){
  if [[ -n "$PID" ]]; then
    kill "$PID" >/dev/null 2>&1 || true
    wait "$PID" 2>/dev/null || true
    PID=""
  fi
}

start_api false
docker exec -i "$C" psql -v ON_ERROR_STOP=1 -U zetruv -d "$DB" <<'SQL'
INSERT INTO products ("Id","CategoryId","Name","Slug","Kind","FulfillmentMethod","RequiresGameAccountValidation","IsActive","IsFeatured","SortOrder","CreatedAt","UpdatedAt")
VALUES ('b1000000-0000-0000-0000-000000000001',(SELECT "Id" FROM catalog_categories WHERE "Key"='top_up_games'),'Payment Reconciliation Product','payment-reconciliation-product','TopUpGame','MANUAL',FALSE,TRUE,FALSE,0,NOW(),NOW());
INSERT INTO product_variants ("Id","ProductId","Name","Sku","Price","StockQuantity","IsActive","SortOrder","CreatedAt","UpdatedAt")
VALUES ('b2000000-0000-0000-0000-000000000001','b1000000-0000-0000-0000-000000000001','Default','PAY-RECON',50000,20,TRUE,0,NOW(),NOW());
SQL

BASE="http://127.0.0.1:$API"
json(){ python3 -c "import json,sys; print(json.load(sys.stdin)$1)"; }

LOGIN=$(curl -fsS -X POST "$BASE/api/v1/cms/auth/login" -H 'Content-Type: application/json' -d '{"email":"payment-reconcile@zetruv.local","password":"PaymentReconcile123!"}')
CTOK=$(json '["accessToken"]' <<<"$LOGIN")

create_payment(){
  local email="$1"
  local out="$2"
  local order token payment txid
  order=$(curl -fsS -X POST "$BASE/api/v1/checkout/orders" -H 'Content-Type: application/json' -d "{\"customerName\":\"Payment Reconcile\",\"customerEmail\":\"$email\",\"items\":[{\"productVariantId\":\"b2000000-0000-0000-0000-000000000001\",\"quantity\":1}]}")
  local oid
  oid=$(json '["id"]' <<<"$order")
  token=$(json '["orderAccessToken"]' <<<"$order")
  payment=$(curl -fsS -X POST "$BASE/api/v1/checkout/orders/$oid/payment" -H "X-Order-Access-Token: $token")
  txid=$(docker exec "$C" psql -At -U zetruv -d "$DB" -c "SELECT \"Id\" FROM payment_transactions WHERE \"OrderId\"='$oid' ORDER BY \"CreatedAt\" DESC LIMIT 1;")
  printf -v "${out}_OID" '%s' "$oid"
  printf -v "${out}_TX" '%s' "$txid"
}

set_ref(){
  local oid="$1" tx="$2" ref="$3" next="$4"
  docker exec "$C" psql -U zetruv -d "$DB" -v ON_ERROR_STOP=1 -c "UPDATE payment_transactions SET \"ProviderReference\"='$ref', \"NextReconciliationAt\"=$next WHERE \"Id\"='$tx'; UPDATE orders SET \"PaymentReference\"='$ref' WHERE \"Id\"='$oid';" >/dev/null
}

echo '=== MANUAL PAID RECONCILIATION ==='
create_payment 'paid@zetruv.local' PAID
set_ref "$PAID_OID" "$PAID_TX" 'MOCK-RECON-PAID-MANUAL' "NOW()-INTERVAL '1 minute'"
PAID_R=$(curl -fsS -X POST "$BASE/api/v1/cms/payments/transactions/$PAID_TX/reconcile" -H "Authorization: Bearer $CTOK")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); assert x["providerStatus"]=="Paid"; assert x["transactionStatus"]=="Succeeded"; assert x["paymentStatus"]=="Paid"; assert x["orderStatus"]=="Processing"; assert x["attemptCount"]==1; assert x["nextAttemptAt"] is None' "$PAID_R"
PAID_DB=$(docker exec "$C" psql -At -F '|' -U zetruv -d "$DB" -c "SELECT o.\"PaymentStatus\",pt.\"Status\",pt.\"ReconciliationAttemptCount\",pt.\"NextReconciliationAt\" IS NULL,ir.\"Status\" FROM orders o JOIN payment_transactions pt ON pt.\"OrderId\"=o.\"Id\" JOIN inventory_reservations ir ON ir.\"OrderId\"=o.\"Id\" WHERE o.\"Id\"='$PAID_OID';")
[[ "$PAID_DB" == 'Paid|Succeeded|1|t|Consumed' ]]
echo 'PASS: manual provider reconciliation can recover a missed paid webhook'

echo '=== PENDING BACKOFF ==='
create_payment 'pending@zetruv.local' PEND
set_ref "$PEND_OID" "$PEND_TX" 'MOCK-RECON-PENDING-MANUAL' "NOW()-INTERVAL '1 minute'"
PEND_R=$(curl -fsS -X POST "$BASE/api/v1/cms/payments/transactions/$PEND_TX/reconcile" -H "Authorization: Bearer $CTOK")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); assert x["providerStatus"]=="Pending"; assert x["transactionStatus"]=="Pending"; assert x["paymentStatus"]=="Pending"; assert x["attemptCount"]==1; assert x["nextAttemptAt"] is not None; assert "Pending" in x["message"]' "$PEND_R"
docker exec "$C" psql -U zetruv -d "$DB" -c "UPDATE payment_transactions SET \"NextReconciliationAt\"=NOW()+INTERVAL '1 day' WHERE \"Id\"='$PEND_TX';" >/dev/null
echo 'PASS: pending provider status records attempt metadata and schedules backoff'

echo '=== FAILED STATUS RELEASE ==='
create_payment 'failed@zetruv.local' FAIL
set_ref "$FAIL_OID" "$FAIL_TX" 'MOCK-RECON-FAILED-MANUAL' "NOW()-INTERVAL '1 minute'"
FAIL_R=$(curl -fsS -X POST "$BASE/api/v1/cms/payments/transactions/$FAIL_TX/reconcile" -H "Authorization: Bearer $CTOK")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); assert x["providerStatus"]=="Failed"; assert x["transactionStatus"]=="Failed"; assert x["paymentStatus"]=="Failed"; assert x["attemptCount"]==1; assert x["nextAttemptAt"] is None' "$FAIL_R"
FAIL_DB=$(docker exec "$C" psql -At -F '|' -U zetruv -d "$DB" -c "SELECT o.\"PaymentStatus\",pt.\"Status\",ir.\"Status\",pv.\"StockQuantity\" FROM orders o JOIN payment_transactions pt ON pt.\"OrderId\"=o.\"Id\" JOIN inventory_reservations ir ON ir.\"OrderId\"=o.\"Id\" JOIN product_variants pv ON pv.\"Id\"=ir.\"ProductVariantId\" WHERE o.\"Id\"='$FAIL_OID';")
[[ "$FAIL_DB" == 'Failed|Failed|Released|18' ]]
echo 'PASS: failed provider reconciliation releases reserved stock exactly once'

DETAIL=$(curl -fsS "$BASE/api/v1/cms/orders/$PAID_OID" -H "Authorization: Bearer $CTOK")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); t=x["transactions"][0]; assert t["reconciliationAttemptCount"]==1; assert t["lastReconciliationAttemptAt"]; assert t["nextReconciliationAt"] is None; assert "Paid" in t["reconciliationMessage"]' "$DETAIL"
echo 'PASS: reconciliation metadata is visible in CMS order detail'

echo '=== WEBHOOK / RECONCILIATION RACE ==='
create_payment 'race@zetruv.local' RACE
set_ref "$RACE_OID" "$RACE_TX" 'MOCK-RECON-PAID-RACE' "NOW()-INTERVAL '1 minute'"
RACE_BODY="{\"providerReference\":\"MOCK-RECON-PAID-RACE\",\"status\":\"Paid\",\"amount\":50000,\"currency\":\"IDR\"}"
RACE_SIG=$(python3 -c 'import hmac,hashlib,sys; print(hmac.new(b"smoke-secret",sys.argv[1].encode(),hashlib.sha256).hexdigest())' "$RACE_BODY")
(curl -sS -o /tmp/zetruv-payment-reconciliation-manual.json -w '%{http_code}' -X POST "$BASE/api/v1/cms/payments/transactions/$RACE_TX/reconcile" -H "Authorization: Bearer $CTOK" > /tmp/zetruv-payment-reconciliation-manual.code) &
RACE_MANUAL_PID=$!
(curl -sS -o /tmp/zetruv-payment-reconciliation-webhook.json -w '%{http_code}' -X POST "$BASE/api/v1/payments/webhooks/mock" -H 'Content-Type: application/json' -H "X-Mock-Signature: $RACE_SIG" -d "$RACE_BODY" > /tmp/zetruv-payment-reconciliation-webhook.code) &
RACE_WEBHOOK_PID=$!
wait "$RACE_MANUAL_PID"
wait "$RACE_WEBHOOK_PID"
RACE_MANUAL_CODE=$(cat /tmp/zetruv-payment-reconciliation-manual.code)
RACE_WEBHOOK_CODE=$(cat /tmp/zetruv-payment-reconciliation-webhook.code)
[[ "$RACE_MANUAL_CODE" == 200 || "$RACE_MANUAL_CODE" == 409 ]]
[[ "$RACE_WEBHOOK_CODE" == 200 ]]
RACE_DB=$(docker exec "$C" psql -At -F '|' -U zetruv -d "$DB" -c "SELECT o.\"PaymentStatus\",pt.\"Status\",ir.\"Status\",pt.\"ReconciliationAttemptCount\" FROM orders o JOIN payment_transactions pt ON pt.\"OrderId\"=o.\"Id\" JOIN inventory_reservations ir ON ir.\"OrderId\"=o.\"Id\" WHERE o.\"Id\"='$RACE_OID';")
python3 -c 'import sys; p,t,r,a=sys.argv[1].split("|"); assert (p,t,r)==("Paid","Succeeded","Consumed"); assert int(a) in (0,1)' "$RACE_DB"
echo 'PASS: webhook and provider reconciliation serialize on the same order lock'

echo '=== BACKGROUND RECONCILIATION ==='
create_payment 'background@zetruv.local' BG
set_ref "$BG_OID" "$BG_TX" 'MOCK-RECON-PAID-BACKGROUND' "NOW()-INTERVAL '1 minute'"

sleep 2
BEFORE=$(docker exec "$C" psql -At -F '|' -U zetruv -d "$DB" -c "SELECT o.\"PaymentStatus\",pt.\"Status\",pt.\"ReconciliationAttemptCount\" FROM orders o JOIN payment_transactions pt ON pt.\"OrderId\"=o.\"Id\" WHERE o.\"Id\"='$BG_OID';")
[[ "$BEFORE" == 'Pending|Pending|0' ]]
echo 'PASS: disabled reconciliation worker does not mutate due payments'

stop_api
start_api true

BG_STATE=""
for _ in $(seq 1 15); do
  BG_STATE=$(docker exec "$C" psql -At -F '|' -U zetruv -d "$DB" -c "SELECT o.\"PaymentStatus\",pt.\"Status\",pt.\"ReconciliationAttemptCount\",pt.\"NextReconciliationAt\" IS NULL,ir.\"Status\" FROM orders o JOIN payment_transactions pt ON pt.\"OrderId\"=o.\"Id\" JOIN inventory_reservations ir ON ir.\"OrderId\"=o.\"Id\" WHERE o.\"Id\"='$BG_OID';")
  [[ "$BG_STATE" == 'Paid|Succeeded|1|t|Consumed' ]] && break
  sleep 1
done
[[ "$BG_STATE" == 'Paid|Succeeded|1|t|Consumed' ]]
echo 'PASS: background worker recovers missed paid webhook without manual action'

DUE_PENDING=$(docker exec "$C" psql -At -U zetruv -d "$DB" -c "SELECT \"ReconciliationAttemptCount\" FROM payment_transactions WHERE \"Id\"='$PEND_TX';")
[[ "$DUE_PENDING" == 1 ]]
echo 'PASS: future backoff prevents premature re-polling'

echo 'PASS: payment provider status reconciliation + backoff + background worker'
