#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

C=zetruv-pg-media-storage
DB=zetruv_media_storage_test
PORT=57447
API=18197
PID=""
MEDIA_DIR=$(mktemp -d)
cleanup(){
  [[ -n "$PID" ]] && kill "$PID" >/dev/null 2>&1 || true
  docker rm -fv "$C" >/dev/null 2>&1 || true
  rm -rf "$MEDIA_DIR"
  rm -f /tmp/zetruv-media-*.json /tmp/zetruv-media-*.png /tmp/zetruv-media-*.bin /tmp/zetruv-media-*.headers
}
trap cleanup EXIT

dotnet build src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --nologo
docker rm -fv "$C" >/dev/null 2>&1 || true
docker run -d --name "$C" -e POSTGRES_USER=zetruv -e POSTGRES_PASSWORD=zetruvtest -e POSTGRES_DB="$DB" -p 127.0.0.1:$PORT:5432 postgres:17-alpine >/dev/null
until docker exec "$C" pg_isready -U zetruv -d "$DB" >/dev/null 2>&1; do sleep 1; done

export ConnectionStrings__Postgres="Host=127.0.0.1;Port=$PORT;Database=$DB;Username=zetruv;Password=zetruvtest"
export Jwt__Key='0123456789abcdef0123456789abcdef'
export CmsAdmin__Email='media-admin@zetruv.test'
export CmsAdmin__Password='media-smoke-password'
export Payments__Provider=mock
export Payments__Mock__WebhookSecret=smoke-secret
export Shipping__Provider=mock
export GameAccountValidation__Provider=mock
export Media__Provider=local
export Media__LocalPath="$MEDIA_DIR"
export Media__PublicPath=/media
export Media__MaxFileSizeBytes=65536
export ASPNETCORE_ENVIRONMENT=Development

dotnet run --project src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --no-build --urls="http://127.0.0.1:$API" >/tmp/zetruv-media-storage.log 2>&1 & PID=$!
for _ in $(seq 1 40); do
  curl -fsS "http://127.0.0.1:$API/health" >/dev/null 2>&1 && break
  sleep 1
done
curl -fsS "http://127.0.0.1:$API/health" >/dev/null
python3 - <<'PY'
import base64
png = base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=')
open('/tmp/zetruv-media-valid.png','wb').write(png)
open('/tmp/zetruv-media-fake.png','wb').write(b'not really an image')
open('/tmp/zetruv-media-large.bin','wb').write(b'\x89PNG\r\n\x1a\n' + b'x' * 70000)
PY

echo '=== CMS AUTH REQUIRED ==='
CODE=$(curl -sS -o /tmp/zetruv-media-unauth.json -w '%{http_code}' -X POST "http://127.0.0.1:$API/api/v1/cms/media" -F "file=@/tmp/zetruv-media-valid.png;type=image/png")
[[ "$CODE" == 401 ]]

curl -fsS -X POST "http://127.0.0.1:$API/api/v1/cms/auth/login"   -H 'Content-Type: application/json'   -d '{"email":"media-admin@zetruv.test","password":"media-smoke-password"}'   > /tmp/zetruv-media-login.json
TOKEN=$(python3 -c 'import json; print(json.load(open("/tmp/zetruv-media-login.json"))["accessToken"])')

echo '=== VALID PNG UPLOAD ==='
CODE=$(curl -sS -o /tmp/zetruv-media-upload.json -w '%{http_code}' -X POST "http://127.0.0.1:$API/api/v1/cms/media"   -H "Authorization: Bearer $TOKEN"   -F "file=@/tmp/zetruv-media-valid.png;type=image/png")
[[ "$CODE" == 201 ]]
ASSET_ID=$(python3 -c 'import json; x=json.load(open("/tmp/zetruv-media-upload.json")); assert x["contentType"]=="image/png"; assert x["sizeBytes"]>0; print(x["id"])')
MEDIA_URL=$(python3 -c 'import json; print(json.load(open("/tmp/zetruv-media-upload.json"))["url"])')
[[ "$MEDIA_URL" == http://127.0.0.1:$API/media/* ]]
echo '=== PUBLIC IMMUTABLE MEDIA ==='
curl -fsS -D /tmp/zetruv-media-public.headers "$MEDIA_URL" -o /tmp/zetruv-media-downloaded.png
cmp /tmp/zetruv-media-valid.png /tmp/zetruv-media-downloaded.png
grep -qi '^Content-Type: image/png' /tmp/zetruv-media-public.headers
grep -qi '^Cache-Control: public,max-age=31536000,immutable' /tmp/zetruv-media-public.headers
grep -qi '^X-Content-Type-Options: nosniff' /tmp/zetruv-media-public.headers

echo '=== MEDIA LIST ==='
curl -fsS "http://127.0.0.1:$API/api/v1/cms/media" -H "Authorization: Bearer $TOKEN" > /tmp/zetruv-media-list.json
python3 - "$ASSET_ID" <<'PY'
import json,sys
items=json.load(open('/tmp/zetruv-media-list.json'))
assert len(items)==1
assert items[0]['id']==sys.argv[1]
assert items[0]['contentType']=='image/png'
PY

echo '=== FAKE IMAGE REJECTED ==='
CODE=$(curl -sS -o /tmp/zetruv-media-fake.json -w '%{http_code}' -X POST "http://127.0.0.1:$API/api/v1/cms/media"   -H "Authorization: Bearer $TOKEN"   -F "file=@/tmp/zetruv-media-fake.png;type=image/png")
[[ "$CODE" == 415 ]]

echo '=== OVERSIZE REJECTED ==='
CODE=$(curl -sS -o /tmp/zetruv-media-large.json -w '%{http_code}' -X POST "http://127.0.0.1:$API/api/v1/cms/media"   -H "Authorization: Bearer $TOKEN"   -F "file=@/tmp/zetruv-media-large.bin;type=image/png")
[[ "$CODE" == 413 ]]
echo '=== DELETE ==='
CODE=$(curl -sS -o /dev/null -w '%{http_code}' -X DELETE "http://127.0.0.1:$API/api/v1/cms/media/$ASSET_ID" -H "Authorization: Bearer $TOKEN")
[[ "$CODE" == 204 ]]
CODE=$(curl -sS -o /dev/null -w '%{http_code}' "$MEDIA_URL")
[[ "$CODE" == 404 ]]
CODE=$(curl -sS -o /dev/null -w '%{http_code}' -X DELETE "http://127.0.0.1:$API/api/v1/cms/media/$ASSET_ID" -H "Authorization: Bearer $TOKEN")
[[ "$CODE" == 204 ]]

curl -fsS "http://127.0.0.1:$API/api/v1/cms/media" -H "Authorization: Bearer $TOKEN" > /tmp/zetruv-media-list-after.json
python3 - <<'PY'
import json
assert json.load(open('/tmp/zetruv-media-list-after.json')) == []
PY

DB_STATE=$(docker exec "$C" psql -At -F '|' -U zetruv -d "$DB" -c 'SELECT "Provider","ContentType","SizeBytes","DeletedAt" IS NOT NULL FROM media_assets;')
echo "DB_STATE=$DB_STATE"
[[ "$DB_STATE" == local\|image/png\|*\|t ]]

echo 'PASS: authenticated media upload + signature validation + public immutable serving + listing + delete'
