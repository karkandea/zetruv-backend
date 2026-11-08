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
  rm -f /tmp/zetruv-banner-*.json /tmp/zetruv-banner-*.png /tmp/zetruv-banner-*.webp
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

# The dedicated banner endpoint rejects tiny images: create a genuine 640x320 PNG.
import struct, zlib
def chunk(kind, data):
    return struct.pack('>I', len(data)) + kind + data + struct.pack('>I', zlib.crc32(kind+data) & 0xffffffff)
width, height = 640, 320
scan = b''.join(b'\0' + (bytes([24, 68, 164]) * width) for _ in range(height))
banner = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>2I5B', width, height, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(scan, 9)) + chunk(b'IEND', b'')
open('/tmp/zetruv-banner-valid.png','wb').write(banner)
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

echo '=== BANNER CAPABILITIES AND SERVER-SIDE WEBP OPTIMIZATION ==='
curl -fsS "http://127.0.0.1:$API/api/v1/cms/media/banner/capabilities" -H "Authorization: Bearer $TOKEN" > /tmp/zetruv-banner-capabilities.json
python3 - <<'PY'
import json
data=json.load(open('/tmp/zetruv-banner-capabilities.json'))
assert data['enabled'] and data['maxUploadBytes']==3*1024*1024
assert data['maxBanners']==50 and data['storageQuotaBytes']==100*1024*1024
PY
CODE=$(curl -sS -o /tmp/zetruv-banner-upload.json -w '%{http_code}' -X POST "http://127.0.0.1:$API/api/v1/cms/media/banner" -H "Authorization: Bearer $TOKEN" -F "file=@/tmp/zetruv-banner-valid.png;type=image/png")
[[ "$CODE" == 201 ]] || { echo "Banner upload failed: $CODE"; sed -n '1,12p' /tmp/zetruv-banner-upload.json; exit 1; }
BANNER_ID=$(python3 -c 'import json; x=json.load(open("/tmp/zetruv-banner-upload.json")); assert x["contentType"]=="image/webp"; assert x["sizeBytes"]<=500*1024; print(x["id"])')
BANNER_URL=$(python3 -c 'import json; print(json.load(open("/tmp/zetruv-banner-upload.json"))["url"])')
[[ "$BANNER_URL" == http://127.0.0.1:$API/media/banners/* ]]
curl -fsS "$BANNER_URL" -o /tmp/zetruv-banner-download.webp
python3 - <<'PY'
data=open('/tmp/zetruv-banner-download.webp','rb').read()
assert data[:4]==b'RIFF' and data[8:12]==b'WEBP'
PY

echo '=== SAVED BANNER IMAGE CANNOT BE DELETED WHILE REFERENCED ==='
CODE=$(curl -sS -o /tmp/zetruv-banner-invalid.json -w '%{http_code}' -X POST "http://127.0.0.1:$API/api/v1/cms/homepage/heroes" -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"title":"Invalid banner","subtitle":"Must not publish","imageUrl":"asofdjtogjesf","isActive":true,"sortOrder":200}')
[[ "$CODE" == 400 ]]
python3 - <<'PY'
import json
image=json.load(open('/tmp/zetruv-banner-upload.json'))['url']
with open('/tmp/zetruv-banner-hero.json','w') as f:
    json.dump({'title':'Banner smoke', 'subtitle':'Safe image', 'imageUrl':image, 'isActive':True, 'sortOrder':10},f)
PY
CODE=$(curl -sS -o /tmp/zetruv-banner-hero-result.json -w '%{http_code}' -X POST "http://127.0.0.1:$API/api/v1/cms/homepage/heroes" -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' --data-binary @/tmp/zetruv-banner-hero.json)
[[ "$CODE" == 201 ]] || { echo "Banner create failed: $CODE"; exit 1; }
HERO_ID=$(python3 -c 'import json; print(json.load(open("/tmp/zetruv-banner-hero-result.json"))["id"])')
CODE=$(curl -sS -o /tmp/zetruv-banner-protected.json -w '%{http_code}' -X DELETE "http://127.0.0.1:$API/api/v1/cms/media/$BANNER_ID" -H "Authorization: Bearer $TOKEN")
[[ "$CODE" == 409 ]]
CODE=$(curl -sS -o /dev/null -w '%{http_code}' -X DELETE "http://127.0.0.1:$API/api/v1/cms/homepage/heroes/$HERO_ID" -H "Authorization: Bearer $TOKEN")
[[ "$CODE" == 204 ]]
CODE=$(curl -sS -o /dev/null -w '%{http_code}' -X DELETE "http://127.0.0.1:$API/api/v1/cms/media/$BANNER_ID" -H "Authorization: Bearer $TOKEN")
[[ "$CODE" == 204 ]]

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
[[ "$DB_STATE" == *local\|image/webp\|*\|t* ]]

echo 'PASS: authenticated media upload + signature validation + public immutable serving + listing + delete'
