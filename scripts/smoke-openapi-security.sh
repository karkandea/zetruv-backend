#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

C=zetruv-pg-openapi-security
DB=zetruv_openapi_security_test
PORT=57444
API=18194
PID=""
cleanup(){ [[ -n "$PID" ]] && kill "$PID" >/dev/null 2>&1 || true; docker rm -f "$C" >/dev/null 2>&1 || true; rm -f /tmp/zetruv-openapi-security*.json /tmp/zetruv-openapi-security*.html; }
trap cleanup EXIT

dotnet build src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --nologo
docker rm -f "$C" >/dev/null 2>&1 || true
docker run -d --name "$C" -e POSTGRES_USER=zetruv -e POSTGRES_PASSWORD=zetruvtest -e POSTGRES_DB="$DB" -p 127.0.0.1:$PORT:5432 postgres:17-alpine >/dev/null
until docker exec "$C" pg_isready -U zetruv -d "$DB" >/dev/null 2>&1; do sleep 1; done

export ConnectionStrings__Postgres="Host=127.0.0.1;Port=$PORT;Database=$DB;Username=zetruv;Password=zetruvtest"
export Jwt__Key='0123456789abcdef0123456789abcdef'
export Payments__Provider=mock
export Payments__Mock__WebhookSecret=smoke-secret
export Shipping__Provider=mock
export GameAccountValidation__Provider=mock
export ASPNETCORE_ENVIRONMENT=Development

dotnet run --project src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --no-build --urls="http://127.0.0.1:$API" >/tmp/zetruv-openapi-security.log 2>&1 & PID=$!
for _ in $(seq 1 40); do curl -fsS "http://127.0.0.1:$API/health" >/dev/null 2>&1 && break; sleep 1; done
curl -fsS "http://127.0.0.1:$API/openapi/v1.json" > /tmp/zetruv-openapi-security.json
curl -fsSL "http://127.0.0.1:$API/scalar/" > /tmp/zetruv-openapi-security.html

python3 - <<'PY'
import json
x=json.load(open('/tmp/zetruv-openapi-security.json'))
scheme=x['components']['securitySchemes']['Bearer']
assert scheme['type']=='http'
assert scheme['scheme']=='bearer'
assert scheme['bearerFormat']=='JWT'

def bearer(method,path):
    security=x['paths'][path][method].get('security') or []
    return any('Bearer' in entry for entry in security)

assert bearer('get','/api/v1/cms/catalog/products')
assert bearer('get','/api/v1/cms/orders')
assert bearer('get','/api/v1/cms/fulfillment/queue')
assert bearer('post','/api/v1/cms/media')
assert not bearer('post','/api/v1/cms/auth/login')
assert not bearer('get','/api/v1/catalog/products')
assert not bearer('get','/api/v1/homepage')
print('PASS: OpenAPI Bearer scheme is scoped to authorized operations')
PY

grep -q 'Zetruv API' /tmp/zetruv-openapi-security.html
echo 'PASS: Scalar renders the secured OpenAPI document'
