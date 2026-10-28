#!/usr/bin/env python3
"""Read-only, sanitized Zetruv DB password reuse audit, run as root on VPS."""

import json
import os
import re
import subprocess
from pathlib import Path


ENVIRONMENTS = {
    "DEV": ("/opt/zetruv-backend-dev", "zetruv-dev-postgres-1", "zetruv-dev-api-1"),
    "STAGING": ("/opt/zetruv-backend-staging", "zetruv-staging-postgres-1", "zetruv-staging-api-1"),
    "LEGACY": ("/opt/zetruv-backend", "zetruv-backend-postgres-1", "zetruv-backend-api-1"),
}


def run(command):
    try:
        return subprocess.run(command, text=True, capture_output=True, timeout=12, check=False)
    except (OSError, subprocess.TimeoutExpired):
        return None


def env_file_value(path, key):
    try:
        lines = path.read_text().splitlines()
    except OSError:
        return None
    for line in lines:
        if line.startswith(key + "="):
            value = line.split("=", 1)[1].strip()
            if len(value) >= 2 and value[0] == value[-1] and value[0] in "\"'":
                value = value[1:-1]
            return value or None
    return None


def container_env(container):
    result = run(["docker", "inspect", "-f", "{{json .Config.Env}}", container])
    if result is None or result.returncode != 0:
        return None
    try:
        entries = json.loads(result.stdout)
        return dict(entry.split("=", 1) for entry in entries if "=" in entry)
    except (ValueError, TypeError):
        return None


def pg_password_from_connection_string(value):
    match = re.search(r"(?:^|;)\s*Password=([^;]+)", value or "", re.IGNORECASE)
    return match.group(1).strip() if match else None


def tracked_password(repo):
    result = run(["git", "-c", f"safe.directory={repo}", "-C", repo,
                  "show", "HEAD:src/Zetruv.Api/appsettings.json"])
    if result is None or result.returncode != 0:
        return None
    try:
        data = json.loads(result.stdout)
        return pg_password_from_connection_string(data["ConnectionStrings"]["Postgres"])
    except (ValueError, KeyError, TypeError):
        return None


def auth_check(container):
    command = ('PGPASSWORD="$POSTGRES_PASSWORD" psql -h 127.0.0.1 '
               '-U "$POSTGRES_USER" -d "$POSTGRES_DB" -X -w -Atqc "SELECT 1"')
    result = run(["docker", "exec", container, "sh", "-c", command])
    return result is not None and result.returncode == 0 and result.stdout.strip() == "1"


def status(value):
    return "YES" if value else "NO"


if os.geteuid() != 0:
    raise SystemExit("ERROR: Run only in the already-open root shell; no audit performed.")

known_public_passwords = {p for repo, _, _ in ENVIRONMENTS.values()
                          if (p := tracked_password(repo))}
print("ZETRUV P0 AUDIT — READ ONLY; NO SECRETS PRINTED")
print("Tracked appsettings passwords discovered:", len(known_public_passwords))
observed = {}

for label, (repo, postgres_container, api_container) in ENVIRONMENTS.items():
    secret = env_file_value(Path(repo) / ".env", "POSTGRES_PASSWORD")
    db_env = container_env(postgres_container)
    api_env = container_env(api_container)
    runtime_db = db_env.get("POSTGRES_PASSWORD") if db_env is not None else None
    runtime_api = pg_password_from_connection_string(
        api_env.get("ConnectionStrings__Postgres") if api_env is not None else None)
    observed[label] = runtime_db
    print(f"{label}: env_present={status(secret is not None)} "
          f"db_running={status(db_env is not None)} api_running={status(api_env is not None)}")
    print(f"{label}: file_matches_db={status(bool(secret and runtime_db and secret == runtime_db))} "
          f"db_matches_api={status(bool(runtime_db and runtime_api and runtime_db == runtime_api))} "
          f"db_auth_select1={status(auth_check(postgres_container))}")
    print(f"{label}: MATCH_TRACKED_PUBLIC_PASSWORD="
          f"{status(bool(runtime_db and runtime_db in known_public_passwords))} "
          f"MATCH_COMPOSE_STATIC_FALLBACK={status(runtime_db == 'zetruv')}")

nonempty = [password for password in observed.values() if password]
print("CROSS_ENV: all_three_secrets_present=", status(len(nonempty) == 3))
print("CROSS_ENV: passwords_all_distinct=", status(len(nonempty) == 3 and len(set(nonempty)) == 3))
print("LIMITATION: Checks current runtime and checked-in defaults, not every historical password.")
