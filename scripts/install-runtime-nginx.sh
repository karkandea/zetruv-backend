#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

ENVIRONMENT="${1:-}"
case "$ENVIRONMENT" in
  dev|staging) ;;
  *) echo 'Usage: sudo bash scripts/install-runtime-nginx.sh <dev|staging>' >&2; exit 2 ;;
esac

[[ ${EUID:-$(id -u)} -eq 0 ]] || { echo 'Run as root.' >&2; exit 1; }
[[ -f .env ]] || { echo 'Missing .env.' >&2; exit 1; }
get_env() { sed -n "s/^${1}=//p" .env | tail -n 1; }

[[ "$(get_env ZETRUV_ENVIRONMENT)" == "$ENVIRONMENT" ]] || { echo '.env environment mismatch.' >&2; exit 1; }
DOMAIN=$(get_env API_DOMAIN)
API_PORT=$(get_env API_PORT)
[[ -n "$DOMAIN" && -n "$API_PORT" ]] || { echo 'API_DOMAIN/API_PORT missing from .env.' >&2; exit 1; }

for cmd in nginx curl dig; do command -v "$cmd" >/dev/null 2>&1 || { echo "$cmd is required." >&2; exit 1; }; done

DNS_CF=$(dig +short @1.1.1.1 "$DOMAIN" A | tail -n 1)
DNS_GOOGLE=$(dig +short @8.8.8.8 "$DOMAIN" A | tail -n 1)
echo "DNS Cloudflare: ${DNS_CF:-<empty>}"
echo "DNS Google: ${DNS_GOOGLE:-<empty>}"
[[ -n "$DNS_CF" && "$DNS_CF" == "$DNS_GOOGLE" ]] || { echo "Public DNS for $DOMAIN is not ready/consistent." >&2; exit 2; }

curl -fsS "http://127.0.0.1:$API_PORT/health" >/dev/null || { echo "Backend is not healthy on 127.0.0.1:$API_PORT." >&2; exit 1; }

AVAILABLE="/etc/nginx/sites-available/zetruv-api-$ENVIRONMENT"
ENABLED="/etc/nginx/sites-enabled/zetruv-api-$ENVIRONMENT"
STAMP=$(date +%Y%m%d%H%M%S)
BACKUP="${AVAILABLE}.backup-${STAMP}"
HAD_CONFIG=0
[[ -f "$AVAILABLE" ]] && { cp -a "$AVAILABLE" "$BACKUP"; HAD_CONFIG=1; }

rollback() {
  set +e
  if [[ "$HAD_CONFIG" == 1 && -f "$BACKUP" ]]; then cp -a "$BACKUP" "$AVAILABLE"; else rm -f "$AVAILABLE" "$ENABLED"; fi
  nginx -t >/dev/null 2>&1 && systemctl reload nginx >/dev/null 2>&1
}
trap rollback ERR

wait_http_health() {
  local code=""
  for _ in $(seq 1 30); do
    code=$(curl --noproxy '*' -sS -H "Host: $DOMAIN" -o /tmp/zetruv-api-env-health.txt -w '%{http_code}' "http://127.0.0.1/health" || true)
    if [[ "$code" == 200 ]]; then
      echo "$code"
      return 0
    fi
    sleep 0.2
  done
  echo "${code:-000}"
  return 1
}

wait_https_health() {
  local code=""
  for _ in $(seq 1 30); do
    code=$(curl --noproxy '*' --resolve "${DOMAIN}:443:127.0.0.1" -sS -o /tmp/zetruv-api-env-health-https.txt -w '%{http_code}' "https://${DOMAIN}/health" || true)
    if [[ "$code" == 200 ]]; then
      echo "$code"
      return 0
    fi
    sleep 0.2
  done
  echo "${code:-000}"
  return 1
}

cat > "$AVAILABLE" <<NGINX
server {
    listen 80;
    listen [::]:80;
    server_name $DOMAIN;

    client_max_body_size 20m;

    location / {
        proxy_pass http://127.0.0.1:$API_PORT;
        proxy_http_version 1.1;
        proxy_set_header Host \$host;
        proxy_set_header X-Real-IP \$remote_addr;
        proxy_set_header X-Forwarded-For \$proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto \$scheme;
        proxy_set_header X-Forwarded-Host \$host;
        proxy_set_header X-Forwarded-Port \$server_port;
        proxy_connect_timeout 10s;
        proxy_read_timeout 60s;
        proxy_send_timeout 60s;
    }
}
NGINX
ln -sfn "$AVAILABLE" "$ENABLED"
nginx -t
systemctl reload nginx

HTTP_CODE=$(wait_http_health) || { echo "Local Nginx HTTP health returned $HTTP_CODE after reload wait." >&2; exit 1; }
echo "PASS: local HTTP proxy for $DOMAIN -> 127.0.0.1:$API_PORT"

if command -v certbot >/dev/null 2>&1; then
  certbot --nginx -d "$DOMAIN" --non-interactive --agree-tos --redirect --register-unsafely-without-email
  nginx -t
  systemctl reload nginx
  HTTPS_CODE=$(wait_https_health) || { echo "Local HTTPS health returned $HTTPS_CODE after reload wait." >&2; exit 1; }
  echo "PASS: https://$DOMAIN is live and proxies only to $ENVIRONMENT backend"
else
  echo "PASS: http://$DOMAIN is live. certbot not installed; HTTPS not configured."
fi

trap - ERR
