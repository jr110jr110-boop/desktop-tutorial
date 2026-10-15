#!/usr/bin/env bash
# End-to-end test of the warehouse / part traceability / Sepidar module against the CI database.
# Starts the API, runs a full scenario through the HTTP API and writes PASS/FAIL lines to
# smoke-output/7-inventory.txt (response bodies of failed checks go to 7-inventory-fail.txt).
set -u
OUT="$PWD/smoke-output"
BASE=http://127.0.0.1:5080
API=$BASE/api/v1
R="$OUT/7-inventory.txt"
F="$OUT/7-inventory-fail.txt"
B="$OUT/inv-body.tmp"
mkdir -p "$OUT"; : > "$R"; : > "$F"

cd src/WebApi
dotnet bin/Release/net10.0/WebApi.dll > "$OUT/7-inventory-app.log" 2>&1 &
APP_PID=$!
cd ../..
for i in $(seq 1 120); do
  [ "$(curl -s -o /dev/null -w '%{http_code}' "$BASE/swagger/v1/swagger.json")" = "200" ] && break
  sleep 2
done

pass=0; fail=0
ok()  { echo "PASS $1" | tee -a "$R"; pass=$((pass+1)); }
bad() { echo "FAIL $1" | tee -a "$R"; { echo "=== $1"; head -c 4000 "$B"; echo; } >> "$F"; fail=$((fail+1)); }
# check NAME JQ-EXPRESSION : passes when the expression is true for the last response body
check() { if jq -e "$2" "$B" >/dev/null 2>&1; then ok "$1"; else bad "$1"; fi; }
# call METHOD PATH [JSON] : body to $B, HTTP code to $CODE
call() {
  if [ $# -ge 3 ]; then
    CODE=$(curl -s -o "$B" -w '%{http_code}' -X "$1" "$API/$2" -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' --data-binary "$3")
  else
    CODE=$(curl -s -o "$B" -w '%{http_code}' -X "$1" "$API/$2" -H "Authorization: Bearer $TOKEN")
  fi
}
val() { jq -r "$1" "$B"; }

login() {
  curl -s -X POST "$API/Accounts/login" -H 'Content-Type: application/json' -d "{\"userName\":\"$1\",\"password\":\"$2\"}" -o "$B"
  jq -r '.. | .accessToken? // empty' "$B" | head -n1
}
TOKEN=$(login Admin 'Ci#Adm1n-Build-2026')
[ -n "$TOKEN" ] && ok "login admin (PMO)" || bad "login admin (PMO)"
ADMIN_TOKEN=$TOKEN

# users of the other roles (approvals must come from different people)
user() { # user NAME ROLEID PASSWORD N
  call POST Accounts/register-user "{\"userName\":\"$1\",\"name\":\"$1\",\"lastName\":\"آزمون\",\"email\":\"$1@example.com\",\"roleIds\":[$2],\"password\":\"$3\",\"phoneNumber\":\"0912000000$4\",\"birthDate\":\"1990-01-01T00:00:00+03:30\",\"nationalCode\":\"001234567$4\"}"
}
user wh.ci 6 'Ci#Wh-User-2026' 1
user prod.ci 9 'Ci#Prod-User-2026' 2
user qc.ci 5 'Ci#Qc-User-2026' 3
user eng.ci 4 'Ci#Eng-User-2026' 4
user sec.ci 10 'Ci#Sec-User-2026' 5
user sup.ci 12 'Ci#Sup-User-2026' 6
user plan.ci 11 'Ci#Plan-User-2026' 7
SUP_TOKEN=$(login sup.ci 'Ci#Sup-User-2026'); PLAN_TOKEN=$(login plan.ci 'Ci#Plan-User-2026')
[ -n "$SUP_TOKEN" ] && [ -n "$PLAN_TOKEN" ] && ok "login warehouse supervisor and planning users" || bad "login warehouse supervisor and planning users"
WH_TOKEN=$(login wh.ci 'Ci#Wh-User-2026'); PROD_TOKEN=$(login prod.ci 'Ci#Prod-User-2026'); QC_TOKEN=$(login qc.ci 'Ci#Qc-User-2026')
ENG_TOKEN=$(login eng.ci 'Ci#Eng-User-2026'); SEC_TOKEN=$(login sec.ci 'Ci#Sec-User-2026')
[ -n "$WH_TOKEN" ] && [ -n "$PROD_TOKEN" ] && [ -n "$QC_TOKEN" ] && [ -n "$ENG_TOKEN" ] && [ -n "$SEC_TOKEN" ] && ok "login warehouse, production, QC, engineering, security users" || bad "login warehouse, production, QC, engineering, security users"
as() { local t=$TOKEN; TOKEN=$1; shift; call "$@"; TOKEN=$t; } # as TOKEN METHOD PATH [JSON]

# item codes per WI-WH-002 (material, part, MESC standard part, product)
C_RAW=01001025120101; C_SHAFT=02130001010003; C_BOLT=50.10.12.024.1; C_ROTOR=031TS250PB1405

# ---------------------------------------------------------------- master data
call POST inventory/groups '{"code":"TRB","title":"توربین بخار"}'; check "group create" '.success and .data.id > 0'
G1=$(val .data.id)
call POST inventory/groups "{\"code\":\"TRB-ROT\",\"title\":\"روتور\",\"parentId\":$G1}"; check "sub-group create" ".success and .data.parentId == $G1"
G2=$(val .data.id)
call POST inventory/groups "{\"id\":$G1,\"code\":\"TRB\",\"title\":\"توربین\",\"parentId\":$G2}"; check "group cycle rejected" '.success == false'

# item VAR JSON LABEL : creates the item and stores its id in VAR (no subshell, so the counters stay right)
item() { call POST inventory/items "$2"; check "item $3" '.success and .data.id > 0'; printf -v "$1" '%s' "$(val .data.id)"; }
item RAW "{\"code\":\"$C_RAW\",\"title\":\"میلگرد فورج 26NiCrMoV\",\"itemGroupId\":$G2,\"unit\":\"kg\",\"trackingMode\":1,\"material\":\"26NiCrMoV14-5\",\"minStock\":100,\"sepidarCode\":\"5001\",\"requiresIncomingInspection\":false}" "material (lot)"
item SHAFT "{\"code\":\"$C_SHAFT\",\"title\":\"شفت روتور\",\"itemGroupId\":$G2,\"unit\":\"عدد\",\"trackingMode\":2,\"technicalCode\":\"130010100\",\"requiresIncomingInspection\":false}" "part (serial)"
item BOLT "{\"code\":\"$C_BOLT\",\"title\":\"پیچ M24\",\"unit\":\"عدد\",\"trackingMode\":0,\"minStock\":500,\"requiresIncomingInspection\":false}" "MESC standard part (none)"
item ROTOR "{\"code\":\"$C_ROTOR\",\"title\":\"روتور کامل\",\"itemGroupId\":$G1,\"unit\":\"عدد\",\"trackingMode\":2}" "product (serial)"
call POST inventory/items "{\"code\":\"$C_RAW\",\"title\":\"تکراری\",\"unit\":\"kg\"}"; check "duplicate item code rejected" '.success == false'
call GET "inventory/items/by-code?code=$C_SHAFT"; check "item by code" ".data.id == $SHAFT"

call POST inventory/warehouses '{"code":"WH-RM","title":"انبار مواد و قطعات","kind":2,"sepidarCode":"101"}'; check "warehouse 1" '.success'
W1=$(val .data.id)
call POST inventory/warehouses '{"code":"WH-WIP","title":"کارگاه","kind":3,"sepidarCode":"102"}'; check "warehouse 2" '.success'
W2=$(val .data.id)
call POST inventory/warehouses/locations "{\"warehouseId\":$W1,\"code\":\"A-01\",\"title\":\"قفسه A ردیف 1\"}"; check "location A-01" '.success'
L1=$(val .data.id)
call POST inventory/warehouses/locations "{\"warehouseId\":$W1,\"code\":\"A-02\"}"; check "location A-02" '.success'
L2=$(val .data.id)
call POST inventory/warehouses/locations "{\"warehouseId\":$W2,\"code\":\"M-01\"}"; check "location M-01" '.success'
L3=$(val .data.id)

# ---------------------------------------------------------------- purchase receipt
DOC="{\"type\":1,\"documentDate\":\"2026-10-15T00:00:00\",\"warehouseId\":$W1,\"referenceNumber\":\"PO-77\",\"lines\":[
 {\"itemId\":$RAW,\"quantity\":500.5,\"lotNumber\":\"H-7781\",\"heatNumber\":\"H-7781\",\"certificateNumber\":\"MTC-3.1-9\",\"locationId\":$L1,\"unitPrice\":120000},
 {\"itemId\":$BOLT,\"quantity\":100,\"locationId\":$L2,\"unitPrice\":5000},
 {\"itemId\":$SHAFT,\"quantity\":2,\"locationId\":$L1,\"serialNumbers\":[\"S-1\",\"S-2\"],\"unitPrice\":900000000}]}"
call POST inventory/documents "$DOC"; check "receipt draft saved" '.success and .data.status == 0 and (.data.lines|length) == 3'
RC=$(val .data.id)
call POST inventory/documents "{\"type\":1,\"documentDate\":\"2026-10-15T00:00:00\",\"warehouseId\":$W1,\"lines\":[{\"itemId\":$SHAFT,\"quantity\":2,\"serialNumbers\":[\"S-9\"]}]}"
check "draft with serial count != quantity is saved (checked at confirm)" '.success'
BADDOC=$(val .data.id)
call POST "inventory/documents/$BADDOC/confirm"; check "confirm rejects serial count mismatch" '.success == false'
call DELETE "inventory/documents/$BADDOC"; check "draft delete" '.success'

call POST "inventory/documents/$RC/confirm"; check "receipt confirmed + numbered" '.success and .data.status == 1 and (.data.number|test("^RC-[0-9]{4}-0*1$")) and .data.sepidarStatus == 1'
call POST "inventory/documents/$RC/confirm"; check "second confirm rejected" '.success == false'
call GET "inventory/stock/items?ItemId=$RAW"; check "stock RM-001 = 500.5" '.data.data[0].onHand == 500.5'
call GET "inventory/stock?ItemId=$SHAFT"; check "stock SH-100 = 2 at A-01" '[.data.data[]|.quantity]|add == 2'
call GET "inventory/stock/serials?Contains=S-1&ItemId=$SHAFT"; check "serial S-1 in stock" '.data.data[0].status == 2'
S1=$(val '.data.data[0].id')
call GET "inventory/stock/serials?Contains=S-2&ItemId=$SHAFT"; S2=$(val '.data.data[0].id')
call GET "inventory/stock/lots?ItemId=$RAW"; check "lot H-7781 created with heat and certificate" '.data.data[0].lotNumber == "H-7781" and .data.data[0].certificateNumber == "MTC-3.1-9" and .data.data[0].onHand == 500.5'
LOT=$(val '.data.data[0].id')
call POST inventory/documents "{\"type\":1,\"documentDate\":\"2026-10-15T00:00:00\",\"warehouseId\":$W1,\"lines\":[{\"itemId\":$SHAFT,\"quantity\":1,\"serialNumbers\":[\"S-1\"]}]}"
D=$(val .data.id); call POST "inventory/documents/$D/confirm"; check "receipt of an existing serial rejected" '.success == false'
call DELETE "inventory/documents/$D"

# ---------------------------------------------------------------- shortage
call POST inventory/documents "{\"type\":6,\"documentDate\":\"2026-10-15T00:00:00\",\"warehouseId\":$W1,\"destinationWarehouseId\":$W2,\"lines\":[{\"itemId\":$BOLT,\"quantity\":1000}]}"
D=$(val .data.id); call POST "inventory/documents/$D/confirm"; check "transfer beyond stock rejected" '.success == false and (.message|length) > 0'
call DELETE "inventory/documents/$D"

# ---------------------------------------------------------------- transfer
call POST inventory/documents "{\"type\":6,\"documentDate\":\"2026-10-15T00:00:00\",\"warehouseId\":$W1,\"destinationWarehouseId\":$W2,\"lines\":[{\"itemId\":$SHAFT,\"quantity\":1,\"serialNumbers\":[\"S-1\"],\"destinationLocationId\":$L3},{\"itemId\":$BOLT,\"quantity\":40,\"destinationLocationId\":$L3}]}"
TR=$(val .data.id)
call POST "inventory/documents/$TR/confirm"; check "transfer confirmed" '.success and (.data.number|startswith("TR-"))'
call GET "inventory/stock/serials?Contains=S-1&ItemId=$SHAFT"; check "S-1 now in WH-WIP / M-01" ".data.data[0].warehouseId == $W2 and .data.data[0].locationCode == \"M-01\""
call GET "inventory/stock?ItemId=$BOLT"; check "bolts split 60 / 40" '[.data.data[]|.quantity]|sort == [40,60]'

# ---------------------------------------------------------------- issue to production (against an approved material request)
call POST inventory/documents "{\"type\":4,\"documentDate\":\"2026-10-16T00:00:00\",\"warehouseId\":$W1,\"lines\":[{\"itemId\":$RAW,\"quantity\":1,\"lotId\":$LOT}]}"
check "issue without a material request rejected" '.success == false'
call POST inventory/material-requests "{\"requestDate\":\"2026-10-16T00:00:00\",\"warehouseId\":$W1,\"purpose\":\"ساخت روتور آزمایشی\",\"lines\":[{\"itemId\":$RAW,\"quantity\":100},{\"itemId\":$SHAFT,\"quantity\":1}]}"
check "material request draft" '.success and .data.status == 0'
MR=$(val .data.id)
call POST inventory/material-requests/action "{\"id\":$MR,\"action\":\"submit\"}"; check "material request submitted + numbered" '.success and .data.status == 1 and (.data.number|startswith("MR-"))'
call POST inventory/documents "{\"type\":4,\"documentDate\":\"2026-10-16T00:00:00\",\"warehouseId\":$W1,\"materialRequestId\":$MR,\"lines\":[{\"itemId\":$RAW,\"quantity\":1,\"lotId\":$LOT}]}"
check "issue against a request not yet approved rejected" '.success == false'
call POST inventory/material-requests/action "{\"id\":$MR,\"action\":\"approve-planning\"}"; check "request approved by planning" '.success and .data.planningApproved and .data.status == 1'
call POST inventory/material-requests/action "{\"id\":$MR,\"action\":\"approve-manufacturing\"}"; check "planning user cannot also approve for manufacturing" '.success == false'
as "$PROD_TOKEN" POST inventory/material-requests/action "{\"id\":$MR,\"action\":\"approve-manufacturing\"}"; check "request approved by manufacturing -> approved" '.success and .data.status == 2'
call GET "inventory/material-requests/$MR"; check "request shows availability" ".data.lines[0].available == 500.5 and .data.lines[0].shortage == 0"
call POST inventory/documents "{\"type\":4,\"documentDate\":\"2026-10-16T00:00:00\",\"warehouseId\":$W1,\"materialRequestId\":$MR,\"lines\":[{\"itemId\":$RAW,\"quantity\":150,\"lotId\":$LOT}]}"
check "issue above the requested quantity rejected" '.success == false'
call POST inventory/documents "{\"type\":4,\"documentDate\":\"2026-10-16T00:00:00\",\"warehouseId\":$W1,\"materialRequestId\":$MR,\"lines\":[{\"itemId\":$BOLT,\"quantity\":1}]}"
check "issue of an item not in the request rejected" '.success == false'
call POST inventory/documents "{\"type\":4,\"documentDate\":\"2026-10-16T00:00:00\",\"warehouseId\":$W1,\"materialRequestId\":$MR,\"lines\":[{\"itemId\":$RAW,\"quantity\":100,\"lotId\":$LOT},{\"itemId\":$SHAFT,\"quantity\":1,\"serialNumbers\":[\"S-2\"]}]}"
IS=$(val .data.id)
call POST "inventory/documents/$IS/confirm"; check "issue confirmed" '.success and (.data.number|startswith("IS-")) and .data.materialRequestNumber != null'
call GET "inventory/material-requests/$MR"; check "request fully issued -> closed" '.data.status == 4 and .data.lines[0].issuedQuantity == 100 and (.data.issues|length) == 1'
call GET "inventory/stock/items?ItemId=$RAW"; check "stock RM-001 = 400.5" '.data.data[0].onHand == 400.5'
call GET "inventory/stock/serials?Contains=S-2&ItemId=$SHAFT"; check "S-2 in production" '.data.data[0].status == 1'
call GET "inventory/stock/kardex?ItemId=$RAW"; check "kardex RM-001: 2 rows, closing 400.5" '(.data.rows|length) == 2 and .data.closingBalance == 400.5 and .data.rows[1].balance == 400.5'
call GET "inventory/stock/items?LowStockOnly=true"; check "low-stock list has the bolt (100 < 500)" "[.data.data[]|.itemCode]|index(\"$C_BOLT\") != null"

# ---------------------------------------------------------------- routing, operations, QC
call POST inventory/trace/templates "{\"itemId\":$SHAFT,\"sequence\":10,\"title\":\"تراشکاری خشن\"}"; check "template 10" '.success'
call POST inventory/trace/templates "{\"itemId\":$SHAFT,\"sequence\":20,\"title\":\"بالانس دینامیکی\",\"requiresQc\":true}"; check "template 20 (QC)" '.success'
call POST inventory/trace/templates "{\"itemGroupId\":$G2,\"sequence\":5,\"title\":\"بازرسی ورودی\"}"; check "group template" '.success'
call POST inventory/trace/operations/apply-routing "{\"serialUnitId\":$S2}"; check "routing applied: item templates win over group" '.success and (.data|length) == 2'
OP1=$(val '.data[]|select(.sequence==10)|.id'); OP2=$(val '.data[]|select(.sequence==20)|.id')
call POST inventory/trace/operations/apply-routing "{\"serialUnitId\":$S2}"; check "routing applied twice: no duplicates" '.success and (.data|length) == 2'
call PUT inventory/trace/operations "{\"id\":$OP1,\"action\":\"start\",\"operatorName\":\"رضایی\"}"; check "op start" '.success and .data.status == 1'
call PUT inventory/trace/operations "{\"id\":$OP1,\"action\":\"finish\",\"result\":\"قطر 120.02\"}"; check "op finish" '.success and .data.status == 2'
call POST inventory/trace/operations/qc "{\"id\":$OP2,\"status\":2}"; check "QC before finish rejected" '.success == false'
call PUT inventory/trace/operations "{\"id\":$OP2,\"action\":\"start\"}"
call PUT inventory/trace/operations "{\"id\":$OP2,\"action\":\"finish\",\"result\":\"G2.5\"}"; check "op2 finished, waits for QC" '.success and .data.qcStatus == 1'
call POST inventory/trace/operations/qc "{\"id\":$OP2,\"status\":3}"; check "QC reject without note rejected" '.success == false'
call POST inventory/trace/operations/qc "{\"id\":$OP2,\"status\":2,\"note\":\"OK\"}"; check "QC accept" '.success and .data.qcStatus == 2'
call POST inventory/trace/operations/files "{\"partOperationId\":$OP2,\"documentId\":\"0f8fad5b-d9cb-469f-a165-70867728950e\",\"title\":\"x\"}"; check "file link to unknown upload rejected" '.success == false'
printf 'balance report G2.5\n' > "$OUT/balance-report.txt"
curl -s -o "$B" -X POST "$API/Documents?Description=balance&Name=balance-report&Code=INV" -H "Authorization: Bearer $TOKEN" -F "File=@$OUT/balance-report.txt"
DOCID=$(grep -oE '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}' "$B" | head -n1)
[ -n "$DOCID" ] && ok "file upload (Documents)" || bad "file upload (Documents)"
call POST inventory/trace/operations/files "{\"partOperationId\":$OP2,\"documentId\":\"$DOCID\",\"title\":\"گزارش بالانس\"}"; check "operation file link" '.success and (.data.files|length) == 1'
call GET "inventory/trace/operations?QcStatus=2"; check "QC queue filter" '.data.data|length >= 1'

# ---------------------------------------------------------------- assembly + traceability
call POST inventory/trace/serials "{\"itemId\":$ROTOR,\"applyRouting\":true}"; check "rotor serial created (label number YYDDDNNNN)" '.success and (.data.serialNumber|test("^[0-9]{9}$")) and .data.status == 1'
RT=$(val .data.id)
call POST inventory/trace/assembly/install "{\"parentSerialUnitId\":$RT,\"childSerialUnitId\":$S1}"; check "install of a serial still in stock rejected" '.success == false'
call POST inventory/trace/assembly/install "{\"parentSerialUnitId\":$RT,\"childSerialUnitId\":$S2}"; check "S-2 installed in rotor" '.success'
LINK=$(val .data.id)
call POST inventory/trace/assembly/install "{\"parentSerialUnitId\":$S2,\"childSerialUnitId\":$RT}"; check "assembly cycle rejected" '.success == false'
call POST inventory/trace/assembly/install "{\"parentSerialUnitId\":$RT,\"childLotId\":$LOT,\"quantity\":5}"; check "lot material installed in rotor" '.success'
call GET "inventory/trace/serial/$RT"; check "rotor trace: child S-2 + lot H-7781" '(.data.children|map(.serialNumber)|index("S-2")) != null and (.data.lotComponents|map(.childLotNumber)|index("H-7781")) != null'
call GET "inventory/trace/serial/$S2"; check "S-2 trace: parent rotor, 2 operations, receipt + issue moves" ".data.unit.status == 3 and .data.ancestors[0].id == $RT and (.data.operations|length) == 2 and (.data.movements|length) >= 2"
call GET "inventory/trace/lot/$LOT"; check "lot trace: used in rotor (where-used)" "(.data.topAssemblies|map(.id)|index($RT)) != null and (.data.movements|length) >= 2"
call GET "inventory/trace/search?q=S/$C_SHAFT/S-2"; check "scan label S/<code>/S-2" ".data.serials[0].id == $S2"
call GET "inventory/trace/search?q=L/$C_RAW/H-7781"; check "scan label L/<code>/H-7781" ".data.lots[0].id == $LOT"
call GET "inventory/trace/search?q=LOC/WH-RM/A-01"; check "scan label LOC/WH-RM/A-01" ".data.locations[0].id == $L1"

# ---------------------------------------------------------------- cancel rules
call POST "inventory/documents/$TR/cancel" '{"reason":""}'; check "cancel without reason rejected" '.success == false'
call POST "inventory/documents/$IS/cancel" '{"reason":"test"}'; check "cancel of issue blocked (S-2 installed)" '.success == false'
call POST "inventory/documents/$TR/cancel" '{"reason":"انتقال اشتباه"}'; check "transfer cancelled" '.success and .data.status == 2'
call GET "inventory/stock/serials?Contains=S-1&ItemId=$SHAFT"; check "S-1 back in WH-RM" ".data.data[0].warehouseId == $W1"
call GET "inventory/stock/kardex?ItemId=$BOLT"; check "bolt kardex shows reversal, closing 100" '.data.closingBalance == 100 and ([.data.rows[]|select(.isReversal)]|length) >= 1'
call POST inventory/trace/assembly/remove "{\"assemblyLinkId\":$LINK,\"notes\":\"تعویض\"}"; check "S-2 removed from rotor" '.success'
call GET "inventory/stock/serials?Contains=S-2&ItemId=$SHAFT"; check "S-2 back to in-production" '.data.data[0].status == 1'
call DELETE "inventory/items/$BOLT"; check "item with transactions cannot be deleted" '.success == false'
call POST inventory/items "{\"id\":$BOLT,\"code\":\"$C_BOLT\",\"title\":\"پیچ M24\",\"unit\":\"عدد\",\"trackingMode\":1}"; check "tracking mode locked after movements" '.success == false'

# ---------------------------------------------------------------- Excel import
call POST inventory/items/import "[{\"code\":\"BL-200\",\"title\":\"پره ردیف 1\",\"groupCode\":\"TRB-BLD\",\"groupTitle\":\"پره‌ها\",\"unit\":\"عدد\",\"trackingMode\":\"سریال\",\"legacyCode\":\"RD-77\"},{\"code\":\"$C_RAW\",\"title\":\"میلگرد فورج (به‌روز)\",\"unit\":\"kg\",\"trackingMode\":\"بچ\"}]"
check "import: 1 new, 1 updated, 1 group; non-standard code listed as warning" '.success and .data.created == 1 and .data.updated == 1 and .data.groupsCreated == 1 and (.data.warnings|length) == 1'

# ---------------------------------------------------------------- Sepidar
call GET inventory/sepidar/check; check "sepidar check: RC and IS pending (transfer cancelled before export)" '.success and .data.pendingDocuments == 2'
call GET "inventory/sepidar/outbox?Status=1"; check "sepidar outbox" '.data.data|length == 2'
IDS=$(jq -c '[.data.data[].id]' "$B")
export_csv() {
  curl -s -D "$OUT/sepidar.hdr" -o "$OUT/sepidar.csv" -X POST "$API/inventory/sepidar/export" -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d "{\"documentIds\":$1,\"markExported\":true}"
}
export_csv "$IDS"
if grep -qi 'content-type: text/csv' "$OUT/sepidar.hdr" && [ "$(head -c 3 "$OUT/sepidar.csv" | od -An -tx1 | tr -d ' \n')" = "efbbbf" ] && grep -q "RC-" "$OUT/sepidar.csv"; then ok "sepidar CSV export (UTF-8 BOM)"; else cp "$OUT/sepidar.csv" "$B"; bad "sepidar CSV export (UTF-8 BOM)"; fi
cp "$OUT/sepidar.csv" "$OUT/sepidar-1.csv"
call GET "inventory/sepidar/outbox?Status=2"; check "exported documents marked" '.data.data|length == 2'
call POST "inventory/documents/$IS/cancel" '{"reason":"برگشت کامل به انبار"}'; check "exported issue cancelled -> pending for Sepidar again" '.success and .data.sepidarStatus == 1'
export_csv "[$IS]"
cp "$OUT/sepidar.csv" "$OUT/sepidar-2.csv"
grep -q -- "-100" "$OUT/sepidar.csv" && ok "cancelled exported issue goes to Sepidar as reversal" || { cp "$OUT/sepidar.csv" "$B"; bad "cancelled exported issue goes to Sepidar as reversal"; }
call GET "inventory/stock/items?ItemId=$RAW"; check "stock RM-001 back to 500.5" '.data.data[0].onHand == 500.5'
call GET "inventory/stock/serials?Contains=S-2&ItemId=$SHAFT"; check "S-2 back in stock" '.data.data[0].status == 2'
call POST inventory/sepidar/mappings '{"entityType":"WorkOrder","entityId":1,"sepidarCode":"CC-10"}'; check "mapping to missing work order rejected" '.success == false'

# ---------------------------------------------------------------- role checks
call POST Accounts/register-user '{"userName":"fin.ci","name":"مالی","lastName":"آزمون","email":"fin.ci@example.com","roleIds":[3],"password":"Ci#Fin-User-2026","phoneNumber":"09120000000","birthDate":"1990-01-01T00:00:00+03:30","nationalCode":"0012345678"}'
TOKEN=$(login fin.ci 'Ci#Fin-User-2026'); FIN_TOKEN=$TOKEN
if [ -n "$TOKEN" ]; then
  ok "login financial user"
  call POST inventory/documents "{\"type\":1,\"documentDate\":\"2026-10-15T00:00:00\",\"warehouseId\":$W1,\"lines\":[]}"; [ "$CODE" = "403" ] && ok "financial user cannot write documents (403)" || bad "financial user cannot write documents (403) got $CODE"
  call GET inventory/sepidar/check; [ "$CODE" = "200" ] && ok "financial user can use Sepidar page" || bad "financial user can use Sepidar page got $CODE"
  call GET "inventory/stock/items"; [ "$CODE" = "200" ] && ok "financial user can read stock" || bad "financial user can read stock got $CODE"
else
  bad "login financial user"
fi
TOKEN=$ADMIN_TOKEN

# ================================================================ phase 2: standard warehouse flows
TOKEN=$ADMIN_TOKEN
user pmo2.ci 1 'Ci#Pmo2-User-2026' 6
PMO2_TOKEN=$(login pmo2.ci 'Ci#Pmo2-User-2026'); [ -n "$PMO2_TOKEN" ] && ok "login second planning user" || bad "login second planning user"
TOKEN=$ADMIN_TOKEN

# ---------------------------------------------------------------- coding (WI-WH-002)
call GET "inventory/coding/values?scheme=PartStatus"; check "coding tables seeded: 42 part statuses, 5 stockable (★)" '(.data|length) == 42 and ([.data[]|select(.isStockable)]|length) == 5'
call GET "inventory/coding/check?code=02130001010003"; check "part code check: assembly Rotor, final part" '.data.groupCode == "02" and (.data.segments[1].meaning == "Rotor") and .data.stageCode == "03" and .data.errors == ["این کد قبلاً ثبت شده است: 02130001010003"]'
call GET "inventory/coding/check?code=02600001010003"; check "assembly 600 outside every range rejected" '.data.isValid == false'
call GET "inventory/coding/check?code=02130009010014"; check "milling status (14) is not a stockable stage" '.data.stageNotStockable == true and .data.isValid == true'
call GET "inventory/coding/check?code=16.10.05.001.2"; check "MESC code -> group 08" '.data.groupCode == "08" and .data.isValid'
call GET "inventory/coding/check?code=0213000101"; check "short code rejected" '.data.isValid == false'
as "$WH_TOKEN" POST inventory/items '{"code":"02130009010003","title":"قطعه بدون درخواست","unit":"عدد"}'; check "warehouse user cannot create a new code" '.success == false'
call POST inventory/items '{"code":"02130009010014","title":"مرحله فرز","unit":"عدد"}'; check "item with a non-stockable stage rejected (★)" '.success == false'

# new item request: engineering asks, planning approves
as "$ENG_TOKEN" POST inventory/coding/requests '{"title":"پوسته خام ریخته‌گری","groupCode":"02","proposedCode":"02101002010010","technicalCode":"101020100","unit":"عدد","trackingMode":2,"reason":"پروژه آزمایشی"}'
check "item request by engineering" '.success and (.data.number|startswith("IR-")) and .data.status == 0'
IR=$(val .data.id)
as "$ENG_TOKEN" POST inventory/coding/requests/review "{\"id\":$IR,\"approve\":true}"; [ "$CODE" = "403" ] && ok "engineering cannot approve its own request (403)" || bad "engineering cannot approve its own request (403) got $CODE"
call POST inventory/coding/requests/review "{\"id\":$IR,\"approve\":true}"; check "planning approves -> item created" '.success and .data.status == 1 and .data.itemCode == "02101002010010"'
CAST=$(val .data.itemId)
item ROUGH '{"code":"02101002010001","title":"پوسته رافینگ","unit":"عدد","trackingMode":2}' "rough-machined stage of the casing"
call GET "inventory/coding/check?code=02101002010002"; check "other stages of the same part are listed" '(.data.otherStages|length) == 2'
item PLATE '{"code":"01003010060101","title":"ورق SS304","unit":"kg","trackingMode":1}' "SS304 plate (lot, needs inspection)"

# ---------------------------------------------------------------- warehouses of phase 2
call POST inventory/warehouses '{"code":"WH-QA","title":"قرنطینه","kind":5}'; WQ=$(val .data.id)
call POST inventory/warehouses '{"code":"WH-SC","title":"ضایعات","kind":6}'; WS=$(val .data.id)
call POST inventory/warehouses '{"code":"WH-K1","title":"پیمانکار ماشینکاری","kind":7}'; WK=$(val .data.id)
[ "$WQ" != "null" ] && [ "$WS" != "null" ] && [ "$WK" != "null" ] && ok "quarantine, scrap and contractor warehouses" || bad "quarantine, scrap and contractor warehouses"

# ---------------------------------------------------------------- quarantine and incoming inspection
call POST inventory/documents "{\"type\":1,\"documentDate\":\"2026-10-17T00:00:00\",\"warehouseId\":$W1,\"lines\":[{\"itemId\":$PLATE,\"quantity\":10}]}"
check "receipt of an item that needs inspection outside quarantine rejected" '.success == false'
call POST inventory/documents "{\"type\":1,\"documentDate\":\"2026-10-17T00:00:00\",\"warehouseId\":$WQ,\"destinationWarehouseId\":$W1,\"referenceNumber\":\"INV-501\",\"lines\":[
 {\"itemId\":$PLATE,\"quantity\":50,\"heatNumber\":\"HT-55\",\"unitPrice\":2500000},
 {\"itemId\":$CAST,\"quantity\":3,\"serialNumbers\":[\"C-1\",\"C-2\",\"C-3\"]}]}"
RQ=$(val .data.id)
call POST "inventory/documents/$RQ/confirm"; check "receipt into quarantine confirmed" '.success and .data.status == 1'
call GET "inventory/stock/lots?ItemId=$PLATE"; check "lot gets the label number YYDDDNNNN" '(.data.data[0].lotNumber|test("^[0-9]{9}$")) and .data.data[0].heatNumber == "HT-55"'
PLOT=$(val '.data.data[0].id')
call POST inventory/documents "{\"type\":6,\"documentDate\":\"2026-10-17T00:00:00\",\"warehouseId\":$WQ,\"destinationWarehouseId\":$W1,\"lines\":[{\"itemId\":$PLATE,\"quantity\":1,\"lotId\":$PLOT}]}"
check "transfer out of quarantine without QC rejected" '.success == false'
call GET "inventory/quality/inspections?Status=0"; check "two inspections waiting" '.data.data|length == 2'
QI_P=$(jq -r ".data.data[]|select(.itemId==$PLATE)|.id" "$B"); QI_C=$(jq -r ".data.data[]|select(.itemId==$CAST)|.id" "$B")
call POST inventory/quality/inspections "{\"id\":$QI_P,\"acceptedQuantity\":45,\"rejectedQuantity\":5}"; [ "$CODE" = "200" ] && check "QC by planning user: reject reason is required" '.success == false' || bad "QC call failed $CODE"
as "$WH_TOKEN" POST inventory/quality/inspections "{\"id\":$QI_P,\"acceptedQuantity\":45,\"rejectedQuantity\":5,\"rejectReason\":\"x\"}"; [ "$CODE" = "403" ] && ok "warehouse user cannot record inspection (403)" || bad "warehouse user cannot record inspection (403) got $CODE"
as "$QC_TOKEN" POST inventory/quality/inspections "{\"id\":$QI_P,\"acceptedQuantity\":45,\"rejectedQuantity\":5,\"reportNumber\":\"QC-IR-12\",\"rejectReason\":\"ترک سطحی در لبه ورق\"}"
check "plate inspection: 45 released, 5 to NCR" '.success and .data.status == 1 and (.data.releaseDocumentNumber|startswith("QR-")) and (.data.ncrNumber|startswith("NCR-"))'
NCR1=$(val .data.ncrId)
as "$QC_TOKEN" POST inventory/quality/inspections "{\"id\":$QI_C,\"acceptedQuantity\":2,\"rejectedQuantity\":1,\"rejectedSerialNumbers\":[\"C-3\"],\"rejectReason\":\"تخلخل در فلنج\"}"
check "casting inspection: C-3 rejected" '.success and .data.rejectedSerialNumbers == ["C-3"]'
NCR2=$(val .data.ncrId)
call GET "inventory/stock?ItemId=$PLATE"; check "plate: 45 in main warehouse, 5 in quarantine" "([.data.data[]|select(.warehouseId==$W1)|.quantity]|add) == 45 and ([.data.data[]|select(.warehouseId==$WQ)|.quantity]|add) == 5"
call POST "inventory/documents/$RQ/cancel" '{"reason":"test"}'; check "receipt with finished inspections cannot be cancelled" '.success == false'

# ---------------------------------------------------------------- NCR (PR-QC-002)
ncr() { as "$QC_TOKEN" POST inventory/quality/ncr "$1"; }
ncr "{\"id\":$NCR1,\"severity\":1,\"disposition\":4,\"source\":1,\"causeCategory\":2,\"rootCause\":\"ورق معیوب از تامین‌کننده\"}"; check "NCR1 classified: minor, scrap" '.success and .data.severity == 1 and .data.disposition == 4'
as "$QC_TOKEN" POST inventory/quality/ncr/approve "{\"id\":$NCR1,\"kind\":4}"; check "NCR1 signed by QC" '.success and .data.qualityApprovedAt != null and .data.status == 0'
as "$QC_TOKEN" POST inventory/quality/ncr/approve "{\"id\":$NCR1,\"kind\":5}"; check "QC cannot sign for engineering" '.success == false'
as "$ENG_TOKEN" POST inventory/quality/ncr/approve "{\"id\":$NCR1,\"kind\":5}"; check "NCR1 signed by engineering" '.success'
as "$PROD_TOKEN" POST inventory/quality/ncr/approve "{\"id\":$NCR1,\"kind\":3}"; check "NCR1 signed by manufacturing -> approved" '.success and .data.status == 1'
as "$QC_TOKEN" POST inventory/quality/ncr/execute "{\"id\":$NCR1,\"targetWarehouseId\":$W1}"; check "scrap disposition needs the scrap warehouse" '.success == false'
as "$QC_TOKEN" POST inventory/quality/ncr/execute "{\"id\":$NCR1,\"targetWarehouseId\":$WS}"; check "NCR1 executed: moved to scrap" '.success and .data.status == 2 and (.data.resultDocumentNumber|startswith("TR-"))'
call GET "inventory/stock?ItemId=$PLATE"; check "plate: 5 in scrap" "([.data.data[]|select(.warehouseId==$WS)|.quantity]|add) == 5"
as "$QC_TOKEN" POST inventory/quality/ncr/close "{\"id\":$NCR1,\"effectivenessCheck\":\"ok\"}"; check "close without corrective action rejected" '.success == false'
ncr "{\"id\":$NCR1,\"severity\":1,\"disposition\":4,\"source\":1,\"causeCategory\":2,\"rootCause\":\"ورق معیوب\",\"correctiveAction\":\"بازرسی اولتراسونیک در محل تامین‌کننده\"}"
as "$QC_TOKEN" POST inventory/quality/ncr/close "{\"id\":$NCR1,\"effectivenessCheck\":\"دو محموله بعدی بدون عیب\"}"; check "NCR1 closed" '.success and .data.status == 3'

ncr "{\"id\":$NCR2,\"severity\":2,\"disposition\":1,\"source\":1,\"customerApprovalRequired\":true}"
as "$QC_TOKEN" POST inventory/quality/ncr/approve "{\"id\":$NCR2,\"kind\":4}"; check "major NCR without committee minutes rejected" '.success == false'
ncr "{\"id\":$NCR2,\"severity\":2,\"disposition\":1,\"source\":1,\"customerApprovalRequired\":true,\"committeeMinutesNumber\":\"TC-14\",\"committeeMembers\":\"QC، مهندسی، تولید\",\"concessionNumber\":\"CN-3\"}"
as "$QC_TOKEN" POST inventory/quality/ncr/approve "{\"id\":$NCR2,\"kind\":4}"; as "$ENG_TOKEN" POST inventory/quality/ncr/approve "{\"id\":$NCR2,\"kind\":5}"
as "$PROD_TOKEN" POST inventory/quality/ncr/approve "{\"id\":$NCR2,\"kind\":3}"; check "concession still needs its permit" '.success and .data.status == 0'
as "$ENG_TOKEN" POST inventory/quality/ncr/approve "{\"id\":$NCR2,\"concession\":true}"; check "concession without customer approval rejected (contract requires it)" '.success == false'
ncr "{\"id\":$NCR2,\"severity\":2,\"disposition\":1,\"source\":1,\"customerApprovalRequired\":true,\"committeeMinutesNumber\":\"TC-14\",\"committeeMembers\":\"QC، مهندسی، تولید\",\"concessionNumber\":\"CN-3\",\"customerApprovalReference\":\"نامه کارفرما 55/2\"}"
check "signatures kept when only the customer reference is added" '.data.qualityApprovedAt != null'
as "$ENG_TOKEN" POST inventory/quality/ncr/approve "{\"id\":$NCR2,\"concession\":true}"; check "concession issued -> approved" '.success and .data.status == 1'
as "$QC_TOKEN" POST inventory/quality/ncr/execute "{\"id\":$NCR2,\"targetWarehouseId\":$W1}"; check "NCR2 executed: C-3 released with concession" '.success and .data.status == 2 and (.data.resultDocumentNumber|startswith("QR-"))'
call GET "inventory/stock/serials?Contains=C-3&ItemId=$CAST"; check "C-3 in the main warehouse" ".data.data[0].warehouseId == $W1 and .data.data[0].status == 2"
call POST inventory/documents "{\"type\":6,\"documentDate\":\"2026-10-17T00:00:00\",\"warehouseId\":$W1,\"destinationWarehouseId\":$WS,\"lines\":[{\"itemId\":$BOLT,\"quantity\":1}]}"
check "transfer to scrap without NCR rejected" '.success == false'

# ---------------------------------------------------------------- stage conversion (label genealogy)
call POST inventory/documents "{\"type\":12,\"documentDate\":\"2026-10-18T00:00:00\",\"warehouseId\":$W1,\"lines\":[{\"itemId\":$CAST,\"quantity\":1,\"serialNumbers\":[\"C-1\"],\"direction\":1},{\"itemId\":$ROUGH,\"quantity\":1,\"direction\":2}]}"
check "conversion without source row rejected" '.success == false'
call POST inventory/documents "{\"type\":12,\"documentDate\":\"2026-10-18T00:00:00\",\"warehouseId\":$W1,\"lines\":[{\"itemId\":$CAST,\"quantity\":1,\"serialNumbers\":[\"C-1\"],\"direction\":1},{\"itemId\":$ROUGH,\"quantity\":1,\"direction\":2,\"sourceRowNumber\":1}]}"
SC=$(val .data.id); call POST "inventory/documents/$SC/confirm"; check "stage conversion confirmed" '.success and (.data.number|startswith("SC-"))'
call GET "inventory/stock/serials?ItemId=$ROUGH"; check "new label linked to the casting label C-1" '(.data.data[0].serialNumber|test("^[0-9]{9}$")) and .data.data[0].sourceSerialNumber == "C-1"'
call GET "inventory/stock/serials?Contains=C-1&ItemId=$CAST"; check "C-1 marked converted" '.data.data[0].status == 6'

# ---------------------------------------------------------------- subcontractor
call POST ProviderCategories '{"categoryName":"پیمانکاران ماشینکاری"}'; CAT=$(jq -r '.data.id // .id // empty' "$B")
call POST Provider "{\"name\":\"کارگاه ماشینکاری آزمون\",\"categoryId\":${CAT:-0}}"; PRV=$(jq -r '.data.id // empty' "$B")
[ -n "$PRV" ] && ok "subcontractor created" || bad "subcontractor created"
call POST inventory/documents "{\"type\":9,\"documentDate\":\"2026-10-18T00:00:00\",\"warehouseId\":$W1,\"destinationWarehouseId\":$WK,\"lines\":[{\"itemId\":$CAST,\"quantity\":1,\"serialNumbers\":[\"C-2\"]}]}"
check "subcontract issue without contractor rejected" '.success == false'
call POST inventory/documents "{\"type\":9,\"documentDate\":\"2026-10-18T00:00:00\",\"warehouseId\":$W1,\"destinationWarehouseId\":$WK,\"providerId\":${PRV:-0},\"lines\":[{\"itemId\":$CAST,\"quantity\":1,\"serialNumbers\":[\"C-2\"]}]}"
SI=$(val .data.id); call POST "inventory/documents/$SI/confirm"; check "subcontract issue needs driver, plate, receiver" '.success == false'
call POST inventory/documents "{\"id\":$SI,\"type\":9,\"documentDate\":\"2026-10-18T00:00:00\",\"warehouseId\":$W1,\"destinationWarehouseId\":$WK,\"providerId\":${PRV:-0},\"driverName\":\"احمدی\",\"vehiclePlate\":\"12ع345-67\",\"receiverName\":\"مدیر کارگاه\",\"lines\":[{\"itemId\":$CAST,\"quantity\":1,\"serialNumbers\":[\"C-2\"]}]}"
call POST "inventory/documents/$SI/confirm"; check "subcontract issue confirmed: C-2 at the contractor" '.success and (.data.number|startswith("SI-"))'
as "$SEC_TOKEN" POST "inventory/documents/$SI/exit-check" '{"note":"بازدید شد"}'; check "guard records the exit" '.success and .data.exitCheckedAt != null'
as "$SEC_TOKEN" POST "inventory/documents/$SI/exit-check" '{}'; check "second exit check rejected" '.success == false'
call POST "inventory/documents/$SI/cancel" '{"reason":"x"}'; check "document that left the premises cannot be cancelled" '.success == false'
call POST inventory/documents "{\"type\":6,\"documentDate\":\"2026-10-18T00:00:00\",\"warehouseId\":$WK,\"destinationWarehouseId\":$W1,\"lines\":[{\"itemId\":$CAST,\"quantity\":1,\"serialNumbers\":[\"C-2\"]}]}"
check "plain transfer out of the contractor warehouse rejected" '.success == false'
call POST inventory/documents "{\"type\":10,\"documentDate\":\"2026-10-19T00:00:00\",\"warehouseId\":$WK,\"destinationWarehouseId\":$WQ,\"providerId\":${PRV:-0},\"lines\":[{\"itemId\":$CAST,\"quantity\":1,\"serialNumbers\":[\"C-2\"]}]}"
SB=$(val .data.id); call POST "inventory/documents/$SB/confirm"; check "return from contractor into quarantine" '.success and (.data.number|startswith("SB-"))'
call GET "inventory/quality/inspections?Status=0"; check "return from contractor waits for inspection" '.data.data|length == 1'

# ---------------------------------------------------------------- customer delivery needs a contract and QC loading approval
call POST inventory/documents "{\"type\":11,\"documentDate\":\"2026-10-19T00:00:00\",\"warehouseId\":$W1,\"lines\":[{\"itemId\":$BOLT,\"quantity\":1}]}"
check "customer delivery without contract rejected" '.success == false'

# ---------------------------------------------------------------- adjustment: warehouse supervisor + planning
call POST inventory/documents "{\"type\":7,\"documentDate\":\"2026-10-19T00:00:00\",\"warehouseId\":$W1,\"lines\":[{\"itemId\":$BOLT,\"quantity\":5,\"locationId\":$L2}]}"
AI=$(val .data.id)
call POST "inventory/documents/$AI/confirm"; check "adjustment without approvals rejected" '.success == false'
call POST "inventory/documents/$AI/approve" '{"kind":1}'; check "planning user cannot approve as warehouse" '.success == false'
call POST "inventory/documents/$AI/approve" '{"kind":2}'; check "creator cannot approve" '.success == false'
as "$WH_TOKEN" POST "inventory/documents/$AI/approve" '{"kind":1}'; check "storekeeper cannot approve an adjustment (segregation)" '.success == false'
as "$SUP_TOKEN" POST "inventory/documents/$AI/approve" '{"kind":1}'; check "warehouse supervisor approves" '.success and .data.warehouseApprovedAt != null'
as "$SUP_TOKEN" POST "inventory/documents/$AI/approve" '{"kind":2}'; check "supervisor cannot also approve for planning" '.success == false'
as "$PLAN_TOKEN" POST "inventory/documents/$AI/approve" '{"kind":2}'; check "planning manager approves" '.success and .data.planningApprovedAt != null'
call POST "inventory/documents/$AI/confirm"; check "approved adjustment confirmed" '.success and (.data.number|startswith("AI-"))'

# ---------------------------------------------------------------- scrap sale (WI-WH-004)
call POST inventory/documents "{\"type\":13,\"documentDate\":\"2026-10-19T00:00:00\",\"warehouseId\":$W1,\"lines\":[{\"itemId\":$BOLT,\"quantity\":1}]}"
check "scrap receipt outside the scrap warehouse rejected" '.success == false'
call POST inventory/documents "{\"type\":14,\"documentDate\":\"2026-10-20T00:00:00\",\"warehouseId\":$WS,\"driverName\":\"کریمی\",\"vehiclePlate\":\"55ب123-11\",\"receiverName\":\"خریدار\",\"lines\":[{\"itemId\":$PLATE,\"quantity\":5,\"lotId\":$PLOT}]}"
WSD=$(val .data.id); call POST "inventory/documents/$WSD/confirm"; check "scrap sale without buyer, minutes, weighbridge, deposit rejected" '.success == false and (.message|contains("باسکول"))'
call POST inventory/documents "{\"id\":$WSD,\"type\":14,\"documentDate\":\"2026-10-20T00:00:00\",\"warehouseId\":$WS,\"driverName\":\"کریمی\",\"vehiclePlate\":\"55ب123-11\",\"receiverName\":\"خریدار\",\"buyerName\":\"بازیافت آزمون\",\"minutesNumber\":\"FO-WH-020/7\",\"weighbridgeDocumentId\":\"$DOCID\",\"depositReference\":\"واریز 99887\",\"priceQuotes\":\"بازیافت آزمون: 50000000 | خریدار دوم: 45000000\",\"lines\":[{\"itemId\":$PLATE,\"quantity\":5,\"lotId\":$PLOT}]}"
call POST "inventory/documents/$WSD/confirm"; check "scrap sale with two quotes rejected" '.success == false and (.message|contains("سه خریدار"))'
call POST inventory/documents "{\"id\":$WSD,\"type\":14,\"documentDate\":\"2026-10-20T00:00:00\",\"warehouseId\":$WS,\"driverName\":\"کریمی\",\"vehiclePlate\":\"55ب123-11\",\"receiverName\":\"خریدار\",\"buyerName\":\"بازیافت آزمون\",\"minutesNumber\":\"FO-WH-020/7\",\"weighbridgeDocumentId\":\"$DOCID\",\"depositReference\":\"واریز 99887\",\"priceQuotes\":\"بازیافت آزمون: 50000000 | خریدار دوم: 45000000 | خریدار سوم: 41000000\",\"lines\":[{\"itemId\":$PLATE,\"quantity\":5,\"lotId\":$PLOT}]}"
call POST "inventory/documents/$WSD/confirm"; check "scrap sale without finance approval rejected" '.success == false and (.message|contains("مالی"))'
call POST "inventory/documents/$WSD/approve" '{"kind":6}'; check "creator cannot approve the scrap sale for finance" '.success == false'
as "$FIN_TOKEN" POST "inventory/documents/$WSD/approve" '{"kind":6}'; check "finance approves price and deposit" '.success and .data.financeApprovedAt != null'
call POST "inventory/documents/$WSD/confirm"; check "scrap sale confirmed" '.success and (.data.number|startswith("WS-")) and .data.depositReference == "واریز 99887"'

# ---------------------------------------------------------------- cycle count of the standard parts
call POST inventory/counts "{\"kind\":1,\"countDate\":\"2026-10-20T00:00:00\",\"warehouseId\":$W1,\"groupCode\":\"50.\"}"; check "cycle count started with a snapshot" '.success and (.data.number|startswith("CNT-")) and .data.linesCount >= 1'
CNT=$(val .data.id)
LINES=$(jq -c '[.data.lines|to_entries[]|{lineId:.value.id,countedQuantity:(if .key==0 then .value.systemQuantity-3 else .value.systemQuantity end)}]' "$B")
call POST inventory/counts "{\"kind\":1,\"countDate\":\"2026-10-20T00:00:00\",\"warehouseId\":$W1}"; check "second open count of the same warehouse rejected" '.success == false'
call POST inventory/counts/action "{\"id\":$CNT,\"action\":\"submit\"}"; check "submit before every line is counted rejected" '.success == false'
call POST inventory/counts/lines "{\"id\":$CNT,\"lines\":$LINES}"; check "counts saved: one difference of -3" '.success and .data.differencesCount == 1'
call POST inventory/counts/action "{\"id\":$CNT,\"action\":\"submit\"}"; check "count submitted" '.success and .data.status == 1'
call POST inventory/counts/action "{\"id\":$CNT,\"action\":\"post\"}"; check "post before approvals rejected" '.success == false'
as "$WH_TOKEN" POST inventory/counts/action "{\"id\":$CNT,\"action\":\"approve-warehouse\"}"; check "storekeeper cannot approve the count" '.success == false'
as "$SUP_TOKEN" POST inventory/counts/action "{\"id\":$CNT,\"action\":\"approve-warehouse\"}"; check "count approved by warehouse supervisor" '.success'
as "$PLAN_TOKEN" POST inventory/counts/action "{\"id\":$CNT,\"action\":\"approve-planning\"}"; check "count approved by planning" '.success and .data.status == 2'
call GET "inventory/stock/items?ItemId=$BOLT"; BEFORE=$(val '.data.data[0].onHand')
call POST inventory/counts/action "{\"id\":$CNT,\"action\":\"post\"}"; check "count posted: decrease adjustment made" '.success and .data.status == 3 and (.data.decreaseDocumentNumber|startswith("AD-"))'
call GET "inventory/stock/items?ItemId=$BOLT"; check "bolt stock lowered by 3" ".data.data[0].onHand == ($BEFORE - 3)"

# annual count freezes the warehouse
call POST inventory/counts "{\"kind\":2,\"countDate\":\"2026-10-21T00:00:00\",\"warehouseId\":$W2}"; ANN=$(val .data.id)
call POST inventory/documents "{\"type\":6,\"documentDate\":\"2026-10-21T00:00:00\",\"warehouseId\":$W1,\"destinationWarehouseId\":$W2,\"lines\":[{\"itemId\":$BOLT,\"quantity\":1}]}"
D=$(val .data.id); call POST "inventory/documents/$D/confirm"; check "warehouse frozen during the annual count" '.success == false and (.message|contains("انبارگردانی"))'
call POST inventory/counts/action "{\"id\":$ANN,\"action\":\"cancel\"}"
call POST "inventory/documents/$D/confirm"; check "after the count is cancelled the transfer is confirmed" '.success'

# ---------------------------------------------------------------- opening stock from the old warehouse data
call GET "inventory/items/$BOLT"; BOLT_CODE=$(val .data.code)
OPEN_ROWS="[{\"itemCode\":\"$BOLT_CODE\",\"warehouseCode\":\"WH-RM\",\"locationCode\":\"Z9/1\",\"quantity\":7,\"labelNumber\":\"041940002\"},{\"itemCode\":\"02999999999999\",\"warehouseCode\":\"WH-RM\",\"quantity\":1}]"
call POST inventory/documents/import-opening "{\"documentDate\":\"2026-10-21T00:00:00\",\"rows\":$OPEN_ROWS}"; check "opening import with an unknown item creates nothing" '.success and (.data.documentIds|length) == 0 and (.data.errors|length) == 1'
call POST inventory/documents/import-opening "{\"documentDate\":\"2026-10-21T00:00:00\",\"importValidRows\":true,\"rows\":$OPEN_ROWS}"; check "opening import of the valid rows: one draft, location created" '.success and (.data.documentIds|length) == 1 and .data.locationsCreated == 1'
OB=$(val '.data.documentIds[0]')
call POST "inventory/documents/$OB/confirm"; check "opening balance needs the two approvals" '.success == false'
as "$SUP_TOKEN" POST "inventory/documents/$OB/approve" '{"kind":1}'; as "$PLAN_TOKEN" POST "inventory/documents/$OB/approve" '{"kind":2}'
call POST "inventory/documents/$OB/confirm"; check "approved opening balance confirmed" '.success and (.data.number|startswith("OB-"))'

# ---------------------------------------------------------------- roles of the new users
as "$SEC_TOKEN" POST inventory/documents "{\"type\":1,\"documentDate\":\"2026-10-15T00:00:00\",\"warehouseId\":$W1,\"lines\":[]}"; [ "$CODE" = "403" ] && ok "security user cannot write documents (403)" || bad "security user cannot write documents (403) got $CODE"
as "$PROD_TOKEN" POST inventory/material-requests "{\"requestDate\":\"2026-10-21T00:00:00\",\"purpose\":\"تعمیرات\",\"lines\":[{\"itemId\":$BOLT,\"quantity\":2}]}"; check "production user writes a material request" '.success'
TOKEN=$ADMIN_TOKEN

echo "--- inventory: $pass passed, $fail failed" | tee -a "$R"
rm -f "$B"
kill "$APP_PID" 2>/dev/null; sleep 3; kill -9 "$APP_PID" 2>/dev/null || true
exit 0
