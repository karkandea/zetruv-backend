#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

test_dir=$(mktemp -d)
container="zetruv-game-item-smoke-${test_dir##*.}"
pid=""
cleanup() {
  if [[ -n "$pid" ]]; then kill "$pid" 2>/dev/null || true; wait "$pid" 2>/dev/null || true; fi
  docker rm -fv "$container" >/dev/null 2>&1 || true
  rm -rf "$test_dir"
}
trap cleanup EXIT

dotnet build src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --nologo
docker run -d --name "$container" -e POSTGRES_USER=zetruv -e POSTGRES_PASSWORD=smoke-db \
  -e POSTGRES_DB=zetruv_game_item_test -p 127.0.0.1::5432 postgres:17-alpine >/dev/null
port=$(docker port "$container" 5432/tcp | sed 's/.*://')
for _ in $(seq 1 30); do
  docker exec "$container" pg_isready -U zetruv -d zetruv_game_item_test >/dev/null 2>&1 && break
  sleep 1
done
docker exec "$container" pg_isready -U zetruv -d zetruv_game_item_test >/dev/null

api_port=$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1",0)); print(s.getsockname()[1])')
export GameItemSmokeApi="http://127.0.0.1:$api_port"
export ConnectionStrings__Postgres="Host=127.0.0.1;Port=$port;Database=zetruv_game_item_test;Username=zetruv;Password=smoke-db"
export Jwt__Key='0123456789abcdef0123456789abcdef'
export CmsAdmin__Email='game-item-smoke@zetruv.local'
export CmsAdmin__Password='GameItemSmoke123!'
export Payments__Provider=mock Payments__Mock__WebhookSecret=game-item-test-secret
export ManualLogin__EncryptionKey
ManualLogin__EncryptionKey=$(printf '%s' '0123456789abcdef0123456789abcdef' | openssl base64 -A)
export ASPNETCORE_ENVIRONMENT=Staging
dotnet src/Zetruv.Api/bin/Release/net10.0/Zetruv.Api.dll --urls="$GameItemSmokeApi" > "$test_dir/api.log" 2>&1 & pid=$!
for _ in $(seq 1 45); do
  curl -fsS "$GameItemSmokeApi/health" >/dev/null 2>&1 && break
  sleep 1
done
curl -fsS "$GameItemSmokeApi/health" >/dev/null || { cat "$test_dir/api.log"; exit 1; }

python3 - <<'PY'
import hashlib
import hmac
import json
import os
from urllib.error import HTTPError
from urllib.request import Request, urlopen

base = os.environ['GameItemSmokeApi'] + '/api/v1'
admin = {}
def request(path, method='GET', payload=None, headers=None, status=200):
    data = None if payload is None else json.dumps(payload).encode()
    return send(path, method, data, headers, status)

def send(path, method, data, headers, status):
    req = Request(base + path, data=data, method=method,
                  headers={'Content-Type': 'application/json', **(headers or {})})
    try:
        response = urlopen(req, timeout=10)
    except HTTPError as error:
        response = error
    with response:
        raw = response.read()
        assert response.status == status, (method, path, response.status, status, raw[:400])
        return json.loads(raw) if raw else None

credentials = {'email': os.environ['CmsAdmin__Email'], 'password': os.environ['CmsAdmin__Password']}
token = request('/cms/auth/login', 'POST', credentials)['accessToken']
admin = {'Authorization': f'Bearer {token}'}
home = request('/homepage')
category = next(x for x in home['serviceCategories'] if x['kind'] == 'GameItem')
assert category['slug'] == 'game-items'
assert len(home['serviceCategories']) > 1, 'Migration must preserve the other categories in a fresh database'

settings = request('/cms/site/settings', headers=admin)
support = '+6281234567811'
request('/cms/site/settings', 'PUT', {**settings, 'brandDescription': 'QA storefront', 'gameItemSupportPhone': support}, admin, 204)
assert 'gameItemSupportPhone' not in request('/site/footer')
game = request('/cms/catalog/games', 'POST', {
    'name': 'QA Game', 'slug': 'qa-game-items', 'isActive': True, 'sortOrder': 99
}, admin, 201)

product = request('/cms/catalog/products', 'POST', {
    'categoryId': category['id'], 'gameId': game, 'name': 'QA Digital Item',
    'slug': 'qa-digital-item', 'kind': 'GameItem', 'fulfillmentMethod': 'MANUAL',
    'requiresGameAccountValidation': False, 'isActive': True, 'isFeatured': False, 'sortOrder': 0,
}, admin, 201)
product_path = f'/cms/catalog/products/{product}'
schema = {'key': 'steam-url', 'label': 'Steam Trade Offer URL', 'scope': 'OrderDelivery',
          'type': 'SteamTradeUrl', 'isRequired': True, 'isSensitive': False, 'maxLength': 200, 'sortOrder': 0}
request(product_path + '/input-fields', 'POST', {**schema, 'key': 'game_password', 'label': 'Password'}, admin, 400)
request(product_path + '/input-fields', 'POST', schema, admin, 201)
variant = request(product_path + '/variants', 'POST', {
    'name': 'QA Variant', 'sku': 'QA-ITEM-01', 'price': 25000, 'stockQuantity': 1, 'isActive': True, 'sortOrder': 0
}, admin, 201)
listing = request('/catalog/products?kind=GameItem')
assert any(x['slug'] == 'qa-digital-item' for x in listing['items'])
pdp = request('/catalog/products/qa-digital-item')
assert pdp['inputFields'][0]['scope'] == 'OrderDelivery'
assert pdp['inputFields'][0]['type'] == 'SteamTradeUrl'

trade_url = 'https://steamcommunity.com/tradeoffer/new/?partner=12345&token=qa-test'
phone = '+6281234567800'
line = {'productVariantId': variant, 'quantity': 1}
def checkout(item, status=201):
    return request('/checkout/orders', 'POST', {'customerPhone': phone, 'items': [item]}, status=status)

checkout(line, 400)
checkout({**line, 'deliveryDetails': {'steam-url': 'https://example.com/tradeoffer/new/?partner=12345&token=qa-test'}}, 400)
order = checkout({**line, 'deliveryDetails': {'steam-url': trade_url}})
assert trade_url not in json.dumps(order)
oid = order['id']
payment_path = f'/checkout/orders/{oid}/payment'
order_path = f'/cms/orders/{oid}'
access = {'X-Order-Access-Token': order['orderAccessToken']}
assert request(payment_path, headers=access).get('gameItemSupportPhone') is None
details = request(order_path, headers=admin)
assert not details['items'][0].get('gameItemDelivery')
item_id = details['items'][0]['id']
fulfillment_path = f'{order_path}/items/{item_id}/fulfillment'
request(fulfillment_path, 'PUT', {'status': 'Completed', 'reference': 'early'}, admin, 409)
attempt = request(payment_path, 'POST', headers=access)
webhook_body = json.dumps({'providerReference': attempt['providerReference'], 'status': 'Paid',
                           'amount': 25000, 'currency': 'IDR'}).encode()
signature = hmac.new(b'game-item-test-secret', webhook_body, hashlib.sha256).hexdigest()
send('/payments/webhooks/mock', 'POST', webhook_body, {'X-Mock-Signature': signature}, 200)
assert request(payment_path, headers=access)['gameItemSupportPhone'] == support
details = request(order_path, headers=admin)
assert details['items'][0]['gameItemDelivery'] == [{'label': schema['label'], 'value': trade_url}]
lookup = request('/orders/lookup', 'POST', {'orderNumber': order['orderNumber'], 'customerPhone': phone})
assert lookup['gameItemSupportPhone'] == support
assert trade_url not in json.dumps(lookup)
result = request(fulfillment_path, 'PUT', {'status': 'Completed', 'reference': 'QA-TRADE-01'}, admin)
assert result['orderStatus'] == 'Completed'
assert not request('/catalog/products/qa-digital-item')['isAvailable']
print('PASS: GameItem category, CMS SKU and input schema, checkout, paid-only visibility, fulfillment and stock.')
PY
