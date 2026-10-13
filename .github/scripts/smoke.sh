#!/usr/bin/env bash
# Starts the API against an empty SQL Server (migrations run from scratch), logs in as the
# seeded admin and calls every parameterless GET endpoint. Results go to smoke-output/.
set -u
OUT="$PWD/smoke-output"
BASE=http://127.0.0.1:5080
mkdir -p "$OUT"

cd src/WebApi
dotnet bin/Release/net10.0/WebApi.dll > "$OUT/2-app.log" 2>&1 &
APP_PID=$!
cd ../..

ok=0; code=000
for i in $(seq 1 180); do
  code=$(curl -s -o /dev/null -w '%{http_code}' "$BASE/swagger/v1/swagger.json" || true)
  if [ "$code" = "200" ]; then ok=1; break; fi
  if ! kill -0 "$APP_PID" 2>/dev/null; then echo "app exited during startup" | tee -a "$OUT/3-results.txt"; break; fi
  sleep 2
done
echo "startup_ok=$ok swagger_http=$code" | tee -a "$OUT/3-results.txt"
curl -s "$BASE/swagger/v1/swagger.json" -o "$OUT/swagger.json" || true

curl -s -X POST "$BASE/api/v1/Accounts/login" -H 'Content-Type: application/json' \
  -d '{"userName":"Admin","password":"Ci#Adm1n-Build-2026"}' \
  -o "$OUT/login.json" -w 'login_http=%{http_code}\n' | tee -a "$OUT/3-results.txt"
TOKEN=$(jq -r '.. | .accessToken? // empty' "$OUT/login.json" 2>/dev/null | head -n1)
REFRESH=$(jq -r '.. | .refreshToken? // empty' "$OUT/login.json" 2>/dev/null | head -n1)
echo "access_token_length=${#TOKEN} refresh_token_length=${#REFRESH}" | tee -a "$OUT/3-results.txt"

# refresh-token flow (what the front-end does in App.vue)
curl -s -X POST "$BASE/api/v1/Accounts/refresh-token" -H 'Content-Type: application/json' -H "Authorization: Bearer $TOKEN" \
  -d "{\"refreshToken\":\"$REFRESH\"}" -o "$OUT/refresh.json" -w 'refresh_http=%{http_code}\n' | tee -a "$OUT/3-results.txt"
NEW_TOKEN=$(jq -r '.. | .accessToken? // empty' "$OUT/refresh.json" 2>/dev/null | head -n1)
NEW_REFRESH=$(jq -r '.. | .refreshToken? // empty' "$OUT/refresh.json" 2>/dev/null | head -n1)
echo "refreshed_access_token_length=${#NEW_TOKEN}" | tee -a "$OUT/3-results.txt"
if [ -n "$NEW_TOKEN" ]; then TOKEN="$NEW_TOKEN"; REFRESH="$NEW_REFRESH"; fi

: > "$OUT/4-endpoints.txt"
while IFS= read -r ep; do
  [ -z "$ep" ] && continue
  code=$(curl -s -o "$OUT/body.tmp" -w '%{http_code}' -H "Authorization: Bearer $TOKEN" "$BASE/$ep?PageNumber=1&PageSize=10")
  echo "$code $ep" >> "$OUT/4-endpoints.txt"
  if [ "$code" -ge 500 ] || [ "$code" = "000" ]; then
    { echo "=== $code $ep"; head -c 3000 "$OUT/body.tmp"; echo; } >> "$OUT/5-errors.txt"
  fi
done < .github/scripts/get-endpoints.txt

# second pass with search text, to run the "Contains" search branches of the list queries
: > "$OUT/4b-endpoints-search.txt"
while IFS= read -r ep; do
  [ -z "$ep" ] && continue
  code=$(curl -s -o "$OUT/body.tmp" -w '%{http_code}' -H "Authorization: Bearer $TOKEN" "$BASE/$ep?PageNumber=1&PageSize=10&Contains=ab%20cd")
  echo "$code $ep" >> "$OUT/4b-endpoints-search.txt"
  if [ "$code" -ge 500 ] || [ "$code" = "000" ]; then
    { echo "=== search $code $ep"; head -c 3000 "$OUT/body.tmp"; echo; } >> "$OUT/5-errors.txt"
  fi
done < .github/scripts/get-endpoints.txt
rm -f "$OUT/body.tmp"

curl -s "$BASE/api/v1/Accounts/logout/$REFRESH" -H "Authorization: Bearer $TOKEN" -o "$OUT/logout.json" -w 'logout_http=%{http_code}\n' | tee -a "$OUT/3-results.txt"

echo "--- status codes:" >> "$OUT/3-results.txt"
awk '{print $1}' "$OUT/4-endpoints.txt" | sort | uniq -c >> "$OUT/3-results.txt"
cat "$OUT/3-results.txt"

kill "$APP_PID" 2>/dev/null; sleep 3; kill -9 "$APP_PID" 2>/dev/null || true
exit 0
