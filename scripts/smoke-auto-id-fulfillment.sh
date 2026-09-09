#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

C=zetruv-pg-auto-id-fulfillment
DB=zetruv_auto_id_fulfillment_test
PORT=56439
API=18187
PID=""
cleanup(){ [[ -n "$PID" ]] && kill "$PID" >/dev/null 2>&1 || true; docker rm -f "$C" >/dev/null 2>&1 || true; }
trap cleanup EXIT

dotnet build src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --nologo
docker rm -f "$C" >/dev/null 2>&1 || true
docker run -d --name "$C" -e POSTGRES_USER=zetruv -e POSTGRES_PASSWORD=zetruvtest -e POSTGRES_DB="$DB" -p 127.0.0.1:$PORT:5432 postgres:17-alpine >/dev/null
until docker exec "$C" pg_isready -U zetruv -d "$DB" >/dev/null 2>&1; do sleep 1; done

export ConnectionStrings__Postgres="Host=127.0.0.1;Port=$PORT;Database=$DB;Username=zetruv;Password=zetruvtest"
export Jwt__Key='0123456789abcdef0123456789abcdef'
export CmsAdmin__Email='fulfillment-smoke@zetruv.local'
export CmsAdmin__Password='FulfillmentSmoke123!'
export Payments__Provider=mock
export Payments__Mock__WebhookSecret=smoke-secret
export GameAccountValidation__Provider=mock
export Fulfillment__AutoId__Provider=mock
export Shipping__Provider=mock
export ASPNETCORE_ENVIRONMENT=Staging

dotnet run --project src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --no-build --urls="http://127.0.0.1:$API" >/tmp/zetruv-auto-id-fulfillment.log 2>&1 & PID=$!
for _ in $(seq 1 40); do curl -fsS "http://127.0.0.1:$API/health" >/dev/null 2>&1 && break; sleep 1; done
curl -fsS "http://127.0.0.1:$API/health" >/dev/null

docker exec -i "$C" psql -v ON_ERROR_STOP=1 -U zetruv -d "$DB" <<'SQL'
INSERT INTO games ("Id","Name","Slug","IsActive","IsPopular","SortOrder","CreatedAt","UpdatedAt") VALUES ('81000000-0000-0000-0000-000000000001','Fulfillment Smoke Game','fulfillment-smoke-game',TRUE,FALSE,0,NOW(),NOW());
INSERT INTO products ("Id","CategoryId","GameId","Name","Slug","Kind","FulfillmentMethod","RequiresGameAccountValidation","IsActive","IsFeatured","SortOrder","CreatedAt","UpdatedAt") VALUES ('82000000-0000-0000-0000-000000000001',(SELECT "Id" FROM catalog_categories WHERE "Key"='top_up_games'),'81000000-0000-0000-0000-000000000001','Fulfillment Smoke Product','fulfillment-smoke-product','TopUpGame','AUTO_ID',TRUE,TRUE,FALSE,0,NOW(),NOW());
INSERT INTO product_variants ("Id","ProductId","Name","Sku","Price","StockQuantity","IsActive","SortOrder","CreatedAt","UpdatedAt") VALUES ('83000000-0000-0000-0000-000000000001','82000000-0000-0000-0000-000000000001','100 Diamonds','FULFILL-100',25000,50,TRUE,0,NOW(),NOW());
SQL

validate(){ curl -fsS -X POST "http://127.0.0.1:$API/api/v1/game-account/validate" -H 'Content-Type: application/json' -d "{\"productId\":\"82000000-0000-0000-0000-000000000001\",\"fields\":$1}"; }
checkout(){ curl -fsS -X POST "http://127.0.0.1:$API/api/v1/checkout/orders" -H 'Content-Type: application/json' -d "{\"customerName\":\"Fulfillment Smoke\",\"customerEmail\":\"$2\",\"items\":[{\"productVariantId\":\"83000000-0000-0000-0000-000000000001\",\"quantity\":1,\"gameAccountValidationId\":\"$1\"}]}"; }
json(){ python3 -c "import json,sys; print(json.load(sys.stdin)$1)"; }
pay(){ curl -fsS -X POST "http://127.0.0.1:$API/api/v1/checkout/orders/$1/payment" -H "X-Order-Access-Token: $2"; }
webhook(){ body="{\"providerReference\":\"$1\",\"status\":\"Paid\",\"amount\":25000,\"currency\":\"IDR\"}"; sig=$(python3 -c 'import hmac,hashlib,sys; print(hmac.new(b"smoke-secret",sys.argv[1].encode(),hashlib.sha256).hexdigest())' "$body"); curl -fsS -X POST "http://127.0.0.1:$API/api/v1/payments/webhooks/mock" -H 'Content-Type: application/json' -H "X-Mock-Signature: $sig" -d "$body"; }

V=$(validate '{"userId":"10001","zoneId":"20001","nickname":"SmokeSuccess"}'); VID=$(json '["validationId"]' <<<"$V")
CO=$(checkout "$VID" success@zetruv.local); OID=$(json '["id"]' <<<"$CO"); TOK=$(json '["orderAccessToken"]' <<<"$CO")
P=$(pay "$OID" "$TOK"); REF=$(json '["providerReference"]' <<<"$P"); webhook "$REF" >/dev/null
STATE=$(docker exec "$C" psql -At -F '|' -U zetruv -d "$DB" -c "SELECT o.\"PaymentStatus\",o.\"Status\",oi.\"FulfillmentStatus\",oi.\"FulfillmentAttemptCount\",(oi.\"FulfillmentReference\" IS NOT NULL) FROM orders o JOIN order_items oi ON oi.\"OrderId\"=o.\"Id\" WHERE o.\"Id\"='$OID';")
[[ "$STATE" == 'Paid|Completed|Completed|1|t' ]]
webhook "$REF" >/dev/null
[[ "$(docker exec "$C" psql -At -U zetruv -d "$DB" -c "SELECT \"FulfillmentAttemptCount\" FROM order_items WHERE \"OrderId\"='$OID';")" == '1' ]]

V2=$(validate '{"userId":"10002","zoneId":"20002","simulateFulfillmentFailure":"true"}'); VID2=$(json '["validationId"]' <<<"$V2")
CO2=$(checkout "$VID2" failure@zetruv.local); OID2=$(json '["id"]' <<<"$CO2"); TOK2=$(json '["orderAccessToken"]' <<<"$CO2")
P2=$(pay "$OID2" "$TOK2"); REF2=$(json '["providerReference"]' <<<"$P2"); webhook "$REF2" >/dev/null
ROW=$(docker exec "$C" psql -At -F '|' -U zetruv -d "$DB" -c "SELECT oi.\"Id\",o.\"Status\",oi.\"FulfillmentStatus\",oi.\"FulfillmentAttemptCount\" FROM orders o JOIN order_items oi ON oi.\"OrderId\"=o.\"Id\" WHERE o.\"Id\"='$OID2';")
IFS='|' read -r ITEM2 OS2 IS2 AC2 <<<"$ROW"
[[ "$OS2" == Processing && "$IS2" == Failed && "$AC2" == 1 ]]

LOGIN=$(curl -fsS -X POST "http://127.0.0.1:$API/api/v1/cms/auth/login" -H 'Content-Type: application/json' -d '{"email":"fulfillment-smoke@zetruv.local","password":"FulfillmentSmoke123!"}')
CTOK=$(json '["accessToken"]' <<<"$LOGIN")
Q=$(curl -fsS "http://127.0.0.1:$API/api/v1/cms/fulfillment/queue?status=Failed" -H "Authorization: Bearer $CTOK")
python3 -c 'import json,sys; d=json.loads(sys.argv[1]); iid=sys.argv[2]; assert any(str(x["orderItemId"])==iid for x in d["items"])' "$Q" "$ITEM2"

docker exec "$C" psql -U zetruv -d "$DB" -c "UPDATE game_account_validations SET \"InputJson\"='{\"userId\":\"10002\",\"zoneId\":\"20002\"}'::jsonb WHERE \"OrderItemId\"='$ITEM2';" >/dev/null
R=$(curl -fsS -X POST "http://127.0.0.1:$API/api/v1/cms/fulfillment/orders/$OID2/items/$ITEM2/execute" -H "Authorization: Bearer $CTOK")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); assert x["status"]=="Completed" and x["attemptCount"]==2 and x["orderStatus"]=="Completed" and x["reference"]' "$R"

echo 'PASS: AUTO_ID paid execution + duplicate webhook idempotency + failure queue + CMS retry'
