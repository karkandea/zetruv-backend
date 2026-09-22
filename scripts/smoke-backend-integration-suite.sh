#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

scripts=(
  smoke-catalog-pdp-contract.sh
  smoke-payment-recovery-concurrency.sh
  smoke-payment-reconciliation.sh
  smoke-provider-mapping-runtime.sh
  smoke-cms-crud.sh
  smoke-openapi-security.sh
  smoke-auto-id-fulfillment.sh
  smoke-manual-login-credentials.sh
  smoke-payment-inventory-hardening.sh
  smoke-shipping.sh
  smoke-shipment-fulfillment.sh
)

for script in "${scripts[@]}"; do
  echo
  echo "============================================================"
  echo "BACKEND INTEGRATION: $script"
  echo "============================================================"
  bash "scripts/$script"
done

echo
echo "PASS: critical backend integration suite"
