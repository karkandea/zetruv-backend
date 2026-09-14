#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

C=zetruv-pg-catalog-pdp
DB=zetruv_catalog_pdp_test
PORT=56441
API=18189
PID=""
cleanup(){ [[ -n "$PID" ]] && kill "$PID" >/dev/null 2>&1 || true; docker rm -f "$C" >/dev/null 2>&1 || true; }
trap cleanup EXIT

dotnet build src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --nologo
docker rm -f "$C" >/dev/null 2>&1 || true
docker run -d --name "$C" -e POSTGRES_USER=zetruv -e POSTGRES_PASSWORD=zetruvtest -e POSTGRES_DB="$DB" -p 127.0.0.1:$PORT:5432 postgres:17-alpine >/dev/null
until docker exec "$C" pg_isready -U zetruv -d "$DB" >/dev/null 2>&1; do sleep 1; done

export ConnectionStrings__Postgres="Host=127.0.0.1;Port=$PORT;Database=$DB;Username=zetruv;Password=zetruvtest"
export Jwt__Key='0123456789abcdef0123456789abcdef'
export CmsAdmin__Email='catalog-smoke@zetruv.local'
export CmsAdmin__Password='CatalogSmoke123!'
export Payments__Provider=mock
export Payments__Mock__WebhookSecret=smoke-secret
export GameAccountValidation__Provider=mock
export Fulfillment__AutoId__Provider=mock
export ManualLogin__EncryptionKey
ManualLogin__EncryptionKey=$(printf '%s' '0123456789abcdef0123456789abcdef' | openssl base64 -A)
export Shipping__Provider=mock
export ASPNETCORE_ENVIRONMENT=Staging

dotnet run --project src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --no-build --urls="http://127.0.0.1:$API" >/tmp/zetruv-catalog-pdp.log 2>&1 & PID=$!
for _ in $(seq 1 40); do curl -fsS "http://127.0.0.1:$API/health" >/dev/null 2>&1 && break; sleep 1; done
curl -fsS "http://127.0.0.1:$API/health" >/dev/null

docker exec -i "$C" psql -v ON_ERROR_STOP=1 -U zetruv -d "$DB" <<'SQL'
INSERT INTO games ("Id","Name","Slug","Publisher","IsActive","IsPopular","SortOrder","CreatedAt","UpdatedAt") VALUES
('a1000000-0000-0000-0000-000000000001','Genshin Impact','genshin-impact','HoYoverse',TRUE,TRUE,0,NOW(),NOW()),
('a1000000-0000-0000-0000-000000000002','Mobile Legends','mobile-legends','Moonton',TRUE,TRUE,1,NOW(),NOW()),
('a1000000-0000-0000-0000-000000000003','Hidden Game','hidden-game','Hidden Studio',FALSE,FALSE,2,NOW(),NOW());

INSERT INTO products ("Id","CategoryId","GameId","Name","Slug","Kind","FulfillmentMethod","RequiresGameAccountValidation","IsActive","IsFeatured","SortOrder","CreatedAt","UpdatedAt") VALUES
('a2000000-0000-0000-0000-000000000001',(SELECT "Id" FROM catalog_categories WHERE "Key"='top_up_login'),'a1000000-0000-0000-0000-000000000001','Genshin Impact','genshin-impact','TopUpLogin','MANUAL_LOGIN',FALSE,TRUE,TRUE,0,NOW(),NOW()),
('a2000000-0000-0000-0000-000000000002',(SELECT "Id" FROM catalog_categories WHERE "Key"='top_up_games'),'a1000000-0000-0000-0000-000000000002','Mobile Legends','mobile-legends','TopUpGame','AUTO_ID',TRUE,TRUE,TRUE,1,NOW(),NOW()),
('a2000000-0000-0000-0000-000000000003',(SELECT "Id" FROM catalog_categories WHERE "Key"='top_up_login'),'a1000000-0000-0000-0000-000000000003','Hidden Product','hidden-product','TopUpLogin','MANUAL_LOGIN',FALSE,TRUE,FALSE,2,NOW(),NOW()),
('a2000000-0000-0000-0000-000000000004',(SELECT "Id" FROM catalog_categories WHERE "Key"='top_up_login'),'a1000000-0000-0000-0000-000000000001','Unconfigured Product','unconfigured-product','TopUpLogin','MANUAL_LOGIN',FALSE,TRUE,FALSE,3,NOW(),NOW());

INSERT INTO product_variants ("Id","ProductId","Name","Sku","Price","StockQuantity","IsActive","SortOrder","CreatedAt","UpdatedAt") VALUES
('a3000000-0000-0000-0000-000000000001','a2000000-0000-0000-0000-000000000001','60 Genesis Crystals','GEN-60',16500,10,TRUE,0,NOW(),NOW()),
('a3000000-0000-0000-0000-000000000002','a2000000-0000-0000-0000-000000000001','300+30 Genesis Crystals','GEN-330',81000,0,TRUE,1,NOW(),NOW()),
('a3000000-0000-0000-0000-000000000003','a2000000-0000-0000-0000-000000000002','86 Diamonds','ML-86',25000,20,TRUE,0,NOW(),NOW()),
('a3000000-0000-0000-0000-000000000004','a2000000-0000-0000-0000-000000000003','Hidden Pack','HIDDEN-1',10000,10,TRUE,0,NOW(),NOW()),
('a3000000-0000-0000-0000-000000000005','a2000000-0000-0000-0000-000000000004','Disabled Pack','DISABLED-1',10000,10,FALSE,0,NOW(),NOW());

INSERT INTO promotions ("Id","Name","Slug","IsFlashSale","IsActive","StartsAt","EndsAt","CreatedAt","UpdatedAt") VALUES
('a4000000-0000-0000-0000-000000000001','Genesis Flash Sale','genesis-flash-sale',TRUE,TRUE,NOW()-INTERVAL '1 hour',NOW()+INTERVAL '2 hours',NOW(),NOW());
INSERT INTO promotion_items ("Id","PromotionId","ProductVariantId","SalePrice","SortOrder") VALUES
('a5000000-0000-0000-0000-000000000001','a4000000-0000-0000-0000-000000000001','a3000000-0000-0000-0000-000000000001',15000,0);
SQL

json_assert(){ python3 - "$@"; }

echo '=== LIST CONTRACT ==='
LIST=$(curl -fsS "http://127.0.0.1:$API/api/v1/catalog/products?kind=TopUpLogin&q=HoYoverse&pageSize=50")
python3 - "$LIST" <<'PY'
import json,sys
x=json.loads(sys.argv[1]); assert x['totalItems']==1 and len(x['items'])==1
p=x['items'][0]
assert p['slug']=='genshin-impact'
assert p['gameSlug']=='genshin-impact' and p['publisher']=='HoYoverse'
assert p['minPrice']==15000 and p['maxPrice']==81000
assert p['regularMinPrice']==16500 and p['regularMaxPrice']==81000
assert p['activeVariantCount']==2 and p['isAvailable'] is True and p['isOnSale'] is True
PY

ALL=$(curl -fsS "http://127.0.0.1:$API/api/v1/catalog/products?pageSize=50")
python3 - "$ALL" <<'PY'
import json,sys
slugs={x['slug'] for x in json.loads(sys.argv[1])['items']}
assert 'genshin-impact' in slugs and 'mobile-legends' in slugs
assert 'hidden-product' not in slugs and 'unconfigured-product' not in slugs
PY

echo '=== PDP CONTRACT ==='
DETAIL=$(curl -fsS "http://127.0.0.1:$API/api/v1/catalog/products/genshin-impact")
python3 - "$DETAIL" <<'PY'
import json,sys
p=json.loads(sys.argv[1]); assert p['isAvailable'] is True and p['isOnSale'] is True
assert p['fulfillmentMethod']=='MANUAL_LOGIN' and p['game']['publisher']=='HoYoverse'
assert len(p['variants'])==2
v={x['sku']:x for x in p['variants']}
assert v['GEN-60']['price']==16500 and v['GEN-60']['effectivePrice']==15000
assert v['GEN-60']['isOnSale'] is True and v['GEN-60']['isAvailable'] is True
assert v['GEN-60']['promotionName']=='Genesis Flash Sale' and v['GEN-60']['promotionEndsAt']
assert v['GEN-330']['effectivePrice']==81000 and v['GEN-330']['isAvailable'] is False
PY
[[ "$(curl -sS -o /dev/null -w '%{http_code}' "http://127.0.0.1:$API/api/v1/catalog/products/hidden-product")" == 404 ]]
[[ "$(curl -sS -o /dev/null -w '%{http_code}' "http://127.0.0.1:$API/api/v1/catalog/products/unconfigured-product")" == 404 ]]

echo '=== CHECKOUT PRICE PARITY ==='
ORDER=$(curl -fsS -X POST "http://127.0.0.1:$API/api/v1/checkout/orders" -H 'Content-Type: application/json' -d '{"customerEmail":"catalog@zetruv.local","items":[{"productVariantId":"a3000000-0000-0000-0000-000000000001","quantity":1,"loginCredentials":{"email":"player@example.com","password":"demo-password"}}]}')
python3 - "$ORDER" <<'PY'
import json,sys
x=json.loads(sys.argv[1]); assert x['items'][0]['unitPrice']==15000
assert x['subtotal']==16500 and x['discountAmount']==1500 and x['grandTotal']==15000
PY

echo 'PASS: catalog search + public gating + promo-aware PDP + checkout price parity'
