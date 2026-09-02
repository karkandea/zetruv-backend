#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ -f .env ]]; then
  echo '.env already exists; leaving it unchanged.'
  exit 0
fi

command -v openssl >/dev/null 2>&1 || {
  echo 'openssl is required to generate staging secrets.' >&2
  exit 1
}

umask 077
POSTGRES_PASSWORD=$(openssl rand -hex 24)
JWT_KEY=$(openssl rand -base64 48 | tr -d '\n' | tr '+/' '-_')
CMS_ADMIN_PASSWORD=$(openssl rand -base64 24 | tr -d '\n' | tr '+/' '-_')
WEBHOOK_SECRET=$(openssl rand -hex 32)

cat > .env <<EOF
ASPNETCORE_ENVIRONMENT=Staging
POSTGRES_DB=zetruv
POSTGRES_USER=zetruv
POSTGRES_PASSWORD=$POSTGRES_PASSWORD
JWT_KEY=$JWT_KEY
CMS_ADMIN_EMAIL=admin@zetruv.com
CMS_ADMIN_PASSWORD=$CMS_ADMIN_PASSWORD
FRONTEND_ORIGIN=https://zetruv.dualangka.com
CMS_ORIGIN=https://zetruv.dualangka.com
API_PORT=8080
ORDER_ACCESS_TOKEN_LIFETIME_MINUTES=1440
FORWARDED_HEADERS_ENABLED=true
PAYMENTS_PROVIDER=mock
PAYMENTS_MOCK_WEBHOOK_SECRET=$WEBHOOK_SECRET
GAME_ACCOUNT_VALIDATION_PROVIDER=mock
SHIPPING_PROVIDER=mock
EOF

chmod 600 .env

echo 'Created staging .env with generated secrets.'
echo 'Admin email: admin@zetruv.com'
echo 'Admin password is stored only in .env.'
echo "Read it when needed with: grep '^CMS_ADMIN_PASSWORD=' .env"
echo 'Staging intentionally uses mock providers; do not switch ASPNETCORE_ENVIRONMENT to Production until real providers are configured.'
