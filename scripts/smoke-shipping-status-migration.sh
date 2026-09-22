#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

C=zetruv-pg-shipping-migration
DB=zetruv_shipping_migration_test
PORT=57448
PREVIOUS=20260922085337_AddMediaAssets

cleanup() {
  docker rm -f "$C" >/dev/null 2>&1 || true
}
trap cleanup EXIT

dotnet build src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --nologo
docker rm -f "$C" >/dev/null 2>&1 || true
docker run -d --name "$C" \
  -e POSTGRES_USER=zetruv \
  -e POSTGRES_PASSWORD=zetruvtest \
  -e POSTGRES_DB="$DB" \
  -p "127.0.0.1:${PORT}:5432" \
  postgres:17-alpine >/dev/null

until docker exec "$C" pg_isready -U zetruv -d "$DB" >/dev/null 2>&1; do
  sleep 1
done

export ConnectionStrings__Postgres="Host=127.0.0.1;Port=${PORT};Database=${DB};Username=zetruv;Password=zetruvtest"
export Jwt__Key='0123456789abcdef0123456789abcdef'
export Payments__Provider=mock
export Payments__Mock__WebhookSecret=smoke-secret
export Shipping__Provider=mock
export GameAccountValidation__Provider=mock
export ASPNETCORE_ENVIRONMENT=Staging

echo '=== MIGRATE TO PRE-HARDENING SCHEMA ==='
dotnet ef database update "$PREVIOUS" \
  --project src/Zetruv.Api/Zetruv.Api.csproj \
  --startup-project src/Zetruv.Api/Zetruv.Api.csproj \
  --configuration Release \
  --no-build >/dev/null

docker exec -i "$C" psql -v ON_ERROR_STOP=1 -U zetruv -d "$DB" <<'SQL'
INSERT INTO orders (
  "Id","OrderNumber","Status","PaymentStatus",
  "Subtotal","DiscountAmount","ShippingAmount","GrandTotal","Currency",
  "CreatedAt","UpdatedAt"
)
VALUES (
  'e1000000-0000-0000-0000-000000000001',
  'ZTR-MIGRATION-001','Processing','Paid',
  100000,0,17000,117000,'IDR',NOW(),NOW()
);

INSERT INTO shipments (
  "Id","OrderId","Status","Provider","ServiceCode","ServiceName",
  "Cost","Currency","TotalWeightGrams",
  "RecipientName","Phone","AddressLine1","District","City","Province","PostalCode",
  "CreatedAt","UpdatedAt"
)
VALUES (
  'e2000000-0000-0000-0000-000000000001',
  'e1000000-0000-0000-0000-000000000001',
  2,'mock','REG','Regular',
  17000,'IDR',500,
  'Migration User','08123456789','Jl. Migration No. 1',
  'Pesanggrahan','Jakarta Selatan','DKI Jakarta','12320',
  NOW(),NOW()
);
SQL

echo '=== UPGRADE INTEGER STATUS TO STRING ==='
dotnet ef database update \
  --project src/Zetruv.Api/Zetruv.Api.csproj \
  --startup-project src/Zetruv.Api/Zetruv.Api.csproj \
  --configuration Release \
  --no-build >/dev/null

UP_STATE=$(docker exec "$C" psql -At -F '|' -U zetruv -d "$DB" -c "
SELECT
  s.\"Status\",
  c.data_type,
  c.character_maximum_length
FROM shipments s
JOIN information_schema.columns c
  ON c.table_name='shipments' AND c.column_name='Status'
WHERE s.\"Id\"='e2000000-0000-0000-0000-000000000001';")
echo "UP_STATE=$UP_STATE"
[[ "$UP_STATE" == 'Shipped|character varying|30' ]]

echo '=== ROLLBACK STRING STATUS TO INTEGER ==='
dotnet ef database update "$PREVIOUS" \
  --project src/Zetruv.Api/Zetruv.Api.csproj \
  --startup-project src/Zetruv.Api/Zetruv.Api.csproj \
  --configuration Release \
  --no-build >/dev/null

DOWN_STATE=$(docker exec "$C" psql -At -F '|' -U zetruv -d "$DB" -c "
SELECT
  s.\"Status\",
  c.data_type
FROM shipments s
JOIN information_schema.columns c
  ON c.table_name='shipments' AND c.column_name='Status'
WHERE s.\"Id\"='e2000000-0000-0000-0000-000000000001';")
echo "DOWN_STATE=$DOWN_STATE"
[[ "$DOWN_STATE" == '2|integer' ]]

echo 'PASS: existing shipment status migrates safely integer <-> string'
