#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

C=zetruv-pg-provider-mapping-runtime
DB=zetruv_provider_mapping_runtime_test
PORT=57442
API=18192
PID=""
cleanup(){ [[ -n "$PID" ]] && kill "$PID" >/dev/null 2>&1 || true; docker rm -f "$C" >/dev/null 2>&1 || true; rm -f /tmp/zetruv-pmr-*; }
trap cleanup EXIT

dotnet build src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --nologo
docker rm -f "$C" >/dev/null 2>&1 || true
docker run -d --name "$C" -e POSTGRES_USER=zetruv -e POSTGRES_PASSWORD=zetruvtest -e POSTGRES_DB="$DB" -p 127.0.0.1:$PORT:5432 postgres:17-alpine >/dev/null
until docker exec "$C" pg_isready -U zetruv -d "$DB" >/dev/null 2>&1; do sleep 1; done

export ConnectionStrings__Postgres="Host=127.0.0.1;Port=$PORT;Database=$DB;Username=zetruv;Password=zetruvtest"
export Jwt__Key='0123456789abcdef0123456789abcdef'
export CmsAdmin__Email='provider-runtime@zetruv.local'
export CmsAdmin__Password='ProviderRuntime123!'
export Payments__Provider=mock
export Payments__Mock__WebhookSecret=smoke-secret
export Shipping__Provider=mock
export GameAccountValidation__Provider=mock
export ASPNETCORE_ENVIRONMENT=Staging

dotnet run --project src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --no-build --urls="http://127.0.0.1:$API" >/tmp/zetruv-pmr.log 2>&1 & PID=$!
for _ in $(seq 1 40); do curl -fsS "http://127.0.0.1:$API/health" >/dev/null 2>&1 && break; sleep 1; done
curl -fsS "http://127.0.0.1:$API/health" >/dev/null

docker exec -i "$C" psql -v ON_ERROR_STOP=1 -U zetruv -d "$DB" <<'SQL'
INSERT INTO games ("Id","Name","Slug","IsActive","IsPopular","SortOrder","CreatedAt","UpdatedAt")
VALUES ('a1000000-0000-0000-0000-000000000001','Provider Runtime Game','provider-runtime-game',TRUE,FALSE,0,NOW(),NOW());
INSERT INTO products ("Id","CategoryId","GameId","Name","Slug","Kind","FulfillmentMethod","RequiresGameAccountValidation","IsActive","IsFeatured","SortOrder","CreatedAt","UpdatedAt")
VALUES ('a2000000-0000-0000-0000-000000000001',(SELECT "Id" FROM catalog_categories WHERE "Key"='top_up_games'),'a1000000-0000-0000-0000-000000000001','Provider Runtime Product','provider-runtime-product','TopUpGame','AUTO_ID',TRUE,TRUE,FALSE,0,NOW(),NOW());
INSERT INTO product_variants ("Id","ProductId","Name","Sku","Price","StockQuantity","IsActive","SortOrder","CreatedAt","UpdatedAt")
VALUES ('a3000000-0000-0000-0000-000000000001','a2000000-0000-0000-0000-000000000001','100 Diamonds','ZETRUV-100',25000,20,TRUE,0,NOW(),NOW());
INSERT INTO product_input_fields ("Id","ProductId","Key","Label","Scope","Type","IsRequired","IsSensitive","MaxLength","SortOrder","CreatedAt","UpdatedAt") VALUES
('a4000000-0000-0000-0000-000000000001','a2000000-0000-0000-0000-000000000001','userid','User ID','AccountValidation','Text',TRUE,FALSE,200,0,NOW(),NOW()),
('a4000000-0000-0000-0000-000000000002','a2000000-0000-0000-0000-000000000001','zoneid','Zone ID','AccountValidation','Text',TRUE,FALSE,200,1,NOW(),NOW());
SQL

json(){ python3 -c "import json,sys; print(json.load(sys.stdin)$1)"; }
validate(){ curl -fsS -X POST "http://127.0.0.1:$API/api/v1/game-account/validate" -H 'Content-Type: application/json' -d '{"productId":"a2000000-0000-0000-0000-000000000001","fields":{"userid":"10001","zoneid":"20001"}}'; }
checkout(){ curl -fsS -X POST "http://127.0.0.1:$API/api/v1/checkout/orders" -H 'Content-Type: application/json' -d "{\"customerName\":\"Provider Runtime\",\"customerEmail\":\"$2\",\"items\":[{\"productVariantId\":\"a3000000-0000-0000-0000-000000000001\",\"quantity\":1,\"gameAccountValidationId\":\"$1\"}]}"; }
pay(){ curl -fsS -X POST "http://127.0.0.1:$API/api/v1/checkout/orders/$1/payment" -H "X-Order-Access-Token: $2"; }
webhook(){
  local ref="$1"
  local body="{\"providerReference\":\"$ref\",\"status\":\"Paid\",\"amount\":25000,\"currency\":\"IDR\"}"
  local sig
  sig=$(python3 -c 'import hmac,hashlib,sys; print(hmac.new(b"smoke-secret",sys.argv[1].encode(),hashlib.sha256).hexdigest())' "$body")
  curl -fsS -X POST "http://127.0.0.1:$API/api/v1/payments/webhooks/mock" -H 'Content-Type: application/json' -H "X-Mock-Signature: $sig" -d "$body"
}

V=$(validate); VID=$(json '["validationId"]' <<<"$V")
CO=$(checkout "$VID" missing-map@zetruv.local)
OID=$(json '["id"]' <<<"$CO"); TOK=$(json '["orderAccessToken"]' <<<"$CO")
P=$(pay "$OID" "$TOK"); REF=$(json '["providerReference"]' <<<"$P")
webhook "$REF" >/dev/null
ROW=$(docker exec "$C" psql -At -F '|' -U zetruv -d "$DB" -c "SELECT oi.\"Id\",o.\"PaymentStatus\",o.\"Status\",oi.\"FulfillmentStatus\",oi.\"FulfillmentAttemptCount\",COALESCE(oi.\"FulfillmentMessage\",'') FROM orders o JOIN order_items oi ON oi.\"OrderId\"=o.\"Id\" WHERE o.\"Id\"='$OID';")
IFS='|' read -r ITEM PAYSTAT OSTAT FSTAT ATTEMPTS MSG <<<"$ROW"
[[ "$PAYSTAT" == Paid && "$OSTAT" == Processing && "$FSTAT" == Failed && "$ATTEMPTS" == 1 ]]
[[ "$MSG" == *"Active provider and provider SKU mappings are required"* ]]
echo 'PASS: AUTO_ID runtime fails closed when provider mapping is missing'

LOGIN=$(curl -fsS -X POST "http://127.0.0.1:$API/api/v1/cms/auth/login" -H 'Content-Type: application/json' -d '{"email":"provider-runtime@zetruv.local","password":"ProviderRuntime123!"}')
CTOK=$(json '["accessToken"]' <<<"$LOGIN")

PRE_CODE=$(curl -sS -o /tmp/zetruv-pmr-pre.json -w '%{http_code}' -X PUT "http://127.0.0.1:$API/api/v1/cms/provider-mappings/variants/a3000000-0000-0000-0000-000000000001" -H "Authorization: Bearer $CTOK" -H 'Content-Type: application/json' -d '{"providerSku":"PROVIDER-100","isActive":true}')
[[ "$PRE_CODE" == 409 ]]

GAME_CODE=$(curl -sS -o /dev/null -w '%{http_code}' -X PUT "http://127.0.0.1:$API/api/v1/cms/provider-mappings/games/a1000000-0000-0000-0000-000000000001" -H "Authorization: Bearer $CTOK" -H 'Content-Type: application/json' -d '{"providerCode":"mock","nicknameCheckEnabled":true,"isActive":true}')
[[ "$GAME_CODE" == 204 ]]
SKU_CODE=$(curl -sS -o /dev/null -w '%{http_code}' -X PUT "http://127.0.0.1:$API/api/v1/cms/provider-mappings/variants/a3000000-0000-0000-0000-000000000001" -H "Authorization: Bearer $CTOK" -H 'Content-Type: application/json' -d '{"providerSku":"PROVIDER-100","isActive":true}')
[[ "$SKU_CODE" == 204 ]]

MAPS=$(curl -fsS "http://127.0.0.1:$API/api/v1/cms/provider-mappings" -H "Authorization: Bearer $CTOK")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); g=next(v for v in x if v["gameId"]=="a1000000-0000-0000-0000-000000000001"); assert g["providerCode"]=="mock" and g["mappingActive"] is True; s=next(v for v in g["skus"] if v["variantId"]=="a3000000-0000-0000-0000-000000000001"); assert s["providerSku"]=="PROVIDER-100" and s["isOperational"] is True' "$MAPS"
echo 'PASS: CMS provider mapping upsert + operational projection'

R=$(curl -fsS -X POST "http://127.0.0.1:$API/api/v1/cms/fulfillment/orders/$OID/items/$ITEM/execute" -H "Authorization: Bearer $CTOK")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); assert x["status"]=="Completed" and x["attemptCount"]==2 and x["orderStatus"]=="Completed"; assert "PROVIDER-100" in x["reference"]' "$R"
echo 'PASS: CMS retry uses mapped provider SKU and completes the order'

ACT=$(curl -fsS "http://127.0.0.1:$API/api/v1/cms/fulfillment/orders/$OID/items/$ITEM/activity" -H "Authorization: Bearer $CTOK")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); types=[a["type"] for a in x]; assert types.count("ProviderAttemptStarted")==2; assert "ProviderAttemptFailed" in types and "ProviderAttemptSucceeded" in types; assert any(a["source"]=="Payment" and a["type"]=="ProviderAttemptFailed" for a in x); assert any(a["source"]=="CmsAdmin" and a["actorEmail"]=="provider-runtime@zetruv.local" and a["type"]=="ProviderAttemptSucceeded" for a in x); assert all("password" not in json.dumps(a).lower() for a in x)' "$ACT"
echo 'PASS: fulfillment activity API exposes payment failure + audited CMS retry without secrets'

GAME_OFF=$(curl -sS -o /dev/null -w '%{http_code}' -X PUT "http://127.0.0.1:$API/api/v1/cms/provider-mappings/games/a1000000-0000-0000-0000-000000000001" -H "Authorization: Bearer $CTOK" -H 'Content-Type: application/json' -d '{"providerCode":"mock","nicknameCheckEnabled":true,"isActive":false}')
[[ "$GAME_OFF" == 204 ]]
MAPS_OFF=$(curl -fsS "http://127.0.0.1:$API/api/v1/cms/provider-mappings" -H "Authorization: Bearer $CTOK")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); g=next(v for v in x if v["gameId"]=="a1000000-0000-0000-0000-000000000001"); s=next(v for v in g["skus"] if v["variantId"]=="a3000000-0000-0000-0000-000000000001"); assert g["mappingActive"] is False and s["isOperational"] is False' "$MAPS_OFF"
echo 'PASS: disabling game mapping makes variants non-operational'

echo 'PASS: provider mapping runtime + CMS + fail-closed + activity history'
