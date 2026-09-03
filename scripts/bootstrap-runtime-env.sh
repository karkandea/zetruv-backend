#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

ENVIRONMENT="${1:-}"
case "$ENVIRONMENT" in
  dev)
    ASPNET_ENV=Development
    DB_NAME=zetruv_dev
    API_PORT=8081
    API_DOMAIN=api-dev.zetruv.dualangka.com
    PROJECT_NAME=zetruv-dev
    CMS_EMAIL=admin-dev@zetruv.com
    ;;
  staging)
    ASPNET_ENV=Staging
    DB_NAME=zetruv_staging
    API_PORT=8082
    API_DOMAIN=api-staging.zetruv.dualangka.com
    PROJECT_NAME=zetruv-staging
    CMS_EMAIL=admin-staging@zetruv.com
    ;;
  *)
    echo 'Usage: bash scripts/bootstrap-runtime-env.sh <dev|staging>' >&2
    exit 2
    ;;
esac

if [[ -f .env ]]; then
  current=$(sed -n 's/^ZETRUV_ENVIRONMENT=//p' .env | tail -n 1)
  if [[ "$current" == "$ENVIRONMENT" ]]; then
    echo ".env already exists for $ENVIRONMENT; leaving secrets unchanged."
    exit 0
  fi
  echo ".env already exists for '${current:-unknown}', refusing to overwrite it with $ENVIRONMENT." >&2
  exit 1
fi

command -v openssl >/dev/null 2>&1 || { echo 'openssl is required.' >&2; exit 1; }
umask 077
POSTGRES_PASSWORD=$(openssl rand -hex 24)
JWT_KEY=$(openssl rand -base64 48 | tr -d '\n' | tr '+/' '-_')
CMS_ADMIN_PASSWORD=$(openssl rand -base64 24 | tr -d '\n' | tr '+/' '-_')
WEBHOOK_SECRET=$(openssl rand -hex 32)

cat > .env <<EOF
ZETRUV_ENVIRONMENT=$ENVIRONMENT
COMPOSE_PROJECT_NAME=$PROJECT_NAME
ASPNETCORE_ENVIRONMENT=$ASPNET_ENV
POSTGRES_DB=$DB_NAME
POSTGRES_USER=zetruv
POSTGRES_PASSWORD=$POSTGRES_PASSWORD
JWT_KEY=$JWT_KEY
CMS_ADMIN_EMAIL=$CMS_EMAIL
CMS_ADMIN_PASSWORD=$CMS_ADMIN_PASSWORD
FRONTEND_ORIGIN=https://zetruv.dualangka.com
CMS_ORIGIN=https://admin.zetruv.dualangka.com
API_PORT=$API_PORT
API_DOMAIN=$API_DOMAIN
ORDER_ACCESS_TOKEN_LIFETIME_MINUTES=1440
FORWARDED_HEADERS_ENABLED=true
PAYMENTS_PROVIDER=mock
PAYMENTS_MOCK_WEBHOOK_SECRET=$WEBHOOK_SECRET
GAME_ACCOUNT_VALIDATION_PROVIDER=mock
SHIPPING_PROVIDER=mock
EOF
chmod 600 .env

echo "Created isolated $ENVIRONMENT runtime config."
echo "Compose project: $PROJECT_NAME"
echo "Database: $DB_NAME"
echo "Local API bind: 127.0.0.1:$API_PORT"
echo "Public API domain: https://$API_DOMAIN"
echo "CMS admin email: $CMS_EMAIL"
echo 'Generated passwords/secrets are stored only in this clone .env (mode 600).'
