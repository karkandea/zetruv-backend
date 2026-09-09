#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

C=zetruv-pg-manual-login
DB=zetruv_manual_login_test
PORT=56440
API=18188
PID=""
cleanup(){ [[ -n "$PID" ]] && kill "$PID" >/dev/null 2>&1 || true; docker rm -f "$C" >/dev/null 2>&1 || true; }
trap cleanup EXIT

dotnet build src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --nologo
docker rm -f "$C" >/dev/null 2>&1 || true
docker run -d --name "$C" -e POSTGRES_USER=zetruv -e POSTGRES_PASSWORD=zetruvtest -e POSTGRES_DB="$DB" -p 127.0.0.1:$PORT:5432 postgres:17-alpine >/dev/null
until docker exec "$C" pg_isready -U zetruv -d "$DB" >/dev/null 2>&1; do sleep 1; done

export ConnectionStrings__Postgres="Host=127.0.0.1;Port=$PORT;Database=$DB;Username=zetruv;Password=zetruvtest"
export Jwt__Key='0123456789abcdef0123456789abcdef'
export CmsAdmin__Email='manual-login-smoke@zetruv.local'
export CmsAdmin__Password='ManualLoginSmoke123!'
export Payments__Provider=mock
export Payments__Mock__WebhookSecret=smoke-secret
export GameAccountValidation__Provider=mock
export Fulfillment__AutoId__Provider=mock
export ManualLogin__EncryptionKey
ManualLogin__EncryptionKey=$(printf '%s' '0123456789abcdef0123456789abcdef' | openssl base64 -A)
export ManualLogin__RetentionHours=24
export Shipping__Provider=mock
export ASPNETCORE_ENVIRONMENT=Staging

dotnet run --project src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --no-build --urls="http://127.0.0.1:$API" >/tmp/zetruv-manual-login.log 2>&1 & PID=$!
for _ in $(seq 1 40); do curl -fsS "http://127.0.0.1:$API/health" >/dev/null 2>&1 && break; sleep 1; done
curl -fsS "http://127.0.0.1:$API/health" >/dev/null

docker exec -i "$C" psql -v ON_ERROR_STOP=1 -U zetruv -d "$DB" <<'SQL'
INSERT INTO games ("Id","Name","Slug","IsActive","IsPopular","SortOrder","CreatedAt","UpdatedAt")
VALUES ('91000000-0000-0000-0000-000000000001','Manual Login Smoke Game','manual-login-smoke-game',TRUE,FALSE,0,NOW(),NOW());
INSERT INTO products ("Id","CategoryId","GameId","Name","Slug","Kind","FulfillmentMethod","RequiresGameAccountValidation","IsActive","IsFeatured","SortOrder","CreatedAt","UpdatedAt")
VALUES ('92000000-0000-0000-0000-000000000001',(SELECT "Id" FROM catalog_categories WHERE "Key"='top_up_login'),'91000000-0000-0000-0000-000000000001','Manual Login Smoke Product','manual-login-smoke-product','TopUpLogin','MANUAL_LOGIN',FALSE,TRUE,FALSE,0,NOW(),NOW());
INSERT INTO product_variants ("Id","ProductId","Name","Sku","Price","StockQuantity","IsActive","SortOrder","CreatedAt","UpdatedAt")
VALUES ('93000000-0000-0000-0000-000000000001','92000000-0000-0000-0000-000000000001','60 Crystals','MANUAL-60',25000,50,TRUE,0,NOW(),NOW());
SQL

json(){ python3 -c "import json,sys; print(json.load(sys.stdin)$1)"; }
checkout(){
  local credentials="$1" email="$2"
  curl -fsS -X POST "http://127.0.0.1:$API/api/v1/checkout/orders"     -H 'Content-Type: application/json'     -d "{\"customerName\":\"Manual Login Smoke\",\"customerEmail\":\"$email\",\"items\":[{\"productVariantId\":\"93000000-0000-0000-0000-000000000001\",\"quantity\":1,\"loginCredentials\":$credentials}]}"
}
pay(){ curl -fsS -X POST "http://127.0.0.1:$API/api/v1/checkout/orders/$1/payment" -H "X-Order-Access-Token: $2"; }
webhook(){
  local ref="$1" body sig
  body="{\"providerReference\":\"$ref\",\"status\":\"Paid\",\"amount\":25000,\"currency\":\"IDR\"}"
  sig=$(python3 -c 'import hmac,hashlib,sys; print(hmac.new(b"smoke-secret",sys.argv[1].encode(),hashlib.sha256).hexdigest())' "$body")
  curl -fsS -X POST "http://127.0.0.1:$API/api/v1/payments/webhooks/mock" -H 'Content-Type: application/json' -H "X-Mock-Signature: $sig" -d "$body"
}

echo '=== REQUIRED CREDENTIALS ==='
CODE=$(curl -sS -o /tmp/manual-login-error.json -w '%{http_code}' -X POST "http://127.0.0.1:$API/api/v1/checkout/orders"   -H 'Content-Type: application/json'   -d '{"customerEmail":"missing@zetruv.local","items":[{"productVariantId":"93000000-0000-0000-0000-000000000001","quantity":1}]}')
[[ "$CODE" == 400 ]]
grep -Fq 'Login credentials are required' /tmp/manual-login-error.json

echo '=== BLOCK ONE-TIME SECRETS ==='
CODE=$(curl -sS -o /tmp/manual-login-error.json -w '%{http_code}' -X POST "http://127.0.0.1:$API/api/v1/checkout/orders"   -H 'Content-Type: application/json'   -d '{"customerEmail":"otp@zetruv.local","items":[{"productVariantId":"93000000-0000-0000-0000-000000000001","quantity":1,"loginCredentials":{"email":"player@example.com","password":"demo-pass","otp":"123456"}}]}')
[[ "$CODE" == 400 ]]
grep -Fq 'not accepted at checkout' /tmp/manual-login-error.json

echo '=== ENCRYPTED CHECKOUT ==='
CREDS='{"email":"player@example.com","password":"P@ssword-demo-123","server":"Asia"}'
CO=$(checkout "$CREDS" manual@zetruv.local)
[[ "$CO" != *'P@ssword-demo-123'* ]]
OID=$(json '["id"]' <<<"$CO")
TOK=$(json '["orderAccessToken"]' <<<"$CO")
ITEM=$(docker exec "$C" psql -At -U zetruv -d "$DB" -c "SELECT \"Id\" FROM order_items WHERE \"OrderId\"='$OID';")
ENC=$(docker exec "$C" psql -At -F '|' -U zetruv -d "$DB" -c "SELECT (\"EncryptedPayload\" IS NOT NULL),POSITION('P@ssword-demo-123' IN COALESCE(\"EncryptedPayload\",''))=0,\"RevealCount\",(\"ClearedAt\" IS NULL) FROM manual_login_credentials WHERE \"OrderItemId\"='$ITEM';")
[[ "$ENC" == 't|t|0|t' ]]

LOGIN=$(curl -fsS -X POST "http://127.0.0.1:$API/api/v1/cms/auth/login" -H 'Content-Type: application/json' -d '{"email":"manual-login-smoke@zetruv.local","password":"ManualLoginSmoke123!"}')
CTOK=$(json '["accessToken"]' <<<"$LOGIN")
REVEAL_URL="http://127.0.0.1:$API/api/v1/cms/fulfillment/orders/$OID/items/$ITEM/manual-login-credentials"
CODE=$(curl -sS -o /tmp/manual-login-reveal.json -w '%{http_code}' "$REVEAL_URL" -H "Authorization: Bearer $CTOK")
[[ "$CODE" == 409 ]]

echo '=== PAID-ONLY REVEAL ==='
P=$(pay "$OID" "$TOK")
REF=$(json '["providerReference"]' <<<"$P")
webhook "$REF" >/dev/null
STATE=$(docker exec "$C" psql -At -F '|' -U zetruv -d "$DB" -c "SELECT o.\"PaymentStatus\",o.\"Status\",oi.\"FulfillmentStatus\" FROM orders o JOIN order_items oi ON oi.\"OrderId\"=o.\"Id\" WHERE o.\"Id\"='$OID';")
[[ "$STATE" == 'Paid|Processing|Processing' ]]
R=$(curl -fsS "$REVEAL_URL" -H "Authorization: Bearer $CTOK")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); assert x["fields"]["email"]=="player@example.com"; assert x["fields"]["password"]=="P@ssword-demo-123"; assert x["fields"]["server"]=="Asia"; assert x["revealCount"]==1' "$R"
AUDIT=$(docker exec "$C" psql -At -F '|' -U zetruv -d "$DB" -c "SELECT \"RevealCount\",(\"LastRevealedAt\" IS NOT NULL) FROM manual_login_credentials WHERE \"OrderItemId\"='$ITEM';")
[[ "$AUDIT" == '1|t' ]]

echo '=== QUEUE METADATA ONLY ==='
Q=$(curl -fsS "http://127.0.0.1:$API/api/v1/cms/fulfillment/queue?method=MANUAL_LOGIN" -H "Authorization: Bearer $CTOK")
[[ "$Q" != *'P@ssword-demo-123'* ]]
python3 -c 'import json,sys; d=json.loads(sys.argv[1]); iid=sys.argv[2]; x=next(i for i in d["items"] if str(i["orderItemId"])==iid); assert x["hasManualLoginCredentials"] is True; assert set(x["manualLoginCredentialFields"])=={"email","password","server"}' "$Q" "$ITEM"

echo '=== COMPLETION CLEARS CREDENTIALS ==='
F=$(curl -fsS -X PUT "http://127.0.0.1:$API/api/v1/cms/orders/$OID/items/$ITEM/fulfillment"   -H "Authorization: Bearer $CTOK" -H 'Content-Type: application/json'   -d '{"status":"Completed","reference":"MANUAL-SMOKE-OK"}')
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); assert x["status"]=="Completed" and x["orderStatus"]=="Completed"' "$F"
CLEARED=$(docker exec "$C" psql -At -F '|' -U zetruv -d "$DB" -c "SELECT (\"EncryptedPayload\" IS NULL),(\"ClearedAt\" IS NOT NULL) FROM manual_login_credentials WHERE \"OrderItemId\"='$ITEM';")
[[ "$CLEARED" == 't|t' ]]
CODE=$(curl -sS -o /tmp/manual-login-reveal.json -w '%{http_code}' "$REVEAL_URL" -H "Authorization: Bearer $CTOK")
[[ "$CODE" == 410 ]]

echo '=== EXPIRY CLEARS CREDENTIALS ==='
CO2=$(checkout '{"email":"expire@example.com","password":"expire-demo-pass"}' expire@zetruv.local)
OID2=$(json '["id"]' <<<"$CO2"); TOK2=$(json '["orderAccessToken"]' <<<"$CO2")
ITEM2=$(docker exec "$C" psql -At -U zetruv -d "$DB" -c "SELECT \"Id\" FROM order_items WHERE \"OrderId\"='$OID2';")
P2=$(pay "$OID2" "$TOK2"); REF2=$(json '["providerReference"]' <<<"$P2"); webhook "$REF2" >/dev/null
docker exec "$C" psql -U zetruv -d "$DB" -c "UPDATE manual_login_credentials SET \"ExpiresAt\"=NOW()-INTERVAL '1 minute' WHERE \"OrderItemId\"='$ITEM2';" >/dev/null
CODE=$(curl -sS -o /tmp/manual-login-reveal.json -w '%{http_code}' "http://127.0.0.1:$API/api/v1/cms/fulfillment/orders/$OID2/items/$ITEM2/manual-login-credentials" -H "Authorization: Bearer $CTOK")
[[ "$CODE" == 410 ]]
[[ "$(docker exec "$C" psql -At -U zetruv -d "$DB" -c "SELECT (\"EncryptedPayload\" IS NULL) FROM manual_login_credentials WHERE \"OrderItemId\"='$ITEM2';")" == 't' ]]

echo '=== CANCELLATION CLEARS CREDENTIALS ==='
CO3=$(checkout '{"email":"cancel@example.com","password":"cancel-demo-pass"}' cancel@zetruv.local)
OID3=$(json '["id"]' <<<"$CO3")
ITEM3=$(docker exec "$C" psql -At -U zetruv -d "$DB" -c "SELECT \"Id\" FROM order_items WHERE \"OrderId\"='$OID3';")
curl -fsS -X PUT "http://127.0.0.1:$API/api/v1/cms/orders/$OID3/status"   -H "Authorization: Bearer $CTOK" -H 'Content-Type: application/json'   -d '{"status":"Cancelled"}' >/dev/null
CANCELLED=$(docker exec "$C" psql -At -F '|' -U zetruv -d "$DB" -c "SELECT o.\"Status\",(\"EncryptedPayload\" IS NULL),(\"ClearedAt\" IS NOT NULL) FROM orders o JOIN order_items oi ON oi.\"OrderId\"=o.\"Id\" JOIN manual_login_credentials mlc ON mlc.\"OrderItemId\"=oi.\"Id\" WHERE o.\"Id\"='$OID3';")
[[ "$CANCELLED" == 'Cancelled|t|t' ]]

echo 'PASS: MANUAL_LOGIN encrypted checkout + paid reveal + audit + queue metadata + completion/expiry/cancellation cleanup'
