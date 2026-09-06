#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

[[ -f .env ]] || { echo 'Missing .env.' >&2; exit 1; }
get_env() { sed -n "s/^${1}=//p" .env | tail -n 1; }

ENVIRONMENT=$(get_env ZETRUV_ENVIRONMENT)
API_DOMAIN=$(get_env API_DOMAIN)
FRONTEND_ORIGIN=$(get_env FRONTEND_ORIGIN)

[[ "$ENVIRONMENT" == "dev" ]] || { echo "This smoke is DEV-only (current: ${ENVIRONMENT:-unknown})." >&2; exit 1; }
[[ "$API_DOMAIN" == "api-dev.zetruv.dualangka.com" ]] || { echo "Unexpected DEV API_DOMAIN: $API_DOMAIN" >&2; exit 1; }
[[ "$FRONTEND_ORIGIN" == "https://dev.zetruv.dualangka.com" ]] || { echo "Unexpected DEV FRONTEND_ORIGIN: $FRONTEND_ORIGIN" >&2; exit 1; }

BASE_URL="https://$API_DOMAIN"
TMP_HEADERS=$(mktemp)
trap 'rm -f "$TMP_HEADERS"' EXIT

echo '1/4 health'
curl -fsS "$BASE_URL/health" >/dev/null

echo '2/4 OpenAPI contract'
curl -fsS "$BASE_URL/openapi/v1.json" | grep -q '"openapi"'

echo '3/4 public homepage'
curl -fsS "$BASE_URL/api/v1/homepage" >/dev/null

echo '4/4 browser CORS from DEV frontend'
STATUS=$(curl -sS -o /dev/null -D "$TMP_HEADERS" -w '%{http_code}' -X OPTIONS   "$BASE_URL/api/v1/homepage"   -H "Origin: $FRONTEND_ORIGIN"   -H 'Access-Control-Request-Method: GET'   -H 'Access-Control-Request-Headers: content-type')

[[ "$STATUS" == "204" || "$STATUS" == "200" ]] || { echo "Unexpected preflight status: $STATUS" >&2; exit 1; }
grep -qi "^access-control-allow-origin: $FRONTEND_ORIGIN\r\?$" "$TMP_HEADERS" || {
  echo "CORS header does not allow $FRONTEND_ORIGIN." >&2
  cat "$TMP_HEADERS" >&2
  exit 1
}

echo 'PASS: DEV frontend handoff is healthy, OpenAPI is published, public API responds, and DEV CORS is correct.'
