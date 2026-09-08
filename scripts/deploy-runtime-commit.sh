#!/usr/bin/env bash
set -euo pipefail

ENVIRONMENT="${1:-}"
EXPECTED_SHA="${2:-}"

case "$ENVIRONMENT" in
  dev)
    REPO_DIR=/opt/zetruv-backend-dev
    BRANCH=dev
    PUBLIC_HEALTH=https://api-dev.zetruv.dualangka.com/health
    ;;
  staging)
    REPO_DIR=/opt/zetruv-backend-staging
    BRANCH=main
    PUBLIC_HEALTH=https://api-staging.zetruv.dualangka.com/health
    ;;
  *)
    echo 'Usage: deploy-runtime-commit.sh <dev|staging> <40-char-commit-sha>' >&2
    exit 2
    ;;
esac

[[ "$EXPECTED_SHA" =~ ^[0-9a-f]{40}$ ]] || {
  echo 'Expected a full 40-character commit SHA.' >&2
  exit 2
}

[[ $EUID -eq 0 ]] || {
  echo 'This deployment entrypoint must run as root.' >&2
  exit 1
}

[[ -d "$REPO_DIR/.git" ]] || {
  echo "Runtime clone missing: $REPO_DIR" >&2
  exit 1
}

exec 9>"/run/lock/zetruv-backend-${ENVIRONMENT}.lock"
flock -x 9

cd "$REPO_DIR"

CURRENT_BRANCH=$(git branch --show-current)
[[ "$CURRENT_BRANCH" == "$BRANCH" ]] || {
  echo "Unexpected runtime branch in $REPO_DIR: $CURRENT_BRANCH (expected $BRANCH)" >&2
  exit 1
}

PREVIOUS_SHA=$(git rev-parse HEAD)

echo "Fetching origin/$BRANCH..."
git fetch origin "$BRANCH"
REMOTE_SHA=$(git rev-parse FETCH_HEAD)

if [[ "$REMOTE_SHA" != "$EXPECTED_SHA" ]]; then
  echo "Skipping stale deployment: event=$EXPECTED_SHA current-origin/$BRANCH=$REMOTE_SHA"
  exit 0
fi

rollback() {
  local failed_status=$?
  trap - ERR
  echo "Deployment failed; rolling $ENVIRONMENT back to $PREVIOUS_SHA" >&2
  git reset --hard "$PREVIOUS_SHA"
  if ! bash scripts/deploy-runtime-env.sh "$ENVIRONMENT"; then
    echo "WARNING: automatic rollback deployment also failed." >&2
  fi
  exit "$failed_status"
}
trap rollback ERR

git reset --hard "$EXPECTED_SHA"
bash scripts/deploy-runtime-env.sh "$ENVIRONMENT"
curl -fsS "$PUBLIC_HEALTH" >/dev/null

trap - ERR
echo "PASS: $ENVIRONMENT deployed exact commit $EXPECTED_SHA and public health is OK"
