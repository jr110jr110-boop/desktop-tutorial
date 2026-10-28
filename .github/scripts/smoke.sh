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

# Front-end served by the API from wwwroot (one IIS site): pages, assets, and API paths that must stay 404.
if [ -f src/WebApi/wwwroot/index.html ]; then
  for p in "" "login" "contract/list" "contract/update/12" "workflowGroup/list" "inventory/documents/new" "inventory/trace" "inventory/ncr/new" "inventory/material-requests/new" "inventory/counts" "inventory/inspections" "inventory/item-requests" "inventory/coding"; do
    code=$(curl -s -o "$OUT/page.tmp" -w '%{http_code}' "$BASE/$p")
    if grep -q '<div id="app">' "$OUT/page.tmp"; then kind=index.html; else kind=other; fi
    echo "ui_page /$p http=$code body=$kind" | tee -a "$OUT/3-results.txt"
  done
  asset=$(grep -o -m1 '/assets/index\.[0-9a-f]*\.js' src/WebApi/wwwroot/index.html)
  echo "ui_asset $asset $(curl -s -o /dev/null -w 'http=%{http_code} type=%{content_type}' "$BASE$asset")" | tee -a "$OUT/3-results.txt"
  echo "ui_favicon $(curl -s -o /dev/null -w 'http=%{http_code}' "$BASE/favicon.ico")" | tee -a "$OUT/3-results.txt"
  echo "api_unknown_path $(curl -s -o /dev/null -w 'http=%{http_code}' "$BASE/api/v1/NoSuchThing/xyz") (expect 404)" | tee -a "$OUT/3-results.txt"
  echo "ui_post_to_page $(curl -s -o /dev/null -w 'http=%{http_code}' -X POST "$BASE/contract/list") (expect 404)" | tee -a "$OUT/3-results.txt"
  echo "swagger_ui $(curl -s -o /dev/null -w 'http=%{http_code}' "$BASE/swagger/index.html")" | tee -a "$OUT/3-results.txt"
  rm -f "$OUT/page.tmp"
fi

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

# logout (R1a2, M6): POST with the refresh token in the body; the address form is kept for one release only (deprecated) and is no longer used here
curl -s -X POST "$BASE/api/v1/Accounts/logout" -H 'Content-Type: application/json' -H "Authorization: Bearer $TOKEN" \
  -d "{\"refreshToken\":\"$REFRESH\"}" -o "$OUT/logout.json" -w 'logout_http=%{http_code}\n' | tee -a "$OUT/3-results.txt"
echo "logout_success=$(jq -r '.success' "$OUT/logout.json" 2>/dev/null) (expect true)" | tee -a "$OUT/3-results.txt"
# the refresh token of a signed-out user is dead, and so is the access token it was issued with (the security stamp moved on, R1a2 H2)
curl -s -X POST "$BASE/api/v1/Accounts/refresh-token" -H 'Content-Type: application/json' \
  -d "{\"refreshToken\":\"$REFRESH\"}" -o "$OUT/refresh-after-logout.json" -w 'refresh_after_logout_http=%{http_code}\n' | tee -a "$OUT/3-results.txt"
echo "refresh_after_logout_success=$(jq -r '.success' "$OUT/refresh-after-logout.json" 2>/dev/null) (expect false)" | tee -a "$OUT/3-results.txt"
echo "old_access_token_after_logout $(curl -s -o /dev/null -w 'http=%{http_code}' -H "Authorization: Bearer $TOKEN" "$BASE/api/v1/Accounts/get-current-user-info") (expect 401)" | tee -a "$OUT/3-results.txt"

echo "--- status codes:" >> "$OUT/3-results.txt"
awk '{print $1}' "$OUT/4-endpoints.txt" | sort | uniq -c >> "$OUT/3-results.txt"
cat "$OUT/3-results.txt"

kill "$APP_PID" 2>/dev/null; sleep 3; kill -9 "$APP_PID" 2>/dev/null || true
exit 0
