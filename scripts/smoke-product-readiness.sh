#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

C=zetruv-pg-product-readiness
DB=zetruv_readiness_test
PORT=57449
API=18199
PID=""

cleanup(){
  [[ -n "$PID" ]] && kill "$PID" >/dev/null 2>&1 || true
  docker rm -fv "$C" >/dev/null 2>&1 || true
  rm -f /tmp/zetruv-readiness-*
}
trap cleanup EXIT

dotnet build src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --nologo

docker rm -fv "$C" >/dev/null 2>&1 || true
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
export ManualLogin__EncryptionKey=$(printf %s 0123456789abcdef0123456789abcdef | openssl base64 -A)

dotnet run --project src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --no-build   --urls="http://127.0.0.1:$API" >/tmp/zetruv-readiness-app.log 2>&1 & PID=$!
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

BASE="$BASE" CTOK="$CTOK" python3 - <<'PY'
import os,json,urllib.request,urllib.error
base=os.environ['BASE']+'/api/v1/cms'
token=os.environ['CTOK']
def call(path,method='GET',body=None,status=200):
    req=urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,headers={'Authorization':'Bearer '+token,'Content-Type':'application/json'},method=method)
    try:
        with urllib.request.urlopen(req) as r:
            assert r.status==status,(path,r.status,status)
            raw=r.read();return json.loads(raw) if raw else None
    except urllib.error.HTTPError as e:
        raw=e.read();assert e.code==status,(path,e.code,status,raw);return json.loads(raw) if raw else None
def ident(x):return x['id'] if isinstance(x,dict) else x
def checks(id):return call('/catalog/products/'+id+'/readiness')
def checkmap(id):return {x['key']:x for x in checks(id)['checks']}
def publish(id,payload,status=204):return call('/catalog/products/'+id,'PUT',{**payload,'isActive':True},status)
game=ident(call('/catalog/games','POST',dict(name='Readiness QA Game',slug='readiness-qa-game',isActive=True,isPopular=False,sortOrder=0),201))
for kind in ['TopUpGame','TopUpLogin','GameVoucher','Joki','Merchandise','GameAccount','GameItem']:
    slug='ready-'+kind.lower()
    category=ident(call('/catalog/categories','POST',dict(key=slug,name=kind,slug=slug,kind=kind,isActive=True,sortOrder=0),201))
    method='AUTO_ID' if kind=='TopUpGame' else 'MANUAL_LOGIN' if kind=='TopUpLogin' else 'MANUAL'
    draft=dict(categoryId=category,gameId=game,name='QA '+kind,slug=slug,shortDescription='Valid QA description',kind=kind,fulfillmentMethod=method,requiresGameAccountValidation=method=='AUTO_ID',isActive=False,isFeatured=False,sortOrder=0)
    assert call('/catalog/products','POST',{**draft,'isActive':True},409)['code']=='PRODUCT_DRAFT_REQUIRED'
    id=ident(call('/catalog/products','POST',draft,201))
    assert not checks(id)['canPublish'] and not checkmap(id)['variants']['passed']
    assert publish(id,draft,409)['code']=='PRODUCT_NOT_READY'
    variant=ident(call('/catalog/products/'+id+'/variants','POST',dict(name='QA package',sku=slug.upper(),price=10000,stockQuantity=0 if kind=='GameVoucher' else 1,weightGrams=10 if kind=='Merchandise' else None,isActive=True,sortOrder=0),201))
    if kind=='TopUpLogin' or kind=='TopUpGame':
        assert not checkmap(id)['inputs']['passed']
        call('/catalog/products/'+id+'/input-fields','POST',dict(key='account',label='Account',scope='LoginCredential' if kind=='TopUpLogin' else 'AccountValidation',type='Text',isRequired=True,isSensitive=False,maxLength=100,sortOrder=0),201)
    if kind=='TopUpGame':
        assert not checkmap(id)['provider']['passed']
        call('/provider-mappings/games/'+game,'PUT',dict(providerCode='mock',nicknameCheckEnabled=True,isActive=True),204)
        call('/provider-mappings/variants/'+variant,'PUT',dict(providerSku='QA-PACKAGE',isActive=True),204)
    if kind=='GameVoucher':
        assert not checkmap(id)['voucher']['passed']
        call('/catalog/products/'+id+'/variants/'+variant+'/voucher-codes','POST',dict(codes=['QA-VOUCHER-123']),200)
    r=checks(id);assert r['canPublish'],(kind,r)
    assert not next(c for c in r['checks'] if c['key']=='thumbnail')['blocking']
    publish(id,draft)
    assert call('/catalog/products/'+id)['isActive']
    call('/catalog/products/'+id,'PUT',draft,204)
    assert not call('/catalog/products/'+id)['isActive']
    publish(id,draft)
    print('PASS draft, publication guard, readiness and reactivation:',kind)
PY
echo 'PASS: product readiness integration'
