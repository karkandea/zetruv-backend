#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

TMP=$(mktemp -d)
C="zetruv-pg-game-voucher-$$"
PID=""
cleanup() {
  status=$?
  if [[ "$status" != 0 ]]; then
    echo "--- API ERROR LOG ---" >&2
    tail -90 "$TMP/api.log" >&2 || true
  fi
  [[ -n "$PID" ]] && kill "$PID" >/dev/null 2>&1 || true
  docker rm -fv "$C" >/dev/null 2>&1 || true
  rm -rf "$TMP"
}
trap cleanup EXIT

dotnet build src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --nologo >/dev/null
docker run -d --name "$C" -e POSTGRES_USER=zetruv -e POSTGRES_PASSWORD=zetruvtest   -e POSTGRES_DB=zetruv_game_voucher_test -p 127.0.0.1::5432 postgres:17-alpine >/dev/null
until docker exec "$C" pg_isready -U zetruv -d zetruv_game_voucher_test >/dev/null 2>&1; do sleep 1; done
DB_PORT=$(docker port "$C" 5432/tcp | awk -F: 'NR==1 {print $NF}')
API_PORT=$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1]); s.close()')
export ConnectionStrings__Postgres="Host=127.0.0.1;Port=$DB_PORT;Database=zetruv_game_voucher_test;Username=zetruv;Password=zetruvtest"
export Jwt__Key='0123456789abcdef0123456789abcdef'
export CmsAdmin__Email='game-voucher-admin@zetruv.test'
export CmsAdmin__Password='GameVoucherAdmin123!'
export CustomerEmail__Provider=capture
export CustomerEmail__CaptureDirectory="$TMP/emails"
export CustomerAuth__FrontendBaseUrl=https://dev.zetruv.com
export Payments__Provider=mock
export Payments__Mock__WebhookSecret=smoke-secret
export Shipping__Provider=mock
export GameAccountValidation__Provider=mock
export ManualLogin__EncryptionKey
ManualLogin__EncryptionKey=$(printf '%s' '0123456789abcdef0123456789abcdef' | openssl base64 -A)
export ASPNETCORE_ENVIRONMENT=Development
export TEST_API="http://127.0.0.1:$API_PORT"
export TEST_CONTAINER="$C"
export TEST_MAIL="$TMP/emails"

dotnet run --project src/Zetruv.Api/Zetruv.Api.csproj --configuration Release   --no-build --urls="$TEST_API" > "$TMP/api.log" 2>&1 & PID=$!
ready=false
for _ in $(seq 1 50); do
  if curl -fsS "$TEST_API/health" >/dev/null 2>&1; then ready=true; break; fi
  if ! kill -0 "$PID" 2>/dev/null; then break; fi
  sleep 1
done
[[ "$ready" == true ]]
python3 - <<'PY'
import hashlib, hmac, html, json, os, re, subprocess, urllib.error, urllib.parse, urllib.request, uuid
from pathlib import Path

BASE=os.environ['TEST_API']
MAIL=Path(os.environ['TEST_MAIL'])
C=os.environ['TEST_CONTAINER']

def api(method,path,payload=None,token=None,headers=None):
    h={'Content-Type':'application/json'}
    if token: h['Authorization']='Bearer '+token
    if headers: h.update(headers)
    req=urllib.request.Request(BASE+path,
        data=json.dumps(payload).encode() if payload is not None else None,
        headers=h,method=method)
    try:
        with urllib.request.urlopen(req,timeout=15) as r:
            body=r.read()
            return r.status,(json.loads(body) if body else {})
    except urllib.error.HTTPError as exc:
        body=exc.read()
        return exc.code,(json.loads(body) if body else {})

def check(result,status):
    assert result[0]==status,(status,result)
    return result[1]

def scalar(sql):
    return subprocess.check_output([
        'docker','exec',C,'psql','-At','-U','zetruv',
        '-d','zetruv_game_voucher_test','-c',sql
    ],text=True).strip()

def sql(statement):
    subprocess.run([
        'docker','exec',C,'psql','-v','ON_ERROR_STOP=1','-U','zetruv',
        '-d','zetruv_game_voucher_test','-c',statement
    ],check=True,stdout=subprocess.DEVNULL)

def signup(email,name):
    before=set(MAIL.glob('*.json'))
    check(api('POST','/api/v1/auth/register',
        {'name':name,'email':email,'password':'Start1234'}),202)
    fresh=set(MAIL.glob('*.json'))-before
    assert len(fresh)==1
    message=json.loads(next(iter(fresh)).read_text())
    link=re.search(r'href="([^"]+)"',message['Html']).group(1)
    token=urllib.parse.parse_qs(
        urllib.parse.urlsplit(html.unescape(link)).query)['token'][0]
    return check(api('POST','/api/v1/auth/verify-email',{'token':token}),200)['accessToken']

def pay_and_webhook(order):
    payment=check(api('POST',f"/api/v1/checkout/orders/{order['id']}/payment",
        headers={'X-Order-Access-Token':order['orderAccessToken']}),200)
    body={'providerReference':payment['providerReference'],'status':'Paid',
          'amount':payment['amount'],'currency':payment['currency']}
    raw=json.dumps(body)
    sig=hmac.new(b'smoke-secret',raw.encode(),hashlib.sha256).hexdigest()
    check(api('POST','/api/v1/payments/webhooks/mock',body,
        headers={'X-Mock-Signature':sig}),200)

admin=check(api('POST','/api/v1/cms/auth/login',
    {'email':'game-voucher-admin@zetruv.test','password':'GameVoucherAdmin123!'}),200)['accessToken']
alice=signup('voucher-alice@zetruv.test','Voucher Alice')
bob=signup('voucher-bob@zetruv.test','Voucher Bob')
categories=check(api('GET','/api/v1/cms/catalog/categories',token=admin),200)
category=next(x for x in categories if x['kind']=='GameVoucher')
product_payload={
    'categoryId':category['id'],'gameId':None,'name':'Steam Wallet Codes',
    'slug':'steam-wallet-codes','shortDescription':'Encrypted redeem codes',
    'description':'Smoke Game Voucher','thumbnailUrl':'https://example.test/voucher.png',
    'kind':'GameVoucher','fulfillmentMethod':'MANUAL',
    'requiresGameAccountValidation':False,'isActive':True,'isFeatured':False,'sortOrder':0}
product=check(api('POST','/api/v1/cms/catalog/products',product_payload,admin),201)
pid=product if isinstance(product,str) else product['id']

bad_variant={
    'name':'100K','sku':'GV-SMOKE-100K','groupName':'Wallet','price':100000,
    'compareAtPrice':None,'stockQuantity':1,'weightGrams':None,'isActive':True,'sortOrder':0}
check(api('POST',f'/api/v1/cms/catalog/products/{pid}/variants',bad_variant,admin),400)
variant_payload={**bad_variant,'stockQuantity':0}
variant=check(api('POST',f'/api/v1/cms/catalog/products/{pid}/variants',variant_payload,admin),201)
vid=variant if isinstance(variant,str) else variant['id']
pdp=check(api('GET','/api/v1/catalog/products/steam-wallet-codes'),200)
assert pdp['isAvailable'] is False
assert next(x for x in pdp['variants'] if x['id']==vid)['stockQuantity']==0
assert next(x for x in pdp['variants'] if x['id']==vid)['isAvailable'] is False

inventory_path=f'/api/v1/cms/catalog/products/{pid}/variants/{vid}/voucher-codes'
imported=check(api('POST',inventory_path,
    {'codes':['STEAM-AAA-001','STEAM-BBB-002','STEAM-CCC-003','STEAM-BBB-002']},admin),200)
assert imported=={'imported':3,'skippedDuplicates':1,'sellableStock':3},imported
inventory=check(api('GET',inventory_path,token=admin),200)
assert inventory['sellableStock']==3 and inventory['availableCodeCount']==3
pdp=check(api('GET','/api/v1/catalog/products/steam-wallet-codes'),200)
public_variant=next(x for x in pdp['variants'] if x['id']==vid)
assert pdp['isAvailable'] is True and public_variant['isAvailable'] is True
assert public_variant['stockQuantity']==3
assert 'STEAM-' not in json.dumps(inventory)
# Plain voucher values never appear in DB ciphertext/hash columns.
for raw in ['STEAM-AAA-001','STEAM-BBB-002','STEAM-CCC-003']:
    leaked=scalar("SELECT COUNT(*) FROM game_voucher_codes WHERE COALESCE(\"EncryptedCode\",'') LIKE '%"+raw+"%' OR \"CodeHash\" LIKE '%"+raw+"%';")
    assert leaked=='0',raw

# Stock cannot be edited around the encrypted inventory.
check(api('PUT',f'/api/v1/cms/catalog/products/{pid}/variants/{vid}',
    {**variant_payload,'stockQuantity':99},admin),400)

order_payload={'customerPhone':'+6281234567890',
    'items':[{'productVariantId':vid,'quantity':2}]}

# Redeem codes are owner-only after payment, so guest checkout must fail before money can be taken.
guest_status,guest_body=api('POST','/api/v1/checkout/orders',order_payload)
assert guest_status==400,(guest_status,guest_body)
assert 'verified customer account' in guest_body.get('message','')

order=check(api('POST','/api/v1/checkout/orders',order_payload,alice,
    {'Idempotency-Key':str(uuid.uuid4())}),201)
# Checkout itself is non-reserving; inventory is reserved when a payment session is initiated.
assert scalar(f'SELECT "StockQuantity" FROM product_variants WHERE "Id"=\'{vid}\';')=='3'
item_id=scalar(f'SELECT "Id" FROM order_items WHERE "OrderId"=\'{order["id"]}\';')

# Codes are not revealable before payment and never leak through normal order payload.
check(api('GET',f'/api/v1/me/orders/{order["id"]}/items/{item_id}/voucher-codes',token=alice),409)
assert 'STEAM-' not in json.dumps(order)
pay_and_webhook(order)

state=scalar(f'''SELECT o."PaymentStatus"||'|'||o."Status"||'|'||oi."FulfillmentStatus"
FROM orders o JOIN order_items oi ON oi."OrderId"=o."Id"
WHERE o."Id"='{order["id"]}';''')
assert state=='Paid|Completed|Completed',state
assigned=scalar(f'SELECT COUNT(*) FROM game_voucher_codes WHERE "OrderItemId"=\'{item_id}\' AND "Status"=\'Assigned\';')
assert assigned=='2'
assert scalar(f'SELECT "StockQuantity" FROM product_variants WHERE "Id"=\'{vid}\';')=='1'
reveal=check(api('GET',f'/api/v1/me/orders/{order["id"]}/items/{item_id}/voucher-codes',token=alice),200)
assert set(reveal['codes']).issubset({'STEAM-AAA-001','STEAM-BBB-002','STEAM-CCC-003'})
assert len(reveal['codes'])==2 and reveal['revealCount']==1
check(api('GET',f'/api/v1/me/orders/{order["id"]}/items/{item_id}/voucher-codes',token=bob),404)
reveal2=check(api('GET',f'/api/v1/me/orders/{order["id"]}/items/{item_id}/voucher-codes',token=alice),200)
assert reveal2['revealCount']==2

inventory=check(api('GET',inventory_path,token=admin),200)
available_id=next(x['id'] for x in inventory['items'] if x['status']=='Available')
assigned_id=next(x['id'] for x in inventory['items'] if x['status']=='Assigned')
check(api('DELETE',inventory_path+'/'+assigned_id,token=admin),409)
check(api('DELETE',inventory_path+'/'+available_id,token=admin),204)
inventory=check(api('GET',inventory_path,token=admin),200)
assert inventory['sellableStock']==0 and inventory['assignedCodeCount']==2 and inventory['revokedCodeCount']==1
pdp=check(api('GET','/api/v1/catalog/products/steam-wallet-codes'),200)
assert pdp['isAvailable'] is False
assert next(x for x in pdp['variants'] if x['id']==vid)['isAvailable'] is False

# An Available row can still be committed to a pending payment reservation.
# CMS revoke must follow sellable stock, not merely Available row count.
guard_variant_payload={**variant_payload,'name':'Guard','sku':'GV-SMOKE-GUARD','price':25000}
guard=check(api('POST',f'/api/v1/cms/catalog/products/{pid}/variants',guard_variant_payload,admin),201)
gvid=guard if isinstance(guard,str) else guard['id']
guard_path=f'/api/v1/cms/catalog/products/{pid}/variants/{gvid}/voucher-codes'
check(api('POST',guard_path,{'codes':['GUARD-CODE-001']},admin),200)
guard_order=check(api('POST','/api/v1/checkout/orders',
    {'customerPhone':'+6281234567890','items':[{'productVariantId':gvid,'quantity':1}]},
    bob,{'Idempotency-Key':str(uuid.uuid4())}),201)
check(api('POST',f"/api/v1/checkout/orders/{guard_order['id']}/payment",
    headers={'X-Order-Access-Token':guard_order['orderAccessToken']}),200)
guard_inventory=check(api('GET',guard_path,token=admin),200)
assert guard_inventory['sellableStock']==0 and guard_inventory['availableCodeCount']==1
guard_code=guard_inventory['items'][0]['id']
check(api('DELETE',guard_path+'/'+guard_code,token=admin),409)
check(api('PUT',f"/api/v1/cms/orders/{guard_order['id']}/status",
    {'status':'Cancelled'},admin),204)
assert check(api('GET',guard_path,token=admin),200)['sellableStock']==1
check(api('DELETE',guard_path+'/'+guard_code,token=admin),204)

# Repair path: a paid order fails closed if encrypted inventory is inconsistent.
repair_variant_payload={**variant_payload,'name':'50K','sku':'GV-SMOKE-50K','price':50000}
repair=check(api('POST',f'/api/v1/cms/catalog/products/{pid}/variants',repair_variant_payload,admin),201)
rvid=repair if isinstance(repair,str) else repair['id']
repair_path=f'/api/v1/cms/catalog/products/{pid}/variants/{rvid}/voucher-codes'
check(api('POST',repair_path,{'codes':['REPAIR-OLD-001']},admin),200)
repair_order=check(api('POST','/api/v1/checkout/orders',
    {'customerPhone':'+6281234567890','items':[{'productVariantId':rvid,'quantity':1}]},
    alice,{'Idempotency-Key':str(uuid.uuid4())}),201)
repair_item=scalar(f'SELECT "Id" FROM order_items WHERE "OrderId"=\'{repair_order["id"]}\';')
sql(f'''UPDATE game_voucher_codes
SET "Status"='Revoked',"EncryptedCode"=NULL
WHERE "ProductVariantId"='{rvid}' AND "Status"='Available';''')
pay_and_webhook(repair_order)
failed=scalar(f'''SELECT "FulfillmentStatus"||'|'||COALESCE("FulfillmentReference",'')
FROM order_items WHERE "Id"='{repair_item}';''')
assert failed=='Failed|GAME_VOUCHER_CODE_SHORTAGE',failed

repaired=check(api('POST',repair_path,{'codes':['REPAIR-NEW-002']},admin),200)
assert repaired['sellableStock']==1
retry=check(api('POST',
    f'/api/v1/cms/orders/{repair_order["id"]}/items/{repair_item}/game-voucher-assign',
    token=admin),200)
assert retry['assignedCount']==1
assert scalar(f'SELECT "StockQuantity" FROM product_variants WHERE "Id"=\'{rvid}\';')=='0'
reveal_repaired=check(api('GET',
    f'/api/v1/me/orders/{repair_order["id"]}/items/{repair_item}/voucher-codes',
    token=alice),200)
assert reveal_repaired['codes']==['REPAIR-NEW-002']

# Generic manual fulfillment cannot bypass secure Game Voucher assignment.
code,body=api('PUT',
    f'/api/v1/cms/orders/{repair_order["id"]}/items/{repair_item}/fulfillment',
    {'status':'Completed','reference':'FAKE'},admin)
assert code==409,(code,body)

print('PASS: encrypted Game Voucher import, stock source-of-truth, paid auto assignment, owner-only reveal, revoke guard, shortage repair retry')
PY
