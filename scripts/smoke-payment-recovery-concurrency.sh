#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

C=zetruv-pg-payment-recovery-concurrency
DB=zetruv_payment_recovery_concurrency_test
PORT=57441
API=18191
PID=""
cleanup(){ [[ -n "$PID" ]] && kill "$PID" >/dev/null 2>&1 || true; docker rm -fv "$C" >/dev/null 2>&1 || true; rm -f /tmp/zetruv-prc-*; }
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
dotnet run --project src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --no-build --urls="http://127.0.0.1:$API" >/tmp/zetruv-prc.log 2>&1 & PID=$!
for _ in $(seq 1 40); do curl -fsS "http://127.0.0.1:$API/health" >/dev/null 2>&1 && break; sleep 1; done
curl -fsS "http://127.0.0.1:$API/health" >/dev/null

docker exec -i "$C" psql -v ON_ERROR_STOP=1 -U zetruv -d "$DB" <<'SQL'
INSERT INTO products ("Id","CategoryId","Name","Slug","Kind","FulfillmentMethod","RequiresGameAccountValidation","IsActive","IsFeatured","SortOrder","CreatedAt","UpdatedAt")
VALUES ('91000000-0000-0000-0000-000000000001',(SELECT "Id" FROM catalog_categories WHERE "Key"='top_up_games'),'Payment Recovery Smoke','payment-recovery-smoke','TopUpGame','AUTO_ID',FALSE,TRUE,FALSE,0,NOW(),NOW());
INSERT INTO product_variants ("Id","ProductId","Name","Sku","Price","StockQuantity","IsActive","SortOrder","CreatedAt","UpdatedAt")
VALUES ('92000000-0000-0000-0000-000000000001','91000000-0000-0000-0000-000000000001','Default','PAY-RECOVERY-100',50000,10,TRUE,0,NOW(),NOW());
SQL

ORDER=$(curl -fsS -X POST "http://127.0.0.1:$API/api/v1/checkout/orders" -H 'Content-Type: application/json' -d '{"customerName":"Recovery Smoke","customerEmail":"recovery@zetruv.local","customerPhone":null,"items":[{"productVariantId":"92000000-0000-0000-0000-000000000001","quantity":1}]}')
OID=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["id"])' <<<"$ORDER")
ONO=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["orderNumber"])' <<<"$ORDER")
TOK=$(python3 -c 'import json,sys; print(json.load(sys.stdin)["orderAccessToken"])' <<<"$ORDER")

pay_async(){
  local n="$1"
  curl -sS -o "/tmp/zetruv-prc-$n.json" -w '%{http_code}' -X POST "http://127.0.0.1:$API/api/v1/checkout/orders/$OID/payment" -H "X-Order-Access-Token: $TOK" > "/tmp/zetruv-prc-$n.code"
}
pay_async 1 & P1=$!
pay_async 2 & P2=$!
wait "$P1" "$P2"
[[ "$(cat /tmp/zetruv-prc-1.code)" == 200 ]]
[[ "$(cat /tmp/zetruv-prc-2.code)" == 200 ]]
python3 - <<'PY'
import json
one=json.load(open('/tmp/zetruv-prc-1.json'))
two=json.load(open('/tmp/zetruv-prc-2.json'))
assert one['providerReference']==two['providerReference'], (one,two)
assert one['paymentUrl']==two['paymentUrl'], (one,two)
assert sorted([one['isRecovery'],two['isRecovery']]) == [False,True], (one,two)
print('PASS: concurrent initiation shares one provider session')
PY
REF1=$(python3 -c 'import json; print(json.load(open("/tmp/zetruv-prc-1.json"))["providerReference"])')
[[ "$(docker exec "$C" psql -At -U zetruv -d "$DB" -c "SELECT count(*) FROM payment_transactions WHERE \"OrderId\"='$OID' AND \"Status\"='Pending';")" == 1 ]]

curl -fsS -o /tmp/zetruv-prc-3.json -X POST "http://127.0.0.1:$API/api/v1/checkout/orders/$OID/payment" -H "X-Order-Access-Token: $TOK"
python3 - "$REF1" <<'PY'
import json,sys
x=json.load(open('/tmp/zetruv-prc-3.json'))
assert x['providerReference']==sys.argv[1]
assert x['isRecovery'] is True
print('PASS: later initiation recovers the same active session')
PY
docker exec "$C" psql -U zetruv -d "$DB" -c "UPDATE payment_transactions SET \"ExpiresAt\"=NOW()-INTERVAL '1 minute' WHERE \"OrderId\"='$OID' AND \"ProviderReference\"='$REF1';" >/dev/null
curl -fsS -o /tmp/zetruv-prc-4.json -X POST "http://127.0.0.1:$API/api/v1/checkout/orders/$OID/payment" -H "X-Order-Access-Token: $TOK"
REF2=$(python3 -c 'import json; print(json.load(open("/tmp/zetruv-prc-4.json"))["providerReference"])')
REC4=$(python3 -c 'import json; print(str(json.load(open("/tmp/zetruv-prc-4.json"))["isRecovery"]).lower())')
[[ "$REF2" != "$REF1" ]]
[[ "$REC4" == false ]]
STATE=$(docker exec "$C" psql -At -F '|' -U zetruv -d "$DB" -c "SELECT count(*) FILTER (WHERE \"Status\"='Failed'),count(*) FILTER (WHERE \"Status\"='Pending') FROM payment_transactions WHERE \"OrderId\"='$OID';")
[[ "$STATE" == '1|1' ]]

LOOKUP=$(curl -fsS -X POST "http://127.0.0.1:$API/api/v1/orders/lookup" -H 'Content-Type: application/json' -d "{\"orderNumber\":\"$ONO\",\"customerEmail\":\"recovery@zetruv.local\",\"customerPhone\":null}")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); assert x["hasActivePaymentSession"] is True; assert x["canInitiatePayment"] is True; assert x["activePaymentExpiresAt"]; print("PASS: expired session is failed and replaced with one new active session")' "$LOOKUP"

echo 'PASS: payment recovery concurrency + expiry replacement'
