#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

# Removing a PostgreSQL test container without its anonymous data volume leaks disk space.
bad_pattern='docker rm -f'
if grep -nF "$bad_pattern " scripts/smoke-*.sh; then
  echo 'FAIL: smoke tests must remove their disposable anonymous volumes (docker rm -fv).' >&2
  exit 1
fi

C="zetruv-volume-cleanup-$$"
cleanup() { docker rm -fv "$C" >/dev/null 2>&1 || true; }
trap cleanup EXIT
docker run -d --name "$C" --entrypoint sleep postgres:17-alpine 300 >/dev/null
volume=$(docker inspect "$C" --format '{{range .Mounts}}{{if eq .Type "volume"}}{{.Name}}{{end}}{{end}}')
[[ "$volume" =~ ^[0-9a-f]{64}$ ]] || {
  echo "FAIL: expected a disposable anonymous postgres volume, got '$volume'." >&2
  exit 1
}
docker rm -fv "$C" >/dev/null
if docker volume inspect "$volume" >/dev/null 2>&1; then
  echo "FAIL: anonymous postgres volume $volume leaked after container removal." >&2
  exit 1
fi
echo 'PASS: all smoke scripts remove anonymous volumes; disposable postgres volume does not leak'
