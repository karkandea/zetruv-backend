#!/usr/bin/env bash
set -euo pipefail

ENV_FILE="${1:-.env}"
[[ -f "$ENV_FILE" ]] || {
  echo "Runtime env file not found: $ENV_FILE" >&2
  exit 1
}

if grep -q '^FULFILLMENT_AUTO_ID_PROVIDER=' "$ENV_FILE"; then
  TMP_FILE=$(mktemp)
  trap 'rm -f "$TMP_FILE"' EXIT
  grep -v '^FULFILLMENT_AUTO_ID_PROVIDER=' "$ENV_FILE" > "$TMP_FILE" || true
  cat "$TMP_FILE" > "$ENV_FILE"
  chmod 600 "$ENV_FILE"
  echo "Removed legacy FULFILLMENT_AUTO_ID_PROVIDER from $ENV_FILE."
fi
