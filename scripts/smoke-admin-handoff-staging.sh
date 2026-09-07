#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

[[ -f .env ]] || { echo 'Missing .env.' >&2; exit 1; }
get_env() { sed -n "s/^${1}=//p" .env | tail -n 1; }

ENVIRONMENT=$(get_env ZETRUV_ENVIRONMENT)
API_DOMAIN=$(get_env API_DOMAIN)
CMS_ORIGIN=$(get_env CMS_ORIGIN)
CMS_ORIGIN_LEGACY=$(get_env CMS_ORIGIN_LEGACY)
CMS_EMAIL=$(get_env CMS_ADMIN_EMAIL)
CMS_PASSWORD=$(get_env CMS_ADMIN_PASSWORD)

[[ "$ENVIRONMENT" == "staging" ]] || { echo "This smoke is STAGING-only (current: ${ENVIRONMENT:-unknown})." >&2; exit 1; }
[[ "$API_DOMAIN" == "api-staging.zetruv.com" ]] || { echo "Unexpected STAGING API_DOMAIN: $API_DOMAIN" >&2; exit 1; }
[[ "$CMS_ORIGIN" == "https://admin-staging.zetruv.com" ]] || { echo "Unexpected STAGING CMS_ORIGIN: $CMS_ORIGIN" >&2; exit 1; }
[[ "$CMS_ORIGIN_LEGACY" == "https://admin.zetruv.dualangka.com" ]] || { echo "Unexpected STAGING CMS_ORIGIN_LEGACY: $CMS_ORIGIN_LEGACY" >&2; exit 1; }
[[ -n "$CMS_EMAIL" && -n "$CMS_PASSWORD" ]] || { echo 'CMS admin credentials are missing.' >&2; exit 1; }

for cmd in curl python3; do
  command -v "$cmd" >/staging/null 2>&1 || { echo "$cmd is required." >&2; exit 1; }
done

BASE_URL="https://$API_DOMAIN"
TMP_HEADERS=$(mktemp)
TMP_HEADERS_NORMALIZED=$(mktemp)
TMP_LOGIN=$(mktemp)
TMP_ORDERS=$(mktemp)
trap 'rm -f "$TMP_HEADERS" "$TMP_HEADERS_NORMALIZED" "$TMP_LOGIN" "$TMP_ORDERS"' EXIT

assert_cors() {
  local origin="$1"
  : > "$TMP_HEADERS"
  local status
  status=$(curl -sS -o /staging/null -D "$TMP_HEADERS" -w '%{http_code}' -X OPTIONS     "$BASE_URL/api/v1/cms/orders"     -H "Origin: $origin"     -H 'Access-Control-Request-Method: GET'     -H 'Access-Control-Request-Headers: authorization,content-type')

  [[ "$status" == "204" || "$status" == "200" ]] || {
    echo "Unexpected CMS preflight status for $origin: $status" >&2
    exit 1
  }

  tr -d '\r' < "$TMP_HEADERS" > "$TMP_HEADERS_NORMALIZED"
  grep -Fxiq "Access-Control-Allow-Origin: $origin" "$TMP_HEADERS_NORMALIZED" || {
    echo "CMS CORS does not allow $origin." >&2
    cat "$TMP_HEADERS_NORMALIZED" >&2
    exit 1
  }
}

echo '1/4 CORS from new STAGING admin'
assert_cors "$CMS_ORIGIN"

echo '2/4 protected CMS rejects unauthenticated request'
UNAUTH_STATUS=$(curl -sS -o /staging/null -w '%{http_code}' "$BASE_URL/api/v1/cms/orders?pageSize=1")
[[ "$UNAUTH_STATUS" == "401" ]] || { echo "Expected 401 without JWT, got $UNAUTH_STATUS." >&2; exit 1; }

echo '3/4 canonical CMS login'
LOGIN_PAYLOAD=$(CMS_EMAIL="$CMS_EMAIL" CMS_PASSWORD="$CMS_PASSWORD" python3 - <<'PY'
import json, os
print(json.dumps({
    "email": os.environ["CMS_EMAIL"],
    "password": os.environ["CMS_PASSWORD"],
}))
PY
)
LOGIN_STATUS=$(curl -sS -o "$TMP_LOGIN" -w '%{http_code}'   -X POST "$BASE_URL/api/v1/cms/auth/login"   -H 'Content-Type: application/json'   --data "$LOGIN_PAYLOAD")
[[ "$LOGIN_STATUS" == "200" ]] || {
  echo "CMS login failed with HTTP $LOGIN_STATUS." >&2
  cat "$TMP_LOGIN" >&2
  exit 1
}

ACCESS_TOKEN=$(python3 - "$TMP_LOGIN" <<'PY'
import json, sys
with open(sys.argv[1], encoding="utf-8") as fh:
    data = json.load(fh)
token = data.get("accessToken") or data.get("AccessToken")
if not token:
    raise SystemExit("Login response did not contain accessToken.")
print(token)
PY
)
[[ -n "$ACCESS_TOKEN" ]] || { echo 'CMS login returned an empty access token.' >&2; exit 1; }

echo '4/4 JWT opens protected CMS endpoint'
AUTH_STATUS=$(curl -sS -o "$TMP_ORDERS" -w '%{http_code}'   "$BASE_URL/api/v1/cms/orders?pageSize=1"   -H "Authorization: Bearer $ACCESS_TOKEN")
[[ "$AUTH_STATUS" == "200" ]] || {
  echo "Protected CMS endpoint returned HTTP $AUTH_STATUS." >&2
  cat "$TMP_ORDERS" >&2
  exit 1
}

echo 'PASS: STAGING admin backend handoff is healthy; new admin origin, login, JWT authorization, and protected CMS API all pass.'
