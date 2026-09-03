#!/usr/bin/env bash
set -euo pipefail

[[ ${EUID:-$(id -u)} -eq 0 ]] || { echo 'Run as root.' >&2; exit 1; }

REPO_URL="${REPO_URL:-https://github.com/karkandea/zetruv-backend.git}"
DEV_DIR="${DEV_DIR:-/opt/zetruv-backend-dev}"
STAGING_DIR="${STAGING_DIR:-/opt/zetruv-backend-staging}"

command -v git >/dev/null 2>&1 || { echo 'git is required.' >&2; exit 1; }

clone_if_missing() {
  local dir="$1"
  local ref="$2"
  if [[ -d "$dir/.git" ]]; then
    echo "$dir already exists; leaving repository contents untouched."
    return
  fi
  if [[ -e "$dir" ]]; then
    echo "$dir exists but is not a Git repository; refusing to overwrite it." >&2
    exit 1
  fi
  git clone --branch "$ref" --single-branch "$REPO_URL" "$dir"
}

clone_if_missing "$DEV_DIR" dev
clone_if_missing "$STAGING_DIR" main

bash "$DEV_DIR/scripts/bootstrap-runtime-env.sh" dev
bash "$STAGING_DIR/scripts/bootstrap-runtime-env.sh" staging

cat <<EOF
PASS: isolated VPS backend layout prepared.
DEV:     $DEV_DIR  -> branch dev  -> 127.0.0.1:8081
STAGING: $STAGING_DIR -> branch main -> 127.0.0.1:8082

The existing /opt/zetruv-backend stack is intentionally untouched.
Deploy each environment only after its branch is synced, then install its Nginx vhost.
EOF
