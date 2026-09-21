#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

C=zetruv-pg-cms-crud
DB=zetruv_cms_crud_test
PORT=57443
API=18193
PID=""

cleanup(){
  [[ -n "$PID" ]] && kill "$PID" >/dev/null 2>&1 || true
  docker rm -f "$C" >/dev/null 2>&1 || true
  rm -f /tmp/zetruv-cms-crud-*
}
trap cleanup EXIT

dotnet build src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --nologo

docker rm -f "$C" >/dev/null 2>&1 || true
docker run -d --name "$C"   -e POSTGRES_USER=zetruv   -e POSTGRES_PASSWORD=zetruvtest   -e POSTGRES_DB="$DB"   -p 127.0.0.1:$PORT:5432 postgres:17-alpine >/dev/null
until docker exec "$C" pg_isready -U zetruv -d "$DB" >/dev/null 2>&1; do sleep 1; done

export ConnectionStrings__Postgres="Host=127.0.0.1;Port=$PORT;Database=$DB;Username=zetruv;Password=zetruvtest"
export Jwt__Key='0123456789abcdef0123456789abcdef'
export CmsAdmin__Email='cms-crud@zetruv.local'
export CmsAdmin__Password='CmsCrud123!'
export Payments__Provider=mock
export Payments__Mock__WebhookSecret=smoke-secret
export Shipping__Provider=mock
export GameAccountValidation__Provider=mock
export ASPNETCORE_ENVIRONMENT=Staging

dotnet run --project src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --no-build   --urls="http://127.0.0.1:$API" >/tmp/zetruv-cms-crud-app.log 2>&1 & PID=$!
for _ in $(seq 1 40); do
  curl -fsS "http://127.0.0.1:$API/health" >/dev/null 2>&1 && break
  sleep 1
done
curl -fsS "http://127.0.0.1:$API/health" >/dev/null

BASE="http://127.0.0.1:$API"
extract_id(){ python3 -c 'import json,sys; x=json.load(sys.stdin); print(x["id"] if isinstance(x,dict) else x)'; }
json_value(){ python3 -c "import json,sys; print(json.load(sys.stdin)$1)"; }

LOGIN=$(curl -fsS -X POST "$BASE/api/v1/cms/auth/login"   -H 'Content-Type: application/json'   -d '{"email":"cms-crud@zetruv.local","password":"CmsCrud123!"}')
CTOK=$(json_value '["accessToken"]' <<<"$LOGIN")
AUTH=(-H "Authorization: Bearer $CTOK" -H 'Content-Type: application/json')

UNAUTH=$(curl -sS -o /dev/null -w '%{http_code}' "$BASE/api/v1/cms/catalog/products")
[[ "$UNAUTH" == 401 ]]
echo 'PASS: CMS endpoints reject unauthenticated access'

echo '=== CATALOG CRUD ==='
CAT=$(curl -fsS -X POST "$BASE/api/v1/cms/catalog/categories" "${AUTH[@]}"   -d '{"key":"cms-crud-games","name":"CMS CRUD Games","slug":"cms-crud-games","description":"Initial","iconUrl":null,"kind":"TopUpLogin","isActive":true,"sortOrder":91}')
CAT_ID=$(extract_id <<<"$CAT")

DUP_CAT=$(curl -sS -o /tmp/zetruv-cms-crud-dup-cat.json -w '%{http_code}'   -X POST "$BASE/api/v1/cms/catalog/categories" "${AUTH[@]}"   -d '{"key":"cms-crud-games","name":"Duplicate","slug":"another-slug","description":null,"iconUrl":null,"kind":"TopUpLogin","isActive":true,"sortOrder":92}')
[[ "$DUP_CAT" == 409 ]]

CAT_UP=$(curl -sS -o /dev/null -w '%{http_code}'   -X PUT "$BASE/api/v1/cms/catalog/categories/$CAT_ID" "${AUTH[@]}"   -d '{"key":"cms-crud-games","name":"CMS CRUD Games Updated","slug":"cms-crud-games","description":"Updated","iconUrl":"https://example.test/icon.png","kind":"TopUpLogin","isActive":true,"sortOrder":90}')
[[ "$CAT_UP" == 204 ]]

GAME=$(curl -fsS -X POST "$BASE/api/v1/cms/catalog/games" "${AUTH[@]}"   -d '{"name":"CMS CRUD Game","slug":"cms-crud-game","publisher":"Zetruv Test","imageUrl":"https://example.test/game.png","isActive":true,"isPopular":true,"sortOrder":91}')
GAME_ID=$(extract_id <<<"$GAME")

GAME_UP=$(curl -sS -o /dev/null -w '%{http_code}'   -X PUT "$BASE/api/v1/cms/catalog/games/$GAME_ID" "${AUTH[@]}"   -d '{"name":"CMS CRUD Game Updated","slug":"cms-crud-game","publisher":"Zetruv Test Updated","imageUrl":"https://example.test/game2.png","isActive":true,"isPopular":false,"sortOrder":90}')
[[ "$GAME_UP" == 204 ]]

PRODUCT=$(curl -fsS -X POST "$BASE/api/v1/cms/catalog/products" "${AUTH[@]}"   -d "{\"categoryId\":\"$CAT_ID\",\"gameId\":\"$GAME_ID\",\"name\":\"CMS CRUD Product\",\"slug\":\"cms-crud-product\",\"shortDescription\":\"Initial\",\"description\":\"Initial description\",\"thumbnailUrl\":\"https://example.test/product.png\",\"kind\":\"TopUpLogin\",\"fulfillmentMethod\":\"MANUAL_LOGIN\",\"requiresGameAccountValidation\":false,\"isActive\":true,\"isFeatured\":true,\"sortOrder\":91}")
PRODUCT_ID=$(extract_id <<<"$PRODUCT")

VARIANT=$(curl -fsS -X POST "$BASE/api/v1/cms/catalog/products/$PRODUCT_ID/variants" "${AUTH[@]}"   -d '{"name":"Package A","sku":"CMS-CRUD-A","price":100000,"compareAtPrice":120000,"stockQuantity":5,"weightGrams":null,"isActive":true,"sortOrder":1}')
VARIANT_ID=$(extract_id <<<"$VARIANT")

VARIANT_UP=$(curl -sS -o /dev/null -w '%{http_code}'   -X PUT "$BASE/api/v1/cms/catalog/products/$PRODUCT_ID/variants/$VARIANT_ID" "${AUTH[@]}"   -d '{"name":"Package A Updated","sku":"CMS-CRUD-A","price":110000,"compareAtPrice":125000,"stockQuantity":7,"weightGrams":null,"isActive":true,"sortOrder":2}')
[[ "$VARIANT_UP" == 204 ]]

IMAGE=$(curl -fsS -X POST "$BASE/api/v1/cms/catalog/products/$PRODUCT_ID/images" "${AUTH[@]}"   -d '{"url":"https://example.test/hero.png","altText":"Initial hero","sortOrder":1}')
IMAGE_ID=$(extract_id <<<"$IMAGE")

IMAGE_UP=$(curl -sS -o /dev/null -w '%{http_code}'   -X PUT "$BASE/api/v1/cms/catalog/products/$PRODUCT_ID/images/$IMAGE_ID" "${AUTH[@]}"   -d '{"url":"https://example.test/hero-updated.png","altText":"Updated hero","sortOrder":2}')
[[ "$IMAGE_UP" == 204 ]]

EMAIL_FIELD=$(curl -fsS -X POST "$BASE/api/v1/cms/catalog/products/$PRODUCT_ID/input-fields" "${AUTH[@]}"   -d '{"key":"email","label":"Account Email","scope":"LoginCredential","type":"Email","placeholder":"name@example.com","helpText":"Login email","isRequired":true,"isSensitive":false,"maxLength":200,"options":null,"sortOrder":1}')
EMAIL_FIELD_ID=$(extract_id <<<"$EMAIL_FIELD")

PASS_FIELD=$(curl -fsS -X POST "$BASE/api/v1/cms/catalog/products/$PRODUCT_ID/input-fields" "${AUTH[@]}"   -d '{"key":"password","label":"Account Password","scope":"LoginCredential","type":"Password","placeholder":null,"helpText":"Login password","isRequired":true,"isSensitive":true,"maxLength":200,"options":null,"sortOrder":2}')
PASS_FIELD_ID=$(extract_id <<<"$PASS_FIELD")

OTP_CODE=$(curl -sS -o /tmp/zetruv-cms-crud-otp.json -w '%{http_code}'   -X POST "$BASE/api/v1/cms/catalog/products/$PRODUCT_ID/input-fields" "${AUTH[@]}"   -d '{"key":"otp","label":"OTP","scope":"LoginCredential","type":"Text","placeholder":null,"helpText":null,"isRequired":false,"isSensitive":false,"maxLength":20,"options":null,"sortOrder":3}')
[[ "$OTP_CODE" == 400 ]]

PASS_FIELD_UP=$(curl -sS -o /dev/null -w '%{http_code}'   -X PUT "$BASE/api/v1/cms/catalog/products/$PRODUCT_ID/input-fields/$PASS_FIELD_ID" "${AUTH[@]}"   -d '{"key":"password","label":"Konami Password","scope":"LoginCredential","type":"Password","placeholder":null,"helpText":"Updated help","isRequired":true,"isSensitive":true,"maxLength":250,"options":null,"sortOrder":2}')
[[ "$PASS_FIELD_UP" == 204 ]]

PRODUCT_UP=$(curl -sS -o /dev/null -w '%{http_code}'   -X PUT "$BASE/api/v1/cms/catalog/products/$PRODUCT_ID" "${AUTH[@]}"   -d "{\"categoryId\":\"$CAT_ID\",\"gameId\":\"$GAME_ID\",\"name\":\"CMS CRUD Product Updated\",\"slug\":\"cms-crud-product\",\"shortDescription\":\"Updated\",\"description\":\"Updated description\",\"thumbnailUrl\":\"https://example.test/product2.png\",\"kind\":\"TopUpLogin\",\"fulfillmentMethod\":\"MANUAL_LOGIN\",\"requiresGameAccountValidation\":false,\"isActive\":true,\"isFeatured\":false,\"sortOrder\":90}")
[[ "$PRODUCT_UP" == 204 ]]

CMS_PRODUCT=$(curl -fsS "$BASE/api/v1/cms/catalog/products/$PRODUCT_ID" "${AUTH[@]}")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); assert x["name"]=="CMS CRUD Product Updated"; assert x["isActive"] is True; assert any(v["id"]==sys.argv[2] and v["price"]==110000 for v in x["variants"]); assert any(i["id"]==sys.argv[3] and i["url"].endswith("hero-updated.png") for i in x["images"]); assert any(f["id"]==sys.argv[4] and f["label"]=="Konami Password" and f["isSensitive"] is True for f in x["inputFields"])' "$CMS_PRODUCT" "$VARIANT_ID" "$IMAGE_ID" "$PASS_FIELD_ID"

PUBLIC_PRODUCT=$(curl -fsS "$BASE/api/v1/catalog/products/cms-crud-product")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); assert x["name"]=="CMS CRUD Product Updated"; assert x["fulfillmentMethod"]=="MANUAL_LOGIN"; assert x["isAvailable"] is True; assert [f["key"] for f in x["inputFields"]]==["email","password"]' "$PUBLIC_PRODUCT"
echo 'PASS: catalog category/game/product/variant/image/input-field create-update-read + public projection'

echo '=== PROMOTION CRUD ==='
START=$(python3 -c 'import datetime; print((datetime.datetime.now(datetime.timezone.utc)-datetime.timedelta(minutes=5)).isoformat())')
END=$(python3 -c 'import datetime; print((datetime.datetime.now(datetime.timezone.utc)+datetime.timedelta(hours=1)).isoformat())')
PROMO_PAYLOAD=$(python3 - "$VARIANT_ID" "$START" "$END" <<'PY'
import json,sys
print(json.dumps({
  "name":"CMS CRUD Flash Sale",
  "slug":"cms-crud-flash-sale",
  "isFlashSale":True,
  "isActive":True,
  "startsAt":sys.argv[2],
  "endsAt":sys.argv[3],
  "items":[{"productVariantId":sys.argv[1],"salePrice":90000,"sortOrder":1}]
}))
PY
)
PROMO=$(curl -fsS -X POST "$BASE/api/v1/cms/promotions" "${AUTH[@]}" -d "$PROMO_PAYLOAD")
PROMO_ID=$(extract_id <<<"$PROMO")

BAD_PROMO=$(python3 - "$VARIANT_ID" "$START" "$END" <<'PY'
import json,sys
print(json.dumps({
  "name":"Bad Promo","slug":"bad-promo","isFlashSale":True,"isActive":True,
  "startsAt":sys.argv[2],"endsAt":sys.argv[3],
  "items":[{"productVariantId":sys.argv[1],"salePrice":999999,"sortOrder":1}]
}))
PY
)
BAD_PROMO_CODE=$(curl -sS -o /tmp/zetruv-cms-crud-bad-promo.json -w '%{http_code}'   -X POST "$BASE/api/v1/cms/promotions" "${AUTH[@]}" -d "$BAD_PROMO")
[[ "$BAD_PROMO_CODE" == 400 ]]

PROMO_UP_PAYLOAD=$(python3 - "$VARIANT_ID" "$START" "$END" <<'PY'
import json,sys
print(json.dumps({
  "name":"CMS CRUD Flash Sale Updated","slug":"cms-crud-flash-sale","isFlashSale":True,"isActive":True,
  "startsAt":sys.argv[2],"endsAt":sys.argv[3],
  "items":[{"productVariantId":sys.argv[1],"salePrice":85000,"sortOrder":2}]
}))
PY
)
PROMO_UP=$(curl -sS -o /dev/null -w '%{http_code}'   -X PUT "$BASE/api/v1/cms/promotions/$PROMO_ID" "${AUTH[@]}" -d "$PROMO_UP_PAYLOAD")
[[ "$PROMO_UP" == 204 ]]

FLASH=$(curl -fsS "$BASE/api/v1/catalog/flash-sale")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); assert x["name"]=="CMS CRUD Flash Sale Updated"; assert len(x["items"])==1; assert x["items"][0]["variantId"]==sys.argv[2] and x["items"][0]["salePrice"]==85000' "$FLASH" "$VARIANT_ID"
echo 'PASS: promotion create-update validation + public flash-sale projection'

echo '=== HOMEPAGE CRUD ==='
HERO=$(curl -fsS -X POST "$BASE/api/v1/cms/homepage/heroes" "${AUTH[@]}"   -d '{"title":"CMS CRUD Hero","subtitle":"Initial subtitle","imageUrl":"https://example.test/hero-banner.png","primaryCtaLabel":"Shop","primaryCtaUrl":"/shop","secondaryCtaLabel":null,"secondaryCtaUrl":null,"isActive":true,"sortOrder":91,"startsAt":null,"endsAt":null}')
HERO_ID=$(extract_id <<<"$HERO")

BAD_HERO=$(curl -sS -o /tmp/zetruv-cms-crud-bad-hero.json -w '%{http_code}'   -X POST "$BASE/api/v1/cms/homepage/heroes" "${AUTH[@]}"   -d '{"title":"Bad Hero","subtitle":"Bad","imageUrl":"https://example.test/bad.png","primaryCtaLabel":null,"primaryCtaUrl":null,"secondaryCtaLabel":null,"secondaryCtaUrl":null,"isActive":true,"sortOrder":92,"startsAt":"2030-01-02T00:00:00Z","endsAt":"2030-01-01T00:00:00Z"}')
[[ "$BAD_HERO" == 400 ]]

HERO_UP=$(curl -sS -o /tmp/zetruv-cms-crud-hero-up.json -w '%{http_code}'   -X PUT "$BASE/api/v1/cms/homepage/heroes/$HERO_ID" "${AUTH[@]}"   -d '{"title":"CMS CRUD Hero Updated","subtitle":"Updated subtitle","imageUrl":"https://example.test/hero-banner2.png","primaryCtaLabel":"Explore","primaryCtaUrl":"/explore","secondaryCtaLabel":null,"secondaryCtaUrl":null,"isActive":true,"sortOrder":90,"startsAt":null,"endsAt":null}')
[[ "$HERO_UP" == 200 ]]

SECTIONS=$(curl -fsS "$BASE/api/v1/cms/homepage/sections" "${AUTH[@]}")
SECTION_KEY=$(python3 -c 'import json,sys; x=json.loads(sys.argv[1]); assert x; print(x[0]["key"])' "$SECTIONS")
SECTION_UP=$(curl -sS -o /tmp/zetruv-cms-crud-section-up.json -w '%{http_code}'   -X PUT "$BASE/api/v1/cms/homepage/sections/$SECTION_KEY" "${AUTH[@]}"   -d '{"title":"CMS CRUD Section","subtitle":"Updated by integration test","ctaLabel":"See all","ctaUrl":"/all","isEnabled":true,"sortOrder":90,"itemLimit":7}')
[[ "$SECTION_UP" == 200 ]]

HEROES=$(curl -fsS "$BASE/api/v1/cms/homepage/heroes" "${AUTH[@]}")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); h=next(v for v in x if v["id"]==sys.argv[2]); assert h["title"]=="CMS CRUD Hero Updated" and h["isActive"] is True' "$HEROES" "$HERO_ID"

SECTIONS2=$(curl -fsS "$BASE/api/v1/cms/homepage/sections" "${AUTH[@]}")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); s=next(v for v in x if v["key"]==sys.argv[2]); assert s["title"]=="CMS CRUD Section" and s["itemLimit"]==7 and s["isEnabled"] is True' "$SECTIONS2" "$SECTION_KEY"

HOME=$(curl -fsS "$BASE/api/v1/homepage")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); assert any(h["title"]=="CMS CRUD Hero Updated" for h in x["heroes"])' "$HOME"

HERO_DEL=$(curl -sS -o /dev/null -w '%{http_code}'   -X DELETE "$BASE/api/v1/cms/homepage/heroes/$HERO_ID" -H "Authorization: Bearer $CTOK")
[[ "$HERO_DEL" == 204 ]]
HOME_AFTER_HERO=$(curl -fsS "$BASE/api/v1/homepage")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); assert all(h["title"]!="CMS CRUD Hero Updated" for h in x["heroes"])' "$HOME_AFTER_HERO"
echo 'PASS: homepage hero CRUD + section update + public projection'

echo '=== ARTICLES CRUD ==='
ARTICLE_CAT=$(curl -fsS -X POST "$BASE/api/v1/cms/articles/categories" "${AUTH[@]}"   -d '{"name":"CMS CRUD News","slug":"cms-crud-news","isActive":true,"sortOrder":91}')
ARTICLE_CAT_ID=$(extract_id <<<"$ARTICLE_CAT")

ARTICLE_CAT_UP=$(curl -sS -o /dev/null -w '%{http_code}'   -X PUT "$BASE/api/v1/cms/articles/categories/$ARTICLE_CAT_ID" "${AUTH[@]}"   -d '{"name":"CMS CRUD News Updated","slug":"cms-crud-news","isActive":true,"sortOrder":90}')
[[ "$ARTICLE_CAT_UP" == 204 ]]

PUBLISHED=$(python3 -c 'import datetime; print((datetime.datetime.now(datetime.timezone.utc)-datetime.timedelta(minutes=1)).isoformat())')
ARTICLE_PAYLOAD=$(python3 - "$ARTICLE_CAT_ID" "$PUBLISHED" <<'PY'
import json,sys
print(json.dumps({
  "categoryId":sys.argv[1],
  "title":"CMS CRUD Article",
  "slug":"cms-crud-article",
  "excerpt":"Initial excerpt",
  "content":"Initial integration-test content",
  "thumbnailUrl":"https://example.test/article.png",
  "authorName":"Zetruv QA",
  "isPublished":True,
  "isFeatured":True,
  "publishedAt":sys.argv[2]
}))
PY
)
ARTICLE=$(curl -fsS -X POST "$BASE/api/v1/cms/articles" "${AUTH[@]}" -d "$ARTICLE_PAYLOAD")
ARTICLE_ID=$(extract_id <<<"$ARTICLE")

ARTICLE_DETAIL=$(curl -fsS "$BASE/api/v1/articles/cms-crud-article")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); assert x["title"]=="CMS CRUD Article" and x["category"]["slug"]=="cms-crud-news"' "$ARTICLE_DETAIL"

ARTICLE_UP_PAYLOAD=$(python3 - "$ARTICLE_CAT_ID" "$PUBLISHED" <<'PY'
import json,sys
print(json.dumps({
  "categoryId":sys.argv[1],
  "title":"CMS CRUD Article Updated",
  "slug":"cms-crud-article",
  "excerpt":"Updated excerpt",
  "content":"Updated integration-test content",
  "thumbnailUrl":"https://example.test/article2.png",
  "authorName":"Zetruv QA Updated",
  "isPublished":True,
  "isFeatured":False,
  "publishedAt":sys.argv[2]
}))
PY
)
ARTICLE_UP=$(curl -sS -o /dev/null -w '%{http_code}'   -X PUT "$BASE/api/v1/cms/articles/$ARTICLE_ID" "${AUTH[@]}" -d "$ARTICLE_UP_PAYLOAD")
[[ "$ARTICLE_UP" == 204 ]]

ARTICLE_LIST=$(curl -fsS "$BASE/api/v1/articles?q=Updated")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); assert any(a["slug"]=="cms-crud-article" and a["title"]=="CMS CRUD Article Updated" for a in x["items"])' "$ARTICLE_LIST"

ARTICLE_DEL=$(curl -sS -o /dev/null -w '%{http_code}'   -X DELETE "$BASE/api/v1/cms/articles/$ARTICLE_ID" -H "Authorization: Bearer $CTOK")
[[ "$ARTICLE_DEL" == 204 ]]
ARTICLE_PUBLIC_GONE=$(curl -sS -o /dev/null -w '%{http_code}' "$BASE/api/v1/articles/cms-crud-article")
[[ "$ARTICLE_PUBLIC_GONE" == 404 ]]

ARTICLE_CAT_DEL=$(curl -sS -o /dev/null -w '%{http_code}'   -X DELETE "$BASE/api/v1/cms/articles/categories/$ARTICLE_CAT_ID" -H "Authorization: Bearer $CTOK")
[[ "$ARTICLE_CAT_DEL" == 204 ]]
ARTICLE_CATS=$(curl -fsS "$BASE/api/v1/articles/categories")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); assert all(c["slug"]!="cms-crud-news" for c in x)' "$ARTICLE_CATS"
echo 'PASS: article category/article CRUD + publication/search/delete projection'

echo '=== SITE CRUD ==='
SETTINGS_UP=$(curl -sS -o /dev/null -w '%{http_code}'   -X PUT "$BASE/api/v1/cms/site/settings" "${AUTH[@]}"   -d '{"logoUrl":"https://example.test/logo.png","brandDescription":"CMS CRUD brand","copyrightText":"Copyright CMS CRUD","contactTeamLabel":"Talk to Zetruv","contactTeamUrl":"https://example.test/contact"}')
[[ "$SETTINGS_UP" == 204 ]]

SETTINGS=$(curl -fsS "$BASE/api/v1/cms/site/settings" "${AUTH[@]}")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); assert x["brandDescription"]=="CMS CRUD brand" and x["contactTeamLabel"]=="Talk to Zetruv"' "$SETTINGS"

FOOTER_LINK=$(curl -fsS -X POST "$BASE/api/v1/cms/site/footer-links" "${AUTH[@]}"   -d '{"group":"Support","label":"CMS CRUD Help","url":"/help-crud","isActive":true,"sortOrder":91}')
FOOTER_LINK_ID=$(extract_id <<<"$FOOTER_LINK")
FOOTER_LINK_UP=$(curl -sS -o /dev/null -w '%{http_code}'   -X PUT "$BASE/api/v1/cms/site/footer-links/$FOOTER_LINK_ID" "${AUTH[@]}"   -d '{"group":"Support","label":"CMS CRUD Help Updated","url":"/help-crud-updated","isActive":true,"sortOrder":90}')
[[ "$FOOTER_LINK_UP" == 204 ]]

SOCIAL=$(curl -fsS -X POST "$BASE/api/v1/cms/site/social-links" "${AUTH[@]}"   -d '{"platform":"CMSCRUD","url":"https://example.test/social","iconUrl":"https://example.test/social.png","isActive":true,"sortOrder":91}')
SOCIAL_ID=$(extract_id <<<"$SOCIAL")
SOCIAL_UP=$(curl -sS -o /dev/null -w '%{http_code}'   -X PUT "$BASE/api/v1/cms/site/social-links/$SOCIAL_ID" "${AUTH[@]}"   -d '{"platform":"CMSCRUD Updated","url":"https://example.test/social2","iconUrl":"https://example.test/social2.png","isActive":true,"sortOrder":90}')
[[ "$SOCIAL_UP" == 204 ]]

PAYMENT_METHOD=$(curl -fsS -X POST "$BASE/api/v1/cms/site/payment-methods" "${AUTH[@]}"   -d '{"code":"cmscrud","name":"CMS CRUD Pay","iconUrl":"https://example.test/pay.png","isActive":true,"sortOrder":91}')
PAYMENT_METHOD_ID=$(extract_id <<<"$PAYMENT_METHOD")

DUP_PAYMENT=$(curl -sS -o /tmp/zetruv-cms-crud-dup-payment.json -w '%{http_code}'   -X POST "$BASE/api/v1/cms/site/payment-methods" "${AUTH[@]}"   -d '{"code":"cmscrud","name":"Duplicate Pay","iconUrl":null,"isActive":true,"sortOrder":92}')
[[ "$DUP_PAYMENT" == 409 ]]

PAYMENT_UP=$(curl -sS -o /dev/null -w '%{http_code}'   -X PUT "$BASE/api/v1/cms/site/payment-methods/$PAYMENT_METHOD_ID" "${AUTH[@]}"   -d '{"code":"cmscrud","name":"CMS CRUD Pay Updated","iconUrl":"https://example.test/pay2.png","isActive":true,"sortOrder":90}')
[[ "$PAYMENT_UP" == 204 ]]

FOOTER=$(curl -fsS "$BASE/api/v1/site/footer")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); assert x["brandDescription"]=="CMS CRUD brand"; assert any(v["label"]=="CMS CRUD Help Updated" for v in x["links"]); assert any(v["platform"]=="CMSCRUD Updated" for v in x["socials"]); assert any(v["code"]=="cmscrud" and v["name"]=="CMS CRUD Pay Updated" for v in x["paymentMethods"])' "$FOOTER"
echo 'PASS: site settings/footer/social/payment create-update + public projection'

for spec in   "footer-links:$FOOTER_LINK_ID"   "social-links:$SOCIAL_ID"   "payment-methods:$PAYMENT_METHOD_ID"; do
  resource=${spec%%:*}
  id=${spec#*:}
  code=$(curl -sS -o /dev/null -w '%{http_code}'     -X DELETE "$BASE/api/v1/cms/site/$resource/$id" -H "Authorization: Bearer $CTOK")
  [[ "$code" == 204 ]]
done

FOOTER_GONE=$(curl -fsS "$BASE/api/v1/site/footer")
python3 -c 'import json,sys; x=json.loads(sys.argv[1]); assert all(v["label"]!="CMS CRUD Help Updated" for v in x["links"]); assert all(v["platform"]!="CMSCRUD Updated" for v in x["socials"]); assert all(v["code"]!="cmscrud" for v in x["paymentMethods"])' "$FOOTER_GONE"
echo 'PASS: site hard-delete projection'

echo '=== CATALOG / PROMOTION DELETE SEMANTICS ==='
PROMO_DEL=$(curl -sS -o /dev/null -w '%{http_code}'   -X DELETE "$BASE/api/v1/cms/promotions/$PROMO_ID" -H "Authorization: Bearer $CTOK")
[[ "$PROMO_DEL" == 204 ]]

IMAGE_DEL=$(curl -sS -o /dev/null -w '%{http_code}'   -X DELETE "$BASE/api/v1/cms/catalog/products/$PRODUCT_ID/images/$IMAGE_ID" -H "Authorization: Bearer $CTOK")
[[ "$IMAGE_DEL" == 204 ]]

for fid in "$EMAIL_FIELD_ID" "$PASS_FIELD_ID"; do
  code=$(curl -sS -o /dev/null -w '%{http_code}'     -X DELETE "$BASE/api/v1/cms/catalog/products/$PRODUCT_ID/input-fields/$fid" -H "Authorization: Bearer $CTOK")
  [[ "$code" == 204 ]]
done

VARIANT_DEL=$(curl -sS -o /dev/null -w '%{http_code}'   -X DELETE "$BASE/api/v1/cms/catalog/products/$PRODUCT_ID/variants/$VARIANT_ID" -H "Authorization: Bearer $CTOK")
[[ "$VARIANT_DEL" == 204 ]]

PRODUCT_DEL=$(curl -sS -o /dev/null -w '%{http_code}'   -X DELETE "$BASE/api/v1/cms/catalog/products/$PRODUCT_ID" -H "Authorization: Bearer $CTOK")
[[ "$PRODUCT_DEL" == 204 ]]

GAME_DEL=$(curl -sS -o /dev/null -w '%{http_code}'   -X DELETE "$BASE/api/v1/cms/catalog/games/$GAME_ID" -H "Authorization: Bearer $CTOK")
[[ "$GAME_DEL" == 204 ]]

CAT_DEL=$(curl -sS -o /dev/null -w '%{http_code}'   -X DELETE "$BASE/api/v1/cms/catalog/categories/$CAT_ID" -H "Authorization: Bearer $CTOK")
[[ "$CAT_DEL" == 204 ]]

PUBLIC_PRODUCT_GONE=$(curl -sS -o /dev/null -w '%{http_code}' "$BASE/api/v1/catalog/products/cms-crud-product")
[[ "$PUBLIC_PRODUCT_GONE" == 404 ]]

DB_STATE=$(docker exec "$C" psql -At -F '|' -U zetruv -d "$DB" -c "
SELECT
  (SELECT "IsActive" FROM catalog_categories WHERE "Id"='$CAT_ID'),
  (SELECT "IsActive" FROM games WHERE "Id"='$GAME_ID'),
  (SELECT "IsActive" FROM products WHERE "Id"='$PRODUCT_ID'),
  (SELECT "IsActive" FROM product_variants WHERE "Id"='$VARIANT_ID'),
  (SELECT "IsActive" FROM promotions WHERE "Id"='$PROMO_ID'),
  (SELECT COUNT(*) FROM product_images WHERE "Id"='$IMAGE_ID'),
  (SELECT COUNT(*) FROM product_input_fields WHERE "ProductId"='$PRODUCT_ID');
")
[[ "$DB_STATE" == 'f|f|f|f|f|0|0' ]]
echo 'PASS: catalog/promotion soft-delete + child hard-delete semantics'

echo
echo 'PASS: CMS CRUD integration suite'
