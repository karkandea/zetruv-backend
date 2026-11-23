#!/usr/bin/env bash
# DEV ONLY. Run interactively on the Zetruv VPS:
#   sudo bash /opt/zetruv-backend-dev/scripts/activate-midtrans-sandbox.sh
# No secret is accepted as a command-line argument, printed, committed or logged.
set -euo pipefail
umask 077
cd /opt/zetruv-backend-dev
[[ -t 0 ]] || { echo "Open an interactive terminal on the VPS." >&2; exit 1; }
[[ -f .env && -f docker-compose.yml ]] || { echo "Zetruv DEV compose is missing." >&2; exit 1; }
if [[ $(grep '^ZETRUV_ENVIRONMENT=' .env | cut -d= -f2-) != dev ]]; then
  echo "Refusing to activate Midtrans outside DEV." >&2
  exit 1
fi

read -r -s -p 'Paste Midtrans SANDBOX Server Key: ' MIDTRANS_INPUT_SERVER_KEY
printf '\n'
[[ "$MIDTRANS_INPUT_SERVER_KEY" == SB-Mid-server-* ]] || {
  echo 'Invalid SANDBOX key prefix.' >&2
  exit 1
}
read -r -s -p 'Optional Midtrans Signature Secret Key (press Enter to use verified GET Status): ' MIDTRANS_INPUT_SIGNATURE_KEY
printf '\n'
export MIDTRANS_INPUT_SERVER_KEY MIDTRANS_INPUT_SIGNATURE_KEY
backup=$(mktemp .env.midtrans-preflight.XXXXXXXX)
cp -p .env "$backup"
chmod 600 "$backup"
restore() {
  echo 'Midtrans readiness failed. Restoring previous DEV payment configuration.' >&2
  cp -p "$backup" .env
  docker compose up -d --no-deps --force-recreate api >&2 || true
}
cleanup() { rm -f "$backup"; unset MIDTRANS_INPUT_SERVER_KEY MIDTRANS_INPUT_SIGNATURE_KEY; }
trap cleanup EXIT

python3 - <<'PY'
import os,pathlib,tempfile
p=pathlib.Path(".env")
lines=p.read_text().splitlines()
values={
    "PAYMENTS_PROVIDER":"midtrans",
    "PAYMENTS_MIDTRANS_ENABLED":"true",
    "PAYMENTS_MIDTRANS_MODE":"Sandbox",
    "PAYMENTS_MIDTRANS_SERVER_KEY":os.environ["MIDTRANS_INPUT_SERVER_KEY"],
    "PAYMENTS_MIDTRANS_FINISH_URL":"https://dev.zetruv.com/payment",
}
if os.environ.get("MIDTRANS_INPUT_SIGNATURE_KEY"):
    values["PAYMENTS_MIDTRANS_SIGNATURE_SECRET_KEY"]=os.environ["MIDTRANS_INPUT_SIGNATURE_KEY"]
seen=set()
result=[]
for line in lines:
    key=line.partition("=")[0].strip()
    if key in values and not line.lstrip().startswith("#"):
        result.append(key+"="+values[key]);seen.add(key)
    else:
        result.append(line)
for key,value in values.items():
    if key not in seen:result.append(key+"="+value)
fd,tmp=tempfile.mkstemp(dir=".",prefix=".env.midtrans-")
with os.fdopen(fd,"w") as file:
    file.write("\n".join(result)+"\n")
os.chmod(tmp,0o600)
os.replace(tmp,p)
PY
unset MIDTRANS_INPUT_SERVER_KEY MIDTRANS_INPUT_SIGNATURE_KEY

echo 'Recreating Zetruv DEV API (staging/production unchanged).'
if ! docker compose up -d --no-deps --force-recreate api; then
  restore
  exit 1
fi

api_port=$(sed -n 's/^API_PORT=//p' .env | tail -n1)
[[ $api_port =~ ^[0-9]{2,5}$ ]] || { restore; exit 1; }

ready=0
for attempt in $(seq 1 20); do
  if curl -fsS --max-time 3 "http://127.0.0.1:$api_port/api/v1/checkout/payment-options" |
     python3 -c 'import json,sys;d=json.load(sys.stdin);sys.exit(0 if d.get("canAcceptLivePayments") is True and d.get("isSandbox") is True and any(m.get("code")=="MIDTRANS_SNAP" and m.get("paymentFee")==0 for m in d.get("availableMethods",[])) else 1)' 2>/dev/null; then
    ready=1
    break
  fi
  sleep 2
done
if [[ "$ready" != 1 ]]; then
  restore
  exit 1
fi
echo 'PASS: DEV Midtrans Sandbox is selectable and adds no customer surcharge.'
echo 'Next: configure Midtrans dashboard Payment Notification URL and test an actual sandbox payment.'
