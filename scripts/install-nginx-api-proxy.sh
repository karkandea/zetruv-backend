#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

DOMAIN="zetruv.dualangka.com"
SNIPPET_SOURCE="deploy/nginx/zetruv-api.conf"
SNIPPET_TARGET="/etc/nginx/snippets/zetruv-api.conf"
INCLUDE_LINE="include /etc/nginx/snippets/zetruv-api.conf;"
STAMP="$(date +%Y%m%d%H%M%S)"

if [[ "${EUID:-$(id -u)}" -ne 0 ]]; then
  echo 'Run this installer as root so it can update /etc/nginx.' >&2
  exit 1
fi

command -v nginx >/dev/null 2>&1 || { echo 'nginx is not installed.' >&2; exit 1; }
command -v python3 >/dev/null 2>&1 || { echo 'python3 is required.' >&2; exit 1; }
[[ -f "$SNIPPET_SOURCE" ]] || { echo "Missing $SNIPPET_SOURCE" >&2; exit 1; }

echo '=== PRECHECK BACKEND ==='
curl -fsS http://127.0.0.1:8080/health; echo

mapfile -t CANDIDATES < <(
  {
    find /etc/nginx/sites-enabled -maxdepth 1 -type f -o -type l 2>/dev/null || true
    find /etc/nginx/conf.d -maxdepth 1 -type f -name '*.conf' 2>/dev/null || true
  } | while read -r path; do realpath "$path" 2>/dev/null || true; done | sort -u
)

[[ ${#CANDIDATES[@]} -gt 0 ]] || { echo 'No active Nginx site files found.' >&2; exit 1; }

TARGET_FILE=$(python3 - "$DOMAIN" "${CANDIDATES[@]}" <<'PY'
import pathlib, re, sys

domain = sys.argv[1]
paths = [pathlib.Path(p) for p in sys.argv[2:]]
server_start = re.compile(r'\bserver\s*\{', re.I)
server_name = re.compile(r'\bserver_name\s+[^;]*\b' + re.escape(domain) + r'\b[^;]*;', re.I)
listen_443 = re.compile(r'\blisten\s+[^;]*(?<!\d)443(?!\d)[^;]*;', re.I)

matches = []
for path in paths:
    try:
        text = path.read_text()
    except (OSError, UnicodeDecodeError):
        continue
    for m in server_start.finditer(text):
        brace = text.find('{', m.start())
        depth = 0
        end = None
        for i in range(brace, len(text)):
            if text[i] == '{':
                depth += 1
            elif text[i] == '}':
                depth -= 1
                if depth == 0:
                    end = i + 1
                    break
        if end is None:
            continue
        block = text[m.start():end]
        if server_name.search(block) and listen_443.search(block):
            matches.append((str(path), m.start(), end))

if len(matches) != 1:
    print(f'Expected exactly one active HTTPS server block for {domain}, found {len(matches)}.', file=sys.stderr)
    for path, _, _ in matches:
        print(f'  match: {path}', file=sys.stderr)
    sys.exit(2)

print(matches[0][0])
PY
)

echo "Target Nginx config: $TARGET_FILE"
CONFIG_BACKUP="${TARGET_FILE}.zetruv-api-backup-${STAMP}"
cp -a "$TARGET_FILE" "$CONFIG_BACKUP"

SNIPPET_BACKUP=""
if [[ -f "$SNIPPET_TARGET" ]]; then
  SNIPPET_BACKUP="${SNIPPET_TARGET}.backup-${STAMP}"
  cp -a "$SNIPPET_TARGET" "$SNIPPET_BACKUP"
fi
mkdir -p /etc/nginx/snippets
cp "$SNIPPET_SOURCE" "$SNIPPET_TARGET"

python3 - "$TARGET_FILE" "$DOMAIN" "$INCLUDE_LINE" <<'PY'
import pathlib, re, sys

path = pathlib.Path(sys.argv[1])
domain = sys.argv[2]
include_line = sys.argv[3]
text = path.read_text()
server_start = re.compile(r'\bserver\s*\{', re.I)
server_name = re.compile(r'\bserver_name\s+[^;]*\b' + re.escape(domain) + r'\b[^;]*;', re.I)
listen_443 = re.compile(r'\blisten\s+[^;]*(?<!\d)443(?!\d)[^;]*;', re.I)

found = None
for m in server_start.finditer(text):
    brace = text.find('{', m.start())
    depth = 0
    end = None
    for i in range(brace, len(text)):
        if text[i] == '{':
            depth += 1
        elif text[i] == '}':
            depth -= 1
            if depth == 0:
                end = i + 1
                break
    if end is None:
        continue
    block = text[m.start():end]
    if server_name.search(block) and listen_443.search(block):
        if found is not None:
            raise SystemExit('Multiple matching HTTPS server blocks found while editing.')
        found = (m.start(), end)

if found is None:
    raise SystemExit('Matching HTTPS server block disappeared before edit.')

start, end = found
block = text[start:end]
if include_line in block:
    print('Nginx API include already installed; leaving server block unchanged.')
    raise SystemExit(0)

closing = end - 1
new_text = text[:closing] + f'    {include_line}\n' + text[closing:]
path.write_text(new_text)
print('Inserted API proxy include into HTTPS server block.')
PY

rollback() {
  echo 'Rolling back Nginx changes...' >&2
  cp -a "$CONFIG_BACKUP" "$TARGET_FILE"
  if [[ -n "$SNIPPET_BACKUP" ]]; then
    cp -a "$SNIPPET_BACKUP" "$SNIPPET_TARGET"
  else
    rm -f "$SNIPPET_TARGET"
  fi
  nginx -t >/dev/null 2>&1 || true
}

if ! nginx -t; then
  rollback
  echo 'FAIL: nginx -t rejected the API proxy configuration; original config restored.' >&2
  exit 1
fi

systemctl reload nginx

validate_json_response() {
  local label="$1"
  local body_file="$2"
  local header_file="$3"
  python3 - "$label" "$body_file" "$header_file" <<'PY'
import json, pathlib, sys
label, body_path, header_path = sys.argv[1:]
body = pathlib.Path(body_path).read_bytes()
headers = pathlib.Path(header_path).read_text(errors='replace')
print(f'{label} bytes={len(body)}')
try:
    data = json.loads(body)
except Exception as exc:
    print(f'FAIL: {label} did not return valid JSON: {exc}', file=sys.stderr)
    print('--- response headers ---', file=sys.stderr)
    print(headers[-2000:], file=sys.stderr)
    print('--- first 800 response bytes ---', file=sys.stderr)
    print(body[:800].decode('utf-8', errors='replace'), file=sys.stderr)
    sys.exit(1)
if not isinstance(data, dict):
    print(f'FAIL: {label} JSON root is not an object.', file=sys.stderr)
    sys.exit(1)
print(f'PASS: {label} returned JSON')
PY
}

echo '=== DIRECT KESTREL SMOKE ==='
DIRECT_CODE=$(curl -sS -D /tmp/zetruv-direct.headers -o /tmp/zetruv-direct.json -w '%{http_code}' \
  'http://127.0.0.1:8080/api/v1/homepage')
echo "Direct API HTTP $DIRECT_CODE"
[[ "$DIRECT_CODE" == "200" ]] || { cat /tmp/zetruv-direct.headers; exit 1; }
validate_json_response 'direct Kestrel /api/v1/homepage' /tmp/zetruv-direct.json /tmp/zetruv-direct.headers

echo '=== LOCAL NGINX HTTPS SMOKE ==='
LOCAL_CODE=$(curl -sS --resolve "${DOMAIN}:443:127.0.0.1" \
  -D /tmp/zetruv-local-nginx.headers -o /tmp/zetruv-local-nginx.json -w '%{http_code}' \
  "https://${DOMAIN}/api/v1/homepage")
echo "Local Nginx API HTTP $LOCAL_CODE"
[[ "$LOCAL_CODE" == "200" ]] || { cat /tmp/zetruv-local-nginx.headers; exit 1; }
validate_json_response 'local Nginx /api/v1/homepage' /tmp/zetruv-local-nginx.json /tmp/zetruv-local-nginx.headers

echo '=== PUBLIC DOMAIN SMOKE ==='
PUBLIC_CODE=$(curl -sS -D /tmp/zetruv-public.headers -o /tmp/zetruv-public.json -w '%{http_code}' \
  "https://${DOMAIN}/api/v1/homepage")
echo "Public API HTTP $PUBLIC_CODE"
[[ "$PUBLIC_CODE" == "200" ]] || { cat /tmp/zetruv-public.headers; exit 1; }
validate_json_response 'public domain /api/v1/homepage' /tmp/zetruv-public.json /tmp/zetruv-public.headers

FRONTEND_CODE=$(curl -sS -o /dev/null -w '%{http_code}' "https://${DOMAIN}/")
echo "Frontend HTTP $FRONTEND_CODE"
[[ "$FRONTEND_CODE" == "200" ]]

echo "PASS: live Nginx API proxy installed for https://${DOMAIN}/api/"
echo "Backup kept at: $CONFIG_BACKUP"
