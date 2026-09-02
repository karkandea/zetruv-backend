#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

CONTAINER=zetruv-pg-payment-ref
PORT=55435
DB=zetruv_payment_ref_test
cleanup() { docker rm -f "$CONTAINER" >/dev/null 2>&1 || true; }
trap cleanup EXIT

echo "=== BUILD ==="
dotnet build src/Zetruv.Api/Zetruv.Api.csproj --configuration Release

echo "=== FRESH POSTGRES + MIGRATIONS ==="
cleanup
docker run -d --name "$CONTAINER" \
  -e POSTGRES_USER=zetruv \
  -e POSTGRES_PASSWORD=zetruvtest \
  -e POSTGRES_DB="$DB" \
  -p "127.0.0.1:${PORT}:5432" \
  postgres:17-alpine >/dev/null
until docker exec "$CONTAINER" pg_isready -U zetruv -d "$DB" >/dev/null 2>&1; do sleep 1; done

export ConnectionStrings__Postgres="Host=127.0.0.1;Port=${PORT};Database=${DB};Username=zetruv;Password=zetruvtest"
dotnet tool restore >/dev/null
dotnet tool run dotnet-ef database update \
  --project src/Zetruv.Api/Zetruv.Api.csproj \
  --startup-project src/Zetruv.Api/Zetruv.Api.csproj >/dev/null

echo "=== INDEX ==="
INDEX_DEF=$(docker exec "$CONTAINER" psql -At -U zetruv -d "$DB" -c \
  "SELECT indexdef FROM pg_indexes WHERE tablename='payment_transactions' AND indexname='IX_payment_transactions_Provider_ProviderReference';")
echo "$INDEX_DEF"
grep -q 'CREATE UNIQUE INDEX' <<< "$INDEX_DEF"

echo "=== SEED ==="
docker exec -i "$CONTAINER" psql -v ON_ERROR_STOP=1 -U zetruv -d "$DB" <<'SQL' >/dev/null
INSERT INTO orders ("Id","OrderNumber","Status","PaymentStatus","Subtotal","DiscountAmount","ShippingAmount","GrandTotal","Currency","CreatedAt","UpdatedAt") VALUES
('10000000-0000-0000-0000-000000000001','ZTR-REF-001','Pending','Pending',10000,0,0,10000,'IDR',NOW(),NOW()),
('10000000-0000-0000-0000-000000000002','ZTR-REF-002','Pending','Pending',10000,0,0,10000,'IDR',NOW(),NOW()),
('10000000-0000-0000-0000-000000000003','ZTR-REF-003','Pending','Pending',10000,0,0,10000,'IDR',NOW(),NOW()),
('10000000-0000-0000-0000-000000000004','ZTR-REF-004','Pending','Pending',10000,0,0,10000,'IDR',NOW(),NOW()),
('10000000-0000-0000-0000-000000000005','ZTR-REF-005','Pending','Pending',10000,0,0,10000,'IDR',NOW(),NOW());

INSERT INTO payment_transactions ("Id","OrderId","Provider","ProviderReference","Type","Status","Amount","Currency","CreatedAt","UpdatedAt") VALUES
('20000000-0000-0000-0000-000000000001','10000000-0000-0000-0000-000000000001','mock','SAME-REF','Payment','Pending',10000,'IDR',NOW(),NOW()),
('20000000-0000-0000-0000-000000000002','10000000-0000-0000-0000-000000000002','other','SAME-REF','Payment','Pending',10000,'IDR',NOW(),NOW()),
('20000000-0000-0000-0000-000000000003','10000000-0000-0000-0000-000000000003','mock',NULL,'Payment','Pending',10000,'IDR',NOW(),NOW()),
('20000000-0000-0000-0000-000000000004','10000000-0000-0000-0000-000000000004','mock',NULL,'Payment','Pending',10000,'IDR',NOW(),NOW());
SQL

echo "=== DUPLICATE SAME PROVIDER/REFERENCE ==="
set +e
DUP_OUTPUT=$(docker exec "$CONTAINER" psql -v ON_ERROR_STOP=1 -U zetruv -d "$DB" -c \
  "INSERT INTO payment_transactions (\"Id\",\"OrderId\",\"Provider\",\"ProviderReference\",\"Type\",\"Status\",\"Amount\",\"Currency\",\"CreatedAt\",\"UpdatedAt\") VALUES ('20000000-0000-0000-0000-000000000005','10000000-0000-0000-0000-000000000005','mock','SAME-REF','Payment','Pending',10000,'IDR',NOW(),NOW());" 2>&1)
DUP_STATUS=$?
set -e
echo "$DUP_OUTPUT"

if [[ "$DUP_STATUS" -eq 0 ]]; then
  echo "ERROR: duplicate provider/reference was accepted" >&2
  exit 1
fi
grep -q 'IX_payment_transactions_Provider_ProviderReference' <<< "$DUP_OUTPUT"

COUNT=$(docker exec "$CONTAINER" psql -At -U zetruv -d "$DB" -c \
  "SELECT COUNT(*) FROM payment_transactions WHERE \"ProviderReference\"='SAME-REF';")
NULL_COUNT=$(docker exec "$CONTAINER" psql -At -U zetruv -d "$DB" -c \
  "SELECT COUNT(*) FROM payment_transactions WHERE \"Provider\"='mock' AND \"ProviderReference\" IS NULL;")

[[ "$COUNT" == "2" ]]
[[ "$NULL_COUNT" == "2" ]]

echo "PASS: same provider + same reference is rejected"
echo "PASS: same reference across different providers is allowed"
echo "PASS: multiple null provider references are allowed"
echo "PASS: payment provider reference uniqueness"
