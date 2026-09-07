#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

[[ -f .env ]] || { echo 'Missing .env.' >&2; exit 1; }
get_env() { sed -n "s/^${1}=//p" .env | tail -n 1; }

ENVIRONMENT=$(get_env ZETRUV_ENVIRONMENT)
API_DOMAIN=$(get_env API_DOMAIN)
API_DOMAIN_LEGACY=$(get_env API_DOMAIN_LEGACY)
FRONTEND_ORIGIN=$(get_env FRONTEND_ORIGIN)
FRONTEND_ORIGIN_LEGACY=$(get_env FRONTEND_ORIGIN_LEGACY)

[[ "$ENVIRONMENT" == "staging" ]] || { echo "This smoke is STAGING-only (current: ${ENVIRONMENT:-unknown})." >&2; exit 1; }
[[ "$API_DOMAIN" == "api-staging.zetruv.com" ]] || { echo "Unexpected STAGING API_DOMAIN: $API_DOMAIN" >&2; exit 1; }
[[ "$API_DOMAIN_LEGACY" == "api-staging.zetruv.dualangka.com" ]] || { echo "Unexpected STAGING API_DOMAIN_LEGACY: $API_DOMAIN_LEGACY" >&2; exit 1; }
[[ "$FRONTEND_ORIGIN" == "https://staging.zetruv.com" ]] || { echo "Unexpected STAGING FRONTEND_ORIGIN: $FRONTEND_ORIGIN" >&2; exit 1; }
[[ "$FRONTEND_ORIGIN_LEGACY" == "https://staging.zetruv.dualangka.com" ]] || { echo "Unexpected STAGING FRONTEND_ORIGIN_LEGACY: $FRONTEND_ORIGIN_LEGACY" >&2; exit 1; }

BASE_URL="https://$API_DOMAIN"
LEGACY_BASE_URL="https://$API_DOMAIN_LEGACY"
TMP_HEADERS=$(mktemp)
TMP_HEADERS_NORMALIZED=$(mktemp)
TMP_OPENAPI=$(mktemp)
trap 'rm -f "$TMP_HEADERS" "$TMP_HEADERS_NORMALIZED" "$TMP_OPENAPI"' EXIT

assert_cors() {
  local base_url="$1"
  local origin="$2"
  : > "$TMP_HEADERS"
  local status
  status=$(curl -sS -o /dev/null -D "$TMP_HEADERS" -w '%{http_code}' -X OPTIONS     "$base_url/api/v1/homepage"     -H "Origin: $origin"     -H 'Access-Control-Request-Method: GET'     -H 'Access-Control-Request-Headers: content-type')

  [[ "$status" == "204" || "$status" == "200" ]] || { echo "Unexpected preflight status for $origin via $base_url: $status" >&2; exit 1; }

  tr -d '\r' < "$TMP_HEADERS" > "$TMP_HEADERS_NORMALIZED"
  grep -Fxiq "Access-Control-Allow-Origin: $origin" "$TMP_HEADERS_NORMALIZED" || {
    echo "CORS header does not allow $origin via $base_url." >&2
    cat "$TMP_HEADERS_NORMALIZED" >&2
    exit 1
  }
}

echo '1/6 primary health'
curl -fsS "$BASE_URL/health" >/dev/null

echo '2/6 primary OpenAPI contract'
curl -fsS "$BASE_URL/openapi/v1.json" -o "$TMP_OPENAPI"
grep -q '"openapi"' "$TMP_OPENAPI"

echo '3/6 primary homepage'
curl -fsS "$BASE_URL/api/v1/homepage" >/dev/null

echo '4/6 CORS from new STAGING frontend'
assert_cors "$BASE_URL" "$FRONTEND_ORIGIN"

echo '5/6 legacy API alias health'
curl -fsS "$LEGACY_BASE_URL/health" >/dev/null

echo '6/6 legacy frontend CORS remains valid during cutover'
assert_cors "$LEGACY_BASE_URL" "$FRONTEND_ORIGIN_LEGACY"

echo 'PASS: STAGING zetruv.com backend cutover is healthy; new API/origin are primary and dualangka.com remains a temporary alias.'
