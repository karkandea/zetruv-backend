#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
TMP=$(mktemp -d)
C=zetruv-sandbox-qa-${TMP##*.}
DB=zetruv_sandbox_qa
API=$(python3 -c 'import socket;s=socket.socket();s.bind(("127.0.0.1",0));print(s.getsockname()[1]);s.close()')
PID=''
cleanup(){ if [[ -n "$PID" ]]; then kill "$PID" 2>/dev/null || true; wait "$PID" 2>/dev/null || true; fi; docker rm -fv "$C" >/dev/null 2>&1 || true; rm -rf -- "$TMP"; }
trap cleanup EXIT
dotnet build src/Zetruv.Api/Zetruv.Api.csproj -c Release --nologo
docker run -d --name "$C" -e POSTGRES_USER=zetruv -e POSTGRES_PASSWORD=qa-test -e POSTGRES_DB="$DB" -p 127.0.0.1::5432 postgres:17-alpine >/dev/null
PORT=$(docker port "$C" 5432/tcp | sed -E 's/^.*:([0-9]+)$/\1/')
for i in $(seq 1 45); do docker exec "$C" pg_isready -U zetruv -d "$DB" >/dev/null 2>&1 && break; sleep 1; done
export ConnectionStrings__Postgres="Host=127.0.0.1;Port=$PORT;Database=$DB;Username=zetruv;Password=qa-test"
export Jwt__Key=0123456789abcdef0123456789abcdef
export CmsAdmin__Email=qa@zetruv.local CmsAdmin__Password='SyntheticQA123!'
export ManualLogin__EncryptionKey=$(printf %s 0123456789abcdef0123456789abcdef | openssl base64 -A)
export ManualLogin__SandboxTestEnabled=true
export ManualLogin__SandboxTestProductIds__0=c2000000-0000-0000-0000-000000000001
export ManualLogin__SandboxTestProductIds__1=c2000000-0000-0000-0000-000000000002
export Payments__Provider=midtrans Payments__Midtrans__Enabled=true Payments__Midtrans__Mode=Sandbox Payments__Midtrans__ServerKey=SB-Mid-server-synthetic-QA-only
export GameAccountValidation__Provider=mock Shipping__Provider=mock
start(){ ASPNETCORE_ENVIRONMENT="$1" dotnet src/Zetruv.Api/bin/Release/net10.0/Zetruv.Api.dll --urls="http://127.0.0.1:$API" >"$TMP/api.log" 2>&1 & PID=$!; for i in $(seq 1 45); do curl -fsS "http://127.0.0.1:$API/health" >/dev/null 2>&1 && return; sleep 1; done; tail -40 "$TMP/api.log"; return 1; }
stop(){ kill "$PID"; wait "$PID" 2>/dev/null || true; PID=''; }
start Development
docker exec -i "$C" psql -v ON_ERROR_STOP=1 -U zetruv -d "$DB" <<'SQL'
UPDATE games SET "IsActive"=TRUE WHERE "Slug" IN ('efootball-mobile','call-of-duty-mobile');
UPDATE catalog_categories SET "IsActive"=TRUE WHERE "Kind"='TopUpLogin';
-- Fresh migrations may run before top_up_login is bootstrapped. Populate
-- these test-only fixtures explicitly so absence cannot make the gate pass.
INSERT INTO products ("Id","CategoryId","GameId","Name","Slug","Kind","FulfillmentMethod","RequiresGameAccountValidation","IsActive","IsFeatured","SortOrder","CreatedAt","UpdatedAt")
SELECT s.id::uuid,(SELECT "Id" FROM catalog_categories WHERE "Key"='top_up_login'),g."Id",s.name,s.slug,'TopUpLogin','MANUAL_LOGIN',FALSE,TRUE,FALSE,0,NOW(),NOW()
FROM (VALUES ('c2000000-0000-0000-0000-000000000001','efootball-mobile','eFootball QA'),('c2000000-0000-0000-0000-000000000002','call-of-duty-mobile','CODM QA')) s(id,slug,name)
JOIN games g ON g."Slug"=s.slug ON CONFLICT ("Slug") DO NOTHING;
UPDATE products SET "IsActive"=TRUE WHERE "Id" IN ('c2000000-0000-0000-0000-000000000001','c2000000-0000-0000-0000-000000000002');
INSERT INTO product_variants ("Id","ProductId","Name","Sku","Price","StockQuantity","IsActive","SortOrder","CreatedAt","UpdatedAt")
VALUES ('d4000000-0000-0000-0000-000000000001','c2000000-0000-0000-0000-000000000001','QA package','DUMMY-EFOOTBALL-S',10000,10,TRUE,0,NOW(),NOW()),
('d4000000-0000-0000-0000-000000000004','c2000000-0000-0000-0000-000000000002','QA package','DUMMY-CODM-S',10000,10,TRUE,0,NOW(),NOW()) ON CONFLICT ("Sku") DO NOTHING;
INSERT INTO product_input_fields ("Id","ProductId","Key","Label","Scope","Type","IsRequired","IsSensitive","MaxLength","SortOrder","CreatedAt","UpdatedAt")
VALUES ('c3000000-0000-0000-0000-000000000003','c2000000-0000-0000-0000-000000000001','nickname','QA nickname','LoginCredential','Text',TRUE,FALSE,120,0,NOW(),NOW()),
('c3000000-0000-0000-0000-000000000006','c2000000-0000-0000-0000-000000000002','nickname','QA nickname','LoginCredential','Text',TRUE,FALSE,120,0,NOW(),NOW()) ON CONFLICT ("ProductId","Key") DO NOTHING;
INSERT INTO product_input_fields ("Id","ProductId","Key","Label","Scope","Type","IsRequired","IsSensitive","MaxLength","SortOrder","CreatedAt","UpdatedAt")
SELECT gen_random_uuid(),p."Id",s.key,s.label,'LoginCredential',s.typ,TRUE,s.sensitive,200,0,NOW(),NOW()
FROM products p CROSS JOIN (VALUES ('email','QA email','Email',FALSE),('password','QA password','Password',TRUE)) s(key,label,typ,sensitive)
WHERE p."Id" IN ('c2000000-0000-0000-0000-000000000001','c2000000-0000-0000-0000-000000000002') ON CONFLICT ("ProductId","Key") DO NOTHING;
SQL
for id in "$ManualLogin__SandboxTestProductIds__0" "$ManualLogin__SandboxTestProductIds__1"; do
 curl -fsS "http://127.0.0.1:$API/api/v1/checkout/manual-login/readiness/$id" | python3 -c 'import sys,json;d=json.load(sys.stdin);assert d["status"]=="SandboxTest" and d["available"] is True,d'
done
for vid in d4000000-0000-0000-0000-000000000001 d4000000-0000-0000-0000-000000000004; do
 for mode in reject accept; do
  body=$(python3 -c 'import json,sys; v,m=sys.argv[1:]; print(json.dumps({"customerEmail":"qa@example.test","customerPhone":"+6281234567890","items":[{"productVariantId":v,"quantity":1,"loginCredentials":{"email":"qa-player@example.test","password":"Zetruv-QA-Only-2026!" if m=="accept" else "not-the-fixed-fixture","nickname":"Zetruv QA"}}]}))' "$vid" "$mode")
  status=$(curl -sS -o "$TMP/response" -w '%{http_code}' -X POST "http://127.0.0.1:$API/api/v1/checkout/orders" -H 'Content-Type: application/json' -d "$body")
  if [[ "$mode" == reject ]]; then [[ "$status" == 400 ]] && grep -Fq 'only the supplied synthetic QA account' "$TMP/response" || { echo "Unexpected rejection: $status"; exit 1; }
  else [[ "$status" == 200 || "$status" == 201 ]] || { echo "Unexpected acceptance: $status"; cat "$TMP/response"; exit 1; }; fi
 done
done
stop
export ManualLogin__SandboxTestEnabled=false
start Development
for id in "$ManualLogin__SandboxTestProductIds__0" "$ManualLogin__SandboxTestProductIds__1"; do
 curl -fsS "http://127.0.0.1:$API/api/v1/checkout/manual-login/readiness/$id" | python3 -c 'import sys,json;d=json.load(sys.stdin);assert d["status"]=="NotReady" and d["available"] is False,d'
done
stop
export ManualLogin__SandboxTestEnabled=true
start Staging
for id in "$ManualLogin__SandboxTestProductIds__0" "$ManualLogin__SandboxTestProductIds__1"; do
 curl -fsS "http://127.0.0.1:$API/api/v1/checkout/manual-login/readiness/$id" | python3 -c 'import sys,json;d=json.load(sys.stdin);assert d["status"]=="NotReady" and d["available"] is False,d'
done
echo 'PASS: two scoped sandbox products, fixed synthetic input only, disabled and non-Development gates closed; no payment request or supplier call'
