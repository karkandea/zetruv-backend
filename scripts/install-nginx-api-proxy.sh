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

echo '=== LIVE DOMAIN SMOKE ==='
curl -fsS -o /tmp/zetruv-live-homepage.json -w 'API HTTP %{http_code}\n' "https://${DOMAIN}/api/v1/homepage"
python3 - <<'PY'
import json
with open('/tmp/zetruv-live-homepage.json') as f:
    data = json.load(f)
assert isinstance(data, dict)
print('PASS: /api/v1/homepage returned JSON through Nginx')
PY
curl -fsS -o /dev/null -w 'Frontend HTTP %{http_code}\n' "https://${DOMAIN}/"

echo "PASS: live Nginx API proxy installed for https://${DOMAIN}/api/"
echo "Backup kept at: $CONFIG_BACKUP"
