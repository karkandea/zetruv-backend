#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

C=zetruv-pg-payment-hardening
DB=zetruv_payment_hardening_test
PORT=55435
API=18083
PID=""
cleanup(){ [[ -n "$PID" ]] && kill "$PID" >/dev/null 2>&1 || true; docker rm -f "$C" >/dev/null 2>&1 || true; }
trap cleanup EXIT

echo '=== BUILD ==='
dotnet build src/Zetruv.Api/Zetruv.Api.csproj --configuration Release

echo '=== START FRESH POSTGRES ==='
docker rm -f "$C" >/dev/null 2>&1 || true
docker run -d --name "$C" -e POSTGRES_USER=zetruv -e POSTGRES_PASSWORD=zetruvtest -e POSTGRES_DB="$DB" -p 127.0.0.1:${PORT}:5432 postgres:17-alpine >/dev/null
until docker exec "$C" pg_isready -U zetruv -d "$DB" >/dev/null 2>&1; do sleep 1; done

export ConnectionStrings__Postgres="Host=127.0.0.1;Port=$PORT;Database=$DB;Username=zetruv;Password=zetruvtest"
export Jwt__Key='0123456789abcdef0123456789abcdef'
export Payments__Provider=mock
export Payments__Mock__WebhookSecret=smoke-secret
export Shipping__Provider=mock
export GameAccountValidation__Provider=mock
export ASPNETCORE_ENVIRONMENT=Staging

dotnet run --project src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --no-build --urls="http://127.0.0.1:$API" >/tmp/zetruv-payment-hardening.log 2>&1 & PID=$!
for _ in $(seq 1 30); do curl -fsS "http://127.0.0.1:$API/health" >/dev/null && break; sleep 1; done
curl -fsS "http://127.0.0.1:$API/health"; echo

echo '=== SEED ==='
docker exec -i "$C" psql -v ON_ERROR_STOP=1 -U zetruv -d "$DB" <<'SQL'
INSERT INTO products ("Id","CategoryId","Name","Slug","Kind","RequiresGameAccountValidation","IsActive","IsFeatured","SortOrder","CreatedAt","UpdatedAt") VALUES ('10000000-0000-0000-0000-000000000001',(SELECT "Id" FROM catalog_categories WHERE "Key"='top_up_games'),'Hardening Product','hardening-product','TopUpGame',FALSE,TRUE,FALSE,0,NOW(),NOW());
INSERT INTO product_variants ("Id","ProductId","Name","Sku","Price","StockQuantity","IsActive","SortOrder","CreatedAt","UpdatedAt") VALUES
('20000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001','A','HARD-A',100000,1,TRUE,0,NOW(),NOW()),
('20000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000001','B','HARD-B',100000,0,TRUE,1,NOW(),NOW()),
('20000000-0000-0000-0000-000000000003','10000000-0000-0000-0000-000000000001','C','HARD-C',100000,0,TRUE,2,NOW(),NOW());
INSERT INTO orders ("Id","OrderNumber","Status","PaymentStatus","Subtotal","DiscountAmount","ShippingAmount","GrandTotal","Currency","PaymentProvider","PaymentReference","CreatedAt","UpdatedAt") VALUES
('30000000-0000-0000-0000-000000000001','ZTR-HARD-001','Pending','Pending',100000,0,0,100000,'IDR','mock','REF-A',NOW(),NOW()),
('30000000-0000-0000-0000-000000000002','ZTR-HARD-002','Pending','Pending',100000,0,0,100000,'IDR','mock','REF-B',NOW(),NOW()),
('30000000-0000-0000-0000-000000000003','ZTR-HARD-003','Pending','Pending',100000,0,0,100000,'IDR','mock','REF-C',NOW(),NOW());
INSERT INTO order_items ("Id","OrderId","ProductId","ProductVariantId","ProductName","ProductSlug","ProductKind","VariantName","Sku","UnitPrice","Quantity","LineTotal","CreatedAt") VALUES
('40000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001','Hardening Product','hardening-product','TopUpGame','A','HARD-A',100000,1,100000,NOW()),
('40000000-0000-0000-0000-000000000002','30000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000002','Hardening Product','hardening-product','TopUpGame','B','HARD-B',100000,1,100000,NOW()),
('40000000-0000-0000-0000-000000000003','30000000-0000-0000-0000-000000000003','10000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000003','Hardening Product','hardening-product','TopUpGame','C','HARD-C',100000,1,100000,NOW());
INSERT INTO payment_transactions ("Id","OrderId","Provider","ProviderReference","Type","Status","Amount","Currency","CreatedAt","UpdatedAt") VALUES
('50000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','mock','REF-A','Payment','Pending',100000,'IDR',NOW(),NOW()),
('50000000-0000-0000-0000-000000000002','30000000-0000-0000-0000-000000000002','mock','REF-B','Payment','Pending',100000,'IDR',NOW(),NOW()),
('50000000-0000-0000-0000-000000000003','30000000-0000-0000-0000-000000000003','mock','REF-C','Payment','Pending',100000,'IDR',NOW(),NOW());
INSERT INTO inventory_reservations ("Id","OrderId","ProductVariantId","Quantity","Status","ExpiresAt","CreatedAt","UpdatedAt") VALUES
('60000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000001',1,'Released',NOW()-INTERVAL '1 minute',NOW(),NOW()),
('60000000-0000-0000-0000-000000000002','30000000-0000-0000-0000-000000000002','20000000-0000-0000-0000-000000000002',1,'Released',NOW()-INTERVAL '1 minute',NOW(),NOW()),
('60000000-0000-0000-0000-000000000003','30000000-0000-0000-0000-000000000003','20000000-0000-0000-0000-000000000003',1,'Active',NOW()+INTERVAL '1 hour',NOW(),NOW());
SQL

webhook(){
  local ref="$1" status="$2" expected="$3" body sig code
  body="{\"providerReference\":\"$ref\",\"status\":\"$status\",\"amount\":100000,\"currency\":\"IDR\"}"
  sig=$(python3 -c 'import hmac,hashlib,sys; print(hmac.new(b"smoke-secret",sys.argv[1].encode(),hashlib.sha256).hexdigest())' "$body")
  code=$(curl -sS -o /tmp/hardening-response.json -w '%{http_code}' -X POST "http://127.0.0.1:$API/api/v1/payments/webhooks/mock" -H 'Content-Type: application/json' -H "X-Mock-Signature: $sig" -d "$body")
  cat /tmp/hardening-response.json; echo
  [[ "$code" == "$expected" ]]
}

echo '=== LATE PAID REACQUIRE ==='
webhook REF-A Paid 200

echo '=== LATE PAID SOLD OUT ==='
webhook REF-B Paid 409

echo '=== FAILED RELEASE IDEMPOTENCY ==='
webhook REF-C Failed 200
webhook REF-C Failed 200

echo '=== ASSERT ==='
R=$(docker exec "$C" psql -At -F '|' -U zetruv -d "$DB" -c "SELECT o.\"OrderNumber\",o.\"PaymentStatus\",pt.\"Status\",ir.\"Status\",pv.\"StockQuantity\" FROM orders o JOIN payment_transactions pt ON pt.\"OrderId\"=o.\"Id\" JOIN inventory_reservations ir ON ir.\"OrderId\"=o.\"Id\" JOIN product_variants pv ON pv.\"Id\"=ir.\"ProductVariantId\" WHERE o.\"OrderNumber\" LIKE 'ZTR-HARD-%' ORDER BY o.\"OrderNumber\";")
echo "$R"
[[ "$R" == *"ZTR-HARD-001|Paid|Succeeded|Consumed|0"* ]]
[[ "$R" == *"ZTR-HARD-002|Pending|Pending|Released|0"* ]]
[[ "$R" == *"ZTR-HARD-003|Failed|Failed|Released|1"* ]]
echo 'PASS: late paid reacquires available stock'
echo 'PASS: late paid is blocked when stock is unavailable'
echo 'PASS: repeated failed webhook releases stock exactly once'
echo 'PASS: payment inventory reconciliation hardening'
