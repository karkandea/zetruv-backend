#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
TMP=$(mktemp -d)
C="zetruv-pg-storefront-$$"
PID=""
cleanup() {
  status=$?
  if [[ "$status" != 0 ]]; then echo "--- API ERROR LOG ---" >&2; tail -85 "$TMP/api.log" >&2 || true; fi
  [[ -n "$PID" ]] && kill "$PID" >/dev/null 2>&1 || true
  docker rm -f "$C" >/dev/null 2>&1 || true
  rm -rf "$TMP"
}
trap cleanup EXIT
dotnet build src/Zetruv.Api/Zetruv.Api.csproj -c Release --nologo >/dev/null
docker run -d --name "$C" -e POSTGRES_USER=zetruv -e POSTGRES_PASSWORD=zetruvtest \
  -e POSTGRES_DB=zetruv_storefront_test -p 127.0.0.1::5432 postgres:17-alpine >/dev/null
until docker exec "$C" pg_isready -U zetruv -d zetruv_storefront_test >/dev/null 2>&1; do sleep 1; done
DB_PORT=$(docker port "$C" 5432/tcp | awk -F: 'NR==1 {print $NF}')
API_PORT=$(python3 -c 'import socket; s=socket.socket();s.bind(("127.0.0.1",0));print(s.getsockname()[1]);s.close()')
export ConnectionStrings__Postgres="Host=127.0.0.1;Port=$DB_PORT;Database=zetruv_storefront_test;Username=zetruv;Password=zetruvtest"
export Jwt__Key='0123456789abcdef0123456789abcdef'
export CmsAdmin__Email='storefront-admin@zetruv.test'
export CmsAdmin__Password='StorefrontAdmin123!'
export CustomerEmail__Provider=capture
export CustomerEmail__CaptureDirectory="$TMP/emails"
export CustomerAuth__FrontendBaseUrl=https://dev.zetruv.com
export Payments__Provider=mock
export Payments__Mock__WebhookSecret=smoke-secret
export Shipping__Provider=mock
export GameAccountValidation__Provider=mock
export ASPNETCORE_ENVIRONMENT=Development
export TEST_API="http://127.0.0.1:$API_PORT"
export TEST_CONTAINER="$C"
export TEST_MAIL="$TMP/emails"
dotnet run --project src/Zetruv.Api/Zetruv.Api.csproj --configuration Release \
  --no-build --urls="$TEST_API" > "$TMP/api.log" 2>&1 & PID=$!
ready=false
for _ in $(seq 1 50); do
 if curl -fsS "$TEST_API/health" >/dev/null 2>&1; then ready=true; break; fi
 if ! kill -0 "$PID" 2>/dev/null; then break; fi
 sleep 1
done
if [[ "$ready" != true ]]; then tail -65 "$TMP/api.log" >&2; exit 1; fi
python3 - <<'PY'
import glob, html, json, os, re, subprocess, urllib.error, urllib.parse, urllib.request
from pathlib import Path
BASE=os.environ['TEST_API']
MAIL=Path(os.environ['TEST_MAIL'])
C=os.environ['TEST_CONTAINER']
def api(method,path,payload=None,token=None):
    headers={'Content-Type':'application/json'}
    if token: headers['Authorization']='Bearer '+token
    req=urllib.request.Request(BASE+path,data=json.dumps(payload).encode() if payload is not None else None,headers=headers,method=method)
    try:
        with urllib.request.urlopen(req,timeout=15) as r:
            body=r.read()
            return r.status,(json.loads(body) if body else {})
    except urllib.error.HTTPError as exc:
        body=exc.read()
        return exc.code,(json.loads(body) if body else {})
def check(res,status):
    assert res[0]==status,(status,res)
    return res[1]
def sql(value):
    subprocess.run(['docker','exec',C,'psql','-v','ON_ERROR_STOP=1','-U','zetruv','-d','zetruv_storefront_test','-c',value],check=True,stdout=subprocess.DEVNULL)
def signup(email,name):
    before=set(MAIL.glob('*.json'))
    check(api('POST','/api/v1/auth/register',{'name':name,'email':email,'password':'Start1234'}),202)
    new=set(MAIL.glob('*.json'))-before
    assert len(new)==1
    msg=json.loads(next(iter(new)).read_text())
    token=urllib.parse.parse_qs(urllib.parse.urlsplit(html.unescape(re.search('href="([^"]+)"',msg['Html']).group(1))).query)['token'][0]
    return check(api('POST','/api/v1/auth/verify-email',{'token':token}),200)['accessToken']
admin=check(api('POST','/api/v1/cms/auth/login',{'email':'storefront-admin@zetruv.test','password':'StorefrontAdmin123!'}),200)['accessToken']
first=signup('first@zetruv.test','Alpha Storefront')
second=signup('second@zetruv.test','Beta Storefront')
check(api('GET','/api/v1/me/cart'),401)
check(api('GET','/api/v1/me/recent-purchases'),401)
check(api('GET','/api/v1/me/cart',token=admin),401)
assert check(api('GET','/api/v1/homepage'),200)['recentlyPurchased']==[]
# Hero: total capacity counts inactive records, and invalid URLs cannot be stored.
hero={'title':'Test','subtitle':'Test subtitle','imageUrl':'https://example.test/hero.png','primaryCtaLabel':'Shop','primaryCtaUrl':'/search','secondaryCtaLabel':None,'secondaryCtaUrl':None,'isActive':False,'sortOrder':0,'startsAt':None,'endsAt':None}
check(api('POST','/api/v1/cms/homepage/heroes',hero),401)
check(api('POST','/api/v1/cms/homepage/heroes',{**hero,'primaryCtaUrl':'javascript:alert(1)'},admin),400)
for i in range(10):
    check(api('POST','/api/v1/cms/homepage/heroes',{**hero,'title':str(i),'sortOrder':i},admin),201)
assert check(api('POST','/api/v1/cms/homepage/heroes',hero,admin),409)['code']=='HERO_LIMIT_REACHED'
assert check(api('GET','/api/v1/homepage'),200)['heroes']==[]
# CMS catalog fixture: seeded category Joki, actual paid sales and verified reviews.
cats=check(api('GET','/api/v1/cms/catalog/categories',token=admin),200)
cat=next(x for x in cats if x['kind']=='Joki')
product=check(api('POST','/api/v1/cms/catalog/products',{
    'categoryId':cat['id'],'gameId':None,'name':'Storefront Joki',
    'slug':'storefront-joki','shortDescription':'Real test','description':'Test service',
    'thumbnailUrl':'https://example.test/item.png','kind':'Joki',
    'fulfillmentMethod':'MANUAL','requiresGameAccountValidation':False,
    'isActive':True,'isFeatured':True,'sortOrder':1},admin),201)
pid=product if isinstance(product,str) else product['id']
variant=check(api('POST',f'/api/v1/cms/catalog/products/{pid}/variants',{
    'name':'Service A','sku':'STOREFRONT-JOKI-A','price':100000,
    'compareAtPrice':None,'stockQuantity':12,'weightGrams':None,
    'isActive':True,'sortOrder':1},admin),201)
vid=variant if isinstance(variant,str) else variant['id']
check(api('PUT',f'/api/v1/me/cart/items/{vid}',{'productVariantId':vid,'quantity':2},first),200)
check(api('PUT',f'/api/v1/me/cart/items/{vid}',{'productVariantId':vid,'quantity':1},second),200)
assert len(check(api('GET','/api/v1/me/cart',token=first),200)['items'])==1
assert check(api('GET','/api/v1/me/cart',token=second),200)['items'][0]['quantity']==1
check(api('PUT',f'/api/v1/me/cart/items/{vid}',{'productVariantId':vid,'quantity':99},first),409)
check(api('POST','/api/v1/checkout/orders',{'customerEmail':'second@zetruv.test','items':[{'productVariantId':vid,'quantity':1}]},first),400)
order=check(api('POST','/api/v1/checkout/orders',{'items':[{'productVariantId':vid,'quantity':1}]},first),201)
oid=order['id']
other=check(api('POST','/api/v1/checkout/orders',{'items':[{'productVariantId':vid,'quantity':1}]},second),201)
oid2=other['id']
guest=check(api('POST','/api/v1/checkout/orders',{'customerEmail':'guest@zetruv.test','items':[{'productVariantId':vid,'quantity':1}]}),201)
# Isolated DB fixture simulates payment+fulfillment completion to exercise aggregated read models.
sql('UPDATE orders SET "PaymentStatus"=\'Paid\', "Status"=\'Completed\', "PaidAt"=now(), "CompletedAt"=now() WHERE "Id" IN (\''+oid+'\',\''+oid2+'\',\''+guest['id']+'\');')
first_history=check(api('GET','/api/v1/me/recent-purchases',token=first),200)
second_history=check(api('GET','/api/v1/me/recent-purchases',token=second),200)
assert len(first_history)==1 and len(second_history)==1
check(api('GET','/api/v1/homepage/personal'),401)
assert len(check(api('GET','/api/v1/homepage/personal',token=first),200)['recentlyPurchased'])==1
assert len(check(api('GET','/api/v1/homepage/personal',token=second),200)['recentlyPurchased'])==1
assert check(api('GET','/api/v1/homepage'),200)['recentlyPurchased']==[]
board=check(api('GET','/api/v1/leaderboard'),200)
assert len(board['weekly'])==2 and board['weekly'][0]['amount']==100000
assert all('@' not in entry['displayName'] for entry in board['weekly'])
assert len(board['monthly'])==2 and board['podium']==board['weekly'][:3]
assert check(api('GET','/api/v1/catalog/products/storefront-joki'),200)['soldQuantity']==3
assert check(api('GET','/api/v1/catalog/products/storefront-joki'),200)['rating'] is None
check(api('POST','/api/v1/me/reviews',{'orderItemId':str('00000000-0000-0000-0000-000000000000'),'rating':5},first),400)
sql('UPDATE order_items SET "ProductId"=\''+pid+'\' WHERE "OrderId"=\''+oid+'\';')
# Query the actual paid order item id from the customer-visible list (public order tracking omits IDs).
raw=subprocess.check_output(['docker','exec',C,'psql','-At','-U','zetruv','-d','zetruv_storefront_test','-c','SELECT "Id" FROM order_items WHERE "OrderId"=\''+oid+'\' LIMIT 1;']).decode().strip()
check(api('POST','/api/v1/me/reviews',{'orderItemId':raw,'rating':5,'comment':'Great'},first),202)
assert check(api('GET','/api/v1/catalog/products/storefront-joki'),200)['rating'] is None
review=check(api('GET','/api/v1/cms/catalog/reviews',token=admin),200)[0]
check(api('PUT',f"/api/v1/cms/catalog/reviews/{review['id']}/approval",{'isApproved':True},admin),204)
detail=check(api('GET','/api/v1/catalog/products/storefront-joki'),200)
assert detail['rating']==5 and detail['reviewCount']==1 and detail['soldQuantity']==3
check(api('DELETE',f'/api/v1/me/cart/items/{vid}',token=first),204)
assert check(api('GET','/api/v1/me/cart',token=first),200)['items']==[]
search=check(api('GET','/api/v1/search/suggestions?q=storefront'),200)
assert search['totalMatches']>=1 and 'Joki' in search['groups']
assert check(api('GET','/api/v1/search/suggestions?q=st'),200)['totalMatches']==0
# Product-specific game account teaser content is structured and CMS-managed.
account_cat=next(x for x in cats if x['kind']=='GameAccount')
account=check(api('POST','/api/v1/cms/catalog/products',{
    'categoryId':account_cat['id'],'gameId':None,'name':'Real Account',
    'slug':'real-account','shortDescription':'Real account','description':'Account details',
    'thumbnailUrl':'https://example.test/account.png','kind':'GameAccount',
    'fulfillmentMethod':'MANUAL','requiresGameAccountValidation':False,
    'isActive':True,'isFeatured':True,'sortOrder':1},admin),201)
aid=account if isinstance(account,str) else account['id']
check(api('POST',f'/api/v1/cms/catalog/products/{aid}/variants',{
    'name':'Invalid unlimited account','sku':'STOREFRONT-ACCOUNT-B',
    'price':1850000,'compareAtPrice':None,'stockQuantity':None,
    'weightGrams':None,'isActive':True,'sortOrder':2},admin),400)
check(api('POST',f'/api/v1/cms/catalog/products/{aid}/variants',{
    'name':'Unique account','sku':'STOREFRONT-ACCOUNT-A','price':1850000,
    'compareAtPrice':None,'stockQuantity':1,'weightGrams':None,
    'isActive':True,'sortOrder':1},admin),201)
check(api('PUT',f'/api/v1/cms/catalog/products/{aid}/game-account-details',{
    'rank':'Mythic','skinCount':119,'region':'Indonesia/Jawa Barat',
    'level':61,'additionalInfo':'Verified inventory'},admin),200)
account_detail=check(api('GET','/api/v1/catalog/products/real-account'),200)
assert account_detail['accountDetails']['rank']=='Mythic'
assert account_detail['accountDetails']['skinCount']==119
assert account_detail['accountDetails']['level']==61
homepage=check(api('GET','/api/v1/homepage'),200)
assert any(x['accountDetails'] and x['accountDetails']['region']=='Indonesia/Jawa Barat' for x in homepage['gameAccounts'])
# Only the owner of an actual paid item can review it, and refund removes sold quantity.
check(api('POST','/api/v1/me/reviews',{'orderItemId':raw,'rating':1},second),400)
sql('UPDATE orders SET "PaymentStatus"=\'Refunded\' WHERE "Id"=\''+guest['id']+'\';')
assert check(api('GET','/api/v1/catalog/products/storefront-joki'),200)['soldQuantity']==2
# Hero schedule is UTC-aware; disabled drafts never leak.
existing=check(api('GET','/api/v1/cms/homepage/heroes',token=admin),200)
first_hero=existing[0]
check(api('PUT','/api/v1/cms/homepage/heroes/'+first_hero['id'],{
    **hero,'title':'Future banner','isActive':True,'startsAt':'2099-01-01T00:00:00Z',
    'endsAt':'2099-01-02T00:00:00Z'},admin),200)
assert check(api('GET','/api/v1/homepage'),200)['heroes']==[]
print('PASS: 10 total heroes, CTA safety, personal purchase isolation, customer cart, checkout owner, spend leaderboard, paid sales, verified moderated rating, search, account details, refunds, hero scheduling')
PY
