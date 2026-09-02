#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

LOG=/tmp/zetruv-production-provider-guard.log
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
