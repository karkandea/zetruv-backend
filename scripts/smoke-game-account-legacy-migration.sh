#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
C="zetruv-pg-game-account-migration-$$"
cleanup(){ docker rm -f "$C" >/dev/null 2>&1 || true; }
trap cleanup EXIT
dotnet build src/Zetruv.Api/Zetruv.Api.csproj -c Release --nologo >/dev/null
docker run -d --name "$C" -e POSTGRES_USER=zetruv -e POSTGRES_PASSWORD=zetruvtest \
  -e POSTGRES_DB=zetruv_game_account_migration -p 127.0.0.1::5432 postgres:17-alpine >/dev/null
until docker exec "$C" pg_isready -U zetruv -d zetruv_game_account_migration >/dev/null 2>&1; do sleep 1; done
PORT=$(docker port "$C" 5432/tcp | awk -F: 'NR==1 {print $NF}')
export ConnectionStrings__Postgres="Host=127.0.0.1;Port=$PORT;Database=zetruv_game_account_migration;Username=zetruv;Password=zetruvtest"
export Jwt__Key='0123456789abcdef0123456789abcdef'
dotnet ef database update 20260923080846_AddStorefrontPersonalizationAndMetrics \
  --project src/Zetruv.Api/Zetruv.Api.csproj \
  --startup-project src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --no-build >/dev/null
docker exec -i "$C" psql -v ON_ERROR_STOP=1 -U zetruv -d zetruv_game_account_migration >/dev/null <<'SQL'
INSERT INTO catalog_categories
  ("Id","Key","Name","Slug","Kind","IsActive","SortOrder","CreatedAt","UpdatedAt")
VALUES
  ('c1111111-1111-1111-1111-111111111111','game_account','Game Account',
   'game-account','GameAccount',TRUE,0,now(),now());
INSERT INTO games
  ("Id","Name","Slug","IsActive","IsPopular","SortOrder","CreatedAt","UpdatedAt")
VALUES
  ('e1111111-1111-1111-1111-111111111111','Mobile Legends',
   'legacy-ml',TRUE,FALSE,0,now(),now());
INSERT INTO products
  ("Id","CategoryId","GameId","Name","Slug","Kind","FulfillmentMethod",
   "RequiresGameAccountValidation","IsActive","IsFeatured","SortOrder","CreatedAt","UpdatedAt")
VALUES
  ('a1111111-1111-1111-1111-111111111111',
   'c1111111-1111-1111-1111-111111111111',
   'e1111111-1111-1111-1111-111111111111',
   'Legacy ML','legacy-ml-account','GameAccount','MANUAL',FALSE,TRUE,FALSE,0,now(),now()),
  ('a2222222-2222-2222-2222-222222222222',
   'c1111111-1111-1111-1111-111111111111',
   NULL,'Legacy without game','legacy-unlinked-account',
   'GameAccount','MANUAL',FALSE,TRUE,FALSE,1,now(),now());
INSERT INTO game_account_details
  ("ProductId","Rank","SkinCount","Region","Level","AdditionalInfo")
VALUES
  ('a1111111-1111-1111-1111-111111111111',
   'Mythic',119,'ID',61,'Starlight active'),
  ('a2222222-2222-2222-2222-222222222222',
   'Ancient',3,'SEA',NULL,NULL);
SQL
dotnet ef database update \
  --project src/Zetruv.Api/Zetruv.Api.csproj \
  --startup-project src/Zetruv.Api/Zetruv.Api.csproj --configuration Release --no-build >/dev/null
export TEST_CONTAINER="$C"
python3 - <<'PY'
import json,os,subprocess
c=os.environ['TEST_CONTAINER']
def query(sql):
 return subprocess.check_output(['docker','exec',c,'psql','-At','-U','zetruv',
    '-d','zetruv_game_account_migration','-c',sql],text=True).strip()
linked=json.loads(query("""SELECT "AttributesJson" FROM game_account_details
 WHERE "ProductId"='a1111111-1111-1111-1111-111111111111'"""))
unlinked=json.loads(query("""SELECT "AttributesJson" FROM game_account_details
 WHERE "ProductId"='a2222222-2222-2222-2222-222222222222'"""))
assert linked=={'rank':'Mythic','skinCount':119,'region':'ID',
    'level':61,'additionalInfo':'Starlight active'},linked
assert unlinked=={'rank':'Ancient','skinCount':3,'region':'SEA'},unlinked
schema=query("""SELECT "Key" FROM game_account_attribute_definitions
 WHERE "GameId"='e1111111-1111-1111-1111-111111111111' ORDER BY "SortOrder" """)
assert schema.splitlines()==['rank','skinCount','region','level','additionalInfo'],schema
assert query('SELECT count(*) FROM game_account_attribute_definitions')=='5'
print('PASS: fixed legacy attributes migrated to JSON and per-game template; unlinked legacy values preserved')
PY
