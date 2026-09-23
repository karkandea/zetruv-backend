#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

TMP=$(mktemp -d)
C="zetruv-pg-customer-auth-$$"
PID=""
cleanup() {
  [[ -n "$PID" ]] && kill "$PID" >/dev/null 2>&1 || true
  docker rm -f "$C" >/dev/null 2>&1 || true
  rm -rf "$TMP"
}
trap cleanup EXIT

dotnet build src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --nologo >/dev/null
docker run -d --name "$C" -e POSTGRES_USER=zetruv \
  -e POSTGRES_PASSWORD=zetruvtest -e POSTGRES_DB=zetruv_auth_smoke \
  -p 127.0.0.1::5432 postgres:17-alpine >/dev/null
until docker exec "$C" pg_isready -U zetruv -d zetruv_auth_smoke >/dev/null 2>&1; do sleep 1; done
DB_PORT=$(docker port "$C" 5432/tcp | awk -F: 'NR==1 {print $NF}')
API_PORT=$(python3 -c 'import socket; s=socket.socket();s.bind(("127.0.0.1",0));print(s.getsockname()[1]);s.close()')
export ConnectionStrings__Postgres="Host=127.0.0.1;Port=$DB_PORT;Database=zetruv_auth_smoke;Username=zetruv;Password=zetruvtest"
export Jwt__Key='0123456789abcdef0123456789abcdef'
export CmsAdmin__Email='customer-auth-admin@zetruv.test'
export CmsAdmin__Password='CustomerAuthAdmin123!'
export CustomerEmail__Provider=capture
export CustomerEmail__CaptureDirectory="$TMP/emails"
export CustomerAuth__FrontendBaseUrl=https://dev.zetruv.com
export CustomerAuth__ResendCooldownSeconds=1
export Payments__Provider=mock
export Payments__Mock__WebhookSecret=smoke-secret
export Shipping__Provider=mock
export GameAccountValidation__Provider=mock
export ASPNETCORE_ENVIRONMENT=Development
export TEST_API="http://127.0.0.1:$API_PORT"
export TEST_CONTAINER="$C"
export TEST_MAIL="$TMP/emails"
dotnet run --project src/Zetruv.Api/Zetruv.Api.csproj --configuration Release \
  --no-build --urls="$TEST_API" > "$TMP/api.log" 2>&1 & PID=$!
ready=false
for _ in $(seq 1 50); do
  if curl -fsS "$TEST_API/health" >/dev/null 2>&1; then ready=true; break; fi
  if ! kill -0 "$PID" 2>/dev/null; then break; fi
  sleep 1
done
if [[ "$ready" != true ]]; then
  echo 'FAIL: customer auth test API did not start' >&2
  tail -45 "$TMP/api.log" >&2
  exit 1
fi

python3 - <<'PY'
import glob, html, json, os, re, subprocess, time, urllib.error, urllib.parse, urllib.request
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path
BASE=os.environ['TEST_API']
MAIL=Path(os.environ['TEST_MAIL'])
CONTAINER=os.environ['TEST_CONTAINER']

def api(method, path, payload=None, token=None):
    headers={'Content-Type':'application/json'}
    if token: headers['Authorization']='Bearer '+token
    req=urllib.request.Request(BASE+path,
        data=json.dumps(payload).encode() if payload is not None else None,
        headers=headers, method=method)
    try:
        with urllib.request.urlopen(req,timeout=12) as resp:
            return resp.status,json.loads(resp.read())
    except urllib.error.HTTPError as exc:
        raw=exc.read()
        return exc.code,(json.loads(raw) if raw else {})

def expect(status, code, wanted_status, wanted_code=None):
    assert status==wanted_status, (status, wanted_status, code)
    if wanted_code:
        assert code.get('code')==wanted_code, (code, wanted_code)

def files(): return set(MAIL.glob('*.json'))

def new_link(before, target):
    fresh=files()-before
    assert len(fresh)==1, ('email count', len(fresh))
    email=json.loads(next(iter(fresh)).read_text())
    assert email['To']==target
    match=re.search(r'href="([^"]+)"',email['Html'])
    assert match
    parsed=urllib.parse.urlsplit(html.unescape(match.group(1)))
    assert parsed.scheme=='https' and parsed.netloc=='dev.zetruv.com'
    token=urllib.parse.parse_qs(parsed.query)['token'][0]
    assert len(token)==64
    return token

def expire_tokens(purpose):
    sql=('UPDATE customer_auth_tokens SET "ExpiresAt"=now()-interval \'1 minute\' '
         'WHERE "Purpose"=\''+purpose+'\' AND "ConsumedAt" IS NULL;')
    subprocess.run(['docker','exec',CONTAINER,'psql','-U','zetruv',
                    '-d','zetruv_auth_smoke','-c',sql],
                   check=True,stdout=subprocess.DEVNULL)

doc=api('GET','/openapi/v1.json')[1]
assert 'CustomerBearer' in doc['components']['securitySchemes']
assert any('CustomerBearer' in x for x in doc['paths']['/api/v1/auth/me']['get']['security'])
assert not doc['paths']['/api/v1/auth/login']['post'].get('security')

email='auth-test@zetruv.test'
bad=api('POST','/api/v1/auth/register',{'name':'Test','email':email,'password':'weak'})
expect(*bad,400,'INVALID_PASSWORD')
invalid_name=api('POST','/api/v1/auth/register',
    {'name':'  ','email':email,'password':'Start1234'})
expect(*invalid_name,400)
assert 'Name' in invalid_name[1].get('errors',{}), invalid_name
before=files()
register=api('POST','/api/v1/auth/register',
    {'name':'Test Customer','email':email,'password':'Start1234'})
expect(*register,202)
initial=new_link(before,email)
expect(*api('POST','/api/v1/auth/login',
    {'email':email,'password':'Start1234'}),403,'EMAIL_NOT_VERIFIED')
expect(*api('POST','/api/v1/auth/register',
    {'name':'Test','email':email,'password':'Start1234'}),409,'EMAIL_ALREADY_REGISTERED')
before=files()
expect(*api('POST','/api/v1/auth/resend-verification',{'email':email}),202)
assert files()==before, 'cooldown did not suppress resend'
time.sleep(1.2)
before=files()
with ThreadPoolExecutor(max_workers=2) as pool:
    replies=list(pool.map(lambda _: api('POST','/api/v1/auth/resend-verification',
                            {'email':email}), range(2)))
for reply in replies: expect(*reply,202)
replacement=new_link(before,email)
expect(*api('POST','/api/v1/auth/verify-email',{'token':initial}),
       400,'VERIFICATION_LINK_INVALID')
status,verified=api('POST','/api/v1/auth/verify-email',{'token':replacement})
expect(status,verified,200)
jwt=verified['accessToken']
assert verified['customer']['email']==email and verified['customer']['emailVerified']
expect(*api('POST','/api/v1/auth/verify-email',{'token':replacement}),
       400,'VERIFICATION_LINK_INVALID')
expect(*api('GET','/api/v1/auth/me',token=jwt),200)
status,admin=api('POST','/api/v1/cms/auth/login',
    {'email':'customer-auth-admin@zetruv.test','password':'CustomerAuthAdmin123!'})
expect(status,admin,200)
expect(*api('GET','/api/v1/auth/me',token=admin['accessToken']),401)
expect(*api('GET','/api/v1/cms/orders',token=jwt),401)
expect(*api('POST','/api/v1/auth/login',
    {'email':email,'password':'wrongpassword'}),401,'INVALID_CREDENTIALS')

unknown='missing@zetruv.test'
before=files()
unknown_result=api('POST','/api/v1/auth/forgot-password',{'email':unknown})
expect(*unknown_result,202)
assert files()==before, 'unknown email sent mail'
before=files()
known_result=api('POST','/api/v1/auth/forgot-password',{'email':email})
expect(*known_result,202)
assert unknown_result==known_result, 'account enumeration response'
reset_token=new_link(before,email)
before=files()
expect(*api('POST','/api/v1/auth/forgot-password',{'email':email}),202)
assert files()==before, 'reset cooldown did not suppress resend'
expect(*api('POST','/api/v1/auth/reset-password',
    {'token':reset_token,'newPassword':'weak','confirmPassword':'weak'}),
    400,'INVALID_PASSWORD')
expect(*api('POST','/api/v1/auth/reset-password',
    {'token':reset_token,'newPassword':'Updated1234','confirmPassword':'Other1234'}),
    400,'PASSWORD_MISMATCH')
expect(*api('POST','/api/v1/auth/reset-password',
    {'token':reset_token,'newPassword':'Start1234','confirmPassword':'Start1234'}),
    400,'PASSWORD_REUSED')
expect(*api('POST','/api/v1/auth/reset-password',
    {'token':reset_token,'newPassword':'Updated1234','confirmPassword':'Updated1234'}),200)
expect(*api('POST','/api/v1/auth/reset-password',
    {'token':reset_token,'newPassword':'Another1234','confirmPassword':'Another1234'}),
    400,'RESET_LINK_INVALID')
expect(*api('GET','/api/v1/auth/me',token=jwt),401)
expect(*api('POST','/api/v1/auth/login',
    {'email':email,'password':'Start1234'}),401,'INVALID_CREDENTIALS')
status,updated=api('POST','/api/v1/auth/login',
    {'email':email,'password':'Updated1234'})
expect(status,updated,200)
expect(*api('GET','/api/v1/auth/me',token=updated['accessToken']),200)

time.sleep(1.2)
before=files()
expect(*api('POST','/api/v1/auth/forgot-password',{'email':email}),202)
expired_reset=new_link(before,email)
expect(*api('POST','/api/v1/auth/reset-password',
    {'token':expired_reset,'newPassword':'Start1234','confirmPassword':'Start1234'}),
    400,'PASSWORD_REUSED')
expire_tokens('PasswordReset')
expect(*api('POST','/api/v1/auth/reset-password',
    {'token':expired_reset,'newPassword':'Third1234','confirmPassword':'Third1234'}),
    400,'RESET_LINK_EXPIRED')

second='second-auth@zetruv.test'
before=files()
expect(*api('POST','/api/v1/auth/register',
    {'name':'Second','email':second,'password':'Second1234'}),202)
expired_verify=new_link(before,second)
expire_tokens('EmailVerification')
expect(*api('POST','/api/v1/auth/verify-email',{'token':expired_verify}),
       400,'VERIFICATION_LINK_EXPIRED')
time.sleep(1.2)
before=files()
expect(*api('POST','/api/v1/auth/resend-verification',{'email':second}),202)
new_verify=new_link(before,second)
expect(*api('POST','/api/v1/auth/verify-email',{'token':new_verify}),200)

old_email='typo-auth@zetruv.test'
corrected_email='corrected-auth@zetruv.test'
before=files()
status,pending=api('POST','/api/v1/auth/register',
    {'name':'Editable Customer','email':old_email,'password':'Editable1234'})
expect(status,pending,202)
old_link=new_link(before,old_email)
session=pending['registrationToken']
before=files()
status,changed=api('POST','/api/v1/auth/change-registration-email',
    {'registrationToken':session,'newEmail':corrected_email})
expect(status,changed,202)
corrected_link=new_link(before,corrected_email)
expect(*api('POST','/api/v1/auth/verify-email',{'token':old_link}),
       400,'VERIFICATION_LINK_INVALID')
expect(*api('POST','/api/v1/auth/change-registration-email',
    {'registrationToken':session,'newEmail':'replay-auth@zetruv.test'}),
       400,'REGISTRATION_SESSION_INVALID')
expect(*api('POST','/api/v1/auth/login',
    {'email':old_email,'password':'Editable1234'}),401,'INVALID_CREDENTIALS')
status,corrected=api('POST','/api/v1/auth/verify-email',{'token':corrected_link})
expect(status,corrected,200)
assert corrected['customer']['email']==corrected_email
expect(*api('POST','/api/v1/auth/change-registration-email',
    {'registrationToken':changed['registrationToken'],
     'newEmail':'after-verify@zetruv.test'}),400,'REGISTRATION_SESSION_INVALID')
print('PASS: customer register, verify, edit email, resend, cooldown, expiry, auto-login, JWT isolation,')
print('PASS: forgot generic response, reset, password history, replay guard, old session revocation')
PY
