#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

LOG=/tmp/zetruv-production-provider-guard.log
CONTAINER=zetruv-pg-production-provider-guard
DB=zetruv_production_provider_guard
PORT=57448

cleanup() {
  docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
}
trap cleanup EXIT
rm -f "$LOG"

echo "=== BUILD ==="
dotnet build src/Zetruv.Api/Zetruv.Api.csproj --configuration Release

echo "=== PRODUCTION MOCK PROVIDER GUARD ==="
set +e
ASPNETCORE_ENVIRONMENT=Production \
ConnectionStrings__Postgres='Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused' \
Jwt__Key='0123456789abcdef0123456789abcdef' \
Payments__Provider='mock' \
Shipping__Provider='mock' \
GameAccountValidation__Provider='mock' \
dotnet run --project src/Zetruv.Api/Zetruv.Api.csproj \
  --configuration Release \
  --no-build \
  >"$LOG" 2>&1
STATUS=$?
set -e

if [[ "$STATUS" -eq 0 ]]; then
  echo "ERROR: Production unexpectedly started with mock providers enabled."
  cat "$LOG"
  exit 1
fi

if ! grep -Fq 'Mock providers cannot be enabled in Production' "$LOG"; then
  echo "ERROR: Production failed for an unexpected reason."
  cat "$LOG"
  exit 1
fi

cat "$LOG"
echo "PASS: Production rejects mock providers before startup"

echo "=== PRODUCTION MANUAL LOGIN KEY GUARD ==="
set +e
ASPNETCORE_ENVIRONMENT=Production \
ConnectionStrings__Postgres='Host=127.0.0.1;Port=1;Database=unused;Username=unused;Password=unused' \
Jwt__Key='0123456789abcdef0123456789abcdef' \
Payments__Provider='real' \
Shipping__Provider='real' \
GameAccountValidation__Provider='real' \
dotnet run --project src/Zetruv.Api/Zetruv.Api.csproj \
  --configuration Release \
  --no-build \
  >"$LOG" 2>&1
STATUS=$?
set -e

if [[ "$STATUS" -eq 0 ]]; then
  echo "ERROR: Production unexpectedly started without a manual login encryption key."
  cat "$LOG"
  exit 1
fi

if ! grep -Fq 'ManualLogin:EncryptionKey must be configured' "$LOG"; then
  echo "ERROR: Production manual-login key guard failed for an unexpected reason."
  cat "$LOG"
  exit 1
fi

cat "$LOG"
echo "PASS: Production requires a valid manual login encryption key before startup"

echo "=== PRODUCTION AUTO_ID MOCK MAPPING GUARD ==="
docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
docker run -d --name "$CONTAINER" \
  -e POSTGRES_USER=zetruv \
  -e POSTGRES_PASSWORD=zetruvtest \
  -e POSTGRES_DB="$DB" \
  -p "127.0.0.1:${PORT}:5432" \
  postgres:17-alpine >/dev/null

until docker exec "$CONTAINER" pg_isready -U zetruv -d "$DB" >/dev/null 2>&1; do
  sleep 1
done

export ConnectionStrings__Postgres="Host=127.0.0.1;Port=${PORT};Database=${DB};Username=zetruv;Password=zetruvtest"
dotnet tool restore >/dev/null
ASPNETCORE_ENVIRONMENT=Staging \
  dotnet ef database update \
  --project src/Zetruv.Api/Zetruv.Api.csproj \
  --startup-project src/Zetruv.Api/Zetruv.Api.csproj \
  --configuration Release \
  --no-build >/dev/null

docker exec -i "$CONTAINER" psql -v ON_ERROR_STOP=1 -U zetruv -d "$DB" <<'SQL'
INSERT INTO provider_game_mappings (
  "Id","GameId","ProviderCode","NicknameCheckEnabled","IsActive","CreatedAt","UpdatedAt"
)
VALUES (
  'c2000000-0000-0000-0000-000000000001',
  (SELECT "Id" FROM games ORDER BY "CreatedAt" LIMIT 1),
  'mock',
  FALSE,
  TRUE,
  NOW(),
  NOW()
)
ON CONFLICT ("GameId") DO UPDATE
SET "ProviderCode" = 'mock',
    "IsActive" = TRUE,
    "UpdatedAt" = NOW();
SQL

MANUAL_LOGIN_KEY=$(printf '0123456789abcdef0123456789abcdef' | openssl base64 -A)
set +e
ASPNETCORE_ENVIRONMENT=Production \
ConnectionStrings__Postgres="$ConnectionStrings__Postgres" \
Jwt__Key='0123456789abcdef0123456789abcdef' \
Payments__Provider='real' \
Shipping__Provider='real' \
GameAccountValidation__Provider='real' \
ManualLogin__EncryptionKey="$MANUAL_LOGIN_KEY" \
Payments__Reconciliation__Enabled='false' \
dotnet run --project src/Zetruv.Api/Zetruv.Api.csproj \
  --configuration Release \
  --no-build \
  >"$LOG" 2>&1
STATUS=$?
set -e

if [[ "$STATUS" -eq 0 ]]; then
  echo "ERROR: Production unexpectedly started with an active AUTO_ID mock mapping."
  cat "$LOG"
  exit 1
fi

if ! grep -Fq 'Active AUTO_ID mock provider mappings cannot be enabled in Production.' "$LOG"; then
  echo "ERROR: Production AUTO_ID mapping guard failed for an unexpected reason."
  cat "$LOG"
  exit 1
fi

cat "$LOG"
echo "PASS: Production rejects active AUTO_ID mappings that target mock"
