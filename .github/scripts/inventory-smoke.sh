#!/usr/bin/env bash
# End-to-end test of the warehouse / part traceability / Sepidar module against the CI database.
# Starts the API, runs a full scenario through the HTTP API and writes PASS/FAIL lines to
# smoke-output/7-inventory.txt (response bodies of failed checks go to 7-inventory-fail.txt).
set -u
# a script that stops before its summary line (unset variable, killed, syntax error) must not look like a pass
finished=0; PL=0; CL=0; EXITLINE=0
trap 'PL=$CL; CL=$LINENO' DEBUG
on_exit() {
  local code=${EC:-0}
  trap - DEBUG
  if [ "$finished" != 1 ]; then
    echo "FAIL script ended early (last line $EXITLINE, exit code $code)" | tee -a "${R:-/dev/null}"
    echo "--- inventory: ${pass:-0} passed, $(( ${fail:-0} + 1 )) failed (script ended early)" | tee -a "${R:-/dev/null}"
    [ -n "${APP_PID:-}" ] && kill -9 "$APP_PID" 2>/dev/null
    exit 1
  fi
}
trap 'EC=$?; EXITLINE=$PL; on_exit' EXIT
OUT="$PWD/smoke-output"
BASE=http://127.0.0.1:5080
API=$BASE/api/v1
R="$OUT/7-inventory.txt"
F="$OUT/7-inventory-fail.txt"
B="$OUT/inv-body.tmp"
mkdir -p "$OUT"; : > "$R"; : > "$F"

cd src/WebApi
Finance__AutoPost=false dotnet bin/Release/net10.0/WebApi.dll > "$OUT/7-inventory-app.log" 2>&1 &
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
# the API description must be generated (two DTO classes with the same name break it and the installer waits for it)
curl -s "$BASE/swagger/v1/swagger.json" -o "$B"; check "API description (swagger) is generated without schema name conflicts" '(.paths|length) > 100'
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
user com.ci 2 'Ci#Com-User-2026' 9
call POST Accounts/register-user "{\"userName\":\"ceo.ci\",\"name\":\"ceo.ci\",\"lastName\":\"آزمون\",\"email\":\"ceo.ci@example.com\",\"roleIds\":[13],\"password\":\"Ci#Ceo-User-2026\",\"phoneNumber\":\"09120000019\",\"birthDate\":\"1990-01-01T00:00:00+03:30\",\"nationalCode\":\"0012345619\"}"
COM_TOKEN=$(login com.ci 'Ci#Com-User-2026'); CEO_TOKEN=$(login ceo.ci 'Ci#Ceo-User-2026')
[ -n "$COM_TOKEN" ] && [ -n "$CEO_TOKEN" ] && ok "login commercial and CEO users" || bad "login commercial and CEO users"
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

# go-live switch: receipts without a PMIS purchase order are allowed until procurement is switched on (tested at the end)
call GET procurement/settings; check "procurement settings seeded from the procedures" '.success and ([.data[].key]|index("PettyCashLimit")) != null'
call POST procurement/settings '{"key":"EnforcePurchaseOrder","value":0}'; check "purchase order rule switched off for go-live" '.success and .data.value == 0'

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
check "major NCR: the system drafts the CAPA and the decision due date is set (14 days, company setting)" '.success and .data.capaId != null and (.data.capaNumber|startswith("CAPA-")) and .data.dueDate != null and .data.severity == 2'
as "$QC_TOKEN" POST inventory/quality/ncr/approve "{\"id\":$NCR2,\"kind\":4}"; check "major NCR without committee minutes rejected" '.success == false'
ncr "{\"id\":$NCR2,\"severity\":2,\"disposition\":1,\"source\":1,\"customerApprovalRequired\":true,\"committeeMinutesNumber\":\"TC-14\",\"committeeMembers\":\"QC، مهندسی\",\"concessionNumber\":\"CN-3\"}"
as "$QC_TOKEN" POST inventory/quality/ncr/approve "{\"id\":$NCR2,\"kind\":4}"; check "major NCR with fewer than three committee members rejected" '.success == false'
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

# ---------------------------------------------------------------- cost accounting and the Sepidar link
TOKEN=$ADMIN_TOKEN
call GET "finance/events?PageSize=200"; check "accounting events written for confirmed documents" '.success and (.data.data|length) > 5'
check "every accounting event is balanced" '[.data.data[] | ([.lines[].debit]|add) == ([.lines[].credit]|add)] | all'
call GET finance/valuation; check "stock value equals the inventory balance of the events" '.success and .data.totalValue > 0 and .data.totalValue == .data.totalEventsBalance'
call GET finance/accounts; check "account roles seeded for Sepidar mapping" '.success and ([.data[].role]|index("GRNI")) != null'
call POST finance/accounts '{"role":"Inventory","title":"موجودی کالا","sepidarAccountCode":"1-105","isActive":true}'; check "Sepidar account code saved" '.success'
call GET finance/check; check "Sepidar readiness check lists unmapped roles" '.success and (.data.unmappedRoles|length) > 0'
CODE=$(curl -s -o "$B" -w '%{http_code}' -X POST "$API/finance/events/export" -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' --data-binary '{"markExported":false}')
[ "$CODE" = "200" ] && grep -q "کلید رویداد" "$B" && ok "voucher file for Sepidar" || bad "voucher file for Sepidar ($CODE)"
call GET finance/costs; check "cost report of work orders" '.success'

# ---------------------------------------------------------------- procurement: request -> inquiry -> order -> receipt -> invoice -> payment
TOKEN=$ADMIN_TOKEN
call POST Provider "{\"name\":\"تامین‌کننده الف آزمون\",\"categoryId\":${CAT:-0}}"; PA=$(jq -r '.data.id // empty' "$B")
call POST Provider "{\"name\":\"تامین‌کننده ب آزمون\",\"categoryId\":${CAT:-0}}"; PB=$(jq -r '.data.id // empty' "$B")
[ -n "$PA" ] && [ -n "$PB" ] && ok "two suppliers created" || bad "two suppliers created"
as "$WH_TOKEN" POST procurement/requests "{\"source\":1,\"kind\":1,\"requestDate\":\"2026-10-22T00:00:00\",\"lines\":[{\"itemId\":$BOLT,\"quantity\":100}]}"
check "only planning writes a BOM purchase request" '.success == false'
as "$WH_TOKEN" POST procurement/requests "{\"source\":2,\"kind\":1,\"requestDate\":\"2026-10-22T00:00:00\",\"description\":\"نقطه سفارش پیچ\",\"submit\":true,\"lines\":[{\"itemId\":$BOLT,\"quantity\":100,\"estimatedUnitPrice\":400000,\"technicalRequirements\":\"ISO 4014 گرید 8.8 با گواهی 3.1\"}]}"
check "purchase request submitted (40M rial)" '.success and .data.status == 1 and .data.estimatedAmount == 40000000'
PRQ=$(val .data.id); PRL=$(val '.data.lines[0].id')
as "$WH_TOKEN" POST procurement/requests/action "{\"id\":$PRQ,\"action\":\"approve-planning\"}"; check "requester cannot approve own request" '.success == false'
as "$PLAN_TOKEN" POST procurement/requests/action "{\"id\":$PRQ,\"action\":\"approve-planning\"}"; check "planning approved; medium purchase still waits for the CEO" '.success and .data.status == 1'
as "$CEO_TOKEN" POST procurement/requests/action "{\"id\":$PRQ,\"action\":\"approve-management\"}"; check "CEO approval completes the request" '.success and .data.status == 2'
as "$COM_TOKEN" POST procurement/suppliers/evaluate "{\"providerId\":$PA,\"kind\":1,\"evaluationDate\":\"2026-10-22T00:00:00\",\"lines\":[{\"question\":\"کیفیت\",\"weight\":3,\"score\":90},{\"question\":\"تحویل به موقع\",\"weight\":2,\"score\":88},{\"question\":\"سابقه\",\"weight\":1}]}"
check "supplier evaluation: weighted score and grade A" '.success and .data.grade == 1 and .data.score == 89.2'
QA="{\"providerId\":$PA,\"quoteNumber\":\"Q-A\",\"vatPercent\":10,\"deliveryDays\":20,\"technicallyAccepted\":true,\"lines\":[{\"requestLineId\":$PRL,\"quantity\":100,\"unitPrice\":390000}]}"
QB="{\"providerId\":$PB,\"quoteNumber\":\"Q-B\",\"vatPercent\":10,\"technicallyAccepted\":true,\"lines\":[{\"requestLineId\":$PRL,\"quantity\":100,\"unitPrice\":380000}]}"
as "$WH_TOKEN" POST procurement/inquiries "{\"purchaseRequestId\":$PRQ,\"quotes\":[$QA]}"; check "only commercial takes quotes" '.success == false'
as "$COM_TOKEN" POST procurement/inquiries "{\"purchaseRequestId\":$PRQ,\"inquiryDate\":\"2026-10-22T00:00:00\",\"quotes\":[$QA]}"
check "inquiry needs two quotes for 40M rial" '.success and .data.requiredQuotes == 2'
INQ=$(val .data.id); QAID=$(val '.data.quotes[0].id')
as "$COM_TOKEN" POST procurement/inquiries/select "{\"inquiryId\":$INQ,\"quoteId\":$QAID}"; check "one quote is not enough" '.success == false'
as "$COM_TOKEN" POST procurement/inquiries "{\"id\":$INQ,\"purchaseRequestId\":$PRQ,\"inquiryDate\":\"2026-10-22T00:00:00\",\"quotes\":[$QA,$QB]}"; check "second quote added" '.success and (.data.quotes|length) == 2'
QAID=$(val ".data.quotes[] | select(.providerId == $PA) | .id")
as "$COM_TOKEN" POST procurement/inquiries/select "{\"inquiryId\":$INQ,\"quoteId\":$QAID}"; check "choosing a dearer quote needs a reason" '.success == false'
as "$COM_TOKEN" POST procurement/inquiries/select "{\"inquiryId\":$INQ,\"quoteId\":$QAID,\"reason\":\"گرید A و تحویل 20 روزه\"}"; check "quote selected, inquiry approved" '.success and .data.status == 2'
as "$COM_TOKEN" POST "procurement/orders/from-inquiry/$INQ"; check "purchase order draft from the inquiry" '.success and .data.status == 0 and .data.totalAmount == 39000000'
PO=$(val .data.id); POL=$(val '.data.lines[0].id')
as "$COM_TOKEN" POST procurement/orders/action "{\"id\":$PO,\"action\":\"approve\"}"; check "buyer cannot approve the order" '.success == false'
call POST procurement/orders/action "{\"id\":$PO,\"action\":\"approve\"}"; check "order approved by PMO" '.success and .data.status == 1'
as "$COM_TOKEN" POST procurement/orders/action "{\"id\":$PO,\"action\":\"release\"}"; check "order released to the supplier" '.success and .data.status == 2'
call GET "procurement/requests/$PRQ"; check "request marked ordered" '.data.status == 3'

call POST inventory/documents "{\"type\":1,\"documentDate\":\"2026-10-22T00:00:00\",\"warehouseId\":$W1,\"purchaseOrderId\":$PO,\"referenceNumber\":\"حواله الف-1\",\"lines\":[{\"itemId\":$BOLT,\"quantity\":60,\"locationId\":$L2,\"purchaseOrderLineId\":$POL}]}"
D=$(val .data.id); call POST "inventory/documents/$D/confirm"
check "receipt against the order: supplier and order price taken" ".success and .data.providerId == $PA and .data.lines[0].unitPrice == 390000"
call GET "procurement/orders/$PO"; check "order partly received (60 of 100)" '.data.status == 3 and .data.lines[0].receivedQuantity == 60'
call POST inventory/documents "{\"type\":1,\"documentDate\":\"2026-10-22T00:00:00\",\"warehouseId\":$W1,\"purchaseOrderId\":$PO,\"lines\":[{\"itemId\":$BOLT,\"quantity\":50,\"locationId\":$L2,\"purchaseOrderLineId\":$POL}]}"
D2=$(val .data.id); call POST "inventory/documents/$D2/confirm"; check "receipt above the open order quantity rejected" '.success == false'
call DELETE "inventory/documents/$D2"

as "$COM_TOKEN" POST procurement/invoices "{\"purchaseOrderId\":$PO,\"invoiceNumber\":\"F-1001\",\"invoiceDate\":\"2026-10-23T00:00:00\",\"isOfficial\":true,\"lines\":[{\"orderLineId\":$POL,\"quantity\":70,\"unitPrice\":390000}]}"
check "invoice above the received quantity rejected (three-way match)" '.success == false'
as "$COM_TOKEN" POST procurement/invoices "{\"purchaseOrderId\":$PO,\"invoiceNumber\":\"F-1001\",\"invoiceDate\":\"2026-10-23T00:00:00\",\"isOfficial\":true,\"lines\":[{\"orderLineId\":$POL,\"quantity\":60,\"unitPrice\":390000}]}"
check "invoice matched with order and receipt" '.success and .data.status == 1 and .data.totalAmount == 25740000'
SINV=$(val .data.id)
as "$COM_TOKEN" POST procurement/invoices/action "{\"id\":$SINV,\"action\":\"approve\"}"; check "invoice creator cannot approve it" '.success == false'
as "$FIN_TOKEN" POST procurement/invoices/action "{\"id\":$SINV,\"action\":\"approve\"}"; check "finance approves the invoice" '.success and .data.status == 3'
as "$COM_TOKEN" POST procurement/invoices "{\"purchaseOrderId\":$PO,\"invoiceNumber\":\"F-1001\",\"invoiceDate\":\"2026-10-23T00:00:00\",\"lines\":[{\"orderLineId\":$POL,\"quantity\":1,\"unitPrice\":390000}]}"
check "same supplier invoice number cannot be registered twice" '.success == false'
as "$COM_TOKEN" POST procurement/payments "{\"type\":3,\"invoiceId\":$SINV,\"amount\":30000000}"; check "payment above the invoice rejected" '.success == false'
as "$COM_TOKEN" POST procurement/payments "{\"type\":3,\"invoiceId\":$SINV,\"amount\":25740000,\"deductions\":0}"; check "payment request for the approved invoice" '.success and .data.status == 0'
PAY=$(val .data.id)
as "$COM_TOKEN" POST procurement/payments/action "{\"id\":$PAY,\"action\":\"approve\"}"; check "buyer cannot approve the payment" '.success == false'
call POST procurement/payments/action "{\"id\":$PAY,\"action\":\"approve\"}"; check "payment approved by PMO" '.success and .data.status == 1'
user2() { call POST Accounts/register-user "{\"userName\":\"$1\",\"name\":\"$1\",\"lastName\":\"آزمون\",\"email\":\"$1@example.com\",\"roleIds\":[$2],\"password\":\"$3\",\"phoneNumber\":\"$4\",\"birthDate\":\"1990-01-01T00:00:00+03:30\",\"nationalCode\":\"$5\"}"; }
user2 fin2.ci 3 'Ci#Fin2-User-2026' 09120000024 0012345624
user2 fm.ci 17 'Ci#Fm-User-2026' 09120000025 0012345625
user2 fm2.ci 17 'Ci#Fm2-User-2026' 09120000031 0012345631
user2 pay.ci 18 'Ci#Pay-User-2026' 09120000026 0012345626
user2 tr.ci 19 'Ci#Tr-User-2026' 09120000027 0012345627
FIN2_TOKEN=$(login fin2.ci 'Ci#Fin2-User-2026'); FM_TOKEN=$(login fm.ci 'Ci#Fm-User-2026'); FM2_TOKEN=$(login fm2.ci 'Ci#Fm2-User-2026'); PAYR_TOKEN=$(login pay.ci 'Ci#Pay-User-2026'); TR_TOKEN=$(login tr.ci 'Ci#Tr-User-2026')
as "$FIN_TOKEN" POST procurement/payments/action "{\"id\":$PAY,\"action\":\"paid\"}"; check "payment needs the bank or Sepidar reference" '.success == false'
as "$FIN_TOKEN" POST procurement/payments/action "{\"id\":$PAY,\"action\":\"paid\",\"reference\":\"سند 4521\",\"bankAccountId\":1}"; nomsg "r1: the old pay action is for treasury or the finance manager only" "خزانه"
as "$TR_TOKEN" POST procurement/payments/action "{\"id\":$PAY,\"action\":\"paid\",\"reference\":\"سند 4521\"}"; check "r1: the old pay action needs the paying bank account (the bank side of the entry is always linked)" '.success == false and (.message|contains("حساب بانکی"))'
as "$TR_TOKEN" POST finance/treasury/bank-accounts '{"title":"بانک مسیر قدیمی","bankName":"ملی","accountNumber":"100324200005","iban":"IR160170000000100324200005"}'; BALEG=$(val .data.id)
call POST procurement/suppliers "{\"providerId\":$PA,\"iban\":\"IR970170000000100324200002\"}"
as "$TR_TOKEN" POST procurement/payments/action "{\"id\":$PAY,\"action\":\"paid\",\"reference\":\"سند 4521\",\"bankAccountId\":$BALEG}"; check "r2 f23: the old pay action creates a DRAFT treasury payment (request stays approved, not paid)" '.success and .data.status == 1 and (.data.treasuryPaymentId // 0) > 0'
OLDPM=$(val .data.treasuryPaymentId)
as "$TR_TOKEN" POST finance/treasury/payments/action "{\"id\":$OLDPM,\"action\":\"post\"}"; no "r2 f23: the draft from the old path cannot be posted without the finance manager's approval"
as "$FM_TOKEN" POST finance/treasury/payments/action "{\"id\":$OLDPM,\"action\":\"approve\"}"; check "r2 f23: finance manager approves the draft (IBAN equals the registered one)" '.success and .data.status == 1'
as "$TR_TOKEN" POST finance/treasury/payments/action "{\"id\":$OLDPM,\"action\":\"post\"}"; check "finance records the payment" '.success and .data.status == 2 and .data.netAmount == 25740000'
call GET "finance/events?PageSize=0"
check "invoice event: GRNI cleared, VAT and payable to the supplier" '[.data.data[] | select(.sourceType == "SupplierInvoice")] | length == 1 and (.[0].lines | map(select(.accountRole == "AccountsPayable")) | .[0].credit) == 25740000'
check "goods received not invoiced is zero for the supplier" "[.data.data[] | select(.status != 9) | .lines[] | select(.accountRole == \"GRNI\" and .providerId == $PA) | (.debit - .credit)] | add == 0"
check "every accounting event is balanced (with procurement)" '[.data.data[] | ([.lines[].debit]|add) == ([.lines[].credit]|add)] | all'
call GET finance/valuation; check "stock value still equals the events after procurement" '.data.totalValue == .data.totalEventsBalance'
as "$COM_TOKEN" GET "procurement/suppliers?contains=%D8%A7%D9%84%D9%81"; check "supplier list shows grade and on-time delivery" '.success and .data[0].grade == 1 and .data[0].receiptLines == 1'

as "$CEO_TOKEN" GET finance/ceo-dashboard; check "CEO dashboard: stock value, payables and projects" '.success and .data.stockValue > 0 and .data.payablesOpen == 0 and .data.receivedNotInvoiced > 0 and .data.openPurchaseOrders == 1'
as "$WH_TOKEN" GET finance/ceo-dashboard; [ "$CODE" = "403" ] && ok "storekeeper cannot open the CEO dashboard (403)" || bad "storekeeper cannot open the CEO dashboard (403) got $CODE"

# switching the purchase order rule on: only petty-cash purchases are received without an order
call POST procurement/settings '{"key":"EnforcePurchaseOrder","value":1}'; check "purchase order rule switched on" '.success and .data.value == 1'
call POST inventory/documents "{\"type\":1,\"documentDate\":\"2026-10-23T00:00:00\",\"warehouseId\":$W1,\"lines\":[{\"itemId\":$BOLT,\"quantity\":1,\"locationId\":$L2}]}"
D=$(val .data.id); call POST "inventory/documents/$D/confirm"; check "receipt without order and price rejected" '.success == false'; call DELETE "inventory/documents/$D"
call POST inventory/documents "{\"type\":1,\"documentDate\":\"2026-10-23T00:00:00\",\"warehouseId\":$W1,\"lines\":[{\"itemId\":$BOLT,\"quantity\":1,\"locationId\":$L2,\"unitPrice\":60000000}]}"
D=$(val .data.id); call POST "inventory/documents/$D/confirm"; check "receipt without order above petty cash rejected" '.success == false'; call DELETE "inventory/documents/$D"
call POST inventory/documents "{\"type\":1,\"documentDate\":\"2026-10-23T00:00:00\",\"warehouseId\":$W1,\"lines\":[{\"itemId\":$BOLT,\"quantity\":2,\"locationId\":$L2,\"unitPrice\":400000}]}"
D=$(val .data.id); call POST "inventory/documents/$D/confirm"; check "petty-cash receipt without order accepted" '.success'

# ---------------------------------------------------------------- quality module (QC + QA), release 1
# Everything below uses its own contract, equipment and work orders; dates are relative to today so the
# welder continuity (six months) and calibration intervals behave the same on every run.
dd() { date -d "$1 days" +%Y-%m-%d; }
D0=$(dd 0)
TOKEN=$ADMIN_TOKEN
call POST Accounts/register-user '{"userName":"qa.ci","name":"qa.ci","lastName":"آزمون","email":"qa.ci@example.com","roleIds":[14],"password":"Ci#Qa-User-2026","phoneNumber":"09120000020","birthDate":"1990-01-01T00:00:00+03:30","nationalCode":"0012345620"}'
call POST Accounts/register-user '{"userName":"qc2.ci","name":"qc2.ci","lastName":"آزمون","email":"qc2.ci@example.com","roleIds":[5],"password":"Ci#Qc2-User-2026","phoneNumber":"09120000021","birthDate":"1990-01-01T00:00:00+03:30","nationalCode":"0012345621"}'
QA_TOKEN=$(login qa.ci 'Ci#Qa-User-2026'); QC2_TOKEN=$(login qc2.ci 'Ci#Qc2-User-2026')
[ -n "$QA_TOKEN" ] && [ -n "$QC2_TOKEN" ] && ok "quality: login QA (role 14) and second QC user" || bad "quality: login QA (role 14) and second QC user"
uid() { as "$1" GET Accounts/get-current-user-info; jq -r '.data.id // .data.userId // empty' "$B"; }
QAID=$(uid "$QA_TOKEN"); QCID=$(uid "$QC_TOKEN"); QC2ID=$(uid "$QC2_TOKEN"); ENGID=$(uid "$ENG_TOKEN"); PRODID=$(uid "$PROD_TOKEN"); WHID=$(uid "$WH_TOKEN")
[ -n "$QAID" ] && [ -n "$QCID" ] && [ -n "$QC2ID" ] && [ -n "$ENGID" ] && [ -n "$PRODID" ] && [ -n "$WHID" ] && ok "quality: user ids resolved" || bad "quality: user ids resolved"
# refused(): the last call did not succeed (the services answer 200 + success=false for role and rule violations)
refused() { jq -e '.success == false' "$B" >/dev/null 2>&1; }
no() { if refused; then ok "$1"; else bad "$1"; fi; }
# nomsg NAME FRAGMENT : refused, and the reason names the rule (so a check cannot pass for an unrelated reason)
nomsg() { if jq -e --arg f "$2" '.success == false and ((.message // "") | contains($f))' "$B" >/dev/null 2>&1; then ok "$1"; else bad "$1"; fi; }

# --- contract, pump equipment, two work orders (the pump template has 6 activities)
call POST Contracts '{"code":"QA-CT-01","title":"قرارداد آزمون ماژول کیفیت","status":1,"employer":"کارفرمای آزمون"}'; check "quality: contract created" '.success and .data.id > 0'
QCT=$(val .data.id)
call POST Equipments '{"code":"QA-EQ-PUMP","title":"Feed Pump API 610"}'; check "quality: pump equipment created" '.success and .data.id > 0'
QEQ=$(val .data.id)
call POST ContractEquipments "{\"contractId\":$QCT,\"equipmentId\":$QEQ,\"count\":2}"; check "quality: contract equipment" '.success'
call POST WorkOrders "{\"contractId\":$QCT,\"equipmentId\":$QEQ,\"code\":\"QA-WO-1\",\"title\":\"Pump unit 1\",\"description\":\"x\",\"count\":1}"; check "quality: work order 1" '.success and .data.id > 0'
WO1=$(val .data.id)
call POST WorkOrders "{\"contractId\":$QCT,\"equipmentId\":$QEQ,\"code\":\"QA-WO-2\",\"title\":\"Pump unit 2\",\"description\":\"x\",\"count\":1}"; check "quality: work order 2" '.success and .data.id > 0'
WO2=$(val .data.id)

# --- A. ITP: templates, system drafts, point rules
as "$QC_TOKEN" GET quality/itp-templates; check "ITP templates seeded: steam turbine 37 rows, pump, condenser, lube oil" '.success and (.data.data|length) >= 4 and ([.data.data[]|select(.code=="ITP-STEAM-TURBINE")|.activityCount][0] == 37) and ([.data.data[]|select(.code=="ITP-PUMP")|.activityCount][0] == 6)'
PUMPT=$(jq -r '.data.data[]|select(.code=="ITP-PUMP")|.id' "$B")
as "$QC_TOKEN" GET "quality/itp-templates/$PUMPT"; check "pump template: hydro test has H/H/W and no invented test pressure" '[.data.activities[]|select(.seq==3)][0] as $a | ($a.pointVendor == 1 and $a.pointTpi == 1 and $a.pointClient == 2) and ([$a.parameters[]|select(.name=="Test pressure")|.requiresLimits][0] == true) and ([$a.parameters[]|select(.name=="Test pressure")|.max][0] == null)'
as "$WH_TOKEN" POST quality/itps/ensure "{\"workOrderId\":$WO1,\"productType\":4}"; no "warehouse user cannot create an ITP"
as "$QC_TOKEN" POST quality/itps/ensure "{\"workOrderId\":$WO1,\"productType\":4}"; check "ITP drafted from the pump template" '.success and .data.status == 0 and (.data.activities|length) == 6 and .data.templateCode == "ITP-PUMP" and .data.noticeDays == 10'
ITP1=$(val .data.id)
A1=$(val '.data.activities[]|select(.seq==1)|.id'); A2=$(val '.data.activities[]|select(.seq==2)|.id'); A3=$(val '.data.activities[]|select(.seq==3)|.id')
A4=$(val '.data.activities[]|select(.seq==4)|.id'); A5=$(val '.data.activities[]|select(.seq==5)|.id'); A6=$(val '.data.activities[]|select(.seq==6)|.id')
P_TESTP=$(val ".data.activities[]|select(.id==$A3)|.parameters[]|select(.name==\"Test pressure\")|.id")
as "$QC_TOKEN" POST quality/itps/ensure "{\"workOrderId\":$WO1,\"productType\":4}"; check "second ensure returns the same ITP" ".success and .data.id == $ITP1"
as "$QC_TOKEN" POST quality/itps/activity/action "{\"id\":$A1,\"action\":\"done\"}"; nomsg "activity with an H point cannot be done before the client approved the ITP" "کارفرما تایید نشده"
as "$QC_TOKEN" POST quality/itps/action "{\"id\":$ITP1,\"action\":\"client-approve\",\"reference\":\"CL-1\"}"; no "client approval before submission rejected"
as "$QC_TOKEN" POST quality/itps/action "{\"id\":$ITP1,\"action\":\"submit\"}"; check "ITP submitted" '.success and .data.status == 1'
as "$QC_TOKEN" POST quality/itps/action "{\"id\":$ITP1,\"action\":\"client-approve\"}"; no "client approval needs the client letter reference"
as "$ENG_TOKEN" POST quality/itps/action "{\"id\":$ITP1,\"action\":\"client-approve\",\"reference\":\"CL-1\"}"; no "engineering cannot record the client approval"
as "$QC_TOKEN" POST quality/itps/action "{\"id\":$ITP1,\"action\":\"client-approve\",\"reference\":\"CL-LTR-77\",\"date\":\"$D0\"}"; check "ITP approved by the client (letter reference kept)" '.success and .data.status == 2 and .data.clientApprovalReference == "CL-LTR-77"'
as "$QC_TOKEN" POST quality/itps/activity/action "{\"id\":$A1,\"action\":\"done\"}"; check "H point: done refused without the vendor release" '.success == false and (.message|contains("H"))'
as "$ENG_TOKEN" POST quality/itps/release "{\"activityId\":$A1,\"party\":1,\"action\":1,\"personName\":\"Vendor QC\",\"organisation\":\"Vendor Co\",\"reference\":\"V-REL-1\"}"; no "only QC records a point release"
as "$QC_TOKEN" POST quality/itps/release "{\"activityId\":$A1,\"party\":1,\"action\":4,\"personName\":\"Vendor QC\",\"reference\":\"x\"}"; check "H point cannot be waived" '.success == false'
as "$QC_TOKEN" POST quality/itps/release "{\"activityId\":$A1,\"party\":1,\"action\":1,\"personName\":\"Vendor QC\",\"organisation\":\"Vendor Co\",\"reference\":\"V-REL-1\"}"; check "vendor H release recorded" '.success'
as "$QC_TOKEN" POST quality/itps/activity/action "{\"id\":$A1,\"action\":\"done\"}"; check "H point released -> activity done, ITP in use" ".success and ([.data.activities[]|select(.id==$A1)|.status][0] == 2) and .data.status == 3"
as "$PROD_TOKEN" POST quality/itps/activity/action "{\"id\":$A4,\"action\":\"not-applicable\",\"note\":\"x\"}"; no "production cannot mark an activity not applicable"
as "$QC_TOKEN" POST quality/itps/activity/action "{\"id\":$A4,\"action\":\"not-applicable\"}"; no "not applicable needs a reason"
as "$QC_TOKEN" POST quality/itps/activity/action "{\"id\":$A4,\"action\":\"not-applicable\",\"note\":\"No run test for this pump order (client letter CL-2)\"}"; check "run test marked not applicable with a reason" ".success and ([.data.activities[]|select(.id==$A4)|.status][0] == 4)"
as "$QC_TOKEN" POST quality/itps/activity/action "{\"id\":$A5,\"action\":\"not-applicable\",\"note\":\"Performance test at site only (client letter CL-2)\"}"; check "performance test marked not applicable" '.success'

# the system drafts the ITP of a work order that only has QC records (no human step)
call POST quality/records "{\"type\":1,\"workOrderId\":$WO2,\"operation\":\"Casing welding\",\"result\":1,\"quantity\":1}"; check "first record of work order 2 saved (PMO may write non-ITP records)" '.success' 
as "$QC_TOKEN" GET "quality/itps/by-work-order/$WO2"; check "ITP of work order 2 was drafted by the system when the first record was saved" '.success and .data.autoDrafted == true and .data.status == 0 and (.data.activities|length) == 6'
ITP2=$(val .data.id)

# --- B. calibration and measuring equipment
as "$QC_TOKEN" GET quality/calibration/intervals; check "default calibration intervals seeded with their source" '.success and (.data|length) >= 5 and ([.data[]|.source]|all(. != null))'
as "$WH_TOKEN" POST quality/equipment '{"name":"Caliper","type":1}'; no "warehouse user cannot register measuring equipment"
as "$QC_TOKEN" POST quality/equipment '{"name":"Digital caliper 0-150","type":1,"serialNumber":"DC-1","location":"QC lab"}'; check "equipment registered with an INN-IST code and the default interval" '.success and (.data.code|test("^INN-IST-[0-9]+$")) and .data.intervalMonths > 0'
G1=$(val .data.id); G1CODE=$(val .data.code)
call GET "quality/equipment/$G1"; check "never calibrated instrument is overdue (computed, not stored)" '.data.status == 2'
as "$QC_TOKEN" POST quality/records "{\"type\":3,\"workOrderId\":$WO1,\"quantity\":1,\"result\":1,\"measurements\":[{\"characteristic\":\"Bore\",\"nominal\":100,\"tolPlus\":0.05,\"tolMinus\":0.05,\"measured\":100.01,\"unit\":\"mm\",\"equipmentId\":$G1}]}"; nomsg "record with an uncalibrated instrument rejected" "کالیبره نشده"
as "$QC_TOKEN" POST quality/equipment/calibration "{\"equipmentId\":$G1,\"eventDate\":\"$(dd -200)\",\"labName\":\"Lab A\",\"labAccredited\":true,\"certificateNumber\":\"CAL-G1-1\",\"asFoundPass\":true,\"asLeftPass\":true}"; nomsg "calibration without a traceability statement rejected (ISO 7.1.5.1)" "7.1.5.1"
as "$WH_TOKEN" POST quality/equipment/calibration "{\"equipmentId\":$G1,\"eventDate\":\"$(dd -200)\",\"labName\":\"Lab A\",\"certificateNumber\":\"CAL-G1-1\",\"traceabilityStatement\":\"Traceable to national standard\",\"asFoundPass\":true,\"asLeftPass\":true}"; no "only QC registers a calibration"
as "$QC_TOKEN" POST quality/equipment/calibration "{\"equipmentId\":$G1,\"eventDate\":\"$(dd -200)\",\"labName\":\"Lab A\",\"labAccredited\":true,\"certificateNumber\":\"CAL-G1-1\",\"certificateDocumentId\":\"$DOCID\",\"traceabilityStatement\":\"Traceable to national standard\",\"asFoundPass\":true,\"asLeftPass\":true}"; check "calibration registered: valid for the interval" '.success and .data.status == 0 and .data.nextDue != null and (.data.events|length) == 1'
# an instrument whose certificate expired two years ago, one that failed its last calibration, one due in ten days
as "$QC_TOKEN" POST quality/equipment '{"name":"Old micrometer","type":1,"serialNumber":"MM-2"}'; G2=$(val .data.id)
as "$QC_TOKEN" POST quality/equipment/calibration "{\"equipmentId\":$G2,\"eventDate\":\"$(dd -800)\",\"labName\":\"Lab A\",\"certificateNumber\":\"CAL-G2-1\",\"traceabilityStatement\":\"Traceable\",\"asFoundPass\":true,\"asLeftPass\":true}"
call GET "quality/equipment/$G2"; check "expired certificate -> overdue" '.data.status == 2'
as "$QC_TOKEN" POST quality/records "{\"type\":3,\"workOrderId\":$WO1,\"quantity\":1,\"result\":1,\"measurements\":[{\"characteristic\":\"Bore\",\"nominal\":100,\"tolPlus\":0.05,\"tolMinus\":0.05,\"measured\":100.01,\"unit\":\"mm\",\"equipmentId\":$G2}]}"; check "overdue instrument cannot be used" '.success == false and (.message|contains("INN-IST"))'
as "$QC_TOKEN" POST quality/equipment '{"name":"Dial gauge","type":2}'; G3=$(val .data.id)
as "$QC_TOKEN" POST quality/equipment/calibration "{\"equipmentId\":$G3,\"eventDate\":\"$(dd -10)\",\"labName\":\"Lab A\",\"certificateNumber\":\"CAL-G3-1\",\"traceabilityStatement\":\"Traceable\",\"asFoundPass\":false,\"asLeftPass\":false}"; check "calibration failed as-left -> out of service" '.success and .data.status == 3'
as "$QC_TOKEN" POST quality/records "{\"type\":3,\"workOrderId\":$WO1,\"quantity\":1,\"result\":1,\"measurements\":[{\"characteristic\":\"Bore\",\"nominal\":100,\"tolPlus\":0.05,\"tolMinus\":0.05,\"measured\":100.01,\"unit\":\"mm\",\"equipmentId\":$G3}]}"; nomsg "out-of-service instrument cannot be used" "از رده خارج"
as "$QC_TOKEN" POST quality/equipment/action "{\"id\":$G3,\"action\":\"return-to-service\"}"; nomsg "return to service needs a valid calibration first" "کالیبراسیون معتبر"
as "$QC_TOKEN" POST quality/equipment '{"name":"Torque wrench","type":99}'; G5=$(val .data.id)
as "$QC_TOKEN" POST quality/equipment/calibration "{\"equipmentId\":$G5,\"eventDate\":\"$(dd -355)\",\"labName\":\"Lab A\",\"certificateNumber\":\"CAL-G5-1\",\"traceabilityStatement\":\"Traceable\",\"asFoundPass\":true,\"asLeftPass\":true,\"intervalMonths\":12}"
call GET "quality/equipment/due?Days=30"; check "due list holds the instrument due within 30 days and the overdue ones" "(.data.data|map(.id)|index($G5)) != null and (.data.data|map(.id)|index($G2)) != null"
as "$QC_TOKEN" POST quality/equipment '{"name":"Reference caliper for reports","type":1}'; G4=$(val .data.id)
as "$QC_TOKEN" POST quality/equipment/calibration "{\"equipmentId\":$G4,\"eventDate\":\"$(dd -60)\",\"labName\":\"Lab A\",\"labAccredited\":true,\"certificateNumber\":\"CAL-G4-1\",\"certificateDocumentId\":\"$DOCID\",\"traceabilityStatement\":\"Traceable\",\"asFoundPass\":true,\"asLeftPass\":true}"
as "$QC_TOKEN" POST quality/equipment '{"name":"Test pressure gauge","type":3}'; PG1=$(val .data.id)
as "$QC_TOKEN" POST quality/equipment/calibration "{\"equipmentId\":$PG1,\"eventDate\":\"$(dd -30)\",\"labName\":\"Lab A\",\"labAccredited\":true,\"certificateNumber\":\"CAL-PG1-1\",\"certificateDocumentId\":\"$DOCID\",\"traceabilityStatement\":\"Traceable\",\"asFoundPass\":true,\"asLeftPass\":true}"; check "pressure gauge calibrated" '.success and .data.status == 0'

# --- C. inspection records with measurements, self inspection, auto NCR
as "$QC_TOKEN" POST quality/records "{\"type\":3,\"workOrderId\":$WO1,\"itpActivityId\":$A2,\"quantity\":1,\"operation\":\"Casing bore\",\"drawingNumber\":\"DWG-1\",\"drawingRevision\":\"A\",\"result\":1,\"measurements\":[{\"characteristic\":\"Bore\",\"nominal\":100,\"tolPlus\":0.05,\"tolMinus\":0.05,\"measured\":100.02,\"unit\":\"mm\",\"equipmentId\":$G1}]}"
check "dimensional record with a calibrated instrument" '.success and (.data.number|startswith("QIR-")) and .data.result == 1 and .data.measurements[0].pass == true and .data.selfInspection == false'
R1=$(val .data.id); R1NUM=$(val .data.number)
as "$QC_TOKEN" POST quality/records "{\"type\":3,\"workOrderId\":$WO1,\"itpActivityId\":$A2,\"quantity\":1,\"result\":1,\"measurements\":[{\"characteristic\":\"Bore\",\"nominal\":100,\"tolPlus\":0.05,\"tolMinus\":0.05,\"measured\":100.2,\"unit\":\"mm\",\"equipmentId\":$G1}]}"; nomsg "accepting an out-of-tolerance measurement rejected" "خارج از تلرانس"
as "$PROD_TOKEN" POST quality/records "{\"type\":3,\"workOrderId\":$WO1,\"itpActivityId\":$A2,\"quantity\":1,\"result\":1,\"measurements\":[{\"characteristic\":\"Bore\",\"nominal\":100,\"tolPlus\":0.05,\"tolMinus\":0.05,\"measured\":100.0,\"unit\":\"mm\"}]}"; nomsg "ITP inspections are QC only (operator refused)" "فقط توسط بازرس QC"
as "$PROD_TOKEN" POST quality/records "{\"type\":1,\"workOrderId\":$WO2,\"operation\":\"Machining\",\"quantity\":1,\"result\":1}"; check "operator self-inspection allowed outside the ITP and marked as such" '.success and .data.selfInspection == true'
as "$QC_TOKEN" POST quality/records "{\"type\":3,\"workOrderId\":$WO2,\"itemId\":$BOLT,\"quantity\":1,\"result\":1,\"measurements\":[{\"characteristic\":\"Thread\",\"nominal\":24,\"tolPlus\":0.1,\"tolMinus\":0.1,\"measured\":24.05,\"unit\":\"mm\",\"equipmentId\":$G1}]}"; check "second record with the same instrument (item known)" '.success'
R2=$(val .data.id); R2NUM=$(val .data.number)
as "$QC_TOKEN" POST quality/records "{\"type\":3,\"workOrderId\":$WO2,\"quantity\":1,\"result\":2,\"note\":\"x\",\"measurements\":[{\"characteristic\":\"Bore\",\"nominal\":100,\"tolPlus\":0.05,\"tolMinus\":0.05,\"measured\":100.4,\"unit\":\"mm\"}]}"; nomsg "reject without an item (no NCR possible) rejected" "گزارش عدم انطباق"
as "$QC_TOKEN" POST quality/records "{\"type\":3,\"workOrderId\":$WO2,\"itemId\":$BOLT,\"quantity\":1,\"result\":2,\"measurements\":[{\"characteristic\":\"Thread\",\"nominal\":24,\"tolPlus\":0.1,\"tolMinus\":0.1,\"measured\":24.4,\"unit\":\"mm\",\"equipmentId\":$G1}]}"
check "reject drafts an NCR automatically (with source record, due date, not classified)" '.success and .data.result == 2 and .data.ncrId != null and (.data.ncrNumber|startswith("NCR-"))'
R3=$(val .data.id); R3NCR=$(val .data.ncrId); R3NUM=$(val .data.number)
call GET "inventory/quality/ncr/$R3NCR"; check "auto NCR: not classified, decision deadline set, source record linked, work order 2" ".success and .data.severity == 0 and .data.dueDate != null and .data.sourceRecordNumber == \"$R3NUM\" and .data.workOrderId == $WO2 and .data.status == 0"

# --- D. out-of-tolerance impact (ISO 7.1.5.2)
as "$QC_TOKEN" POST quality/release-notes "{\"workOrderId\":$WO1,\"itemsText\":\"Feed pump unit 1\"}"; check "release note drafted while points are still open (cannot be released yet)" '.success and .data.status == 0 and (.data.number|startswith("RN-")) and .data.canRelease == false'
RN1=$(val .data.id)
as "$QC_TOKEN" POST quality/release-notes/action "{\"id\":$RN1,\"action\":\"release\"}"; check "release refused while H/W points and reports are open (computed checks named in the answer)" '.success == false and (.message|contains("ترخیص ممکن نیست"))'
as "$QC_TOKEN" GET "quality/work-orders/$WO1/release-checks"; check "release checks computed: ITP approved, points still open" '.success and .data.canRelease == false and ([.data.checks[]|select(.code=="ITP_APPROVED")|.passed][0] == true) and ([.data.checks[]|select(.code=="POINTS_DONE")|.passed][0] == false)'
as "$QC_TOKEN" POST quality/reports "{\"type\":12,\"workOrderId\":$WO2,\"equipmentIds\":[$G1],\"values\":[{\"parameter\":\"Flange thickness\",\"unit\":\"mm\",\"min\":19.9,\"max\":20.1,\"measured\":20.02}]}"; check "test report made with instrument G1 (before its failed calibration)" '.success and .data.equipment[0].equipmentId != null'
DRG1=$(val .data.number)
as "$QC_TOKEN" POST quality/equipment/calibration "{\"equipmentId\":$G1,\"eventDate\":\"$D0\",\"labName\":\"Lab A\",\"labAccredited\":true,\"certificateNumber\":\"CAL-G1-2\",\"certificateDocumentId\":\"$DOCID\",\"traceabilityStatement\":\"Traceable to national standard\",\"asFoundPass\":false,\"asLeftPass\":true,\"adjusted\":true}"
check "calibration with a failed as-found: instrument usable again, impact case opened" '.success and .data.status == 0 and .data.events[0].ootCaseId != null and (.data.events[0].ootNumber|startswith("OOT-"))'
call GET "quality/out-of-tolerance?EquipmentId=$G1"; OOT=$(val '.data.data[0].id')
check "case lists every document made with the instrument since the previous good calibration" '.data.data[0].itemCount >= 3 and .data.data[0].pendingCount >= 3 and .data.data[0].status == 0'
call GET "quality/out-of-tolerance/$OOT"; check "the case lists the affected test report and the affected inspection records" "([.data.items[]|select(.sourceKind==\"TestReport\")|.sourceNumber]|index(\"$DRG1\")) != null and ([.data.items[]|select(.sourceKind==\"QcRecord\")]|length) >= 3"
as "$QC_TOKEN" GET "quality/work-orders/$WO1/release-checks"; check "release gate: out-of-tolerance impact of work order 1 not assessed" '[.data.checks[]|select(.code=="OOT_ASSESSED")|.passed][0] == false'
as "$QC_TOKEN" POST quality/out-of-tolerance/close "{\"id\":$OOT}"; nomsg "case cannot be closed while documents are not assessed" "ارزیابی اثر"
call GET "quality/out-of-tolerance/$OOT"
for IID in $(jq -r '.data.items[].id' "$B"); do
  SRC=$(jq -r ".data.items[]|select(.id==$IID)|.sourceNumber" "$B")
  if [ "$SRC" = "$R2NUM" ]; then AS=3; else AS=1; fi
  as "$QC_TOKEN" POST quality/out-of-tolerance/assess "{\"itemId\":$IID,\"assessment\":$AS,\"note\":\"assessed in the smoke test\"}"
done
as "$QC_TOKEN" POST quality/out-of-tolerance/assess "{\"itemId\":$IID,\"assessment\":1}"; no "assessment needs a written note"
call GET "quality/out-of-tolerance/$OOT"; check "every item assessed; the one with an item got its NCR drafted" "(.data.pendingCount == 0) and ([.data.items[]|select(.sourceNumber==\"$R2NUM\")|.ncrNumber|startswith(\"NCR-\")][0] == true)"
as "$QC_TOKEN" POST quality/out-of-tolerance/close "{\"id\":$OOT,\"note\":\"closed\"}"; check "case closed after the assessments" '.success and .data.status == 1'
as "$QC_TOKEN" GET "quality/work-orders/$WO1/release-checks"; check "release gate: OOT check passes after the assessments" '[.data.checks[]|select(.code=="OOT_ASSESSED")|.passed][0] == true'

# --- E. test reports: limits, qualified examiners, segregation
as "$QC_TOKEN" GET quality/reports/standards/1; check "PT standard rows: dwell time maximum 10 min as printed, no invented minimum" '.success and ([.data[]|select(.name=="Dwell time")|.max][0] == 10) and ([.data[]|select(.name=="Dwell time")|.min][0] == null) and ([.data[]|select(.mandatory)]|length) >= 4'
as "$QC_TOKEN" GET quality/reports/standards/7; check "hydro standard rows have no limits (engineering sets them)" '.success and ([.data[]|.requiresLimits]|all)'
as "$QC_TOKEN" GET quality/reports/standards/8; check "run test overspeed trip limits 107.8 - 112.2" '[.data[]|select(.name=="Overspeed trip 1")][0] as $t | $t.min == 107.8 and $t.max == 112.2'
# NDT personnel: a person with several certificates
as "$QA_TOKEN" POST quality/persons '{"name":"NDT Examiner","personnelCode":"N-001","employer":1,"stampId":"N1"}'; check "NDT person registered" '.success and .data.id > 0'
NP=$(val .data.id)
as "$WH_TOKEN" POST quality/persons '{"name":"x","employer":1}'; no "warehouse cannot register qualified persons"
qual() { as "$QA_TOKEN" POST quality/qualifications "$1"; }
qual "{\"personId\":$NP,\"kind\":3,\"method\":\"PT\",\"level\":2,\"scheme\":2,\"certificateNumber\":\"PT-L2\",\"issuedDate\":\"$(dd -100)\",\"expiryDate\":\"$(dd 300)\",\"certificateDocumentId\":\"$DOCID\"}"; check "NDT qualification PT level II registered" '.success and .data.state == 0'
QPT2=$(val .data.id)
qual "{\"personId\":$NP,\"kind\":3,\"method\":\"PT\",\"level\":1,\"scheme\":2,\"certificateNumber\":\"PT-L1\",\"issuedDate\":\"$(dd -100)\",\"expiryDate\":\"$(dd 300)\"}"; QPT1=$(val .data.id)
qual "{\"personId\":$NP,\"kind\":3,\"method\":\"PT\",\"level\":2,\"scheme\":2,\"certificateNumber\":\"PT-OLD\",\"issuedDate\":\"$(dd -800)\",\"expiryDate\":\"$(dd -10)\"}"; check "expired certificate is stored as expired (computed)" '.success and .data.state == 2'
QPTX=$(val .data.id)
qual "{\"personId\":$NP,\"kind\":3,\"method\":\"MT\",\"level\":2,\"scheme\":2,\"certificateNumber\":\"MT-L2\",\"issuedDate\":\"$(dd -100)\",\"expiryDate\":\"$(dd 300)\"}"; QMT2=$(val .data.id)
qual "{\"personId\":$NP,\"kind\":3,\"method\":\"PT\",\"level\":2,\"certificateNumber\":\"PT-NOSCHEME\",\"scheme\":9,\"issuedDate\":\"$(dd -10)\"}"; no "NDT certificate needs ASNT SNT-TC-1A or ISO 9712 scheme"
pt_values() { jq -nc --argjson t "$1" --argjson d "$2" '[{parameter:"Part and penetrant temperature",unit:"°C",measured:$t},{parameter:"Dwell time",unit:"min",measured:$d},{parameter:"Interpretation time after developer",unit:"min",measured:20},{parameter:"White light intensity",unit:"lux",measured:1200}]'; }
pt_report() { # pt_report QUALIFICATIONID TEMP DWELL [ITEMID]
  jq -nc --argjson q "$1" --argjson wo "$WO2" --argjson v "$(pt_values "$2" "$3")" --argjson eq "$G4" --arg item "${4:-}" \
    '{type:1,workOrderId:$wo,examinerQualificationId:$q,procedureRef:"WI-QC-004",codeEdition:"ASME V 2023",values:$v,equipmentIds:[$eq]} + (if $item != "" then {itemId:($item|tonumber)} else {} end)'
}
as "$QC_TOKEN" POST quality/reports "$(pt_report "$QPT1" 25 8)"; check "PT report saved as draft (level I examiner)" '.success and .data.status == 0 and (.data.problems|length) > 0'
PR1=$(val .data.id)
as "$QC_TOKEN" POST quality/reports/action "{\"id\":$PR1,\"action\":\"submit\"}"; nomsg "PT report by a level I examiner cannot be submitted" "سطح"
as "$QC_TOKEN" POST quality/reports "$(pt_report "$QPTX" 25 8)"; PR2=$(val .data.id)
as "$QC_TOKEN" POST quality/reports/action "{\"id\":$PR2,\"action\":\"submit\"}"; nomsg "PT report with an expired certificate cannot be submitted" "منقضی"
as "$QC_TOKEN" POST quality/reports "$(pt_report "$QMT2" 25 8)"; PR3=$(val .data.id)
as "$QC_TOKEN" POST quality/reports/action "{\"id\":$PR3,\"action\":\"submit\"}"; nomsg "PT report with an MT certificate cannot be submitted (method mismatch)" "خوان نیست"
as "$QC_TOKEN" POST quality/reports "$(pt_report "$QPT2" 25 8)"; check "PT report with a valid level II examiner has no problems; pass computed by the server" '.success and (.data.problems|length) == 0 and ([.data.values[]|.pass]|all(. == true))'
PR4=$(val .data.id)
as "$ENG_TOKEN" POST quality/reports/action "{\"id\":$PR4,\"action\":\"submit\"}"; no "only QC submits a report"
as "$QC_TOKEN" POST quality/reports/action "{\"id\":$PR4,\"action\":\"submit\"}"; check "PT report submitted; result Pass" '.success and .data.status == 1 and .data.result == 1'
as "$QC_TOKEN" POST quality/reports/action "{\"id\":$PR4,\"action\":\"approve\"}"; nomsg "the examiner cannot approve his own report (segregation of duties)" "تفکیک وظایف"
as "$QC2_TOKEN" POST quality/reports/action "{\"id\":$PR4,\"action\":\"approve\"}"; check "second QC approves the PT report" '.success and .data.status == 2 and .data.approvedByName != null'
as "$QC_TOKEN" POST quality/reports "$(pt_report "$QPT2" 25 8 | jq -c --argjson id "$PR4" '. + {id:$id}')"; nomsg "an approved report cannot be edited" "قابل ویرایش نیست"
# a failing PT (dwell 11 min > 10): needs an item to draft the NCR, the server computes Fail
as "$QC_TOKEN" POST quality/reports "$(pt_report "$QPT2" 25 11)"; check "dwell time 11 min computes Fail" '.success and ([.data.values[]|select(.parameter=="Dwell time")|.pass][0] == false)'
PR5=$(val .data.id)
as "$QC_TOKEN" POST quality/reports/action "{\"id\":$PR5,\"action\":\"submit\"}"; no "failed report without an item (no NCR possible) cannot be submitted"
as "$QC_TOKEN" POST quality/reports "$(pt_report "$QPT2" 25 11 $BOLT)"; PR6=$(val .data.id)
as "$QC_TOKEN" POST quality/reports/action "{\"id\":$PR6,\"action\":\"submit\"}"; check "failed report submitted: NCR drafted automatically" '.success and .data.result == 2 and .data.ncrId != null and (.data.ncrNumber|startswith("NCR-"))'
# run test limits and mandatory rows
as "$QC_TOKEN" POST quality/reports "{\"type\":8,\"workOrderId\":$WO2,\"itemId\":$BOLT,\"values\":[{\"parameter\":\"Overspeed trip 1\",\"measured\":109}]}"; RT1=$(val .data.id)
as "$QC_TOKEN" POST quality/reports/action "{\"id\":$RT1,\"action\":\"submit\"}"; check "run test without the mandatory rows cannot be submitted" '.success == false and (.message|contains("Overspeed trip 2"))'
as "$QC_TOKEN" POST quality/reports "{\"type\":8,\"workOrderId\":$WO2,\"itemId\":$BOLT,\"values\":[{\"parameter\":\"Overspeed trip 1\",\"measured\":109},{\"parameter\":\"Overspeed trip 2\",\"measured\":110},{\"parameter\":\"Overspeed trip 3\",\"measured\":113},{\"parameter\":\"Continuous run duration\",\"measured\":5}]}"
check "overspeed 113 % is outside 107.8 - 112.2 -> Fail" '.success and ([.data.values[]|select(.parameter=="Overspeed trip 3")|.pass][0] == false)'
as "$QC_TOKEN" POST quality/reports "{\"type\":8,\"itpActivityId\":$A3,\"values\":[]}"; no "report type must match the ITP activity (hydro activity needs a hydro report)"
as "$QC_TOKEN" POST quality/reports "{\"type\":7,\"itpActivityId\":$A3,\"values\":[],\"equipmentIds\":[$G2]}"; no "report with an overdue instrument rejected"

# hydro test on the pump: limits come only from engineering
HYD=$(jq -nc --argjson a "$A3" --argjson pg "$PG1" '{type:7,itpActivityId:$a,procedureRef:"API 610 8.3",values:[{parameter:"Test pressure",unit:"bar",measured:16.5},{parameter:"Hold time",unit:"min",measured:35}],equipmentIds:[$pg]}')
as "$QC_TOKEN" POST quality/reports "$HYD"; check "hydro report: test pressure has no limit yet (unknown, not invented)" '.success and ([.data.values[]|select(.parameter=="Test pressure")|.requiresLimits][0] == true) and ([.data.values[]|select(.parameter=="Test pressure")|.pass][0] == null) and (.data.problems|length) > 0'
HR=$(val .data.id); HRV=$(val .data.rowVersion)
as "$QC_TOKEN" POST quality/reports/action "{\"id\":$HR,\"action\":\"submit\"}"; nomsg "hydro report cannot be submitted without engineering limits" "حدود پذیرش"
as "$QC_TOKEN" POST quality/itps/limits "{\"parameterId\":$P_TESTP,\"min\":15,\"max\":18,\"source\":\"x\"}"; no "QC cannot set acceptance limits (engineering decides)"
as "$ENG_TOKEN" POST quality/itps/limits "{\"parameterId\":$P_TESTP,\"min\":15,\"source\":\"MAWP 11 bar x 1.5 = 16.5 (datasheet DS-77)\"}"; check "engineering sets the limit on the ITP" ".success and ([.data.activities[]|select(.id==$A3)|.parameters[]|select(.name==\"Test pressure\")|.min][0] == 15)"
as "$ENG_TOKEN" POST quality/itps/limits "{\"parameterId\":$P_TESTP,\"min\":20,\"max\":10}"; nomsg "inverted limits rejected" "بیشتر است"
as "$QC_TOKEN" POST quality/reports "$(echo "$HYD" | jq -c --argjson id "$HR" --arg rv "$HRV" '. + {id:$id,rowVersion:$rv}')"; check "report re-evaluated against the engineering limit: Pass" '.success and (.data.problems|length) == 0 and ([.data.values[]|select(.parameter=="Test pressure")|.pass][0] == true)'
as "$ENG_TOKEN" POST quality/itps/limits "{\"parameterId\":$P_TESTP,\"min\":15,\"max\":18,\"source\":\"MAWP 11 bar x 1.5 = 16.5 (datasheet DS-77)\"}"

# --- A (continued). W points waived by notice, reports complete the activity, release row rules
as "$QC_TOKEN" POST quality/itps/activity/action "{\"id\":$A2,\"action\":\"done\"}"; check "activity 2: done refused (vendor H, TPI W notice and report missing)" '.success == false and (.message|contains("H"))'
as "$QC_TOKEN" POST quality/itps/release "{\"activityId\":$A2,\"party\":1,\"action\":1,\"personName\":\"Vendor QC\",\"reference\":\"V-REL-2\"}"; check "activity 2 vendor H release" '.success'
as "$QC_TOKEN" POST quality/itps/activity/action "{\"id\":$A2,\"action\":\"done\"}"; check "activity 2: W point needs the witness notice; the report is still missing" '.success == false and (.message|contains("W"))'
as "$QC_TOKEN" POST quality/itps/activity/action "{\"id\":$A2,\"action\":\"notify\",\"date\":\"$(dd -12)\"}"; check "witness notice sent 12 days ago (notice period 10 days)" ".success and ([.data.activities[]|select(.id==$A2)|.status][0] == 1) and ([.data.activities[]|select(.id==$A2)|.witnessDeadline][0] != null)"
as "$QC_TOKEN" POST quality/itps/activity/action "{\"id\":$A2,\"action\":\"done\"}"; check "report still required for activity 2" '.success == false'
DIMV=$(jq -nc --argjson a "$A2" --argjson e "$G4" '{type:12,itpActivityId:$a,equipmentIds:[$e],values:[{parameter:"Bore diameter",unit:"mm",min:99.95,max:100.05,measured:100.01}]}')
as "$QC_TOKEN" POST quality/reports "$DIMV"; DR=$(val .data.id)
as "$QC_TOKEN" POST quality/reports/action "{\"id\":$DR,\"action\":\"submit\"}"; check "dimensional report submitted" '.success and .data.result == 1'
as "$QC2_TOKEN" POST quality/reports/action "{\"id\":$DR,\"action\":\"approve\"}"; check "approved by the second QC" '.success and .data.status == 2'
as "$QC_TOKEN" GET "quality/itps/$ITP1"; check "approved passing report completes activity 2; the TPI witness was waived by notice (W only)" "([.data.activities[]|select(.id==$A2)|.status][0] == 2) and ([.data.activities[]|select(.id==$A2)|.releases[]|select(.waivedByNotice)|.party]|index(2)) != null"
# activity 3 (hydro, H/H/W): both H releases and the report
as "$QC_TOKEN" POST quality/reports/action "{\"id\":$HR,\"action\":\"submit\"}"; check "hydro report submitted" '.success and .data.status == 1 and .data.result == 1'
as "$QC2_TOKEN" POST quality/reports/action "{\"id\":$HR,\"action\":\"approve\"}"; check "hydro report approved" '.success and .data.status == 2'
as "$QC_TOKEN" GET "quality/itps/$ITP1"; check "approved report alone does not finish the activity: H points of vendor and TPI are open" "([.data.activities[]|select(.id==$A3)|.status][0] != 2) and ([.data.activities[]|select(.id==$A3)|.blockers|length][0] >= 2)"
as "$QC_TOKEN" POST quality/itps/release "{\"activityId\":$A3,\"party\":1,\"action\":1,\"personName\":\"Vendor QC\",\"reference\":\"V-REL-3\"}"; check "vendor H release on the hydro test" '.success'
as "$QC_TOKEN" POST quality/itps/activity/action "{\"id\":$A3,\"action\":\"notify\",\"date\":\"$(dd -12)\"}"
as "$QC_TOKEN" POST quality/itps/release "{\"activityId\":$A3,\"party\":2,\"action\":1,\"personName\":\"TPI Inspector\",\"organisation\":\"TPI Co\",\"reference\":\"TPI-REL-3\"}"
check "TPI H release completes the hydro activity (client W waived by notice)" ".success and ([.data.activities[]|select(.id==$A3)|.status][0] == 2) and ([.data.activities[]|select(.id==$A3)|.releases[]|select(.waivedByNotice)|.party]|index(3)) != null"
as "$QC_TOKEN" GET "quality/itps/pending-points?WorkOrderId=$WO1"; check "pending points list holds the packing H point only" "(.data.data|length) == 1 and .data.data[0].itemNo == \"6\""

# --- F. welding: WPS/PQR approvals, welder qualification and 6-month continuity (ASME IX QW-322)
call POST quality/pqrs "{\"number\":\"PQR-001\",\"process\":\"GTAW\",\"codeEdition\":\"ASME IX 2023\",\"resultsDocumentId\":\"$DOCID\"}"; check "PQR drafted by planning/PMO" '.success and .data.approved == false'
PQR1=$(val .data.id)
as "$ENG_TOKEN" POST quality/pqrs/action "{\"id\":$PQR1,\"action\":\"approve\"}"; check "PQR approved by engineering (not its creator)" '.success and .data.approved == true'
as "$ENG_TOKEN" POST quality/pqrs "{\"number\":\"PQR-002\",\"process\":\"GTAW\"}"; PQR2=$(val .data.id)
as "$ENG_TOKEN" POST quality/pqrs/action "{\"id\":$PQR2,\"action\":\"approve\"}"; nomsg "engineer cannot approve a PQR he created" "تفکیک وظایف"
call POST quality/wps "{\"number\":\"WPS-001\",\"revision\":\"0\",\"process\":\"GTAW\",\"basePNo\":\"P-1\",\"preheatMinC\":100,\"interpassMaxC\":250,\"pqrIds\":[$PQR2]}"; WPSX=$(val .data.id)
as "$QC_TOKEN" POST quality/wps/action "{\"id\":$WPSX,\"action\":\"approve-qc\"}"; nomsg "WPS without an approved PQR cannot be approved" "PQR تاییدشده"
call POST quality/wps "{\"number\":\"WPS-002\",\"revision\":\"0\",\"process\":\"GTAW\",\"basePNo\":\"P-1\",\"preheatMinC\":100,\"interpassMaxC\":250,\"pqrIds\":[$PQR1]}"; check "WPS drafted" '.success and .data.status == 0'
WPS1=$(val .data.id)
as "$ENG_TOKEN" POST quality/wps/action "{\"id\":$WPS1,\"action\":\"approve-qc\"}"; no "engineering cannot give the QC approval"
as "$QC_TOKEN" POST quality/wps/action "{\"id\":$WPS1,\"action\":\"approve-engineering\"}"; no "QC cannot give the engineering approval"
as "$QC_TOKEN" POST quality/wps/action "{\"id\":$WPS1,\"action\":\"approve-qc\"}"; check "QC approval recorded; WPS still not approved" '.success and .data.status == 0 and .data.qcApprovedAt != null'
as "$QC_TOKEN" POST quality/weld-logs "{\"weldDate\":\"$D0\",\"workOrderId\":$WO2,\"jointNo\":\"J-0\",\"wpsId\":$WPS1,\"qualificationId\":$QPT2,\"vt\":1}"; nomsg "weld log against a WPS with only one approval rejected" "تایید نهایی"
as "$ENG_TOKEN" POST quality/wps/action "{\"id\":$WPS1,\"action\":\"approve-engineering\"}"; check "WPS approved after the second, different approver" '.success and .data.status == 1'
# a WPS created by engineering cannot be approved by engineering
as "$ENG_TOKEN" POST quality/wps "{\"number\":\"WPS-003\",\"revision\":\"0\",\"process\":\"GTAW\",\"pqrIds\":[$PQR1]}"; WPS3=$(val .data.id)
as "$QC_TOKEN" POST quality/wps/action "{\"id\":$WPS3,\"action\":\"approve-qc\"}"
as "$ENG_TOKEN" POST quality/wps/action "{\"id\":$WPS3,\"action\":\"approve-engineering\"}"; nomsg "creator of a WPS cannot approve it (segregation of duties)" "تفکیک وظایف"

wperson() { as "$QA_TOKEN" POST quality/persons "{\"name\":\"$1\",\"personnelCode\":\"$2\",\"employer\":1,\"stampId\":\"$3\"}"; val .data.id; }
wqual() { # wqual PERSON NUMBER METHOD ISSUED-DAYS-AGO
  as "$QA_TOKEN" POST quality/qualifications "{\"personId\":$1,\"kind\":1,\"method\":\"$3\",\"scheme\":1,\"scope\":\"P-1 plate\",\"certificateNumber\":\"$2\",\"issuedDate\":\"$(dd -$4)\",\"certificateDocumentId\":\"$DOCID\"}"; val .data.id; }
WP1=$(wperson "Welder One" W-001 W11); QW1=$(wqual $WP1 WQ-1 GTAW 30)
WP2=$(wperson "Welder Two" W-002 W12); QW2=$(wqual $WP2 WQ-2 GTAW 300)
WP3=$(wperson "Welder Three" W-003 W13); QW3=$(wqual $WP3 WQ-3 GTAW 300)
WP4=$(wperson "Welder Four" W-004 W14); QW4=$(wqual $WP4 WQ-4 GTAW 170)
WP5=$(wperson "Welder Five" W-005 W15); QW5=$(wqual $WP5 WQ-5 SMAW 10)
WP6=$(wperson "Welder Six" W-006 W16); QW6=$(wqual $WP6 WQ-6 GTAW 10)
call GET "quality/qualifications?PersonId=$WP2"; check "welder without welding for more than six months is lapsed (computed)" '.data.data[0].state == 3 and .data.data[0].lapsesOn != null'
weld() { # weld DATE JOINT QUAL [PREHEAT INTERPASS]
  as "$QC_TOKEN" POST quality/weld-logs "{\"weldDate\":\"$1\",\"workOrderId\":$WO2,\"jointNo\":\"$2\",\"wpsId\":$WPS1,\"qualificationId\":$3,\"preheatC\":${4:-120},\"interpassC\":${5:-200},\"fillerBatch\":\"FB-1\",\"vt\":1}"
}
weld "$D0" J-1 $QW1; check "weld log by a qualified welder on an approved WPS" '.success and .data.welderName == "Welder One" and .data.wpsNumber == "WPS-002/0"'
weld "$D0" J-2 $QW5; check "welder certified for SMAW cannot weld a GTAW WPS" '.success == false and (.message|contains("SMAW"))'
weld "$D0" J-3 $QW2; check "lapsed welder cannot weld (continuity broken)" '.success == false and (.message|contains("شش ماه"))'
weld "$D0" J-4 $QW1 80 200; check "preheat below the WPS minimum rejected" '.success == false and (.message|contains("پیش‌گرم"))'
weld "$D0" J-5 $QW1 120 300; check "interpass above the WPS maximum rejected" '.success == false and (.message|contains("بین‌پاسی"))'
as "$QC_TOKEN" POST quality/weld-logs "{\"weldDate\":\"$D0\",\"workOrderId\":$WO2,\"jointNo\":\"J-6\",\"wpsId\":$WPSX,\"qualificationId\":$QW1,\"vt\":1}"; nomsg "weld log against a WPS that is not approved rejected" "تایید نهایی"
as "$QA_TOKEN" POST quality/weld-logs "{\"weldDate\":\"$D0\",\"workOrderId\":$WO2,\"jointNo\":\"J-7\",\"wpsId\":$WPS1,\"qualificationId\":$QW1,\"vt\":1}"; no "only QC writes weld logs"
weld "$(dd -250)" J-10 $QW3; check "chain: weld 250 days ago within the first six months" '.success'
weld "$D0" J-11 $QW3; check "chain: weld today, last weld 250 days ago -> lapsed" '.success == false'
weld "$(dd -150)" J-12 $QW3; check "chain: weld 150 days ago still inside six months of the previous weld" '.success'
weld "$D0" J-13 $QW3; check "chain: weld today is inside six months of the weld 150 days ago" '.success'
as "$QA_TOKEN" POST quality/qualifications "{\"personId\":$WP2,\"kind\":1,\"method\":\"GTAW\",\"scheme\":1,\"certificateNumber\":\"WQ-2R\",\"issuedDate\":\"$(dd -5)\",\"renewsQualificationId\":$QW2}"; check "re-test registered as renewal" '.success and .data.renewsQualificationId != null'
QW2N=$(val .data.id)
weld "$D0" J-14 $QW2N; check "renewed qualification can weld" '.success'
weld "$D0" J-15 $QW2; check "old certificate is superseded and cannot be used" '.success == false'
as "$QA_TOKEN" POST quality/qualifications/action "{\"id\":$QW6,\"action\":\"revoke\"}"; no "revoking needs a reason"
as "$QA_TOKEN" POST quality/qualifications/action "{\"id\":$QW6,\"action\":\"revoke\",\"reason\":\"Rejected welds in the audit\"}"; check "qualification revoked" '.success and .data.revoked == true and .data.state == 4'
weld "$D0" J-16 $QW6; check "revoked qualification cannot be used" '.success == false'
call GET "quality/qualifications/continuity?Days=30"; check "continuity list shows the welder whose six months end within 30 days" "(.data.data|map(.id)|index($QW4)) != null"
call GET "quality/weld-logs?WorkOrderId=$WO2"; check "weld logs of work order 2: 5 accepted" '(.data.data|length) == 5'

# --- G. material certificates (EN 10204) and traceability
as "$WH_TOKEN" POST quality/certificates "{\"lotId\":$LOT,\"type\":2,\"number\":\"MTC-3.1-9\",\"issuer\":\"Mill A\",\"documentId\":\"$DOCID\",\"summary\":\"26NiCrMoV14-5 chemistry and mechanicals OK\"}"; check "warehouse registers the certificate of the lot (pending)" '.success and .data.result == 0 and .data.heatNumber == "H-7781"'
CERT1=$(val .data.id)
as "$WH_TOKEN" POST quality/certificates/verify "{\"id\":$CERT1,\"accept\":true}"; no "warehouse cannot verify a certificate (QC only)"
as "$QC_TOKEN" POST quality/certificates/verify "{\"id\":$CERT1,\"accept\":false}"; no "rejection needs a reason"
as "$QC_TOKEN" POST quality/certificates/verify "{\"id\":$CERT1,\"accept\":true,\"note\":\"checked against heat H-7781\"}"; check "QC verifies the certificate" '.success and .data.result == 1 and .data.verifiedByName != null'
as "$QC_TOKEN" POST quality/certificates "{\"lotId\":$LOT,\"type\":1,\"number\":\"MTC-2.2-1\",\"documentId\":\"$DOCID\"}"; CERT2=$(val .data.id)
as "$QC_TOKEN" POST quality/certificates/verify "{\"id\":$CERT2,\"accept\":true}"; nomsg "the person who registered a certificate cannot verify it" "تفکیک وظایف"
as "$QC2_TOKEN" POST quality/certificates/verify "{\"id\":$CERT2,\"accept\":false,\"note\":\"Not the right heat\"}"; check "certificate rejected with a reason" '.success and .data.result == 2'
as "$QC_TOKEN" GET "quality/traceability?LotId=$LOT"; check "traceability by lot: heat number, certificates and receipts" '.success and .data.lots[0].lotNumber == "H-7781" and .data.lots[0].heatNumber == "H-7781" and (.data.lots[0].certificates|length) >= 1 and (.data.lots[0].receipts|length) >= 1'
as "$QC_TOKEN" GET "quality/traceability"; no "traceability without a serial or lot rejected"

# required certificate type on an item blocks incoming acceptance
item PL31 '{"code":"01003010060102","title":"ورق با گواهی 3.1","unit":"kg","trackingMode":1,"requiresIncomingInspection":true,"requiredCertificateType":2}' "plate that needs an EN 10204 3.1 certificate"
call GET "inventory/items/by-code?code=01003010060102"; check "required certificate type saved on the item" '.data.requiredCertificateType == 2'
call POST inventory/documents "{\"type\":1,\"documentDate\":\"$D0\",\"warehouseId\":$WQ,\"destinationWarehouseId\":$W1,\"referenceNumber\":\"INV-901\",\"lines\":[{\"itemId\":$PL31,\"quantity\":20,\"heatNumber\":\"HT-3100\",\"unitPrice\":1000}]}"; PRC=$(val .data.id)
call POST "inventory/documents/$PRC/confirm"; check "receipt of the 3.1 plate into quarantine" '.success'
call GET "inventory/quality/inspections?Status=0"; QI31=$(jq -r ".data.data[]|select(.itemId==$PL31)|.id" "$B")
call GET "inventory/stock/lots?ItemId=$PL31"; PL31LOT=$(val '.data.data[0].id')
as "$QC_TOKEN" POST inventory/quality/inspections "{\"id\":$QI31,\"acceptedQuantity\":20,\"rejectedQuantity\":0}"; check "incoming acceptance refused: no verified certificate" '.success == false and (.message|contains("EN 10204"))'
as "$WH_TOKEN" POST quality/certificates "{\"lotId\":$PL31LOT,\"type\":1,\"number\":\"MTC-2.2-77\",\"documentId\":\"$DOCID\"}"; C22=$(val .data.id)
as "$QC_TOKEN" POST quality/certificates/verify "{\"id\":$C22,\"accept\":true}"
as "$QC_TOKEN" POST inventory/quality/inspections "{\"id\":$QI31,\"acceptedQuantity\":20,\"rejectedQuantity\":0}"; check "a 2.2 certificate does not satisfy a 3.1 requirement" '.success == false'
as "$WH_TOKEN" POST quality/certificates "{\"lotId\":$PL31LOT,\"type\":2,\"number\":\"MTC-3.1-77\",\"documentId\":\"$DOCID\"}"; C31=$(val .data.id)
as "$QC_TOKEN" POST quality/certificates/verify "{\"id\":$C31,\"accept\":true}"
as "$QC_TOKEN" POST inventory/quality/inspections "{\"id\":$QI31,\"acceptedQuantity\":20,\"rejectedQuantity\":0}"; check "with a verified 3.1 certificate the lot is accepted" '.success and .data.status == 1'

# --- H. NCR additions: due date, CAPA for major NCR, committee size (see also the NCR section above)
call GET "inventory/quality/ncr/$NCR2"; check "major NCR from the NCR section has its CAPA drafted by the system" '.success and .data.capaId != null and (.data.capaNumber|startswith("CAPA-")) and .data.dueDate != null'
NCR2CAPA=$(val .data.capaId)

# --- I. CAPA (ISO 9001 10.2): owner, actions, effectiveness check by another person
capa_cycle() { # capa_cycle ID OWNER_ID OWNER_TOKEN EFFECTIVENESS_TOKEN CLOSE_TOKEN
  as "$QA_TOKEN" GET "quality/capas/$1"; local rv src sid
  rv=$(val .data.rowVersion); src=$(val .data.source); sid=$(val '.data.sourceId // empty')
  local body
  body=$(jq -nc --argjson id "$1" --arg rv "$rv" --argjson src "$src" --arg sid "$sid" --argjson owner "$2" --arg due "$(dd 20)" --arg adue "$(dd 10)" \
    '{id:$id,rowVersion:$rv,source:$src,problem:"Problem found",rootCauseMethod:1,rootCauseAnalysis:"Why 1 ... why 5: procedure gap",ownerUserId:$owner,dueDate:$due,effectivenessCriteria:"No recurrence in the next 3 lots",actions:[{description:"Revise the procedure and train staff",ownerUserId:$owner,dueDate:$adue}]} + (if $sid != "" then {sourceId:($sid|tonumber)} else {} end)')
  as "$QA_TOKEN" POST quality/capas "$body"
}
capa_cycle $NCR2CAPA $PRODID "$PROD_TOKEN"; check "QA completes the CAPA: owner, root cause, action, effectiveness criteria -> actions defined" '.success and .data.status == 1 and .data.ownerName != null and (.data.actions|length) == 1'
CA_ACT=$(val '.data.actions[0].id')
as "$PROD_TOKEN" POST quality/capas/action "{\"id\":$NCR2CAPA,\"action\":\"implemented\"}"; no "implemented refused while an action is not done"
as "$PROD_TOKEN" POST quality/capas/action "{\"id\":$NCR2CAPA,\"action\":\"complete-action\",\"actionId\":$CA_ACT}"; no "completing an action needs evidence or a note"
as "$PROD_TOKEN" POST quality/capas/action "{\"id\":$NCR2CAPA,\"action\":\"complete-action\",\"actionId\":$CA_ACT,\"note\":\"Procedure revised, training record TR-9\"}"; check "owner completes the action" '.success'
as "$PROD_TOKEN" POST quality/capas/action "{\"id\":$NCR2CAPA,\"action\":\"implemented\"}"; check "CAPA implemented: effectiveness review date set automatically" '.success and .data.status == 2 and .data.effectivenessDueDate != null'
as "$PROD_TOKEN" POST quality/capas/action "{\"id\":$NCR2CAPA,\"action\":\"effectiveness\",\"note\":\"ok\",\"passed\":true}"; nomsg "the owner cannot verify effectiveness (role and segregation)" "مجوز"
as "$QC_TOKEN" POST quality/capas/action "{\"id\":$NCR2CAPA,\"action\":\"close\"}"; no "closing before the effectiveness check rejected"
as "$QC_TOKEN" POST quality/capas/action "{\"id\":$NCR2CAPA,\"action\":\"effectiveness\",\"passed\":true}"; no "effectiveness result needs a written note against the criteria"
as "$QC_TOKEN" POST quality/capas/action "{\"id\":$NCR2CAPA,\"action\":\"effectiveness\",\"note\":\"3 lots inspected, no recurrence\",\"passed\":true}"; check "effectiveness verified by QC" '.success and .data.status == 3'
as "$PROD_TOKEN" POST quality/capas/action "{\"id\":$NCR2CAPA,\"action\":\"close\"}"; no "only QA/QC close a CAPA"
as "$QA_TOKEN" POST quality/capas/action "{\"id\":$NCR2CAPA,\"action\":\"close\",\"note\":\"closed\"}"; check "CAPA closed by QA" '.success and .data.status == 4 and .data.closedByName != null'
# a CAPA owned by QA cannot be closed by QA
as "$QA_TOKEN" POST quality/capas "{\"problem\":\"Calibration labels missing\",\"source\":5,\"rootCauseMethod\":2,\"rootCauseAnalysis\":\"Fishbone\",\"ownerUserId\":$QAID,\"dueDate\":\"$(dd 15)\",\"effectivenessCriteria\":\"Audit shows labels\",\"actions\":[{\"description\":\"Label all instruments\",\"ownerUserId\":$QAID,\"dueDate\":\"$(dd 5)\"}]}"; check "manual CAPA with owner QA" '.success and .data.status == 1 and .data.autoDrafted == false'
CP2=$(val .data.id); CP2A=$(val '.data.actions[0].id')
as "$QA_TOKEN" POST quality/capas/action "{\"id\":$CP2,\"action\":\"complete-action\",\"actionId\":$CP2A,\"note\":\"done\"}"
as "$QA_TOKEN" POST quality/capas/action "{\"id\":$CP2,\"action\":\"implemented\"}"
as "$QA_TOKEN" POST quality/capas/action "{\"id\":$CP2,\"action\":\"effectiveness\",\"note\":\"self check\",\"passed\":true}"; nomsg "the owner (QA) cannot verify the effectiveness of his own CAPA" "تفکیک وظایف"
as "$QC_TOKEN" POST quality/capas/action "{\"id\":$CP2,\"action\":\"effectiveness\",\"note\":\"labels present on 14 of 14\",\"passed\":true}"
as "$QA_TOKEN" POST quality/capas/action "{\"id\":$CP2,\"action\":\"close\"}"; nomsg "the owner (QA) cannot close his own CAPA" "تفکیک وظایف"
as "$QC2_TOKEN" POST quality/capas/action "{\"id\":$CP2,\"action\":\"close\"}"; check "another quality person closes it" '.success and .data.status == 4'
# overdue CAPA is computed and listed
as "$QA_TOKEN" POST quality/capas "{\"problem\":\"Late CAPA\",\"source\":9,\"rootCauseMethod\":1,\"rootCauseAnalysis\":\"x\",\"ownerUserId\":$PRODID,\"dueDate\":\"$(dd -5)\",\"effectivenessCriteria\":\"y\",\"actions\":[{\"description\":\"z\",\"ownerUserId\":$PRODID,\"dueDate\":\"$(dd -5)\"}]}"; CP3=$(val .data.id)
as "$QA_TOKEN" GET quality/capas/overdue; check "overdue CAPA listed (computed from the due date)" "(.data.data|map(.id)|index($CP3)) != null"

# --- J. customer complaints (response due within 10 days)
as "$COM_TOKEN" POST quality/complaints "{\"contractId\":$QCT,\"workOrderId\":$WO1,\"channel\":\"letter\",\"description\":\"Vibration after installation\",\"severity\":2,\"receivedDate\":\"$(dd -15)\"}"; check "commercial registers a complaint; response was due 5 days ago -> overdue" '.success and .data.overdue == true and (.data.number|startswith("CC-")) and .data.status == 0'
CC1=$(val .data.id)
as "$COM_TOKEN" POST quality/complaints "{\"workOrderId\":$WO1,\"description\":\"second complaint left open\",\"severity\":1,\"receivedDate\":\"$(dd -20)\"}"; CC2=$(val .data.id)
as "$QA_TOKEN" POST quality/complaints/action "{\"id\":$CC1,\"action\":\"respond\",\"response\":\"x\"}"; no "cannot respond before the review"
as "$QA_TOKEN" POST quality/complaints/action "{\"id\":$CC1,\"action\":\"review\"}"; check "complaint under review" '.success and .data.status == 1'
as "$QA_TOKEN" POST quality/complaints/action "{\"id\":$CC1,\"action\":\"respond\"}"; no "response text is required"
as "$QA_TOKEN" POST quality/complaints/action "{\"id\":$CC1,\"action\":\"respond\",\"response\":\"Site visit on Monday, root cause analysis opened\"}"; check "responded" '.success and .data.status == 2'
as "$QA_TOKEN" POST quality/complaints/action "{\"id\":$CC1,\"action\":\"close\"}"; no "major complaint cannot be closed without an NCR or CAPA link"
as "$QA_TOKEN" POST quality/complaints/action "{\"id\":$CC1,\"action\":\"create-capa\"}"; check "CAPA drafted from the complaint" '.success and .data.capaId != null and (.data.capaNumber|startswith("CAPA-"))'
as "$QA_TOKEN" POST quality/complaints/action "{\"id\":$CC1,\"action\":\"close\"}"; check "complaint closed" '.success and .data.status == 3'

# --- K. document control (PR/WI/FO): preparer, reviewer and approver are three different people
as "$WH_TOKEN" POST quality/documents '{"type":1,"department":"QC","title":"Calibration control"}'; no "warehouse cannot create controlled documents"
as "$QC_TOKEN" POST quality/documents '{"type":1,"department":"QC","title":"Control of measuring equipment","retentionClass":"Quality records","responsibleUnit":"QC"}'; check "controlled document coded TYPE-DEPT-NNN" '.success and (.data.code|test("^PR-QC-[0-9]{3}$"))'
DOC1=$(val .data.id); DOC1CODE=$(val .data.code)
as "$QC_TOKEN" GET "quality/documents/check-code?type=1&department=QC&serial=1"; check "code check reports an existing code and the next free one" '.success and .data.exists == true and .data.nextFreeCode != null'
as "$QC_TOKEN" POST quality/documents/revisions "{\"documentId\":$DOC1,\"revision\":\"00\",\"changeSummary\":\"First issue\"}"; check "revision 00 drafted (no file yet)" '.success'
DREV=$(val '.data.revisions[0].id')
as "$QC_TOKEN" POST quality/documents/revisions/action "{\"id\":$DREV,\"action\":\"review\"}"; nomsg "preparer cannot review his own revision" "تفکیک وظایف"
as "$ENG_TOKEN" POST quality/documents/revisions/action "{\"id\":$DREV,\"action\":\"review\"}"; check "reviewed by engineering" '.success and ([.data.revisions[]|select(.id=='$DREV')|.status][0] == 1)'
as "$QA_TOKEN" POST quality/documents/revisions/action "{\"id\":$DREV,\"action\":\"approve\",\"effectiveDate\":\"$D0\"}"; nomsg "approval needs the file attached" "فایل بازنگری"
as "$QC_TOKEN" POST quality/documents/revisions "{\"documentId\":$DOC1,\"revision\":\"00\",\"changeSummary\":\"First issue\",\"fileDocumentId\":\"$DOCID\"}"; no "duplicate revision number rejected"
# attach the file by saving the revision again is not allowed once in review, so use a second document for the full cycle
as "$QC_TOKEN" POST quality/documents '{"type":2,"department":"QC","title":"Welding inspection instruction"}'; DOC2=$(val .data.id)
as "$QC_TOKEN" POST quality/documents/revisions "{\"documentId\":$DOC2,\"revision\":\"00\",\"changeSummary\":\"First issue\",\"fileDocumentId\":\"$DOCID\"}"; DREV2=$(val '.data.revisions[0].id')
as "$ENG_TOKEN" POST quality/documents/revisions/action "{\"id\":$DREV2,\"action\":\"review\"}"
as "$ENG_TOKEN" POST quality/documents/revisions/action "{\"id\":$DREV2,\"action\":\"approve\"}"; nomsg "reviewer cannot approve (and engineering is not an approver role)" "تایید سند"
as "$QC_TOKEN" POST quality/documents/revisions/action "{\"id\":$DREV2,\"action\":\"approve\"}"; nomsg "preparer cannot approve" "تایید سند"
as "$QA_TOKEN" POST quality/documents/revisions/action "{\"id\":$DREV2,\"action\":\"approve\",\"effectiveDate\":\"$D0\"}"; check "QA approves the third-person revision -> effective" ".success and ([.data.revisions[]|select(.id==$DREV2)|.status][0] == 3) and .data.currentRevision == \"00\" and .data.status == 3"
as "$QC_TOKEN" POST quality/documents/revisions/action "{\"id\":$DREV2,\"action\":\"distribute\",\"recipientUserIds\":[$PRODID,$WHID],\"recipientUnits\":[\"Assembly\"]}"; check "effective revision distributed" "([.data.revisions[]|select(.id==$DREV2)|.distributions|length][0]) == 3"
DIST=$(jq -r ".data.revisions[]|select(.id==$DREV2)|.distributions[]|select(.recipientUserId==$PRODID)|.id" "$B")
as "$WH_TOKEN" POST quality/documents/revisions/action "{\"id\":$DREV2,\"action\":\"acknowledge\",\"distributionId\":$DIST}"; no "a recipient cannot acknowledge for someone else"
as "$PROD_TOKEN" POST quality/documents/revisions/action "{\"id\":$DREV2,\"action\":\"acknowledge\",\"distributionId\":$DIST}"; check "recipient acknowledges receipt" ".success and ([.data.revisions[]|select(.id==$DREV2)|.distributions[]|select(.id==$DIST)|.acknowledgedAt][0] != null)"
as "$QC_TOKEN" POST quality/documents/revisions "{\"documentId\":$DOC2,\"revision\":\"01\",\"changeSummary\":\"Added NDT hold points\",\"fileDocumentId\":\"$DOCID\"}"; DREV3=$(val '.data.revisions[]|select(.revision=="01")|.id')
as "$ENG_TOKEN" POST quality/documents/revisions/action "{\"id\":$DREV3,\"action\":\"review\"}"
as "$QA_TOKEN" POST quality/documents/revisions/action "{\"id\":$DREV3,\"action\":\"approve\",\"effectiveDate\":\"$D0\"}"; check "new revision effective; the previous one becomes obsolete" "([.data.revisions[]|select(.revision==\"00\")|.status][0] == 4) and ([.data.revisions[]|select(.revision==\"01\")|.status][0] == 3) and .data.currentRevision == \"01\""
as "$QC_TOKEN" GET quality/documents/master-list; check "master list holds only effective documents" '.success and ([.data.data[]|.status]|all(. == 3)) and (.data.data|length) >= 1'
as "$QC_TOKEN" GET quality/retention; check "retention rules seeded with a source" '.success and (.data|length) >= 3'
as "$QA_TOKEN" POST quality/retention '{"recordType":"Welding procedures","permanent":true,"source":"Company rule"}'; check "retention rule saved by QA" '.success'

# --- L. audits, findings and management review
as "$QA_TOKEN" POST quality/audit-programs '{"year":1405,"scope":"All units, ISO 9001:2015 clause 9.2"}'; check "annual audit program" '.success and .data.id > 0'
APR=$(val .data.id)
call POST quality/audits "{\"programId\":$APR,\"auditedUnit\":\"QC\",\"criteria\":\"ISO 9001:2015\",\"plannedDate\":\"$(dd 5)\",\"leadAuditorUserId\":$QCID}"; check "auditor cannot audit his own unit (independence, ISO 9.2)" '.success == false'
call POST quality/audits "{\"programId\":$APR,\"auditedUnit\":\"QC\",\"criteria\":\"ISO 9001:2015, WI-QC-004\",\"plannedDate\":\"$(dd 5)\",\"leadAuditorUserId\":$QAID,\"teamUserIds\":[$ENGID]}"; check "audit planned by an auditor from another unit" '.success and (.data.number|startswith("AUD-")) and .data.status == 0'
AUD=$(val .data.id)
as "$WH_TOKEN" POST quality/audits/findings "{\"auditId\":$AUD,\"clause\":\"7.1.5\",\"type\":2,\"evidence\":\"x\"}"; no "warehouse cannot add findings"
call POST quality/audits/action "{\"id\":$AUD,\"action\":\"done\"}"; no "audit cannot be marked done without its report"
call POST quality/audits/findings "{\"auditId\":$AUD,\"clause\":\"7.1.5\",\"type\":2,\"evidence\":\"Two calipers without calibration label\"}"; check "minor nonconformance finding drafts a CAPA automatically" '.success and (.data.findings|length) == 1 and .data.findings[0].capaId != null'
AFC=$(val '.data.findings[0].capaId')
call POST quality/audits/findings "{\"auditId\":$AUD,\"clause\":\"8.5\",\"type\":3,\"evidence\":\"Good practice in packing\"}"; check "observation does not create a CAPA" '.success and ([.data.findings[]|select(.type==3)|.capaId][0] == null)'
call POST quality/audits/action "{\"id\":$AUD,\"action\":\"done\",\"reportDocumentId\":\"$DOCID\"}"; check "audit done with its report" '.success and .data.status == 1'
call POST quality/audits/action "{\"id\":$AUD,\"action\":\"close\"}"; nomsg "audit cannot be closed while the finding CAPA is open" "CAPA"
capa_cycle $AFC $PRODID "$PROD_TOKEN"; CAA=$(val '.data.actions[0].id')
as "$PROD_TOKEN" POST quality/capas/action "{\"id\":$AFC,\"action\":\"complete-action\",\"actionId\":$CAA,\"note\":\"Labels fitted\"}"
as "$PROD_TOKEN" POST quality/capas/action "{\"id\":$AFC,\"action\":\"implemented\"}"
as "$QC_TOKEN" POST quality/capas/action "{\"id\":$AFC,\"action\":\"effectiveness\",\"note\":\"All labelled\",\"passed\":true}"
as "$QA_TOKEN" POST quality/capas/action "{\"id\":$AFC,\"action\":\"close\"}"
call POST quality/audits/action "{\"id\":$AUD,\"action\":\"close\"}"; check "audit closed once the CAPA is closed" '.success and .data.status == 2'
# management review: inputs are computed from the system data
call POST quality/management-reviews "{\"reviewDate\":\"$D0\",\"attendees\":\"CEO, QA, QC, Production\"}"; check "management review drafted with the 12 inputs of clause 9.3.2" '.success and (.data.inputs|length) == 12'
MR1=$(val .data.id)
check "inputs for CAPA/NCR, audits, objectives are computed by the system" '([.data.inputs[]|select(.category==6)|.autoDrafted][0] == true) and ([.data.inputs[]|select(.category==8)|.autoDrafted][0] == true) and ([.data.inputs[]|select(.category==3)|.autoDrafted][0] == true)'
MRCATS=$(jq -r '.data.inputs[]|select(.text == null or .text == "")|.category' "$B")
call POST quality/management-reviews/action "{\"id\":$MR1,\"action\":\"hold\"}"; check "review cannot be held while inputs are empty" '.success == false and (.message|contains("9.3.2") or contains("ورودی"))'
for CATN in $MRCATS; do
  call POST quality/management-reviews/input "{\"reviewId\":$MR1,\"category\":$CATN,\"text\":\"Reviewed in the meeting.\"}"
done
call POST quality/management-reviews/action "{\"id\":$MR1,\"action\":\"hold\"}"; check "review held after the inputs are complete" '.success and .data.status == 1'
call POST quality/management-reviews/decision "{\"reviewId\":$MR1,\"text\":\"Add a second calibration technician\",\"responsibleUserId\":$QCID,\"dueDate\":\"$(dd 30)\",\"createCapa\":true}"; check "decision with an action creates a CAPA" '.success and (.data.decisions|length) == 1 and .data.decisions[0].capaId != null'
call POST quality/management-reviews/action "{\"id\":$MR1,\"action\":\"close\"}"; no "review cannot be closed without the minutes (FO-QA-022)"
call POST quality/management-reviews/action "{\"id\":$MR1,\"action\":\"close\",\"minutesDocumentId\":\"$DOCID\"}"; check "management review closed with the minutes" '.success and .data.status == 2'

# --- M. release note, final data book and the delivery gate
as "$QC_TOKEN" POST quality/itps/activity/action "{\"id\":$A6,\"action\":\"plan\",\"date\":\"$(dd 3)\"}"; check "packing activity planned" '.success'
as "$QC_TOKEN" GET "quality/work-orders/$WO1/release-checks"; check "all release checks of work order 1 pass (computed)" '.success and .data.canRelease == true and ([.data.checks[]|.passed]|all)'
as "$QC_TOKEN" GET "quality/work-orders/$WO1/data-book"; check "final data book: 10 sections, nothing mandatory missing" '.success and (.data.sections|length) == 10 and .data.missingCount == 0 and ([.data.sections[]|select(.no==6)|.entries[]|select(.sourceKind=="TestReport")]|length) >= 1'
as "$QC_TOKEN" GET "quality/release-notes/$RN1"; check "the same release note can now be released (computed checks all pass)" '.success and .data.status == 0 and .data.canRelease == true'
as "$QA_TOKEN" POST quality/release-notes/action "{\"id\":$RN1,\"action\":\"release\"}"; nomsg "the release note is signed by QC (QA refused)" "مدیر QC"
as "$QC_TOKEN" POST quality/release-notes "{\"workOrderId\":$WO1,\"itemsText\":\"duplicate\"}"; nomsg "second active release note for the same work order rejected" "برگه ترخیص فعال"
as "$QC_TOKEN" POST quality/release-notes "{\"workOrderId\":$WO2,\"itemsText\":\"Feed pump unit 2\"}"; RN2=$(val .data.id)
as "$QC_TOKEN" POST quality/release-notes/action "{\"id\":$RN2,\"action\":\"release\"}"; check "work order 2 cannot be released: ITP not approved, open NCRs, reports missing" '.success == false and (.message|contains("ترخیص"))'
as "$QC_TOKEN" GET "quality/work-orders/$WO2/release-checks"; check "work order 2 checks fail for the ITP and the open NCR" '.data.canRelease == false and ([.data.checks[]|select(.code=="ITP_APPROVED")|.passed][0] == false) and ([.data.checks[]|select(.code=="NO_OPEN_NCR")|.passed][0] == false)'
# delivery gate: no release -> no customer delivery
mkdelivery() { # mkdelivery WORKORDER [QUANTITY of bolts, default 1] (empty work order = none)
  local wo="" q=${2:-1}; [ -n "$1" ] && wo=",\"workOrderId\":$1"
  call POST inventory/documents "{\"type\":11,\"documentDate\":\"$D0\",\"warehouseId\":$W1,\"contractId\":$QCT$wo,\"driverName\":\"Driver\",\"vehiclePlate\":\"11A111\",\"receiverName\":\"Client rep\",\"lines\":[{\"itemId\":$BOLT,\"quantity\":$q}]}"
}
mkdelivery $WO2; DL2=$(val .data.id); as "$QC_TOKEN" POST "inventory/documents/$DL2/approve" '{"kind":4}'
call POST "inventory/documents/$DL2/confirm"; check "customer delivery of a work order without a release note refused" '.success == false and (.message|contains("ترخیص"))'
mkdelivery ""; DL0=$(val .data.id); as "$QC_TOKEN" POST "inventory/documents/$DL0/approve" '{"kind":4}'
call POST "inventory/documents/$DL0/confirm"; check "customer delivery without a work order refused (release cannot be checked)" '.success == false and (.message|contains("دستور کار"))'
as "$QC_TOKEN" GET "quality/work-orders/$WO1/data-book"; COMPANY_N=$(jq '[.data.sections[].entries[]]|length' "$B"); check "data book default variant is company (every entry)" '.data.variant == "company"'
as "$QC_TOKEN" GET "quality/work-orders/$WO1/data-book?variant=client"; check "client data book: only entries the client receives (ITP, reports with a client point, release note), fewer than the company book" ".success and .data.variant == \"client\" and ([.data.sections[].entries[]|.clientRelevant]|all) and ([.data.sections[].entries[]]|length) < $COMPANY_N and ([.data.sections[].entries[]|select(.sourceKind==\"Itp\")]|length) == 1 and ([.data.sections[].entries[]|select(.sourceKind==\"Equipment\")]|length) == 0"
as "$QC_TOKEN" GET "quality/work-orders/$WO1/data-book?variant=x"; no "unknown data book variant rejected"
as "$QC_TOKEN" POST quality/release-notes/action "{\"id\":$RN1,\"action\":\"release\"}"; check "release note signed by QC: certificate of conformity numbered" '.success and .data.status == 1 and (.data.certificateOfConformityNumber|startswith("CoC-")) and .data.releasedByName != null and ([.data.checks[]|.passed]|all)'
as "$QC_TOKEN" POST quality/release-notes/action "{\"id\":$RN1,\"action\":\"release\"}"; no "release note cannot be released twice"
as "$QC_TOKEN" POST quality/release-notes "{\"id\":$RN1,\"workOrderId\":$WO1,\"itemsText\":\"changed\"}"; nomsg "a released note cannot be edited" "ویرایش نمی"
as "$QC_TOKEN" GET "quality/work-orders/$WO1/data-book"; check "data book: release note section now complete" '[.data.sections[]|select(.no==10)|.entries[0].missing][0] == false'
mkdelivery $WO1; DL1=$(val .data.id); as "$QC_TOKEN" POST "inventory/documents/$DL1/approve" '{"kind":4}'
call POST "inventory/documents/$DL1/confirm"; check "customer delivery of the released work order is confirmed" '.success and .data.status == 1'

# --- N. ITP revision after approval (snapshot) and settings
as "$QC_TOKEN" POST quality/itps/action "{\"id\":$ITP2,\"action\":\"submit\"}"
as "$QC_TOKEN" POST quality/itps/action "{\"id\":$ITP2,\"action\":\"client-approve\",\"reference\":\"CL-2-1\"}"; check "ITP 2 approved" '.success and .data.status == 2'
A2_1=$(val '.data.activities[0].id')
as "$QC_TOKEN" POST quality/itps/activity "{\"id\":$A2_1,\"itpId\":$ITP2,\"seq\":1,\"activity\":\"Material certificates of pressure-containing parts (revised)\",\"pointVendor\":1,\"pointTpi\":3,\"pointClient\":3}"; nomsg "changing an approved ITP needs a written reason" "دلیل تغییر"
as "$QC_TOKEN" POST quality/itps/activity "{\"id\":$A2_1,\"itpId\":$ITP2,\"seq\":1,\"activity\":\"Material certificates of pressure-containing parts (revised)\",\"pointVendor\":1,\"pointTpi\":3,\"pointClient\":3,\"reason\":\"Client asked for review of all certificates\"}"; check "approved ITP edited with a reason: revision 1 (first issue is 0), back to draft, old revision kept" '.success and .data.revision == 1 and .data.status == 0 and (.data.revisions|length) >= 1'
as "$QA_TOKEN" GET quality/settings; check "quality settings: defaults with their source, major NCR committee minimum 3" '.success and ([.data[]|select(.key=="MajorNcrCommitteeMin")|.value][0] == 3) and ([.data[]|select(.key=="ItpNoticeDaysIran")|.value][0] == 10) and ([.data[]|.source]|all(. != null))'
as "$WH_TOKEN" POST quality/settings '{"key":"ItpNoticeDaysIran","value":12}'; no "warehouse cannot change quality settings"
as "$QA_TOKEN" POST quality/settings '{"key":"NotASetting","value":1}'; no "unknown setting rejected"
as "$QA_TOKEN" POST quality/settings '{"key":"CalibrationWarnDays","value":45}'; check "setting changed by QA" '[.data[]|select(.key=="CalibrationWarnDays")|.value][0] == 45 and [.data[]|select(.key=="CalibrationWarnDays")|.isDefault][0] == false'
as "$QA_TOKEN" POST quality/settings '{"key":"CalibrationWarnDays","value":30}'

# --- O. quality dashboard and CEO dashboard (computed, read-only)
as "$QA_TOKEN" GET quality/dashboard
check "quality dashboard: overdue CAPA, calibration, pending H points, lapsing qualifications, overdue complaints" '.success and .data.overdueCapas >= 1 and .data.calibrationOverdue >= 1 and .data.calibrationDue30 >= 1 and .data.pendingHPoints >= 1 and .data.qualificationsLapsing >= 1 and .data.complaintsOverdue >= 1'
check "quality dashboard: alerts and lists are computed" '(.data.alerts|length) >= 3 and (.data.overdueCapaList|length) >= 1 and (.data.calibrationList|length) >= 1 and (.data.pendingPoints|length) >= 1 and (.data.ncrs.notClassified + .data.ncrs.minor + .data.ncrs.major) >= 1'
as "$CEO_TOKEN" GET finance/ceo-dashboard
check "CEO dashboard carries the quality counts and alerts" '.success and .data.overdueCapas >= 1 and .data.calibrationOverdue >= 1 and .data.pendingHPoints >= 1 and .data.qualificationsLapsing >= 1 and .data.complaintsOverdue >= 1 and ([.data.alerts[]|.text]|length) >= 3'
as "$QA_TOKEN" POST inventory/documents "{\"type\":1,\"documentDate\":\"2026-10-15T00:00:00\",\"warehouseId\":$W1,\"lines\":[]}"; [ "$CODE" = "403" ] && ok "QA user cannot write warehouse documents (403)" || bad "QA user cannot write warehouse documents (403) got $CODE"
TOKEN=$ADMIN_TOKEN

# ================================================================ PROJECT CONTROL (PMO) MODULE (release 1)
# Own contracts: PCT (exact CPM / EVM / change control scenario), PCT2 (WBS generation + legacy predecessor cycles),
# PCP (late penalty). Schedule dates are fixed (calendar Mon-Fri, start Monday 2026-11-02) so every figure is exact.
TOKEN=$ADMIN_TOKEN
PC=projectcontrol
call POST Accounts/register-user '{"userName":"pm.ci","name":"pm.ci","lastName":"آزمون","email":"pm.ci@example.com","roleIds":[15],"password":"Ci#Pm-User-2026","phoneNumber":"09120000022","birthDate":"1990-01-01T00:00:00+03:30","nationalCode":"0012345622"}'
call POST Accounts/register-user '{"userName":"dcc.ci","name":"dcc.ci","lastName":"آزمون","email":"dcc.ci@example.com","roleIds":[16],"password":"Ci#Dcc-User-2026","phoneNumber":"09120000023","birthDate":"1990-01-01T00:00:00+03:30","nationalCode":"0012345623"}'
PM_TOKEN=$(login pm.ci 'Ci#Pm-User-2026'); DCC_TOKEN=$(login dcc.ci 'Ci#Dcc-User-2026')
[ -n "$PM_TOKEN" ] && [ -n "$DCC_TOKEN" ] && ok "pc: login project manager (role 15) and DCC (role 16)" || bad "pc: login project manager (role 15) and DCC (role 16)"
PMID=$(uid "$PM_TOKEN"); PLANID=$(uid "$PLAN_TOKEN")
[ -n "$PMID" ] && [ -n "$PLANID" ] && ok "pc: user ids resolved" || bad "pc: user ids resolved"

as "$PLAN_TOKEN" POST $PC/calendars '{"name":"Mon-Fri","workingWeekdays":62,"hoursPerDay":8,"useCompanyHolidays":false}'; check "pc: working calendar saved (Mon-Fri)" '.success and .data.id > 0 and .data.workingWeekdays == 62'
PCCAL=$(val .data.id)
as "$PM_TOKEN" POST $PC/calendars '{"name":"x","workingWeekdays":62,"hoursPerDay":8}'; no "pc: project manager cannot write calendars"
as "$PLAN_TOKEN" POST $PC/calendars '{"name":"bad","workingWeekdays":0,"hoursPerDay":8}'; no "pc: calendar without any working day rejected"

# --- contract, pump equipment, work order, terms
call POST Contracts '{"code":"PC-CT-01","title":"قرارداد آزمون کنترل پروژه","status":1,"employer":"کارفرمای آزمون"}'; check "pc: contract created" '.success and .data.id > 0'
PCT=$(val .data.id)
call POST Equipments '{"code":"PC-EQ-PUMP","title":"Process Pump API 610"}'; PEQ=$(val .data.id)
call POST ContractEquipments "{\"contractId\":$PCT,\"equipmentId\":$PEQ,\"count\":1}"; check "pc: contract equipment" '.success'
call POST WorkOrders "{\"contractId\":$PCT,\"equipmentId\":$PEQ,\"code\":\"PC-WO-1\",\"title\":\"Pump 1\",\"description\":\"x\",\"count\":1}"; check "pc: work order created" '.success and .data.id > 0'
PCW1=$(val .data.id)
PCTERMS="{\"contractId\":$PCT,\"currencyCode\":\"IRR\",\"valueExVat\":1000000000,\"vatPercent\":10,\"advancePercent\":10,\"retentionPercent\":5,\"latePenaltyPercentPerWeek\":1,\"penaltyCapPercent\":10,\"graceDays\":0,\"calendarId\":$PCCAL,\"dataDateWeekday\":3,\"scheduleStartDate\":\"2026-11-02T00:00:00\",\"accrualMode\":1,\"clientReviewDays\":14,\"managementReserve\":0,\"autoSnapshot\":true,\"clientReportShowsIndices\":false}"
as "$WH_TOKEN" POST $PC/terms "$PCTERMS"; no "pc: warehouse cannot write contract terms"
as "$PLAN_TOKEN" POST $PC/terms "$PCTERMS"; check "pc: contract terms saved (value, penalty, calendar, start)" '.success and .data.valueExVat == 1000000000 and .data.dataDateWeekday == 3 and .data.clientReportShowsIndices == false'
as "$PLAN_TOKEN" POST $PC/terms "$(echo "$PCTERMS" | jq -c '.vatPercent = 101')"; no "pc: percentage above 100 rejected"

# --- initiation (WI-PL-009 sections 3-8) and the 9-character project code
as "$PLAN_TOKEN" POST $PC/initiation/start/$PCT; check "pc: initiation started: 10 steps" '.success and (.data.steps|length) == 10'
as "$PLAN_TOKEN" POST $PC/terms/generate-code "{\"contractId\":$PCT,\"category\":1}"; check "pc: project code generated (9 characters, MA)" '.success and (.data.projectCode|length) == 9 and (.data.projectCode|contains("MA"))'
as "$PLAN_TOKEN" POST $PC/terms/generate-code "{\"contractId\":$PCT,\"category\":1}"; no "pc: project code is issued once"
as "$PLAN_TOKEN" POST $PC/initiation/action "{\"contractId\":$PCT,\"step\":3,\"note\":\"units notified\"}"; check "pc: units notified step done" '.success'
as "$PLAN_TOKEN" POST $PC/initiation/action "{\"contractId\":$PCT,\"step\":4,\"projectManagerUserId\":$PMID}"; no "pc: planning cannot select the project manager"
as "$CEO_TOKEN" POST $PC/initiation/action "{\"contractId\":$PCT,\"step\":4,\"projectManagerUserId\":$PMID}"; check "pc: CEO selects the project manager" '.success'
call GET "$PC/terms/$PCT"; check "pc: terms show the manager and the code" ".success and .data.projectManagerUserId == $PMID and .data.projectCode != null"
as "$PLAN_TOKEN" GET "$PC/claims?ContractId=$PCT"; check "pc: claims file opened with the initiation (read)" '.success'

# --- equipment code and MDR/MDL from the template
as "$PLAN_TOKEN" POST $PC/work-order-info "{\"workOrderId\":$PCW1,\"contractId\":$PCT,\"typeCode\":\"PU\",\"sizeCode\":\"100\",\"optionCode\":\"25\"}"; check "pc: equipment project code PU1002501" '.success and .data.equipmentProjectCode == "PU1002501"'
as "$QC_TOKEN" POST $PC/mdr/draft "{\"contractId\":$PCT,\"workOrderId\":$PCW1}"; no "pc: QC cannot draft the MDR"
as "$DCC_TOKEN" POST $PC/mdr/draft "{\"contractId\":$PCT,\"workOrderId\":$PCW1}"; check "pc: MDR drafted from the pump template" '.success and .data.linesDrafted == 12'
as "$DCC_TOKEN" POST $PC/mdr/draft "{\"contractId\":$PCT,\"workOrderId\":$PCW1}"; no "pc: the MDR is drafted once per work order"
as "$DCC_TOKEN" GET "$PC/mdr?WorkOrderId=$PCW1&PageSize=0"
check "pc: MDR has 12 lines, 11 on the client list (MDL subset), each with a standard source" '(.data.data|length) == 12 and ([.data.data[]|select(.clientSubmittal)]|length) == 11 and ([.data.data[]|select(.standardSource != null)]|length) == 12'
check "pc: template standard sources are marked to be verified" '[.data.data[]|select((.standardSource // "")|contains("to be verified"))]|length >= 1'
CL_IDS=$(jq -c '[.data.data[]|select(.clientSubmittal)|.id]' "$B"); INT_ID=$(jq -r '[.data.data[]|select(.clientSubmittal|not)|.id][0]' "$B")

# --- supply chain for two activities: one purchase order with two lines (60 and 40 plates)
as "$WH_TOKEN" POST procurement/requests "{\"source\":2,\"kind\":1,\"requestDate\":\"2026-10-22T00:00:00\",\"description\":\"ورق پروژه کنترل\",\"submit\":true,\"lines\":[{\"itemId\":$PLATE,\"quantity\":60,\"estimatedUnitPrice\":400000,\"technicalRequirements\":\"ASTM A516-70 با گواهی 3.1\"},{\"itemId\":$PLATE,\"quantity\":40,\"estimatedUnitPrice\":400000,\"technicalRequirements\":\"ASTM A516-70 با گواهی 3.1\"}]}"
check "pc: purchase request with two lines submitted (40M rial)" '.success and .data.status == 1 and (.data.lines|length) == 2 and .data.estimatedAmount == 40000000'
PCPR=$(val .data.id); PRL1=$(val '.data.lines[]|select(.quantity==60)|.id'); PRL2=$(val '.data.lines[]|select(.quantity==40)|.id')
as "$PLAN_TOKEN" POST procurement/requests/action "{\"id\":$PCPR,\"action\":\"approve-planning\"}"
as "$CEO_TOKEN" POST procurement/requests/action "{\"id\":$PCPR,\"action\":\"approve-management\"}"; check "pc: purchase request approved" '.success and .data.status == 2'
PQA="{\"providerId\":$PA,\"quoteNumber\":\"PC-Q-A\",\"vatPercent\":10,\"deliveryDays\":20,\"technicallyAccepted\":true,\"lines\":[{\"requestLineId\":$PRL1,\"quantity\":60,\"unitPrice\":390000},{\"requestLineId\":$PRL2,\"quantity\":40,\"unitPrice\":390000}]}"
PQB="{\"providerId\":$PB,\"quoteNumber\":\"PC-Q-B\",\"vatPercent\":10,\"technicallyAccepted\":true,\"lines\":[{\"requestLineId\":$PRL1,\"quantity\":60,\"unitPrice\":380000},{\"requestLineId\":$PRL2,\"quantity\":40,\"unitPrice\":380000}]}"
as "$COM_TOKEN" POST procurement/inquiries "{\"purchaseRequestId\":$PCPR,\"inquiryDate\":\"2026-10-22T00:00:00\",\"quotes\":[$PQA,$PQB]}"; check "pc: inquiry with two quotes" '.success and (.data.quotes|length) == 2'
PCINQ=$(val .data.id); PQAID=$(val ".data.quotes[]|select(.providerId == $PA)|.id")
as "$COM_TOKEN" POST procurement/inquiries/select "{\"inquiryId\":$PCINQ,\"quoteId\":$PQAID,\"reason\":\"گرید A و تحویل 20 روزه\"}"; check "pc: quote selected" '.success and .data.status == 2'
as "$COM_TOKEN" POST "procurement/orders/from-inquiry/$PCINQ"; check "pc: purchase order draft with two lines" '.success and (.data.lines|length) == 2'
PCPO=$(val .data.id); POL1=$(val '.data.lines[]|select(.quantity==60)|.id'); POL2=$(val '.data.lines[]|select(.quantity==40)|.id')
as "$ADMIN_TOKEN" POST procurement/orders/action "{\"id\":$PCPO,\"action\":\"approve\"}"
as "$COM_TOKEN" POST procurement/orders/action "{\"id\":$PCPO,\"action\":\"release\"}"; check "pc: purchase order released" '.success and .data.status == 2'

# --- four activities (weights 0: they count equally in the physical percent), budgets 1,000,000,000 rial
actp() { as "$PLAN_TOKEN" POST $PC/activities "$1"; }
actp "{\"contractId\":$PCT,\"code\":\"PC-A\",\"title\":\"صدور مدارک مهندسی\",\"sourceType\":4,\"sourceId\":0,\"workOrderId\":$PCW1,\"durationDays\":3,\"responsibleUnit\":\"ENGINEERING\",\"progressMethod\":1,\"weight\":0,\"budgetAtCompletion\":300000000,\"phase\":1}"
check "pc: activity A (engineering issue, 3 days)" '.success and .data.id > 0 and .data.responsibleUnit == "ENGINEERING"'; PCA=$(val .data.id)
actp "{\"contractId\":$PCT,\"code\":\"PC-B\",\"title\":\"تامین ورق الف\",\"sourceType\":6,\"durationDays\":2,\"responsibleUnit\":\"PLANNING\",\"progressMethod\":2,\"weight\":0,\"budgetAtCompletion\":200000000,\"phase\":2,\"purchaseOrderLineId\":$POL1}"
check "pc: activity B (supply receipt, 60 plates)" '.success and .data.id > 0'; PCB=$(val .data.id)
actp "{\"contractId\":$PCT,\"code\":\"PC-C\",\"title\":\"ساخت\",\"sourceType\":7,\"durationDays\":4,\"responsibleUnit\":\"CONSTRUCTION\",\"progressMethod\":0,\"weight\":0,\"budgetAtCompletion\":400000000,\"phase\":3}"
check "pc: activity C (manual progress)" '.success and .data.id > 0'; PCC=$(val .data.id)
actp "{\"contractId\":$PCT,\"code\":\"PC-D\",\"title\":\"تامین ورق ب\",\"sourceType\":6,\"durationDays\":1,\"responsibleUnit\":\"PLANNING\",\"progressMethod\":2,\"weight\":0,\"budgetAtCompletion\":100000000,\"phase\":2,\"purchaseOrderLineId\":$POL2}"
check "pc: activity D (supply receipt, 40 plates)" '.success and .data.id > 0'; PCD=$(val .data.id)
actp "{\"contractId\":$PCT,\"code\":\"PC-X\",\"title\":\"x\",\"sourceType\":6,\"durationDays\":1,\"progressMethod\":2}"; no "pc: supply activity without an order line rejected"
as "$WH_TOKEN" POST $PC/activities "{\"contractId\":$PCT,\"code\":\"PC-Y\",\"title\":\"y\",\"durationDays\":1}"; no "pc: warehouse cannot write activities"

pcdep() { as "$PLAN_TOKEN" POST $PC/dependencies "{\"contractId\":$PCT,\"predecessorId\":$1,\"successorId\":$2,\"type\":1,\"lagDays\":0}"; }
pcdep $PCA $PCB; check "pc: dependency A to B" '.success'
pcdep $PCA $PCC; pcdep $PCB $PCD; check "pc: dependency B to D" '.success'
pcdep $PCC $PCD; check "pc: dependency C to D" '.success'
pcdep $PCD $PCA; no "pc: cycle D to A rejected on save"
pcdep $PCB $PCB; no "pc: self dependency rejected"
pcdep $PCA $PCB; no "pc: duplicate dependency rejected"

# --- CPM: exact dates, float and the critical path
as "$PLAN_TOKEN" POST $PC/cpm/run "{\"contractId\":$PCT,\"forecast\":false,\"mirror\":true}"
check "pc: CPM finishes on 2026-11-11 after 8 working days, no cycle" '.success and .data.hasCycle == false and .data.projectFinishOffset == 8 and (.data.projectFinish|startswith("2026-11-11")) and (.data.projectStart|startswith("2026-11-02"))'
check "pc: CPM early dates of A, C and D" '([.data.items[]|select(.code=="PC-A")][0].earlyFinish|startswith("2026-11-04")) and ([.data.items[]|select(.code=="PC-C")][0].earlyFinish|startswith("2026-11-10")) and ([.data.items[]|select(.code=="PC-D")][0].earlyStart|startswith("2026-11-11"))'
check "pc: activity B has total float 2 (2026-11-05..06)" '[.data.items[]|select(.code=="PC-B")][0]|(.totalFloat == 2 and (.earlyStart|startswith("2026-11-05")) and (.earlyFinish|startswith("2026-11-06")) and .critical == false)'
check "pc: critical path is A, C, D" '.data.criticalPath == ["PC-A","PC-C","PC-D"]'
as "$PM_TOKEN" GET "$PC/activities?ContractId=$PCT&OnlyCritical=true&PageSize=0"; check "pc: critical activity filter" '(.data.data|length) == 3'
as "$PLAN_TOKEN" GET "$PC/executive-plan?ContractId=$PCT&From=2026-11-02&Days=14&PageSize=0"; check "pc: executive plan (look-ahead) lists the activities" '.success'

# --- baseline 0: signatures of the unit managers, approval by the PMO deputy
as "$PLAN_TOKEN" POST $PC/baselines "{\"contractId\":$PCT,\"reason\":\"مبنای اولیه\"}"
check "pc: baseline 0 drafted: 4 activities, BAC 1,000,000,000, 3 unit signatures" '.success and .data.number == 0 and .data.status == 0 and .data.activityCount == 4 and .data.bac == 1000000000 and (.data.signatures|length) == 3'
PCB0=$(val .data.id)
call POST $PC/baselines/action "{\"id\":$PCB0,\"action\":\"approve\"}"; no "pc: baseline cannot be approved before the managers sign"
as "$PM_TOKEN" POST $PC/baselines/action "{\"id\":$PCB0,\"action\":\"sign\"}"; no "pc: project manager is not a signing unit"
as "$PROD_TOKEN" POST $PC/baselines/action "{\"id\":$PCB0,\"action\":\"sign\"}"; check "pc: construction manager signs" '.success and ([.data.signatures[]|select(.unitRole=="CONSTRUCTION")][0].signedAt != null)'
as "$ENG_TOKEN" POST $PC/baselines/action "{\"id\":$PCB0,\"action\":\"sign\"}"; check "pc: engineering manager signs" '.success and .data.status == 0'
as "$PLAN_TOKEN" POST $PC/baselines/action "{\"id\":$PCB0,\"action\":\"sign\"}"; check "pc: planning manager signs -> managers signed" '.success and .data.status == 1'
as "$PM_TOKEN" POST $PC/baselines/action "{\"id\":$PCB0,\"action\":\"approve\"}"; no "pc: only the PMO deputy approves a baseline"
call POST $PC/baselines/action "{\"id\":$PCB0,\"action\":\"approve\"}"; check "pc: baseline 0 approved and current" '.success and .data.status == 2 and .data.isCurrent == true and .data.bac == 1000000000'
call GET "$PC/terms/$PCT"; check "pc: terms point to the current baseline" ".data.currentBaselineId == $PCB0"

# --- engineering progress from the MDR (60/80/100) and the transmittal response codes
as "$DCC_TOKEN" POST $PC/mdr/action "{\"id\":$INT_ID,\"action\":\"first-issue\",\"revision\":\"A\"}"; check "pc: internal document first issue = 60 percent" '.success and .data.issueState == 1 and .data.progressPercent == 60'
as "$DCC_TOKEN" POST $PC/mdr/action "{\"id\":$INT_ID,\"action\":\"internal-approve\"}"; no "pc: DCC cannot approve internally (engineering manager only)"
as "$ENG_TOKEN" POST $PC/mdr/action "{\"id\":$INT_ID,\"action\":\"internal-approve\"}"; check "pc: engineering manager approves the internal document = 100 percent" '.success and .data.issueState == 3 and .data.progressPercent == 100'
PCT_BAD=$(jq -nc --argjson c $PCT --argjson i $INT_ID '{contractId:$c,direction:1,transmittalDate:"2026-10-01T00:00:00",lines:[{deliverableId:$i,purpose:1}]}')
as "$DCC_TOKEN" POST $PC/transmittals "$PCT_BAD"; no "pc: outgoing transmittal with an internal-only document rejected"
PCT1=$(jq -nc --argjson c $PCT --argjson ids "$CL_IDS" '{contractId:$c,direction:1,transmittalDate:"2026-10-01T00:00:00",reference:"Letter 1",lines:($ids|map({deliverableId:.,purpose:1}))}')
as "$DCC_TOKEN" POST $PC/transmittals "$PCT1"
check "pc: transmittal T1 of the 11 client documents, response due 14 days later" '.success and (.data.number|length) > 0 and (.data.lines|length) == 11 and (.data.responseDue|startswith("2026-10-15"))'
TR1=$(val .data.id); TL1=$(jq -r '.data.lines[0].id' "$B"); TL2=$(jq -r '.data.lines[1].id' "$B"); TL3=$(jq -r '.data.lines[2].id' "$B"); TL4=$(jq -r '.data.lines[3].id' "$B")
as "$DCC_TOKEN" GET "$PC/transmittals?OnlyOverdue=true&PageSize=0"; check "pc: unanswered transmittal past its due date is overdue" "[.data.data[]|select(.id == $TR1)]|length == 1"
as "$DCC_TOKEN" POST $PC/transmittals/response "{\"transmittalLineId\":$TL1,\"responseCode\":\"Z\",\"responseDate\":\"2026-10-10T00:00:00\"}"; no "pc: unknown response code rejected"
as "$DCC_TOKEN" POST $PC/transmittals/response "{\"transmittalLineId\":$TL1,\"responseCode\":\"A\",\"responseDate\":\"2026-10-10T00:00:00\"}"; check "pc: client code A approves the document" '.success'
as "$DCC_TOKEN" POST $PC/transmittals/response "{\"transmittalLineId\":$TL2,\"responseCode\":\"A\",\"responseDate\":\"2026-10-10T00:00:00\"}"
as "$DCC_TOKEN" POST $PC/transmittals/response "{\"transmittalLineId\":$TL3,\"responseCode\":\"B\",\"responseDate\":\"2026-10-10T00:00:00\"}"; check "pc: client code B (approved with comments) also approves" '.success'
as "$DCC_TOKEN" POST $PC/transmittals/response "{\"transmittalLineId\":$TL4,\"responseCode\":\"C\",\"responseDate\":\"2026-10-10T00:00:00\"}"; check "pc: client code C leaves the document unapproved" '.success'
PCT2L=$(jq -nc --argjson c $PCT --argjson ids "$CL_IDS" '{contractId:$c,direction:1,transmittalDate:"2026-10-12T00:00:00",reference:"Letter 2",lines:($ids[3:7]|map({deliverableId:.,purpose:1}))}')
as "$DCC_TOKEN" POST $PC/transmittals "$PCT2L"; check "pc: second transmittal re-sends four documents" '.success and (.data.lines|length) == 4'
as "$DCC_TOKEN" GET "$PC/mdr?WorkOrderId=$PCW1&PageSize=0"
check "pc: MDR states: 3+1 approved, 4 at second issue, 4 at first issue" '([.data.data[]|select(.issueState==3)]|length) == 4 and ([.data.data[]|select(.issueState==2)]|length) == 4 and ([.data.data[]|select(.issueState==1)]|length) == 4'
as "$PLAN_TOKEN" POST $PC/cpm/run "{\"contractId\":$PCT,\"forecast\":true,\"mirror\":false}"
as "$PLAN_TOKEN" GET "$PC/activities?ContractId=$PCT&PageSize=0"
check "pc: engineering activity A is exactly 80 percent (average over 12 documents)" '[.data.data[]|select(.code=="PC-A")][0].percentCache == 80'

# --- supply progress from the receipt, the incoming inspection and the NCR
call POST inventory/documents "{\"type\":1,\"documentDate\":\"2026-10-22T00:00:00\",\"warehouseId\":$WQ,\"destinationWarehouseId\":$W1,\"purchaseOrderId\":$PCPO,\"referenceNumber\":\"PC-INV-1\",\"lines\":[{\"itemId\":$PLATE,\"quantity\":60,\"heatNumber\":\"HT-PC1\",\"purchaseOrderLineId\":$POL1},{\"itemId\":$PLATE,\"quantity\":40,\"heatNumber\":\"HT-PC2\",\"purchaseOrderLineId\":$POL2}]}"
check "pc: plate receipt against the order drafted" '.success and (.data.lines|length) == 2'
PCRCV=$(val .data.id); call POST "inventory/documents/$PCRCV/confirm"; check "pc: plate receipt into quarantine confirmed" '.success and .data.status == 1'
as "$PLAN_TOKEN" GET "$PC/activities?ContractId=$PCT&PageSize=0"
as "$PLAN_TOKEN" POST $PC/cpm/run "{\"contractId\":$PCT,\"forecast\":true,\"mirror\":false}"
as "$PLAN_TOKEN" GET "$PC/activities?ContractId=$PCT&PageSize=0"
check "pc: received plates in quarantine = temporary receipt = 70 percent for B and D" '([.data.data[]|select(.code=="PC-B")][0].percentCache == 70) and ([.data.data[]|select(.code=="PC-D")][0].percentCache == 70)'
as "$PLAN_TOKEN" POST $PC/cpm/run "{\"contractId\":$PCT,\"forecast\":true,\"mirror\":false}"

# --- cost entries (actual cost 600,000,000 by 2026-11-06) and manual progress that needs another person's approval
pccost() { as "$FIN_TOKEN" POST $PC/costs "{\"contractId\":$PCT,\"kind\":$1,\"entryDate\":\"$2T00:00:00\",\"amount\":$3,\"description\":\"$4\"}"; }
pccost 1 2026-11-03 400000000 "مواد اولیه"; check "pc: cost entry 400M recorded against the contract" '.success and .data.contractId > 0 and .data.amount == 400000000'
pccost 3 2026-11-05 200000000 "دستمزد"; check "pc: cost entry 200M recorded" '.success'
as "$PLAN_TOKEN" POST $PC/costs "{\"contractId\":$PCT,\"kind\":1,\"entryDate\":\"2026-11-03T00:00:00\",\"amount\":1,\"description\":\"x\"}"; no "pc: planning cannot post costs (finance only)"
as "$PLAN_TOKEN" POST $PC/manual-progress "{\"activityId\":$PCC,\"percent\":50,\"comment\":\"نیمی از ساخت انجام شد\"}"; check "pc: manual progress proposed (pending)" '.success and .data.state == 0 and .data.percent == 50'
PCMP=$(val .data.id)
as "$PLAN_TOKEN" POST $PC/manual-progress "{\"activityId\":$PCA,\"percent\":50}"; no "pc: manual progress on a system-measured activity rejected"
as "$PLAN_TOKEN" GET "$PC/evm/$PCT?asOf=2026-11-06"
check "pc: EVM stage 1: PV 700M, EV 450M (A 80%, B 70%, D 70%, C unapproved excluded), AC 600M" '.success and .data.fromBaseline == true and .data.bac == 1000000000 and .data.pv == 700000000 and .data.ev == 450000000 and .data.ac == 600000000 and .data.cv == -150000000 and .data.sv == -250000000'
check "pc: EVM stage 1 indices: CPI 0.75, SPI 0.6429, both red, corrective action required" '.data.cpi == 0.75 and .data.spi == 0.6429 and .data.cpiStatus == "red" and .data.spiStatus == "red" and .data.correctiveActionRequired == true'
as "$PLAN_TOKEN" POST $PC/manual-progress/decide "{\"id\":$PCMP,\"approve\":true}"; no "pc: the proposer cannot approve their own manual progress"
as "$PM_TOKEN" POST $PC/manual-progress/decide "{\"id\":$PCMP,\"approve\":true}"; no "pc: project manager is not the approver of manual progress"
call POST $PC/manual-progress/decide "{\"id\":$PCMP,\"approve\":true,\"note\":\"تایید\"}"; check "pc: manual progress approved by a different person" '.success and .data.state == 1'
as "$PLAN_TOKEN" GET "$PC/evm/$PCT?asOf=2026-11-06"; check "pc: EVM after approving C at 50 percent: EV 650M" '.data.ev == 650000000 and .data.cpi == 1.0833'

# --- weekly snapshot (immutable) and the scheduled run
PCSNAPDATE=$(dd -14)
as "$PLAN_TOKEN" POST $PC/snapshots "{\"contractId\":$PCT,\"dataDate\":\"${PCSNAPDATE}T00:00:00\"}"
check "pc: weekly snapshot taken with 4 lines, BAC and hash" '.success and (.data.lines|length) == 4 and .data.bac == 1000000000 and (.data.hash|length) > 10'
PCSNAP=$(val .data.id); PCSNAPHASH=$(val .data.hash)
as "$PLAN_TOKEN" POST $PC/snapshots "{\"contractId\":$PCT,\"dataDate\":\"${PCSNAPDATE}T00:00:00\"}"; no "pc: second snapshot for the same data date rejected"
as "$PLAN_TOKEN" POST $PC/snapshots "{\"contractId\":$PCT,\"dataDate\":\"2030-01-02T00:00:00\"}"; no "pc: snapshot with a future data date rejected"
as "$PLAN_TOKEN" GET "$PC/snapshots/$PCSNAP"; check "pc: snapshot unchanged after later progress (hash identical)" ".data.hash == \"$PCSNAPHASH\""
as "$PLAN_TOKEN" POST $PC/snapshots/run-due '{"asOf":"2026-11-11T12:00:00"}'; check "pc: scheduled snapshot run takes the Wednesday snapshot" '.success and .data.snapshotsTaken >= 1'
as "$PLAN_TOKEN" POST $PC/snapshots/run-due '{"asOf":"2026-11-11T12:00:00"}'; check "pc: second run takes nothing (one per data date)" '.success and .data.snapshotsTaken == 0'
as "$PM_TOKEN" POST $PC/snapshots "{\"contractId\":$PCT}"; no "pc: project manager cannot take snapshots (planning)"

# --- QC accepts plate A; plate B is rejected: NCR classified as rework caps D at 80 percent
as "$QC_TOKEN" GET "inventory/quality/inspections?Status=0&PageSize=0"
PCQI1=$(jq -r ".data.data[]|select(.receiptDocumentId==$PCRCV and .quantity==60)|.id" "$B"); PCQI2=$(jq -r ".data.data[]|select(.receiptDocumentId==$PCRCV and .quantity==40)|.id" "$B")
[ -n "$PCQI1" ] && [ -n "$PCQI2" ] && ok "pc: two incoming inspections waiting for the receipt" || bad "pc: two incoming inspections waiting for the receipt"
as "$QC_TOKEN" POST inventory/quality/inspections "{\"id\":$PCQI1,\"acceptedQuantity\":60,\"rejectedQuantity\":0,\"reportNumber\":\"QC-PC-1\"}"; check "pc: plate A accepted (60)" '.success and .data.status == 1'
as "$QC_TOKEN" POST inventory/quality/inspections "{\"id\":$PCQI2,\"acceptedQuantity\":0,\"rejectedQuantity\":40,\"reportNumber\":\"QC-PC-2\",\"rejectReason\":\"ترک سطحی در لبه ورق\"}"; check "pc: plate B rejected (40) -> NCR" '.success and (.data.ncrNumber|startswith("NCR-"))'
PCNCR=$(val .data.ncrId)
as "$PLAN_TOKEN" POST $PC/cpm/run "{\"contractId\":$PCT,\"forecast\":true,\"mirror\":false}"
as "$PLAN_TOKEN" GET "$PC/activities?ContractId=$PCT&PageSize=0"
check "pc: B is 100 after QC accept; D with an unclassified NCR stays at 70" '([.data.data[]|select(.code=="PC-B")][0].percentCache == 100) and ([.data.data[]|select(.code=="PC-D")][0].percentCache == 70)'
as "$QC_TOKEN" POST inventory/quality/ncr "{\"id\":$PCNCR,\"severity\":1,\"disposition\":2,\"source\":1,\"causeCategory\":2,\"rootCause\":\"ورق معیوب از تامین‌کننده\"}"; check "pc: NCR classified: minor, rework (still open)" '.success and .data.disposition == 2'
as "$PLAN_TOKEN" POST $PC/cpm/run "{\"contractId\":$PCT,\"forecast\":true,\"mirror\":false}"
as "$PLAN_TOKEN" GET "$PC/activities?ContractId=$PCT&PageSize=0"
check "pc: D is capped at 80 percent while the rework NCR is open" '[.data.data[]|select(.code=="PC-D")][0].percentCache == 80'
as "$PLAN_TOKEN" GET "$PC/evm/$PCT?asOf=2026-11-06"
check "pc: EVM stage 2 exact: PV 700M, EV 720M, AC 600M, CV 120M, SV 20M" '.data.pv == 700000000 and .data.ev == 720000000 and .data.ac == 600000000 and .data.cv == 120000000 and .data.sv == 20000000'
check "pc: EVM stage 2 indices: CPI 1.2, SPI 1.0286, EAC 833,333,333.33" '.data.cpi == 1.2 and .data.spi == 1.0286 and .data.eacTypical == 833333333.33 and .data.cpiStatus == "green" and .data.spiStatus == "green" and .data.correctiveActionRequired == false'
as "$PLAN_TOKEN" GET "$PC/progress/$PCT"; check "pc: physical progress is the average of the four activities" '.success and .data.physicalPercent == 77.5'
as "$PLAN_TOKEN" GET "$PC/cash-flow/$PCT"; check "pc: cash flow curve (S curve) is returned" '.success'

# --- change request: scenario, impact on the critical path, board, authority, new baseline
PCCH="{\"contractId\":$PCT,\"origin\":2,\"type\":2,\"managerAgreement\":\"موافقت مدیر برنامه‌ریزی\",\"description\":\"افزایش زمان ساخت به دلیل تعمیر قالب\",\"reason\":\"عیب قالب\",\"requestedDaysDelta\":2,\"requestedPriceDelta\":100000000,\"scenario\":[{\"activityId\":$PCC,\"durationDelta\":2,\"budgetDelta\":100000000}]}"
as "$SEC_TOKEN" POST $PC/changes "$PCCH"; no "pc: security user is not a requester of changes"
as "$PLAN_TOKEN" POST $PC/changes "$(echo "$PCCH" | jq -c 'del(.managerAgreement)')"; no "pc: internal request needs the requester manager's agreement"
as "$PLAN_TOKEN" POST $PC/changes "$PCCH"; check "pc: change request drafted with its scenario" '.success and .data.status == 0 and (.data.scenario|length) == 1 and (.data.number|length) > 0'
PCCHID=$(val .data.id)
as "$PLAN_TOKEN" POST $PC/changes/action "{\"id\":$PCCHID,\"action\":\"register\"}"
check "pc: registered; board = project manager, planning, construction, commercial; due date set" '.success and .data.status == 1 and ([.data.board[].role]|sort) == ["COMMERCIAL","CONSTRUCTION","PLANNING","PROJECTMANAGER"] and .data.dueDate != null'
call POST $PC/changes/action "{\"id\":$PCCHID,\"action\":\"decide\",\"decision\":1,\"note\":\"x\"}"; no "pc: decision before the impact analysis rejected"
as "$PLAN_TOKEN" POST $PC/changes/action "{\"id\":$PCCHID,\"action\":\"analyze\"}"
check "pc: impact analysis: +2 days, +100M, on the critical path, C and D move" '.success and .data.status == 2 and .data.impactDeltaDays == 2 and .data.impactDeltaCost == 100000000 and .data.impactOnCriticalPath == true and ([.data.impactActivities[].code]|index("PC-C") != null) and ([.data.impactActivities[].code]|index("PC-D") != null)'
as "$PLAN_TOKEN" POST $PC/changes/action "{\"id\":$PCCHID,\"action\":\"recommend\",\"note\":\"تایید با رعایت مهلت\"}"; no "pc: planning cannot give the project manager's recommendation"
as "$PM_TOKEN" POST $PC/changes/action "{\"id\":$PCCHID,\"action\":\"recommend\",\"note\":\"تایید با رعایت مهلت\"}"; check "pc: project manager recommends" '.success and .data.recommendation != null'
call POST $PC/changes/action "{\"id\":$PCCHID,\"action\":\"decide\",\"decision\":1,\"note\":\"x\"}"; no "pc: decision before the board has voted rejected"
as "$PM_TOKEN" POST $PC/changes/action "{\"id\":$PCCHID,\"action\":\"vote\",\"vote\":1}"; check "pc: project manager votes" '.success'
as "$PLAN_TOKEN" POST $PC/changes/action "{\"id\":$PCCHID,\"action\":\"vote\",\"vote\":1}"
as "$PROD_TOKEN" POST $PC/changes/action "{\"id\":$PCCHID,\"action\":\"vote\",\"vote\":1}"
as "$COM_TOKEN" POST $PC/changes/action "{\"id\":$PCCHID,\"action\":\"vote\",\"vote\":1}"; check "pc: all four board members voted" '.success and ([.data.board[]|select(.vote == 1)]|length) == 4'
as "$PM_TOKEN" POST $PC/changes/action "{\"id\":$PCCHID,\"action\":\"decide\",\"decision\":1,\"note\":\"x\"}"; no "pc: project manager recommends only, cannot decide (authority table)"
call POST $PC/changes/action "{\"id\":$PCCHID,\"action\":\"decide\",\"decision\":1,\"note\":\"تایید معاون PMO\"}"
check "pc: PMO deputy approves within authority -> implementing, baseline attached" '.success and .data.status == 4 and .data.decision == 1 and .data.baselineId != null'
call GET "$PC/baselines?ContractId=$PCT&PageSize=0"
check "pc: baseline 1 approved and current (BAC 1.1bn); baseline 0 superseded" '([.data.data[]|select(.number==1)][0]|(.status == 2 and .isCurrent == true and .bac == 1100000000 and .changeRequestId != null)) and ([.data.data[]|select(.number==0)][0].status == 3)'
as "$PLAN_TOKEN" GET "$PC/activities?ContractId=$PCT&PageSize=0"
check "pc: plan re-run: C now 6 days ends 2026-11-12, D 2026-11-13, budget of C 500M" '([.data.data[]|select(.code=="PC-C")][0]|(.durationDays == 6 and (.earlyFinish|startswith("2026-11-12")) and .budgetAtCompletion == 500000000)) and ([.data.data[]|select(.code=="PC-D")][0].earlyFinish|startswith("2026-11-13"))'
as "$PM_TOKEN" POST $PC/changes/action "{\"id\":$PCCHID,\"action\":\"close\"}"; no "pc: only planning closes a change"
as "$PLAN_TOKEN" POST $PC/changes/action "{\"id\":$PCCHID,\"action\":\"close\"}"; check "pc: change closed" '.success and .data.status == 5'
as "$PLAN_TOKEN" GET "$PC/changes?ContractId=$PCT"; check "pc: change list is paged" '.success and (.data.data|length) >= 1'
call GET "$PC/approval-authorities"; check "pc: approval authority table seeded (PMO up to 5bn rial and 30 days; PM recommends only)" '.success and ([.data[]|select(.role=="PMO")][0]|(.canDecide == true and .maxAmount == 5000000000 and .maxDelayDays == 30)) and ([.data[]|select(.role=="PROJECTMANAGER")][0].canDecide == false)'
as "$CEO_TOKEN" GET "$PC/approval-authorities"; check "pc: the CEO also reads the table" '.success'

# --- commercial: payment milestones, guarantee alerts, late penalty (contract PCP)
as "$COM_TOKEN" POST $PC/milestones "{\"contractId\":$QCT,\"title\":\"پرداخت پس از ترخیص کیفی\",\"kind\":3,\"trigger\":4,\"workOrderId\":$WO1,\"amount\":50000000}"; check "pc: payment milestone on the release note trigger" '.success and .data.status == 0 and .data.seq == 1'
PCMS1=$(val .data.id)
as "$COM_TOKEN" POST $PC/milestones "{\"contractId\":$QCT,\"title\":\"پرداخت تاریخ‌دار\",\"kind\":9,\"trigger\":1,\"plannedDate\":\"$(dd -1)T00:00:00\",\"amount\":10000000}"; check "pc: payment milestone on a date" '.success'
as "$COM_TOKEN" POST $PC/milestones "{\"contractId\":$QCT,\"title\":\"پرداخت پیشرفت\",\"kind\":2,\"trigger\":2,\"triggerPercent\":100,\"amount\":10000000}"
as "$COM_TOKEN" POST $PC/milestones/evaluate/$QCT; check "pc: released note and a past date make two milestones due; progress one stays planned" '.success and .data.becameDue == 2 and ([.data.milestones[]|select(.status == 1)]|length) == 2'
as "$QC_TOKEN" POST $PC/milestones "{\"contractId\":$QCT,\"title\":\"x\",\"kind\":9,\"trigger\":1,\"plannedDate\":\"$(dd 1)T00:00:00\",\"amount\":1}"; no "pc: QC cannot define payment milestones"
as "$COM_TOKEN" POST $PC/milestones/action "{\"id\":$PCMS1,\"action\":\"paid\"}"; no "pc: a milestone that is only due cannot be marked paid"

call POST ContractGuaranty "{\"title\":\"ضمانت‌نامه حسن انجام کار\",\"bank\":\"ملی\",\"no\":\"PC-G-1\",\"price\":\"500,000,000\",\"regDate\":\"$(dd -300)T00:00:00\",\"dueDate\":\"$(dd 10)T00:00:00\",\"contractEntityId\":$PCT}"; check "pc: legacy contract guaranty created (text amount)" '.success'
as "$COM_TOKEN" POST $PC/guarantees/parse; check "pc: legacy guarantees parsed into typed side data" '.success and .data.created >= 1'
as "$COM_TOKEN" GET "$PC/guarantees?ContractId=$PCT&PageSize=0"; check "pc: guarantee amount parsed to 500,000,000, expiry in 10 days" '.success and ([.data.data[]|select(.no=="PC-G-1")][0]|(.amountDecimal == 500000000 and .parseFailed == false and .daysToExpiry == 10))'
as "$COM_TOKEN" GET "$PC/guarantees/alerts?contractId=$PCT"; check "pc: guarantee alert listed at the 14-day level" '(if type == "array" then . else .data end) | ([.[]|select(.no=="PC-G-1")][0]|(.alertLevel == 14 and .daysToExpiry == 10))'
as "$QC_TOKEN" POST $PC/guarantees/parse; no "pc: QC cannot parse guarantees"

call POST Contracts '{"code":"PC-CT-P","title":"قرارداد آزمون جریمه","status":1,"employer":"کارفرمای آزمون"}'; PCP=$(val .data.id)
as "$PLAN_TOKEN" POST $PC/terms "{\"contractId\":$PCP,\"currencyCode\":\"IRR\",\"valueExVat\":1000000000,\"latePenaltyPercentPerWeek\":1,\"penaltyCapPercent\":10,\"graceDays\":0,\"contractDeliveryDate\":\"$(dd -14)T00:00:00\",\"dataDateWeekday\":3,\"clientReviewDays\":14,\"autoSnapshot\":false}"; check "pc: penalty contract terms saved" '.success'
as "$COM_TOKEN" POST $PC/penalties/compute/$PCP; check "pc: late penalty 14 days x 1 percent per week of 1bn = 20,000,000" '.success and (.data|length) == 1 and .data[0].lateDays == 14 and .data[0].amount == 20000000 and .data[0].baseAmount == 1000000000'
PCPEN=$(val '.data[0].id')
as "$COM_TOKEN" POST $PC/penalties/decide "{\"id\":$PCPEN,\"status\":1,\"reason\":\"بخشودگی\"}"; no "pc: only the CEO waives a penalty"
as "$CEO_TOKEN" POST $PC/penalties/decide "{\"id\":$PCPEN,\"status\":1,\"reason\":\"بخشودگی به دلیل تاخیر کارفرما\"}"; check "pc: CEO waives the penalty -> amount 0" '.success and .data.status == 1 and .data.amount == 0'
as "$COM_TOKEN" POST $PC/penalties/compute/$PCP; check "pc: a decided penalty is not recomputed" '.success and .data[0].amount == 0 and .data[0].status == 1'

# --- shipping gate: packing needs a released quality release note (the single record is the Quality one)
as "$WH_TOKEN" POST $PC/shipments "{\"contractId\":$QCT,\"workOrderId\":$WO2,\"packingList\":\"1 crate\"}"; check "pc: shipment planned for the unreleased work order" '.success and .data.status == 0 and (.data.number|startswith("SH"))'
PCSH2=$(val .data.id)
as "$WH_TOKEN" POST $PC/shipments/action "{\"id\":$PCSH2,\"action\":\"pack\",\"packingVerification\":\"checked\"}"; nomsg "pc: packing without a released release note is blocked" "بدون برگه ترخیص"
as "$WH_TOKEN" POST $PC/shipments "{\"contractId\":$QCT,\"workOrderId\":$WO1,\"packingList\":\"2 crates\"}"; PCSH1=$(val .data.id)
as "$WH_TOKEN" POST $PC/shipments/action "{\"id\":$PCSH1,\"action\":\"pack\"}"; no "pc: packing needs the verification result"
as "$WH_TOKEN" POST $PC/shipments/action "{\"id\":$PCSH1,\"action\":\"pack\",\"packingVerification\":\"matches the packing list\"}"; check "pc: released work order packed; release note linked" ".success and .data.status == 1 and .data.releaseNoteId == $RN1"
as "$WH_TOKEN" POST $PC/shipments/action "{\"id\":$PCSH1,\"action\":\"release\"}"; no "pc: shipping release belongs to planning"
as "$PLAN_TOKEN" POST $PC/shipments/action "{\"id\":$PCSH1,\"action\":\"release\"}"; check "pc: planning releases the shipment" '.success and .data.status == 2'
as "$WH_TOKEN" POST $PC/shipments/action "{\"id\":$PCSH1,\"action\":\"ship\",\"waybill\":\"WB-100\",\"shippingDocuments\":\"packing list, CoC\"}"; check "pc: shipped" '.success and .data.status == 3'
as "$PLAN_TOKEN" POST $PC/shipments/action "{\"id\":$PCSH1,\"action\":\"deliver\",\"deliveryReceiptNumber\":\"DR-1\"}"; check "pc: delivered" '.success and .data.status == 4 and .data.deliveredAt != null'
as "$PLAN_TOKEN" POST $PC/shipments/action "{\"id\":$PCSH1,\"action\":\"handover\",\"handoverMinutesNumber\":\"HM-1\"}"; check "pc: handover minutes recorded" '.success and .data.status == 5'

# --- reports: client report has no indices unless the contract says so; MS Project export; final book
as "$PLAN_TOKEN" POST $PC/reports/generate "{\"contractId\":$PCT,\"kind\":1}"
check "pc: client monthly report carries progress but no SPI/CPI" '.success and (.data.hash|length) > 10 and (.data.content|fromjson|(.physicalPercent // .PhysicalPercent)) != null and ((.data.content|fromjson|(.spi // .Spi)) == null) and ((.data.content|fromjson|(.cpi // .Cpi)) == null)'
PCREP=$(val .data.id)
as "$PM_TOKEN" POST $PC/reports/$PCREP/release; check "pc: project manager releases the client report" '.success and .data.releasedAt != null'
as "$PLAN_TOKEN" POST $PC/terms "$(echo "$PCTERMS" | jq -c '.clientReportShowsIndices = true')"; check "pc: contract allows indices in the client report" '.success and .data.clientReportShowsIndices == true'
as "$PLAN_TOKEN" POST $PC/reports/generate "{\"contractId\":$PCT,\"kind\":1,\"dataDate\":\"2026-11-06T00:00:00\"}"; check "pc: client report shows SPI/CPI only when the contract allows it" '(.data.content|fromjson|(.spi // .Spi)) != null'
as "$PLAN_TOKEN" POST $PC/terms "$PCTERMS" >/dev/null
as "$PLAN_TOKEN" POST $PC/reports/generate "{\"contractId\":$PCT,\"kind\":2}"; check "pc: internal weekly report generated" '.success and .data.kind == 2'
as "$PLAN_TOKEN" POST $PC/reports/generate "{\"contractId\":$PCT,\"kind\":3}"; check "pc: CEO monthly report generated" '.success and .data.kind == 3'
as "$PM_TOKEN" GET "$PC/reports?ContractId=$PCT"; check "pc: reports are listed (paged)" '.success and (.data.data|length) >= 3'
as "$PLAN_TOKEN" POST $PC/reports/generate "{\"contractId\":$PCT,\"kind\":4}"; no "pc: final book is not a periodic report"

PCMSP=$(curl -s -o "$OUT/pc-msp.tmp" -w '%{http_code} %{content_type}' "$API/$PC/export/msproject/$PCT" -H "Authorization: Bearer $PLAN_TOKEN")
if [ "${PCMSP%% *}" = "200" ] && echo "$PCMSP" | grep -qi "xml" && grep -q "<Project" "$OUT/pc-msp.tmp" && [ "$(grep -o '<Task>' "$OUT/pc-msp.tmp" | wc -l)" -eq 5 ] && python3 -I -c 'import sys,xml.dom.minidom as m; m.parse(sys.argv[1])' "$OUT/pc-msp.tmp" 2>/dev/null; then ok "pc: MS Project XML export is well-formed with 5 tasks (project + 4)"; else echo "$PCMSP" > "$B"; head -c 600 "$OUT/pc-msp.tmp" >> "$B"; bad "pc: MS Project XML export is well-formed with 5 tasks (project + 4)"; fi
rm -f "$OUT/pc-msp.tmp"

as "$DCC_TOKEN" POST $PC/final-books "{\"workOrderId\":$PCW1,\"variant\":\"client\"}"; no "pc: final book of an incomplete data book is refused"
as "$DCC_TOKEN" POST $PC/final-books "{\"workOrderId\":$PCW1,\"variant\":\"client\",\"allowIncomplete\":true}"; check "pc: incomplete final book by DCC refused (PMO only)" '.success == false'
call POST $PC/final-books "{\"workOrderId\":$PCW1,\"variant\":\"client\",\"allowIncomplete\":true}"; check "pc: final book (client variant) issued as controlled document R01 with a hash" '.success and .data.revision == "R01" and (.data.hash|length) > 10 and .data.controlledDocumentId != null and (.data.documentCode|endswith("-L"))'
call POST $PC/final-books "{\"workOrderId\":$PCW1,\"variant\":\"client\",\"allowIncomplete\":true}"; check "pc: re-issue makes revision R02" '.success and .data.revision == "R02"'
as "$DCC_TOKEN" GET "$PC/final-books?WorkOrderId=$PCW1"; check "pc: final books listed" '.success and (.data.data|length) == 2'

# --- second contract: WBS generation from the work order, plan run, cycle checks (new and legacy predecessors)
call POST Contracts '{"code":"PC-CT-02","title":"قرارداد آزمون WBS","status":1,"employer":"کارفرمای آزمون"}'; PCT2=$(val .data.id)
call POST ContractEquipments "{\"contractId\":$PCT2,\"equipmentId\":$PEQ,\"count\":1}"
call POST WorkOrders "{\"contractId\":$PCT2,\"equipmentId\":$PEQ,\"code\":\"PC-WO-3\",\"title\":\"Pump 3\",\"description\":\"x\",\"count\":1}"; PCW3=$(val .data.id)
as "$PLAN_TOKEN" POST $PC/terms "{\"contractId\":$PCT2,\"currencyCode\":\"IRR\",\"valueExVat\":500000000,\"calendarId\":$PCCAL,\"dataDateWeekday\":3,\"scheduleStartDate\":\"2026-11-02T00:00:00\",\"clientReviewDays\":14,\"autoSnapshot\":false}"; check "pc: second contract terms" '.success'
as "$PLAN_TOKEN" POST $PC/work-order-info "{\"workOrderId\":$PCW3,\"contractId\":$PCT2,\"typeCode\":\"PU\",\"sizeCode\":\"150\",\"optionCode\":\"25\"}"; check "pc: second equipment code PU1502501 (unique across contracts)" '.success and .data.equipmentProjectCode == "PU1502501"'
as "$PLAN_TOKEN" POST $PC/wbs/generate "{\"contractId\":$PCT2,\"workOrderIds\":[$PCW3]}"
check "pc: WBS generated from the work order: 4 nodes, 6 activities, 5 dependencies, 12 MDR lines" '.success and .data.nodesCreated == 4 and .data.activitiesCreated == 6 and .data.dependenciesCreated == 5 and .data.mdrLinesDrafted == 12'
as "$PLAN_TOKEN" POST $PC/wbs/generate "{\"contractId\":$PCT2,\"workOrderIds\":[$PCW3]}"; check "pc: generating again creates nothing twice" '(.success == false) or (.data.nodesCreated == 0 and .data.activitiesCreated == 0)'
as "$PLAN_TOKEN" GET "$PC/wbs?ContractId=$PCT2&PageSize=0"; check "pc: generated WBS nodes are listed" '.success and (.data.data|length) == 4'
as "$PLAN_TOKEN" POST $PC/cpm/run "{\"contractId\":$PCT2,\"forecast\":false,\"mirror\":true}"
check "pc: generated plan: MDR 10 + MDL 10 + packing 2 = 22 working days, ends 2026-12-01" '.success and .data.hasCycle == false and .data.projectFinishOffset == 22 and (.data.projectFinish|startswith("2026-12-01"))'
as "$PLAN_TOKEN" GET "$PC/activities?ContractId=$PCT2&PageSize=0"
PCMDRACT=$(jq -r '[.data.data[]|select(.title|contains("MDR"))|select(.title|contains("MDL")|not)][0].id' "$B"); PCDELACT=$(jq -r '[.data.data[]|select(.title|contains("تحویل به کارفرما"))][0].id' "$B")
as "$PLAN_TOKEN" POST $PC/dependencies "{\"contractId\":$PCT2,\"predecessorId\":$PCDELACT,\"successorId\":$PCMDRACT,\"type\":1,\"lagDays\":0}"; no "pc: cycle delivered -> MDR rejected"
as "$PLAN_TOKEN" POST $PC/wbs/auto-weights/$PCT2
as "$PLAN_TOKEN" GET "$PC/need-by?ContractId=$PCT2&PageSize=0"; check "pc: need-by dates listing" '.success'
as "$PLAN_TOKEN" GET "$PC/notices?WorkOrderId=$PCW3&PageSize=0"; check "pc: inspection notice listing" '.success'

# legacy assembly predecessors now run the same cycle detection
call POST Assemblies "{\"title\":\"Casing\",\"code\":\"PC-ASM-1\",\"workOrderId\":$PCW3,\"assemblerDuration\":1}"; PCAS1=$(jq -r '.data.id // empty' "$B")
call POST Assemblies "{\"title\":\"Rotor\",\"code\":\"PC-ASM-2\",\"workOrderId\":$PCW3,\"assemblerDuration\":1}"; PCAS2=$(jq -r '.data.id // empty' "$B")
[ -n "$PCAS1" ] && [ -n "$PCAS2" ] && ok "pc: two legacy assemblies created" || bad "pc: two legacy assemblies created"
call PUT Assemblies/set-predecessor "{\"id\":$PCAS2,\"predecessorId\":$PCAS1}"; check "pc: legacy assembly predecessor 1 -> 2 accepted" '.success'
call PUT Assemblies/set-predecessor "{\"id\":$PCAS1,\"predecessorId\":$PCAS2}"; no "pc: legacy assembly loop 2 -> 1 rejected"
call PUT Assemblies/set-predecessor "{\"id\":$PCAS1,\"predecessorId\":$PCAS1}"; no "pc: legacy assembly self link rejected"

# --- dashboards
as "$PM_TOKEN" GET $PC/dashboard; check "pc: PMO dashboard: active projects, red projects, late activities, guarantee alerts, exposure" '.success and .data.activeProjects >= 2 and .data.guaranteeAlerts >= 1 and .data.openChangeRequests >= 0 and (.data.worst|length) >= 1'
as "$CEO_TOKEN" GET "$PC/portfolio?PageSize=0"; check "pc: portfolio has one row for the main contract" ".success and ([.data.data[]|select(.contractId == $PCT)]|length) == 1"
as "$CEO_TOKEN" GET finance/ceo-dashboard
check "pc: CEO dashboard carries the project control counts" '.success and .data.pcActiveProjects >= 2 and .data.pcGuaranteeAlerts >= 1 and (.data.pcPenaltyExposure != null) and (.data.pcOverdueClientResponses >= 1)'
as "$SEC_TOKEN" GET $PC/dashboard; [ "$CODE" = "403" ] && ok "pc: security user cannot open the PMO dashboard (403)" || bad "pc: security user cannot open the PMO dashboard (403) got $CODE"
TOKEN=$ADMIN_TOKEN

# ---------------------------------------------------------------- finance: general ledger, treasury, receivables, tax (release F1)
TOKEN=$ADMIN_TOKEN
FG=finance/gl; FT=finance/treasury; FR=finance/receivables; FX=finance/tax
[ -n "$FIN2_TOKEN" ] && [ -n "$FM_TOKEN" ] && [ -n "$PAYR_TOKEN" ] && [ -n "$TR_TOKEN" ] && ok "finance: login second accountant, finance manager (17), payroll (18) and treasury (19) users" || bad "finance: login second accountant, finance manager (17), payroll (18) and treasury (19) users"
TODAY=$(date +%Y-%m-%d)
ADMINID=$(uid "$ADMIN_TOKEN")

# --- fiscal years on the Persian calendar
as "$FM_TOKEN" POST $FG/years/ensure '{"date":"2026-03-21T00:00:00"}'
check "finance: fiscal year 1405 has 12 periods" '.success and .data.year == 1405 and (.data.periods|length) == 12'
check "finance: 1405-01-01 is 2026-03-21, 1405-07-01 is 2026-09-23 and the year ends 2027-03-20" '(.data.startDate|startswith("2026-03-21")) and (.data.periods[6].startDate|startswith("2026-09-23")) and (.data.endDate|startswith("2027-03-20"))'
check "finance: first six periods have 31 days, the next five 30 days" '([.data.periods[0:6][]|((.endDate[0:10]|strptime("%Y-%m-%d")|mktime) - (.startDate[0:10]|strptime("%Y-%m-%d")|mktime))/86400]|all(. == 30)) and ([.data.periods[6:11][]|((.endDate[0:10]|strptime("%Y-%m-%d")|mktime) - (.startDate[0:10]|strptime("%Y-%m-%d")|mktime))/86400]|all(. == 29))'
FY=$(val .data.id)
as "$FM_TOKEN" POST $FG/years/ensure "{\"date\":\"${TODAY}T00:00:00\"}"; check "finance: the year of today exists" '.success and .data.id > 0'
FYT=$(val .data.id)
as "$PAYR_TOKEN" POST $FG/years/ensure '{"date":"2026-03-21T00:00:00"}'; no "finance: payroll user cannot manage fiscal years"
as "$COM_TOKEN" GET $FG/years; [ "$CODE" = "403" ] && ok "finance: commercial user cannot read the ledger (403)" || bad "finance: commercial user cannot read the ledger (403) got $CODE"
as "$PAYR_TOKEN" GET finance/dashboard; [ "$CODE" = "403" ] && ok "finance: payroll user has no finance dashboard yet (403)" || bad "finance: payroll user has no finance dashboard yet (403) got $CODE"

# --- chart of accounts, set-up check (statutory values are not seeded)
as "$FM_TOKEN" GET "$FG/accounts?PageSize=0"; check "finance: chart of accounts seeded with groups, general and subsidiary levels" '.success and (.data.data|length) > 100 and ([.data.data[]|select(.code=="10301")]|length) == 1 and ([.data.data[]|select(.code=="10301")][0].requiresDetail) == true'
as "$FM_TOKEN" GET $FG/setup-check
check "finance: set-up check lists only the missing VAT rate" '.success and .data.ok == false and ([.data.problems[].code]|sort) == ["VAT_RATE_MISSING"]'
check "finance: seeded ratios are flagged for verification" '(.data.toVerify|length) >= 3'
as "$FM_TOKEN" GET "$FX/rates?PageSize=0"; check "finance: tax table has no VAT row and the seeded ratios carry source and verify flag" '.success and ([.data.data[]|select(.kind == 1)]|length) == 0 and ([.data.data[]|select(.verify == true and (.source|length) > 0)]|length) >= 3'

# --- manual vouchers: rules and segregation of duties
MV="{\"date\":\"${TODAY}T00:00:00\",\"description\":\"سند آزمون دستی\",\"lines\":[{\"accountCode\":\"80108\",\"debit\":1000000},{\"accountCode\":\"10101\",\"credit\":1000000}]}"
as "$FIN_TOKEN" POST $FG/vouchers "{\"date\":\"${TODAY}T00:00:00\",\"lines\":[{\"accountCode\":\"80108\",\"debit\":1000},{\"accountCode\":\"10101\",\"credit\":900}]}"; no "finance: unbalanced voucher rejected"
as "$FIN_TOKEN" POST $FG/vouchers "{\"date\":\"${TODAY}T00:00:00\",\"lines\":[{\"accountCode\":\"801\",\"debit\":1000},{\"accountCode\":\"10101\",\"credit\":1000}]}"; no "finance: vouchers post to subsidiary accounts only"
as "$FIN_TOKEN" POST $FG/vouchers "{\"date\":\"${TODAY}T00:00:00\",\"lines\":[{\"accountCode\":\"10301\",\"debit\":1000},{\"accountCode\":\"10101\",\"credit\":1000}]}"; no "finance: account with a mandatory detail needs it in a manual voucher"
as "$FIN_TOKEN" POST $FG/vouchers "{\"date\":\"${TODAY}T00:00:00\",\"lines\":[{\"accountCode\":\"80108\",\"debit\":1000,\"credit\":1000},{\"accountCode\":\"10101\",\"credit\":0}]}"; no "finance: a line has either debit or credit"
as "$COM_TOKEN" POST $FG/vouchers "$MV"; no "finance: commercial user cannot write vouchers"
as "$FIN_TOKEN" POST $FG/vouchers "$MV"; check "finance: manual voucher saved as draft" '.success and .data.status == 0 and .data.type == 2 and .data.totalAmount == 1000000 and .data.number == null'
V1=$(val .data.id)
as "$FIN_TOKEN" POST $FG/vouchers/action "{\"id\":$V1,\"action\":\"check\"}"; no "finance: preparer cannot check own voucher"
as "$FIN2_TOKEN" POST $FG/vouchers/action "{\"id\":$V1,\"action\":\"check\"}"; check "finance: second accountant checks the voucher" '.success and .data.status == 1'
as "$FIN2_TOKEN" POST $FG/vouchers/action "{\"id\":$V1,\"action\":\"finalize\"}"; no "finance: only the finance manager finalizes"
as "$FM_TOKEN" POST $FG/vouchers/action "{\"id\":$V1,\"action\":\"finalize\"}"; check "finance: finance manager finalizes: number 1" '.success and .data.status == 2 and .data.number == 1'
as "$FIN_TOKEN" POST $FG/vouchers "$(jq -c ". + {id:$V1}" <<< "$MV")"; no "finance: a final voucher cannot be edited"
as "$FIN_TOKEN" DELETE "$FG/vouchers/$V1"; no "finance: a final voucher cannot be deleted"
as "$FM_TOKEN" POST $FG/vouchers "$MV"; V2=$(val .data.id)
as "$FIN2_TOKEN" POST $FG/vouchers/action "{\"id\":$V2,\"action\":\"check\"}"
as "$FM_TOKEN" POST $FG/vouchers/action "{\"id\":$V2,\"action\":\"finalize\"}"; no "finance: the preparer cannot finalize his own voucher"
as "$FIN2_TOKEN" POST $FG/vouchers/action "{\"id\":$V2,\"action\":\"uncheck\"}"; check "finance: a checked voucher goes back to draft" '.success and .data.status == 0'
as "$FM_TOKEN" DELETE "$FG/vouchers/$V2"; check "finance: a draft voucher is deleted" '.success'
as "$FIN_TOKEN" POST $FG/vouchers/action "{\"id\":$V1,\"action\":\"reverse\",\"note\":\"آزمون\"}"; check "finance: reversal of a final voucher is a new draft with swapped lines" '.success and .data.type == 5 and .data.status == 0 and .data.reversesVoucherId == '"$V1"' and ([.data.lines[]|select(.accountCode=="10101")][0].debit) == 1000000'
V1R=$(val .data.id)
as "$FIN_TOKEN" POST $FG/vouchers/action "{\"id\":$V1,\"action\":\"reverse\"}"; no "finance: a voucher is reversed only once"
as "$FIN2_TOKEN" POST $FG/vouchers/action "{\"id\":$V1R,\"action\":\"check\"}"; as "$FM_TOKEN" POST $FG/vouchers/action "{\"id\":$V1R,\"action\":\"finalize\"}"; check "finance: reversal voucher finalized: number 2" '.success and .data.number == 2'

# --- period lock, late posting of events into the next open period
as "$FM_TOKEN" GET $FG/years; PERIOD7=$(jq -r --argjson y "$FY" '.data[]|select(.id == $y)|.periods[]|select(.startDate[0:10] <= "2026-10-15" and .endDate[0:10] >= "2026-10-15")|.id' "$B")
as "$FM_TOKEN" POST $FG/periods/action "{\"id\":$PERIOD7,\"action\":\"lock\"}"; check "finance: period of 2026-10-15 locked" '.success'
as "$FIN_TOKEN" POST $FG/vouchers "$(jq -c '.date = "2026-10-15T00:00:00"' <<< "$MV")"; no "finance: a voucher in a locked period is rejected"
as "$FM_TOKEN" POST $FG/periods/action "{\"id\":$PERIOD7,\"action\":\"reopen\"}"; no "finance: reopening a period needs a reason"

# --- events into vouchers
as "$FM_TOKEN" POST $FG/post-events; check "finance: pending events turned into auto vouchers (some late: their period is locked)" '.success and .data.posted > 5 and .data.failed == 0 and .data.latePosted >= 1'
as "$FM_TOKEN" POST $FG/post-events; check "finance: posting events is idempotent" '.success and .data.posted == 0'
as "$FM_TOKEN" GET "finance/events?PageSize=0"; check "finance: every event is posted (or not required)" '[.data.data[]|select(.status != 4 and .status != 9)]|length == 0'
as "$FM_TOKEN" GET "$FG/vouchers?FiscalYearId=$FY&Type=3&PageSize=0"; check "finance: auto vouchers are checked and flagged as late posting where needed" '.success and (.data.data|length) > 5 and ([.data.data[]|select(.status != 1)]|length) == 0 and ([.data.data[]|select(.latePosting == true)]|length) >= 1'
as "$FM_TOKEN" POST $FG/periods/action "{\"id\":$PERIOD7,\"action\":\"reopen\",\"reason\":\"آزمون بازگشایی\"}"; check "finance: period reopened with a reason" '.success'
as "$FM_TOKEN" POST $FG/vouchers/finalize-up-to "{\"fiscalYearId\":$FY,\"upTo\":\"$(dd 400)T00:00:00\"}"; check "finance: bulk finalize numbers the auto vouchers one after the other" '.success and .data.finalized > 5 and .data.firstNumber == 3 and (.data.lastNumber - .data.firstNumber + 1) == .data.finalized'
as "$FM_TOKEN" GET "$FG/vouchers?FiscalYearId=$FY&Status=2&PageSize=0"; check "finance: final voucher numbers are gapless" '([.data.data[].number]|sort) as $n | ($n|length) > 5 and $n == [range(1; ($n|length)+1)]'
as "$FM_TOKEN" GET "$FG/reports/trial-balance?FiscalYearId=$FY&Level=2&Columns=6"; check "finance: trial balance of final vouchers balances" '.success and .data.balanced == true and .data.totalDebit == .data.totalCredit and .data.totalDebit > 0 and .data.includesDraft == false'
as "$FM_TOKEN" GET "$FG/reports/reconciliation"; check "finance: ledger equals the item cost sub-ledger" '.success and ([.data.rows[]|select(.key == "inventory")][0].ok) == true'
check "finance: goods received not invoiced equals the open receipts" '([.data.rows[]|select(.key == "grni")][0].ok) == true'
check "finance: payables per supplier equal approved invoices minus paid requests" '([.data.rows[]|select(.key == "ap")][0].ok) == true'
check "finance: no unposted events remain" '.data.unpostedEvents == 0'
as "$FM_TOKEN" GET "$FG/reports/balance-sheet?FiscalYearId=$FY"; check "finance: balance sheet balances" '.success and .data.balanced == true'
as "$FM_TOKEN" GET "$FG/reports/income-statement?FiscalYearId=$FY"; check "finance: income statement returned" '.success and (.data.lines|length) > 0'
as "$FM_TOKEN" GET "$FG/reports/journal?FiscalYearId=$FY&PageSize=5"; check "finance: journal is paged by day" '.success and (.data.data|length) <= 5 and .data.totalItems > 0'
CODE=$(curl -s -o "$B" -w '%{http_code}' "$API/$FG/reports/export/trial-balance?FiscalYearId=$FY" -H "Authorization: Bearer $FM_TOKEN")
[ "$CODE" = "200" ] && [ "$(head -c 3 "$B" | od -An -tx1 | tr -d ' \n')" = "efbbbf" ] && ok "finance: trial balance exports as UTF-8 CSV with BOM" || bad "finance: trial balance exports as UTF-8 CSV with BOM ($CODE)"

# --- treasury masters
as "$FIN_TOKEN" POST $FT/bank-accounts '{"title":"x","iban":"IR270170000000100324200001"}'; no "finance: only treasury creates bank accounts"
as "$TR_TOKEN" POST $FT/bank-accounts '{"title":"بانک آزمون","iban":"IR000000000000000000000000"}'; no "finance: IBAN with a wrong check digit rejected"
as "$TR_TOKEN" POST $FT/bank-accounts '{"title":"بانک الف - جاری","bankName":"ملی","branch":"مرکزی","accountNumber":"100324200001","iban":"IR270170000000100324200001"}'; check "finance: bank account saved on account 10102" '.success and .data.accountCode == "10102"'
BA1=$(val .data.id)
as "$TR_TOKEN" POST $FT/bank-accounts '{"title":"بانک ب - جاری","bankName":"صادرات","accountNumber":"200000000003","iban":"IR950190000000200000000003"}'; BA2=$(val .data.id)
as "$TR_TOKEN" POST $FT/bank-accounts '{"title":"تکراری","iban":"IR270170000000100324200001"}'; no "finance: the same IBAN cannot be registered twice"
as "$TR_TOKEN" POST $FT/cash-boxes '{"title":"صندوق مرکزی","custodianTitle":"خزانه‌دار"}'; check "finance: cash box saved" '.success and .data.id > 0'
CB=$(val .data.id)
as "$TR_TOKEN" POST $FT/cheque-books "{\"bankAccountId\":$BA1,\"series\":\"A\",\"fromSerial\":501,\"toSerial\":510}"; check "finance: cheque book with 10 blank leaves" '.success and .data.leaves == 10 and .data.blank == 10'
CBK=$(val .data.id)
as "$TR_TOKEN" POST $FT/cheque-books "{\"bankAccountId\":$BA1,\"series\":\"A\",\"fromSerial\":505,\"toSerial\":515}"; no "finance: overlapping cheque book rejected"
as "$TR_TOKEN" POST $FT/petty-cash "{\"holderUserId\":$ADMINID,\"holderTitle\":\"تنخواه دار آزمون\",\"ceiling\":5000000}"; check "finance: petty cash fund saved" '.success and .data.ceiling == 5000000 and .data.balance == 0'
PC1=$(val .data.id)

# --- tax rates are data: no VAT rate yet, so a sales invoice is refused
as "$FIN_TOKEN" POST $FR/customers '{"name":"مشتری الف آزمون","economicCode":"123"}'; no "finance: economic code must have 12 or 14 digits"
as "$FIN_TOKEN" POST $FR/customers '{"name":"مشتری الف آزمون","economicCode":"411111111111","nationalId":"10101010101","postalCode":"1234567890","employerName":"مشتری الف آزمون"}'; check "finance: customer saved" '.success and .data.id > 0 and .data.openAmount == 0'
CU1=$(val .data.id)
INVJ="{\"date\":\"$(dd -50)T00:00:00\",\"dueDate\":\"$(dd -45)T00:00:00\",\"customerId\":$CU1,\"type\":2,\"description\":\"صورت‌وضعیت آزمون\",\"useDefaultDeductionRates\":true,\"lines\":[{\"description\":\"قطعه ریخته‌گری\",\"quantity\":2,\"unitPrice\":50000000}]}"
as "$FIN_TOKEN" POST $FR/invoices "$INVJ"; nomsg "finance: invoice refused while the VAT rate table has no row for the date" "نرخ مالیات بر ارزش افزوده"
as "$COM_TOKEN" POST $FX/rates '{"kind":1,"rate":10,"effectiveFrom":"2026-03-21T00:00:00","source":"x"}'; no "finance: a commercial user cannot save tax rates"
as "$FM_TOKEN" POST $FX/rates '{"kind":1,"rate":10,"effectiveFrom":"2026-03-21T00:00:00"}'; no "finance: a tax rate needs its source"
as "$FM_TOKEN" POST $FX/rates '{"kind":1,"rate":150,"effectiveFrom":"2026-03-21T00:00:00","source":"داده آزمون CI"}'; no "finance: a tax rate above 100 is rejected"
as "$FM_TOKEN" POST $FX/rates '{"kind":1,"rate":10,"effectiveFrom":"2026-03-21T00:00:00","source":"داده آزمون CI","note":"نرخ آزمایشی؛ نرخ واقعی را مدیر مالی ثبت می‌کند"}'; check "finance: VAT rate entered as data by the finance manager" '.success and .data.kind == 1 and .data.rate == 10'
as "$FM_TOKEN" GET $FG/setup-check; check "finance: set-up check is clean once the VAT rate exists" '.success and .data.ok == true'

# --- sales invoice: VAT from the table, deductions from the default ratios, segregation
as "$FIN_TOKEN" POST $FR/invoices "$INVJ"; check "finance: sales invoice draft: net 100M, VAT 10M, receivable 92M" '.success and .data.status == 0 and .data.netAmount == 100000000 and .data.vatAmount == 10000000 and .data.grossAmount == 110000000 and .data.receivableAmount == 92000000'
INV1=$(val .data.id)
as "$FIN_TOKEN" POST $FR/invoices/action "{\"id\":$INV1,\"action\":\"approve\"}"; no "finance: the creator cannot approve his own sales invoice"
as "$FM_TOKEN" POST $FR/invoices/action "{\"id\":$INV1,\"action\":\"approve\"}"; check "finance: sales invoice approved and numbered" '.success and .data.status == 2 and (.data.number|length) > 0'
as "$FM_TOKEN" POST $FR/invoices "$(jq -c ". + {id:$INV1}" <<< "$INVJ")"; no "finance: an approved sales invoice cannot be edited"
as "$FM_TOKEN" GET "$FR/customers?PageSize=0"; check "finance: customer open amount is the receivable" '[.data.data[]|select(.id == '$CU1')][0].openAmount == 92000000'

# --- receipts: transfer + cheque, cash; FIFO allocation
RJ="{\"date\":\"${TODAY}T00:00:00\",\"customerId\":$CU1,\"description\":\"دریافت آزمون\",\"lines\":[{\"instrument\":2,\"amount\":20000000,\"bankAccountId\":$BA1,\"reference\":\"TRK-1\"},{\"instrument\":3,\"amount\":30000000,\"chequeSayadId\":\"1234567890123456\",\"chequeSerial\":\"7788\",\"chequeBank\":\"ملی\",\"chequeDueDate\":\"${TODAY}T00:00:00\"}]}"
as "$COM_TOKEN" POST $FT/receipts "$RJ"; nomsg "finance: a commercial user cannot draft a receipt (treasury and accountants only)" "مجوز"
as "$TR_TOKEN" POST $FT/receipts "$(jq -c '.lines[0].amount = 0' <<< "$RJ")"; no "finance: a receipt line needs an amount"
as "$TR_TOKEN" POST $FT/receipts "$(jq -c '.lines[1].chequeSayadId = "123"' <<< "$RJ")"; no "finance: a cheque needs a 16-digit Sayad id"
as "$TR_TOKEN" POST $FT/receipts "$RJ"; check "finance: receipt of 50M saved as draft, FIFO allocated to the invoice" '.success and .data.status == 0 and .data.totalAmount == 50000000'
R1=$(val .data.id)
as "$TR_TOKEN" POST $FT/receipts/action "{\"id\":$R1,\"action\":\"post\"}"; no "finance: a receipt must be approved before it is posted"
as "$FM_TOKEN" POST $FT/receipts/action "{\"id\":$R1,\"action\":\"approve\"}"; check "finance: receipt approved" '.success and .data.status == 1'
as "$TR_TOKEN" POST $FT/receipts/action "{\"id\":$R1,\"action\":\"post\"}"; check "finance: receipt posted" '.success and .data.status == 2'
as "$FM_TOKEN" GET "$FT/received-cheques?PageSize=0"; check "finance: the cheque entered the box" '.success and ([.data.data[]|select(.sayadId == "1234567890123456" and .status == 1 and .amount == 30000000)]|length) == 1'
RC1=$(jq -r '[.data.data[]|select(.sayadId == "1234567890123456")][0].id' "$B")
as "$TR_TOKEN" POST $FT/received-cheques/action "{\"id\":$RC1,\"action\":\"deposit\",\"bankAccountId\":$BA1}"; check "finance: cheque deposited into the bank account" '.success and .data.status == 2'
as "$TR_TOKEN" POST $FT/received-cheques/action "{\"id\":$RC1,\"action\":\"clear\"}"; check "finance: cheque cleared" '.success and .data.status == 3 and (.data.history|length) >= 3'
as "$FM_TOKEN" GET "$FR/invoices/$INV1"; check "finance: invoice paid 50M, open 42M" '.success and .data.paidAmount == 50000000 and .data.openAmount == 42000000'

# --- cash receipt into the cash box, then a credit note
as "$TR_TOKEN" POST $FT/receipts "{\"date\":\"${TODAY}T00:00:00\",\"customerId\":$CU1,\"lines\":[{\"instrument\":1,\"amount\":5000000,\"cashBoxId\":$CB}]}"; R2=$(val .data.id)
as "$FM_TOKEN" POST $FT/receipts/action "{\"id\":$R2,\"action\":\"approve\"}"; as "$TR_TOKEN" POST $FT/receipts/action "{\"id\":$R2,\"action\":\"post\"}"; check "finance: cash receipt posted into the cash box" '.success and .data.status == 2'
as "$FIN_TOKEN" POST $FR/invoices "{\"date\":\"${TODAY}T00:00:00\",\"customerId\":$CU1,\"type\":6,\"creditedInvoiceId\":$INV1,\"description\":\"اصلاحیه آزمون\",\"lines\":[{\"description\":\"کسر\",\"quantity\":1,\"unitPrice\":1000000}]}"; check "finance: credit note of 1M + 10% VAT" '.success and .data.type == 6 and .data.grossAmount == 1100000'
CN1=$(val .data.id)
as "$FM_TOKEN" POST $FR/invoices/action "{\"id\":$CN1,\"action\":\"approve\"}"; check "finance: credit note approved" '.success and .data.status == 2'
as "$FM_TOKEN" GET "$FR/ageing"; check "finance: ageing puts the old invoice in the 31-60 day bucket" '.success and .data.total > 0 and .data.days31To60 > 0 and .data.rows[0].total == .data.total'
as "$FM_TOKEN" GET "$FR/customers/$CU1/statement"; check "finance: customer statement ends with the open balance" '.success and (.data.rows|length) >= 4 and .data.balance == .data.rows[-1].balance'
as "$FM_TOKEN" GET "$FR/invoices?PageSize=2"; check "finance: sales invoices are paged" '.success and .data.totalItems >= 2 and (.data.data|length) <= 2'

# --- supplier payment through treasury (second invoice of the order)
call POST inventory/documents "{\"type\":1,\"documentDate\":\"2026-10-22T00:00:00\",\"warehouseId\":$W1,\"purchaseOrderId\":$PO,\"lines\":[{\"itemId\":$BOLT,\"quantity\":40,\"locationId\":$L2,\"purchaseOrderLineId\":$POL}]}"
DF=$(val .data.id); call POST "inventory/documents/$DF/confirm"; check "finance: 40 more bolts received against the order" '.success'
as "$COM_TOKEN" POST procurement/invoices "{\"purchaseOrderId\":$PO,\"invoiceNumber\":\"F-1002\",\"invoiceDate\":\"2026-10-24T00:00:00\",\"isOfficial\":true,\"lines\":[{\"orderLineId\":$POL,\"quantity\":40,\"unitPrice\":390000}]}"; SINV2=$(val .data.id)
as "$FIN_TOKEN" POST procurement/invoices/action "{\"id\":$SINV2,\"action\":\"approve\"}"
as "$COM_TOKEN" POST procurement/payments "{\"type\":3,\"invoiceId\":$SINV2,\"amount\":17160000,\"deductionLines\":[{\"kind\":1,\"base\":17160000,\"ratePercent\":2.727,\"amount\":468000}]}"; check "finance: payment request with a typed withholding line: deductions 468000" '.success and .data.deductions == 468000'
PAY2=$(val .data.id)
call POST procurement/payments/action "{\"id\":$PAY2,\"action\":\"approve\"}"; check "finance: payment request approved by PMO" '.success and .data.status == 1'
as "$TR_TOKEN" GET "$FT/payable-requests?PageSize=0"; check "finance: treasury sees the approved request with net 16692000" '.success and ([.data.data[]|select(.id == '$PAY2' and .netAmount == 16692000)]|length) == 1'
call POST procurement/suppliers "{\"providerId\":$PA,\"iban\":\"IR000000000000000000000000\"}"; no "finance: supplier IBAN with a wrong check digit rejected"
call POST procurement/suppliers "{\"providerId\":$PA,\"iban\":\"IR970170000000100324200002\"}"; check "finance: supplier IBAN registered" '.success and .data.iban == "IR970170000000100324200002"'
PJ="{\"date\":\"${TODAY}T00:00:00\",\"purpose\":1,\"paymentRequestIds\":[$PAY2],\"lines\":[{\"instrument\":2,\"amount\":9532000,\"bankAccountId\":$BA1,\"reference\":\"PAYREF-77\"},{\"instrument\":3,\"amount\":7160000,\"bankAccountId\":$BA1,\"chequeSerial\":501,\"chequeDueDate\":\"${TODAY}T00:00:00\",\"chequeSayadId\":\"6543210987654321\"}]}"
as "$TR_TOKEN" POST $FT/payments "$(jq -c '.lines[0].amount = 9000000' <<< "$PJ")"; no "finance: payment lines must add up to the net of the requests"
as "$TR_TOKEN" POST $FT/payments "$(jq -c '.lines[0].payeeIban = "IR950190000000200000000003"' <<< "$PJ")"; check "finance: payment to another IBAN is drafted" '.success and .data.status == 0'
PM0=$(val .data.id)
as "$FM_TOKEN" POST $FT/payments/action "{\"id\":$PM0,\"action\":\"approve\"}"; nomsg "finance: IBAN differing from the registered one needs a written reason" "شبا"
as "$TR_TOKEN" POST $FT/payments/action "{\"id\":$PM0,\"action\":\"cancel\"}"; check "finance: that draft is cancelled" '.success and .data.status == 3'
as "$TR_TOKEN" POST $FT/payments "$PJ"; check "finance: payment of 16692000 by transfer and cheque drafted" '.success and .data.status == 0 and .data.netAmount == 16692000 and .data.deductionAmount == 468000'
PM1=$(val .data.id)
as "$TR_TOKEN" POST procurement/payments/action "{\"id\":$PAY2,\"action\":\"paid\",\"reference\":\"x\",\"bankAccountId\":$BA1}"; nomsg "finance: the old pay action is refused for a request linked to a treasury payment" "خزانه"
as "$TR_TOKEN" POST $FT/payments/action "{\"id\":$PM1,\"action\":\"post\"}"; no "finance: an unapproved payment cannot be posted"
as "$TR_TOKEN" POST $FT/payments/action "{\"id\":$PM1,\"action\":\"approve\"}"; no "finance: treasury cannot approve (finance manager only)"
as "$FM_TOKEN" POST $FT/payments/action "{\"id\":$PM1,\"action\":\"approve\"}"; check "finance: finance manager approves" '.success and .data.status == 1'
as "$TR_TOKEN" POST $FT/payments/action "{\"id\":$PM1,\"action\":\"post\"}"; check "finance: payment posted" '.success and .data.status == 2 and (.data.number|length) > 0'
as "$FM_TOKEN" GET "$FT/issued-cheques?PageSize=0"; check "finance: cheque leaf 501 is issued" '.success and ([.data.data[]|select(.serial == 501 and .status == 1 and .amount == 7160000)]|length) == 1'
as "$FM_TOKEN" GET "$FT/bank-accounts?PageSize=0"; check "finance: bank account balance moved by the receipts and payments" '[.data.data[]|select(.id == '$BA1')][0].balance > 0'
as "$FM_TOKEN" POST $FG/post-events; as "$FM_TOKEN" GET "$FG/reports/reconciliation"; check "finance: ledgers still agree with sub-ledgers after receipts and payments" '.success and .data.clean == true'

# --- bank statement import, matching and reconciliation
SJ="{\"bankAccountId\":$BA1,\"openingBalance\":0,\"closingBalance\":33160000,\"rows\":[{\"date\":\"${TODAY}T00:00:00\",\"description\":\"واریز\",\"deposit\":20000000,\"reference\":\"TRK-1\"},{\"date\":\"${TODAY}T00:00:00\",\"description\":\"وصول چک 1234567890123456\",\"deposit\":30000000,\"reference\":\"C-1\"},{\"date\":\"${TODAY}T00:00:00\",\"description\":\"سود\",\"deposit\":2000},{\"date\":\"${TODAY}T00:00:00\",\"description\":\"انتقال\",\"withdrawal\":9532000,\"reference\":\"PAYREF-77\"},{\"date\":\"${TODAY}T00:00:00\",\"description\":\"چک پرداختی\",\"withdrawal\":7160000,\"reference\":\"501\"},{\"date\":\"${TODAY}T00:00:00\",\"description\":\"کارمزد\",\"withdrawal\":150000}]}"
as "$TR_TOKEN" POST $FT/statements/import "$(jq -c '.closingBalance = 1' <<< "$SJ")"; no "finance: a statement whose closing balance does not follow from its rows is refused"
as "$TR_TOKEN" POST $FT/statements/import "$SJ"; check "finance: bank statement imported with 6 lines" '.success and (.data.lines|length) == 6'
ST1=$(val .data.id)
as "$TR_TOKEN" POST $FT/statements/import "$SJ"; no "finance: the same statement cannot be imported twice"
as "$TR_TOKEN" POST $FT/statements/$ST1/auto-match; check "finance: auto-match pairs transfers and cheques with their documents" '.success and .data.matched >= 2'
as "$TR_TOKEN" GET "$FT/statements/$ST1"; check "finance: statement view shows matched and unmatched counts" '.success and .data.matched + .data.unmatched == 6'
FEELINE=$(jq -r '[.data.lines[]|select(.description == "کارمزد")][0].id' "$B"); INTLINE=$(jq -r '[.data.lines[]|select(.description == "سود")][0].id' "$B")
as "$TR_TOKEN" POST $FT/reconciliations "{\"statementId\":$ST1}"; check "finance: reconciliation is created from the statement" '.success and .data.status == 0 and .data.statementId == '$ST1
RC_A=$(val .data.id)
as "$FM_TOKEN" POST $FT/reconciliations/action "{\"id\":$RC_A,\"action\":\"approve\"}"; no "finance: a reconciliation with a difference or unmatched lines is not approved"
as "$FIN_TOKEN" POST $FT/statements/match "{\"statementId\":$ST1,\"lineId\":$FEELINE,\"kind\":\"fee\"}"; check "finance: bank fee matched manually" '.success'
as "$FIN_TOKEN" POST $FT/statements/match "{\"statementId\":$ST1,\"lineId\":$INTLINE,\"kind\":\"interest\"}"; check "finance: bank interest matched manually" '.success'
as "$FM_TOKEN" POST $FG/post-events
as "$TR_TOKEN" POST $FT/reconciliations/action "{\"id\":$RC_A,\"action\":\"refresh\"}"; check "finance: reconciliation refreshed" '.success'
RC_A_DIFF=$(val '.data.difference|round'); RC_A_UNM=$(val '.data.unmatchedLines')
as "$FM_TOKEN" POST $FT/reconciliations/action "{\"id\":$RC_A,\"action\":\"approve\"}"
# the outcome is tied to the refreshed difference: zero means approved by someone other than the preparer, anything else is refused with the reason
if [ "$RC_A_DIFF" != "0" ]; then nomsg "finance: reconciliation with a difference ($RC_A_DIFF) is refused" "مغایرت"
elif [ "$RC_A_UNM" != "0" ]; then nomsg "finance: reconciliation with zero difference but $RC_A_UNM unmatched statement lines is refused" "تطبیق نخورده"
else check "finance: reconciliation with zero difference is approved by the finance manager, not the preparer" '.success and .data.status == 1 and .data.approvedByName != .data.preparedByName'; fi

# --- petty cash: top-up from the cash box, settlement by the holder
as "$TR_TOKEN" POST $FT/payments "{\"date\":\"${TODAY}T00:00:00\",\"purpose\":2,\"pettyCashId\":$PC1,\"topUpAmount\":2000000,\"lines\":[{\"instrument\":1,\"amount\":2000000,\"cashBoxId\":$CB}]}"; check "finance: petty cash top-up drafted" '.success and .data.purpose == 2 and .data.netAmount == 2000000'
PM2=$(val .data.id)
as "$FM_TOKEN" POST $FT/payments/action "{\"id\":$PM2,\"action\":\"approve\"}"; as "$TR_TOKEN" POST $FT/payments/action "{\"id\":$PM2,\"action\":\"post\"}"; check "finance: top-up posted" '.success and .data.status == 2'
as "$FM_TOKEN" GET "$FT/petty-cash?PageSize=0"; check "finance: petty cash balance is 2M" '[.data.data[]|select(.id == '$PC1')][0].balance == 2000000'
as "$TR_TOKEN" POST $FT/settlements "{\"pettyCashId\":$PC1,\"date\":\"${TODAY}T00:00:00\",\"lines\":[{\"description\":\"خرید ملزومات\",\"accountCode\":\"10301\",\"amount\":800000}]}"; no "finance: petty cash settlement to a non-expense account is refused"
call POST $FT/settlements "{\"pettyCashId\":$PC1,\"date\":\"${TODAY}T00:00:00\",\"lines\":[{\"description\":\"خرید ملزومات\",\"accountCode\":\"70202\",\"amount\":9000000}]}"; SEBIG=$(val .data.id)
as "$FIN_TOKEN" POST $FT/settlements/action "{\"id\":$SEBIG,\"action\":\"approve\"}"; nomsg "finance: a settlement above the fund balance is refused" "کافی نیست"
call POST $FT/settlements/action "{\"id\":$SEBIG,\"action\":\"cancel\"}"
call POST $FT/settlements "{\"pettyCashId\":$PC1,\"date\":\"${TODAY}T00:00:00\",\"lines\":[{\"description\":\"خرید ملزومات\",\"accountCode\":\"70202\",\"amount\":800000}]}"; check "finance: holder saves a settlement of 800000" '.success and .data.status == 0 and .data.totalAmount == 800000'
SE1=$(val .data.id)
call POST $FT/settlements/action "{\"id\":$SE1,\"action\":\"approve\"}"; no "finance: the holder cannot approve his own settlement"
as "$FIN_TOKEN" POST $FT/settlements/action "{\"id\":$SE1,\"action\":\"approve\"}"; check "finance: accountant approves the settlement" '.success and .data.status == 2'
as "$FM_TOKEN" GET "$FT/petty-cash?PageSize=0"; check "finance: petty cash balance is 1.2M after settlement" '[.data.data[]|select(.id == '$PC1')][0].balance == 1200000'

# --- guarantees, cash position
as "$TR_TOKEN" POST $FT/guarantees "{\"kind\":1,\"direction\":1,\"partyName\":\"کارفرمای آزمون\",\"contractId\":$QCT,\"amount\":5000000,\"date\":\"${TODAY}T00:00:00\",\"dueDate\":\"$(dd 20)T00:00:00\",\"reference\":\"ZM-1\"}"; check "finance: guarantee saved" '.success and .data.status == 1'
GU1=$(val .data.id)
as "$TR_TOKEN" POST $FT/guarantees/action "{\"id\":$GU1,\"action\":\"return\"}"; check "finance: guarantee returned" '.success and .data.status == 2'
as "$FM_TOKEN" GET $FT/cash-position; check "finance: cash position totals and an eight-week forecast" '.success and .data.total == .data.cash + .data.banks + .data.pettyCash and (.data.forecast|length) == 8 and (.data.accounts|length) >= 3'
as "$COM_TOKEN" GET $FT/cash-position; [ "$CODE" = "403" ] && ok "finance: commercial user cannot read the cash position (403)" || bad "finance: commercial user cannot read the cash position (403) got $CODE"

# --- tax reports
as "$FM_TOKEN" GET "$FX/rates?PageSize=0"; check "finance: VAT rate table has one row, the old row closed or open-ended" '.success and ([.data.data[]|select(.kind == 1)]|length) == 1'
as "$FM_TOKEN" GET "$FX/vat-season?Year=1405&Season=1"; check "finance: VAT season report returned" '.success and .data.season == 1'
as "$FM_TOKEN" GET "$FX/taxpayer-sales?From=$(dd -400)T00:00:00&To=$(dd 5)T00:00:00&PageSize=0"; check "finance: taxpayer sales list shows the approved invoice" '.success and ([.data.data[]|select(.invoiceId == '$INV1')]|length) == 1'

# --- draft sales invoices from payment milestones
as "$FM_TOKEN" POST $FR/invoices/draft-from-milestones; check "finance: due milestones become draft sales invoices" '.success and .data.drafted >= 1'
as "$FM_TOKEN" POST $FR/invoices/draft-from-milestones; check "finance: a second run drafts nothing new" '.success and .data.drafted == 0'

# --- dashboards
as "$FM_TOKEN" GET finance/dashboard; check "finance: dashboard returned for the finance manager" '.success and .data.cashTotal >= 0 and (.data.generatedAt|length) > 0'
as "$CEO_TOKEN" GET finance/ceo-dashboard; check "finance: CEO dashboard carries the finance block" '.success and .data.finance != null and (.data.finance.cashAndBank|type) == "number"'
as "$WH_TOKEN" GET finance/dashboard; [ "$CODE" = "403" ] && ok "finance: storekeeper has no finance dashboard (403)" || bad "finance: storekeeper has no finance dashboard (403) got $CODE"

# --- year-end close on a past year (1400)
as "$FM_TOKEN" POST $FG/years/ensure '{"date":"2021-06-01T00:00:00"}'; check "finance: fiscal year 1400 created" '.success and .data.year == 1400'
FY0=$(val .data.id)
as "$FIN_TOKEN" POST $FG/vouchers '{"date":"2021-06-01T00:00:00","description":"افتتاح آزمون","lines":[{"accountCode":"10101","debit":100000},{"accountCode":"60102","credit":100000}]}'; VA=$(val .data.id)
as "$FIN_TOKEN" POST $FG/vouchers '{"date":"2021-06-02T00:00:00","description":"هزینه آزمون","lines":[{"accountCode":"80108","debit":40000},{"accountCode":"10101","credit":40000}]}'; VB=$(val .data.id)
for V in $VA $VB; do as "$FIN2_TOKEN" POST $FG/vouchers/action "{\"id\":$V,\"action\":\"check\"}"; as "$FM_TOKEN" POST $FG/vouchers/action "{\"id\":$V,\"action\":\"finalize\"}"; done
check "finance: vouchers of year 1400 finalized" '.success and .data.status == 2'
as "$FM_TOKEN" GET $FG/years; PIDS=$(jq -r --argjson y "$FY0" '.data[]|select(.id == $y)|.periods[].id' "$B")
for P in $PIDS; do as "$FM_TOKEN" POST $FG/periods/action "{\"id\":$P,\"action\":\"lock\"}"; done
as "$FM_TOKEN" POST $FG/years/$FY0/close '{"overrideReconciliation":true,"reason":"آزمون بستن سال"}'; check "finance: year 1400 closed with closing vouchers and a 1401 opening voucher" '.success and .data.status == 2 and .data.closingVoucherId != null'
as "$FM_TOKEN" GET $FG/years; YC=$(jq -r --argjson y "$FY0" '.data[]|select(.id == $y)|.closingVoucherId' "$B"); YNEXT=$(jq -r '.data[]|select(.year == 1401)|.id' "$B"); YOPEN=$(jq -r '.data[]|select(.year == 1401)|.openingVoucherId' "$B")
# exact consequences of the close: profit 100,000 - 40,000 = 60,000 moves to retained earnings 50301; temporary accounts are zero; the opening voucher repeats the closing balances
as "$FM_TOKEN" GET "$FG/reports/trial-balance?FiscalYearId=$FY0&Level=2&IncludeDraft=true"
check "finance: after the close the temporary accounts 60102 and 80108 are zero in year 1400" '([.data.rows[]|select(.code == "60102" or .code == "80108")|(.closingDebit + .closingCredit)]|add) == 0'
check "finance: the profit and loss summary 50901 is cleared and retained earnings 50301 were credited 60000 during the close" '([.data.rows[]|select(.code == "50901")|(.closingDebit + .closingCredit)]|add // 0) == 0 and ([.data.rows[]|select(.code == "50301")|.credit]|add) == 60000'
as "$FM_TOKEN" GET $FG/vouchers/$YOPEN
check "finance: the 1401 opening voucher is number 1 with exactly cash 60000 debit and retained earnings 60000 credit" '.success and .data.number == 1 and (.data.lines|length) == 2 and ([.data.lines[]|select(.accountCode == "10101" and .debit == 60000)]|length) == 1 and ([.data.lines[]|select(.accountCode == "50301" and .credit == 60000)]|length) == 1'
as "$FM_TOKEN" GET $FG/vouchers/$YC
check "finance: the closing voucher of 1400 mirrors the opening voucher (60000 both sides)" '.success and ([.data.lines[].debit]|add) == 60000 and ([.data.lines[].credit]|add) == 60000'
as "$FM_TOKEN" POST $FG/years/$FY0/close '{"overrideReconciliation":true,"reason":"دوباره"}'; no "finance: a closed year is not closed twice"
as "$FIN_TOKEN" POST $FG/vouchers '{"date":"2021-07-01T00:00:00","lines":[{"accountCode":"80108","debit":1},{"accountCode":"10101","credit":1}]}'; no "finance: no voucher can be written into a closed year"

# =============================================================================================== release F2
TOKEN=$ADMIN_TOKEN
FA=finance/assets; FP=finance/payroll; FS=finance/sepidar

# --- F1 leftover: the VAT season report takes purchases from the posted supplier invoices (season 3 of 1405 holds F-1001 and F-1002)
as "$FM_TOKEN" GET "$FX/vat-season?Year=1405&Season=3"
check "finance: VAT season 3 of 1405 takes purchases from the posted supplier invoices (net 39000000, VAT 3900000)" '.success and .data.purchasesNet == 39000000 and .data.purchasesVat == 3900000'
check "finance: the supplier purchase row shows net 39000000, VAT 3900000 and 2 documents" '([.data.rows[]|select(.side == "Purchases" and .netAmount == 39000000 and .vatAmount == 3900000 and .documents == 2)]|length) == 1'

# --- F1 leftover: bank reconciliation approval, exact rules
as "$TR_TOKEN" POST $FT/bank-accounts '{"title":"بانک ج - آزمون تطبیق","bankName":"ملت","accountNumber":"100324200003","iban":"IR700170000000100324200003"}'; BA3=$(val .data.id)
as "$TR_TOKEN" POST $FT/bank-accounts '{"title":"بانک د - آزمون تطبیق","bankName":"ملت","accountNumber":"100324200004","iban":"IR430170000000100324200004"}'; BA4=$(val .data.id)
[ -n "$BA3" ] && [ "$BA3" != "null" ] && [ -n "$BA4" ] && [ "$BA4" != "null" ] && ok "finance: two more bank accounts for the reconciliation rules" || bad "finance: two more bank accounts for the reconciliation rules"
as "$TR_TOKEN" POST $FT/statements/import "{\"bankAccountId\":$BA3,\"openingBalance\":0,\"closingBalance\":5000,\"rows\":[{\"date\":\"${TODAY}T00:00:00\",\"description\":\"سود روزشمار\",\"deposit\":5000}]}"; check "finance: statement of account C imported" '.success and (.data.lines|length) == 1'
STC=$(val .data.id); STCLINE=$(val '.data.lines[0].id')
as "$TR_TOKEN" POST $FT/reconciliations "{\"statementId\":$STC}"; check "finance: reconciliation of account C created by treasury" '.success and .data.status == 0'
RCC=$(val .data.id)
as "$FM_TOKEN" POST $FT/reconciliations/action "{\"id\":$RCC,\"action\":\"approve\"}"; nomsg "finance: a reconciliation with a difference is refused" "مغایرت صفر نیست"
as "$FIN_TOKEN" POST $FT/statements/match "{\"statementId\":$STC,\"lineId\":$STCLINE,\"kind\":\"interest\"}"; check "finance: the interest line is matched" '.success'
as "$FM_TOKEN" POST $FG/post-events
as "$TR_TOKEN" POST $FT/reconciliations/action "{\"id\":$RCC,\"action\":\"refresh\"}"; check "finance: reconciliation of account C refreshed with zero difference" '.success and .data.difference == 0 and .data.bankBalance == 5000 and .data.bookBalance == 5000'
as "$FM_TOKEN" POST $FT/reconciliations/action "{\"id\":$RCC,\"action\":\"approve\"}"; check "finance: zero difference is approved by a user other than the preparer" '.success and .data.status == 1 and (.data.approvedByName|length) > 0 and .data.approvedByName != .data.preparedByName'
as "$TR_TOKEN" POST $FT/statements/import "{\"bankAccountId\":$BA4,\"openingBalance\":0,\"closingBalance\":0,\"rows\":[{\"date\":\"${TODAY}T00:00:00\",\"description\":\"واریز\",\"deposit\":100},{\"date\":\"${TODAY}T00:00:00\",\"description\":\"برداشت\",\"withdrawal\":100}]}"; STD=$(val .data.id)
as "$FM_TOKEN" POST $FT/reconciliations "{\"statementId\":$STD}"; RCD=$(val .data.id)
as "$FM_TOKEN" POST $FT/reconciliations/action "{\"id\":$RCD,\"action\":\"approve\"}"; nomsg "finance: the preparer of a reconciliation cannot approve it" "تهیه‌کننده"

# --- F1 leftover: cancelling the only cheque of a supplier payment returns the payment requests to Approved (capital purchase, 9900000 with VAT)
as "$WH_TOKEN" POST procurement/requests "{\"source\":2,\"kind\":1,\"requestDate\":\"2026-10-22T00:00:00\",\"description\":\"خرید سرمایه‌ای آزمون\",\"submit\":true,\"lines\":[{\"itemId\":$BOLT,\"quantity\":1,\"estimatedUnitPrice\":9000000,\"technicalRequirements\":\"آزمون\"}]}"; check "f2: capital purchase request submitted" '.success and .data.status == 1'
F2RQ=$(val .data.id); F2RL=$(val '.data.lines[0].id')
as "$PLAN_TOKEN" POST procurement/requests/action "{\"id\":$F2RQ,\"action\":\"approve-planning\"}"; check "f2: capital purchase request approved by planning" '.success and .data.status == 2'
as "$COM_TOKEN" POST procurement/orders "{\"kind\":1,\"orderDate\":\"2026-10-25T00:00:00\",\"providerId\":$PA,\"vatPercent\":10,\"requirements\":\"آزمون\",\"deliveryDate\":\"2026-11-10T00:00:00\",\"lines\":[{\"requestLineId\":$F2RL,\"itemId\":$BOLT,\"quantity\":1,\"unitPrice\":9000000,\"isCapital\":true}]}"; check "f2: capital purchase order saved (9000000 plus 900000 VAT)" '.success and .data.totalAmount == 9000000 and .data.vatAmount == 900000'
F2PO=$(val .data.id); F2POL=$(val '.data.lines[0].id')
call POST procurement/orders/action "{\"id\":$F2PO,\"action\":\"approve\"}"
as "$COM_TOKEN" POST procurement/orders/action "{\"id\":$F2PO,\"action\":\"release\"}"; check "f2: capital purchase order released" '.success and .data.status == 2'
call POST inventory/documents "{\"type\":1,\"documentDate\":\"2026-10-25T00:00:00\",\"warehouseId\":$W1,\"purchaseOrderId\":$F2PO,\"lines\":[{\"itemId\":$BOLT,\"quantity\":1,\"locationId\":$L2,\"purchaseOrderLineId\":$F2POL}]}"; F2D=$(val .data.id)
call POST "inventory/documents/$F2D/confirm"; check "f2: capital purchase received" '.success'
as "$COM_TOKEN" POST procurement/invoices "{\"purchaseOrderId\":$F2PO,\"invoiceNumber\":\"F-1003\",\"invoiceDate\":\"2026-10-25T00:00:00\",\"isOfficial\":true,\"lines\":[{\"orderLineId\":$F2POL,\"quantity\":1,\"unitPrice\":9000000}]}"; F2INV=$(val .data.id)
as "$FIN_TOKEN" POST procurement/invoices/action "{\"id\":$F2INV,\"action\":\"approve\"}"; check "f2: capital invoice approved by finance" '.success and .data.status == 3'
as "$FM_TOKEN" GET "$FA?Status=0&PageSize=0"; check "f2: the approved capital invoice line drafted a fixed asset of 9000000 (source: supplier invoice line)" '.success and ([.data.data[]|select(.source == 2 and .cost == 9000000 and .status == 0)]|length) == 1'
as "$COM_TOKEN" POST procurement/payments "{\"type\":3,\"invoiceId\":$F2INV,\"amount\":9900000}"; F2PAY=$(val .data.id)
call POST procurement/payments/action "{\"id\":$F2PAY,\"action\":\"approve\"}"; check "f2: payment request approved" '.success and .data.status == 1'
as "$TR_TOKEN" POST $FT/payments "{\"date\":\"${TODAY}T00:00:00\",\"purpose\":1,\"paymentRequestIds\":[$F2PAY],\"lines\":[{\"instrument\":3,\"amount\":9900000,\"bankAccountId\":$BA1,\"chequeSerial\":502,\"chequeDueDate\":\"${TODAY}T00:00:00\",\"chequeSayadId\":\"5555666677778888\"}]}"; check "f2: payment by a single cheque drafted" '.success and .data.status == 0 and .data.netAmount == 9900000'
F2PM=$(val .data.id)
as "$FM_TOKEN" POST $FT/payments/action "{\"id\":$F2PM,\"action\":\"approve\"}"
as "$TR_TOKEN" POST $FT/payments/action "{\"id\":$F2PM,\"action\":\"post\"}"; check "f2: payment posted" '.success and .data.status == 2'
as "$FM_TOKEN" GET "$FT/issued-cheques?PageSize=0"; F2CHQ=$(jq -r '[.data.data[]|select(.serial == 502)][0].id' "$B")
as "$TR_TOKEN" POST $FT/issued-cheques/action "{\"id\":$F2CHQ,\"action\":\"cancel\",\"note\":\"آزمون ابطال\"}"; check "f2: the cheque is cancelled" '.success and .data.status == 3'
as "$FM_TOKEN" GET "$FT/payments/$F2PM"; check "finance: cancelling the only cheque cancels the payment" '.success and .data.status == 3'
as "$TR_TOKEN" GET "$FT/payable-requests?PageSize=0"; check "finance: the payment request is Approved again and payable (net 9900000)" '.success and ([.data.data[]|select(.id == '$F2PAY' and .netAmount == 9900000)]|length) == 1'
as "$FM_TOKEN" POST $FG/post-events; as "$FM_TOKEN" GET "$FG/reports/reconciliation"; check "finance: ledgers agree with sub-ledgers after the cheque cancellation" '.success and .data.clean == true'

# --- fixed assets: classes, rates not invented
as "$FM_TOKEN" GET $FA/classes; check "assets: classes are seeded with a source, flagged for verification and without invented rates" '.success and (.data|length) >= 10 and ([.data[]|select(.code == "MCH" and .verify == true and (.source|length) > 0 and .hasRate == false)]|length) == 1 and ([.data[]|select(.code == "LND" and .hasRate == true)]|length) == 1'
as "$FM_TOKEN" GET $FG/setup-check; check "assets: set-up check lists the missing rates in the modules list, apart from the ledger problems" '.success and (.data.problems|type) == "array" and ([.data.modules[]|select(.module == "Assets" and .ok == false and (.missing|index("MCH")) != null)]|length) == 1'
as "$PAYR_TOKEN" GET $FA/classes; [ "$CODE" = "403" ] && ok "assets: payroll user cannot read fixed assets (403)" || bad "assets: payroll user cannot read fixed assets (403) got $CODE"
call POST finance/cost-centers '{"code":"CC-F2","title":"مرکز هزینه آزمون F2","isProduction":true}'; check "assets: production cost centre saved" '.success and .data.isProduction == true'
CCF2=$(val .data.id)
as "$FIN_TOKEN" POST $FA/classes '{"code":"TST","title":"رده آزمون","assetAccountCode":"20103","accumulatedAccountCode":"20203","method":1,"ratePercent":12,"startRule":1,"idleRatePercent":30,"idleAfterMonths":0,"salvagePercent":0,"source":"آزمون CI - نرخ فرضی فقط برای آزمون","verify":true,"isActive":true}'; check "assets: test class with a rate saved" '.success and .data.hasRate == true'
CLS1=$(val .data.id)
as "$FIN_TOKEN" POST $FA/classes '{"code":"TNR","title":"رده بدون نرخ","assetAccountCode":"20103","accumulatedAccountCode":"20203","method":1,"startRule":1,"idleRatePercent":30,"idleAfterMonths":0,"salvagePercent":0,"source":"آزمون","verify":true,"isActive":true}'; check "assets: class without a rate saved" '.success and .data.hasRate == false'
CLS2=$(val .data.id)
as "$COM_TOKEN" POST $FA/classes '{"code":"XXX","title":"x"}'; no "assets: commercial user cannot save classes"

# --- assets: draft, approval by a second person, offset account
AJ() { echo "{\"title\":\"$1\",\"classId\":$2,\"costCenterId\":$CCF2,\"acquisitionDate\":\"2026-06-22T00:00:00\",\"inUseDate\":\"$3T00:00:00\",\"cost\":$4,\"source\":1,\"offsetAccountCode\":\"${5:-20108}\"}"; }
as "$FIN_TOKEN" POST $FA "$(AJ 'دستگاه آزمون یک' $CLS1 2026-06-25 120000000)"; check "assets: asset saved as a draft with a temporary code" '.success and .data.status == 0 and (.data.code|startswith("TMP-"))'
AS1=$(val .data.id)
as "$FIN_TOKEN" POST $FA/action "{\"id\":$AS1,\"action\":\"approve\"}"; nomsg "assets: the creator cannot approve the asset" "شخص دیگری"
as "$FIN_TOKEN" POST $FA "$(AJ 'دستگاه بدون حساب طرف' $CLS1 2026-06-25 1000000 | jq -c 'del(.offsetAccountCode)')"; AS0=$(val .data.id)
as "$FM_TOKEN" POST $FA/action "{\"id\":$AS0,\"action\":\"approve\"}"; nomsg "assets: approval needs the account the cost sits in now" "حساب طرف بستانکار"
as "$FM_TOKEN" POST $FA/action "{\"id\":$AS1,\"action\":\"approve\"}"; check "assets: finance manager approves: final code and acquisition posted" '.success and .data.status == 1 and (.data.code|startswith("TST-0001"))'
as "$FIN_TOKEN" POST $FA "$(AJ 'دستگاه آزمون دو' $CLS1 2026-06-22 60000000)"; AS2=$(val .data.id)
as "$FM_TOKEN" POST $FA/action "{\"id\":$AS2,\"action\":\"approve\"}"
as "$FIN_TOKEN" POST $FA/action "{\"id\":$AS2,\"action\":\"idle-from\",\"date\":\"2026-06-22T00:00:00\"}"; check "assets: asset put idle" '.success and .data.status == 2'
as "$FIN_TOKEN" POST $FA "$(AJ 'دستگاه بدون نرخ' $CLS2 2026-06-25 10000000)"; AS3=$(val .data.id)
as "$FM_TOKEN" POST $FA/action "{\"id\":$AS3,\"action\":\"approve\"}"; check "assets: asset of the class without a rate approved" '.success and .data.status == 1'
as "$FM_TOKEN" GET $FG/years; PTIR=$(jq -r '.data[]|select(.year == 1405)|.periods[]|select(.startDate|startswith("2026-06-22"))|.id' "$B")
[ -n "$PTIR" ] && [ "$PTIR" != "null" ] && ok "assets: period Tir 1405 found" || bad "assets: period Tir 1405 found"
as "$FIN_TOKEN" POST $FA/depreciation/prepare/$PTIR; nomsg "assets: depreciation is refused while a class has no rate or useful life (names the class)" "TNR"
as "$FIN_TOKEN" POST $FA/classes "{\"id\":$CLS2,\"code\":\"TNR\",\"title\":\"رده بدون نرخ\",\"assetAccountCode\":\"20103\",\"accumulatedAccountCode\":\"20203\",\"method\":1,\"ratePercent\":12,\"startRule\":1,\"idleRatePercent\":30,\"idleAfterMonths\":0,\"salvagePercent\":0,\"source\":\"آزمون CI - نرخ فرضی\",\"verify\":true,\"isActive\":true}"; check "assets: rate entered for the class" '.success and .data.hasRate == true'
as "$FM_TOKEN" POST $FA/depreciation/prepare/$PTIR; check "assets: depreciation of Tir 1405 prepared: 1200000 + 180000 (idle 30%) + 100000" '.success and .data.status == 0 and .data.totalAmount == 1480000 and .data.assetCount == 3'
DRN=$(val .data.id)
as "$FM_TOKEN" POST $FA/depreciation/action "{\"id\":$DRN,\"action\":\"approve\"}"; nomsg "assets: the preparer of the depreciation cannot approve it" "شخص دیگری"
as "$FIN_TOKEN" POST $FA/depreciation/prepare/$PTIR; check "assets: depreciation prepared again by the accountant" '.success and .data.totalAmount == 1480000'
as "$FIN_TOKEN" POST $FA/depreciation/action "{\"id\":$DRN,\"action\":\"approve\"}"; no "assets: only the finance manager approves depreciation"
as "$FM2_TOKEN" POST $FA/depreciation/action "{\"id\":$DRN,\"action\":\"approve\"}"; check "assets: depreciation approved by a manager who prepared nothing (the first preparer stays excluded)" '.success and .data.status == 1'
as "$FIN_TOKEN" POST $FA/depreciation/prepare/$PTIR; no "assets: an approved depreciation run is final"
as "$FM_TOKEN" GET "$FA/$AS1"; check "assets: accumulated depreciation 1200000, book value 118800000" '.success and .data.accumulatedDepreciation == 1200000 and .data.bookValue == 118800000'
as "$FM_TOKEN" GET "$FA/$AS2"; check "assets: idle asset depreciated at 30% of the rate: 180000" '.success and .data.accumulatedDepreciation == 180000'
as "$FM_TOKEN" POST $FG/post-events; as "$FM_TOKEN" GET "$FA/reports/depreciation-schedule?year=1405"; check "assets: depreciation schedule of 1405 totals 1480000 in Tir" '.success and .data.total == 1480000 and ([.data.assets[]|select(.months[3] == 1200000)]|length) == 1'
as "$FM_TOKEN" GET "$FA/reports/movement?year=1405"; check "assets: movement report returned" '.success'
as "$FM_TOKEN" GET "$FA/reports/handover?custodian=x"; check "assets: handover report returned" '.success'

# --- disposal: sale in Mordad with catch-up depreciation (1200000), book value 117600000, loss 17600000
as "$FIN_TOKEN" POST $FA/disposals "{\"assetId\":$AS1,\"date\":\"2026-07-25T00:00:00\",\"kind\":1,\"proceedsAmount\":100000000,\"proceedsMethod\":1,\"cashBoxId\":$CB}"; check "assets: disposal previews catch-up depreciation 1200000, book value 117600000 and a loss of 17600000" '.success and .data.status == 0 and .data.catchUpDepreciation == 1200000 and .data.bookValue == 117600000 and .data.gainLoss == -17600000'
DSP=$(val .data.id)
as "$FIN_TOKEN" POST $FA/disposals/action "{\"id\":$DSP,\"action\":\"approve\"}"; no "assets: the accountant cannot approve a disposal"
as "$FM_TOKEN" POST $FA/disposals/action "{\"id\":$DSP,\"action\":\"approve\"}"; check "assets: finance manager approves the disposal" '.success'
as "$FM_TOKEN" GET "$FA/$AS1"; check "assets: the sold asset is disposed" '.success and .data.status == 4'
as "$FM_TOKEN" POST $FG/post-events; as "$FM_TOKEN" GET "$FG/reports/reconciliation"; check "assets: ledgers agree with sub-ledgers after assets" '.success and .data.clean == true'
as "$FM_TOKEN" GET "$FA/depreciation/runs?PageSize=0"; check "assets: depreciation runs listed" '.success and ([.data.data[]|select(.id == '$DRN' and .status == 1)]|length) == 1'

# --- payroll: confidentiality
as "$FIN_TOKEN" GET $FP/employees; [ "$CODE" = "403" ] && ok "payroll: accountant cannot read employees (403)" || bad "payroll: accountant cannot read employees (403) got $CODE"
as "$ADMIN_TOKEN" GET $FP/payslips; [ "$CODE" = "403" ] && ok "payroll: PMO cannot read payslips (403)" || bad "payroll: PMO cannot read payslips (403) got $CODE"
as "$TR_TOKEN" GET $FP/runs; [ "$CODE" = "403" ] && ok "payroll: treasury cannot read payroll runs (403)" || bad "payroll: treasury cannot read payroll runs (403) got $CODE"
as "$PAYR_TOKEN" GET finance/dashboard; [ "$CODE" = "403" ] && ok "payroll: payroll user still has no finance dashboard (403)" || bad "payroll: payroll user still has no finance dashboard (403) got $CODE"

# --- payroll: master data, the statutory values are not seeded
as "$PAYR_TOKEN" GET "$FP/setup?year=1405"; check "payroll: set-up lists the missing rate keys and the tax brackets" '.success and .data.ok == false and (.data.missingRateKeys|index("MinimumDailyWage")) != null and .data.taxBracketsMissing == true'
as "$PAYR_TOKEN" GET "$FP/rates?Year=0"; check "payroll: only the long-standing ratios are seeded, with source and verify flag" '.success and ([.data[]|select(.verify == true and (.source|length) > 0)]|length) >= 5 and ([.data[]|select(.key == "MinimumDailyWage")]|length) == 0'
as "$PAYR_TOKEN" GET $FP/items; check "payroll: payroll items are seeded" '.success and ([.data[]|select(.code == "BASE_WAGE" or .code == "OVERTIME" or .code == "SS_EMPLOYEE" or .code == "INCOME_TAX")]|length) == 4'
as "$PAYR_TOKEN" POST $FP/workshops '{"code":"1234567890","name":"کارگاه آزمون","employerName":"شرکت آزمون","address":"آزمون","branch":"آزمون","isActive":true}'; check "payroll: insurance workshop saved" '.success and .data.id > 0'
PWS=$(val .data.id)
EJ() { jq -nc --arg c "$1" --arg n "$2" --arg l "$3" --arg nc "$4" --arg iban "$5" --argjson w "$PWS" '{personnelCode:$c,firstName:$n,lastName:$l,nationalCode:$nc,gender:1,maritalStatus:1,childrenCount:0,insuranceNumber:("99"+$c),insuranceJobCode:"1111",workshopId:$w,hireDate:"2020-01-01T00:00:00",employmentType:1,jobTitle:"کارشناس آزمون",unit:"آزمون",dailyWage:600000,bankIban:$iban,isActive:true}'; }
as "$PAYR_TOKEN" POST $FP/employees "$(EJ T001 آزمون یک 123 IR270170000000100324200001)"; no "payroll: an invalid national code is refused"
as "$PAYR_TOKEN" POST $FP/employees "$(EJ T001 آزمون یک 0499370899 IR000000000000000000000000)"; no "payroll: an IBAN with a wrong check digit is refused"
as "$PAYR_TOKEN" POST $FP/employees "$(EJ T001 آزمون یک 0499370899 IR270170000000100324200001)"; check "payroll: employee saved" '.success and .data.id > 0'
PE1=$(val .data.id)
as "$PAYR_TOKEN" POST $FP/employees "$(EJ T002 آزمون دو 0499370899 IR270170000000100324200001)"; no "payroll: the same national code cannot be used twice"
as "$FIN_TOKEN" POST $FP/employees "$(EJ T003 آزمون سه 0013542419 IR270170000000100324200001)"; [ "$CODE" = "403" ] && ok "payroll: accountant cannot save employees (403)" || bad "payroll: accountant cannot save employees (403) got $CODE"
as "$CEO_TOKEN" GET "$FP/employees/$PE1"; check "payroll: the CEO may read employee data" '.success and .data.id == '$PE1
as "$CEO_TOKEN" POST $FP/employees "$(EJ T004 آزمون چهار 1234567891 IR270170000000100324200001)"; no "payroll: the CEO cannot change payroll data"
as "$FIN_TOKEN" GET finance/employees/directory; check "payroll: the directory shows name, title and unit only" '.success and ([.data[]|select(.id == '$PE1')]|length) == 1 and ([.data[]|has("dailyWage")]|any|not)'
as "$PAYR_TOKEN" POST $FP/attendance "{\"employeeId\":$PE1,\"year\":1405,\"month\":6,\"daysWorked\":30,\"overtimeHours\":10}"; check "payroll: attendance of Shahrivar 1405 saved" '.success'
as "$PAYR_TOKEN" POST $FP/runs '{"year":1405,"month":6,"note":"آزمون"}'; check "payroll: run of Shahrivar 1405 created" '.success and .data.status == 0'
PRUN=$(val .data.id)
as "$PAYR_TOKEN" POST $FP/runs/$PRUN/calculate; nomsg "payroll: calculation without the statutory values fails and lists the missing keys" "HousingAllowance"
SRC='آزمون CI - مقدار فرضی فقط برای آزمون'
for KV in SeniorityDaily=10000 HousingAllowance=9000000 GroceryAllowance=4000000 MaritalAllowance=3000000 MinimumDailyWage=500000 InsuranceCeilingDaily=900000 DailyWorkHours=8; do
  as "$PAYR_TOKEN" POST $FP/rates "{\"year\":1405,\"key\":\"${KV%%=*}\",\"value\":${KV##*=},\"source\":\"$SRC\",\"verify\":true}"
done
check "payroll: statutory rates entered by payroll with a source" '.success and .data.key == "DailyWorkHours"'
as "$PAYR_TOKEN" POST $FP/rates '{"year":1405,"key":"X","value":1}'; no "payroll: a rate without a source is refused"
as "$PAYR_TOKEN" POST $FP/runs/$PRUN/calculate; nomsg "payroll: calculation without tax brackets fails" "TaxBrackets"
as "$PAYR_TOKEN" POST $FP/tax-brackets '{"year":1405,"brackets":[{"fromAmount":0,"toAmount":240000000,"ratePercent":0},{"fromAmount":240000000,"ratePercent":10}]}'; check "payroll: tax brackets saved" '.success and (.data|length) == 2'
as "$PAYR_TOKEN" POST $FP/tax-brackets '{"year":1405,"brackets":[{"fromAmount":0,"toAmount":100,"ratePercent":0},{"fromAmount":200,"ratePercent":10}]}'; no "payroll: tax brackets must be contiguous"
as "$PAYR_TOKEN" POST $FP/runs/$PRUN/calculate; check "payroll: calculated: gross 32350000, insurance 1890000 (7% up to the ceiling), employer 6210000 (20% + 3%), tax 1046000, net 29414000" '.success and .data.run.status == 1 and .data.run.totalGross == 32350000 and .data.run.totalEmployeeInsurance == 1890000 and .data.run.totalEmployerInsurance == 6210000 and .data.run.totalTax == 1046000 and .data.run.totalNet == 29414000'
as "$PAYR_TOKEN" GET "$FP/payslips?RunId=$PRUN"; check "payroll: payslip shows base wage 18000000 and overtime 1050000 (1.4 x hourly wage x 10 hours)" '.success and (.data.data|length) == 1 and .data.data[0].net == 29414000 and ([.data.data[0].lines[]|select(.itemCode == "BASE_WAGE" and .amount == 18000000)]|length) == 1 and ([.data.data[0].lines[]|select(.itemCode == "OVERTIME" and .amount == 1050000)]|length) == 1'
PSL=$(val '.data.data[0].id')
as "$PAYR_TOKEN" POST $FP/runs/$PRUN/approve; nomsg "payroll: the payroll clerk cannot approve (finance manager only)" "مدیر مالی"
as "$FIN_TOKEN" POST $FP/runs/$PRUN/approve; [ "$CODE" = "403" ] && ok "payroll: accountant cannot approve the run (403)" || bad "payroll: accountant cannot approve the run (403) got $CODE"
as "$PAYR_TOKEN" POST $FP/runs/$PRUN/post; no "payroll: a run that is not approved cannot be posted"
as "$FM_TOKEN" POST $FP/attendance "{\"employeeId\":$PE1,\"year\":1405,\"month\":7,\"daysWorked\":30}"; check "payroll: finance manager saves attendance of Mehr" '.success'
as "$FM_TOKEN" POST $FP/runs '{"year":1405,"month":7}'; PRUN7=$(val .data.id)
as "$FM_TOKEN" POST $FP/runs/$PRUN7/calculate; check "payroll: finance manager calculates the run of Mehr" '.success and .data.run.status == 1'
as "$FM_TOKEN" POST $FP/runs/$PRUN7/approve; nomsg "payroll: the person who calculated the run cannot approve it" "شخص دیگری"
as "$FM_TOKEN" DELETE $FP/runs/$PRUN7; check "payroll: an unapproved run can be deleted" '.success'

# --- the payroll month basis is 30 days (company decision): 29-, 30- and 31-day months; only unpaid leave and absence reduce the 30 payable days
pcalc() { # pcalc YEAR MONTH ATTENDANCE_JSON_FIELDS : saves the attendance, calculates a run, leaves the result in $B (the run is deleted by the caller)
  as "$PAYR_TOKEN" POST $FP/attendance "{\"employeeId\":$PE1,\"year\":$1,\"month\":$2,$3}"
  as "$PAYR_TOKEN" POST $FP/runs "{\"year\":$1,\"month\":$2}"; PRX=$(val .data.id)
  as "$PAYR_TOKEN" POST $FP/runs/$PRX/calculate
}
as "$PAYR_TOKEN" POST $FP/attendance "{\"employeeId\":$PE1,\"year\":1405,\"month\":12,\"daysWorked\":30}"; nomsg "payroll: 30 days worked in Esfand 1405 (29 days) is refused" "29 روز"
as "$PAYR_TOKEN" POST $FP/attendance "{\"employeeId\":$PE1,\"year\":1405,\"month\":12,\"daysWorked\":29,\"unpaidLeaveDays\":1}"; nomsg "payroll: 29 worked + 1 unpaid in a 29-day month is refused" "29 روز"
pcalc 1405 12 '"daysWorked":29'
check "payroll: a full 29-day month (Esfand 1405) pays the 30-day basis: gross 31300000 (base 18000000 + seniority 300000 + housing 9000000 + grocery 4000000)" '.success and .data.run.totalGross == 31300000'
as "$PAYR_TOKEN" DELETE $FP/runs/$PRX
pcalc 1405 1 '"daysWorked":31'
check "payroll: a full 31-day month (Farvardin 1405) pays 30 days, not 31: gross 31300000" '.success and .data.run.totalGross == 31300000'
as "$PAYR_TOKEN" DELETE $FP/runs/$PRX
pcalc 1405 7 '"daysWorked":27,"unpaidLeaveDays":3'
check "payroll: 30-day month with 3 unpaid days pays 27 days: gross 28170000 (base 16200000 + seniority 270000 + housing 8100000 + grocery 3600000)" '.success and .data.run.totalGross == 28170000'
as "$PAYR_TOKEN" DELETE $FP/runs/$PRX
as "$PAYR_TOKEN" POST $FP/attendance "{\"employeeId\":$PE1,\"year\":1405,\"month\":7,\"daysWorked\":30}"
as "$FM_TOKEN" POST $FP/runs/$PRUN/approve; check "payroll: finance manager approves the run" '.success and .data.status == 2 and (.data.approvedByName|length) > 0'
as "$PAYR_TOKEN" POST $FP/attendance "{\"employeeId\":$PE1,\"year\":1405,\"month\":6,\"daysWorked\":28}"; no "payroll: attendance of an approved month is locked"
as "$PAYR_TOKEN" POST $FP/runs/$PRUN/calculate; no "payroll: an approved run is immutable"
as "$PAYR_TOKEN" DELETE $FP/runs/$PRUN; no "payroll: an approved run cannot be deleted"
as "$PAYR_TOKEN" POST $FP/tax-brackets '{"year":1405,"brackets":[{"fromAmount":0,"ratePercent":1}]}'; no "payroll: tax brackets of a year with an approved run are frozen"
as "$PAYR_TOKEN" GET "$FP/reports/insurance-list?year=1405&month=6"; check "payroll: insurance list of the approved run: one worker, capped base 27000000" '.success and (.data.rows|length) == 1 and .data.rows[0].insurableCapped == 27000000'
as "$PAYR_TOKEN" GET "$FP/reports/tax-list?year=1405&month=6"; check "payroll: tax list of the approved run: tax 1046000" '.success and .data.totalTax == 1046000'
as "$PAYR_TOKEN" POST $FP/runs/$PRUN/post; check "payroll: posted to the ledger as totals" '.success and .data.status == 3 and .data.accountingEventId != null'
as "$FM_TOKEN" POST $FG/post-events; as "$FM_TOKEN" GET "$FG/reports/reconciliation"; check "payroll: ledgers agree with sub-ledgers after the payroll voucher" '.success and .data.clean == true'
as "$PAYR_TOKEN" GET "$FP/runs/$PRUN/bank-transfer"; check "payroll: bank transfer list shows the employee IBAN and the net" '.success and (.data|length) == 1 and .data[0].amount == 29414000'
as "$TR_TOKEN" POST $FT/payments "{\"date\":\"${TODAY}T00:00:00\",\"purpose\":3,\"payrollRunId\":$PRUN,\"lines\":[{\"instrument\":2,\"amount\":29414000,\"bankAccountId\":$BA1,\"reference\":\"PAYROLL-1405-06\"}]}"; check "payroll: payment of the run drafted by treasury for the net total" '.success and .data.status == 0 and .data.netAmount == 29414000'
PRPM=$(val .data.id)
as "$FM_TOKEN" POST $FT/payments/action "{\"id\":$PRPM,\"action\":\"approve\"}"
as "$TR_TOKEN" POST $FT/payments/action "{\"id\":$PRPM,\"action\":\"post\"}"; check "payroll: payment of the run posted" '.success and .data.status == 2'
as "$PAYR_TOKEN" GET "$FP/runs/$PRUN"; check "payroll: the run is Paid" '.success and .data.status == 4'
as "$TR_TOKEN" POST $FT/payments "{\"date\":\"${TODAY}T00:00:00\",\"purpose\":3,\"payrollRunId\":$PRUN,\"lines\":[{\"instrument\":2,\"amount\":29414000,\"bankAccountId\":$BA1}]}"; no "payroll: a run cannot be paid twice"
as "$PAYR_TOKEN" GET "$FP/leave-balances?year=1405"; check "payroll: leave balances returned" '.success'
as "$PAYR_TOKEN" GET "$FP/reports/employee-year?employeeId=$PE1&year=1405"; check "payroll: employee year summary returned" '.success'
as "$PAYR_TOKEN" GET "$FP/setup?year=1405"; check "payroll: set-up is complete for 1405" '.success and .data.ok == true'

# --- Sepidar outbox: tax and insurance packages, approval by a second person, Excel export
as "$PAYR_TOKEN" POST $FS/generate "{\"kind\":3,\"year\":1405,\"month\":6,\"workshopId\":$PWS}"; check "outbox: insurance list package generated by payroll" '.success and .data.status == 0 and .data.version == 1 and .data.rowCount == 1 and (.data.number|startswith("SEP-"))'
OP1=$(val .data.id)
as "$PAYR_TOKEN" POST $FS/action "{\"id\":$OP1,\"action\":\"approve\"}"; nomsg "outbox: the preparer cannot approve the package" "شخص دیگری"
as "$FM_TOKEN" POST $FS/action "{\"id\":$OP1,\"action\":\"approve\"}"; check "outbox: finance manager approves the package" '.success and .data.status == 1'
BX="$B.xlsx"; XC=$(curl -s -o "$BX" -w '%{http_code}' -H "Authorization: Bearer $PAYR_TOKEN" "$API/$FS/packages/$OP1/export")
[ "$XC" = "200" ] && [ "$(head -c 2 "$BX")" = "PK" ] && ok "outbox: export is an Excel workbook" || bad "outbox: export is an Excel workbook (http $XC)"
rm -f "$BX"
as "$PAYR_TOKEN" POST $FS/action "{\"id\":$OP1,\"action\":\"send\"}"; nomsg "outbox: sending without a configured exchange folder says so" "پیکربندی نشده"
as "$PAYR_TOKEN" POST $FS/action "{\"id\":$OP1,\"action\":\"mark-sent\"}"; no "outbox: marking as sent needs a reference"
as "$PAYR_TOKEN" POST $FS/action "{\"id\":$OP1,\"action\":\"mark-sent\",\"reference\":\"TRK-INS-1\"}"; check "outbox: marked as sent with a reference" '.success and .data.status == 3 and .data.externalReference == "TRK-INS-1"'
as "$PAYR_TOKEN" POST $FS/action "{\"id\":$OP1,\"action\":\"mark-rejected\"}"; no "outbox: a rejection needs a reason"
as "$PAYR_TOKEN" POST $FS/action "{\"id\":$OP1,\"action\":\"mark-accepted\"}"; check "outbox: accepted" '.success and .data.status == 4'
as "$PAYR_TOKEN" POST $FS/generate "{\"kind\":3,\"year\":1405,\"month\":6,\"workshopId\":$PWS}"; check "outbox: a new package for the same period is version 2" '.success and .data.version == 2'
OP2=$(val .data.id)
as "$PAYR_TOKEN" GET "$FS/packages/$OP1"; check "outbox: the earlier package is superseded" '.success and .data.status == 6 and .data.supersededByPackageId == '$OP2
as "$FIN_TOKEN" GET "$FS/packages/$OP2"; no "outbox: payroll packages are hidden from the accountants"
as "$FIN_TOKEN" POST $FS/generate '{"kind":4,"year":1405,"month":6}'; no "outbox: the accountant cannot prepare a payroll package"
as "$PAYR_TOKEN" POST $FS/generate '{"kind":2,"year":1405,"season":3}'; no "outbox: payroll cannot prepare a VAT package"
as "$FIN_TOKEN" POST $FS/generate '{"kind":2,"year":1405,"season":3}'; check "outbox: VAT season package generated with Summary and Parties tables" '.success and .data.status == 0 and ([.data.tables[].name]|index("Summary")) != null and ([.data.tables[].name]|index("Parties")) != null'
OP3=$(val .data.id)
as "$FIN_TOKEN" POST $FS/action "{\"id\":$OP3,\"action\":\"approve\"}"; no "outbox: only the finance manager approves a VAT package"
as "$FM_TOKEN" POST $FS/action "{\"id\":$OP3,\"action\":\"approve\"}"; check "outbox: VAT package approved" '.success and .data.status == 1'
as "$FIN_TOKEN" POST $FS/generate '{"kind":1,"year":1405,"month":1}'; no "outbox: a period without data gives no empty package"
as "$FIN_TOKEN" GET "$FS/packages?PageSize=0"; check "outbox: the accountant sees the VAT package and no payroll package" '.success and ([.data.data[]|select(.id == '$OP3')]|length) == 1 and ([.data.data[]|select(.kind == 3 or .kind == 4)]|length) == 0'
as "$PAYR_TOKEN" GET $FS/summary; check "outbox: summary counters returned" '.success and (.data.pending|type) == "number" and (.data.readyToSend|type) == "number"'
as "$WH_TOKEN" GET $FS/summary; [ "$CODE" = "403" ] && ok "outbox: storekeeper has no access (403)" || bad "outbox: storekeeper has no access (403) got $CODE"

# --- dashboards and set-up check
as "$CEO_TOKEN" GET finance/ceo-dashboard; check "dashboard: CEO finance block carries the Sepidar, assets and payroll counters" '.success and (.data.finance.sepidarPendingPackages|type) == "number" and (.data.finance.assetsCount|type) == "number" and (.data.finance.assetsNetBookValue|type) == "number" and (.data.finance.depreciationRunsWaiting|type) == "number" and (.data.finance.payrollRunsInProgress|type) == "number"'
as "$CEO_TOKEN" GET finance/ceo-dashboard; check "dashboard: two assets are on the books (one sold, one draft excluded)" '.data.finance.assetsCount >= 2'
as "$FM_TOKEN" GET $FG/setup-check; check "set-up check: the payroll module is complete for the year and the assets module still lists the unrated classes" '.success and ([.data.modules[]|select(.module == "Payroll" and .ok == true)]|length) == 1 and ([.data.modules[]|select(.module == "Assets" and .ok == false and (.missing|index("TNR")) == null)]|length) == 1'

TOKEN=$ADMIN_TOKEN

# ---------------------------------------------------------------- finance completeness (release F3, milestone 1)
TOKEN=$ADMIN_TOKEN
FE=finance/entities; FM2=finance/migration; FAU=finance/audit; FSX=finance/sepidar-export
user2 aud.ci 28 'Ci#Aud-User-2026' 09120000032 0012345632
AUD_TOKEN=$(login aud.ci 'Ci#Aud-User-2026')
[ -n "$FM2_TOKEN" ] && [ -n "$AUD_TOKEN" ] && ok "f3: login second finance manager and the auditor (role 28)" || bad "f3: login second finance manager and the auditor (role 28)"

# --- Sepidar migration: chart of accounts (preview, then commit)
ACC_BAD='{"rows":[{"code":"1019901","title":"صندوق قدیم","level":3},{"code":"8019902","title":"هزینه قدیم","level":3},{"code":"5039901","title":"سرمایه قدیم","level":3,"nature":"Credit"},{"code":"10101","title":"موجود","level":3},{"code":"","title":"بدون کد","level":3}]}'
as "$FIN_TOKEN" POST "$FM2/accounts" "$ACC_BAD"; no "f3 migration: the accountant cannot import the chart"
as "$FM_TOKEN" POST "$FM2/accounts" "$ACC_BAD"; check "f3 migration: account preview counts 3 new, 1 existing, 1 error and writes nothing" '.success and .data.dryRun == true and .data.committed == false and .data.total == 5 and .data.new == 3 and .data.existing == 1 and .data.errors == 1'
as "$FM_TOKEN" POST "$FM2/accounts?commit=true" "$ACC_BAD"; check "f3 migration: commit with an error row is not applied" '.success and .data.committed == false'
as "$FM_TOKEN" GET "$FG/accounts?PageSize=0&Contains=1019901"; check "f3 migration: nothing was created by the failed commit" '.success and ([.data.data[]|select(.code == "1019901")]|length) == 0'
ACC_OK=$(jq -c '.rows |= map(select(.code != ""))' <<< "$ACC_BAD")
as "$FM_TOKEN" POST "$FM2/accounts?commit=true" "$ACC_OK"; check "f3 migration: chart committed (3 new, 1 existing)" '.success and .data.committed == true and .data.new == 3 and .data.existing == 1 and .data.errors == 0'
as "$FM_TOKEN" POST "$FM2/accounts" "$ACC_OK"; check "f3 migration: a second preview reports the codes as mapped" '.success and .data.new == 0 and .data.mapped == 4'
as "$FM_TOKEN" GET "$FG/accounts?PageSize=0"; check "f3 migration: new accounts exist with the Sepidar code kept" '([.data.data[]|select(.code == "1019901" and .sepidarCode == "1019901")]|length) == 1 and ([.data.data[]|select(.code == "5039901")]|length) == 1'

# --- detail accounts: type guess with a review flag
DET='{"rows":[{"code":"SD-1","title":"بانک ملی - جاری"},{"code":"SD-2","title":"نمونه بدون نوع"},{"code":"SD-3","title":"مشتری یک","type":"Customer"}]}'
as "$FM_TOKEN" POST "$FM2/details" "$DET"; check "f3 migration: detail preview guesses Bank, flags one for review, keeps the given type" '.success and .data.new == 2 and .data.review == 1 and ([.data.rows[]|select(.code == "SD-1")][0].guessedType) == "Bank" and ([.data.rows[]|select(.code == "SD-2")][0].status) == "Review"'
as "$FM_TOKEN" POST "$FM2/details?commit=true" "$DET"; check "f3 migration: details committed" '.success and .data.committed == true'
as "$FM_TOKEN" POST "$FM2/details" "$DET"; check "f3 migration: a second detail preview shows all three as existing" '.success and .data.existing == 3 and .data.new == 0'

# --- opening balances of a fresh past year: unknown codes, unbalanced, then commit
as "$FM_TOKEN" POST $FG/years/ensure '{"date":"2020-06-01T00:00:00"}'; check "f3 migration: fiscal year 1399 ensured" '.success and .data.year == 1399'
Y99=$(val .data.id)
as "$FM_TOKEN" POST "$FM2/opening" "{\"fiscalYearId\":$Y99,\"rows\":[{\"accountCode\":\"1019901\",\"debit\":5000000},{\"accountCode\":\"9999999\",\"credit\":4000000}]}"
check "f3 migration: opening preview lists the unknown account and the imbalance" '.success and (.data.unknownAccounts|index("9999999")) != null and .data.balanced == false and .data.difference == 1000000 and .data.committed == false'
as "$FM_TOKEN" POST "$FM2/opening?commit=true" "{\"fiscalYearId\":$Y99,\"rows\":[{\"accountCode\":\"1019901\",\"debit\":5000000},{\"accountCode\":\"9999999\",\"credit\":4000000}]}"; check "f3 migration: opening with unknown account is not committed" '.success and .data.committed == false'
OPEN_OK="{\"fiscalYearId\":$Y99,\"rows\":[{\"accountCode\":\"1019901\",\"debit\":5000000},{\"accountCode\":\"5039901\",\"credit\":5000000}]}"
as "$FM_TOKEN" POST "$FM2/opening?commit=true" "$OPEN_OK"; check "f3 migration: balanced opening committed as one voucher" '.success and .data.committed == true and .data.balanced == true and .data.totalDebit == 5000000 and .data.totalCredit == 5000000 and .data.voucherId > 0'
OPV=$(val .data.voucherId)
as "$FM_TOKEN" POST "$FM2/opening" "$OPEN_OK"; check "f3 migration: a year takes only one opening" '.success and .data.alreadyHasOpening == true and (.data.errors|length) >= 1'
as "$FM_TOKEN" POST $FG/vouchers/action "{\"id\":$OPV,\"action\":\"finalize\"}"; no "f3 migration: the importer cannot finalize its own opening voucher"
as "$FM2_TOKEN" POST $FG/vouchers/action "{\"id\":$OPV,\"action\":\"finalize\"}"; check "f3 migration: the second finance manager finalizes it" '.success and .data.status == 2 and .data.totalAmount == 5000000'
as "$FM_TOKEN" GET "$FG/reports/trial-balance?FiscalYearId=$Y99"; check "f3 migration: the trial balance of 1399 equals the imported opening" '.success and .data.balanced == true and .data.totalDebit == 5000000 and .data.totalCredit == 5000000'

# --- imported historical year (read-only, periods closed) and its vouchers
IY='{"year":1398,"title":"1398 (سپیدار)","startDate":"2019-03-21T00:00:00","endDate":"2020-03-19T00:00:00"}'
as "$FIN_TOKEN" POST "$FM2/imported-years" "$IY"; no "f3 migration: only the finance manager creates an imported year"
as "$FM_TOKEN" POST "$FM2/imported-years" "$IY"; check "f3 migration: imported year 1398 has 12 closed periods" '.success and .data.isImported == true and (.data.periods|length) == 12 and ([.data.periods[]|select(.status == 2)]|length) == 12'
Y98=$(val .data.id); IP1=$(val '.data.periods[0].id'); IP2=$(val '.data.periods[1].id'); IP3=$(val '.data.periods[2].id')
HIST='{"rows":[{"number":1,"date":"2019-04-01T00:00:00","description":"سند تاریخی یک","accountCode":"1019901","debit":1000},{"number":1,"date":"2019-04-01T00:00:00","accountCode":"5039901","credit":1000},{"number":2,"date":"2019-05-01T00:00:00","description":"سند تاریخی دو","accountCode":"1019901","debit":2500},{"number":2,"date":"2019-05-01T00:00:00","accountCode":"5039901","credit":2500}]}'
as "$FM_TOKEN" POST "$FM2/vouchers/import" "$(jq -c ". + {fiscalYearId:$Y98,dryRun:true}" <<< "$HIST")"; check "f3 migration: historical preview 2 vouchers, 4 lines, 3500" '.success and .data.vouchers == 2 and .data.lines == 4 and .data.totalDebit == 3500 and .data.totalCredit == 3500 and (.data.errors|length) == 0 and .data.committed == false'
as "$FM_TOKEN" POST "$FM2/vouchers/import" "$(jq -c ". + {fiscalYearId:$Y98,dryRun:true} | .rows += [{number:3,date:\"2021-01-01T00:00:00\",accountCode:\"1019901\",debit:10},{number:3,date:\"2021-01-01T00:00:00\",accountCode:\"5039901\",credit:10}]" <<< "$HIST")"; check "f3 migration: a voucher dated outside the year is an error" '.success and (.data.errors|length) == 1'
as "$FM_TOKEN" POST "$FM2/vouchers/import" "$(jq -c ". + {fiscalYearId:$Y98,dryRun:false}" <<< "$HIST")"; check "f3 migration: historical vouchers committed" '.success and .data.committed == true and .data.vouchers == 2'
as "$FM_TOKEN" POST "$FM2/vouchers/import" "$(jq -c ". + {fiscalYearId:$Y98,dryRun:false}" <<< "$HIST")"; check "f3 migration: the same numbers cannot be imported twice" '.success and .data.committed == false and (.data.errors|length) >= 1'
as "$FM_TOKEN" GET "$FG/vouchers?FiscalYearId=$Y98&PageSize=0"; check "f3 migration: imported vouchers are final, imported and keep their numbers" '(.data.data|length) == 2 and ([.data.data[]|select(.status == 2 and .isImported == true)]|length) == 2 and ([.data.data[].number]|sort) == [1,2]'
HV1=$(val '.data.data[]|select(.number == 1)|.id')
as "$FM_TOKEN" POST $FG/vouchers/action "{\"id\":$HV1,\"action\":\"reverse\"}"; no "f3 migration: an imported voucher cannot be reversed or edited"
as "$FM_TOKEN" GET "$FG/reports/trial-balance?FiscalYearId=$Y98"; check "f3 migration: the imported year's trial balance is exactly the import" '.success and .data.totalDebit == 3500 and .data.totalCredit == 3500'
TB98=$(val .data.totalDebit)

# --- parallel run
as "$FM_TOKEN" GET "$FM2/parallel-run/status"; check "f3 parallel run: nothing signed off yet" '.success and .data.requiredMonths == 3 and .data.consecutiveCleanPeriods == 0 and .data.goLiveReady == false'
as "$FM_TOKEN" POST "$FM2/parallel-run/compare" "{\"periodId\":$IP1,\"rows\":[{\"accountCode\":\"1019901\",\"debit\":1000},{\"accountCode\":\"5039901\",\"credit\":1000}]}"; check "f3 parallel run: period 1 matches both rows exactly" '.success and .data.rows == 2 and .data.matched == 2 and .data.pmisOnly == 0 and .data.sepidarOnly == 0 and .data.differs == 0 and .data.unexplained == 0'
RUN1=$(val .data.id)
as "$FIN_TOKEN" POST "$FM2/parallel-run/$RUN1/sign-off" '{"note":"x"}'; no "f3 parallel run: only the finance manager signs off"
as "$FM_TOKEN" POST "$FM2/parallel-run/$RUN1/sign-off" '{"note":"بدون مغایرت"}'; check "f3 parallel run: period 1 signed off" '.success and .data.signedOff == true and .data.unexplainedAtSignOff == 0'
as "$FM_TOKEN" GET "$FM2/parallel-run/status"; check "f3 parallel run: one clean period is not enough" '.success and .data.consecutiveCleanPeriods == 1 and .data.goLiveReady == false'
as "$FM_TOKEN" POST "$FM2/parallel-run/compare" "{\"periodId\":$IP2,\"rows\":[{\"accountCode\":\"1019901\",\"debit\":2500},{\"accountCode\":\"5039901\",\"credit\":2400},{\"accountCode\":\"8019902\",\"debit\":100}]}"; check "f3 parallel run: period 2 gives one match, one amount difference and one Sepidar-only row" '.success and .data.rows == 3 and .data.matched == 1 and .data.differs == 1 and .data.sepidarOnly == 1 and .data.pmisOnly == 0 and .data.unexplained == 2'
RUN2=$(val .data.id); DIF1=$(val '.data.differences[0].id'); DIF2=$(val '.data.differences[1].id')
as "$FM_TOKEN" POST "$FM2/parallel-run/$RUN2/explain" "{\"differenceId\":$DIF1,\"explanation\":\"کسری در سپیدار\"}"; check "f3 parallel run: explaining one difference leaves one unexplained" '.success and .data.unexplained == 1'
as "$FM_TOKEN" POST "$FM2/parallel-run/$RUN2/explain" "{\"differenceId\":$DIF2,\"explanation\":\"سند هنوز وارد نشده\"}"; check "f3 parallel run: all explained" '.success and .data.unexplained == 0'
as "$FM_TOKEN" POST "$FM2/parallel-run/$RUN2/sign-off" '{"note":"توضیح داده شد"}'; check "f3 parallel run: period 2 signed off" '.success and .data.signedOff == true'
as "$FM_TOKEN" POST "$FM2/parallel-run/compare" "{\"periodId\":$IP3,\"rows\":[]}"; check "f3 parallel run: an empty upload for an empty period is clean" '.success and .data.rows == 0 and .data.unexplained == 0'
RUN3=$(val .data.id)
as "$FM_TOKEN" POST "$FM2/parallel-run/$RUN3/sign-off" '{}'; check "f3 parallel run: period 3 signed off" '.success and .data.signedOff == true'
as "$FM_TOKEN" GET "$FM2/parallel-run/status"; check "f3 parallel run: three consecutive clean periods make the go-live ready" '.success and .data.consecutiveCleanPeriods == 3 and .data.goLiveReady == true'
as "$FM_TOKEN" POST $FG/settings '{"key":"ParallelRun.RequiredMonths","value":"4"}'; check "f3 parallel run: required months is a setting" '.success and .data.value == "4"'
as "$FM_TOKEN" GET "$FM2/parallel-run/status"; check "f3 parallel run: four months are now required" '.data.requiredMonths == 4 and .data.goLiveReady == false'
as "$FM_TOKEN" POST $FG/settings '{"key":"ParallelRun.RequiredMonths","value":"3"}'
as "$FIN_TOKEN" POST $FG/settings '{"key":"ParallelRun.RequiredMonths","value":"1"}'; no "f3 settings: only the finance manager changes settings"
as "$FM_TOKEN" POST $FG/settings '{"key":"Nonexistent.Key","value":"1"}'; no "f3 settings: unknown keys are refused"

# --- scope: one legal entity, rials only (Finance:MultiEntity and Finance:ForeignCurrency are off by default)
FXA=finance/fx; FLN=finance/loans
as "$FM_TOKEN" POST $FE '{"code":"DE1","name":"شرکت دوم","jurisdiction":"DE","baseCurrency":"EUR"}'; nomsg "scope: a second legal entity is refused while Finance:MultiEntity is off" "فقط ریال"
as "$FM_TOKEN" POST $FE/pairs '{"title":"بین شرکتی","entityAId":1,"entityBId":2,"accountACode":"1019901","accountBCode":"5039901"}'; nomsg "scope: an intercompany pair is refused" "فقط ریال"
as "$FIN_TOKEN" POST $FE/intercompany/post "{\"pairId\":1,\"date\":\"${TODAY}T00:00:00\",\"amount\":4000000,\"rate\":1,\"chargerEntityId\":1,\"counterAccountACode\":\"60102\",\"counterAccountBCode\":\"80108\"}"; nomsg "scope: an intercompany posting is refused" "فقط ریال"
as "$FM_TOKEN" POST $FXA/currencies '{"code":"USD","title":"دلار آمریکا","decimals":2,"isBase":false}'; nomsg "scope: a foreign currency cannot be defined while Finance:ForeignCurrency is off" "فقط ریال"
as "$FIN_TOKEN" POST $FXA/rates '{"currencyCode":"USD","date":"2025-06-20T00:00:00","average":650000,"rateType":1,"source":1}'; nomsg "scope: an exchange rate is refused" "فقط ریال"
as "$FM_TOKEN" POST $FXA/revaluation "{\"periodId\":$IP1,\"dryRun\":true}"; nomsg "scope: the foreign-currency revaluation is refused" "فقط ریال"
as "$FM_TOKEN" POST $FXA/settlement '{"date":"2025-07-25T00:00:00","direction":"Payable","currencyCode":"USD","currencyAmount":400,"bookRate":600000,"settleRate":655000,"accountCode":"30101","counterAccountCode":"10102"}'; nomsg "scope: a foreign-currency settlement is refused" "فقط ریال"
as "$TR_TOKEN" POST $FT/bank-accounts '{"title":"بانک ارزی","bankName":"ملت","accountNumber":"100324200007","iban":"IR590170000000100324200007","currencyCode":"USD"}'; nomsg "scope: a foreign-currency bank account is refused" "فقط ریال"
as "$TR_TOKEN" POST $FLN '{"bankName":"بانک","amount":1000,"currencyCode":"USD","startDate":"2026-10-01T00:00:00","ratePercent":0,"scheduleKind":3,"installmentCount":1}'; nomsg "scope: a foreign-currency loan is refused" "فقط ریال"
as "$TR_TOKEN" POST $FXA/letters-of-credit "{\"providerId\":$PA,\"bankAccountId\":$BA1,\"currencyCode\":\"USD\",\"amount\":200,\"openDate\":\"2025-06-01T00:00:00\",\"expiryDate\":\"2025-06-30T00:00:00\",\"marginPercent\":20}"; nomsg "scope: a foreign-currency letter of credit is refused" "فقط ریال"
as "$FIN_TOKEN" POST $FG/vouchers "{\"date\":\"${TODAY}T00:00:00\",\"lines\":[{\"accountCode\":\"10101\",\"debit\":600000,\"currencyCode\":\"USD\",\"currencyAmount\":1,\"rate\":600000},{\"accountCode\":\"10102\",\"credit\":600000}]}"; nomsg "scope: a voucher line with a currency is refused" "فقط ریال"
as "$FM_TOKEN" GET "$FE"; check "scope: the main company is the only legal entity" '.success and (.data|length) == 1 and .data[0].id == 1'
ENT=$(jq -c '.data[0] | {id: .id, code: .code, name: .name, jurisdiction: .jurisdiction, baseCurrency: .baseCurrency}' "$B")
as "$FM_TOKEN" POST $FE "$(jq -c '.baseCurrency = "EUR"' <<< "$ENT")"; nomsg "r3 m2: the base currency of the main company cannot be changed to EUR while Finance:ForeignCurrency is off" "فقط ریال"

# --- audit trail
as "$FM_TOKEN" GET "$FAU?EntityType=FnVoucher&EntityId=$V1&PageSize=0"; check "f3 audit: finalizing the first manual voucher left before and after values and the user" '.success and ([.data.data[]|select(.action == "Modified" and (.beforeJson|tostring|contains("Checked")) and (.afterJson|tostring|contains("Final")) and (.userName|length) > 0)]|length) >= 1'
check "f3 audit: the finalisation of the first manual voucher is in the trail exactly once (a save wrote every audit row twice)" '.success and ([.data.data[]|select(.action == "Modified" and (.beforeJson|tostring|contains("Checked")) and (.afterJson|tostring|contains("Final")))]|length) == 1'
JE=$(EJ T001 آزمون یک 0499370899 IR950190000000200000000003)
as "$PAYR_TOKEN" POST $FP/employees "$(jq -c ". + {id:$PE1}" <<< "$JE")"; check "f3 audit: the employee's bank account is changed" '.success'
as "$FM_TOKEN" GET "$FAU?EntityType=FnEmployee&EntityId=$PE1&PageSize=0"; check "f3 audit: the IBAN change is recorded masked (last four digits only)" '.success and ([.data.data[]|select((.beforeJson|tostring|contains("****0001")) and (.afterJson|tostring|contains("****0003")) and ((.afterJson|tostring|contains("IR9501900")|not)))]|length) >= 1'
as "$AUD_TOKEN" GET "$FAU?PageSize=10"; check "f3 audit: the auditor reads the trail" '.success'
as "$AUD_TOKEN" GET "$FG/vouchers?PageSize=5"; check "f3 audit: the auditor reads vouchers" '.success'
as "$AUD_TOKEN" GET "$FG/reports/trial-balance?FiscalYearId=$Y98"; check "f3 audit: the auditor reads reports" '.success and .data.totalDebit == 3500'
as "$AUD_TOKEN" POST $FG/vouchers "{\"date\":\"${TODAY}T00:00:00\",\"lines\":[{\"accountCode\":\"80108\",\"debit\":10},{\"accountCode\":\"10101\",\"credit\":10}]}"; [ "$CODE" = "403" ] || refused && ok "f3 audit: the auditor cannot write vouchers" || bad "f3 audit: the auditor cannot write vouchers"
as "$AUD_TOKEN" GET $FP/employees; [ "$CODE" = "403" ] && ok "f3 audit: the auditor cannot read payroll employees (403)" || bad "f3 audit: the auditor cannot read payroll employees (403) got $CODE"
as "$AUD_TOKEN" GET $FP/payslips; [ "$CODE" = "403" ] && ok "f3 audit: the auditor cannot read payslips (403)" || bad "f3 audit: the auditor cannot read payslips (403) got $CODE"
as "$AUD_TOKEN" GET $FP/runs; [ "$CODE" = "403" ] && ok "f3 audit: the auditor cannot read payroll runs (403)" || bad "f3 audit: the auditor cannot read payroll runs (403) got $CODE"
as "$FIN_TOKEN" GET "$FAU?PageSize=10"; [ "$CODE" = "403" ] && ok "f3 audit: the accountant has no access to the trail (403)" || bad "f3 audit: the accountant has no access to the trail (403) got $CODE"

# --- period-close checklist
# a sandbox period of the main company (fiscal year 1396) holds one final and one draft voucher for the close-checklist and export tests
as "$FM_TOKEN" POST $FG/years/ensure '{"date":"2017-06-01T00:00:00"}'; check "f3 close: a sandbox fiscal year (1396) of the main company with 12 periods" '.success and .data.year == 1396 and (.data.periods|length) == 12'
PEND2=$(jq -r '.data.periods[]|select((.startDate[0:10] <= "2017-06-01") and (.endDate[0:10] >= "2017-06-01"))|.id' "$B")
as "$FIN_TOKEN" POST $FG/vouchers '{"date":"2017-06-01T00:00:00","description":"سند آزمایشی نهایی","lines":[{"accountCode":"80108","debit":7000},{"accountCode":"10101","credit":7000}]}'; check "f3 close: a voucher in the sandbox period" '.success and .data.id > 0'
EV1=$(val .data.id)
as "$FIN2_TOKEN" POST $FG/vouchers/action "{\"id\":$EV1,\"action\":\"check\"}"
as "$FM_TOKEN" POST $FG/vouchers/action "{\"id\":$EV1,\"action\":\"finalize\"}"; check "f3 close: the sandbox voucher is final" '.success and .data.status == 2'
as "$FIN_TOKEN" POST $FG/vouchers '{"date":"2017-06-01T00:00:00","description":"پیش‌نویس باز","lines":[{"accountCode":"80108","debit":300},{"accountCode":"10101","credit":300}]}'
as "$FM_TOKEN" GET "$FG/close-checklist/$PEND2"; check "f3 close: an open draft makes the item red, the lock is still free while the checklist is not enforced" '.success and ([.data.items[]|select(.key == "NonFinalVouchers")][0].status) == "Red" and ([.data.items[]|select(.key == "NonFinalVouchers")][0].count) == 1 and .data.enforced == false and .data.canLock == true and ([.data.items[]|select(.key == "Intercompany")][0].status) == "NotApplicable" and ([.data.items[]|select(.key == "FxRevaluation")][0].status) == "NotApplicable"'
as "$FM_TOKEN" POST $FG/settings '{"key":"CloseChecklist.Enforce","value":"1"}'; check "f3 close: enforcement switched on" '.success and .data.value == "1"'
as "$FM_TOKEN" GET "$FG/close-checklist/$PEND2"; check "f3 close: enforced, the period cannot be locked" '.data.enforced == true and .data.canLock == false and .data.redCount >= 1'
as "$FM_TOKEN" POST $FG/periods/action "{\"id\":$PEND2,\"action\":\"lock\"}"; nomsg "f3 close: the lock is refused while items are red" "فهرست کنترل"
as "$FIN_TOKEN" POST "$FG/close-checklist/$PEND2/override" '{"reason":"x"}'; no "f3 close: only the finance manager overrides"
as "$FM_TOKEN" POST "$FG/close-checklist/$PEND2/override" '{}'; no "f3 close: an override needs a reason"
as "$FM_TOKEN" POST "$FG/close-checklist/$PEND2/override" '{"reason":"پیش‌نویس به ماه بعد منتقل می‌شود"}'; check "f3 close: override with a reason" '.success and .data.overridden == true and .data.canLock == true and (.data.overrideReason|length) > 0'
as "$FM_TOKEN" POST $FG/periods/action "{\"id\":$PEND2,\"action\":\"lock\"}"; check "f3 close: the period locks after the override" '.success'
as "$FM_TOKEN" POST $FG/settings '{"key":"CloseChecklist.Enforce","value":"0"}'; check "f3 close: enforcement switched off again" '.success and .data.value == "0"'

# --- voucher controls: approval threshold above which the CEO approves
as "$FIN_TOKEN" POST $FG/approval-thresholds '{"minAmount":1500000}'; no "f3 controls: only the finance manager sets thresholds"
as "$FM_TOKEN" POST $FG/approval-thresholds '{"minAmount":1500000,"note":"آزمون"}'; check "f3 controls: threshold of 1,500,000 saved" '.success and .data.minAmount == 1500000 and .data.isActive == true'
THR=$(val .data.id)
as "$FIN_TOKEN" POST $FG/vouchers "{\"date\":\"${TODAY}T00:00:00\",\"description\":\"سند بالای حد\",\"lines\":[{\"accountCode\":\"80108\",\"debit\":2000000},{\"accountCode\":\"10101\",\"credit\":2000000}]}"; TV=$(val .data.id)
as "$FIN2_TOKEN" POST $FG/vouchers/action "{\"id\":$TV,\"action\":\"check\"}"
as "$FM_TOKEN" POST $FG/vouchers/action "{\"id\":$TV,\"action\":\"finalize\"}"; nomsg "f3 controls: a voucher above the threshold needs the CEO first" "مدیرعامل"
as "$FM_TOKEN" POST $FG/vouchers/action "{\"id\":$TV,\"action\":\"ceo-approve\"}"; no "f3 controls: the finance manager cannot give the CEO approval"
as "$CEO_TOKEN" POST $FG/vouchers/action "{\"id\":$TV,\"action\":\"ceo-approve\"}"; check "f3 controls: CEO approval recorded" '.success and (.data.ceoApprovedByUserId|type) == "number"'
as "$FM_TOKEN" POST $FG/vouchers/action "{\"id\":$TV,\"action\":\"finalize\"}"; check "f3 controls: the voucher finalizes after the CEO approval" '.success and .data.status == 2'
as "$FM_TOKEN" POST $FG/approval-thresholds "{\"id\":$THR,\"minAmount\":1500000,\"isActive\":false}"; check "f3 controls: threshold deactivated" '.success and .data.isActive == false'

# --- attachment rule
as "$FM_TOKEN" POST $FG/attachment-rules '{"accountCode":"1019901","note":"سند این حساب پیوست می‌خواهد"}'; check "f3 controls: attachment rule on account 1019901" '.success and .data.accountCode == "1019901"'
RULE=$(val .data.id)
as "$FIN_TOKEN" POST $FG/vouchers "{\"date\":\"${TODAY}T00:00:00\",\"description\":\"نیازمند پیوست\",\"lines\":[{\"accountCode\":\"1019901\",\"debit\":1000},{\"accountCode\":\"5039901\",\"credit\":1000}]}"; AV=$(val .data.id)
as "$FIN2_TOKEN" POST $FG/vouchers/action "{\"id\":$AV,\"action\":\"check\"}"; nomsg "f3 controls: a voucher without the required attachment cannot be checked" "پیوست"
as "$FIN_TOKEN" POST $FG/attachments "{\"voucherId\":$AV,\"documentId\":\"6f1c2a3e-8c1d-4a55-9d3e-0a1b2c3d4e5f\",\"title\":\"فاکتور\"}"; check "f3 controls: attachment added" '.success and .data.id > 0'
as "$FIN2_TOKEN" POST $FG/vouchers/action "{\"id\":$AV,\"action\":\"check\"}"; check "f3 controls: checked once the attachment exists" '.success and .data.status == 1'
as "$FM_TOKEN" GET "$FG/vouchers/$AV"; check "f3 controls: the voucher shows its attachment count" '.success and .data.attachmentCount == 1'
as "$FM_TOKEN" POST $FG/attachment-rules "{\"id\":$RULE,\"accountCode\":\"1019901\",\"isActive\":false}"; check "f3 controls: rule deactivated" '.success and .data.isActive == false'

# --- copy, template vouchers, mass entry
as "$FIN_TOKEN" POST $FG/vouchers/copy "{\"id\":$V1}"; check "f3 controls: copy of a final voucher is a new draft with the same amounts" '.success and .data.status == 0 and .data.number == null and .data.totalAmount == 1000000 and .data.id != '$V1' and (.data.lines|length) == 2'
CPV=$(val .data.id)
as "$FIN_TOKEN" POST $FG/templates '{"title":"اجاره ماهانه","dayOfMonth":5,"lines":[{"accountCode":"80108","debit":300000},{"accountCode":"10101","credit":300000}]}'; check "f3 controls: voucher template saved" '.success and (.data.lines|length) == 2'
PCUR=$(as "$FM_TOKEN" GET "$FG/years" >/dev/null; jq -r --arg t "$TODAY" '.data[]|select(.legalEntityId == 1 and .isImported == false)|.periods[]|select((.startDate[0:10] <= $t) and (.endDate[0:10] >= $t))|.id' "$B" | head -n1)
as "$FIN_TOKEN" POST $FG/templates/generate "{\"periodId\":$PCUR}"; check "f3 controls: the template is drafted once for the period" '.success and .data.drafted == 1 and .data.skipped == 0 and (.data.voucherIds|length) == 1'
TPV=$(val '.data.voucherIds[0]')
as "$FIN_TOKEN" POST $FG/templates/generate "{\"periodId\":$PCUR}"; check "f3 controls: generating again drafts nothing" '.success and .data.drafted == 0 and .data.skipped == 1'
as "$FIN_TOKEN" GET "$FG/vouchers/$TPV"; check "f3 controls: the drafted voucher is a draft dated on day 5 of the Persian month" '.success and .data.status == 0 and .data.totalAmount == 300000'
MASS="{\"rows\":[{\"group\":\"g1\",\"date\":\"${TODAY}T00:00:00\",\"description\":\"گروه یک\",\"accountCode\":\"80108\",\"debit\":100},{\"group\":\"g1\",\"date\":\"${TODAY}T00:00:00\",\"accountCode\":\"10101\",\"credit\":100},{\"group\":\"g2\",\"date\":\"${TODAY}T00:00:00\",\"description\":\"گروه دو\",\"accountCode\":\"80108\",\"debit\":200},{\"group\":\"g2\",\"date\":\"${TODAY}T00:00:00\",\"accountCode\":\"10101\",\"credit\":200}]}"
as "$FIN_TOKEN" POST $FG/vouchers/mass-import "$(jq -c '. + {dryRun:true} | .rows += [{group:"g3",date:"'${TODAY}'T00:00:00",accountCode:"7777777",debit:5},{group:"g3",date:"'${TODAY}'T00:00:00",accountCode:"10101",credit:5}]' <<< "$MASS")"; check "f3 controls: mass entry preview counts 2 good vouchers and 1 error and creates nothing" '.success and .data.dryRun == true and .data.vouchers == 2 and .data.lines == 4 and (.data.errors|length) == 1 and (.data.voucherIds|length) == 0'
as "$FIN_TOKEN" POST $FG/vouchers/mass-import "$(jq -c '. + {dryRun:false}' <<< "$MASS")"; check "f3 controls: mass entry creates 2 drafts" '.success and .data.vouchers == 2 and (.data.voucherIds|length) == 2 and (.data.errors|length) == 0'

# --- full voucher export for Sepidar (fallback path; off by default)
as "$FM_TOKEN" GET $FSX/status; check "f3 export: the full export is off by default" '.success and .data == false'
as "$FM_TOKEN" POST $FSX/export "{\"periodId\":$IP1}"; nomsg "f3 export: exporting while the switch is off is refused" "غیرفعال"
as "$FM_TOKEN" GET $FSX/layout; check "f3 export: default layout has the 9 standard columns active" '.success and ([.data[]|select(.isActive)]|length) == 9 and .data[0].key == "VoucherNo"'
as "$FM_TOKEN" GET "$FSX/account-mapping?onlyUnmapped=true"; check "f3 export: mapping screen lists the accounts without a Sepidar code" '.success and ([.data[]|select(.code == "80108")]|length) == 1 and ([.data[]|select(.code == "1019901")]|length) == 0'
as "$FM_TOKEN" POST $FG/settings '{"key":"Finance:SepidarFullExport","value":"1"}'; check "f3 export: switched on" '.success and .data.value == "1"'
as "$FM_TOKEN" GET "$FSX/preview/$PEND2"; check "f3 export: unmapped accounts of the locked sandbox period are listed as errors" '.success and (.data.unmappedAccounts|index("80108")) != null and (.data.errors|length) >= 2'
as "$FM_TOKEN" POST $FSX/export "{\"periodId\":$PEND2}"; no "f3 export: a period with unmapped accounts is not exported"
as "$FM_TOKEN" POST $FSX/account-mapping '{"code":"80108","sepidarCode":"9801"}'; check "f3 export: account mapped" '.success and .data.mapped == true'
as "$FM_TOKEN" GET "$FSX/preview/$IP1"; check "f3 export: preview of imported period 1 is clean, 1 voucher and 2 rows" '.success and .data.enabled == true and .data.vouchers == 1 and .data.rows == 2 and .data.totalDebit == 1000 and .data.totalCredit == 1000 and (.data.errors|length) == 0 and .data.alreadyExported == false'
as "$FIN_TOKEN" POST $FSX/export "{\"periodId\":$IP1}"; no "f3 export: only the finance manager exports"
as "$FM_TOKEN" POST $FSX/export "{\"periodId\":$IP1}"; check "f3 export: period 1 exported with a hash" '.success and .data.rows == 2 and .data.totalDebit == 1000 and (.data.sha256|length) == 64 and .data.status == 1 and (.data.number|startswith("EXP-"))'
EX1=$(val .data.id); EXH1=$(val .data.sha256)
as "$FM_TOKEN" POST $FSX/export "{\"periodId\":$IP1}"; nomsg "f3 export: a second export of the same period is refused" "علت جایگزینی"
as "$FM_TOKEN" POST $FSX/layout '{"columns":[{"key":"VoucherNo","title":"No.","sortOrder":1,"isActive":true},{"key":"Date","title":"تاریخ","sortOrder":2,"isActive":true},{"key":"AccountCode","title":"کد حساب","sortOrder":3,"isActive":true},{"key":"Debit","title":"بدهکار","sortOrder":4,"isActive":true},{"key":"Credit","title":"بستانکار","sortOrder":5,"isActive":true}]}'; check "f3 export: the column layout is configurable" '.success and ([.data[]|select(.isActive)]|length) == 5'
as "$FIN_TOKEN" POST $FSX/layout '{"columns":[{"key":"Debit","title":"x","sortOrder":1,"isActive":true}]}'; no "f3 export: only the finance manager changes the layout"
as "$FM_TOKEN" POST $FSX/export "{\"periodId\":$IP1,\"supersedeReason\":\"تغییر قالب ستون‌ها\"}"; check "f3 export: superseding with a reason creates a second export" '.success and .data.id != '$EX1' and .data.status == 1'
EX1B=$(val .data.id)
as "$FM_TOKEN" GET "$FSX/exports?periodId=$IP1"; check "f3 export: the first export is kept as superseded, the new one is active" '.success and ([.data[]|select(.status == 2 and .id == '$EX1' and .supersedeReason != null)]|length) == 1 and ([.data[]|select(.status == 1 and .id == '$EX1B')]|length) == 1'
as "$FM_TOKEN" POST $FSX/export "{\"periodId\":$IP2}"; check "f3 export: period 2 exported" '.success and .data.totalDebit == 2500'
EX2TOT=$(val .data.totalDebit)
[ "$((1000 + EX2TOT))" = "$TB98" ] && ok "f3 export: the exports of the closed periods add up exactly to the ledger of the year ($TB98)" || bad "f3 export: the exports of the closed periods add up exactly to the ledger of the year (got $((1000 + EX2TOT)) vs $TB98)"
XF="$B.csv"; curl -s -o "$XF" -H "Authorization: Bearer $FM_TOKEN" "$API/$FSX/exports/$EX1B/file"
[ "$(head -c 3 "$XF" | od -An -tx1 | tr -d ' ')" = "efbbbf" ] && ok "f3 export: the file is UTF-8 with BOM" || bad "f3 export: the file is UTF-8 with BOM"
[ "$(tail -c +4 "$XF" | sha256sum | cut -d' ' -f1)" = "$(as "$FM_TOKEN" GET "$FSX/exports?periodId=$IP1" >/dev/null; jq -r '.data[]|select(.id == '$EX1B')|.sha256' "$B")" ] && ok "f3 export: the file's SHA-256 equals the recorded hash" || bad "f3 export: the file's SHA-256 equals the recorded hash"
[ "$(tail -n +2 "$XF" | wc -l)" = "2" ] && head -n1 "$XF" | grep -q "No\." && ok "f3 export: the file has the configured header and exactly the two ledger rows" || bad "f3 export: the file has the configured header and exactly the two ledger rows"
grep -q "1019901,1000,0" "$XF" && grep -q "5039901,0,1000" "$XF" && ok "f3 export: debit and credit per account match the ledger" || bad "f3 export: debit and credit per account match the ledger"
grep -Eq '^1,13(97|98)/' "$XF" && ok "f3 export: dates are Persian (no Gregorian date in the file)" || bad "f3 export: dates are Persian (no Gregorian date in the file)"
rm -f "$XF"
as "$AUD_TOKEN" GET "$FSX/exports?periodId=$IP1"; check "f3 export: the auditor sees the export records" '.success and (.data|length) == 2'
as "$AUD_TOKEN" POST $FSX/export "{\"periodId\":$IP3}"; no "f3 export: the auditor cannot export"
as "$FM_TOKEN" POST $FSX/detail-mapping '{"code":"SD-1","sepidarCode":"D-1"}'; check "f3 export: detail mapped to a Sepidar code" '.success and .data.sepidarCode == "D-1"'
as "$FM_TOKEN" GET "$FSX/detail-mapping?onlyUnmapped=true"; check "f3 export: unmapped details exclude it" '.success and ([.data[]|select(.code == "SD-1")]|length) == 0 and ([.data[]|select(.code == "SD-2")]|length) == 1'
as "$FM_TOKEN" POST $FG/settings '{"key":"Finance:SepidarFullExport","value":"0"}'; check "f3 export: switched off again" '.success and .data.value == "0"'

TOKEN=$ADMIN_TOKEN

# ---------------------------------------------------------------- finance completeness (release F3, milestone 2): FX, budget, costing, deferrals
TOKEN=$ADMIN_TOKEN
FXA=finance/fx; FBU=finance/budgets; FCO=finance/costing; FDF=finance/deferrals; FRR=finance/receivables/revenue-recognition
fv() { # a manual voucher through the full cycle (prepared by the accountant, checked by the second accountant, finalised by the finance manager); sets FVID
  as "$FIN_TOKEN" POST $FG/vouchers "$1"; FVID=$(val .data.id)
  as "$FIN2_TOKEN" POST $FG/vouchers/action "{\"id\":$FVID,\"action\":\"check\"}"
  as "$FM_TOKEN" POST $FG/vouchers/action "{\"id\":$FVID,\"action\":\"finalize\"}"
  if jq -e '.success and .data.status == 2' "$B" >/dev/null 2>&1; then ok "f3 setup: voucher $FVID finalised"; else bad "f3 setup: voucher $FVID finalised ($(jq -r '.message // .errorMessage // empty' "$B" | head -c 200))"; fi
}
tbv() { as "$FM_TOKEN" GET "$FG/reports/trial-balance?FiscalYearId=$Y4&Level=2&IncludeDraft=true"; jq -r "([.data.rows[]|select(.code == \"$1\")][0].$2 // 0) | round" "$B"; }

# --- fiscal year 1404 for the exact-amount tests
as "$FM_TOKEN" POST $FG/years/ensure '{"date":"2025-06-01T00:00:00"}'; check "f3 m2: fiscal year 1404 ensured with 12 periods" '.success and .data.year == 1404 and (.data.periods|length) == 12'
Y4=$(val .data.id); P1=$(val '.data.periods[0].id'); P2=$(val '.data.periods[1].id'); P3=$(val '.data.periods[2].id'); P4=$(val '.data.periods[3].id'); P5=$(val '.data.periods[4].id'); P6=$(val '.data.periods[5].id')

# --- section 3: foreign currency
as "$FM_TOKEN" POST $FXA/currencies '{"code":"IRR","title":"ریال","decimals":0,"isBase":true}'; check "f3 fx: base currency IRR" '.success and .data.isBase == true'
as "$FIN_TOKEN" POST $FXA/currencies '{"code":"GBP","title":"پوند","decimals":2}'; no "f3 fx: only the finance manager defines currencies"
as "$FM_TOKEN" GET "$FG/details?Type=2&PageSize=0"; FXSUP=$(val '.data.data[0].code // empty'); [ -n "$FXSUP" ] && ok "f3 fx: a supplier detail exists ($FXSUP)" || bad "f3 fx: a supplier detail exists"
as "$FM_TOKEN" GET "$FG/details?Type=3&PageSize=0"; FXCUS=$(val '.data.data[0].code // empty'); [ -n "$FXCUS" ] && ok "f3 fx: a customer detail exists ($FXCUS)" || bad "f3 fx: a customer detail exists"

# --- section 5b: deferrals, accrual reversal, provisions
as "$FIN_TOKEN" POST $FDF '{"kind":1,"description":"بیمه سالانه پیش‌پرداخت","totalAmount":1000001,"startDate":"2025-03-21T00:00:00","endDate":"2025-08-22T00:00:00","balanceAccountCode":"10601","resultAccountCode":"80108"}'; check "f3 deferral: prepaid expense over 5 Persian months (Farvardin to Mordad)" '.success and .data.months == 5 and .data.remaining == 1000001'
DF1=$(val .data.id)
as "$FIN_TOKEN" POST $FDF '{"kind":2,"description":"درآمد انتقالی نگهداری","totalAmount":600000,"startDate":"2025-03-21T00:00:00","endDate":"2025-06-21T00:00:00","balanceAccountCode":"30303","resultAccountCode":"60102"}'; check "f3 deferral: deferred income over 3 months" '.success and .data.months == 3'
DF2=$(val .data.id)
as "$FIN_TOKEN" POST $FDF '{"kind":1,"description":"x","totalAmount":1000,"startDate":"2025-03-21T00:00:00","endDate":"2025-04-21T00:00:00","balanceAccountCode":"30303","resultAccountCode":"80108"}'; no "f3 deferral: prepaid expense must use an asset and an expense account"
as "$FIN_TOKEN" POST $FDF/run/$P1; check "f3 deferral: run for Farvardin drafts one entry per schedule (200,000 and 200,000)" '.success and (.data|length) == 2 and ([.data[]|select(.deferralId == '$DF1')][0].amount) == 200000 and ([.data[]|select(.deferralId == '$DF2')][0].amount) == 200000'
E1=$(val '.data[]|select(.deferralId == '$DF1')|.id'); E2=$(val '.data[]|select(.deferralId == '$DF2')|.id')
as "$FIN_TOKEN" POST $FDF/run/$P1; check "f3 deferral: the run is idempotent" '.success and (.data|length) == 0'
as "$FIN_TOKEN" POST $FDF/entries/$E1/approve; no "f3 deferral: the preparer cannot approve the entry"
as "$FIN2_TOKEN" POST $FDF/entries/$E1/approve; check "f3 deferral: the second person approves; a voucher is posted" '.success and .data.status == 1 and .data.voucherId > 0'
as "$FIN2_TOKEN" POST $FDF/entries/$E2/approve; check "f3 deferral: the deferred-income entry is approved" '.success and .data.status == 1'
as "$FIN_TOKEN" POST $FDF/run/$P3; check "f3 deferral: Khordad is month 3: 200,001 (cumulative rounding lands exactly) and 200,000 for the income schedule" '.success and ([.data[]|select(.deferralId == '$DF1')][0]|(.sequence == 3 and .amount == 200001)) and ([.data[]|select(.deferralId == '$DF2')][0].amount) == 200000'
E3=$(val '.data[]|select(.deferralId == '$DF1')|.id')
as "$FIN2_TOKEN" POST $FDF/entries/$E3/approve; check "f3 deferral: Khordad entry approved" '.success and .data.status == 1 and .data.amount == 200001'
as "$FM_TOKEN" GET $FDF/$DF1; check "f3 deferral: amortised 400,001, remaining 600,000 after two months" '.success and .data.amortised == 400001 and .data.remaining == 600000 and ([.data.entries[]|select(.status == 1)]|length) == 2'
EXP4=$(val .data.entries[1].voucherId)
as "$FM_TOKEN" GET $FG/vouchers/$EXP4; check "f3 deferral: the entry voucher debits the expense 80108 and credits the prepaid 10601" '.success and .data.totalAmount == 200001 and ([.data.lines[]|select(.accountCode == "80108" and .debit == 200001)]|length) == 1 and ([.data.lines[]|select(.accountCode == "10601" and .credit == 200001)]|length) == 1'
fv "{\"date\":\"2025-06-10T00:00:00\",\"description\":\"هزینه تعهدی\",\"reverseNextPeriod\":true,\"lines\":[{\"accountCode\":\"80108\",\"debit\":3000000},{\"accountCode\":\"30208\",\"credit\":3000000}]}"
ACCV=$FVID
as "$FIN_TOKEN" POST $FDF/reversing/run/$P3; check "f3 accrual: one reversing voucher drafted" '.success and (.data.createdVoucherIds|length) == 1'
ACCR=$(val '.data.createdVoucherIds[0]')
as "$FM_TOKEN" GET $FG/vouchers/$ACCR; check "f3 accrual: the reversal is a draft dated day 1 of the next period with swapped sides" '.success and .data.status == 0 and .data.type == 5 and (.data.date|startswith("2025-06-22")) and .data.totalAmount == 3000000 and .data.reversesVoucherId == '$ACCV
as "$FM_TOKEN" GET $FG/vouchers/$ACCV; check "f3 accrual: the original points to its automatic reversal" '.success and .data.autoReversalVoucherId == '$ACCR
as "$FIN_TOKEN" POST $FDF/reversing/run/$P3; check "f3 accrual: running again creates nothing" '.success and (.data.createdVoucherIds|length) == 0'
as "$FIN_TOKEN" POST $FDF/provisions '{"kind":1,"description":"ذخیره گارانتی","expenseAccountCode":"80303","liabilityAccountCode":"30405","reviewDate":"2025-06-30T00:00:00"}'; check "f3 provision: warranty provision registered" '.success and .data.balance == 0 and (.data.number|startswith("PRV-"))'
PV1=$(val .data.id)
as "$FIN_TOKEN" POST $FDF/provisions/movement "{\"provisionId\":$PV1,\"date\":\"2025-06-15T00:00:00\",\"amount\":5000000,\"reason\":\"ذخیره سال\"}"; check "f3 provision: charge of 5,000,000" '.success and .data.balance == 5000000 and (.data.movements|length) == 1'
as "$FIN_TOKEN" POST $FDF/provisions/movement "{\"provisionId\":$PV1,\"date\":\"2025-06-16T00:00:00\",\"amount\":-1200000,\"reason\":\"مصرف برای تعمیر\",\"counterAccountCode\":\"10102\"}"; check "f3 provision: use of 1,200,000 leaves 3,800,000" '.success and .data.balance == 3800000 and (.data.movements|length) == 2'
as "$FIN_TOKEN" POST $FDF/provisions/movement "{\"provisionId\":$PV1,\"date\":\"2025-06-17T00:00:00\",\"amount\":-9000000,\"reason\":\"بیش از مانده\"}"; no "f3 provision: use above the balance is refused"
as "$FM_TOKEN" GET "$FDF/provisions?asOf=2025-07-01"; check "f3 provision: the review date has passed, so a reminder is flagged" '.success and ([.data[]|select(.id == '$PV1' and .reviewDue == true)]|length) == 1'
as "$AUD_TOKEN" POST $FDF/provisions '{"kind":1,"description":"x","expenseAccountCode":"80303","liabilityAccountCode":"30405"}'; no "f3 provision: the auditor cannot write"

# --- section 5: overhead step-down allocation and absorption (3 cost centres)
call POST finance/cost-centers '{"code":"CC-IND","title":"مرکز غیرمستقیم آزمون","isProduction":false}'; check "f3 costing: indirect cost centre" '.success'
CCI=$(val .data.id)
call POST finance/cost-centers '{"code":"CC-P1","title":"مرکز تولید ۱ آزمون","isProduction":true}'; check "f3 costing: production cost centre 1" '.success'
CCP1=$(val .data.id)
call POST finance/cost-centers '{"code":"CC-P2","title":"مرکز تولید ۲ آزمون","isProduction":true}'; check "f3 costing: production cost centre 2" '.success'
CCP2=$(val .data.id)
fv "{\"date\":\"2025-05-01T00:00:00\",\"description\":\"هزینه مراکز\",\"lines\":[{\"accountCode\":\"70206\",\"debit\":1000000,\"costCenterId\":$CCI},{\"accountCode\":\"70206\",\"debit\":200000,\"costCenterId\":$CCP1},{\"accountCode\":\"10101\",\"credit\":1200000}]}"
as "$FM_TOKEN" POST $FCO/allocation-keys '{"fromCostCenterCode":"CC-IND","toCostCenterCode":"CC-P1","basis":"Percent","value":60,"validFrom":"2025-03-21T00:00:00","sequence":1,"source":"آزمون"}'; check "f3 costing: allocation key IND to P1 = 60" '.success'
as "$FM_TOKEN" POST $FCO/allocation-keys '{"fromCostCenterCode":"CC-IND","toCostCenterCode":"CC-P2","basis":"Percent","value":40,"validFrom":"2025-03-21T00:00:00","sequence":1,"source":"آزمون"}'; check "f3 costing: allocation key IND to P2 = 40" '.success'
as "$FM_TOKEN" POST $FCO/allocation-keys '{"fromCostCenterCode":"CC-P1","toCostCenterCode":"CC-P1","basis":"Percent","value":1,"validFrom":"2025-03-21T00:00:00","sequence":2}'; no "f3 costing: a key cannot allocate a centre to itself"
as "$FM_TOKEN" POST $FCO/overhead-rates '{"costCenterCode":"CC-P1","base":1,"rate":5000,"validFrom":"2025-03-21T00:00:00","source":"آزمون","verify":true}'; check "f3 costing: overhead rate P1 = 5,000 per labour hour (input, flagged for verification)" '.success and .data.verify == true'
as "$FM_TOKEN" POST $FCO/overhead-rates '{"costCenterCode":"CC-P2","base":2,"rate":1500,"validFrom":"2025-03-21T00:00:00","source":"آزمون","verify":true}'; check "f3 costing: overhead rate P2 = 1,500 per machine hour" '.success'
as "$AUD_TOKEN" POST $FCO/overhead-rates '{"costCenterCode":"CC-P2","base":2,"rate":1,"validFrom":"2025-03-21T00:00:00"}'; no "f3 costing: the auditor cannot change rates"
as "$FIN_TOKEN" POST $FCO/workorder-bases "{\"workOrderId\":$WO2,\"costCenterCode\":\"CC-P1\",\"periodId\":$P2,\"base\":1,\"quantity\":100}"; check "f3 costing: 100 labour hours of work order 2 in P1" '.success'
as "$FIN_TOKEN" POST $FCO/workorder-bases "{\"workOrderId\":$WO1,\"costCenterCode\":\"CC-P2\",\"periodId\":$P2,\"base\":2,\"quantity\":200}"; check "f3 costing: 200 machine hours of work order 1 in P2" '.success'
as "$FM_TOKEN" POST $FCO/allocate/$P2 '{"dryRun":true}'; check "f3 costing: dry run: 1,000,000 allocated 600,000/400,000, 800,000 absorbed, under-absorbed 400,000" '.success and .data.dryRun == true and .data.totalAllocated == 1000000 and .data.totalAbsorbed == 800000 and .data.variance == 400000 and (.data.steps|length) == 2 and ([.data.steps[]|select(.to == "CC-P1")][0].amount) == 600000 and ([.data.steps[]|select(.to == "CC-P2")][0].amount) == 400000 and ([.data.centers[]|select(.costCenterCode == "CC-P1")][0]|(.actual == 800000 and .absorbed == 500000 and .variance == 300000)) and ([.data.centers[]|select(.costCenterCode == "CC-P2")][0]|(.actual == 400000 and .absorbed == 300000 and .variance == 100000))'
as "$AUD_TOKEN" POST $FCO/allocate/$P2 '{"dryRun":false}'; no "f3 costing: the auditor cannot run the allocation"
as "$FIN_TOKEN" POST $FCO/allocate/$P2 '{"dryRun":false}'; check "f3 costing: allocation run posts three vouchers" '.success and .data.allocationVoucherId > 0 and .data.absorptionVoucherId > 0 and .data.varianceVoucherId > 0 and .data.variance == 400000'
as "$FIN_TOKEN" POST $FCO/allocate/$P2 '{"dryRun":false}'; nomsg "f3 costing: a period is allocated only once" "قبلاً"
[ "$(tbv 70111 closingDebit)" = "400000" ] && ok "f3 costing: the variance account 70111 holds exactly the under-absorbed 400,000" || bad "f3 costing: the variance account 70111 holds exactly the under-absorbed 400,000 (got $(tbv 70111 closingDebit))"
[ "$(tbv 10520 closingDebit)" = "800000" ] && ok "f3 costing: work in progress 10520 received exactly the absorbed 800,000" || bad "f3 costing: work in progress 10520 received exactly the absorbed 800,000 (got $(tbv 10520 closingDebit))"
[ "$(tbv 70206 closingDebit)" = "0" ] && [ "$(tbv 70110 closingCredit)" = "0" ] && ok "f3 costing: the overhead pool 70206 and the absorbed account 70110 are cleared" || bad "f3 costing: the overhead pool 70206 and the absorbed account 70110 are cleared"
as "$FM_TOKEN" GET $FCO/workorders/$WO2/cost-sheet; check "f3 costing: cost sheet of work order 2 shows the absorbed overhead 500,000" '.success and .data.overhead == 500000 and .data.totalCost == 500000 and .data.wipBalance == 500000'

# --- section 5: work-order close-out
as "$FIN_TOKEN" POST $FCO/workorders/$WO2/close '{}'; nomsg "f3 close-out: only a finished work order is closed" "انجام"
call PUT WorkOrders/change-condition "{\"workOrderId\":$WO2,\"newCondition\":2}"; check "f3 close-out: work order 2 set to Done" '.success'
fv "{\"date\":\"2025-05-10T00:00:00\",\"description\":\"مواد و دستمزد دستور کار\",\"lines\":[{\"accountCode\":\"10520\",\"debit\":800000,\"workOrderId\":$WO2},{\"accountCode\":\"70102\",\"debit\":200000,\"workOrderId\":$WO2},{\"accountCode\":\"10101\",\"credit\":1000000}]}"
as "$FIN_TOKEN" POST $FCO/workorders/$WO2/close '{"quantity":1,"date":"2025-05-30T00:00:00"}'; check "f3 close-out: sheet 500,000 to finished goods, 1,000,000 not on the sheet goes to the variance account" '.success and .data.totalCost == 500000 and .data.toFinishedGoods == 500000 and .data.variance == 1000000 and .data.isOpen == false and .data.voucherId > 0'
WOC=$(val .data.voucherId)
as "$FM_TOKEN" GET $FG/vouchers/$WOC; check "f3 close-out: the close voucher moves 1,500,000 (finished goods 500,000 + variance 1,000,000 = WIP 1,300,000 + labour 200,000)" '.success and .data.totalAmount == 1500000 and ([.data.lines[]|select(.accountCode == "10503" and .debit == 500000)]|length) == 1 and ([.data.lines[]|select(.accountCode == "70109" and .debit == 1000000)]|length) == 1 and ([.data.lines[]|select(.accountCode == "10520" and .credit == 1300000)]|length) == 1'
as "$FM_TOKEN" GET $FCO/workorders/$WO2/cost-sheet; check "f3 close-out: nothing is left in work in progress" '.success and .data.wipBalance == 0 and .data.labourInLedger == 0 and .data.isClosed == true'
as "$FIN_TOKEN" POST $FCO/workorders/$WO2/close '{}'; no "f3 close-out: a closed order is not closed twice"
as "$FIN_TOKEN" POST $FCO/workorders/$WO2/reopen '{"reason":"x"}'; no "f3 close-out: only the finance manager reopens"
as "$FM_TOKEN" POST $FCO/workorders/$WO2/reopen '{}'; no "f3 close-out: reopening needs a reason"
as "$FM_TOKEN" POST $FCO/workorders/$WO2/reopen '{"reason":"اصلاح هزینه","date":"2025-05-31T00:00:00"}'; check "f3 close-out: reopening by the finance manager with a reason creates the reversal" '.success and .data.isOpen == true and .data.reversalVoucherId > 0'
as "$FM_TOKEN" GET $FCO/workorders/$WO2/cost-sheet; check "f3 close-out: after the reversal the 1,300,000 work in progress is back" '.success and .data.wipBalance == 1300000 and .data.isClosed == false'

# --- section 5: percentage-of-completion revenue (its own contract with one work order, so the job close-out of work order 1 below stays a normal job)
call POST Contracts '{"code":"R3-CT-POC","title":"قرارداد درصد پیشرفت آزمون","status":1,"employer":"کارفرمای آزمون"}'; check "f3 poc: the percentage-of-completion contract is created" '.success and .data.id > 0'
PQ=$(val .data.id)
call POST ContractEquipments "{\"contractId\":$PQ,\"equipmentId\":$QEQ,\"count\":1}"; check "f3 poc: contract equipment" '.success'
call POST WorkOrders "{\"contractId\":$PQ,\"equipmentId\":$QEQ,\"code\":\"R3-WO-1\",\"title\":\"Pump R3\",\"description\":\"x\",\"count\":1}"; check "f3 poc: the work order of the contract is created" '.success and .data.id > 0'
WO3=$(val .data.id)
as "$FIN_TOKEN" POST $FRR/contracts "{\"contractId\":$PQ,\"method\":1,\"contractPrice\":1000000000,\"progressSource\":\"Manual\",\"progressMeasure\":\"Physical\"}"; no "f3 poc: only the finance manager sets the revenue method"
as "$FM_TOKEN" POST $FRR/contracts "{\"contractId\":$PQ,\"method\":1,\"contractPrice\":1000000000,\"progressSource\":\"Manual\",\"progressMeasure\":\"Physical\"}"; check "f3 poc: contract set to percentage of completion at 1,000,000,000" '.success and .data.method == 1'
as "$FIN_TOKEN" POST $FRR/$P3 '{"dryRun":true,"inputs":[]}'; nomsg "f3 poc: manual progress must be entered" "دستی"
as "$FIN_TOKEN" POST $FRR/$P3 "{\"dryRun\":false,\"inputs\":[{\"contractId\":$PQ,\"progressPercent\":40,\"estimatedCost\":800000000}]}"; check "f3 poc: progress 40% earns 400,000,000 as unbilled receivable, no loss provision" '.success and .data.voucherId > 0 and .data.rows[0].earnedToDate == 400000000 and .data.rows[0].revenueThisRun == 400000000 and .data.rows[0].contractAsset == 400000000 and .data.rows[0].contractLiability == 0 and .data.rows[0].lossProvision == 0'
as "$FIN_TOKEN" POST $FRR/$P4 "{\"dryRun\":false,\"inputs\":[{\"contractId\":$PQ,\"progressPercent\":40,\"estimatedCost\":800000000}]}"; check "f3 poc: a rerun at the same progress posts nothing (cumulative method)" '.success and .data.voucherId == null and .data.rows[0].revenueThisRun == 0'
fv "{\"date\":\"2025-07-24T00:00:00\",\"description\":\"صورت‌حساب پیشرفت\",\"lines\":[{\"accountCode\":\"10301\",\"debit\":150000000,\"detail1Code\":\"$FXCUS\"},{\"accountCode\":\"60101\",\"credit\":150000000,\"contractId\":$PQ}]}"
as "$FIN_TOKEN" POST $FRR/$P5 "{\"dryRun\":false,\"inputs\":[{\"contractId\":$PQ,\"progressPercent\":60,\"estimatedCost\":1200000000}]}"; check "f3 poc: 60% = 600,000,000 earned, 550,000,000 already in revenue, 50,000,000 more; unbilled 450,000,000; expected loss provision 80,000,000" '.success and .data.rows[0].earnedToDate == 600000000 and .data.rows[0].revenueInLedger == 550000000 and .data.rows[0].revenueThisRun == 50000000 and .data.rows[0].contractAsset == 450000000 and .data.rows[0].lossProvision == 80000000 and .data.rows[0].lossProvisionChange == 80000000'
[ "$(tbv 30404 closingCredit)" = "80000000" ] && ok "f3 poc: the expected-loss provision 30404 is exactly 80,000,000 (EAC 1.2bn - price 1bn = 200m loss, 40% still to come)" || bad "f3 poc: the expected-loss provision 30404 is exactly 80,000,000 (got $(tbv 30404 closingCredit))"
[ "$(tbv 10308 closingDebit)" = "450000000" ] && ok "f3 poc: unbilled receivable 10308 is exactly earned minus billed (450,000,000)" || bad "f3 poc: unbilled receivable 10308 is exactly earned minus billed (got $(tbv 10308 closingDebit))"
as "$FM_TOKEN" POST $FRR/contracts "{\"contractId\":$PQ,\"method\":0}"; no "f3 poc: the method cannot go back to invoice once revenue was recognised"

# --- review 3, C1: a work order of a percentage-of-completion contract is expensed once (material 100 and absorbed overhead 30 leave work in progress with the revenue run)
CG_C0=$(tbv 70101 closingDebit)
fv "{\"date\":\"2025-08-25T00:00:00\",\"description\":\"مواد دستور کار قرارداد درصد پیشرفت\",\"lines\":[{\"accountCode\":\"10520\",\"debit\":100,\"workOrderId\":$WO3,\"contractId\":$PQ},{\"accountCode\":\"10101\",\"credit\":100}]}"
fv "{\"date\":\"2025-08-25T00:00:00\",\"description\":\"سربار جذب‌شده دستور کار\",\"lines\":[{\"accountCode\":\"10520\",\"debit\":30,\"workOrderId\":$WO3},{\"accountCode\":\"70110\",\"credit\":30}]}"
as "$FM_TOKEN" GET $FCO/workorders/$WO3/cost-sheet; check "r3 c1: before the revenue run the work order holds 130 in work in progress" '.success and .data.wipBalance == 130'
as "$FIN_TOKEN" POST $FRR/$P6 "{\"dryRun\":false,\"inputs\":[{\"contractId\":$PQ,\"progressPercent\":60,\"estimatedCost\":1200000000}]}"; check "r3 c1: the revenue run recognises the cost of 130 (100 material + 30 overhead) and no further revenue" '.success and .data.rows[0].revenueThisRun == 0 and .data.rows[0].costOfSalesThisRun == 130'
[ "$(( $(tbv 70101 closingDebit) - CG_C0 ))" = "130" ] && ok "r3 c1: cost of sales 70101 rose by exactly 130" || bad "r3 c1: cost of sales 70101 rose by exactly 130 (got $(( $(tbv 70101 closingDebit) - CG_C0 )))"
as "$FM_TOKEN" GET $FCO/workorders/$WO3/cost-sheet; check "r3 c1: nothing is left in work in progress for the work order (the absorbed 30 left too)" '.success and .data.wipBalance == 0'
call PUT WorkOrders/change-condition "{\"workOrderId\":$WO3,\"newCondition\":2}"; check "r3 c1: the work order is set to Done" '.success'
as "$FIN_TOKEN" POST $FCO/workorders/$WO3/close '{"quantity":1}'; no "r3 c1: the close-out of a work order of a percentage-of-completion contract is refused (it would expense the cost a second time)"

# --- section 4: budget, two-person approval, report, commitment control
as "$FM_TOKEN" POST $FBU/import "{\"budgetId\":0,\"csv\":\"x\"}"; no "f3 budget: import into a missing budget is refused"
BL='[{"accountCode":"80108","period":3,"amount":10000000},{"accountCode":"80108","period":4,"amount":5000000},{"costCenterCode":"CC-P1","period":3,"amount":4000000}]'
as "$FIN_TOKEN" POST $FBU "{\"fiscalYearId\":$Y4,\"title\":\"بودجه ۱۴۰۴\",\"kind\":1,\"commitMode\":2,\"lines\":$BL}"; check "f3 budget: draft budget version 1 saved, 19,000,000" '.success and .data.status == 0 and .data.version == 1 and .data.total == 19000000 and (.data.number|startswith("BUD-1404-"))'
BD=$(val .data.id)
as "$FIN_TOKEN" POST $FBU/import "{\"budgetId\":$BD,\"csv\":\"accountCode,costCenterCode,contractId,period,amount,isCapital,note\n80108,,,3,10000000,0,\n80108,,,4,5000000,0,\n,CC-P1,,3,4000000,0,\n20103,,,3,50000000,1,capital\n\"}"; check "f3 budget: CSV import replaces the lines (4 lines, capital line flagged)" '.success and (.data.lines|length) == 4 and .data.total == 69000000 and ([.data.lines[]|select(.isCapital)]|length) == 1'
as "$FIN_TOKEN" POST $FBU/import "{\"budgetId\":$BD,\"csv\":\"9999999,,,3,10\n\"}"; no "f3 budget: an unknown account in the import is refused"
as "$FM_TOKEN" GET "$FBU/$BD/export" ; [ "$CODE" = "200" ] && ok "f3 budget: export answers" || bad "f3 budget: export answers ($CODE)"
as "$FIN_TOKEN" POST $FBU/$BD/approve; no "f3 budget: the accountant cannot approve"
as "$FM_TOKEN" POST $FBU/$BD/approve; check "f3 budget: finance manager approves first; still a draft" '.success and .data.status == 0 and .data.managerApprovedByUserId > 0 and .data.ceoApprovedByUserId == null'
as "$FM_TOKEN" POST $FBU/$BD/approve; no "f3 budget: the same person cannot approve twice"
as "$FM2_TOKEN" POST $FBU/$BD/approve; no "f3 budget: a second finance manager does not replace the CEO"
as "$CEO_TOKEN" POST $FBU/$BD/approve; check "f3 budget: the CEO's approval makes it Approved" '.success and .data.status == 1 and .data.ceoApprovedByUserId > 0'
as "$FIN_TOKEN" POST $FBU "{\"id\":$BD,\"fiscalYearId\":$Y4,\"title\":\"x\",\"lines\":$BL}"; no "f3 budget: an approved budget is not edited"
as "$FM_TOKEN" GET "$FBU/report?BudgetId=$BD&FromPeriod=3&ToPeriod=3&GroupBy=account"; check "f3 budget: period 3 account 80108: budget 10,000,000, actual 3,200,001, variance -6,799,999 (-68%)" '.success and ([.data.rows[]|select(.key == "80108")][0]|(.budget == 10000000 and .actual == 3200001 and .variance == -6799999 and .variancePercent == -68))'
as "$FM_TOKEN" GET "$FBU/report?BudgetId=$BD&FromPeriod=1&ToPeriod=4&GroupBy=account"; check "f3 budget: year to date (1-4) 80108: budget 15,000,000, actual 3,400,001, forecast = actual plus the budget still to come" '.success and ([.data.rows[]|select(.key == "80108")][0]|(.budget == 15000000 and .actual == 3400001 and .fullYearBudget == 15000000 and .forecast == 3400001))'
as "$FM_TOKEN" GET "$FBU/report?BudgetId=$BD&FromPeriod=1&ToPeriod=4&GroupBy=account&Forecast=RunRate"; check "f3 budget: run-rate forecast = actual x 12 / 4 = 10,200,003" '.success and ([.data.rows[]|select(.key == "80108")][0].forecast) == 10200003'
as "$FM_TOKEN" GET "$FBU/report?BudgetId=$BD&FromPeriod=3&ToPeriod=3&GroupBy=costcenter"; check "f3 budget: grouped by cost centre" '.success and ([.data.rows[]|select(.key == "CC-P1")][0].budget) == 4000000'
as "$AUD_TOKEN" GET "$FBU/report?BudgetId=$BD"; check "f3 budget: the auditor can read the report" '.success'
as "$FM_TOKEN" POST $FBU/commitment-check '{"accountCode":"80108","amount":8000000,"date":"2025-06-15T00:00:00"}'; check "f3 commitment: 8,000,000 fits (15,000,000 - 3,400,001 spent)" '.success and .data.allowed == true and .data.warning == false and .data.remaining == 3599999 and .data.mode == 2'
as "$FM_TOKEN" POST $FBU/commitment-check '{"accountCode":"80108","amount":12000000,"date":"2025-06-15T00:00:00"}'; check "f3 commitment: 12,000,000 is blocked in Block mode (over by 400,001)" '.success and .data.allowed == false and .data.remaining == -400001'
as "$FM_TOKEN" POST $FBU/commitment-check '{"accountCode":"80108","amount":12000000,"date":"2025-06-15T00:00:00","overrideReason":"فوری"}'; check "f3 commitment: only the CEO can override a block" '.success and .data.allowed == false'
as "$CEO_TOKEN" POST $FBU/commitment-check '{"accountCode":"80108","amount":12000000,"date":"2025-06-15T00:00:00","overrideReason":"تصمیم مدیرعامل برای خرید فوری"}'; check "f3 commitment: the CEO overrides with a reason (recorded in the message)" '.success and .data.allowed == true and .data.overridden == true'
as "$WH_TOKEN" POST procurement/requests "{\"source\":2,\"kind\":1,\"requestDate\":\"2025-06-15T00:00:00\",\"description\":\"خرید مرکز هزینه\",\"submit\":true,\"costCenter\":\"CC-P1\",\"lines\":[{\"itemId\":$BOLT,\"quantity\":10,\"estimatedUnitPrice\":600000}]}"; check "f3 commitment: purchase request for cost centre CC-P1 (6,000,000)" '.success and .data.estimatedAmount == 6000000'
PRB=$(val .data.id)
as "$PLAN_TOKEN" GET "$FBU/commitment-check/purchase-request/$PRB"; check "f3 commitment: the purchase request exceeds the cost-centre envelope of 4,000,000 and is blocked with the reason (4,000,000 - 300,000 variance already charged - 6,000,000)" '.success and .data.allowed == false and .data.remaining == -2300000 and (.data.message|contains("مسدود"))'
as "$CEO_TOKEN" POST procurement/requests/action "{\"id\":$PRB,\"action\":\"approve-management\"}"
as "$PLAN_TOKEN" POST procurement/requests/action "{\"id\":$PRB,\"action\":\"approve-planning\"}"; nomsg "r2 f10: in Block mode the purchase request over the cost-centre envelope cannot become approved" "مسدود"
as "$FM_TOKEN" POST $FBU/$BD/revise; check "f3 budget: a revision is a new draft (version 2)" '.success and .data.status == 0 and .data.version == 2 and .data.kind == 2 and (.data.lines|length) == 4'
BD2=$(val .data.id); BL2=$(jq -c '[.data.lines[]|{accountCode,costCenterCode,contractId,period,amount,isCapital}]' "$B")
as "$FIN_TOKEN" POST $FBU "{\"id\":$BD2,\"fiscalYearId\":$Y4,\"title\":\"بازنگری ۱\",\"kind\":2,\"commitMode\":1,\"lines\":$BL2}"; check "f3 budget: revision switched to Warn mode" '.success and .data.commitMode == 1'
as "$FM_TOKEN" POST $FBU/$BD2/approve; as "$CEO_TOKEN" POST $FBU/$BD2/approve; check "f3 budget: revision approved by both" '.success and .data.status == 1'
as "$FM_TOKEN" GET $FBU/$BD; check "f3 budget: the previous version is superseded" '.success and .data.status == 2'
as "$FM_TOKEN" POST $FBU/commitment-check '{"accountCode":"80108","amount":12000000,"date":"2025-06-15T00:00:00"}'; check "f3 commitment: in Warn mode the same request passes with a warning" '.success and .data.allowed == true and .data.warning == true and .data.mode == 1'
as "$PLAN_TOKEN" POST procurement/requests/action "{\"id\":$PRB,\"action\":\"approve-planning\"}"; check "r2 f10: in Warn mode the same request is approved and the answer carries the warning" '.success and .data.status == 2 and (.message // "" | length) > 0'
TOKEN=$ADMIN_TOKEN

# =============================================================================================== review fixes R1
TOKEN=$ADMIN_TOKEN
as "$FM_TOKEN" POST $FG/years/ensure "{\"date\":\"${TODAY}T00:00:00\"}"; FYC=$(val .data.id)
tbc() { as "$FM_TOKEN" GET "$FG/reports/trial-balance?FiscalYearId=$FYC&Level=2&IncludeDraft=true"; jq -r "([.data.rows[]|select(.code == \"$1\")][0] | ((.closingDebit // 0) - (.closingCredit // 0))) // 0 | round" "$B"; }
rec() { as "$FM_TOKEN" GET "$FG/reports/reconciliation"; jq -r "[.data.rows[]|select(.key == \"$1\")][0].difference" "$B"; }
rcpt() { # a petty-cash purchase receipt without an order, confirmed: rcpt QTY UNITPRICE -> RCPID, RCPNO
  call POST inventory/documents "{\"type\":1,\"documentDate\":\"${TODAY}T00:00:00\",\"warehouseId\":$W1,\"referenceNumber\":\"تنخواه\",\"lines\":[{\"itemId\":$BOLT,\"quantity\":$1,\"locationId\":$L2,\"unitPrice\":$2}]}"; RCPID=$(val .data.id)
  call POST "inventory/documents/$RCPID/confirm"; RCPNO=$(val .data.number)
  if jq -e '.success' "$B" >/dev/null 2>&1; then ok "r1 setup: receipt $RCPNO ($1 x $2) confirmed"; else bad "r1 setup: receipt confirmed ($(jq -r '.message // empty' "$B" | head -c 150))"; fi
}
evlist() { as "$FM_TOKEN" GET "finance/events?PageSize=0"; }
as "$FM_TOKEN" POST $FG/post-events

# --- 1: petty-cash purchase is not costed twice (GRNI clears)
as "$TR_TOKEN" POST $FT/petty-cash "{\"holderUserId\":$WHID,\"holderTitle\":\"تنخواه خرید - بازبینی\",\"ceiling\":100000000}"; PCR=$(val .data.id)
as "$TR_TOKEN" POST $FT/payments "{\"date\":\"${TODAY}T00:00:00\",\"purpose\":2,\"pettyCashId\":$PCR,\"topUpAmount\":45000000,\"lines\":[{\"instrument\":1,\"amount\":45000000,\"cashBoxId\":$CB}]}"; PMR=$(val .data.id)
as "$FM_TOKEN" POST $FT/payments/action "{\"id\":$PMR,\"action\":\"approve\"}"; as "$TR_TOKEN" POST $FT/payments/action "{\"id\":$PMR,\"action\":\"post\"}"; check "r1 petty cash: fund of 45,000,000 topped up" '.success and .data.status == 2'
G0=$(tbc 30103); X0=$(tbc 70202)
rcpt 100 400000; RCP1=$RCPID; RCP1NO=$RCPNO
as "$FM_TOKEN" POST $FG/post-events
[ "$(( $(tbc 30103) - G0 ))" = "-40000000" ] && ok "r1 petty cash: the receipt of 40,000,000 credits GRNI 30103 by exactly 40,000,000" || bad "r1 petty cash: the receipt credits GRNI by 40,000,000 (delta $(( $(tbc 30103) - G0 )))"
as "$WH_TOKEN" POST $FT/settlements "{\"pettyCashId\":$PCR,\"date\":\"${TODAY}T00:00:00\",\"lines\":[{\"description\":\"خرید پیچ\",\"amount\":39000000,\"inventoryDocumentId\":$RCP1}]}"; nomsg "r1 petty cash: a settlement of a receipt must equal its value (40,000,000)" "برابر ارزش رسید"
as "$WH_TOKEN" POST $FT/settlements "{\"pettyCashId\":$PCR,\"date\":\"${TODAY}T00:00:00\",\"lines\":[{\"description\":\"خرید پیچ\",\"accountCode\":\"70202\",\"amount\":40000000,\"inventoryDocumentId\":$RCP1}]}"; check "r1 petty cash: settlement of the receipt saved" '.success and .data.totalAmount == 40000000'
SER=$(val .data.id)
as "$FIN_TOKEN" POST $FT/settlements/action "{\"id\":$SER,\"action\":\"approve\"}"; check "r1 petty cash: settlement approved" '.success and .data.status == 2'
as "$WH_TOKEN" POST $FT/settlements "{\"pettyCashId\":$PCR,\"date\":\"${TODAY}T00:00:00\",\"lines\":[{\"description\":\"دوباره\",\"amount\":40000000,\"inventoryDocumentId\":$RCP1}]}"; nomsg "r1 petty cash: a receipt is settled only once" "قبلاً تسویه"
as "$FM_TOKEN" POST $FG/post-events
[ "$(( $(tbc 30103) - G0 ))" = "0" ] && ok "r1 petty cash: GRNI 30103 is back to its starting balance after the settlement" || bad "r1 petty cash: GRNI back to the start (delta $(( $(tbc 30103) - G0 )))"
[ "$(( $(tbc 70202) - X0 ))" = "0" ] && ok "r1 petty cash: no expense was booked on 70202 (no second costing)" || bad "r1 petty cash: no expense booked (delta $(( $(tbc 70202) - X0 )))"
as "$FM_TOKEN" GET "$FT/petty-cash?PageSize=0"; check "r1 petty cash: the fund holds 5,000,000 after settling 40,000,000" '[.data.data[]|select(.id == '$PCR')][0].balance == 5000000'

# --- 20: petty cash list is not open to everyone
as "$COM_TOKEN" GET "$FT/petty-cash?PageSize=0"; check "r1 petty cash: a commercial user sees no funds that are not his" '.success and (.data.data|length) == 0'
as "$FM_TOKEN" GET "$FT/petty-cash?PageSize=0"; check "r1 petty cash: the finance manager sees all funds" '.success and (.data.data|length) >= 2'

# --- 3: bank reconciliation with an issued cheque that was not presented
as "$TR_TOKEN" POST $FT/bank-accounts '{"title":"بانک هـ - آزمون چک","bankName":"ملت","accountNumber":"100324200006","iban":"IR860170000000100324200006"}'; BAR=$(val .data.id)
as "$TR_TOKEN" POST $FT/cheque-books "{\"bankAccountId\":$BAR,\"series\":\"R\",\"fromSerial\":901,\"toSerial\":905}"; check "r1 bank: cheque book for the new account" '.success and .data.leaves == 5'
RJB="{\"date\":\"${TODAY}T00:00:00\",\"customerId\":$CU1,\"description\":\"دریافت بانک هـ\",\"lines\":[{\"instrument\":2,\"amount\":5000000,\"bankAccountId\":$BAR,\"reference\":\"TRK-BAR\"}]}"
as "$TR_TOKEN" POST $FT/receipts "$RJB"; RB=$(val .data.id)
as "$FM_TOKEN" POST $FT/receipts/action "{\"id\":$RB,\"action\":\"approve\"}"; as "$TR_TOKEN" POST $FT/receipts/action "{\"id\":$RB,\"action\":\"post\"}"; check "r1 bank: receipt of 5,000,000 into the account posted" '.success and .data.status == 2'
# --- 4 and 18: the reconciliation reads real balances; asset offsets
D0=$(rec inventory)
as "$FIN_TOKEN" POST $FG/vouchers "{\"date\":\"${TODAY}T00:00:00\",\"description\":\"آزمون مغایرت موجودی\",\"lines\":[{\"accountCode\":\"10505\",\"debit\":7000000},{\"accountCode\":\"10101\",\"credit\":7000000}]}"; VINV=$(val .data.id)
[ "$(( $(rec inventory) - D0 ))" = "7000000" ] && ok "r1 reconciliation: a manual voucher on stock account 10505 shows as a 7,000,000 difference" || bad "r1 reconciliation: a manual voucher on 10505 shows a 7,000,000 difference (got $(( $(rec inventory) - D0 )))"
as "$FIN_TOKEN" DELETE "$FG/vouchers/$VINV"
[ "$(rec inventory)" = "$D0" ] && ok "r1 reconciliation: the difference is gone when the voucher is removed" || bad "r1 reconciliation: difference gone after removing the voucher"
as "$FIN_TOKEN" POST $FA "$(AJ 'دستگاه آزمون بازبینی' $CLS1 2026-06-25 1000000 10505)"; ASX=$(val .data.id)
as "$FM_TOKEN" POST $FA/action "{\"id\":$ASX,\"action\":\"approve\"}"; nomsg "r1 assets: a stock account 10505 cannot be the offset of an asset" "تهاتر"

# --- 5: maker-checker, editors cannot approve
MVR="{\"date\":\"${TODAY}T00:00:00\",\"description\":\"آزمون ویرایشگر\",\"lines\":[{\"accountCode\":\"80108\",\"debit\":1000},{\"accountCode\":\"10101\",\"credit\":1000}]}"
as "$FIN_TOKEN" POST $FG/vouchers "$MVR"; VE=$(val .data.id)
as "$FIN2_TOKEN" POST $FG/vouchers "$(jq -c ". + {id:$VE} | .lines[0].debit = 2000 | .lines[1].credit = 2000" <<< "$MVR")"; check "r1 maker-checker: a second accountant edits the draft" '.success'
as "$FIN2_TOKEN" POST $FG/vouchers/action "{\"id\":$VE,\"action\":\"check\"}"; nomsg "r1 maker-checker: the editor of a voucher cannot check it" "تهیه‌کننده"
as "$FM_TOKEN" POST $FG/vouchers/action "{\"id\":$VE,\"action\":\"check\"}"; check "r1 maker-checker: another person checks it" '.success and .data.status == 1'
as "$FM2_TOKEN" POST $FG/vouchers/action "{\"id\":$VE,\"action\":\"finalize\"}"; check "r1 maker-checker: a person who touched nothing finalizes it" '.success and .data.status == 2'
as "$FIN_TOKEN" POST $FR/invoices "$INVJ"; SIE=$(val .data.id)
as "$FIN2_TOKEN" POST $FR/invoices "$(jq -c ". + {id:$SIE}" <<< "$INVJ")"
as "$FIN2_TOKEN" POST $FR/invoices/action "{\"id\":$SIE,\"action\":\"approve\"}"; nomsg "r1 maker-checker: the editor of a sales invoice cannot approve it" "غیر از ثبت‌کننده"
as "$FIN_TOKEN" POST $FR/invoices/action "{\"id\":$SIE,\"action\":\"cancel\"}"
as "$TR_TOKEN" POST $FT/payments "{\"date\":\"${TODAY}T00:00:00\",\"purpose\":2,\"pettyCashId\":$PCR,\"topUpAmount\":1000000,\"lines\":[{\"instrument\":1,\"amount\":1000000,\"cashBoxId\":$CB}]}"; PME=$(val .data.id)
as "$FM2_TOKEN" POST $FT/payments "{\"id\":$PME,\"date\":\"${TODAY}T00:00:00\",\"purpose\":2,\"pettyCashId\":$PCR,\"topUpAmount\":1000000,\"lines\":[{\"instrument\":1,\"amount\":1000000,\"cashBoxId\":$CB}]}"; check "r1 maker-checker: a finance manager edits the treasury draft" '.success'
as "$FM2_TOKEN" POST $FT/payments/action "{\"id\":$PME,\"action\":\"approve\"}"; nomsg "r1 maker-checker: the editor of a payment cannot approve it" "ثبت‌کننده یا تاییدکننده قبلی"
as "$FM_TOKEN" POST $FT/payments/action "{\"id\":$PME,\"action\":\"approve\"}"; check "r1 maker-checker: another finance manager approves it; the approver is stored" '.success and .data.status == 1'
as "$TR_TOKEN" POST $FT/payments/action "{\"id\":$PME,\"action\":\"cancel\"}"
as "$TR_TOKEN" POST $FT/receipts "$RJB"; REE=$(val .data.id)
as "$FM2_TOKEN" POST $FT/receipts "$(jq -c ". + {id:$REE}" <<< "$RJB")"
as "$FM2_TOKEN" POST $FT/receipts/action "{\"id\":$REE,\"action\":\"approve\"}"; nomsg "r1 maker-checker: the editor of a receipt cannot approve it" "ثبت‌کننده یا تاییدکننده قبلی"
as "$TR_TOKEN" POST $FT/receipts/action "{\"id\":$REE,\"action\":\"cancel\"}"
as "$WH_TOKEN" POST $FT/settlements "{\"pettyCashId\":$PCR,\"date\":\"${TODAY}T00:00:00\",\"lines\":[{\"description\":\"هزینه\",\"accountCode\":\"70202\",\"amount\":1000000}]}"; SEE=$(val .data.id)
as "$FIN_TOKEN" POST $FT/settlements "{\"id\":$SEE,\"pettyCashId\":$PCR,\"date\":\"${TODAY}T00:00:00\",\"lines\":[{\"description\":\"هزینه\",\"accountCode\":\"70202\",\"amount\":1000000}]}"
as "$FIN_TOKEN" POST $FT/settlements/action "{\"id\":$SEE,\"action\":\"approve\"}"; nomsg "r1 maker-checker: the editor of a settlement cannot approve it" "ثبت‌کننده"
as "$FIN2_TOKEN" POST $FT/settlements/action "{\"id\":$SEE,\"action\":\"approve\"}"; check "r1 maker-checker: another accountant approves the settlement" '.success and .data.status == 2'

# --- 6 and 7: a poison event does not block posting; a cancelled document does not stay in the ledger
as "$FM_TOKEN" POST $FG/details '{"code":"SUP-9999","title":"آزمون","type":1,"isActive":true}'; nomsg "r1 posting: a manual detail code with the reserved prefix SUP- is refused" "رزرو"
rcpt 10 10000; RC6=$RCPID; RC6NO=$RCPNO
as "$FM_TOKEN" POST $FG/post-events
as "$FM_TOKEN" GET "$FG/vouchers?FiscalYearId=$FYC&Type=3&PageSize=0"; AV6=$(jq -r --arg n "$RC6NO" '[.data.data[]|select(.sourceNumber == $n)][0].id' "$B")
[ -n "$AV6" ] && [ "$AV6" != "null" ] && ok "r1 posting: the receipt has its automatic voucher" || bad "r1 posting: the receipt has its automatic voucher"
as "$FM_TOKEN" POST $FG/vouchers/action "{\"id\":$AV6,\"action\":\"finalize\"}"; check "r1 posting: the automatic voucher is finalised" '.success and .data.status == 2'
as "$FIN_TOKEN" POST $FG/vouchers/action "{\"id\":$AV6,\"action\":\"reverse\",\"note\":\"آزمون\"}"; check "r1 posting: the voucher is reversed by hand" '.success and .data.type == 5'
call POST "inventory/documents/$RC6/cancel" '{"reason":"آزمون رویداد مسموم"}'; check "r1 posting: the receipt is cancelled after the hand reversal" '.success'
rcpt 5 10000; RC6B=$RCPID; RC6BNO=$RCPNO
as "$FM_TOKEN" POST $FG/post-events
evlist; check "r1 posting: the reversal of an already reversed voucher is settled and the next receipt still posts" '([.data.data[]|select(.sourceNumber == "'$RC6NO'")]|length) == 2 and ([.data.data[]|select(.sourceNumber == "'$RC6NO'" and .status != 4)]|length) == 0 and ([.data.data[]|select(.sourceNumber == "'$RC6BNO'" and .status == 4)]|length) >= 1'
as "$FM_TOKEN" GET "$FG/reports/reconciliation"; check "r1 posting: no event is left unposted" '.data.unpostedEvents == 0'
D7=$(rec inventory)
rcpt 10 10000; RC7=$RCPID; RC7NO=$RCPNO
as "$FM_TOKEN" POST $FG/post-events
evlist; EV7=$(jq -r --arg n "$RC7NO" '[.data.data[]|select(.sourceNumber == $n)][0].id' "$B")
as "$FM_TOKEN" POST finance/events/mark "{\"eventIds\":[$EV7],\"status\":0}"; check "r1 events: a posted event cannot be marked pending again (0 changed)" '.success and .data == 0'
call POST "inventory/documents/$RC7/cancel" '{"reason":"آزمون"}'
as "$FM_TOKEN" POST $FG/post-events
evlist; check "r1 events: the cancelled receipt has an original and a posted reversal event" '([.data.data[]|select(.sourceNumber == "'$RC7NO'")]|length) == 2 and ([.data.data[]|select(.sourceNumber == "'$RC7NO'" and .status == 4)]|length) == 2'
[ "$(rec inventory)" = "$D7" ] && ok "r1 events: the stock ledger difference is unchanged after the cancelled receipt" || bad "r1 events: the stock ledger difference is unchanged ($D7 -> $(rec inventory))"

# --- 11: a reversal voucher is read-only
as "$FIN_TOKEN" GET "$FG/vouchers/$V1R"; V1RDOC=$(jq -c '{id:.data.id,date:.data.date,lines:[.data.lines[]|{accountCode,debit:(.debit*2),credit:(.credit*2)}]}' "$B")
as "$FM_TOKEN" GET "$FG/vouchers/$V1R"; check "r1 reversal: the finalised reversal voucher mirrors its original (1,000,000 on 10101)" '.success and ([.data.lines[]|select(.accountCode == "10101")][0].debit) == 1000000'
as "$FIN_TOKEN" POST $FG/vouchers "$V1RDOC"; no "r1 reversal: a final reversal voucher cannot be edited"
as "$FIN_TOKEN" POST $FG/vouchers/action "{\"id\":$VE,\"action\":\"reverse\",\"note\":\"آزمون\"}"; VER=$(val .data.id)
as "$FIN_TOKEN" GET "$FG/vouchers/$VER"; VERDOC=$(jq -c '{id:.data.id,date:.data.date,lines:[.data.lines[]|{accountCode,debit:(.debit*2),credit:(.credit*2)}]}' "$B")
as "$FIN_TOKEN" POST $FG/vouchers "$VERDOC"
as "$FIN_TOKEN" GET "$FG/vouchers/$VER"; check "r1 reversal: editing a draft reversal with doubled amounts leaves the mirrored lines (2,000 each side)" '.success and ([.data.lines[].debit]|add) == 2000 and ([.data.lines[].credit]|add) == 2000'
as "$FIN_TOKEN" DELETE "$FG/vouchers/$VER"

# --- 12: years close in order
as "$FM_TOKEN" POST $FG/years/ensure '{"date":"2023-06-01T00:00:00"}'; check "r1 year: fiscal year 1402 ensured" '.success and .data.year == 1402'
FY2=$(val .data.id)
as "$FM_TOKEN" GET $FG/years; for P in $(jq -r --argjson y "$FY2" '.data[]|select(.id == $y)|.periods[].id' "$B"); do as "$FM_TOKEN" POST $FG/periods/action "{\"id\":$P,\"action\":\"lock\"}"; done
as "$FM_TOKEN" POST $FG/years/$FY2/close '{"overrideReconciliation":true,"reason":"آزمون ترتیب"}'; nomsg "r1 year: year 1402 cannot be closed before 1401" "سال مالی قبل (1401)"

# --- 2 and 14: supplier advance recovery and late penalty credited to its own account
as "$WH_TOKEN" POST procurement/requests "{\"source\":2,\"kind\":1,\"requestDate\":\"2026-10-22T00:00:00\",\"description\":\"پیش‌پرداخت بازبینی\",\"submit\":true,\"lines\":[{\"itemId\":$BOLT,\"quantity\":100,\"estimatedUnitPrice\":400000,\"technicalRequirements\":\"ISO 4014\"}]}"; RQ2=$(val .data.id); RQ2L=$(val '.data.lines[0].id')
as "$PLAN_TOKEN" POST procurement/requests/action "{\"id\":$RQ2,\"action\":\"approve-planning\"}"; as "$CEO_TOKEN" POST procurement/requests/action "{\"id\":$RQ2,\"action\":\"approve-management\"}"; check "r1 advance: purchase request approved" '.success and .data.status == 2'
QA2="{\"providerId\":$PA,\"quoteNumber\":\"R1-A\",\"vatPercent\":10,\"deliveryDays\":20,\"technicallyAccepted\":true,\"lines\":[{\"requestLineId\":$RQ2L,\"quantity\":100,\"unitPrice\":390000}]}"
QB2="{\"providerId\":$PB,\"quoteNumber\":\"R1-B\",\"vatPercent\":10,\"technicallyAccepted\":true,\"lines\":[{\"requestLineId\":$RQ2L,\"quantity\":100,\"unitPrice\":380000}]}"
as "$COM_TOKEN" POST procurement/inquiries "{\"purchaseRequestId\":$RQ2,\"inquiryDate\":\"2026-10-22T00:00:00\",\"quotes\":[$QA2,$QB2]}"; IQ2=$(val .data.id); QA2ID=$(val ".data.quotes[]|select(.providerId == $PA)|.id")
as "$COM_TOKEN" POST procurement/inquiries/select "{\"inquiryId\":$IQ2,\"quoteId\":$QA2ID,\"reason\":\"گرید A\"}"
as "$COM_TOKEN" POST "procurement/orders/from-inquiry/$IQ2"; check "r1 advance: order draft of 39,000,000" '.success and .data.totalAmount == 39000000'
PO2=$(val .data.id); POL2=$(val '.data.lines[0].id')
as "$COM_TOKEN" GET "procurement/orders/$PO2"; PO2SAVE=$(jq -c '.data | {id, kind, orderDate, providerId, inquiryId, vatPercent, advancePercent: 10, advanceGuarantee: "ضمانت‌نامه آزمون", requirements, description, lines: [.lines[] | {requestLineId, itemId, quantity, unitPrice}]}' "$B")
as "$COM_TOKEN" POST procurement/orders "$PO2SAVE"; check "r1 advance: the order carries a 10% advance" '.success and .data.advancePercent == 10'; POL2=$(val '.data.lines[0].id')
call POST procurement/orders/action "{\"id\":$PO2,\"action\":\"approve\"}"; check "r1 advance: order approved" '.success and .data.status == 1'; as "$COM_TOKEN" POST procurement/orders/action "{\"id\":$PO2,\"action\":\"release\"}"; check "r1 advance: order released" '.success and .data.status == 2'
as "$COM_TOKEN" POST procurement/payments "{\"type\":1,\"purchaseOrderId\":$PO2,\"amount\":3900000}"; check "r1 advance: advance request of 3,900,000 (10% of 39,000,000)" '.success and .data.amount == 3900000'
ADVID=$(val .data.id)
as "$FIN_TOKEN" GET procurement/suppliers/open-advances; check "r2 f12: an advance that is only requested (not paid) is not in the open-advance report" '.success and ([.data[]|select(.purchaseOrderId == '$PO2')]|length) == 0'
call POST procurement/payments/action "{\"id\":$ADVID,\"action\":\"approve\"}"; check "r2 f12: advance request approved" '.success and .data.status == 1'
as "$TR_TOKEN" POST $FT/payments "{\"date\":\"${TODAY}T00:00:00\",\"purpose\":1,\"paymentRequestIds\":[$ADVID],\"lines\":[{\"instrument\":2,\"amount\":3900000,\"bankAccountId\":$BA1,\"reference\":\"ADV-R2\"}]}"; check "r2 f12: advance paid through treasury (drafted)" '.success and .data.netAmount == 3900000'
ADVPM=$(val .data.id)
as "$FM_TOKEN" POST $FT/payments/action "{\"id\":$ADVPM,\"action\":\"approve\"}"; as "$TR_TOKEN" POST $FT/payments/action "{\"id\":$ADVPM,\"action\":\"post\"}"; check "r2 f12: advance payment posted" '.success and .data.status == 2'
as "$FIN_TOKEN" GET procurement/suppliers/open-advances; check "r1 advance: the open-advance report shows exactly 3,900,000 for the order" '.success and ([.data[]|select(.purchaseOrderId == '$PO2' and .advanced == 3900000 and .recovered == 0 and .open == 3900000)]|length) == 1'
call POST inventory/documents "{\"type\":1,\"documentDate\":\"2026-10-22T00:00:00\",\"warehouseId\":$W1,\"purchaseOrderId\":$PO2,\"lines\":[{\"itemId\":$BOLT,\"quantity\":100,\"locationId\":$L2,\"purchaseOrderLineId\":$POL2}]}"; DR2=$(val .data.id); call POST "inventory/documents/$DR2/confirm"; check "r1 advance: 100 bolts received against the order" '.success'
as "$COM_TOKEN" POST procurement/invoices "{\"purchaseOrderId\":$PO2,\"invoiceNumber\":\"F-2001\",\"invoiceDate\":\"2026-10-24T00:00:00\",\"isOfficial\":true,\"lines\":[{\"orderLineId\":$POL2,\"quantity\":100,\"unitPrice\":390000}]}"; check "r1 advance: invoice of 42,900,000" '.success and .data.totalAmount == 42900000'
SI2=$(val .data.id); as "$FIN_TOKEN" POST procurement/invoices/action "{\"id\":$SI2,\"action\":\"approve\"}"
as "$COM_TOKEN" POST procurement/payments "{\"type\":3,\"invoiceId\":$SI2,\"amount\":42900000,\"deductionLines\":[{\"kind\":5,\"base\":0,\"ratePercent\":0,\"amount\":5000000}]}"; nomsg "r1 advance: recovering more than the unrecovered advance (5,000,000 > 3,900,000) is refused" "بازیابی پیش‌پرداخت"
as "$COM_TOKEN" POST procurement/payments "{\"type\":3,\"invoiceId\":$SI2,\"amount\":42900000,\"deductionLines\":[{\"kind\":4,\"base\":42900000,\"ratePercent\":0,\"amount\":900000,\"note\":\"جریمه تاخیر\"}]}"
check "r1 advance: the full invoice request gets the 3,900,000 recovery proposed next to the 900,000 penalty: deductions 4,800,000" '.success and .data.deductions == 4800000 and .data.netAmount == 38100000 and ([.data.deductionLines[]|select(.kind == 5 and .amount == 3900000)]|length) == 1 and ([.data.deductionLines[]|select(.kind == 4 and .amount == 900000)]|length) == 1'
PAY3=$(val .data.id)
as "$FIN_TOKEN" GET procurement/suppliers/open-advances; check "r1 advance: nothing is left open for the order" '([.data[]|select(.purchaseOrderId == '$PO2')]|length) == 0'
call POST procurement/payments/action "{\"id\":$PAY3,\"action\":\"approve\"}"; check "r1 advance: request approved" '.success and .data.status == 1'
as "$TR_TOKEN" POST $FT/payments "{\"date\":\"${TODAY}T00:00:00\",\"purpose\":1,\"paymentRequestIds\":[$PAY3],\"lines\":[{\"instrument\":2,\"amount\":36100000,\"bankAccountId\":$BA1,\"reference\":\"PAYREF-R1\"},{\"instrument\":3,\"amount\":2000000,\"bankAccountId\":$BAR,\"chequeSerial\":901,\"chequeDueDate\":\"${TODAY}T00:00:00\",\"chequeSayadId\":\"7777888899990000\"}]}"; check "r1 advance: treasury payment of the net 38,100,000 (transfer 36,100,000 and a 2,000,000 cheque)" '.success and .data.netAmount == 38100000'
PMA=$(val .data.id)
as "$FM_TOKEN" POST $FT/payments/action "{\"id\":$PMA,\"action\":\"approve\"}"; as "$TR_TOKEN" POST $FT/payments/action "{\"id\":$PMA,\"action\":\"post\"}"; PMANO=$(val .data.number); check "r1 advance: payment posted" '.success and .data.status == 2'
as "$FM_TOKEN" POST $FG/post-events
evlist; check "r1 advance: the payment event credits the supplier advance 3,900,000 and the penalty income 900,000 (not other payables)" '[.data.data[]|select(.sourceNumber == "'$PMANO'")][0].lines as $l | ([$l[]|select(.accountRole == "SupplierAdvance")|.credit]|add) == 3900000 and ([$l[]|select(.accountRole == "SupplierPenaltyIncome")|.credit]|add) == 900000 and ([$l[]|select(.accountRole == "OtherPayable")]|length) == 0'

as "$FM_TOKEN" POST $FG/post-events
as "$TR_TOKEN" POST $FT/statements/import "{\"bankAccountId\":$BAR,\"openingBalance\":0,\"closingBalance\":5000000,\"rows\":[{\"date\":\"${TODAY}T00:00:00\",\"description\":\"واریز\",\"deposit\":5000000,\"reference\":\"TRK-BAR\"}]}"; STB=$(val .data.id)
as "$TR_TOKEN" POST $FT/statements/$STB/auto-match; check "r1 bank: the deposit is matched with the receipt" '.success and .data.matched >= 1'
as "$TR_TOKEN" POST $FT/reconciliations "{\"statementId\":$STB}"; RCB=$(val .data.id)
as "$TR_TOKEN" POST $FT/reconciliations/action "{\"id\":$RCB,\"action\":\"refresh\"}"; check "r1 bank: bank 5,000,000, book 5,000,000, cheque of 2,000,000 issued and not presented (informational): difference 0" '.success and .data.bankBalance == 5000000 and .data.bookBalance == 5000000 and .data.outstandingCheques == 2000000 and .data.difference == 0'
as "$FM_TOKEN" POST $FT/reconciliations/action "{\"id\":$RCB,\"action\":\"approve\"}"; check "r1 bank: the reconciliation with an unpresented cheque is approvable" '.success and .data.status == 1'

# --- 9 and 21: retention release and the sales advance cap
as "$FM_TOKEN" GET "$FR/invoices/$INV1"; HELD=$(val ".data.performanceRetention|round")
[ -n "$HELD" ] && [ "$HELD" != "null" ] && [ "$HELD" -gt 0 ] && ok "r1 retention: the posted invoice holds a performance retention ($HELD)" || bad "r1 retention: the posted invoice holds a performance retention ($HELD)"
RELJ="{\"date\":\"${TODAY}T00:00:00\",\"customerId\":$CU1,\"type\":7,\"description\":\"آزادسازی حسن انجام کار\",\"lines\":[{\"description\":\"آزادسازی\",\"quantity\":1,\"unitPrice\":$HELD}]}"
as "$FIN_TOKEN" POST $FR/invoices "$(jq -c '.lines[0].unitPrice += 1' <<< "$RELJ")"; nomsg "r1 retention: a release above the retention held is refused" "حسن انجام کار نگهداری"
as "$FIN_TOKEN" POST $FR/invoices "$RELJ"; check "r1 retention: the release has no VAT and a receivable equal to the retention" '.success and .data.vatAmount == 0 and .data.receivableAmount == '$HELD
REL=$(val .data.id)
as "$FM_TOKEN" POST $FR/invoices/action "{\"id\":$REL,\"action\":\"approve\"}"; RELNO=$(val .data.number); check "r1 retention: the release is approved" '.success and .data.status == 2'
as "$FM_TOKEN" POST $FG/post-events
evlist; check "r1 retention: the event is Dr receivable / Cr retention receivable for the retention, with no revenue and no VAT" '[.data.data[]|select(.sourceNumber == "'$RELNO'")][0].lines as $l | ([$l[]|select(.accountRole == "AccountsReceivable")|.debit]|add) == '$HELD' and ([$l[]|select(.accountRole == "RetentionReceivable")|.credit]|add) == '$HELD' and ($l|length) == 2'
as "$FIN_TOKEN" POST $FR/invoices "$(jq -c '.advanceRecovery = 1000000' <<< "$INVJ")"; nomsg "r1 sales advance: recovery above the advance received (none) is refused" "پیش‌دریافت"

# --- 16: the dashboard profit is final vouchers only and reconciles with the income statement
as "$FM_TOKEN" GET finance/dashboard; N0=$(val .data.netProfitYtd)
as "$FIN_TOKEN" POST $FG/vouchers "{\"date\":\"${TODAY}T00:00:00\",\"description\":\"پیش‌نویس داشبورد\",\"lines\":[{\"accountCode\":\"10101\",\"debit\":777000},{\"accountCode\":\"60102\",\"credit\":777000}]}"; VDR=$(val .data.id)
as "$FM_TOKEN" GET finance/dashboard; N1=$(val .data.netProfitYtd)
[ "$N0" = "$N1" ] && ok "r1 dashboard: a draft voucher of 777,000 does not change the net profit" || bad "r1 dashboard: a draft voucher does not change the net profit ($N0 -> $N1)"
as "$FIN_TOKEN" DELETE "$FG/vouchers/$VDR"
as "$FM_TOKEN" GET "$FG/reports/income-statement?FiscalYearId=$FYC&From=2026-03-21&To=$TODAY"; IS=$(val .data.netProfit)
[ "$IS" = "$N0" ] && ok "r1 dashboard: the net profit equals the income statement of the year to date ($IS)" || bad "r1 dashboard: net profit equals the income statement (dashboard $N0, statement $IS)"

# --- 8, 15 and 17: bonus paid from the provision, severance base, employee loan, payroll rows
as "$PAYR_TOKEN" POST $FP/loans "{\"employeeId\":$PE1,\"kind\":1,\"title\":\"وام آزمون\",\"principal\":12000000,\"installmentAmount\":2000000,\"firstYear\":1405,\"firstMonth\":9}"; check "r1 loan: loan of 12,000,000 in instalments of 2,000,000 saved" '.success and .data.balance == 12000000'
LN=$(val .data.id)
E0=$(tbc 10401)
as "$TR_TOKEN" POST $FT/payments "{\"date\":\"${TODAY}T00:00:00\",\"purpose\":4,\"employeeLoanId\":$LN,\"lines\":[{\"instrument\":2,\"amount\":12000000,\"bankAccountId\":$BA1,\"reference\":\"LOAN-1\"}]}"; check "r1 loan: disbursement drafted for the principal" '.success and .data.purpose == 4 and .data.netAmount == 12000000'
PML=$(val .data.id)
as "$TR_TOKEN" POST $FT/payments "{\"date\":\"${TODAY}T00:00:00\",\"purpose\":4,\"employeeLoanId\":$LN,\"lines\":[{\"instrument\":2,\"amount\":12000000,\"bankAccountId\":$BA1}]}"; nomsg "r1 loan: the same loan cannot be paid in two documents" "قبلاً"
as "$FM_TOKEN" POST $FT/payments/action "{\"id\":$PML,\"action\":\"approve\"}"; as "$TR_TOKEN" POST $FT/payments/action "{\"id\":$PML,\"action\":\"post\"}"; check "r1 loan: disbursement posted" '.success and .data.status == 2'
as "$FM_TOKEN" POST $FG/post-events
[ "$(( $(tbc 10401) - E0 ))" = "12000000" ] && ok "r1 loan: employee advances 10401 rose by exactly 12,000,000" || bad "r1 loan: 10401 rose by 12,000,000 (delta $(( $(tbc 10401) - E0 )))"
as "$FM_TOKEN" GET "$FG/details?Type=4&PageSize=0"; check "r1 loan: the receivable is kept per employee (an EMP- detail exists)" '([.data.data[]|select(.code|startswith("EMP-"))]|length) >= 1'
# --- part months: the engine derives the days before the hire date and after the end date itself (30-day basis)
EJD() { EJ "$1" "$2" "$3" "$4" IR270170000000100324200001 | jq -c --arg h "$5" --arg e "$6" '.hireDate = $h | if $e != "" then .endDate = $e else . end'; }
as "$PAYR_TOKEN" POST $FP/employees "$(EJD T101 آزمون ورود 1234567806 2026-05-01T00:00:00 '')"; PEH=$(val .data.id)
as "$PAYR_TOKEN" POST $FP/employees "$(EJD T102 آزمون خروج 2345678909 2020-01-01T00:00:00 2026-11-11T00:00:00)"; PEX=$(val .data.id)
as "$PAYR_TOKEN" POST $FP/employees "$(EJD T103 آزمون اسفند 3456789017 2027-03-07T00:00:00 '')"; PE29=$(val .data.id)
as "$PAYR_TOKEN" POST $FP/attendance "{\"employeeId\":$PEH,\"year\":1405,\"month\":2,\"daysWorked\":21}"
as "$PAYR_TOKEN" POST $FP/runs '{"year":1405,"month":2}'; PRX=$(val .data.id)
as "$PAYR_TOKEN" POST $FP/runs/$PRX/calculate
as "$PAYR_TOKEN" GET "$FP/payslips?RunId=$PRX"; check "payroll: hired on day 11 of 31-day Ordibehesht: 20 payable days, base wage 12000000" '.success and (.data.data|length) == 1 and .data.data[0].daysPaid == 20 and ([.data.data[0].lines[]|select(.itemCode == "BASE_WAGE" and .amount == 12000000)]|length) == 1'
as "$PAYR_TOKEN" DELETE $FP/runs/$PRX
as "$PAYR_TOKEN" POST $FP/attendance "{\"employeeId\":$PEX,\"year\":1405,\"month\":8,\"daysWorked\":20}"
as "$PAYR_TOKEN" POST $FP/runs '{"year":1405,"month":8}'; PRX=$(val .data.id)
as "$PAYR_TOKEN" POST $FP/runs/$PRX/calculate
as "$PAYR_TOKEN" GET "$FP/payslips?RunId=$PRX"; check "payroll: terminated on day 20 of 30-day Aban: 20 payable days, base wage 12000000" '.success and (.data.data|length) == 1 and .data.data[0].daysPaid == 20 and ([.data.data[0].lines[]|select(.itemCode == "BASE_WAGE" and .amount == 12000000)]|length) == 1'
as "$PAYR_TOKEN" DELETE $FP/runs/$PRX
as "$PAYR_TOKEN" POST $FP/attendance "{\"employeeId\":$PE29,\"year\":1405,\"month\":12,\"daysWorked\":14}"
as "$PAYR_TOKEN" POST $FP/runs '{"year":1405,"month":12}'; PRX=$(val .data.id)
as "$PAYR_TOKEN" POST $FP/runs/$PRX/calculate
as "$PAYR_TOKEN" GET "$FP/payslips?RunId=$PRX"; check "payroll: hired on day 16 of the 29-day Esfand: 15 payable days, base wage 9000000; the full-month employee has 30" '.success and ([.data.data[]|select(.daysPaid == 15 and ([.lines[]|select(.itemCode == "BASE_WAGE" and .amount == 9000000)]|length) == 1)]|length) == 1 and ([.data.data[]|select(.daysPaid == 30)]|length) == 1'
as "$PAYR_TOKEN" DELETE $FP/runs/$PRX
as "$PAYR_TOKEN" POST $FP/attendance "{\"employeeId\":$PE1,\"year\":1405,\"month\":9,\"daysWorked\":30,\"inputs\":{\"BONUS\":5000000}}"
as "$PAYR_TOKEN" POST $FP/runs '{"year":1405,"month":9}'; PR12=$(val .data.id)
as "$PAYR_TOKEN" POST $FP/runs/$PR12/calculate; check "r1 payroll: Azar with a bonus of 5,000,000: gross 36,300,000" '.success and .data.run.totalGross == 36300000'
as "$PAYR_TOKEN" GET "$FP/payslips?RunId=$PR12"; check "r1 payroll: severance accrues on base pay items only (31,300,000 / 12 = 2,608,333), not on the bonus; the loan instalment is 2,000,000" '([.data.data[].lines[]|select(.itemCode == "SEVERANCE" and .amount == 2608333)]|length) == 1 and ([.data.data[].lines[]|select(.itemCode == "LOAN" and .amount == 2000000)]|length) == 1'
as "$FM_TOKEN" POST $FP/runs/$PR12/approve; check "r1 payroll: Azar run approved" '.success and .data.status == 2'
B0=$(tbc 30402)
as "$PAYR_TOKEN" POST $FP/runs/$PR12/post; PR12EV=$(val .data.accountingEventId); check "r1 payroll: Azar run posted" '.success and .data.status == 3'
as "$FM_TOKEN" POST $FG/post-events
evlist; check "r2 f23: the monthly BONUS is an expense and does not touch the provision: payroll expense 40,800,000 = 31,300,000 pay + 5,000,000 bonus + 3,000,000 bonus accrual + 1,500,000 leave accrual" '[.data.data[]|select(.id == '$PR12EV')][0].lines as $l | ([$l[]|select(.accountRole == "BonusProvision")|.debit]|add // 0) == 0 and ([$l[]|select((.accountRole|startswith("PayrollExpense")))|.debit]|add) == 40800000'
[ "$(( $(tbc 30402) - B0 ))" = "-3000000" ] && ok "r2 f23: provision 30402 closing-debit movement -3,000,000 i.e. credit +3,000,000 (only the monthly accrual; the monthly bonus no longer drains it)" || bad "r2 f23: 30402 net movement +3,000,000 (delta $(( $(tbc 30402) - B0 )))"
[ "$(( $(tbc 10401) - E0 ))" = "10000000" ] && ok "r1 loan: after the instalment 10401 holds 10,000,000 (12,000,000 - 2,000,000)" || bad "r1 loan: 10401 holds 10,000,000 after the instalment (delta $(( $(tbc 10401) - E0 )))"
TOKEN=$ADMIN_TOKEN


# ---------------------------------------------------------------- finance completeness (release F3, milestone 3): loans, tax books, statements, collections
TOKEN=$ADMIN_TOKEN
FLN=finance/loans; FST=finance/statements; FTX=finance/tax; FCL=finance/collections
FR=finance/receivables
# --- fiscal year 1403 with eight vouchers: the exact statements below come from these
as "$FM_TOKEN" POST $FG/years/ensure '{"date":"2024-06-01T00:00:00"}'; check "f3 m3: fiscal year 1403 ensured (a leap year)" '.success and .data.year == 1403'
Y3=$(val .data.id)
fv "{\"date\":\"2024-04-10T00:00:00\",\"description\":\"سرمایه\",\"lines\":[{\"accountCode\":\"10102\",\"debit\":1000000000},{\"accountCode\":\"50101\",\"credit\":1000000000}]}"
fv "{\"date\":\"2024-04-20T00:00:00\",\"description\":\"خرید دارایی ثابت\",\"lines\":[{\"accountCode\":\"20103\",\"debit\":300000000},{\"accountCode\":\"10102\",\"credit\":300000000}]}"
fv "{\"date\":\"2024-05-10T00:00:00\",\"description\":\"فروش اعتباری\",\"lines\":[{\"accountCode\":\"10301\",\"debit\":400000000,\"detail1Code\":\"$FXCUS\"},{\"accountCode\":\"60102\",\"credit\":400000000}]}"
fv "{\"date\":\"2024-05-20T00:00:00\",\"description\":\"بهای تمام شده فروش\",\"lines\":[{\"accountCode\":\"70101\",\"debit\":150000000},{\"accountCode\":\"30101\",\"credit\":150000000,\"detail1Code\":\"$FXSUP\"}]}"
fv "{\"date\":\"2024-06-10T00:00:00\",\"description\":\"استهلاک\",\"lines\":[{\"accountCode\":\"80104\",\"debit\":30000000},{\"accountCode\":\"20203\",\"credit\":30000000}]}"
fv "{\"date\":\"2024-07-10T00:00:00\",\"description\":\"تسهیلات دریافتی\",\"lines\":[{\"accountCode\":\"10102\",\"debit\":200000000},{\"accountCode\":\"30501\",\"credit\":200000000}]}"
fv "{\"date\":\"2024-07-20T00:00:00\",\"description\":\"وصول از مشتری\",\"lines\":[{\"accountCode\":\"10102\",\"debit\":100000000},{\"accountCode\":\"10301\",\"credit\":100000000,\"detail1Code\":\"$FXCUS\"}]}"
fv "{\"date\":\"2024-08-10T00:00:00\",\"description\":\"هزینه اداری\",\"lines\":[{\"accountCode\":\"80108\",\"debit\":50000000},{\"accountCode\":\"10102\",\"credit\":50000000}]}"


# --- section 6: loans (Persian calendar schedule, exact annuity)
as "$FM_TOKEN" POST $FLN/limits '{"bankName":"بانک آزمون F3","limitAmount":2000000000,"validFrom":"2025-03-21T00:00:00","validTo":"2026-03-20T00:00:00","note":"سقف آزمون"}'; check "f3 loan: facility limit defined" '.success and .data.limitAmount == 2000000000'
LIM=$(val .data.id)
as "$FIN_TOKEN" POST $FLN/limits '{"bankName":"x","limitAmount":1,"validFrom":"2025-03-21T00:00:00"}'; no "f3 loan: only the finance manager defines limits"
as "$TR_TOKEN" POST $FLN "{\"bankName\":\"بانک آزمون F3\",\"contractNo\":\"LN-F3-1\",\"amount\":1200000000,\"startDate\":\"2025-06-22T00:00:00\",\"ratePercent\":24,\"scheduleKind\":2,\"installmentCount\":12,\"feeAmount\":1000000,\"facilityLimitId\":$LIM,\"bankAccountId\":$BA1}"; check "f3 loan: annuity schedule 1,200,000,000 at 24% over 12 months" '.success and .data.status == 0 and (.data.installments|length) == 12 and .data.installments[0].principal == 89471516 and .data.installments[0].interest == 24000000 and .data.installments[1].principal == 91260946 and .data.installments[1].interest == 22210570 and .data.installments[2].principal == 93086165 and .data.installments[2].interest == 20385351 and .data.installments[11].principal == 111246584 and .data.installments[11].interest == 2224932 and ([.data.installments[].principal]|add) == 1200000000 and .data.totalInterest == 161658192'
LN1=$(val .data.id)
as "$TR_TOKEN" GET $FLN/$LN1; check "f3 loan: instalment dates follow the Persian months (1 Mordad 1404 = 2025-07-23)" '.success and (.data.installments[0].dueDate|startswith("2025-07-23")) and (.data.installments[1].dueDate|startswith("2025-08-23"))'
as "$TR_TOKEN" POST $FLN "{\"bankName\":\"بانک\",\"amount\":300000000,\"startDate\":\"2025-06-22T00:00:00\",\"ratePercent\":12,\"scheduleKind\":1,\"installmentCount\":3}"; check "f3 loan: equal-principal schedule has falling interest 3,000,000 / 2,000,000 / 1,000,000" '.success and ([.data.installments[].principal]|all(. == 100000000)) and ([.data.installments[].interest] == [3000000,2000000,1000000])'
LN2=$(val .data.id)
as "$TR_TOKEN" POST $FLN '{"bankName":"x","amount":0,"startDate":"2025-06-22T00:00:00","ratePercent":10,"installmentCount":3}'; no "f3 loan: amount must be positive"
as "$AUD_TOKEN" POST $FLN/action "{\"id\":$LN1,\"action\":\"disburse\",\"date\":\"2025-06-22T00:00:00\",\"bankAccountId\":$BA1}"; no "f3 loan: the auditor cannot disburse"
as "$TR_TOKEN" POST $FLN/pay "{\"loanId\":$LN1,\"sequence\":1,\"bankAccountId\":$BA1,\"date\":\"2025-07-23T00:00:00\"}"; no "f3 loan: an instalment cannot be paid before the loan is disbursed"
as "$TR_TOKEN" POST $FLN/action "{\"id\":$LN1,\"action\":\"disburse\",\"date\":\"2025-06-22T00:00:00\",\"bankAccountId\":$BA1}"; check "f3 loan: disbursement posts a voucher and activates the loan" '.success and .data.status == 1 and .data.disbursementVoucherId > 0 and .data.outstandingPrincipal == 1200000000'
as "$TR_TOKEN" POST $FLN/action "{\"id\":$LN1,\"action\":\"disburse\",\"date\":\"2025-06-22T00:00:00\",\"bankAccountId\":$BA1}"; no "f3 loan: no second disbursement"
as "$FM_TOKEN" GET $FLN/utilisation; check "f3 loan: the limit shows 1,200,000,000 used and 800,000,000 available" '.success and ([.data.facilities[]|select(.id == '$LIM')][0]|(.utilised == 1200000000 and .available == 800000000))'
as "$FIN_TOKEN" POST $FLN/accrual/run/$P4; check "f3 loan: accrual for Tir drafts one row" '.success and (.data|length) == 1 and .data[0].status == 0'
AC1=$(val '.data[0].id'); LNACC=$(val '.data[0].amount')
as "$TR_TOKEN" GET $FLN/$LN1; LNI1=$(val '.data.installments[0].interest')
[ "$LNACC" = "$LNI1" ] && [ "$LNI1" != "null" ] && [ "${LNI1%.*}" -gt 0 ] && ok "r2 f22: the Tir accrual equals the whole first-instalment interest ($LNI1): the period covers the 31 days from disbursement to the day before the first due date" || bad "r2 f22: the Tir accrual ($LNACC) equals the first-instalment interest ($LNI1)"
as "$FIN_TOKEN" POST $FLN/accrual/$AC1/approve; no "f3 loan: the preparer cannot approve the accrual"
as "$FIN2_TOKEN" POST $FLN/accrual/$AC1/approve; check "f3 loan: the second person approves the accrual; a voucher is posted" '.success and .data.status == 1 and .data.voucherId > 0'
as "$FIN_TOKEN" POST $FLN/accrual/run/$P4; check "f3 loan: the accrual run is idempotent" '.success and (.data|length) == 0'
as "$TR_TOKEN" POST $FLN/pay "{\"loanId\":$LN1,\"sequence\":2,\"bankAccountId\":$BA1,\"date\":\"2025-08-23T00:00:00\"}"; no "f3 loan: instalments are paid in order"
as "$TR_TOKEN" POST $FLN/pay "{\"loanId\":$LN1,\"sequence\":1,\"bankAccountId\":$BA1,\"date\":\"2025-07-23T00:00:00\"}"; check "f3 loan: instalment 1 paid (113,471,516) with a voucher" '.success and .data.installments[0].status == 1 and .data.installments[0].voucherId > 0 and .data.outstandingPrincipal == 1110528484'
as "$TR_TOKEN" POST $FLN/pay "{\"loanId\":$LN1,\"sequence\":1,\"bankAccountId\":$BA1,\"date\":\"2025-07-23T00:00:00\"}"; no "f3 loan: an instalment is paid once"
as "$TR_TOKEN" POST $FLN/pay "{\"loanId\":$LN1,\"sequence\":2,\"bankAccountId\":$BA1,\"date\":\"2025-08-23T00:00:00\",\"penalty\":500000}"; check "f3 loan: instalment 2 with a late penalty of 500,000" '.success and .data.installments[1].status == 1 and .data.installments[1].penaltyPaid == 500000'
as "$TR_TOKEN" POST $FLN/pay "{\"loanId\":$LN1,\"sequence\":3,\"bankAccountId\":$BA1,\"date\":\"2025-09-23T00:00:00\"}"; check "f3 loan: after three payments the outstanding principal is 926,181,373" '.success and .data.outstandingPrincipal == 926181373'
as "$TR_TOKEN" POST $FLN/action "{\"id\":$LN2,\"action\":\"cancel\"}"; check "f3 loan: a draft loan can be cancelled" '.success and .data.status == 3'
as "$AUD_TOKEN" GET $FLN; check "f3 loan: the auditor can read loans" '.success and (.data|length) >= 1'

# --- section 7: tax books
as "$FM_TOKEN" GET "$FTX/vat-season?year=1405&season=3"; VATT=$(mktemp); cp "$B" "$VATT"
as "$FM_TOKEN" GET "$FTX/vat-season/detail?year=1405&season=3"; check "f3 tax: the document list adds up to the season report" '.success and (.data.salesVat == '$(jq .data.salesVat $VATT)') and (.data.purchasesVat == '$(jq .data.purchasesVat $VATT)') and (.data.vatPayable == '$(jq .data.vatPayable $VATT)') and ([.data.rows[]|select(.side == "Sales")|.vatAmount]|add // 0) == .data.salesVat and ([.data.rows[]|select(.side == "Purchases")|.vatAmount]|add // 0) == .data.purchasesVat'
for SN in 1 2 3; do as "$FM_TOKEN" GET "$FTX/vat-season/detail?year=1405&season=$SN"; cp "$B" "$VATT.s$SN"; done
check_carry() { jq -e -s 'def mx(x): if x > 0 then x else 0 end; .[0].data as $a | .[1].data as $b | .[2].data as $c
  | ($c.vatPayable == $c.salesVat - $c.purchasesVat) and ($b.carryIn == mx(-($a.vatPayable - $a.carryIn))) and ($c.carryIn == mx(-($b.vatPayable - $b.carryIn))) and ($c.carryOut == mx(-($c.vatPayable - $c.carryIn))) and ($c.netPayable == mx($c.vatPayable - $c.carryIn))' "$VATT.s1" "$VATT.s2" "$VATT.s3" >/dev/null; }
if check_carry; then ok "r2 f22: VAT carry chain is exact (payable = sales - purchases VAT; each season's carry-in = the previous season's credit position; carry-out and net payable follow)"; else bad "r2 f22: VAT carry chain is exact"; fi
as "$FM_TOKEN" GET "$FTX/vat-season/detail?year=1405&season=3"; check "f3 tax: carry-forward arithmetic (net payable - carry out = payable - carry in)" '.success and (.data.netPayable - .data.carryOut) == (.data.vatPayable - .data.carryIn)'
as "$FM_TOKEN" GET "$FTX/vat-season/detail?year=1405&season=5"; no "f3 tax: season out of range"
as "$AUD_TOKEN" GET "$FTX/vat-season/detail?year=1405&season=3"; check "f3 tax: the auditor can read the document list" '.success'
as "$FIN_TOKEN" POST $FTX/adjustments "{\"fiscalYearId\":$Y3,\"kind\":1,\"description\":\"هزینه غیرمجاز آزمون\",\"amount\":10000000,\"source\":\"مرجع آزمون\",\"verify\":true}"; check "f3 tax: non-deductible expense (+10,000,000) recorded" '.success and .data.amount == 10000000'
as "$FIN_TOKEN" POST $FTX/adjustments "{\"fiscalYearId\":$Y3,\"kind\":2,\"description\":\"معافیت آزمون\",\"amount\":-20000000,\"verify\":true}"; check "f3 tax: exemption (-20,000,000) recorded" '.success'
as "$FIN_TOKEN" POST $FTX/adjustments "{\"fiscalYearId\":$Y3,\"kind\":3,\"description\":\"زیان انتقالی آزمون\",\"amount\":-5000000,\"verify\":true}"; check "f3 tax: loss carried forward (-5,000,000) recorded" '.success'
as "$FIN_TOKEN" POST $FTX/adjustments "{\"fiscalYearId\":$Y3,\"kind\":1,\"description\":\"x\",\"amount\":0}"; no "f3 tax: an adjustment of zero is refused"
as "$FM_TOKEN" GET "$FTX/taxable-profit?FiscalYearId=$Y3&RatePercent=25"; check "f3 tax: taxable profit 170M + 10M - 20M - 5M = 155M and tax at 25% = 38,750,000" '.success and .data.bookProfitBeforeTax == 170000000 and .data.adjustments == -15000000 and .data.taxableProfit == 155000000 and .data.taxAmount == 38750000'
as "$AUD_TOKEN" POST $FTX/taxable-profit/provision "{\"fiscalYearId\":$Y3,\"ratePercent\":25}"; no "f3 tax: the auditor cannot create the provision voucher"
as "$FIN_TOKEN" POST $FTX/taxable-profit/provision "{\"fiscalYearId\":$Y3}"; no "f3 tax: the provision needs a rate (none is assumed)"
as "$FIN_TOKEN" POST $FTX/taxable-profit/provision "{\"fiscalYearId\":$Y3,\"ratePercent\":25}"; check "f3 tax: a draft provision voucher for 38,750,000 is created" '.success and .data.provisionVoucherId > 0 and .data.taxAmount == 38750000'
TPV=$(val .data.provisionVoucherId)
as "$FM_TOKEN" GET $FG/vouchers/$TPV; check "f3 tax: the provision voucher is a draft (status 0), balanced" '.success and .data.status == 0 and .data.totalAmount == 38750000'
as "$FIN_TOKEN" POST $FTX/taxable-profit/provision "{\"fiscalYearId\":$Y3,\"ratePercent\":25}"; nomsg "r3 m8: a second provision run is refused while the first is still a draft (it used to create a second draft of 38,750,000)" "پیش‌نویس"
as "$FM_TOKEN" GET "$FTX/taxable-profit?FiscalYearId=$Y3&RatePercent=25"; check "f3 tax: the draft provision does not change the taxable profit" '.success and .data.taxableProfit == 155000000'

# --- section 8: statements on fiscal year 1403 (366 days, a leap year)
as "$FM_TOKEN" GET "$FST/cash-flow?FiscalYearId=$Y3"; check "f3 stmt: cash flow ties to the cash accounts (opening 0, closing 950,000,000)" '.success and .data.openingCash == 0 and .data.closingCash == 950000000 and .data.ties == true'
as "$FM_TOKEN" GET "$FST/cash-flow?FiscalYearId=$Y3"; check "r2 f22: the catch-all Other line is absent (every account is classified), and the lines add up to the three sections exactly" '.success and ([.data.lines[]|select(.key == "Other")]|length) == 0 and (([.data.lines[]|select(.section == "Investing")|.amount]|add // 0) == -300000000) and (([.data.lines[]|select(.section == "Financing")|.amount]|add // 0) == 1200000000)'
as "$FM_TOKEN" GET "$FST/cash-flow?FiscalYearId=$Y3"; check "f3 stmt: operating 50M, investing -300M, financing 1,200M, net profit 170M" '.success and .data.operating == 50000000 and .data.investing == -300000000 and .data.financing == 1200000000 and .data.netProfit == 170000000 and .data.netChange == 950000000'
as "$FM_TOKEN" GET "$FST/equity-changes?FiscalYearId=$Y3"; check "f3 stmt: equity 1,170,000,000 and ties to assets minus liabilities" '.success and .data.closingEquity == 1170000000 and .data.balanceSheetEquity == 1170000000 and .data.ties == true and .data.netProfit == 170000000'
as "$FM_TOKEN" GET "$FST/ratios?FiscalYearId=$Y3"; check "f3 stmt: ratios current 3.5714, debt 0.2303, gross margin 0.625, net margin 0.425" '.success and ([.data.ratios[]|select(.key == "CurrentRatio")][0].value) == 3.5714 and ([.data.ratios[]|select(.key == "DebtRatio")][0].value) == 0.2303 and ([.data.ratios[]|select(.key == "GrossMargin")][0].value) == 0.625 and ([.data.ratios[]|select(.key == "NetMargin")][0].value) == 0.425'
as "$FM_TOKEN" GET "$FST/ratios?FiscalYearId=$Y3"; check "f3 stmt: DSO 274.5 and DPO 366 days over a 366-day year" '.success and .data.days == 366 and ([.data.ratios[]|select(.key == "DSO")][0].value) == 274.5 and ([.data.ratios[]|select(.key == "DPO")][0].value) == 366'
as "$FM_TOKEN" GET "$FST/drill?account=601&FiscalYearId=$Y3"; check "f3 stmt: drill-down on revenue lists the voucher and the total equals the statement" '.success and (.data.rows|length) == 1 and .data.totalCredit == 400000000 and .data.rows[0].voucherId > 0'
as "$AUD_TOKEN" GET "$FST/cash-flow?FiscalYearId=$Y3"; check "f3 stmt: the auditor can read the statements" '.success and .data.ties == true'
as "$PAYR_TOKEN" GET "$FST/cash-flow?FiscalYearId=$Y3"; [ "$CODE" = "403" ] && ok "f3 stmt: payroll staff are not finance readers (403)" || bad "f3 stmt: payroll staff are not finance readers (403) got $CODE"
as "$FM_TOKEN" GET "$FST/cash-flow?FiscalYearId=999999"; no "f3 stmt: unknown fiscal year"
as "$FIN_TOKEN" POST $FST/views '{"reportKey":"cash-flow","title":"نمای آزمون","filtersJson":"{\"q\":1}","shared":false}'; check "f3 stmt: a saved view" '.success and .data.id > 0'
VW=$(val .data.id)
as "$FIN2_TOKEN" GET $FST/views; check "f3 stmt: a private view is hidden from other users" '.success and ([.data[]|select(.id == '$VW')]|length) == 0'
as "$FIN_TOKEN" GET $FST/views; check "f3 stmt: the owner sees the view" '.success and ([.data[]|select(.id == '$VW')]|length) == 1'

# --- section 10: receivables and payables control
as "$FIN_TOKEN" GET "$FCL/bad-debt?AsOf=2025-08-01"; no "f3 debt: the rates are inputs; none entered, nothing assumed"
as "$FIN_TOKEN" GET "$FCL/bad-debt?AsOf=$TODAY&CurrentRate=1&Days31To60Rate=5&Days61To90Rate=20&Over90Rate=50"; check "f3 debt: preview of the provision by ageing bucket" '.success and (.data.buckets|length) == 4 and .data.voucherId == null and .data.required == ([.data.buckets[].required]|add) and .data.change == (.data.required - .data.previousBalance)'
BDREQ=$(jq -r '.data.required|round' "$B")
BDJ="{\"asOf\":\"${TODAY}T00:00:00\",\"currentRate\":1,\"days31To60Rate\":5,\"days61To90Rate\":20,\"over90Rate\":50}"
as "$AUD_TOKEN" POST $FCL/bad-debt "$BDJ"; no "f3 debt: the auditor cannot post the provision"
if [ "$BDREQ" != "0" ]; then
  as "$FIN_TOKEN" POST $FCL/bad-debt "$BDJ"; check "f3 debt: the provision voucher is posted" '.success and .data.voucherId > 0 and (.data.number|startswith("BD-"))'
  as "$FIN_TOKEN" POST $FCL/bad-debt "$BDJ"; no "f3 debt: a second run with unchanged balances posts nothing"
else
  ok "f3 debt: no receivables, nothing to post"
fi
as "$FIN_TOKEN" POST $FR/customers '{"name":"مشتری توقف اعتبار آزمون","economicCode":"411111111122","nationalId":"10101010102","postalCode":"1234567891","employerName":"مشتری توقف اعتبار آزمون"}'; HC=$(val .data.id)
as "$FIN_TOKEN" POST $FCL/holds "{\"customerId\":$HC,\"active\":true}"; no "f3 hold: a reason is required"
as "$FIN_TOKEN" POST $FCL/holds "{\"customerId\":$HC,\"active\":true,\"reason\":\"بدهی سررسیدگذشته\"}"; check "f3 hold: customer put on credit hold" '.success and .data.active == true'
as "$FM_TOKEN" GET $FCL/holds; check "f3 hold: the hold is listed" '.success and ([.data[]|select(.customerId == '$HC')]|length) == 1'
as "$FIN_TOKEN" POST $FCL/holds "{\"customerId\":$HC,\"active\":false}"; check "f3 hold: the hold is lifted" '.success and .data.active == false'
as "$FIN_TOKEN" POST "$FCL/dunning/run?asOf=2027-01-01"; check "f3 dunning: drafts are created for overdue customers (or none when nothing is overdue)" '.success and (.data|type) == "array"'
as "$FM_TOKEN" GET $FCL/dunning; check "f3 dunning: the list is readable" '.success'
# an approved supplier invoice that no payment request covers yet (10 bolts x 390,000 + 10% VAT = 4,290,000)
as "$WH_TOKEN" POST procurement/requests "{\"source\":2,\"kind\":1,\"requestDate\":\"2026-10-22T00:00:00\",\"description\":\"پیشنهاد پرداخت\",\"submit\":true,\"lines\":[{\"itemId\":$BOLT,\"quantity\":10,\"estimatedUnitPrice\":400000,\"technicalRequirements\":\"ISO 4014\"}]}"; RQ3=$(val .data.id); RQ3L=$(val '.data.lines[0].id')
as "$PLAN_TOKEN" POST procurement/requests/action "{\"id\":$RQ3,\"action\":\"approve-planning\"}"; as "$CEO_TOKEN" POST procurement/requests/action "{\"id\":$RQ3,\"action\":\"approve-management\"}"
QA3="{\"providerId\":$PA,\"quoteNumber\":\"P-A\",\"vatPercent\":10,\"deliveryDays\":20,\"technicallyAccepted\":true,\"lines\":[{\"requestLineId\":$RQ3L,\"quantity\":10,\"unitPrice\":390000}]}"
QB3="{\"providerId\":$PB,\"quoteNumber\":\"P-B\",\"vatPercent\":10,\"technicallyAccepted\":true,\"lines\":[{\"requestLineId\":$RQ3L,\"quantity\":10,\"unitPrice\":380000}]}"
as "$COM_TOKEN" POST procurement/inquiries "{\"purchaseRequestId\":$RQ3,\"inquiryDate\":\"2026-10-22T00:00:00\",\"quotes\":[$QA3,$QB3]}"; IQ3=$(val .data.id); QA3ID=$(val ".data.quotes[]|select(.providerId == $PA)|.id")
as "$COM_TOKEN" POST procurement/inquiries/select "{\"inquiryId\":$IQ3,\"quoteId\":$QA3ID,\"reason\":\"گرید A\"}"
as "$COM_TOKEN" POST "procurement/orders/from-inquiry/$IQ3"; PO3=$(val .data.id); POL3=$(val '.data.lines[0].id')
call POST procurement/orders/action "{\"id\":$PO3,\"action\":\"approve\"}"; as "$COM_TOKEN" POST procurement/orders/action "{\"id\":$PO3,\"action\":\"release\"}"; check "f3 proposal setup: order released" '.success and .data.status == 2'
call POST inventory/documents "{\"type\":1,\"documentDate\":\"2026-10-22T00:00:00\",\"warehouseId\":$W1,\"purchaseOrderId\":$PO3,\"lines\":[{\"itemId\":$BOLT,\"quantity\":10,\"locationId\":$L2,\"purchaseOrderLineId\":$POL3}]}"; DR3=$(val .data.id); call POST "inventory/documents/$DR3/confirm"; check "f3 proposal setup: 10 bolts received" '.success'
as "$COM_TOKEN" POST procurement/invoices "{\"purchaseOrderId\":$PO3,\"invoiceNumber\":\"F-3001\",\"invoiceDate\":\"2026-10-24T00:00:00\",\"isOfficial\":true,\"lines\":[{\"orderLineId\":$POL3,\"quantity\":10,\"unitPrice\":390000}]}"; SI3=$(val .data.id)
as "$FIN_TOKEN" POST procurement/invoices/action "{\"id\":$SI3,\"action\":\"approve\"}"; check "f3 proposal setup: invoice F-3001 of 4,290,000 approved" '.success and .data.status == 3 and .data.totalAmount == 4290000'
as "$FIN_TOKEN" POST "$FCL/payment-proposals" '{"asOf":"2020-01-01T00:00:00"}'; no "f3 proposal: nothing is due that early"
as "$FIN_TOKEN" POST "$FCL/payment-proposals" '{"asOf":"2027-12-31T00:00:00","cashLimit":0}'; check "f3 proposal: the approved unpaid invoice F-3001 (4,290,000) is proposed, by due date, no limit" '.success and ([.data.lines[]|select(.amount == 4290000)]|length) == 1 and (.data.lines|length) >= 1 and .data.total == ([.data.lines[]|select(.included)|.amount]|add) and ([.data.lines[].dueDate]|. == sort)'
PP1=$(val .data.id)
as "$FIN_TOKEN" POST "$FCL/payment-proposals" '{"asOf":"2027-12-31T00:00:00","cashLimit":1}'; check "f3 proposal: a cash limit of 1 rial includes nothing" '.success and .data.total == 0 and ([.data.lines[]|select(.included)]|length) == 0'
as "$FIN_TOKEN" POST $FCL/payment-proposals/$PP1/approve; no "f3 proposal: only the finance manager approves"
as "$FM_TOKEN" POST $FCL/payment-proposals/$PP1/approve; check "f3 proposal: the finance manager approves" '.success and .data.status == 1'
as "$FM_TOKEN" POST $FCL/payment-proposals/$PP1/approve; no "f3 proposal: approved once"

TOKEN=$ADMIN_TOKEN


TOKEN=$ADMIN_TOKEN

# =============================================================================================== review fixes R2
# --- finding 20: Persian dates in everything the user reads (storage and API JSON stay ISO)
for RPT in journal general-ledger; do
  RX="$B.$RPT.csv"; curl -s -D "$B.hdr" -o "$RX" -H "Authorization: Bearer $FM_TOKEN" "$API/$FG/reports/export/$RPT?FiscalYearId=$Y4&IncludeDraft=true&AccountCode=10101"
  if grep -qi "filename=.*$RPT-14[0-9]\{6\}\.csv" "$B.hdr"; then ok "r2 f20: the $RPT file name carries a Persian date stamp (14yymmdd)"; else bad "r2 f20: the $RPT file name carries a Persian date stamp ($(grep -i content-disposition "$B.hdr" | head -c 160))"; fi
  [ "$(grep -Ec '(^|,)"?14[0-9]{2}/[0-9]{2}/[0-9]{2}' "$RX")" -ge 1 ] && ok "r2 f20: the $RPT rows show Persian dates (yyyy/MM/dd)" || bad "r2 f20: the $RPT rows show Persian dates"
  [ "$(grep -Ec '(^|[,"T ])20[0-9]{2}[-/][0-9]{2}[-/][0-9]{2}' "$RX")" = "0" ] && ok "r2 f20: no 20xx Gregorian date pattern in the $RPT file" || bad "r2 f20: no 20xx Gregorian date pattern in the $RPT file ($(grep -Ec '20[0-9]{2}[-/][0-9]{2}' "$RX") lines)"
  rm -f "$RX" "$B.hdr"
done
as "$FM_TOKEN" GET "$FAU?EntityType=FnPeriod&PageSize=0"; check "r2 f20: audit values show Persian dates and no Gregorian date" '.success and ([.data.data[]|(.beforeJson|tostring),(.afterJson|tostring)]|map(select(test("20[0-9]{2}-[0-9]{2}-[0-9]{2}")))|length) == 0 and ([.data.data[]|(.afterJson|tostring)|select(test("14[0-9]{2}/[0-9]{2}/[0-9]{2}"))]|length) >= 1'
# --- finding 21: audit gaps (manual voucher lines, supplier IBAN with a one-way hash of the full value)
as "$FM_TOKEN" GET "$FAU?EntityType=FnVoucherLine&PageSize=0"; check "r2 f21: the lines of manual vouchers are in the audit trail" '.success and (.data.data|length) >= 1'
call POST procurement/suppliers "{\"providerId\":$PA,\"iban\":\"IR950190000000200000000003\"}"; check "r2 f21: supplier IBAN changed" '.success'
as "$FM_TOKEN" GET "$FAU?EntityType=SupplierProfile&PageSize=0"; check "r2 f21: the supplier IBAN change is audited masked with a hash and never in full" '.success and ([.data.data[]|select((.afterJson|tostring|contains("IbanHash")) and (.afterJson|tostring|contains("****0003")) and ((.afterJson|tostring|contains("IR950190000000200000000003"))|not) and ((.beforeJson|tostring|contains("IR970170000000100324200002"))|not))]|length) >= 1'
# --- finding 6: a person who initiated a flow cannot finalise its voucher (system events have creator 0, so they stay unaffected)
as "$FM_TOKEN" POST $FDF/provisions/movement "{\"provisionId\":$PV1,\"date\":\"2025-06-18T00:00:00\",\"amount\":100000,\"reason\":\"آزمون ایجادکننده\"}"; check "r2 f6: finance manager charges the provision by 100,000" '.success and .data.balance == 3900000'
as "$FM_TOKEN" GET "$FG/vouchers?FiscalYearId=$Y4&Type=3&PageSize=0"; PVV=$(jq -r '[.data.data[]|select(.sourceType == "Provision")]|max_by(.id)|.id' "$B")
as "$FM_TOKEN" POST $FG/vouchers/action "{\"id\":$PVV,\"action\":\"finalize\"}"; no "r2 f6: the finance manager cannot finalise the automatic voucher of his own provision movement"
as "$FM2_TOKEN" POST $FG/vouchers/action "{\"id\":$PVV,\"action\":\"finalize\"}"; check "r2 f6: the second finance manager finalises it" '.success and .data.status == 2'
# --- finding 18: a final voucher numbered after one with a later date carries a warning
fv "{\"date\":\"2025-05-02T00:00:00\",\"description\":\"سند با تاریخ قبل از اسناد نهایی\",\"lines\":[{\"accountCode\":\"80108\",\"debit\":1000},{\"accountCode\":\"30208\",\"credit\":1000}]}"
check "r2 f18: finalised after later-dated vouchers, the finalise answer carries the date-order warning" '.success and (.data.warning // "" | length) > 0'

TOKEN=$ADMIN_TOKEN

# =============================================================================================== F3b completeness (migration FinanceCompleteness4)
FCM=finance/completeness; FPS=finance/payroll-settlements
cb() { local v; v=$(tbc "$1"); echo "${v:-0}"; } # closing balance (debit minus credit) of an account in the current fiscal year
cfrow() { as "$FM_TOKEN" GET "$FCM/cash-flow-direct?FiscalYearId=$FYC&IncludeDraft=true"; jq -r "([.data.rows[]|select(.key == \"$1\")][0].net // 0) | round" "$B"; } # net cash of a direct-method row
# --- review 3 (review-2 finding 1): the provision true-up leaves drafts out, and a draft still waiting blocks a second one
# the balance the true-up must use, read independently from the trial balance WITHOUT drafts (credit balance of 30402); the draft-inclusive figure differs by the drafts
TU_ALL=$(( -$(cb 30402) ))
as "$FM_TOKEN" GET "$FG/reports/trial-balance?FiscalYearId=$FYC&Level=2&IncludeDraft=false"; TU_FIN=$(jq -r '([.data.rows[]|select(.code == "30402")][0] | ((.closingCredit // 0) - (.closingDebit // 0))) // 0 | round' "$B")
# independent figure: every non-draft voucher of the entity dated up to the true-up date, except closing vouchers, summed on 30402 (the trial balance of the year also holds vouchers dated later, e.g. a payroll accrual of 1405/09/30)
as "$FM_TOKEN" GET "$FG/vouchers?WithLines=true&PageSize=0"
jq -r '.data.data[]? | select(any(.lines[]?; .accountCode == "30402")) | "voucher \(.id) type \(.type) status \(.status) date \(.date[0:10]) 30402 " + (([.lines[]|select(.accountCode == "30402")|(.credit - .debit)]|add)|tostring)' "$B" >> "$R"
TU_CUR=$(jq -r --arg d "$TODAY" '[.data.data[]? | select(.status != 0 and .type != 4 and .type != 6 and .date[0:10] <= $d) | .lines[]? | select(.accountCode == "30402") | (.credit - .debit)] | add // 0 | round' "$B")
TU_DRAFT=$(jq -r '[.data.data[]? | select(.status == 0) | .lines[]? | select(.accountCode == "30402") | (.credit - .debit)] | add // 0 | round' "$B")
TU_REQ=$(( TU_CUR + 7000000 ))
echo "true-up balances: with drafts $TU_ALL, drafts $TU_DRAFT, final only $TU_FIN, checked+final $TU_CUR" >> "$R"
as "$FIN_TOKEN" POST $FDF/provisions/true-up "{\"kind\":\"bonus\",\"date\":\"${TODAY}T00:00:00\",\"requiredBalance\":$TU_REQ,\"reason\":\"آزمون تعدیل ذخیره\"}"; check "r3 true-up: the bonus provision balance is checked+final without drafts ($TU_CUR = $TU_ALL with drafts - $TU_DRAFT in draft vouchers) and the draft raises it by exactly 7,000,000" '.success and .data.currentBalance == '$TU_CUR' and .data.difference == 7000000 and .data.voucherId > 0'
as "$FIN_TOKEN" POST $FDF/provisions/true-up "{\"kind\":\"bonus\",\"date\":\"${TODAY}T00:00:00\",\"requiredBalance\":$((TU_REQ + 1000000)),\"reason\":\"آزمون تعدیل دوم\"}"; nomsg "r3 true-up: a second true-up is refused while the first is still a draft (drafts are not counted in the balance)" "پیش‌نویس"
RA_AP0=$(rec ap); RA_BK0=$(rec bank)
# --- non-recoverable VAT per purchase line (a second order of 10 bolts at 390,000; the whole VAT of 390,000 is cost)
as "$WH_TOKEN" POST procurement/requests "{\"source\":2,\"kind\":1,\"requestDate\":\"2026-10-22T00:00:00\",\"description\":\"مالیات غیرقابل استرداد\",\"submit\":true,\"lines\":[{\"itemId\":$BOLT,\"quantity\":10,\"estimatedUnitPrice\":400000,\"technicalRequirements\":\"ISO 4014\"}]}"; RQ4=$(val .data.id); RQ4L=$(val '.data.lines[0].id')
as "$PLAN_TOKEN" POST procurement/requests/action "{\"id\":$RQ4,\"action\":\"approve-planning\"}"; as "$CEO_TOKEN" POST procurement/requests/action "{\"id\":$RQ4,\"action\":\"approve-management\"}"
QA4="{\"providerId\":$PA,\"quoteNumber\":\"P-A4\",\"vatPercent\":10,\"deliveryDays\":20,\"technicallyAccepted\":true,\"lines\":[{\"requestLineId\":$RQ4L,\"quantity\":10,\"unitPrice\":390000}]}"
QB4="{\"providerId\":$PB,\"quoteNumber\":\"P-B4\",\"vatPercent\":10,\"technicallyAccepted\":true,\"lines\":[{\"requestLineId\":$RQ4L,\"quantity\":10,\"unitPrice\":380000}]}"
as "$COM_TOKEN" POST procurement/inquiries "{\"purchaseRequestId\":$RQ4,\"inquiryDate\":\"2026-10-22T00:00:00\",\"quotes\":[$QA4,$QB4]}"; IQ4=$(val .data.id); QA4ID=$(val ".data.quotes[]|select(.providerId == $PA)|.id")
as "$COM_TOKEN" POST procurement/inquiries/select "{\"inquiryId\":$IQ4,\"quoteId\":$QA4ID,\"reason\":\"گرید A\"}"
as "$COM_TOKEN" POST "procurement/orders/from-inquiry/$IQ4"; PO4=$(val .data.id); POL4=$(val '.data.lines[0].id')
call POST procurement/orders/action "{\"id\":$PO4,\"action\":\"approve\"}"; as "$COM_TOKEN" POST procurement/orders/action "{\"id\":$PO4,\"action\":\"release\"}"
call POST inventory/documents "{\"type\":1,\"documentDate\":\"2026-10-22T00:00:00\",\"warehouseId\":$W1,\"purchaseOrderId\":$PO4,\"lines\":[{\"itemId\":$BOLT,\"quantity\":10,\"locationId\":$L2,\"purchaseOrderLineId\":$POL4}]}"; DR4=$(val .data.id); call POST "inventory/documents/$DR4/confirm"
as "$FM_TOKEN" GET "$FX/vat-season?Year=1405&Season=3"; NV_VAT0=$(val .data.purchasesVat); NV_NR0=$(val .data.nonRecoverableVat); NV_NET0=$(val .data.purchasesNet)
as "$FM_TOKEN" POST $FG/post-events
NV_PPV0=$(cb 70108); NV_VATA0=$(cb 10403); NV_AP0=$(cb 30101)
as "$COM_TOKEN" POST procurement/invoices "{\"purchaseOrderId\":$PO4,\"invoiceNumber\":\"F-3002\",\"invoiceDate\":\"2026-10-24T00:00:00\",\"isOfficial\":true,\"lines\":[{\"orderLineId\":$POL4,\"quantity\":10,\"unitPrice\":390000,\"nonRecoverableVat\":true}]}"; SI4=$(val .data.id)
check "f3b vat: the invoice shows a non-recoverable VAT of 390,000 and a total of 4,290,000" '.success and .data.nonRecoverableVatAmount == 390000 and .data.totalAmount == 4290000'
as "$FIN_TOKEN" POST procurement/invoices/action "{\"id\":$SI4,\"action\":\"approve\"}"; check "f3b vat: the invoice is approved" '.success and .data.status == 3'
as "$FM_TOKEN" POST $FG/post-events
as "$FM_TOKEN" GET "$FX/vat-season?Year=1405&Season=3"
check "f3b vat: the deductible purchases VAT is unchanged, the non-recoverable VAT is +390,000, purchases net +3,900,000" '.success and .data.purchasesVat == '$NV_VAT0' and .data.nonRecoverableVat == '$((NV_NR0 + 390000))' and .data.purchasesNet == '$((NV_NET0 + 3900000))
[ "$(cb 70108)" = "$((NV_PPV0 + 390000))" ] && ok "f3b vat: the non-recoverable 390,000 is cost (price-variance account +390,000)" || bad "f3b vat: the non-recoverable 390,000 is cost (got $(( $(cb 70108) - NV_PPV0 )))"
[ "$(cb 10403)" = "$NV_VATA0" ] && ok "f3b vat: the deductible VAT account does not move" || bad "f3b vat: the deductible VAT account does not move (got $(( $(cb 10403) - NV_VATA0 )))"
[ "$(cb 30101)" = "$((NV_AP0 - 4290000))" ] && ok "f3b vat: the payable grows by the invoice total 4,290,000" || bad "f3b vat: the payable grows by 4,290,000 (got $(( NV_AP0 - $(cb 30101) )))"

# --- VAT on an asset disposal
as "$FIN_TOKEN" POST $FA "$(AJ 'دستگاه فروش با مالیات' $CLS1 2026-06-25 20000000)"; ASV=$(val .data.id)
as "$FM_TOKEN" POST $FA/action "{\"id\":$ASV,\"action\":\"approve\"}"; check "f3b disposal: asset for the VAT sale approved" '.success'
as "$FM_TOKEN" POST $FG/post-events
DV_VAT0=$(cb 30205)
as "$FIN_TOKEN" POST $FA/disposals "{\"assetId\":$ASV,\"date\":\"2026-10-24T00:00:00\",\"kind\":1,\"proceedsAmount\":10000000,\"proceedsMethod\":1,\"cashBoxId\":$CB,\"vatPercent\":10}"; check "f3b disposal: the preview shows a VAT of 1,000,000 on proceeds of 10,000,000" '.success and .data.vatPercent == 10 and .data.vatAmount == 1000000'
DSV=$(val .data.id)
as "$FM_TOKEN" POST $FA/disposals/action "{\"id\":$DSV,\"action\":\"approve\"}"; check "f3b disposal: the sale with VAT is approved" '.success'
as "$FM_TOKEN" POST $FG/post-events
[ "$(cb 30205)" = "$((DV_VAT0 - 1000000))" ] && ok "f3b disposal: the sales-VAT account is credited exactly 1,000,000" || bad "f3b disposal: the sales-VAT account is credited exactly 1,000,000 (got $(( DV_VAT0 - $(cb 30205) )))"

# --- bank guarantees
GBK=$(cb 90101); GBK2=$(cb 90201); GPP=$(cb 10604)
as "$FIN_TOKEN" POST $FCM/guarantees/limits '{"bankName":"ملت","limit":8000000000}'; no "f3b guarantee: only the finance manager sets a bank limit"
as "$FM_TOKEN" POST $FCM/guarantees/limits '{"bankName":"ملت","limit":8000000000,"note":"آزمون"}'; check "f3b guarantee: the finance manager sets the bank limit" '.success'
as "$FIN_TOKEN" POST $FCM/guarantees "{\"direction\":1,\"kind\":1,\"bankName\":\"ملت\",\"bankAccountId\":$BAR,\"guaranteeNo\":\"G-1\",\"partyName\":\"کارفرمای آزمون\",\"amount\":5000000000,\"issueDate\":\"2026-10-24T00:00:00\",\"expiryDate\":\"2027-10-23T00:00:00\",\"feeAmount\":1200000}"
check "f3b guarantee: an issued guarantee of 5,000,000,000 is registered with a fee of 1,200,000 and a prepaid balance of 1,200,000" '.success and .data.status == 1 and .data.amount == 5000000000 and .data.feeRemaining == 1200000 and (.data.number|length) > 0'
GID=$(val .data.id)
[ "$(cb 90101)" = "$((GBK + 5000000000))" ] && [ "$(cb 90201)" = "$((GBK2 - 5000000000))" ] && ok "f3b guarantee: the memorandum accounts carry exactly 5,000,000,000" || bad "f3b guarantee: the memorandum accounts carry exactly 5,000,000,000"
[ "$(cb 10604)" = "$((GPP + 1200000))" ] && ok "f3b guarantee: the fee is deferred (prepaid +1,200,000)" || bad "f3b guarantee: the fee is deferred (got $(( $(cb 10604) - GPP )))"
as "$FIN_TOKEN" POST $FCM/guarantees "{\"direction\":1,\"kind\":1,\"bankName\":\"ملت\",\"bankAccountId\":$BAR,\"guaranteeNo\":\"G-1\",\"partyName\":\"کارفرمای آزمون\",\"amount\":1000,\"issueDate\":\"2026-10-24T00:00:00\",\"expiryDate\":\"2027-10-23T00:00:00\"}"; no "f3b guarantee: the same bank and guarantee number cannot be registered twice"
as "$FIN_TOKEN" POST $FCM/guarantees "{\"direction\":1,\"kind\":1,\"bankName\":\"ملت\",\"partyName\":\"x\",\"amount\":1000,\"issueDate\":\"2026-10-24T00:00:00\",\"expiryDate\":\"2026-10-01T00:00:00\"}"; no "f3b guarantee: an expiry before the issue date is refused"
as "$FM_TOKEN" GET "$FCM/guarantees/exposure"; check "f3b guarantee: exposure at the bank is 5,000,000,000 of a limit of 8,000,000,000 (3,000,000,000 available)" '.success and ([.data.banks[]|select(.bankName == "ملت" and .exposure == 5000000000 and .limit == 8000000000 and .available == 3000000000 and .activeCount == 1)]|length) == 1 and .data.totalIssued == 5000000000'
as "$FIN_TOKEN" POST $FCM/guarantees/action "{\"id\":$GID,\"action\":\"extend\",\"newExpiry\":\"2028-01-20T00:00:00\",\"feeAmount\":300000}"; check "f3b guarantee: an extension with a fee of 300,000 moves the expiry and defers the extra fee" '.success and (.data.expiryDate|tostring|test("2028-01-20|1406/10/30"))'
[ "$(cb 10604)" = "$((GPP + 1500000))" ] && ok "f3b guarantee: prepaid fee is 1,500,000 after the extension" || bad "f3b guarantee: prepaid fee is 1,500,000 after the extension (got $(( $(cb 10604) - GPP )))"
as "$FM_TOKEN" GET "$FCM/guarantees?direction=1"; check "f3b guarantee: the register lists the extension event" '.success and ([.data[]|select(.id == '$GID')|.events[]|select(.kind == 1)]|length) == 1'
as "$FIN_TOKEN" POST $FCM/guarantees/action "{\"id\":$GID,\"action\":\"release\",\"date\":\"2026-10-24T00:00:00\"}"; check "f3b guarantee: release closes the guarantee" '.success and .data.status == 2'
[ "$(cb 90101)" = "$GBK" ] && [ "$(cb 90201)" = "$GBK2" ] && ok "f3b guarantee: release reverses the memorandum exactly" || bad "f3b guarantee: release reverses the memorandum exactly"
[ "$(cb 10604)" = "$GPP" ] && ok "f3b guarantee: release expenses the whole unamortised fee (prepaid back to its start)" || bad "f3b guarantee: release expenses the whole unamortised fee (got $(( $(cb 10604) - GPP )))"
as "$FM_TOKEN" GET "$FCM/guarantees/exposure"; check "f3b guarantee: after the release the exposure is zero" '.success and .data.totalIssued == 0'
as "$FIN_TOKEN" POST $FCM/guarantees/action "{\"id\":$GID,\"action\":\"release\"}"; no "f3b guarantee: a released guarantee cannot be released again"
as "$FIN_TOKEN" POST $FCM/guarantees "{\"direction\":2,\"kind\":2,\"bankName\":\"ملی\",\"guaranteeNo\":\"R-1\",\"partyName\":\"پیمانکار آزمون\",\"providerId\":$PA,\"amount\":700000000,\"issueDate\":\"2026-10-01T00:00:00\",\"expiryDate\":\"2026-11-05T00:00:00\"}"; check "f3b guarantee: a received guarantee is register-only" '.success and .data.direction == 2 and .data.status == 1'
as "$FM_TOKEN" GET "$FCM/guarantees/exposure"; check "f3b guarantee: the received guarantee appears in the totals and in the 30-day expiry list" '.success and .data.totalReceived == 700000000 and ([.data.expiringWithin30Days[]|select(.guaranteeNo == "R-1")]|length) == 1'
[ "$(cb 90101)" = "$GBK" ] && ok "f3b guarantee: a received guarantee posts nothing" || bad "f3b guarantee: a received guarantee posts nothing"

# --- insurance clearance gates the release of the performance retention
RTH=$(cb 30206)
as "$FM_TOKEN" GET "$FG/details?Type=2&PageSize=0"; SUPA=$(jq -r '[.data.data[]|select(.code == "SUP-'$PA'")][0].code // "SUP-'$PA'"' "$B")
fv "{\"date\":\"2026-10-20T00:00:00\",\"description\":\"کسور حسن انجام کار آزمون\",\"lines\":[{\"accountCode\":\"80108\",\"debit\":5000000},{\"accountCode\":\"30206\",\"credit\":5000000,\"detail1Code\":\"$SUPA\"}]}"
as "$FM_TOKEN" GET "$FCM/retention/status?providerId=$PA&asOf=2026-10-24"; check "f3b retention: 5,000,000 held, nothing released, no valid clearance, release not allowed" '.success and .data.held == 5000000 and .data.released == 0 and .data.releasable == 5000000 and .data.canRelease == false and .data.validClearanceId == null'
as "$FIN_TOKEN" POST $FCM/retention/release "{\"providerId\":$PA,\"date\":\"2026-10-24T00:00:00\",\"amount\":5000000}"; nomsg "f3b retention: the release is refused without an insurance clearance" "مفاصاحساب"
as "$FIN_TOKEN" POST $FCM/clearances "{\"providerId\":$PA,\"letterNo\":\"M-FAKE\",\"issueDate\":\"2026-09-01T00:00:00\",\"validUntil\":\"2026-12-01T00:00:00\",\"documentId\":\"4b0d7a52-1d19-4c6f-9a11-0a3e5b6c7d80\"}"; nomsg "r3 m3: a clearance whose file is not in the document store is refused" "مخزن اسناد"
as "$FIN_TOKEN" POST $FG/vouchers "{\"date\":\"${TODAY}T00:00:00\",\"description\":\"آزادسازی سپرده بیمه بدون مفاصاحساب\",\"lines\":[{\"accountCode\":\"30207\",\"debit\":1000,\"detail1Code\":\"$SUPA\"},{\"accountCode\":\"10101\",\"credit\":1000}]}"; nomsg "r3 m3: the insurance retention 30207 cannot be released by a manual voucher without a valid clearance" "مفاصاحساب"
as "$FIN_TOKEN" POST $FCM/clearances "{\"providerId\":$PA,\"letterNo\":\"M-1\",\"issueDate\":\"2026-09-01T00:00:00\",\"validUntil\":\"2026-12-01T00:00:00\"}"; check "f3b retention: a clearance without the scanned letter is saved but not valid" '.success and .data.isValid == false and .data.hasDocument == false'
CLR=$(val .data.id)
as "$FIN_TOKEN" POST $FCM/retention/release "{\"providerId\":$PA,\"date\":\"2026-10-24T00:00:00\",\"amount\":5000000}"; no "f3b retention: a clearance without a document does not release the retention"
as "$FIN_TOKEN" POST $FCM/clearances "{\"id\":$CLR,\"providerId\":$PA,\"letterNo\":\"M-1\",\"issueDate\":\"2026-09-01T00:00:00\",\"validUntil\":\"2026-10-10T00:00:00\",\"documentId\":\"$DOCID\"}"; check "f3b retention: an expired clearance with a document is not valid" '.success and .data.isValid == false and .data.hasDocument == true'
as "$FIN_TOKEN" POST $FCM/retention/release "{\"providerId\":$PA,\"date\":\"2026-10-24T00:00:00\",\"amount\":5000000}"; no "f3b retention: an expired clearance does not release the retention"
as "$FIN_TOKEN" POST $FCM/clearances "{\"id\":$CLR,\"providerId\":$PA,\"letterNo\":\"M-1\",\"issueDate\":\"2026-09-01T00:00:00\",\"validUntil\":\"2026-12-01T00:00:00\",\"documentId\":\"$DOCID\",\"isRevoked\":true}"; check "f3b retention: a revoked clearance is not valid" '.success and .data.isValid == false'
as "$FIN_TOKEN" POST $FCM/retention/release "{\"providerId\":$PA,\"date\":\"2026-10-24T00:00:00\",\"amount\":5000000}"; no "f3b retention: a revoked clearance does not release the retention"
as "$FIN_TOKEN" POST $FCM/clearances "{\"id\":$CLR,\"providerId\":$PA,\"letterNo\":\"M-1\",\"issueDate\":\"2026-09-01T00:00:00\",\"validUntil\":\"2026-12-01T00:00:00\",\"documentId\":\"$DOCID\"}"; check "f3b retention: a clearance with the document, not revoked, covering the date is valid" '.success and .data.isValid == true'
as "$FIN_TOKEN" POST $FCM/retention/release "{\"providerId\":$PA,\"date\":\"2026-10-24T00:00:00\",\"amount\":6000000}"; no "f3b retention: more than the retention held is refused"
as "$FM_TOKEN" GET "$FCM/retention/status?providerId=$PA&asOf=2026-10-24"; USEDCLR=$(val .data.validClearanceId)
AP_B=$(cb 30101)
as "$FIN_TOKEN" POST $FCM/retention/release "{\"providerId\":$PA,\"date\":\"2026-10-24T00:00:00\",\"amount\":3000000}"; check "f3b retention: 3,000,000 released with a valid clearance" '.success and .data.amount == 3000000 and .data.voucherId > 0'
[ "$(cb 30206)" = "$((RTH - 5000000 + 3000000))" ] && [ "$(cb 30101)" = "$((AP_B - 3000000))" ] && ok "f3b retention: the retention account falls 3,000,000 and the payable rises 3,000,000" || bad "f3b retention: the retention account falls 3,000,000 and the payable rises 3,000,000"
as "$FM_TOKEN" GET "$FCM/retention/status?providerId=$PA&asOf=2026-10-24"; check "f3b retention: 2,000,000 remain releasable" '.success and .data.held == 5000000 and .data.released == 3000000 and .data.releasable == 2000000 and .data.canRelease == true'
as "$FIN_TOKEN" POST $FCM/clearances "{\"id\":$USEDCLR,\"providerId\":$PA,\"letterNo\":\"M-1-edited\",\"issueDate\":\"2026-09-01T00:00:00\",\"validUntil\":\"2026-12-01T00:00:00\",\"documentId\":\"$DOCID\"}"; nomsg "r3 m3: a clearance that was used for a release can no longer be edited" "به کار رفته"
# review 3, H5: 2,000,000 withheld in the previous fiscal year (1404, not closed) is part of the cumulative retention held in 1405
fv "{\"date\":\"2025-06-25T00:00:00\",\"description\":\"کسور حسن انجام کار سال قبل\",\"lines\":[{\"accountCode\":\"80108\",\"debit\":2000000},{\"accountCode\":\"30206\",\"credit\":2000000,\"detail1Code\":\"$SUPA\"}]}"
as "$FM_TOKEN" GET "$FCM/retention/status?providerId=$PA&asOf=2026-10-24"; check "r3 h5: the retention held counts the unclosed previous year: 7,000,000 held, 3,000,000 released, 4,000,000 releasable" '.success and .data.held == 7000000 and .data.released == 3000000 and .data.releasable == 4000000'

# --- planned versus actual per work order
as "$FM_TOKEN" GET $FCO/workorders/$WO2/cost-sheet; ACT_TOT=$(val .data.totalCost)
as "$FIN_TOKEN" POST $FCM/estimates "{\"workOrderId\":$WO2,\"material\":900000,\"labour\":300000,\"overhead\":500000,\"subcontract\":0,\"quantity\":2}"; check "f3b cost: the estimate is saved" '.success'
as "$FM_TOKEN" GET "$FCM/work-orders/$WO2/planned-vs-actual"; check "f3b cost: planned 1,700,000 (unit 850,000), actual equals the cost sheet, variance is the difference" '.success and .data.hasEstimate == true and .data.plannedTotal == 1700000 and .data.plannedUnitCost == 850000 and .data.actualTotal == '$ACT_TOT' and .data.variance == '$((ACT_TOT - 1700000))' and ([.data.rows[]|select(.element == "Overhead")|.planned] == [500000])'
as "$FIN_TOKEN" POST $FCM/estimates "{\"workOrderId\":$WO2,\"material\":1000000,\"labour\":300000,\"overhead\":500000,\"subcontract\":0,\"quantity\":2}"; as "$FM_TOKEN" GET "$FCM/work-orders/$WO2/planned-vs-actual"; check "f3b cost: a new estimate replaces the old one" '.success and .data.plannedTotal == 1800000'

# --- supplier credit and debit notes
AP_N0=$(cb 30101); PPV_N0=$(cb 70108); VAT_N0=$(cb 10403)
as "$FIN_TOKEN" POST $FCM/supplier-notes "{\"kind\":1,\"providerId\":$PA,\"supplierInvoiceId\":$SI3,\"noteNumber\":\"CN-1\",\"date\":\"2026-10-24T00:00:00\",\"netAmount\":100000,\"reason\":\"تخفیف قیمت\"}"; check "f3b note: a credit note of 100,000 gets the default VAT 10,000 (total 110,000)" '.success and .data.vatAmount == 10000 and .data.totalAmount == 110000 and .data.status == 0'
CN=$(val .data.id)
as "$FIN_TOKEN" POST $FCM/supplier-notes "{\"kind\":1,\"providerId\":$PA,\"supplierInvoiceId\":$SI3,\"date\":\"2026-10-24T00:00:00\",\"netAmount\":5000000,\"reason\":\"بیش از فاکتور\"}"; no "f3b note: a credit note above the invoice total is refused"
as "$FIN_TOKEN" POST $FCM/supplier-notes/action "{\"id\":$CN,\"action\":\"approve\"}"; nomsg "f3b note: the preparer cannot approve" "شخص دیگری"
as "$FIN2_TOKEN" POST $FCM/supplier-notes/action "{\"id\":$CN,\"action\":\"approve\"}"; check "f3b note: the second accountant approves and posts" '.success and .data.status == 1 and .data.voucherId > 0'
[ "$(cb 30101)" = "$((AP_N0 + 110000))" ] && [ "$(cb 70108)" = "$((PPV_N0 - 100000))" ] && [ "$(cb 10403)" = "$((VAT_N0 - 10000))" ] && ok "f3b note: credit note debits the payable 110,000, credits price variance 100,000 and VAT 10,000" || bad "f3b note: credit note debits the payable 110,000, credits price variance 100,000 and VAT 10,000"
as "$FIN_TOKEN" POST $FCM/supplier-notes "{\"kind\":2,\"providerId\":$PA,\"noteNumber\":\"DN-1\",\"date\":\"2026-10-24T00:00:00\",\"netAmount\":200000,\"vatAmount\":0,\"reason\":\"هزینه حمل\"}"; check "f3b note: a debit note with VAT 0 totals 200,000" '.success and .data.totalAmount == 200000'
DN=$(val .data.id); as "$FIN2_TOKEN" POST $FCM/supplier-notes/action "{\"id\":$DN,\"action\":\"approve\"}"; check "f3b note: the debit note is posted" '.success and .data.status == 1'
[ "$(cb 30101)" = "$((AP_N0 + 110000 - 200000))" ] && ok "f3b note: the debit note credits the payable 200,000" || bad "f3b note: the debit note credits the payable 200,000"

# --- supplier statement reconciliation
as "$FM_TOKEN" POST $FG/post-events; as "$COM_TOKEN" GET procurement/invoices/$SI3; SI3NO=$(val .data.number)
as "$FIN_TOKEN" POST $FCM/supplier-statements "{\"providerId\":$PA,\"asOf\":\"2026-10-24T00:00:00\",\"theirBalance\":0,\"lines\":[{\"date\":\"2026-10-24T00:00:00\",\"documentNumber\":\"F-3001\",\"credit\":4290000},{\"date\":\"2026-10-24T00:00:00\",\"documentNumber\":\"CN-1\",\"debit\":110000},{\"date\":\"2026-10-24T00:00:00\",\"documentNumber\":\"X-999\",\"credit\":777000}]}"
check "f3b statement: the supplier's own invoice number F-3001 and the supplier's credit-note number CN-1 match exactly, the unknown 777,000 line stays unmatched, difference = their balance minus ours" '.success and .data.difference == (.data.theirBalance - .data.ourBalance) and ([.data.lines[]|select(.documentNumber == "F-3001" and .matchedNumber == "'$SI3NO'" and .amountDifference == null)]|length) == 1 and ([.data.lines[]|select(.documentNumber == "CN-1" and .matchedKind == "SupplierNote" and .amountDifference == null)]|length) == 1 and ([.data.unmatchedTheirs[]|select(.documentNumber == "X-999" and .credit == 777000)]|length) == 1 and (.data.unmatchedTheirs|length) == 1 and .data.reconciled == false'
STM=$(val .data.id)
as "$FM_TOKEN" GET "$FCM/supplier-statements/$STM"; check "f3b statement: the stored statement is readable with the same result" '.success and ([.data.unmatchedTheirs[]|select(.documentNumber == "X-999")]|length) == 1'
as "$FIN_TOKEN" POST $FCM/supplier-statements "{\"providerId\":$PA,\"asOf\":\"2026-10-24T00:00:00\",\"theirBalance\":0,\"lines\":[{\"date\":\"2026-10-24T00:00:00\",\"documentNumber\":\"$SI3NO\",\"credit\":4290000}]}"; check "f3b statement: our internal number still matches by the fallback" '.success and ([.data.lines[]|select(.matchedNumber == "'$SI3NO'" and .amountDifference == null)]|length) == 1 and (.data.unmatchedTheirs|length) == 0'
as "$FIN_TOKEN" POST $FCM/supplier-statements "{\"providerId\":$PA,\"asOf\":\"2026-10-24T00:00:00\",\"theirBalance\":0,\"lines\":[{\"date\":\"2026-10-24T00:00:00\",\"documentNumber\":\"F-3001\",\"credit\":4290001}]}"; check "f3b statement: F-3001 with 4,290,001 is matched by number and shows an amount difference of exactly 1, which is part of the explained difference" '.success and ([.data.lines[]|select(.documentNumber == "F-3001" and .matchedNumber == "'$SI3NO'" and .amountDifference == 1)]|length) == 1 and (.data.unmatchedTheirs|length) == 0 and .data.explained == (1 - ([.data.unmatchedOurs[]|(.credit - .debit)]|add // 0))'

# --- review 3, H1 and M5: a credit note lowers what may be paid; on an invoice whose VAT is all non-recoverable it does not credit the input VAT
AP_R0=$(cb 30101); PPV_R0=$(cb 70108); VAT_R0=$(cb 10403)
as "$FM_TOKEN" GET "$FX/vat-season?Year=1405&Season=3"; VS_R0=$(val .data.purchasesVat)
as "$FIN_TOKEN" POST $FCM/supplier-notes "{\"kind\":1,\"providerId\":$PA,\"supplierInvoiceId\":$SI4,\"noteNumber\":\"CN-2\",\"date\":\"${TODAY}T00:00:00\",\"netAmount\":200000,\"reason\":\"تخفیف فاکتور با مالیات غیرقابل استرداد\"}"; check "r3 m5: a credit note of 200,000 on the invoice F-3002 gets VAT 20,000 (total 220,000)" '.success and .data.vatAmount == 20000 and .data.totalAmount == 220000'
CN2=$(val .data.id)
as "$FIN2_TOKEN" POST $FCM/supplier-notes/action "{\"id\":$CN2,\"action\":\"approve\"}"; check "r3 m5: the credit note is posted" '.success and .data.status == 1'
[ "$(cb 30101)" = "$((AP_R0 + 220000))" ] && [ "$(cb 70108)" = "$((PPV_R0 - 220000))" ] && [ "$(cb 10403)" = "$VAT_R0" ] && ok "r3 m5: payable -220,000, price variance -220,000 (net 200,000 + non-recoverable VAT 20,000) and the input VAT account does not move" || bad "r3 m5: payable moved $(( $(cb 30101) - AP_R0 )), price variance $(( $(cb 70108) - PPV_R0 )), input VAT $(( $(cb 10403) - VAT_R0 ))"
as "$FM_TOKEN" GET "$FX/vat-season?Year=1405&Season=3"; check "r3 m5: the deductible purchases VAT of the return is unchanged by the credit note" '.success and .data.purchasesVat == '$VS_R0
as "$COM_TOKEN" POST procurement/payments "{\"type\":3,\"invoiceId\":$SI4,\"amount\":4290000}"; nomsg "r3 h1: a payment request for the whole invoice 4,290,000 is refused after a credit note of 220,000" "مانده فاکتور"
as "$COM_TOKEN" POST procurement/payments "{\"type\":3,\"invoiceId\":$SI4,\"amount\":4070000}"; check "r3 h1: 4,070,000 (4,290,000 - 220,000) is accepted" '.success and .data.amount == 4070000'
R3PAY=$(val .data.id)
call POST procurement/payments/action "{\"id\":$R3PAY,\"action\":\"reject\",\"note\":\"آزمون پایان\"}"

# --- customer confirmation letter
as "$FIN_TOKEN" POST $FCM/confirmations "{\"customerId\":$HC,\"asOf\":\"2026-10-24T00:00:00\"}"; check "f3b confirmation: letter data for a customer without movement: ledger 0, statement 0, agrees, no open documents" '.success and .data.ledgerBalance == 0 and .data.statementBalance == 0 and .data.agrees == true and .data.openDocuments == 0 and .data.status == 1 and (.data.customerName|length) > 0'
CF=$(val .data.id)
as "$FIN_TOKEN" POST $FCM/confirmations/respond "{\"id\":$CF,\"action\":\"confirm\",\"amount\":5}"; no "f3b confirmation: a confirmed amount different from the ledger is refused"
as "$FIN_TOKEN" POST $FCM/confirmations/respond "{\"id\":$CF,\"action\":\"confirm\",\"amount\":0}"; check "f3b confirmation: the matching confirmation is recorded" '.success and .data.status == 2'
as "$FIN_TOKEN" POST $FCM/confirmations/respond "{\"id\":$CF,\"action\":\"dispute\",\"note\":\"x\"}"; no "f3b confirmation: an answered letter cannot be answered again"

# --- annual bonus (عیدی) and termination settlement
EBJ() { EJ "$1" "$2" "$3" "$4" IR270170000000100324200001 | jq -c --arg h "$5" --argjson w "$6" '.hireDate = $h | .dailyWage = $w'; }
as "$PAYR_TOKEN" POST $FP/employees "$(EBJ T901 آزمون عیدی 0013542419 2026-03-21T00:00:00 1100000)"; check "f3b bonus: employee with a daily wage of 1,100,000 hired on the first day of 1405 saved" '.success and .data.id > 0'
PE2=$(val .data.id)
as "$PAYR_TOKEN" POST $FPS/bonus-runs "{\"year\":1404,\"payDate\":\"2026-10-24T00:00:00\",\"employeeIds\":[$PE1,$PE2]}"; no "f3b bonus: a year without the rate inputs is refused"
fv "{\"date\":\"2026-10-20T00:00:00\",\"description\":\"ذخیره عیدی آزمون\",\"lines\":[{\"accountCode\":\"80108\",\"debit\":1000000000},{\"accountCode\":\"30402\",\"credit\":1000000000}]}"
as "$FIN_TOKEN" POST $FPS/bonus-runs "{\"year\":1405,\"payDate\":\"2026-10-24T00:00:00\",\"employeeIds\":[$PE1,$PE2]}"; [ "$CODE" = "403" ] && ok "f3b bonus: the accountant cannot prepare a bonus run (403)" || bad "f3b bonus: the accountant cannot prepare a bonus run (403) got $CODE"
# review 3, H8: the gross of the bonus is capped at 60 days of the minimum wage (500,000) = 30,000,000 per full year; the cap is a rate-table key
as "$PAYR_TOKEN" POST $FP/rates "{\"year\":1405,\"key\":\"BonusGrossCapDaysOfMinimumWage\",\"value\":60,\"source\":\"$SRC\",\"verify\":true}"; check "r3 h8: the gross cap of the bonus is entered as 60 days" '.success and .data.key == "BonusGrossCapDaysOfMinimumWage"'
as "$PAYR_TOKEN" POST $FPS/bonus-runs "{\"year\":1405,\"payDate\":\"2026-10-24T00:00:00\",\"employeeIds\":[$PE1,$PE2]}"; check "r3 h8: with the cap at 60 days both gross amounts are exactly 30,000,000 (36,000,000 and 66,000,000 before), total 60,000,000, tax 0" '.success and .data.totalGross == 60000000 and .data.totalTax == 0 and ([.data.lines[]|select(.gross == 30000000)]|length) == 2'
BRX=$(val .data.id)
as "$PAYR_TOKEN" POST $FPS/bonus-runs/action "{\"id\":$BRX,\"action\":\"cancel\"}"; check "r3 h8: the capped draft is cancelled" '.success and .data.status == 2'
as "$PAYR_TOKEN" POST $FP/rates "{\"year\":1405,\"key\":\"BonusGrossCapDaysOfMinimumWage\",\"value\":150,\"source\":\"$SRC\",\"verify\":true}"; check "r3 h8: the gross cap is set to 150 days (75,000,000) for the main scenario" '.success'
as "$PAYR_TOKEN" POST $FPS/bonus-runs "{\"year\":1405,\"payDate\":\"2026-10-24T00:00:00\",\"employeeIds\":[$PE1,$PE2]}"
check "f3b bonus: gross 36,000,000 and 66,000,000, tax 0 and 100,000 (the part above 45,000,000 is taxed), totals 102,000,000 / 100,000 / 101,900,000" '.success and .data.totalGross == 102000000 and .data.totalTax == 100000 and .data.totalNet == 101900000 and ([.data.lines[]|select(.employeeId == '$PE1' and .gross == 36000000 and .tax == 0 and .net == 36000000)]|length) == 1 and ([.data.lines[]|select(.employeeId == '$PE2' and .gross == 66000000 and .tax == 100000 and .net == 65900000 and .employedDays == 365 and .yearDays == 365)]|length) == 1 and .data.status == 0'
BR=$(val .data.id)
BN_P0=$(cb 30402); BN_T0=$(cb 30203); BN_W0=$(cb 30201); BN_E0=$(cb 80101)
as "$PAYR_TOKEN" POST $FPS/bonus-runs/action "{\"id\":$BR,\"action\":\"approve\"}"; nomsg "f3b bonus: the preparer cannot approve" "شخص دیگری"
as "$FM_TOKEN" POST $FPS/bonus-runs/action "{\"id\":$BR,\"action\":\"approve\"}"; check "f3b bonus: approved, 102,000,000 drawn from the provision and nothing expensed now" '.success and .data.status == 1 and .data.fromProvision == 102000000 and .data.expensedNow == 0 and .data.voucherId > 0'
[ "$(cb 30402)" = "$((BN_P0 + 102000000))" ] && [ "$(cb 30203)" = "$((BN_T0 - 100000))" ] && [ "$(cb 30201)" = "$((BN_W0 - 101900000))" ] && [ "$(cb 80101)" = "$BN_E0" ] && ok "f3b bonus: provision debited 102,000,000, tax payable +100,000, payroll payable +101,900,000, no expense" || bad "f3b bonus: ledger effect of the bonus run"
# review 3, H6: the net bonus is paid through treasury (draft, approval, posting) and clears the payroll payable
as "$TR_TOKEN" POST $FT/payments "{\"date\":\"${TODAY}T00:00:00\",\"purpose\":5,\"bonusRunId\":$BR,\"lines\":[{\"instrument\":2,\"amount\":101900000,\"bankAccountId\":$BA1,\"reference\":\"BONUS-1405\"}]}"; check "r3 h6: the bonus run is drafted for payment by treasury for the net 101,900,000" '.success and .data.status == 0 and .data.netAmount == 101900000'
BRPM=$(val .data.id)
as "$FM_TOKEN" POST $FT/payments/action "{\"id\":$BRPM,\"action\":\"approve\"}"
as "$TR_TOKEN" POST $FT/payments/action "{\"id\":$BRPM,\"action\":\"post\"}"; check "r3 h6: the bonus payment is posted" '.success and .data.status == 2'
as "$FM_TOKEN" POST $FG/post-events
[ "$(cb 30201)" = "$BN_W0" ] && ok "r3 h6: the payroll payable is back to its start after the bonus payment (101,900,000 paid)" || bad "r3 h6: the payroll payable is back to its start (off by $(( $(cb 30201) - BN_W0 )))"
as "$TR_TOKEN" POST $FT/payments "{\"date\":\"${TODAY}T00:00:00\",\"purpose\":5,\"bonusRunId\":$BR,\"lines\":[{\"instrument\":2,\"amount\":101900000,\"bankAccountId\":$BA1}]}"; no "r3 h6: a bonus run cannot be paid twice"
as "$PAYR_TOKEN" POST $FPS/bonus-runs "{\"year\":1405,\"payDate\":\"2026-10-24T00:00:00\",\"employeeIds\":[$PE1]}"; no "f3b bonus: a second run for the same year is refused"
as "$PAYR_TOKEN" GET "$FPS/tax-reconciliation?year=1405"; check "f3b tax: the annual reconciliation lists the bonus tax of 100,000 for the new employee and the totals add up" '.success and ([.data.rows[]|select(.employeeId == '$PE2')|.taxWithheld] == [100000]) and .data.totalWithheld == ([.data.rows[].taxWithheld]|add) and .data.totalRecomputed == ([.data.rows[].taxRecomputed]|add) and ([.data.rows[]|select(.employeeId == '$PE1')|[.taxWithheld,.taxRecomputed,.difference]] == [[2487000,0,-2487000]]) and ([.data.rows[]|select(.employeeId == '$PE2')|[.taxWithheld,.taxRecomputed,.difference]] == [[100000,0,-100000]]) and .data.totalDifference == (.data.totalRecomputed - .data.totalWithheld)'
as "$AUD_TOKEN" GET "$FPS/tax-reconciliation?year=1405"; [ "$CODE" = "403" ] && ok "f3b payroll: the auditor gets 403 on the reconciliation" || bad "f3b payroll: the auditor gets 403 on the reconciliation got $CODE"
as "$AUD_TOKEN" GET "$FPS/terminations"; [ "$CODE" = "403" ] && ok "f3b payroll: the auditor gets 403 on terminations" || bad "f3b payroll: the auditor gets 403 on terminations got $CODE"
as "$FIN_TOKEN" GET "$FPS/bonus-runs"; [ "$CODE" = "403" ] && ok "f3b payroll: the accountant gets 403 on bonus runs" || bad "f3b payroll: the accountant gets 403 on bonus runs got $CODE"
as "$CEO_TOKEN" GET "$FPS/bonus-runs"; check "f3b payroll: the CEO may read bonus runs" '.success and (.data|length) >= 1'
# termination: hired 1401/01/01, ended 1405/06/31 = 4 years and 6 months = 54 full months; base 30 x 600,000 = 18,000,000
as "$PAYR_TOKEN" POST $FP/employees "$(EBJ T902 آزمون پایان 1234567891 2022-03-21T00:00:00 600000)"; PE3=$(val .data.id)
fv "{\"date\":\"2026-09-01T00:00:00\",\"description\":\"ذخیره سنوات آزمون\",\"lines\":[{\"accountCode\":\"80103\",\"debit\":1000000000},{\"accountCode\":\"40201\",\"credit\":1000000000}]}"
# the leave provision (30403) on the termination date, read from the ledger as of 2026-09-22 (not the current balance); it is below the leave pay of 6,000,000, so only the covered part is drawn
as "$FM_TOKEN" GET "$FG/reports/trial-balance?FiscalYearId=$FYC&Level=2&IncludeDraft=true&To=2026-09-22"; LVB=$(jq -r '([.data.rows[]|select(.code == "30403")][0] | ((.closingCredit // 0) - (.closingDebit // 0))) // 0 | round' "$B"); [ "${LVB:-0}" -lt 0 ] && LVB=0
LVEXP=$(( ${LVB:-0} < 6000000 ? ${LVB:-0} : 6000000 ))
[ "$LVB" -lt 6000000 ] && ok "f3b termination: the leave provision on the termination date ($LVB) is below the leave pay, so the partial-cover path is tested" || bad "f3b termination: the leave provision on the termination date ($LVB) is below the leave pay of 6,000,000"
as "$PAYR_TOKEN" POST $FPS/terminations "{\"employeeId\":$PE3,\"terminationDate\":\"2026-09-22T00:00:00\",\"unusedLeaveDays\":10,\"noticeDays\":0}"
check "f3b termination: 54 months, severance 81,000,000, leave 6,000,000, notice 0, total 87,000,000 (30 days per year from the rate table)" '.success and .data.yearsOfService == 4.5 and .data.monthlyBase == 18000000 and .data.dailyWage == 600000 and .data.severancePay == 81000000 and .data.leavePay == 6000000 and .data.noticePay == 0 and .data.total == 87000000 and .data.status == 0 and (.data.sources|length) >= 1'
TM=$(val .data.id)
as "$PAYR_TOKEN" POST $FPS/terminations/action "{\"id\":$TM,\"action\":\"approve\"}"; nomsg "f3b termination: the preparer cannot approve" "شخص دیگری"
TS_S0=$(cb 40201); TS_L0=$(cb 30403); TS_W0=$(cb 30201); TS_I0=$(cb 30202); TS_X0=$(cb 30203)
as "$FM_TOKEN" POST $FPS/terminations/action "{\"id\":$TM,\"action\":\"approve\"}"; check "f3b termination: approved; severance 81,000,000 covered by the provision, leave covered up to the provision balance" '.success and .data.status == 1 and .data.fromSeveranceProvision == 81000000 and .data.fromLeaveProvision == '$LVEXP' and .data.voucherId > 0 and .data.taxAmount == 0 and .data.insuranceAmount == 420000 and .data.netPayable == 86580000'
[ "$(cb 40201)" = "$((TS_S0 + 81000000))" ] && ok "f3b termination: the severance provision is debited exactly 81,000,000" || bad "f3b termination: the severance provision is debited exactly 81,000,000 (got $(( $(cb 40201) - TS_S0 )))"
# review 3, M4: the employee insurance on the leave pay of 6,000,000 (7%) is booked, so the net payable is 87,000,000 - 420,000
[ "$(cb 30202)" = "$((TS_I0 - 420000))" ] && [ "$(cb 30203)" = "$TS_X0" ] && [ "$(cb 30201)" = "$((TS_W0 - 86580000))" ] && ok "r3 m4: the termination books insurance 420,000 and a net payable of 86,580,000 (tax 0 in the exempt band)" || bad "r3 m4: insurance moved $(( TS_I0 - $(cb 30202) )), net payable moved $(( TS_W0 - $(cb 30201) ))"
# review 3, H6: the net is paid through treasury and clears the payroll payable
as "$TR_TOKEN" POST $FT/payments "{\"date\":\"${TODAY}T00:00:00\",\"purpose\":6,\"terminationId\":$TM,\"lines\":[{\"instrument\":2,\"amount\":86580000,\"bankAccountId\":$BA1,\"reference\":\"TERM-1405\"}]}"; check "r3 h6: the termination is drafted for payment by treasury for the net 86,580,000" '.success and .data.status == 0 and .data.netAmount == 86580000'
TMPM=$(val .data.id)
as "$FM_TOKEN" POST $FT/payments/action "{\"id\":$TMPM,\"action\":\"approve\"}"
as "$TR_TOKEN" POST $FT/payments/action "{\"id\":$TMPM,\"action\":\"post\"}"; check "r3 h6: the termination payment is posted" '.success and .data.status == 2'
as "$FM_TOKEN" POST $FG/post-events
[ "$(cb 30201)" = "$TS_W0" ] && ok "r3 h6: the payroll payable is back to its start after the termination payment" || bad "r3 h6: the payroll payable after the termination payment is off by $(( $(cb 30201) - TS_W0 ))"
# a draft with a large leave pay shows an exact tax line: leave 18,000,000 x 300 / 30 = 180,000,000; insurance 7% = 12,600,000; tax (167,400,000 x 12 - 240,000,000) x 10% / 12 = 14,740,000
as "$PAYR_TOKEN" POST $FP/employees "$(EBJ T903 آزمون مرخصی 0013542427 2022-03-21T00:00:00 600000)"; PE4=$(val .data.id)
as "$PAYR_TOKEN" POST $FPS/terminations "{\"employeeId\":$PE4,\"terminationDate\":\"2026-09-22T00:00:00\",\"unusedLeaveDays\":300,\"noticeDays\":0,\"monthlyBase\":18000000}"; check "r3 m4: a leave pay of 180,000,000 carries insurance 12,600,000 and income tax 14,740,000, net payable 233,660,000" '.success and .data.leavePay == 180000000 and .data.insuranceAmount == 12600000 and .data.taxAmount == 14740000 and .data.netPayable == 233660000'
TM4=$(val .data.id)
as "$PAYR_TOKEN" POST $FPS/terminations/action "{\"id\":$TM4,\"action\":\"cancel\"}"; check "r3 m4: the example draft is cancelled" '.success and .data.status == 2'
as "$PAYR_TOKEN" GET "$FP/employees/$PE3"; check "f3b termination: the employee end date is set to the termination date" '.success and (.data.endDate|tostring|test("2026-09-22|1405/06/31"))'
as "$PAYR_TOKEN" POST $FPS/terminations "{\"employeeId\":$PE3,\"terminationDate\":\"2026-09-22T00:00:00\",\"unusedLeaveDays\":0,\"noticeDays\":0}"; no "f3b termination: an employee already settled cannot be settled again"

# --- review 3, M7: a mixed voucher (Dr expense 1,000 / Cr tax 200 / Cr cash 800) is 800 of operating costs and nothing of taxes
CF_O0=$(cfrow OperatingCosts); CF_T0=$(cfrow Taxes)
fv "{\"date\":\"${TODAY}T00:00:00\",\"description\":\"سند آمیخته نقدی\",\"lines\":[{\"accountCode\":\"80101\",\"debit\":1000},{\"accountCode\":\"30203\",\"credit\":200},{\"accountCode\":\"10101\",\"credit\":800}]}"
[ "$(cfrow OperatingCosts)" = "$((CF_O0 - 800))" ] && [ "$(cfrow Taxes)" = "$CF_T0" ] && ok "r3 m7: the cash flow books operating costs -800 and taxes 0 for the mixed voucher" || bad "r3 m7: operating costs moved $(( $(cfrow OperatingCosts) - CF_O0 )), taxes $(( $(cfrow Taxes) - CF_T0 ))"
# --- direct-method cash flow, comparatives and notes
as "$FM_TOKEN" GET "$FCM/cash-flow-direct?FiscalYearId=$FYC&IncludeDraft=true"
check "f3b cashflow: the direct method nets to the change in cash and to the indirect method" '.success and .data.netChange == (.data.closingCash - .data.openingCash) and .data.netChange == .data.indirectNetChange and .data.netChange == (.data.operating + .data.investing + .data.financing) and ([.data.rows[].net]|add) == .data.netChange and .data.ties == true'
as "$FM_TOKEN" GET "$FCM/comparative?kind=BalanceSheet&fiscalYearId=$FYC&includeDraft=true"; check "f3b comparative: the balance sheet carries the prior year 1404 and every change is current minus prior" '.success and .data.priorYear == 1404 and (.data.rows|length) > 5 and ([.data.rows[]|select(.change != (.current - .prior))]|length) == 0'
as "$FM_TOKEN" GET "$FCM/comparative?kind=IncomeStatement&fiscalYearId=$FYC&includeDraft=true"; check "f3b comparative: the income statement carries the prior year too" '.success and .data.priorYear == 1404 and (.data.rows|length) > 3'
as "$FM_TOKEN" GET "$FCM/comparative?kind=Other&fiscalYearId=$FYC"; no "f3b comparative: an unknown statement kind is refused"
as "$FM_TOKEN" GET "$FCM/notes?fiscalYearId=$FYC"; check "f3b notes: the schedules add up (payables, inventory by group) and the loans are split into current and long-term" '.success and .data.payablesTotal == ([.data.payables[].total]|add // 0) and .data.inventoryTotal == ([.data.inventory[].balance]|add // 0) and ([.data.loans[]|select(.outstanding != (.currentPortion + .longTerm))]|length) == 0 and (.data.receivablesAgeing != null)'
as "$AUD_TOKEN" GET "$FCM/notes?fiscalYearId=$FYC"; check "f3b notes: the auditor can read the notes" '.success'
TOKEN=$ADMIN_TOKEN

# =============================================================================================== scope: rial loan and rial letter of credit unchanged
TOKEN=$ADMIN_TOKEN
# a rial loan keeps posting exactly as before
LRROWS=$(jq -nc '[range(1;4) | {dueDate: ((("2026-10-01T00:00:00Z"|fromdateiso8601) + (. * 2592000)) | todateiso8601 | sub("Z$";"")), principal: 1000000, interest: 0}]')
as "$TR_TOKEN" POST $FLN "$(jq -nc --argjson rows "$LRROWS" '{bankName:"بانک ریالی آزمون",amount:3000000,currencyCode:"IRR",startDate:"2026-10-01T00:00:00",ratePercent:0,scheduleKind:3,installmentCount:3,customRows:$rows}')"; check "scope: a rial loan of 3,000,000 with three instalments is saved" '.success and .data.id > 0'
LNR=$(val .data.id)
as "$FM_TOKEN" POST $FG/post-events
LS_L0=$(cb 30501); LS_B0=$(cb 10102); LS_X0=$(cb 80302); LS_G0=$(cb 60303)
CF_L0=$(cfrow Loans)
as "$TR_TOKEN" POST $FLN/action "{\"id\":$LNR,\"action\":\"disburse\",\"date\":\"2026-10-01T00:00:00\",\"bankAccountId\":$BA1}"; check "scope: the rial loan is disbursed without any rate" '.success and .data.status == 1'
[ "$(cb 30501)" = "$((LS_L0 - 3000000))" ] && [ "$(cb 10102)" = "$((LS_B0 + 3000000))" ] && ok "scope: the rial loan posts exactly 3,000,000 (liability credited, bank debited)" || bad "scope: the rial loan posts 3,000,000 (liability moved $(( LS_L0 - $(cb 30501) )), bank moved $(( $(cb 10102) - LS_B0 )))"
as "$TR_TOKEN" POST $FLN/pay "{\"loanId\":$LNR,\"sequence\":1,\"bankAccountId\":$BA1,\"date\":\"2026-10-24T00:00:00\"}"; check "scope: the first rial instalment is paid without any rate" '.success'
[ "$(cb 30501)" = "$((LS_L0 - 3000000 + 1000000))" ] && [ "$(cb 10102)" = "$((LS_B0 + 3000000 - 1000000))" ] && [ "$(cb 80302)" = "$LS_X0" ] && [ "$(cb 60303)" = "$LS_G0" ] && ok "scope: the rial instalment falls exactly 1,000,000 on the liability and the bank, with no exchange difference" || bad "scope: the rial instalment (liability $(( $(cb 30501) - LS_L0 )), bank $(( $(cb 10102) - LS_B0 )))"
[ "$(cfrow Loans)" = "$((CF_L0 + 2000000))" ] && ok "r3 v: the cash flow row Loans is exactly +3,000,000 received - 1,000,000 repaid" || bad "r3 v: the cash flow row Loans moved $(( $(cfrow Loans) - CF_L0 )), expected 2,000,000"
as "$TR_TOKEN" POST $FXA/letters-of-credit "{\"providerId\":$PA,\"bankAccountId\":$BA1,\"currencyCode\":\"IRR\",\"amount\":200000000,\"openDate\":\"${TODAY}T00:00:00\",\"expiryDate\":\"2030-01-01T00:00:00\",\"marginPercent\":20}"; check "scope: a rial letter of credit is opened" '.success and .data.status == 1 and .data.amount == 200000000 and .data.currencyCode == "IRR"'
LCR=$(val .data.id)
LR_B0=$(cb 10102); LR_D0=$(cb 10402); LR_P0=$(cb 30101); LR_F0=$(cb 80201); LR_G0=$(cb 60303); LR_L0=$(cb 80302)
as "$TR_TOKEN" POST $FXA/letters-of-credit/action "{\"id\":$LCR,\"action\":\"margin\",\"amount\":40000000,\"date\":\"${TODAY}T00:00:00\"}"; check "scope: rial letter of credit: margin 40,000,000 posted" '.success and .data.marginPaid == 40000000'
as "$TR_TOKEN" POST $FXA/letters-of-credit/action "{\"id\":$LCR,\"action\":\"fee\",\"amount\":500000,\"date\":\"${TODAY}T00:00:00\"}"; check "scope: rial letter of credit: fee 500,000 posted" '.success and .data.feesPaid == 500000'
as "$TR_TOKEN" POST $FXA/letters-of-credit/action "{\"id\":$LCR,\"action\":\"documents\",\"foreignInvoiceRef\":\"INV-RIAL-1\",\"date\":\"${TODAY}T00:00:00\"}"; check "scope: rial letter of credit: documents received" '.success and .data.status == 2'
as "$TR_TOKEN" POST $FXA/letters-of-credit/action "{\"id\":$LCR,\"action\":\"pay\",\"rate\":1.1,\"date\":\"${TODAY}T00:00:00\"}"; nomsg "r3 h7: a rial letter of credit cannot be paid at a typed rate of 1.1 (it would overpay 20,000,000)" "نرخ ۱"
as "$TR_TOKEN" POST $FXA/letters-of-credit/action "{\"id\":$LCR,\"action\":\"pay\",\"rate\":1,\"date\":\"${TODAY}T00:00:00\"}"; check "scope: rial letter of credit: payment at rate 1 = 200,000,000, margin 40,000,000 released, status paid" '.success and .data.status == 3 and .data.settledBase == 200000000 and ([.data.postings[]|select(.kind == 3)][0].amount) == 200000000 and ([.data.postings[]|select(.kind == 4)][0].amount) == 40000000'
[ "$(cb 10402)" = "$LR_D0" ] && ok "scope: rial letter of credit: the margin deposit account is back to its start (40,000,000 in, 40,000,000 released)" || bad "scope: rial letter of credit: the deposit account moved $(( $(cb 10402) - LR_D0 ))"
[ "$(cb 10102)" = "$((LR_B0 - 200500000))" ] && ok "scope: rial letter of credit: the bank paid exactly 200,500,000 (200,000,000 + 500,000 fee)" || bad "scope: rial letter of credit: the bank paid 200,500,000 (got $(( LR_B0 - $(cb 10102) )))"
[ "$(cb 30101)" = "$((LR_P0 + 200000000))" ] && [ "$(cb 80201)" = "$((LR_F0 + 500000))" ] && ok "scope: rial letter of credit: the supplier payable is debited 200,000,000 and the fee expense 500,000" || bad "scope: rial letter of credit: payable moved $(( $(cb 30101) - LR_P0 )), fee moved $(( $(cb 80201) - LR_F0 ))"
as "$FM_TOKEN" POST $FG/post-events
[ "$(rec ap)" = "$RA_AP0" ] && [ "$(rec bank)" = "$RA_BK0" ] && ok "r3 h2: after the loan, letter of credit, guarantee, credit and debit notes and the retention release the AP and bank reconciliation differences are unchanged" || bad "r3 h2: reconciliation differences moved: ap $(( $(rec ap) - RA_AP0 )), bank $(( $(rec bank) - RA_BK0 ))"
[ "$(cb 60303)" = "$LR_G0" ] && [ "$(cb 80302)" = "$LR_L0" ] && ok "scope: rial letter of credit: no exchange gain or loss is booked" || bad "scope: rial letter of credit: an exchange difference was booked"
TOKEN=$ADMIN_TOKEN

# --- R2 findings 3 and 5: job close-out with a material return, then the cost of sales on delivery
call PUT WorkOrders/change-condition "{\"workOrderId\":$WO1,\"newCondition\":2}"; check "r2 f3: work order 1 set to Done" '.success'
as "$FM_TOKEN" POST $FG/post-events
as "$FM_TOKEN" GET $FCO/workorders/$WO1/cost-sheet; JM0=$(val .data.material | cut -d. -f1); JS0=$(val .data.totalCost | cut -d. -f1)
bv() { as "$FM_TOKEN" GET "finance/valuation?contains=$C_BOLT"; jq -r '([.data.items[]|select(.itemCode == "'$C_BOLT'")][0].value // 0) | round' "$B"; }
BV0=$(bv)
call POST inventory/material-requests "{\"requestDate\":\"${TODAY}T00:00:00\",\"workOrderId\":$WO1,\"warehouseId\":$W1,\"purpose\":\"مواد دستور کار برای بستن\",\"lines\":[{\"itemId\":$BOLT,\"quantity\":10}]}"; MRJ=$(val .data.id)
call POST inventory/material-requests/action "{\"id\":$MRJ,\"action\":\"submit\"}"; call POST inventory/material-requests/action "{\"id\":$MRJ,\"action\":\"approve-planning\"}"
as "$PROD_TOKEN" POST inventory/material-requests/action "{\"id\":$MRJ,\"action\":\"approve-manufacturing\"}"; check "r2 f3: the material request for the job is approved" '.success and .data.status == 2'
call POST inventory/documents "{\"type\":4,\"documentDate\":\"${TODAY}T00:00:00\",\"warehouseId\":$W1,\"materialRequestId\":$MRJ,\"workOrderId\":$WO1,\"lines\":[{\"itemId\":$BOLT,\"quantity\":10}]}"; ISJ=$(val .data.id); call POST "inventory/documents/$ISJ/confirm"; check "r2 f3: 10 bolts issued to the work order" '.success'
BV1=$(bv); ISSV=$((BV0 - BV1))
as "$FM_TOKEN" POST $FG/post-events; as "$FM_TOKEN" GET $FCO/workorders/$WO1/cost-sheet; JM1=$(val .data.material | cut -d. -f1)
[ "$ISSV" -gt 0 ] && [ "$JM1" = "$((JM0 + ISSV))" ] && ok "r2 f3: the cost sheet material rose by exactly the value issued ($ISSV) per the issue document" || bad "r2 f3: the cost sheet material rose by the issued value $ISSV (got $((JM1 - JM0)))"
call POST inventory/documents "{\"type\":3,\"documentDate\":\"${TODAY}T00:00:00\",\"warehouseId\":$W1,\"workOrderId\":$WO1,\"lines\":[{\"itemId\":$BOLT,\"quantity\":4,\"locationId\":$L2}]}"; RTJ=$(val .data.id); call POST "inventory/documents/$RTJ/confirm"; check "r2 f3: 4 of the bolts returned from the work order" '.success'
BV2=$(bv); RETV=$((BV2 - BV1))
as "$FM_TOKEN" POST $FG/post-events; as "$FM_TOKEN" GET $FCO/workorders/$WO1/cost-sheet; JM2=$(val .data.material | cut -d. -f1); JS2=$(val .data.totalCost | cut -d. -f1)
[ "$RETV" -gt 0 ] && [ "$JM2" = "$((JM0 + ISSV - RETV))" ] && ok "r2 f3: the close-out cost sheet material equals the value issued ($ISSV) minus the value returned ($RETV): returns are subtracted once" || bad "r2 f3: cost sheet material $((JM2 - JM0)) should be issued $ISSV - returned $RETV = $((ISSV - RETV))"
FG_PRE=$(cb 10503)
as "$FIN_TOKEN" POST $FCO/workorders/$WO1/close "{\"quantity\":2,\"date\":\"${TODAY}T00:00:00\"}"; check "r2 f3: the close-out (2 units) moves exactly the cost sheet ($JS2) to finished goods with NO variance (the return is not subtracted twice)" '.success and .data.totalCost == '$JS2' and .data.toFinishedGoods == '$JS2' and .data.variance == 0 and .data.isOpen == false'
[ "$(cb 10503)" = "$((FG_PRE + JS2))" ] && ok "r2 f3: finished goods 10503 rise exactly $JS2" || bad "r2 f3: finished goods 10503 rise $JS2 (got $(( $(cb 10503) - FG_PRE )))"
CG0=$(cb 70101); BV3=$(bv)
D0=$(dd 0)
mkdelivery $WO1; check "r2 f5: the delivery of the closed job is drafted" '.success and .data.id > 0'; DLJ=$(val .data.id); as "$QC_TOKEN" POST "inventory/documents/$DLJ/approve" '{"kind":4}'; call POST "inventory/documents/$DLJ/confirm"; check "r2 f5: the delivery of the closed job is confirmed" '.success and .data.status == 1'
as "$FM_TOKEN" POST $FG/post-events
BOLTV=$((BV3 - $(bv)))
HALF=$(( (JS2 + 1) / 2 ))
[ "$(cb 10503)" = "$((FG_PRE + JS2 - HALF))" ] && ok "r2 f5: delivering 1 of the 2 closed units relieves finished goods 10503 by exactly half of the closed cost ($HALF of $JS2)" || bad "r2 f5: delivering 1 of 2 units relieves 10503 by $HALF (got $(( $(cb 10503) - FG_PRE - JS2 + HALF )) left over)"
[ "$BOLTV" -gt 0 ] && [ "$(( $(cb 70101) - CG0 ))" = "$((HALF + BOLTV))" ] && ok "r2 f5: cost of sales (70101) rose by exactly half of the closed job cost $HALF plus the delivered bolt $BOLTV" || bad "r2 f5: cost of sales rose by $HALF + $BOLTV = $((HALF + BOLTV)) (got $(( $(cb 70101) - CG0 )))"
# review 3, H3: cancelling that delivery gives the relief back, so a later delivery of both units relieves the full cost once
call POST "inventory/documents/$DLJ/cancel" '{"reason":"آزمون ابطال تحویل"}'; check "r3 h3: the delivery of the closed job is cancelled" '.success and .data.status == 2'
as "$FM_TOKEN" POST $FG/post-events
[ "$(cb 10503)" = "$((FG_PRE + JS2))" ] && [ "$(cb 70101)" = "$CG0" ] && ok "r3 h3: after the cancellation finished goods 10503 hold the whole closed cost again and cost of sales is back to its start" || bad "r3 h3: after the cancellation 10503 is off by $(( $(cb 10503) - FG_PRE - JS2 )) and cost of sales by $(( $(cb 70101) - CG0 ))"
BV4=$(bv)
mkdelivery $WO1 2; check "r3 h3: a delivery of both units is drafted" '.success and .data.id > 0'; DLJ2=$(val .data.id); as "$QC_TOKEN" POST "inventory/documents/$DLJ2/approve" '{"kind":4}'; call POST "inventory/documents/$DLJ2/confirm"; check "r3 h3: the delivery of both units is confirmed" '.success and .data.status == 1'
as "$FM_TOKEN" POST $FG/post-events
BOLTV2=$((BV4 - $(bv)))
[ "$(cb 10503)" = "$FG_PRE" ] && ok "r3 h3: delivering both units relieves finished goods 10503 back to its start (nothing is left behind)" || bad "r3 h3: delivering both units leaves $(( $(cb 10503) - FG_PRE )) in finished goods"
[ "$BOLTV2" -gt 0 ] && [ "$(( $(cb 70101) - CG0 ))" = "$((JS2 + BOLTV2))" ] && ok "r3 h3: cost of sales rose by exactly the closed job cost $JS2 plus the delivered bolts $BOLTV2" || bad "r3 h3: cost of sales rose by $JS2 + $BOLTV2 = $((JS2 + BOLTV2)) (got $(( $(cb 70101) - CG0 )))"
TOKEN=$ADMIN_TOKEN

# =============================================================================================== OFFICE AUTOMATION (release O1)
TOKEN=$ADMIN_TOKEN
OF=office
OFA=office/attendance
user2 hr.ci 27 'Ci#Hr-User-2026' 09120000041 0012345641
user2 secr.ci 26 'Ci#Secr-User-2026' 09120000042 0012345642
user2 emp.ci 4 'Ci#Emp-User-2026' 09120000043 0012345643
user2 mgr.ci 4 'Ci#Mgr-User-2026' 09120000044 0012345644
user2 chk.ci 4 'Ci#Chk-User-2026' 09120000045 0012345645
OHR_TOKEN=$(login hr.ci 'Ci#Hr-User-2026'); OSEC_TOKEN=$(login secr.ci 'Ci#Secr-User-2026'); OEMP_TOKEN=$(login emp.ci 'Ci#Emp-User-2026')
OMGR_TOKEN=$(login mgr.ci 'Ci#Mgr-User-2026'); OCHK_TOKEN=$(login chk.ci 'Ci#Chk-User-2026')
[ -n "$OHR_TOKEN" ] && [ -n "$OSEC_TOKEN" ] && [ -n "$OEMP_TOKEN" ] && [ -n "$OMGR_TOKEN" ] && [ -n "$OCHK_TOKEN" ] && ok "office: login of the new roles SECRETARIAT (26), HR (27) and three ordinary users" || bad "office: login of the new roles SECRETARIAT (26), HR (27) and three ordinary users"
OHRID=$(uid "$OHR_TOKEN"); OSECID=$(uid "$OSEC_TOKEN"); OEMPID=$(uid "$OEMP_TOKEN"); OMGRID=$(uid "$OMGR_TOKEN"); OCHKID=$(uid "$OCHK_TOKEN"); OCEOID=$(uid "$CEO_TOKEN")
[ -n "$OHRID" ] && [ -n "$OSECID" ] && [ -n "$OEMPID" ] && [ -n "$OMGRID" ] && [ -n "$OCHKID" ] && [ -n "$OCEOID" ] && ok "office: user ids resolved" || bad "office: user ids resolved"
# the Persian year of today (letter numbers use {yyyy}/{yy} of the Persian calendar)
OTY=$(date +%Y); OTM=$((10#$(date +%m))); OTD=$((10#$(date +%d)))
if [ "$OTM" -gt 3 ] || { [ "$OTM" -eq 3 ] && [ "$OTD" -ge 21 ]; }; then OPY=$((OTY - 621)); else OPY=$((OTY - 622)); fi
OPY2=${OPY: -2}
# ibx TOKEN SOURCEKEY [EXTRA QUERY] : the inbox of a user filtered by the source key
ibx() { as "$1" GET "$OF/inbox?SourceKey=$2${3:+&$3}"; }

# --- the employee of the personnel file (finance F2) linked to a user
as "$PAYR_TOKEN" POST $FP/employees "$(EJ ATT01 حضور آزمون 1234567822 IR270170000000100324200001 | jq -c --argjson u "$OEMPID" '. + {userId:$u}')"; check "office: payroll employee ATT01 linked to the user" ".success and .data.id > 0 and .data.userId == $OEMPID"
OAEMP=$(val .data.id)

# --- organisation chart: units, positions, delegation
call GET $OF/org/tree; check "office: the chart starts with the root unit only and no seeded positions" '.success and .data.code == "CO" and .data.title == "شرکت" and (.data.positions|length) == 0 and (.data.children|length) == 0'
call POST $OF/units '{"code":"ops","title":"واحد عملیات"}'; check "office: unit saved with an upper-case code under the root" '.success and .data.code == "OPS" and .data.parentId > 0'
OU1=$(val .data.id); OROOT=$(val .data.parentId)
as "$OEMP_TOKEN" POST $OF/units '{"code":"X1","title":"x"}'; no "office: an ordinary user cannot change the chart"
call POST $OF/units '{"code":"OPS","title":"تکراری"}'; no "office: a duplicate unit code is refused"
as "$OHR_TOKEN" POST $OF/positions "{\"title\":\"مدیرعامل\",\"unitId\":$OROOT,\"userId\":$OCEOID,\"isManager\":true,\"canSign\":true}"; check "office: CEO position saved" '.success and .data.id > 0 and .data.isManager == true'
OP_CEO=$(val .data.id)
as "$OHR_TOKEN" POST $OF/positions "{\"title\":\"مدیر عملیات\",\"unitId\":$OU1,\"userId\":$OMGRID,\"isManager\":true,\"canSign\":true}"; check "office: manager position saved" '.success and .data.id > 0 and .data.canSign == true'
OP_MGR=$(val .data.id)
as "$OHR_TOKEN" POST $OF/positions "{\"title\":\"ناظر عملیات\",\"unitId\":$OU1,\"userId\":$OCHKID,\"canSign\":false}"; check "office: checker position saved" '.success and .data.id > 0 and .data.canSign == false'
OP_CHK=$(val .data.id)
as "$OHR_TOKEN" POST $OF/positions "{\"title\":\"کارشناس عملیات\",\"unitId\":$OU1,\"userId\":$OEMPID,\"employeeId\":$OAEMP}"; check "office: expert position linked to the personnel record" ".success and .data.id > 0 and .data.employeeId == $OAEMP"
OP_EMP=$(val .data.id)
as "$OHR_TOKEN" POST $OF/positions "{\"title\":\"مسئول دبیرخانه\",\"unitId\":$OROOT,\"userId\":$OSECID}"; check "office: secretariat position saved" '.success and .data.id > 0'
OP_SEC=$(val .data.id)
as "$CEO_TOKEN" POST $OF/positions "{\"title\":\"مسئول اداری\",\"unitId\":$OROOT,\"userId\":$OHRID}"; check "office: administration position saved" '.success and .data.id > 0'
OP_HR=$(val .data.id)
# R1 H4: the holder of a position decides who reads its letters, so only HR assigns it (the CEO only for a vacant position), never for oneself
call POST $OF/positions "{\"title\":\"سمت آزمون\",\"unitId\":$OU1,\"userId\":$OCHKID}"; nomsg "office r1 H4: the PMO administrator cannot assign the holder of a position" "فقط مسئول منابع انسانی"
as "$OHR_TOKEN" POST $OF/positions "{\"title\":\"سمت آزمون\",\"unitId\":$OU1,\"userId\":$OHRID}"; nomsg "office r1 H4: HR cannot assign himself as the holder of a position" "تعیین خود"
as "$CEO_TOKEN" POST $OF/positions "{\"title\":\"سمت آزمون\",\"unitId\":$OU1,\"userId\":$OCEOID}"; nomsg "office r1 H4: the CEO cannot assign himself either" "تعیین خود"
as "$CEO_TOKEN" POST $OF/positions "{\"id\":$OP_MGR,\"title\":\"مدیر عملیات\",\"unitId\":$OU1,\"userId\":$OCHKID,\"isManager\":true,\"canSign\":true}"; nomsg "office r1 H4: the CEO cannot take a position from its holder" "فقط مسئول منابع انسانی"
ibx "$OMGR_TOKEN" "position-holder:$OP_MGR:$OMGRID"; check "office r1 H4: the new holder was told about his position in his inbox" '.success and (.data.data|length) == 1 and .data.data[0].kind == 5 and (.data.data[0].title|contains("مدیر عملیات"))'
as "$OHR_TOKEN" POST $OF/positions "{\"id\":$OP_CHK,\"title\":\"ناظر عملیات\",\"unitId\":$OU1,\"userId\":$OCHKID,\"canSign\":false}"; check "office r1 H4: a position can still be edited without changing its holder" '.success and .data.id > 0'
call GET "$OF/units?Contains=OPS"; check "office: the manager position of the unit is the one flagged as manager" ".success and .data.data[0].managerPositionId == $OP_MGR and .data.data[0].positionCount == 3"
call GET $OF/org/tree; check "office: the chart tree shows the unit with its three positions" '.success and ([.data.children[]|select(.code == "OPS")][0].positions|length) == 3'
as "$OMGR_TOKEN" GET $OF/me; check "office: a user sees the positions he holds" ".success and (.data.positions|length) == 1 and .data.positions[0].positionId == $OP_MGR and .data.positions[0].onBehalf == false"

# --- numbering rules (default {yy}/{kind}/{unit}/{seq:5}; a rule of a unit overrides it)
call GET $OF/numbering-rules; check "office: three default numbering rules (incoming, outgoing, internal) with the pattern {yy}/{kind}/{unit}/{seq:5}" '.success and ([.data.data[]|select(.pattern == "{yy}/{kind}/{unit}/{seq:5}" and .unitId == null)]|length) == 3'
as "$OEMP_TOKEN" POST $OF/numbering-rules "{\"kind\":2,\"unitId\":$OU1,\"pattern\":\"X-{seq:4}\"}"; no "office: an ordinary user cannot change numbering rules"
call POST $OF/numbering-rules "{\"kind\":2,\"unitId\":$OU1,\"pattern\":\"OUT-{yyyy}-{unit}\"}"; no "office: a pattern without {seq} is refused"
call POST $OF/numbering-rules "{\"kind\":2,\"unitId\":$OU1,\"pattern\":\"OUT-{yyyy}-{unit}-{seq:4}\"}"; check "office: the rule of the unit previews the next number exactly" ".success and .data.preview == \"OUT-$OPY-OPS-0001\""

# --- letter 1: outgoing, draft -> approval chain -> signature -> number -> registered -> sent
as "$OSEC_TOKEN" POST $OF/parties '{"name":"سازمان آزمون خارجی","kind":3,"email":"party@example.com"}'; check "office: external party saved" '.success and .data.id > 0'
OPARTY=$(val .data.id)
as "$OEMP_TOKEN" POST $OF/letters "{\"kind\":2,\"subject\":\"نامه صادره آزمون\",\"bodyHtml\":\"<p>متن نامه</p><script>alert(1)</script>\",\"senderPositionId\":$OP_EMP,\"signerPositionId\":$OP_MGR,\"recipients\":[{\"type\":1,\"partyId\":$OPARTY}]}"
check "office: outgoing draft has no number, the script tag is removed and the date is shown in the Persian calendar" '.success and .data.status == 0 and .data.number == null and (.data.bodyHtml|contains("script")|not) and (.data.bodyHtml|contains("متن نامه")) and (.data.dateText|test("^14[0-9]{2}/[0-9]{2}/[0-9]{2}$"))'
OL1=$(val .data.id)
as "$QC_TOKEN" GET $OF/letters/$OL1; no "office: a draft is invisible to everybody but its writer"
as "$OEMP_TOKEN" POST $OF/letters/action "{\"id\":$OL1,\"action\":\"submit\",\"approvalChain\":[$OP_CHK]}"; check "office: letter submitted: in approval with one pending approval" '.success and .data.status == 1 and (.data.approvals|length) == 1 and .data.approvals[0].status == 0'
ibx "$OCHK_TOKEN" "letter:$OL1:approval"; check "office: the checker position has the approval in its inbox" ".success and (.data.data|length) == 1 and .data.data[0].kind == 3 and .data.data[0].status < 2 and .data.data[0].recipientPositionId == $OP_CHK"
as "$OMGR_TOKEN" POST $OF/letters/action "{\"id\":$OL1,\"action\":\"approve\"}"; nomsg "office: the signer cannot approve out of turn" "نوبت تایید با سمت دیگری"
as "$OEMP_TOKEN" POST $OF/letters/action "{\"id\":$OL1,\"action\":\"approve\"}"; no "office: the writer cannot approve his own letter"
as "$OCHK_TOKEN" POST $OF/letters/action "{\"id\":$OL1,\"action\":\"approve\",\"comment\":\"مورد تایید\"}"; check "office: the checker approves" '.success and .data.status == 1 and .data.approvals[0].status == 1 and (.data.approvals[0].actedByName|length) > 0'
ibx "$OCHK_TOKEN" "letter:$OL1:approval"; check "office: the approval item of the checker is closed" '.success and (.data.data|length) == 0'
ibx "$OMGR_TOKEN" "letter:$OL1:approval"; check "office: the signature item is waiting for the signer position" '.success and (.data.data|length) == 1 and (.data.data[0].title|contains("امضا"))'
as "$OCHK_TOKEN" POST $OF/letters/action "{\"id\":$OL1,\"action\":\"sign\"}"; nomsg "office: a position other than the signer cannot sign (checker)" "فقط سمت امضاکننده"
as "$OEMP_TOKEN" POST $OF/letters/action "{\"id\":$OL1,\"action\":\"sign\"}"; nomsg "office: a position other than the signer cannot sign (writer)" "فقط سمت امضاکننده"
as "$OMGR_TOKEN" POST $OF/letters/action "{\"id\":$OL1,\"action\":\"sign\"}"; nomsg "office: the signer needs a stored signature image" "تصویر امضا"
as "$OMGR_TOKEN" POST $OF/me/signature "{\"documentId\":\"$DOCID\"}"; check "office: signature image stored for the signer" '.success and .data.hasSignature == true'
as "$OMGR_TOKEN" POST $OF/letters/action "{\"id\":$OL1,\"action\":\"sign\"}"; check "office: signed: number by the rule pattern of the unit (exact), content hash and signature image stored" ".success and .data.status == 2 and .data.number == \"OUT-$OPY-OPS-0001\" and (.data.contentHash|length) > 0 and .data.signatureDocumentId == \"$DOCID\" and (.data.signedByName|length) > 0"
as "$OEMP_TOKEN" POST $OF/letters "{\"id\":$OL1,\"kind\":2,\"subject\":\"تغییر پس از امضا\",\"senderPositionId\":$OP_EMP,\"recipients\":[{\"type\":1,\"partyId\":$OPARTY}]}"; nomsg "office: a signed letter cannot be edited" "قابل ویرایش نیست"
as "$OMGR_TOKEN" GET $OF/letters/$OL1/verify; check "office: the stored hash equals the hash computed again from the content and the files, and no file changed" '.success and .data.signed == true and .data.valid == true and (.data.storedHash|length) > 0 and .data.storedHash == .data.computedHash and (.data.changedDocumentIds|length) == 0'
ibx "$OSEC_TOKEN" "letter:$OL1:dispatch"; check "office: the secretariat has the signed letter to register and send" '.success and (.data.data|length) == 1 and .data.data[0].recipientRole == "SECRETARIAT"'
as "$OEMP_TOKEN" POST $OF/letters/action "{\"id\":$OL1,\"action\":\"register\"}"; no "office: only the secretariat registers a letter"
as "$OSEC_TOKEN" POST $OF/letters/action "{\"id\":$OL1,\"action\":\"register\"}"; check "office: registered by the secretariat, number unchanged" ".success and .data.status == 3 and .data.number == \"OUT-$OPY-OPS-0001\""
as "$OSEC_TOKEN" POST $OF/letters/action "{\"id\":$OL1,\"action\":\"send\"}"; nomsg "office: sending needs a delivery method" "روش ارسال"
as "$OSEC_TOKEN" POST $OF/letters/action "{\"id\":$OL1,\"action\":\"send\",\"deliveryMethod\":3}"; check "office: sent by courier" '.success and .data.status == 4 and .data.deliveryMethod == 3 and (.data.sentDateText|test("^14[0-9]{2}/[0-9]{2}/[0-9]{2}$"))'
ibx "$OSEC_TOKEN" "letter:$OL1:dispatch"; check "office: the dispatch item is closed after sending" '.success and (.data.data|length) == 0'
as "$OSEC_TOKEN" GET "$OF/letters?Number=OUT-$OPY-OPS-0001"; check "office: search by number finds the letter" ".success and (.data.data|length) == 1 and .data.data[0].id == $OL1"

# --- incoming letter: numbered at registration by the secretariat
as "$OEMP_TOKEN" POST $OF/letters "{\"kind\":1,\"subject\":\"وارده\",\"senderPartyId\":$OPARTY,\"incomingNumber\":\"EXT-1\",\"recipients\":[{\"type\":1,\"positionId\":$OP_MGR}],\"register\":true}"; nomsg "office: only the secretariat registers incoming letters" "فقط توسط دبیرخانه"
as "$OSEC_TOKEN" POST $OF/letters "{\"kind\":1,\"subject\":\"نامه وارده آزمون\",\"senderPartyId\":$OPARTY,\"incomingNumber\":\"EXT-77\",\"incomingDate\":\"2026-10-01T00:00:00\",\"recipients\":[{\"type\":1,\"positionId\":$OP_MGR}],\"register\":true}"
check "office: incoming letter numbered by the default rule (exact) and received" ".success and .data.status == 5 and .data.number == \"$OPY2/و/OPS/00001\" and .data.incomingNumber == \"EXT-77\""
OI1=$(val .data.id)
ibx "$OMGR_TOKEN" "letter:$OI1:delivery"; check "office: the incoming letter is in the inbox of the recipient position" '.success and (.data.data|length) == 1 and .data.data[0].kind == 1'
ibx "$OEMP_TOKEN" "letter:$OI1:delivery"; check "office: and not in the inbox of anybody else" '.success and (.data.data|length) == 0'

# --- referral tree
as "$OMGR_TOKEN" POST $OF/letters/refer "{\"letterId\":$OI1,\"fromPositionId\":$OP_MGR,\"toPositionIds\":[$OP_EMP],\"instruction\":\"بررسی و اقدام\",\"action\":1,\"dueDate\":\"2030-01-01T00:00:00\"}"; check "office: referral for action to the expert" ".success and (.data.referrals|length) == 1 and .data.referrals[0].toPositionId == $OP_EMP and .data.referrals[0].status == 0"
ORA=$(val '.data.referrals[0].id')
as "$OMGR_TOKEN" POST $OF/letters/refer "{\"letterId\":$OI1,\"fromPositionId\":$OP_MGR,\"toPositionIds\":[$OP_CHK],\"instruction\":\"جهت اطلاع\",\"action\":2}"; check "office: referral for information to the checker" '.success and (.data.referrals|length) == 2'
ORB=$(jq -r ".data.referrals[]|select(.toPositionId == $OP_CHK)|.id" "$B")
as "$OEMP_TOKEN" POST $OF/letters/refer "{\"letterId\":$OI1,\"parentReferralId\":$ORA,\"toPositionIds\":[$OP_CHK],\"instruction\":\"نظر فنی\",\"action\":3}"; check "office: the receiver refers on: the tree keeps the child under its parent" ".success and (.data.referrals|length) == 2 and ([.data.referrals[]|select(.id == $ORA)][0].children|length) == 1 and ([.data.referrals[]|select(.id == $ORA)][0].children[0].toPositionId) == $OP_CHK"
ORC=$(jq -r ".data.referrals[]|select(.id == $ORA)|.children[0].id" "$B")
as "$QC_TOKEN" POST $OF/letters/refer "{\"letterId\":$OI1,\"toPositionIds\":[$OP_CHK],\"instruction\":\"x\",\"action\":2}"; no "office: somebody who does not hold the letter cannot refer it"
ibx "$OCHK_TOKEN" "referral:$ORB"; check "office: the information referral is in the inbox of the checker" '.success and (.data.data|length) == 1 and .data.data[0].kind == 2'
ibx "$OCHK_TOKEN" "referral:$ORC"; check "office: the child referral is in the inbox of the checker" '.success and (.data.data|length) == 1'
as "$OMGR_TOKEN" GET $OF/letters/$OI1; check "office: the maker of a pending referral may cancel it (canCancel)" ".success and ([.data.referrals[]|select(.id == $ORB)][0].canCancel == true)"
as "$OCHK_TOKEN" GET $OF/letters/$OI1; check "office: its receiver may not cancel it (canCancel false)" ".success and ([.data.referrals[]|select(.id == $ORB)][0].canCancel == false)"
as "$OEMP_TOKEN" POST $OF/letters/referral-action "{\"id\":$ORA,\"action\":\"close\"}"; nomsg "office: a referral for action needs a note to close" "شرح اقدام"
as "$OMGR_TOKEN" POST $OF/letters/referral-action "{\"id\":$ORC,\"action\":\"close\"}"; nomsg "office: only the receiver closes a referral" "فقط گیرنده"
as "$OEMP_TOKEN" POST $OF/letters/referral-action "{\"id\":$ORA,\"action\":\"comment\",\"text\":\"گزارش تهیه شد\"}"; check "office: the receiver records an action entry" ".success and ([.data.referrals[]|select(.id == $ORA)][0].entries|length) == 1"
as "$OEMP_TOKEN" POST $OF/letters/referral-action "{\"id\":$ORA,\"action\":\"close\",\"text\":\"انجام شد\"}"; check "office: the referral for action is closed with its note" ".success and ([.data.referrals[]|select(.id == $ORA)][0]|.status == 1 and .doneNote == \"انجام شد\")"
ibx "$OMGR_TOKEN" "referral-done:$ORA"; check "office: the one who referred is told that it was closed" '.success and (.data.data|length) == 1 and .data.data[0].kind == 5'
as "$OCHK_TOKEN" POST $OF/letters/referral-action "{\"id\":$ORB,\"action\":\"close\"}"; check "office: an information referral closes without a note" ".success and ([.data.referrals[]|select(.id == $ORB)][0].status) == 1"
as "$OCHK_TOKEN" POST $OF/letters/referral-action "{\"id\":$ORC,\"action\":\"close\",\"text\":\"نظر داده شد\"}"; check "office: the child referral is closed" ".success and ([.data.referrals[]|select(.id == $ORA)][0].children[0].status) == 1"
as "$OCHK_TOKEN" POST $OF/letters/referral-action "{\"id\":$ORC,\"action\":\"close\"}"; no "office: a closed referral cannot be closed again"
ibx "$OCHK_TOKEN" "referral:$ORB" "IncludeClosed=true"; check "office: closing a referral closes its inbox item" '.success and (.data.data|length) == 1 and .data.data[0].status == 2'
ibx "$OCHK_TOKEN" "referral:$ORC"; check "office: no open item is left for the child referral" '.success and (.data.data|length) == 0'

# --- legacy archive import: numbers kept as text, the new sequence is not consumed
as "$OEMP_TOKEN" POST $OF/letters/import '{"rows":[{"number":"1399/ص/123","date":"1399/05/17","kind":"incoming","subject":"نامه قدیمی یک","party":"شرکت قدیمی"}]}'; no "office: only the secretariat imports the archive"
OIMP='{"rows":[{"number":"1399/ص/123","date":"1399/05/17","kind":"incoming","subject":"نامه قدیمی یک","party":"شرکت قدیمی"},{"number":"1399/OUT/45","date":"1399/06/02","kind":"outgoing","subject":"نامه قدیمی دو","party":"شرکت قدیمی"}]}'
as "$OSEC_TOKEN" POST $OF/letters/import "$OIMP"; check "office: two legacy letters imported" '.success and .data.imported == 2 and .data.skipped == 0 and (.data.errors|length) == 0'
as "$OSEC_TOKEN" POST $OF/letters/import "$OIMP"; check "office: importing the same manifest again skips both" '.success and .data.imported == 0 and .data.skipped == 2'
as "$OSEC_TOKEN" GET "$OF/letters?Number=1399/"; check "office: the legacy letters keep their numbers, are archived and flagged as imported" '.success and ([.data.data[]|select(.number == "1399/ص/123" and .imported == true and .status == 6 and .kind == 1 and .dateText == "1399/05/17")]|length) == 1 and ([.data.data[]|select(.number == "1399/OUT/45" and .imported == true and .status == 6 and .kind == 2)]|length) == 1'
as "$OSEC_TOKEN" POST $OF/letters/import '{"rows":[{"number":"x","date":"not a date","kind":"incoming","subject":"s"}]}'; check "office: an invalid legacy row is reported and nothing is imported" '.success and .data.imported == 0 and (.data.errors|length) == 1'

# --- delegation: the deputy sees and acts on the inbox of the position
as "$OMGR_TOKEN" POST $OF/delegations "{\"positionId\":$OP_MGR,\"deputyUserId\":$OCHKID,\"fromDate\":\"2026-01-01T00:00:00\",\"toDate\":\"2035-12-31T00:00:00\"}"; nomsg "office r1 H4: a delegation needs a reason" "علت نیابت"
as "$OMGR_TOKEN" POST $OF/delegations "{\"positionId\":$OP_MGR,\"deputyUserId\":$OCHKID,\"fromDate\":\"2026-01-01T00:00:00\",\"toDate\":\"2035-12-31T00:00:00\",\"reason\":\"ماموریت\"}"; check "office: delegation of the manager position to the checker saved by its holder (approved, current)" '.success and .data.current == true and .data.status == 0 and (.data.fromDateText|test("^14[0-9]{2}/[0-9]{2}/[0-9]{2}$"))'
ODEL=$(val .data.id)
as "$OMGR_TOKEN" POST $OF/delegations "{\"positionId\":$OP_MGR,\"deputyUserId\":$OMGRID,\"fromDate\":\"2026-01-01T00:00:00\",\"toDate\":\"2035-12-31T00:00:00\",\"reason\":\"x\"}"; nomsg "office: the holder cannot be his own deputy" "تعیین خود"
as "$OHR_TOKEN" POST $OF/delegations "{\"positionId\":$OP_EMP,\"deputyUserId\":$OHRID,\"fromDate\":\"2026-01-01T00:00:00\",\"toDate\":\"2035-12-31T00:00:00\",\"reason\":\"x\"}"; nomsg "office r1 H4: the one who registers a delegation cannot make himself the deputy" "تعیین خود"
# R1 H4: a delegation registered by somebody else (HR) is pending: no access until the holder (or the CEO) approves it
as "$OHR_TOKEN" POST $OF/delegations "{\"positionId\":$OP_EMP,\"deputyUserId\":$OCHKID,\"fromDate\":\"2026-01-01T00:00:00\",\"toDate\":\"2035-12-31T00:00:00\",\"reason\":\"مرخصی کارشناس\"}"; check "office r1 H4: a delegation registered by HR for somebody else's position is pending and not current" '.success and .data.status == 1 and .data.current == false and (.data.createdByName|length) > 0'
ODEL2=$(val .data.id)
as "$OCHK_TOKEN" GET $OF/me; check "office r1 H4: while pending the deputy does not act for that position" ".success and ([.data.positions[]|select(.positionId == $OP_EMP)]|length) == 0"
ibx "$OEMP_TOKEN" "delegation:$ODEL2"; check "office r1 H4: the holder of the position has the pending delegation in his inbox" '.success and (.data.data|length) == 1 and .data.data[0].kind == 5'
as "$OHR_TOKEN" GET "$OF/delegations?MineOnly=true&PageSize=100"; check "office: HR (who registered it) sees the pending delegation with cancel only, no approve or reject" ".success and ([.data.data[]|select(.id == $ODEL2)][0].allowedActions == [\"cancel\"])"
as "$OEMP_TOKEN" GET "$OF/delegations?MineOnly=true&PageSize=100"; check "office: the holder sees it with approve, reject and cancel" ".success and ([.data.data[]|select(.id == $ODEL2)][0].allowedActions == [\"approve\",\"reject\",\"cancel\"])"
as "$QC_TOKEN" GET "$OF/delegations?MineOnly=true&PageSize=100"; check "office: a stranger's own list does not hold it" ".success and ([.data.data[]|select(.id == $ODEL2)]|length) == 0"
as "$OHR_TOKEN" POST $OF/delegations/$ODEL2/approve; nomsg "office r1 H4: the one who registered the delegation cannot approve it" "نمی‌تواند تایید کند"
call POST $OF/delegations/$ODEL2/approve; nomsg "office r1 H4: an administrator who does not hold the position cannot approve it" "فقط دارنده سمت"
as "$OEMP_TOKEN" POST $OF/delegations/$ODEL2/approve; check "office r1 H4: the holder approves it" '.success and .data.status == 0 and .data.current == true and (.data.decidedByName|length) > 0'
as "$OCHK_TOKEN" GET $OF/me; check "office r1 H4: after the approval the deputy acts for the position" ".success and ([.data.positions[]|select(.positionId == $OP_EMP and .onBehalf == true)]|length) == 1"
ibx "$OEMP_TOKEN" "delegation:$ODEL2"; check "office r1 H4: the inbox item of the holder is closed by the decision" '.success and (.data.data|length) == 0'
as "$OEMP_TOKEN" POST $OF/delegations/$ODEL2/cancel; check "office r1 H4: the holder cancels the delegation" '.success'
as "$FM_TOKEN" GET "$FAU?EntityType=OfDelegation&EntityId=$ODEL2&PageSize=0"; check "office r1 M7: the approval and the cancellation of the delegation are in the audit trail, each with the user" '.success and ([.data.data[]|select(.action == "Modified" and (.userName|length) > 0)]|length) >= 2'
as "$FM_TOKEN" GET "$FAU?EntityType=OfPosition&Action=Added&PageSize=0"; check "office r1 M7: the creation of the manager position (holder, flags) is in the audit trail with the user" '.success and ([.data.data[]|select(((.afterJson|fromjson).Title == "مدیر عملیات") and (.userName|length) > 0)]|length) >= 1'
as "$FM_TOKEN" GET "$FAU?EntityType=OfDelegation&Action=Added&PageSize=0"; check "office r1 M7: the registration of a delegation is in the audit trail with its reason and Persian dates" '.success and ([.data.data[]|select(((.afterJson|fromjson).Reason == "مرخصی کارشناس") and ((.afterJson|fromjson).FromDate|test("^14[0-9]{2}/[0-9]{2}/[0-9]{2}$")) and ((.afterJson|tostring|test("20[0-9]{2}-[0-9]{2}-[0-9]{2}"))|not))]|length) >= 1'
as "$OCHK_TOKEN" GET $OF/me; check "office: the deputy sees the position on behalf of the holder" ".success and ([.data.positions[]|select(.positionId == $OP_MGR and .onBehalf == true)]|length) == 1"
as "$OSEC_TOKEN" POST $OF/letters "{\"kind\":1,\"subject\":\"نامه وارده دوم\",\"senderPartyId\":$OPARTY,\"incomingNumber\":\"EXT-78\",\"recipients\":[{\"type\":1,\"positionId\":$OP_MGR}],\"register\":true}"
check "office: the second incoming letter gets the next number: the legacy import did not consume the sequence" ".success and .data.number == \"$OPY2/و/OPS/00002\""
OI2=$(val .data.id)
ibx "$OCHK_TOKEN" "letter:$OI2:delivery"; check "office: the deputy finds the letter of the position in his inbox" '.success and (.data.data|length) == 1'
ibx "$OEMP_TOKEN" "letter:$OI2:delivery"; check "office: an unrelated user does not" '.success and (.data.data|length) == 0'

# --- confidential internal letter signed by the deputy on behalf of the signer; invisible to a non-party
as "$OCHK_TOKEN" POST $OF/me/signature "{\"documentId\":\"$DOCID\"}"; check "office: the deputy has his own signature image" '.success and .data.hasSignature == true'
as "$OEMP_TOKEN" POST $OF/letters "{\"kind\":3,\"subject\":\"نامه داخلی محرمانه\",\"classification\":1,\"senderPositionId\":$OP_EMP,\"signerPositionId\":$OP_MGR,\"recipients\":[{\"type\":1,\"positionId\":$OP_CEO}]}"; check "office: confidential internal draft saved" '.success and .data.classification == 1'
OL2=$(val .data.id)
as "$OEMP_TOKEN" POST $OF/letters/action "{\"id\":$OL2,\"action\":\"submit\"}"; check "office: submitted without approvers: waits for the signature" '.success and .data.status == 1 and (.data.approvals|length) == 0'
as "$OCHK_TOKEN" POST $OF/letters/action "{\"id\":$OL2,\"action\":\"sign\"}"; check "office: the deputy signs on behalf; internal letter is numbered by the default rule and delivered" ".success and .data.status == 4 and .data.number == \"$OPY2/د/OPS/00001\" and (.data.signedOnBehalfOfPosition|length) > 0"
as "$QC_TOKEN" GET $OF/letters/$OL2; no "office: a confidential letter is invisible to a non-party"
as "$QC_TOKEN" GET "$OF/letters?PageSize=100"; check "office: and absent from his letter list" ".success and ([.data.data[]|select(.id == $OL2)]|length) == 0"
as "$OEMP_TOKEN" GET $OF/letters/$OL2; check "office: the writer sees it" '.success and .data.classification == 1'
as "$CEO_TOKEN" GET $OF/letters/$OL2; check "office: the CEO sees it" '.success and .data.classification == 1'
ibx "$CEO_TOKEN" "letter:$OL2:delivery"; check "office: it is in the inbox of the CEO position" '.success and (.data.data|length) == 1'
call POST $OF/delegations/$ODEL/cancel; check "office: delegation cancelled" '.success'
as "$OCHK_TOKEN" GET $OF/me; check "office: after the cancellation the deputy no longer acts for the position" '.success and ([.data.positions[]|select(.onBehalf == true)]|length) == 0'
as "$OSEC_TOKEN" POST $OF/letters/action "{\"id\":$OL1,\"action\":\"cancel\"}"; nomsg "office: cancelling a numbered letter needs a reason" "علت ابطال"
as "$OSEC_TOKEN" POST $OF/letters/action "{\"id\":$OL1,\"action\":\"cancel\",\"comment\":\"اشتباه در مخاطب\"}"; check "office: a cancelled letter keeps its number in the register" ".success and .data.status == 7 and .data.number == \"OUT-$OPY-OPS-0001\""

# =================================================================== R1: confidential letters, files of letters, numbering (review findings C1, H1-H3, H5, M1, M6, M7)
enc() { jq -rn --arg v "$1" '$v|@uri'; }
# H1/H3: the secretariat registers Confidential and Secret letters but never reads them: register data only (number, date, kind, classification, parties, delivery)
as "$OSEC_TOKEN" GET $OF/letters/$OL2; check "office r1 H1: the secretariat gets only the register data of a confidential letter (no text, no subject, no signature image, no attachments, no referrals)" ".success and .data.metadataOnly == true and .data.bodyHtml == null and (.data.subject|contains(\"داخلی محرمانه\")|not) and .data.number == \"$OPY2/د/OPS/00001\" and .data.classification == 1 and (.data.recipients|length) == 1 and .data.signatureDocumentId == null and .data.signedByName == null and .data.contentHash == null and (.data.attachments|length) == 0 and (.data.referrals|length) == 0 and (.data.approvals|length) == 0"
as "$OSEC_TOKEN" GET "$OF/letters?PageSize=100"; check "office r1 H1: in the list the confidential letter shows its number and parties but a neutral subject" ".success and ([.data.data[]|select(.id == $OL2 and .metadataOnly == true and .number == \"$OPY2/د/OPS/00001\" and (.subject|contains(\"داخلی محرمانه\")|not) and (.senderName|length) > 0 and (.recipientNames|length) > 0)]|length) == 1"
as "$OSEC_TOKEN" GET "$OF/letters?Subject=$(enc داخلی)&PageSize=100"; check "office r1 H3: a subject search by the secretariat does not find the confidential letter" ".success and ([.data.data[]|select(.id == $OL2)]|length) == 0"
as "$OEMP_TOKEN" GET "$OF/letters?Subject=$(enc داخلی)&PageSize=100"; check "office r1 H3: the same search finds it for its writer (the search itself works)" ".success and ([.data.data[]|select(.id == $OL2)]|length) == 1"
as "$OSEC_TOKEN" GET "$OF/letters?Number=$(enc "$OPY2/د/OPS/00001")"; check "office r1 H1: the secretariat still finds the confidential letter by its number" ".success and ([.data.data[]|select(.id == $OL2)]|length) == 1"
as "$OSEC_TOKEN" POST $OF/letters/refer "{\"letterId\":$OL2,\"toPositionIds\":[$OP_CHK],\"instruction\":\"x\",\"action\":2}"; no "office r1 H1: the secretariat cannot refer a confidential letter it may not read"

# M1: a cancelled referral gives no access any more
as "$CEO_TOKEN" POST $OF/letters/refer "{\"letterId\":$OL2,\"fromPositionId\":$OP_CEO,\"toPositionIds\":[$OP_CHK],\"instruction\":\"جهت اطلاع\",\"action\":2}"; check "office r1 M1: the CEO refers the confidential letter to the checker" '.success and (.data.referrals|length) == 1'
ORM=$(val '.data.referrals[0].id')
as "$OCHK_TOKEN" GET $OF/letters/$OL2; check "office r1 M1: the receiver of the referral reads the letter" '.success and .data.classification == 1'
as "$CEO_TOKEN" POST $OF/letters/referral-action "{\"id\":$ORM,\"action\":\"cancel\"}"; check "office r1 M1: the referral is cancelled" '.success'
as "$OCHK_TOKEN" GET $OF/letters/$OL2; no "office r1 M1: after the cancellation the receiver no longer reads the letter"
as "$OCHK_TOKEN" GET "$OF/letters?PageSize=100"; check "office r1 M1: and it is gone from his list" ".success and ([.data.data[]|select(.id == $OL2)]|length) == 0"
ibx "$OCHK_TOKEN" "referral:$ORM"; check "office r1 M1: and from his inbox" '.success and (.data.data|length) == 0'

# C1: the files of a letter. Upload two files; the first is attached to an internal letter, the second to a secret one
printf 'attachment bytes C1\n' > "$OUT/letter-att.txt"; printf 'secret attachment bytes\n' > "$OUT/letter-att2.txt"; printf 'forged bytes\n' > "$OUT/letter-evil.txt"; printf 'control bytes\n' > "$OUT/letter-ctl.txt"
curl -s -o "$B" -X POST "$API/Documents?Description=att&Name=letter-att&Code=OFC" -H "Authorization: Bearer $OEMP_TOKEN" -F "File=@$OUT/letter-att.txt"; OATT=$(grep -oE '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}' "$B" | head -n1)
curl -s -o "$B" -X POST "$API/Documents?Description=att2&Name=letter-att2&Code=OFC" -H "Authorization: Bearer $OEMP_TOKEN" -F "File=@$OUT/letter-att2.txt"; OATT2=$(grep -oE '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}' "$B" | head -n1)
curl -s -o "$B" -X POST "$API/Documents?Description=ctl&Name=letter-ctl&Code=OFC" -H "Authorization: Bearer $OEMP_TOKEN" -F "File=@$OUT/letter-ctl.txt"; OCTL=$(grep -oE '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}' "$B" | head -n1)
[ -n "$OATT" ] && [ -n "$OATT2" ] && [ -n "$OCTL" ] && ok "office r1 C1: three test files uploaded" || bad "office r1 C1: three test files uploaded"
# control: change-file works on an ordinary document (so the refusals below are not caused by the request format)
CODE=$(curl -s -o "$B" -w '%{http_code}' -X PUT "$API/Documents/change-file?documentId=$OCTL" -H "Authorization: Bearer $QC_TOKEN" -F "File=@$OUT/letter-evil.txt"); jq -e '.success == true' "$B" >/dev/null 2>&1 && ok "office r1 C1: control: change-file replaces the bytes of an ordinary document" || bad "office r1 C1: control: change-file replaces the bytes of an ordinary document (code $CODE)"
as "$OEMP_TOKEN" POST $OF/letters "{\"kind\":3,\"subject\":\"نامه داخلی با پیوست\",\"bodyHtml\":\"<p>متن</p>\",\"senderPositionId\":$OP_EMP,\"signerPositionId\":$OP_MGR,\"recipients\":[{\"type\":1,\"positionId\":$OP_CHK}],\"attachments\":[{\"documentId\":\"$OATT\",\"fileName\":\"letter-att.txt\"}]}"; check "office r1 C1: internal draft with an attachment saved" '.success and (.data.attachments|length) == 1'
OL3=$(val .data.id)
as "$OEMP_TOKEN" POST $OF/letters/action "{\"id\":$OL3,\"action\":\"submit\"}"; check "office r1 C1: submitted" '.success and .data.status == 1'
as "$OMGR_TOKEN" POST $OF/letters/action "{\"id\":$OL3,\"action\":\"sign\"}"; check "office r1 H5: the second internal letter of the unit gets the next number of the general rule" ".success and .data.status == 4 and .data.number == \"$OPY2/د/OPS/00002\""
CODE=$(curl -s -o /dev/null -w '%{http_code}' "$API/Documents?DocumentId=$OATT"); [ "$CODE" = "401" ] && ok "office r1 C1: an anonymous download of a letter file is refused (401)" || bad "office r1 C1: an anonymous download of a letter file is refused (401) got $CODE"
CODE=$(curl -s -o /dev/null -w '%{http_code}' "$API/Documents/get-file-info?fileId=$OATT"); [ "$CODE" = "401" ] && ok "office r1 C1: anonymous file information is refused (401)" || bad "office r1 C1: anonymous file information is refused (401) got $CODE"
CODE=$(curl -s -o "$B" -w '%{http_code}' -X PUT "$API/Documents/change-file?documentId=$OATT" -H "Authorization: Bearer $QC_TOKEN" -F "File=@$OUT/letter-evil.txt"); nomsg "office r1 C1: a signed letter's attachment cannot be replaced by anybody (even a signed-in stranger)" "تغییر یا حذف آن مجاز نیست"
CODE=$(curl -s -o "$B" -w '%{http_code}' -X PUT "$API/Documents/change-file?documentId=$OATT" -H "Authorization: Bearer $OEMP_TOKEN" -F "File=@$OUT/letter-evil.txt"); nomsg "office r1 C1: nor by the writer of the letter" "تغییر یا حذف آن مجاز نیست"
OATTF=$(ls src/WebApi/UploadedDocuments/${OATT}* 2>/dev/null | head -n1)
[ -n "$OATTF" ] && grep -q "attachment bytes C1" "$OATTF" && ok "office r1 C1: the stored bytes of the attachment are unchanged after the attempts" || bad "office r1 C1: the stored bytes of the attachment are unchanged after the attempts (file '$OATTF')"
as "$OCHK_TOKEN" GET $OF/letters/$OL3/attachments/$OATT; { [ "$CODE" = "200" ] && grep -q "attachment bytes C1" "$B"; } && ok "office r1 C1: the recipient position downloads the attachment through the letter" || bad "office r1 C1: the recipient position downloads the attachment through the letter got $CODE"
as "$OEMP_TOKEN" GET $OF/letters/$OL3/attachments/$OATT; [ "$CODE" = "200" ] && ok "office r1 C1: the writer downloads it" || bad "office r1 C1: the writer downloads it got $CODE"
as "$QC_TOKEN" GET $OF/letters/$OL3/attachments/$OATT; [ "$CODE" = "404" ] && ok "office r1 C1: somebody who is not a party gets 404 for the attachment" || bad "office r1 C1: somebody who is not a party gets 404 for the attachment got $CODE"
as "$OCHK_TOKEN" GET "Documents?DocumentId=$OATT"; [ "$CODE" = "404" ] && ok "office r1 C1: the generic download does not serve a letter file even to a party" || bad "office r1 C1: the generic download does not serve a letter file even to a party got $CODE"
as "$OMGR_TOKEN" GET $OF/letters/$OL3/verify; check "office r1 C1: verify: signed, valid, the hashes agree and no file changed" '.success and .data.valid == true and .data.storedHash == .data.computedHash and (.data.changedDocumentIds|length) == 0'
printf 'forged bytes\n' > "$OATTF"
as "$OMGR_TOKEN" GET $OF/letters/$OL3/verify; check "office r1 C1: after the bytes of the attachment are swapped on disk verify fails and names the file" ".success and .data.valid == false and (.data.changedDocumentIds|index(\"$OATT\")) != null"
printf 'attachment bytes C1\n' > "$OATTF"
as "$OMGR_TOKEN" GET $OF/letters/$OL3/verify; check "office r1 C1: with the original bytes back the letter is valid again (the check reads the files, it is not a flag)" '.success and .data.valid == true and (.data.changedDocumentIds|length) == 0'

# H5: numbering rules that can produce the same number are refused; a duplicate number is skipped forward
call POST $OF/numbering-rules "{\"kind\":3,\"unitId\":$OU1,\"pattern\":\"{yy}/{kind}/{unit}/{seq:5}\"}"; nomsg "office r1 H5: an internal rule for the unit with the same shape as the general rule is refused" "می‌تواند شماره‌ای بسازد"
call POST $OF/numbering-rules "{\"kind\":3,\"unitId\":$OU1,\"pattern\":\"INT-{yy}-{seq:3}\"}"; check "office r1 H5: a rule with a literal part is accepted and previews its first number" ".success and .data.preview == \"INT-$OPY2-001\""
ORULE1=$(val .data.id)
oint() { # oint SUBJECT : an internal letter of the unit written by the expert and signed by the manager (OLN = its id)
  as "$OEMP_TOKEN" POST $OF/letters "{\"kind\":3,\"subject\":\"$1\",\"senderPositionId\":$OP_EMP,\"signerPositionId\":$OP_MGR,\"recipients\":[{\"type\":1,\"positionId\":$OP_CHK}]}"; OLN=$(val .data.id)
  as "$OEMP_TOKEN" POST $OF/letters/action "{\"id\":$OLN,\"action\":\"submit\"}"
  as "$OMGR_TOKEN" POST $OF/letters/action "{\"id\":$OLN,\"action\":\"sign\"}"
}
oint "نامه شماره سه"; check "office r1 H5: the next letter uses the rule of the unit" ".success and .data.number == \"INT-$OPY2-001\""
call POST $OF/numbering-rules "{\"id\":$ORULE1,\"kind\":3,\"unitId\":$OU1,\"pattern\":\"INT-{yy}-{seq:3}\",\"isActive\":false}"; check "office r1 H5: the rule is switched off" '.success and .data.isActive == false'
call POST $OF/numbering-rules "{\"kind\":3,\"unitId\":$OU1,\"pattern\":\"INT-{yy}-{seq:3}\"}"; check "office r1 H5: a new rule with the same pattern is accepted once the old one is off (its counter starts again)" '.success and .data.isActive == true'
oint "نامه شماره چهار"; check "office r1 H5: the number INT-001 exists already: the counter skips forward to 002 inside the same signature instead of failing" ".success and .data.status == 4 and .data.number == \"INT-$OPY2-002\""
as "$FM_TOKEN" GET "$FAU?EntityType=OfNumberingRule&Action=Added&PageSize=0"; check "office r1 M7: the two rules with the pattern INT-{yy}-{seq:3} are audited exactly once each, with the user (a save wrote every audit row twice)" '.success and ([.data.data[]|select((.afterJson|tostring|contains("INT-{yy}-{seq:3}")) and (.userName|length) > 0)]|length) == 2'
check "office r1 M7: the three default rules are audited exactly once each" '.success and ([.data.data[]|select(.afterJson|tostring|contains("{yy}/{kind}/{unit}/{seq:5}"))]|length) == 3'

# H2/H1/M6: a Secret outgoing letter: signed, then the secretariat numbers/registers and sends it without ever reading it
as "$OEMP_TOKEN" POST $OF/letters "{\"kind\":2,\"subject\":\"نامه سری آزمون\",\"classification\":2,\"bodyHtml\":\"<p>RAREWORD123</p>\",\"senderPositionId\":$OP_EMP,\"signerPositionId\":$OP_MGR,\"recipients\":[{\"type\":1,\"partyId\":$OPARTY}],\"attachments\":[{\"documentId\":\"$OATT2\",\"fileName\":\"secret.txt\"}]}"; check "office r1 H2: secret outgoing draft saved" '.success and .data.classification == 2'
OL4=$(val .data.id)
as "$OEMP_TOKEN" POST $OF/letters/action "{\"id\":$OL4,\"action\":\"submit\"}"; check "office r1 H2: submitted" '.success and .data.status == 1'
as "$OMGR_TOKEN" POST $OF/letters/action "{\"id\":$OL4,\"action\":\"sign\"}"; check "office r1 H2: signed: numbered, waiting for the secretariat" ".success and .data.status == 2 and .data.number == \"OUT-$OPY-OPS-0002\""
ibx "$OSEC_TOKEN" "letter:$OL4:dispatch"; check "office r1 H2: the dispatch item for the secretariat carries the number and the classification only, never the subject" ".success and (.data.data|length) == 1 and (.data.data[0].title|contains(\"OUT-$OPY-OPS-0002\") and contains(\"سری\") and (contains(\"آزمون\")|not))"
ODISP=$(val '.data.data[0].id')
as "$OSEC_TOKEN" POST $OF/inbox/act "{\"id\":$ODISP,\"action\":\"done\"}"; nomsg "office r1 M6: the dispatch item cannot be closed by hand: it closes when the letter is sent" "با انجام کار مربوطه"
ibx "$OSEC_TOKEN" "letter:$OL4:dispatch"; check "office r1 M6: and it is still waiting" '.success and (.data.data|length) == 1'
as "$OSEC_TOKEN" GET $OF/letters/$OL4; check "office r1 H1: the secretariat sees the secret letter as register data only" '.success and .data.metadataOnly == true and .data.bodyHtml == null and .data.signatureDocumentId == null and (.data.attachments|length) == 0 and (.data.subject|contains("آزمون")|not)'
as "$OSEC_TOKEN" GET $OF/letters/$OL4/attachments/$OATT2; [ "$CODE" = "404" ] && ok "office r1 C1/H1: the secretariat gets 404 for the attachment of a secret letter" || bad "office r1 C1/H1: the secretariat gets 404 for the attachment of a secret letter got $CODE"
as "$OEMP_TOKEN" GET $OF/letters/$OL4/attachments/$OATT2; { [ "$CODE" = "200" ] && grep -q "secret attachment bytes" "$B"; } && ok "office r1 C1: its writer downloads it" || bad "office r1 C1: its writer downloads it got $CODE"
as "$OSEC_TOKEN" GET "$OF/letters?Contains=RAREWORD123&PageSize=100"; check "office r1 H3: a text search by the secretariat does not find the secret letter by a word of its body" '.success and (.data.data|length) == 0'
as "$OEMP_TOKEN" GET "$OF/letters?Contains=RAREWORD123&PageSize=100"; check "office r1 H3: the same search finds it for its writer" ".success and (.data.data|length) == 1 and .data.data[0].id == $OL4"
as "$OSEC_TOKEN" GET "$OF/letters?Subject=$(enc سری)&PageSize=100"; check "office r1 H3: a subject search by the secretariat finds nothing either" '.success and (.data.data|length) == 0'
as "$QC_TOKEN" GET $OF/letters/$OL4; no "office r1 H1: a stranger does not see the secret letter"
as "$OSEC_TOKEN" POST $OF/letters/action "{\"id\":$OL4,\"action\":\"register\"}"; check "office r1 H2: the secretariat registers the secret letter without reading it" '.success and .data.status == 3 and .data.metadataOnly == true and .data.bodyHtml == null'
as "$OSEC_TOKEN" POST $OF/letters/action "{\"id\":$OL4,\"action\":\"send\",\"deliveryMethod\":2}"; check "office r1 H2: and sends it by post" '.success and .data.status == 4 and .data.deliveryMethod == 2 and .data.bodyHtml == null'
ibx "$OSEC_TOKEN" "letter:$OL4:dispatch"; check "office r1 H2: the dispatch item is closed by the sending" '.success and (.data.data|length) == 0'
as "$CEO_TOKEN" GET $OF/letters/$OL4; check "office r1 H1: the CEO reads the secret letter in full" '.success and .data.metadataOnly == false and (.data.bodyHtml|contains("RAREWORD123"))'

# --- meeting -> minutes -> decisions in the owners' inboxes -> overdue alert
OMEET="{\"title\":\"جلسه هماهنگی آزمون\",\"kind\":1,\"startAt\":\"2026-07-01T10:00:00\",\"place\":\"اتاق جلسات\",\"chairPositionId\":$OP_MGR,\"secretaryPositionId\":$OP_EMP,\"agenda\":[{\"seq\":1,\"title\":\"بررسی پیشرفت\",\"minutes\":\"گزارش ارائه شد\"},{\"seq\":2,\"title\":\"موارد متفرقه\",\"minutes\":\"موردی نبود\"}],\"attendees\":[{\"positionId\":$OP_EMP,\"present\":true},{\"positionId\":$OP_CHK,\"present\":true},{\"externalName\":\"مهمان بیرونی\",\"present\":true}],\"decisions\":[{\"text\":\"تحویل گزارش نهایی\",\"ownerPositionId\":$OP_EMP,\"dueDate\":\"2026-07-10T00:00:00\",\"agendaSeq\":1},{\"text\":\"بازبینی نقشه‌ها\",\"ownerPositionId\":$OP_CHK,\"dueDate\":\"2035-01-01T00:00:00\",\"agendaSeq\":2}]}"
as "$OMGR_TOKEN" POST $OF/meetings "$OMEET"; check "office: meeting planned with agenda, attendees and two decisions" '.success and .data.status == 0 and (.data.number|test("^MT-[0-9]{4}-0*1$")) and (.data.agenda|length) == 2 and (.data.attendees|length) == 3 and (.data.decisions|length) == 2 and (.data.startAtText|test("^14[0-9]{2}/[0-9]{2}/[0-9]{2}"))'
OM1=$(val .data.id); OD1=$(val '.data.decisions[0].id'); OD2=$(val '.data.decisions[1].id')
as "$QC_TOKEN" GET $OF/meetings/$OM1; no "office: a meeting is invisible to a non-participant"
as "$OMGR_TOKEN" POST $OF/meetings/action "{\"id\":$OM1,\"action\":\"approve-minutes\"}"; nomsg "office: minutes cannot be approved before they are submitted" "در انتظار تایید نیست"
as "$OMGR_TOKEN" POST $OF/meetings/action "{\"id\":$OM1,\"action\":\"hold\"}"; check "office: meeting held" '.success and .data.status == 1'
as "$OMGR_TOKEN" POST $OF/meetings/action "{\"id\":$OM1,\"action\":\"submit-minutes\"}"; check "office: minutes submitted to the chair" '.success and .data.status == 2'
ibx "$OMGR_TOKEN" "meeting:$OM1:minutes"; check "office: the chair has the minutes to approve" '.success and (.data.data|length) == 1 and .data.data[0].kind == 3'
ibx "$OEMP_TOKEN" "decision:$OD1"; check "office: decisions are not in the owner inbox before the minutes are approved" '.success and (.data.data|length) == 0'
as "$OEMP_TOKEN" POST $OF/meetings/action "{\"id\":$OM1,\"action\":\"approve-minutes\"}"; nomsg "office: only the chair approves the minutes" "رئیس جلسه"
as "$OMGR_TOKEN" POST $OF/meetings/action "{\"id\":$OM1,\"action\":\"approve-minutes\"}"; check "office: minutes approved by the chair" '.success and .data.status == 3 and (.data.minutesApprovedByName|length) > 0'
ibx "$OEMP_TOKEN" "decision:$OD1"; check "office: decision 1 is in the inbox of its owner position with its due date" '.success and (.data.data|length) == 1 and .data.data[0].kind == 6 and (.data.data[0].dueDateText|test("^14[0-9]{2}/[0-9]{2}/[0-9]{2}$"))'
ibx "$OCHK_TOKEN" "decision:$OD2"; check "office: decision 2 is in the inbox of its owner position" '.success and (.data.data|length) == 1 and .data.data[0].kind == 6'
ibx "$OMGR_TOKEN" "meeting:$OM1:minutes"; check "office: the minutes item of the chair is closed" '.success and (.data.data|length) == 0'
as "$OMGR_TOKEN" POST $OF/meetings "$(echo "$OMEET" | jq -c --argjson i "$OM1" '. + {id:$i, title:"تغییر"}')"; nomsg "office: approved minutes cannot be edited" "قابل ویرایش نیست"
as "$OEMP_TOKEN" POST $OF/meetings/action "{\"id\":$OM1,\"action\":\"sign-minutes\"}"; check "office: an attendee signs the minutes electronically" '.success'
call POST $OF/decisions/check-overdue; check "office: overdue check finds exactly the one decision past its due date and alerts once" '.success and .data.overdueDecisions == 1 and .data.alertsCreated == 1'
call POST $OF/decisions/check-overdue; check "office: running the check again creates no second alert" '.success and .data.overdueDecisions == 1 and .data.alertsCreated == 0'
ibx "$OMGR_TOKEN" "decision-overdue:$OD1"; check "office: the chair is alerted about the overdue decision" '.success and (.data.data|length) == 1 and .data.data[0].kind == 5 and .data.data[0].priority == 1'
as "$OMGR_TOKEN" GET "$OF/decisions?OverdueOnly=true"; check "office: the overdue list holds the decision with the overdue flag" ".success and (.data.data|length) == 1 and .data.data[0].id == $OD1 and .data.data[0].overdue == true"
as "$CEO_TOKEN" GET finance/ceo-dashboard; check "office: the CEO dashboard counts the overdue decision and raises an alert" '.success and .data.overdueDecisions == 1 and (.data.alerts|tostring|contains("از موعد گذشته"))'
as "$OEMP_TOKEN" GET "$OF/decisions?MeetingId=$OM1"; check "office: the owner of decision 1 may mark it done and not cancel it" ".success and ([.data.data[]|select(.id == $OD1)][0].allowedActions == [\"done\"])"
as "$OMGR_TOKEN" GET "$OF/decisions?MeetingId=$OM1"; check "office: the chair (creator) may cancel decision 1 but not mark it done" ".success and ([.data.data[]|select(.id == $OD1)][0].allowedActions == [\"cancel\"])"
as "$OMGR_TOKEN" POST $OF/decisions/action "{\"id\":$OD1,\"action\":\"done\",\"note\":\"x\"}"; nomsg "office: only the owner position marks a decision done" "فقط مسئول مصوبه"
as "$OEMP_TOKEN" POST $OF/decisions/action "{\"id\":$OD1,\"action\":\"done\"}"; no "office: a decision is not done without a note"
as "$OEMP_TOKEN" POST $OF/decisions/action "{\"id\":$OD1,\"action\":\"done\",\"note\":\"گزارش نهایی تحویل شد\"}"; check "office: the owner marks the decision done" '.success and .data.status == 1'
ibx "$OEMP_TOKEN" "decision:$OD1" "IncludeClosed=true"; check "office: the decision item of the owner is closed" '.success and (.data.data|length) == 1 and .data.data[0].status == 2'
ibx "$OMGR_TOKEN" "decision-overdue:$OD1"; check "office: and the overdue alert of the chair is closed" '.success and (.data.data|length) == 0'
call POST $OF/decisions/check-overdue; check "office: no decision is overdue any more" '.success and .data.overdueDecisions == 0 and .data.alertsCreated == 0'
as "$CEO_TOKEN" GET finance/ceo-dashboard; check "office: the CEO dashboard shows no overdue decision" '.success and .data.overdueDecisions == 0'

# --- the inbox is used by the main gates of the other modules: a purchase request waiting for planning approval
as "$WH_TOKEN" POST procurement/requests "{\"source\":2,\"kind\":1,\"requestDate\":\"2026-10-22T00:00:00\",\"description\":\"خرید آزمون صندوق کار\",\"submit\":true,\"lines\":[{\"itemId\":$BOLT,\"quantity\":1,\"estimatedUnitPrice\":9000000,\"technicalRequirements\":\"آزمون\"}]}"; check "office gate: purchase request submitted" '.success and .data.status == 1'
OPR=$(val .data.id)
ibx "$PLAN_TOKEN" "pr:$OPR"; check "office gate: the planning role finds the request approval in its inbox" '.success and (.data.data|length) == 1 and .data.data[0].status < 2 and .data.data[0].sourceModule == "procurement" and .data.data[0].recipientRole == "PLANNING"'
ibx "$WH_TOKEN" "pr:$OPR"; check "office gate: the requester does not get the approval item" '.success and (.data.data|length) == 0'
as "$PLAN_TOKEN" POST procurement/requests/action "{\"id\":$OPR,\"action\":\"approve-planning\"}"; check "office gate: planning approves the request" '.success and .data.status == 2'
ibx "$PLAN_TOKEN" "pr:$OPR"; check "office gate: the inbox item is closed when the gate is passed" '.success and (.data.data|length) == 0'
ibx "$PLAN_TOKEN" "pr:$OPR" "IncludeClosed=true"; check "office gate: and stays in the history as done" '.success and (.data.data|length) == 1 and .data.data[0].status == 2 and (.data.data[0].closedReason|length) > 0'

# --- general requests: three seed types, approval by the unit manager, completion by administration
as "$OEMP_TOKEN" GET $OF/request-types; check "office: three seed request types without company data" '.success and (.data|length) == 3 and ([.data[].title]|index("درخواست خودرو")) != null and ([.data[].title]|index("درخواست پشتیبانی فناوری اطلاعات")) != null and ([.data[].title]|index("درخواست آموزش")) != null'
OVT=$(jq -r '.data[]|select(.title == "درخواست خودرو")|.id' "$B")
as "$OEMP_TOKEN" POST $OF/requests "{\"typeId\":$OVT,\"values\":{\"destination\":\"سایت پروژه\"},\"submit\":true}"; nomsg "office: required fields are checked at submit" "را پر کنید"
as "$OEMP_TOKEN" POST $OF/requests "{\"typeId\":$OVT,\"values\":{\"destination\":\"سایت پروژه\",\"date\":\"1405/08/10\",\"purpose\":\"بازدید\",\"passengers\":\"۳\"},\"submit\":true}"; check "office: vehicle request submitted: waits for the manager of the unit; the date value comes back as the Persian text 1405/08/10" ".success and .data.status == 1 and (.data.number|test(\"^RQ-[0-9]{4}-0*1\$\")) and (.data.approvals|length) == 1 and .data.approvals[0].positionId == $OP_MGR and .data.values.passengers == \"3\" and .data.values.date == \"1405/08/10\""
ORQ=$(val .data.id)
as "$OEMP_TOKEN" POST $OF/requests/action "{\"id\":$ORQ,\"action\":\"approve\"}"; no "office: the requester cannot approve his own request"
as "$OMGR_TOKEN" POST $OF/requests/action "{\"id\":$ORQ,\"action\":\"approve\"}"; check "office: the unit manager approves" '.success and .data.status == 2'
as "$OHR_TOKEN" POST $OF/requests/action "{\"id\":$ORQ,\"action\":\"complete\",\"comment\":\"خودرو تخصیص یافت\"}"; check "office: administration completes the request" '.success and .data.status == 5'

# --- attendance: shift, assignment, policy, device
as "$OEMP_TOKEN" POST $OFA/shifts '{"title":"x","start":"08:00","end":"16:00","workDays":[6]}'; no "office: an ordinary user cannot define shifts"
as "$OHR_TOKEN" POST $OFA/shifts '{"title":"شیفت اداری","start":"08:00","end":"16:00","breakMinutes":30,"nightStart":"22:00","nightEnd":"06:00","workDays":[6,0,1,2,3]}'; check "office: office shift 08:00-16:00 with a 30 minute break (450 scheduled minutes)" '.success and .data.scheduledMinutes == 450 and .data.crossesMidnight == false'
OSH=$(val .data.id)
as "$OHR_TOKEN" POST $OFA/shift-assignments "{\"employeeId\":$OAEMP,\"shiftId\":$OSH,\"fromDate\":\"2026-06-01T00:00:00\"}"; check "office: shift assigned to the employee" '.success and .data.shiftId == '$OSH
as "$OHR_TOKEN" GET $OFA/policy; check "office: the attendance policy starts with grace 0 and no other seeded value" '.success and .data.graceMinutes == 0 and .data.maxOvertimeMinutesPerDay == null and .data.roundingMinutes == 0'
as "$OEMP_TOKEN" POST $OFA/policy '{"graceMinutes":5}'; no "office: an ordinary user cannot change the policy"
as "$OHR_TOKEN" POST $OFA/policy '{"graceMinutes":5,"maxOvertimeMinutesPerDay":120,"overtimeNeedsPreApproval":false,"roundingMinutes":15}'; check "office: policy: grace 5, overtime up to 120 minutes without pre-approval, rounding 15" '.success and .data.graceMinutes == 5 and .data.maxOvertimeMinutesPerDay == 120 and .data.overtimeNeedsPreApproval == false and .data.roundingMinutes == 15'

# --- leave requests and the balance (Tir 1405 = 2026-06-22 .. 2026-07-22)
as "$OEMP_TOKEN" GET "$OF/leaves/balance?employeeId=$OAEMP&year=1405&month=4"; check "office: the leave balance of Tir is the projected entitlement of four months" '.success and .data.entitlementRate > 0 and .data.pendingDays == 0 and .data.available == (.data.entitlementRate * 4)'
OAV0=$(val .data.available)
as "$OEMP_TOKEN" POST $OF/leaves '{"kind":1,"fromDate":"2026-06-27T00:00:00","toDate":"2026-07-22T00:00:00","reason":"خیلی طولانی"}'; check "office: a long leave is saved as a draft" '.success and .data.status == 0 and .data.days == 20'
OLX=$(val .data.id)
as "$OEMP_TOKEN" POST $OF/leaves/action "{\"id\":$OLX,\"action\":\"submit\"}"; nomsg "office: a leave above the balance cannot be submitted" "مانده مرخصی کافی نیست"
as "$OEMP_TOKEN" POST $OF/leaves/action "{\"id\":$OLX,\"action\":\"cancel\"}"; check "office: the draft is cancelled" '.success and .data.status == 5'
as "$OEMP_TOKEN" POST $OF/leaves '{"kind":3,"fromDate":"2026-07-14T00:00:00","reason":"بیماری","submit":true}'; no "office: sick leave without an attachment cannot be submitted"
as "$OEMP_TOKEN" POST $OF/leaves '{"kind":1,"fromDate":"2026-07-07T00:00:00","toDate":"2026-07-08T00:00:00","reason":"مرخصی استحقاقی","submit":true}'; check "office: daily leave of two working days submitted to the manager of the unit; the refused sick leave before it took no number (LV 1 was the cancelled draft, this is 2)" ".success and (.data.number|test(\"^LV-[0-9]{4}-0*2\$\")) and .data.status == 1 and .data.days == 2 and .data.approverPositionId == $OP_MGR and (.data.fromDateText|test(\"^14[0-9]{2}/[0-9]{2}/[0-9]{2}\$\"))"
OLV1=$(val .data.id)
as "$OEMP_TOKEN" POST $OF/leaves '{"kind":1,"fromDate":"2026-07-08T00:00:00","reason":"هم‌پوشان","submit":true}'; no "office: an overlapping leave is refused"
ibx "$OMGR_TOKEN" "leave:$OLV1"; check "office: the manager has the leave to approve" '.success and (.data.data|length) == 1'
as "$OEMP_TOKEN" POST $OF/leaves/action "{\"id\":$OLV1,\"action\":\"approve\"}"; no "office: the employee cannot approve his own leave"
as "$OHR_TOKEN" POST $OF/leaves/action "{\"id\":$OLV1,\"action\":\"record\"}"; no "office: HR cannot record a leave that is not approved"
as "$OMGR_TOKEN" POST $OF/leaves/action "{\"id\":$OLV1,\"action\":\"approve\"}"; check "office: the manager approves" '.success and .data.status == 2 and (.data.approvedByName|length) > 0'
OAV1=$(jq -n "$OAV0 - 2")
as "$OEMP_TOKEN" GET "$OF/leaves/balance?employeeId=$OAEMP&year=1405&month=4"; check "office: the approved leave reduced the balance by exactly 2 days" ".success and .data.pendingDays == 2 and .data.available == $OAV1"
ibx "$OHR_TOKEN" "leave-record:$OLV1"; check "office: HR has the leave to record" '.success and (.data.data|length) == 1'
as "$OHR_TOKEN" POST $OF/leaves/action "{\"id\":$OLV1,\"action\":\"record\"}"; check "office: HR records the leave" '.success and .data.status == 3'
as "$OEMP_TOKEN" GET "$OF/leaves/balance?employeeId=$OAEMP&year=1405&month=4"; check "office: recording does not count the days a second time" ".success and .data.pendingDays == 2 and .data.available == $OAV1"
as "$OEMP_TOKEN" POST $OF/leaves '{"kind":2,"fromDate":"2026-07-05T00:00:00","fromTime":"14:00","toTime":"16:00","reason":"کار شخصی","submit":true}'; check "office: hourly leave of two hours is a quarter of the 8 hour day" '.success and .data.status == 1 and .data.days == 0.25'
OLV2=$(val .data.id)
as "$OMGR_TOKEN" POST $OF/leaves/action "{\"id\":$OLV2,\"action\":\"approve\"}"; check "office: hourly leave approved" '.success and .data.status == 2'
as "$OHR_TOKEN" POST $OF/leaves/action "{\"id\":$OLV2,\"action\":\"record\"}"; check "office: hourly leave recorded" '.success and .data.status == 3'
OAV2=$(jq -n "$OAV0 - 2.25")
as "$OEMP_TOKEN" GET "$OF/leaves/balance?employeeId=$OAEMP&year=1405&month=4"; check "office: the balance fell by exactly 2.25 days" ".success and .data.pendingDays == 2.25 and .data.available == $OAV2"
as "$OEMP_TOKEN" POST $OF/leaves '{"kind":5,"fromDate":"2026-07-13T00:00:00","reason":"بازدید کارخانه مشتری","submit":true}'; check "office: a mission is submitted" '.success and .data.status == 1'
OLV3=$(val .data.id)
as "$OMGR_TOKEN" POST $OF/leaves/action "{\"id\":$OLV3,\"action\":\"approve\"}"; check "office: mission approved" '.success and .data.status == 2'
as "$OHR_TOKEN" POST $OF/leaves/action "{\"id\":$OLV3,\"action\":\"record\"}"; check "office: mission recorded" '.success and .data.status == 3'
as "$OEMP_TOKEN" GET "$OF/leaves/balance?employeeId=$OAEMP&year=1405&month=4"; check "office: a mission does not use the leave balance" ".success and .data.available == $OAV2"
as "$OEMP_TOKEN" POST $OF/leaves '{"kind":1,"fromDate":"2026-09-01T00:00:00","reason":"ماه قفل","submit":true}'; nomsg "office: no leave is accepted in a month whose payroll is approved (Shahrivar 1405)" "حقوق این ماه تایید شده"
as "$OHR_TOKEN" POST $OFA/clock/manual "{\"employeeId\":$OAEMP,\"timestamp\":\"2026-09-01T08:00:00\",\"direction\":1,\"reason\":\"ثبت دستی\"}"; nomsg "office: no clock event is accepted in a month whose payroll is approved" "حقوق این ماه تایید شده"

# --- clock import (CSV-like rows, a Persian date, a duplicate and an unknown card) -> exact daily computation
OROWS='[
 {"code":"ATT01","date":"2026-06-27","time":"08:10","device":"GATE-1"},{"code":"ATT01","date":"2026-06-27","time":"17:00","device":"GATE-1"},
 {"code":"ATT01","date":"2026-06-28","time":"07:58","device":"GATE-1"},{"code":"ATT01","date":"2026-06-28","time":"16:04","device":"GATE-1"},{"code":"ATT01","date":"2026-06-28","time":"07:58","device":"GATE-1"},
 {"code":"ATT01","date":"2026-06-29","time":"08:03","device":"GATE-1"},{"code":"ATT01","date":"2026-06-29","time":"15:50","device":"GATE-1"},
 {"code":"ATT01","date":"2026-06-30","time":"08:00","device":"GATE-1"},{"code":"ATT01","date":"2026-06-30","time":"23:30","device":"GATE-1"},
 {"code":"ATT01","date":"2026-07-01","time":"08:00","device":"GATE-1"},
 {"code":"ATT01","date":"2026-07-02","time":"09:00","device":"GATE-1"},{"code":"ATT01","date":"2026-07-02","time":"13:00","device":"GATE-1"},
 {"code":"ATT01","date":"2026-07-05","time":"08:00","device":"GATE-1"},{"code":"ATT01","date":"2026-07-05","time":"14:00","device":"GATE-1"},
 {"code":"ATT01","date":"1405/05/20","time":"08:00","device":"GATE-1"},
 {"code":"ZZ999","date":"2026-06-28","time":"08:00","device":"GATE-1"}]'
as "$OEMP_TOKEN" POST $OFA/clock/import "{\"rows\":$OROWS}"; no "office: an ordinary user cannot import clock events"
as "$OHR_TOKEN" POST $OFA/clock/import "{\"deviceCode\":\"GATE-1\",\"rows\":$OROWS}"; check "office: clock import: 14 events stored, 1 duplicate minute ignored, the unknown personnel code reported" '.success and .data.imported == 14 and .data.duplicates == 1 and (.data.errors|length) == 1'
as "$OHR_TOKEN" GET "$OFA/clock?EmployeeId=$OAEMP&PageSize=100"; check "office: the Persian-dated row 1405/05/20 landed exactly on 2026-08-11 08:00 and all events are of source Import" '.success and (.data.data|length) == 14 and ([.data.data[]|select(.timestamp|test("^2026-08-11T08:00"))]|length) == 1 and ([.data.data[]|select(.source != 2)]|length) == 0'
as "$OHR_TOKEN" POST $OFA/clock/manual "{\"employeeId\":$OAEMP,\"timestamp\":\"2026-07-03T08:00:00\",\"direction\":1}"; no "office: a manual clock event needs a reason"
as "$OEMP_TOKEN" POST $OFA/clock/manual "{\"employeeId\":$OAEMP,\"timestamp\":\"2026-07-03T08:00:00\",\"direction\":1,\"reason\":\"x\"}"; no "office: manual clock events are only for HR"
as "$OHR_TOKEN" POST $OFA/compute "{\"fromDate\":\"2026-06-22T00:00:00\",\"toDate\":\"2026-07-22T00:00:00\",\"employeeId\":$OAEMP}"; check "office: the month is computed for the one employee with a shift" '.success and .data.employees == 1 and .data.days >= 23'
oday() { as "$OHR_TOKEN" GET "$OFA/days?EmployeeId=$OAEMP&From=$1&To=$1"; }
oday 2026-06-27; check "office: Sat 06-27 in 08:10 out 17:00: worked 500, delay 10, overtime 60, night 0" '.success and (.data.data|length) == 1 and (.data.data[0]|.status == 1 and .workedMinutes == 500 and .delayMinutes == 10 and .earlyLeaveMinutes == 0 and .overtimeMinutes == 60 and .nightMinutes == 0 and .holidayMinutes == 0) and (.data.data[0].dateText|test("^14[0-9]{2}/[0-9]{2}/[0-9]{2}$"))'
oday 2026-06-28; check "office: Sun 06-28 in 07:58 out 16:04: no delay, 4 minutes of overtime rounded down to 0, worked 456" '.success and (.data.data[0]|.status == 1 and .workedMinutes == 456 and .delayMinutes == 0 and .earlyLeaveMinutes == 0 and .overtimeMinutes == 0)'
oday 2026-06-29; check "office: Mon 06-29 in 08:03 (inside the grace) out 15:50: no delay, early leave 10, worked 437" '.success and (.data.data[0]|.status == 1 and .workedMinutes == 437 and .delayMinutes == 0 and .earlyLeaveMinutes == 10 and .overtimeMinutes == 0)'
oday 2026-06-30; check "office: Tue 06-30 in 08:00 out 23:30: overtime capped at 120, night 90, worked 900" '.success and (.data.data[0]|.status == 1 and .workedMinutes == 900 and .overtimeMinutes == 120 and .nightMinutes == 90 and .delayMinutes == 0)'
oday 2026-07-01; check "office: Wed 07-01 with a single clock event is incomplete" '.success and .data.data[0].status == 6'
oday 2026-07-02; check "office: Thu 07-02 (not a working day of the shift) 09:00-13:00 is holiday work of 210 minutes" '.success and (.data.data[0]|.status == 1 and .nonWorkingDay == true and .workedMinutes == 210 and .holidayMinutes == 210 and .overtimeMinutes == 0 and .delayMinutes == 0)'
oday 2026-07-05; check "office: Sun 07-05 hourly leave 14:00-16:00 excuses the early leave and counts 0.25 paid leave day" '.success and (.data.data[0]|.status == 1 and .workedMinutes == 330 and .earlyLeaveMinutes == 0 and .paidLeaveDays == 0.25)'
oday 2026-07-07; check "office: Tue 07-07 is an approved leave day" '.success and (.data.data[0]|.status == 3 and .paidLeaveDays == 1)'
oday 2026-07-13; check "office: Mon 07-13 is a mission day" '.success and (.data.data[0]|.status == 4 and .missionDays == 1)'
oday 2026-06-22; check "office: Mon 06-22 without any clock event is an absence" '.success and (.data.data[0]|.status == 2 and .absenceDays == 1)'
as "$PLAN_TOKEN" GET "$OFA/days?EmployeeId=$OAEMP&From=2026-06-27&To=2026-06-27"; check "office: an ordinary user does not see the days of somebody else" '(.success == false) or ((.data.data|length) == 0)'
as "$OEMP_TOKEN" GET "$OFA/days?From=2026-06-27&To=2026-06-27"; check "office: the employee sees his own day" '.success and (.data.data|length) == 1 and .data.data[0].workedMinutes == 500'

# --- device push with an API key (hashed at rest)
as "$OEMP_TOKEN" POST $OFA/devices '{"code":"x","title":"x"}'; no "office: an ordinary user cannot register devices"
as "$OHR_TOKEN" POST $OFA/devices '{"code":"gate-1","title":"درب اصلی","location":"ورودی"}'; check "office: device registered; the 48-hex key is shown once" '.success and .data.device.code == "GATE-1" and (.data.apiKey|test("^[0-9a-f]{48}$"))'
ODKEY=$(val .data.apiKey)
as "$OHR_TOKEN" GET $OFA/devices; if jq -e --arg k "$ODKEY" '.success and ((tostring|contains($k))|not)' "$B" >/dev/null 2>&1; then ok "office: the device key is never shown again"; else bad "office: the device key is never shown again"; fi
opush() { CODE=$(curl -s -o "$B" -w '%{http_code}' -X POST "$API/$OFA/push" -H "X-Device-Code: $1" -H "X-Device-Key: $2" -H 'Content-Type: application/json' --data-binary "$3"); }
OPUSH='{"events":[{"employeeCode":"ATT01","timestamp":"2026-08-02T08:00:00","direction":1},{"employeeCode":"ATT01","timestamp":"2026-08-02T16:00:00","direction":2}]}'
opush GATE-1 0000000000000000000000000000000000000000000000000 "$OPUSH"; [ "$CODE" = "401" ] && jq -e '.success == false' "$B" >/dev/null 2>&1 && ok "office: a push with a wrong key is rejected (401)" || bad "office: a push with a wrong key is rejected (401) got $CODE"
opush NOPE "$ODKEY" "$OPUSH"; [ "$CODE" = "401" ] && ok "office: a push for an unknown device is rejected (401)" || bad "office: a push for an unknown device is rejected (401) got $CODE"
opush GATE-1 "" "$OPUSH"; [ "$CODE" = "401" ] && ok "office: a push without a key is rejected (401)" || bad "office: a push without a key is rejected (401) got $CODE"
as "$OHR_TOKEN" GET "$OFA/clock?EmployeeId=$OAEMP&PageSize=100"; check "office: the rejected pushes stored nothing" '.success and (.data.data|length) == 14'
as "$OHR_TOKEN" GET $OFA/devices; { echo "=== devices before the push"; head -c 1500 "$B"; echo; } >> "$F"
opush GATE-1 "$ODKEY" "$(echo "$OPUSH" | jq -c '.events += [{"employeeCode":"NOPE9","timestamp":"2026-08-02T08:00:00","direction":1}]')"; [ "$CODE" = "200" ] && jq -e '.success and .data.accepted == 2 and .data.rejected == 1' "$B" >/dev/null 2>&1 && ok "office: a push with the right key stores 2 events and rejects the unknown code" || bad "office: a push with the right key stores 2 events and rejects the unknown code got $CODE"
opush GATE-1 "$ODKEY" "$OPUSH"; [ "$CODE" = "200" ] && jq -e '.success and .data.accepted == 0 and .data.duplicates == 2' "$B" >/dev/null 2>&1 && ok "office: pushing the same events again stores nothing (duplicates)" || bad "office: pushing the same events again stores nothing (duplicates) got $CODE"
as "$OHR_TOKEN" GET "$OFA/clock?EmployeeId=$OAEMP&Source=1&PageSize=100"; check "office: the pushed events are of source Device" '.success and (.data.data|length) == 2 and ([.data.data[]|select(.deviceCode == "GATE-1")]|length) == 2'

# --- monthly close writes the payroll attendance sheet with exact values
as "$OEMP_TOKEN" POST $OFA/close-month '{"year":1405,"month":4}'; no "office: only HR closes the month"
as "$OHR_TOKEN" POST $OFA/close-month '{"year":1450,"month":1}'; nomsg "office: a month that is not finished cannot be closed" "هنوز تمام نشده"
as "$OHR_TOKEN" POST $OFA/close-month '{"year":1405,"month":4}'; nomsg "office: the close is refused while a day has a single clock event" "فقط یک تردد"
as "$OHR_TOKEN" POST $OFA/close-month '{"year":1405,"month":4,"acceptIncompleteAsAbsent":true}'
check "office: month closed: one sheet with days worked 4.75, overtime 3, night 1.5, holiday 3.5, paid leave 2.25, absence 15, mission 1" '.success and .data.sheets == 1 and .data.incompleteDays == 1 and (.data.rows[0]|.daysWorked == 4.75 and .overtimeHours == 3 and .nightHours == 1.5 and .holidayHours == 3.5 and .paidLeaveDays == 2.25 and .unpaidLeaveDays == 0 and .absenceDays == 15 and .missionDays == 1)'
as "$PAYR_TOKEN" GET "$FP/attendance?Year=1405&Month=4"; check "office: the payroll attendance sheet (FnAttendanceSheet) holds exactly these values" ".success and ([.data.data[]|select(.employeeId == $OAEMP)]|length) == 1 and ([.data.data[]|select(.employeeId == $OAEMP)][0]|.daysWorked == 4.75 and .overtimeHours == 3 and .nightHours == 1.5 and .holidayHours == 3.5 and .paidLeaveDays == 2.25 and .unpaidLeaveDays == 0 and .absenceDays == 15 and .missionDays == 1)"
as "$OHR_TOKEN" GET $OFA/month/1405/4; check "office: the month is recorded as closed with one sheet" '.success and .data.closed == true and .data.sheets == 1 and .data.lockedByPayroll == false'
check "office: the closed month gives back its saved sheet for reprint (exactly the closed values)" ".success and (.data.rows|length) == 1 and (.data.rows[0]|.employeeId == $OAEMP and .daysWorked == 4.75 and .overtimeHours == 3 and .nightHours == 1.5 and .holidayHours == 3.5 and .paidLeaveDays == 2.25 and .absenceDays == 15 and .missionDays == 1)"
as "$OHR_TOKEN" GET $OFA/month/1405/3; check "office: a month that is not closed has no sheet rows" '.success and .data.closed == false and (.data.rows|length) == 0'
as "$OHR_TOKEN" POST $OFA/close-month '{"year":1405,"month":6,"acceptIncompleteAsAbsent":true}'; check "office: Shahrivar 1405 (payroll approved) is not recomputed and writes no sheet for the employee" '.success and .data.sheets == 0'
as "$PAYR_TOKEN" GET "$FP/attendance?Year=1405&Month=6"; check "office: the payroll sheet of Shahrivar is untouched" ".success and ([.data.data[]|select(.employeeId == $OAEMP)]|length) == 0"
as "$OHR_TOKEN" POST $OFA/worked-hours "{\"employeeIds\":[$OAEMP],\"from\":\"2026-06-27T00:00:00\",\"to\":\"2026-06-27T00:00:00\"}"; check "office: worked hours for the production time-booking check (500 minutes)" '.success and (.data|length) == 1 and .data[0].workedMinutes == 500'
# =================================================================== R1: attendance, leave, payroll, gates, audit (review findings H6, H7, H8, M2, M3, M4, M5, M7)
# M4: events in the future or in a month whose payroll is approved are refused by the import and by the device push (counted as rejected, nothing stored)
ORJ=$(jq -nc --arg f "$(dd 2)" '{deviceCode:"GATE-1",rows:[{code:"ATT01",date:"2026-09-01",time:"08:00"},{code:"ATT01",date:$f,time:"08:00"}]}')
as "$OHR_TOKEN" POST $OFA/clock/import "$ORJ"; check "office r1 M4: import: an event in a locked month (Shahrivar) and one in the future are both rejected and reported" '.success and .data.imported == 0 and .data.rejected == 2 and (.data.errors|length) == 2'
opush GATE-1 "$ODKEY" "$(jq -nc --arg f "$(dd 2)" '{events:[{employeeCode:"ATT01",timestamp:"2026-09-01T08:00:00",direction:1},{employeeCode:"ATT01",timestamp:($f+"T08:00:00"),direction:1}]}')"; [ "$CODE" = "200" ] && jq -e '.success and .data.accepted == 0 and .data.rejected == 2' "$B" >/dev/null 2>&1 && ok "office r1 M4: device push: the same two events are rejected" || bad "office r1 M4: device push: the same two events are rejected got $CODE"
as "$OHR_TOKEN" GET "$OFA/clock?EmployeeId=$OAEMP&PageSize=100"; check "office r1 M4: nothing was stored (14 imported + 2 pushed events only)" '.success and (.data.data|length) == 16'
as "$OHR_TOKEN" POST $OFA/clock/manual "{\"employeeId\":$OAEMP,\"timestamp\":\"$(dd 2)T08:00:00\",\"direction\":1,\"reason\":\"آینده\"}"; nomsg "office r1 M4: a manual event in the future is refused" "در آینده"

# H6: no pre-approval and no daily maximum means no limit (the old code gave 0). Tue 06-30 in 08:00 out 23:30: all 450 minutes after the shift end are overtime
as "$OHR_TOKEN" POST $OFA/policy '{"graceMinutes":5,"maxOvertimeMinutesPerDay":null,"overtimeNeedsPreApproval":false,"roundingMinutes":15}'; check "office r1 H6: policy without pre-approval and without a daily maximum saved" '.success and .data.maxOvertimeMinutesPerDay == null and .data.overtimeNeedsPreApproval == false'
as "$OHR_TOKEN" POST $OFA/compute "{\"fromDate\":\"2026-06-30T00:00:00\",\"toDate\":\"2026-06-30T00:00:00\",\"employeeId\":$OAEMP}"; check "office r1 H6: day recomputed" '.success'
oday 2026-06-30; check "office r1 H6: with no limit the overtime of 06-30 is 450 minutes (the old code gave 0)" '.success and (.data.data[0]|.workedMinutes == 900 and .overtimeMinutes == 450 and .nightMinutes == 90)'

# H7: a shift that ends after midnight keeps its late punches on the day it started. ATT02 afternoon shift 14:00-22:00, ATT03 night shift 22:00-06:00, on Tue 2026-08-04
as "$PAYR_TOKEN" POST $FP/employees "$(EJ ATT02 حضور دو 1234567008 IR270170000000100324200001 | jq -c --argjson u "$OCHKID" '. + {userId:$u}')"; check "office r1 H7: payroll employee ATT02 linked to the checker user" ".success and .data.id > 0 and .data.userId == $OCHKID"
OAEMP2=$(val .data.id)
as "$PAYR_TOKEN" POST $FP/employees "$(EJ ATT03 حضور سه 1234567016 IR270170000000100324200001)"; check "office r1 H7: payroll employee ATT03 saved" '.success and .data.id > 0'
OAEMP3=$(val .data.id)
as "$OHR_TOKEN" POST $OFA/shifts '{"title":"شیفت عصر","start":"14:00","end":"22:00","breakMinutes":30,"nightStart":"22:00","nightEnd":"06:00","workDays":[6,0,1,2,3]}'; check "office r1 H7: afternoon shift 14:00-22:00 saved" '.success and .data.scheduledMinutes == 450'
OSH2=$(val .data.id)
as "$OHR_TOKEN" POST $OFA/shifts '{"title":"شیفت شب","start":"22:00","end":"06:00","breakMinutes":30,"nightStart":"22:00","nightEnd":"06:00","workDays":[6,0,1,2,3]}'; check "office r1 H7: night shift 22:00-06:00 saved and crosses midnight" '.success and .data.crossesMidnight == true and .data.scheduledMinutes == 450'
OSH3=$(val .data.id)
as "$OHR_TOKEN" POST $OFA/shift-assignments "{\"employeeId\":$OAEMP2,\"shiftId\":$OSH2,\"fromDate\":\"2026-08-01T00:00:00\"}"; check "office r1 H7: afternoon shift assigned" '.success'
as "$OHR_TOKEN" POST $OFA/shift-assignments "{\"employeeId\":$OAEMP3,\"shiftId\":$OSH3,\"fromDate\":\"2026-08-01T00:00:00\"}"; check "office r1 H7: night shift assigned" '.success'
OR7='{"deviceCode":"GATE-1","rows":[{"code":"ATT02","date":"2026-08-04","time":"13:55"},{"code":"ATT02","date":"2026-08-05","time":"00:30"},{"code":"ATT03","date":"2026-08-04","time":"22:00"},{"code":"ATT03","date":"2026-08-05","time":"10:00"}]}'
as "$OHR_TOKEN" POST $OFA/clock/import "$OR7"; check "office r1 H7: four events imported" '.success and .data.imported == 4 and .data.rejected == 0'
as "$OHR_TOKEN" POST $OFA/compute '{"fromDate":"2026-08-04T00:00:00","toDate":"2026-08-05T00:00:00"}'; check "office r1 H7: the two days are computed" '.success'
odayE() { as "$OHR_TOKEN" GET "$OFA/days?EmployeeId=$1&From=$2&To=$2"; }
odayE $OAEMP2 2026-08-04; check "office r1 H7: afternoon shift: in 13:55 out 00:30 next day is one present day of 605 minutes (635 minus the 30 minute break), last out on the next calendar day" '.success and (.data.data[0]|.status == 1 and .workedMinutes == 605 and (.firstIn|test("^2026-08-04T13:55")) and (.lastOut|test("^2026-08-05T00:30")))'
odayE $OAEMP2 2026-08-05; check "office r1 H7: the next day has no punch of its own (00:30 belongs to the day before): an absence, not an incomplete day" '.success and (.data.data[0]|.status == 2 and .absenceDays == 1 and .firstIn == null)'
odayE $OAEMP3 2026-08-04; check "office r1 H7: night shift: in 22:00 out 10:00 next day is one present day of 690 minutes with 240 minutes of overtime after the 06:00 end" '.success and (.data.data[0]|.status == 1 and .workedMinutes == 690 and .overtimeMinutes == 240 and (.lastOut|test("^2026-08-05T10:00")))'

# M7: a manual clock event is audited with its reason, the Persian time and the user
as "$OHR_TOKEN" POST $OFA/clock/manual "{\"employeeId\":$OAEMP,\"timestamp\":\"2026-08-06T08:00:00\",\"direction\":1,\"reason\":\"ثبت دستی R1\"}"; check "office r1 M7: manual clock event saved" '.success'
as "$FM_TOKEN" GET "$FAU?EntityType=OfClockEvent&Action=Added&PageSize=0"; check "office r1 M7: the manual event is in the audit trail with the reason, the Persian time and the user (and no Gregorian date)" '.success and ([.data.data[]|select(((.afterJson|fromjson).Reason == "ثبت دستی R1") and ((.afterJson|fromjson).Timestamp|test("^14[0-9]{2}/[0-9]{2}/[0-9]{2} 08:00")) and ((.afterJson|tostring|test("20[0-9]{2}-[0-9]{2}-[0-9]{2}"))|not) and (.userName|length) > 0)]|length) >= 1'

# H8: sick leave is paid by insurance and never uses the annual leave: ATT02 (the checker user) is ill on Sun 08-09 and Mon 08-10
as "$OCHK_TOKEN" POST $OF/leaves "{\"kind\":3,\"fromDate\":\"2026-08-09T00:00:00\",\"toDate\":\"2026-08-10T00:00:00\",\"reason\":\"بیماری\",\"attachmentDocumentId\":\"$DOCID\",\"submit\":true}"; check "office r1 H8: sick leave of two days submitted" '.success and .data.status == 1 and .data.days == 2'
OSK=$(val .data.id)
as "$OMGR_TOKEN" POST $OF/leaves/action "{\"id\":$OSK,\"action\":\"approve\"}"; check "office r1 H8: approved by the manager" '.success and .data.status == 2'
as "$OHR_TOKEN" POST $OF/leaves/action "{\"id\":$OSK,\"action\":\"record\"}"; check "office r1 H8: recorded" '.success and .data.status == 3'
as "$OCHK_TOKEN" GET "$OF/leaves/balance?employeeId=$OAEMP2&year=1405&month=5"; check "office r1 H8: the sick leave does not reduce the annual leave balance" '.success and .data.pendingDays == 0 and .data.available == (.data.carriedForward + .data.entitled - .data.used + .data.projectedEntitlement)'
odayE $OAEMP2 2026-08-09; check "office r1 H8: the day is a sick leave day (not paid leave)" '.success and (.data.data[0]|.status == 3 and .sickLeaveDays == 1 and .paidLeaveDays == 0)'
as "$OHR_TOKEN" POST $OFA/close-month '{"year":1405,"month":5,"acceptIncompleteAsAbsent":true}'; check "office r1 H8: Mordad closed: ATT02 has 2 sick leave days and no paid leave; ATT03 has 4 hours of overtime" ".success and ([.data.rows[]|select(.employeeId == $OAEMP2 and .sickLeaveDays == 2 and .paidLeaveDays == 0)]|length) == 1 and ([.data.rows[]|select(.employeeId == $OAEMP3 and .overtimeHours == 4)]|length) == 1"
as "$PAYR_TOKEN" GET "$FP/attendance?Year=1405&Month=5"; check "office r1 H8: the payroll attendance sheet of ATT02 carries the sick leave days apart from the paid leave" ".success and ([.data.data[]|select(.employeeId == $OAEMP2 and .sickLeaveDays == 2 and .paidLeaveDays == 0)]|length) == 1"
as "$FM_TOKEN" GET "$FAU?EntityType=FnAttendanceSheet&PageSize=0"; check "office r1 M7: the sheets written by the month close are in the audit trail with the user" '.success and ([.data.data[]|select((.userName|length) > 0)]|length) >= 2'
as "$FM_TOKEN" GET "$FAU?EntityType=OfAttendancePolicy&PageSize=0"; check "office r1 M7: the policy change is in the audit trail with the old value and the user" '.success and ([.data.data[]|select(.action == "Modified" and (.beforeJson|tostring|contains("120")) and (.userName|length) > 0)]|length) >= 1'
as "$OHR_TOKEN" POST $OFA/policy '{"graceMinutes":5,"maxOvertimeMinutesPerDay":120,"overtimeNeedsPreApproval":false,"roundingMinutes":15}'; check "office r1 H6: the original policy is restored" '.success and .data.maxOvertimeMinutesPerDay == 120'
as "$OHR_TOKEN" POST $OFA/compute "{\"fromDate\":\"2026-06-30T00:00:00\",\"toDate\":\"2026-06-30T00:00:00\",\"employeeId\":$OAEMP}"; check "office r1 H6: day recomputed under the restored policy" '.success'
oday 2026-06-30; check "office r1 H6: and the overtime of 06-30 is capped at 120 again" '.success and .data.data[0].overtimeMinutes == 120'

# M3: a leave that spans a month whose payroll is approved cannot be approved or rejected (submitted when only its first and last month were open)
as "$OEMP_TOKEN" POST $OF/leaves '{"kind":5,"fromDate":"2026-08-20T00:00:00","toDate":"2026-09-25T00:00:00","reason":"ماموریت بلندمدت","submit":true}'; check "office r1 M3: a long mission over the locked Shahrivar is submitted (its first and last month are open)" '.success and .data.status == 1'
OLM=$(val .data.id)
as "$OMGR_TOKEN" POST $OF/leaves/action "{\"id\":$OLM,\"action\":\"approve\"}"; nomsg "office r1 M3: it cannot be approved: it touches Shahrivar whose payroll is approved" "حقوق این ماه تایید شده"
as "$OMGR_TOKEN" POST $OF/leaves/action "{\"id\":$OLM,\"action\":\"reject\",\"comment\":\"آزمون\"}"; nomsg "office r1 M3: nor rejected" "حقوق این ماه تایید شده"
as "$OEMP_TOKEN" POST $OF/leaves/action "{\"id\":$OLM,\"action\":\"cancel\"}"; check "office r1 M3: the employee withdraws it" '.success and .data.status == 5'

# M2: a leave that crosses the last month booked by payroll stays pending only for the days after it. Leave Tue 07-21 .. Sun 07-26 (working days 21, 22, 25, 26), approved before the Tir payroll is approved
as "$OEMP_TOKEN" POST $OF/leaves '{"kind":1,"fromDate":"2026-07-21T00:00:00","toDate":"2026-07-26T00:00:00","reason":"عبور از مرز ماه","submit":true}'; check "office r1 M2: leave over the end of Tir submitted: 4 working days" '.success and .data.status == 1 and .data.days == 4'
OLK=$(val .data.id)
as "$OMGR_TOKEN" POST $OF/leaves/action "{\"id\":$OLK,\"action\":\"approve\"}"; check "office r1 M2: approved" '.success and .data.status == 2'
as "$PAYR_TOKEN" POST $FP/runs '{"year":1405,"month":4,"note":"آزمون تیر"}'; check "office r1 M2: payroll run of Tir 1405 created" '.success and .data.status == 0'
PRT=$(val .data.id)
as "$PAYR_TOKEN" POST $FP/runs/$PRT/calculate; check "office r1 M2: calculated" '.success and .data.run.status == 1'
as "$FM_TOKEN" POST $FP/runs/$PRT/approve; check "office r1 M2: approved by the finance manager" '.success and .data.status == 2'
as "$OEMP_TOKEN" GET "$OF/leaves/balance?employeeId=$OAEMP&year=1405&month=5"; check "office r1 M2: payroll booked Tir (2.25 paid leave days used); of the leave over the month end only the 2 days after Tir are still pending" '.success and .data.lastMonth == 4 and .data.used == 2.25 and .data.pendingDays == 2'

# M5b: the approval item of a payroll run leaves the inbox when a rate change sends the run back to be calculated again
as "$PAYR_TOKEN" POST $FP/runs '{"year":1405,"month":5,"note":"آزمون مرداد"}'; check "office r1 M5: payroll run of Mordad 1405 created" '.success and .data.status == 0'
PRM=$(val .data.id)
as "$PAYR_TOKEN" POST $FP/runs/$PRM/calculate; check "office r1 M5: Mordad calculated (it holds ATT01, ATT02 and ATT03)" '.success and .data.run.status == 1'
ibx "$FM_TOKEN" "payroll:$PRM"; check "office r1 M5: the finance manager has the approval item of the calculated run" '.success and (.data.data|length) == 1'
as "$PAYR_TOKEN" POST $FP/rates "{\"year\":1405,\"key\":\"SeniorityDaily\",\"value\":10001,\"source\":\"$SRC\",\"verify\":true}"; check "office r1 M5: a rate changed after the calculation" '.success'
as "$PAYR_TOKEN" GET $FP/runs/$PRM; check "office r1 M5: the run went back to draft" '.success and .data.status == 0'
ibx "$FM_TOKEN" "payroll:$PRM"; check "office r1 M5: and its approval item left the inbox in the same save (the old item stayed open)" '.success and (.data.data|length) == 0'
as "$PAYR_TOKEN" POST $FP/runs/$PRM/calculate; check "office r1 M5: calculated again" '.success and .data.run.status == 1'
ibx "$FM_TOKEN" "payroll:$PRM"; check "office r1 M5: the approval item is back" '.success and (.data.data|length) == 1'
as "$FM_TOKEN" POST $FP/runs/$PRM/approve; check "office r1 M5: Mordad approved" '.success and .data.status == 2'
ibx "$FM_TOKEN" "payroll:$PRM"; check "office r1 M5: and the item is closed" '.success and (.data.data|length) == 0'
as "$OHR_TOKEN" GET "$OF/leaves/balance?employeeId=$OAEMP2&year=1405&month=6"; check "office r1 H8: after payroll booked Mordad the sick leave of ATT02 used none of the annual leave" '.success and .data.lastMonth == 5 and .data.used == 0'

# M5a: posting accounting events (the posting job) raises the voucher gate for the finance manager
# the gate counts every checked voucher of every year, so the ledger is cleared first: both finance managers finalise what each may (maker-checker),
# manual vouchers that still cannot be finalised go back to draft; the bodies are kept in smoke-output for diagnosis
as "$FM_TOKEN" POST $FG/vouchers/finalize-up-to "{\"upTo\":\"$(dd 400)T00:00:00\"}"; cp "$B" "$OUT/m5-finalise-fm.json"; check "office r1 M5: the finance manager finalises the checked vouchers of every year he may" '.success'
as "$FM2_TOKEN" POST $FG/vouchers/finalize-up-to "{\"upTo\":\"$(dd 400)T00:00:00\"}"; cp "$B" "$OUT/m5-finalise-fm2.json"; check "office r1 M5: the second finance manager finalises those the first one touched" '.success'
as "$FM_TOKEN" GET "$FG/vouchers?Status=1&PageSize=0"; cp "$B" "$OUT/m5-checked-left.json"
for OV in $(jq -r '.data.data[]|select(.type != 3)|.id' "$B"); do as "$FIN2_TOKEN" POST $FG/vouchers/action "{\"id\":$OV,\"action\":\"uncheck\"}"; done
as "$FM_TOKEN" GET "$FG/vouchers?Status=1&PageSize=0"; check "office r1 M5: no checked voucher is left in the ledger (precondition of the gate check)" '.success and (.data.data|length) == 0'
as "$FM_TOKEN" POST $FG/vouchers/finalize-up-to "{\"upTo\":\"$(dd 400)T00:00:00\"}"; check "office r1 M5: a bulk finalise with nothing left finalises nothing" '.success and .data.finalized == 0 and (.data.skipped|length) == 0'
ibx "$FM_TOKEN" "vouchers:finalise"; check "office r1 M5: so the voucher gate is closed" '.success and (.data.data|length) == 0'
as "$PAYR_TOKEN" POST $FP/runs/$PRM/post; check "office r1 M5: the Mordad payroll is posted as an accounting event" '.success and .data.status == 3'
as "$FM_TOKEN" POST $FG/post-events; check "office r1 M5: the posting job turned the event into a voucher" '.success and .data.posted >= 1'
ibx "$FM_TOKEN" "vouchers:finalise"; check "office r1 M5: and the finance manager has the vouchers waiting to be finalised (the posting job now raises the gate)" '.success and (.data.data|length) == 1'

# M5c/M5d: a major NCR waits for the committee roles in the inbox; its CAPA waits for the quality assurance role to name an owner
ncr "{\"id\":$R3NCR,\"severity\":2,\"disposition\":1,\"source\":1}"; check "office r1 M5: the auto NCR is classified major (its CAPA is drafted)" '.success and .data.severity == 2 and .data.capaId != null'
R3CAPA=$(val .data.capaId)
ibx "$QC_TOKEN" "ncr:$R3NCR"; check "office r1 M5: the quality manager has the NCR decision in the inbox" '.success and (.data.data|length) == 1'
ibx "$ENG_TOKEN" "ncr:$R3NCR"; check "office r1 M5: and engineering" '.success and (.data.data|length) == 1'
ibx "$PROD_TOKEN" "ncr:$R3NCR"; check "office r1 M5: and manufacturing" '.success and (.data.data|length) == 1'
ibx "$WH_TOKEN" "ncr:$R3NCR"; check "office r1 M5: but not the warehouse" '.success and (.data.data|length) == 0'
ibx "$QA_TOKEN" "capa-assign:$R3CAPA"; check "office r1 M5: the drafted CAPA waits in the inbox of the quality assurance role for an owner" '.success and (.data.data|length) == 1'
capa_cycle $R3CAPA $QAID; check "office r1 M5: the CAPA gets its owner" '.success and .data.ownerUserId > 0'
ibx "$QA_TOKEN" "capa-assign:$R3CAPA"; check "office r1 M5: and the assignment item is closed" '.success and (.data.data|length) == 0'

# the inbox counts equal what the list shows, and reading an item moves exactly one unread to read
as "$OEMP_TOKEN" GET "$OF/inbox?PageSize=100"; OIN_OPEN=$(jq -r '.data.data|length' "$B"); OIN_UNREAD=$(jq -r '[.data.data[]|select(.status == 0)]|length' "$B"); OIN_ONE=$(jq -r '[.data.data[]|select(.status == 0)][0].id' "$B")
as "$OEMP_TOKEN" GET $OF/inbox/counts; check "office r1: the inbox counts equal the open and unread items of the list (and the list is not empty)" ".success and .data.open == $OIN_OPEN and .data.unread == $OIN_UNREAD and $OIN_OPEN > 0 and $OIN_OPEN < 100 and $OIN_UNREAD > 0"
as "$OEMP_TOKEN" POST $OF/inbox/act "{\"id\":$OIN_ONE,\"action\":\"read\"}"; check "office r1: one unread item is read" '.success and .data.status == 1'
as "$OEMP_TOKEN" GET $OF/inbox/counts; check "office r1: the unread count fell by exactly one and the open count did not change" ".success and .data.open == $OIN_OPEN and .data.unread == $((OIN_UNREAD - 1))"
TOKEN=$ADMIN_TOKEN

# =================================================================== PRODUCTION / SHOP FLOOR (MES), release P1 part A
# self-contained: its own users, items, work centres, routings and a legacy chain (contract, work order, assembly, three BOM lines with a make process each)
TOKEN=$ADMIN_TOKEN
MF=production
mfuser() { # mfuser NAME ROLEID PASSWORD N
  call POST Accounts/register-user "{\"userName\":\"$1\",\"name\":\"$1\",\"lastName\":\"آزمون\",\"email\":\"$1@example.com\",\"roleIds\":[$2],\"password\":\"$3\",\"phoneNumber\":\"091200000$4\",\"birthDate\":\"1990-01-01T00:00:00+03:30\",\"nationalCode\":\"00123456$4\"}"
}
mfuser mf.pm.ci 20 'Ci#Mfpm-User-2026' 51
mfuser mf.sv.ci 21 'Ci#Mfsv-User-2026' 52
mfuser mf.op.ci 22 'Ci#Mfop-User-2026' 53
mfuser mf.mt.ci 23 'Ci#Mfmt-User-2026' 54
MPM_TOKEN=$(login mf.pm.ci 'Ci#Mfpm-User-2026'); MSV_TOKEN=$(login mf.sv.ci 'Ci#Mfsv-User-2026')
MOP_TOKEN=$(login mf.op.ci 'Ci#Mfop-User-2026'); MMT_TOKEN=$(login mf.mt.ci 'Ci#Mfmt-User-2026')
[ -n "$MPM_TOKEN" ] && [ -n "$MSV_TOKEN" ] && [ -n "$MOP_TOKEN" ] && [ -n "$MMT_TOKEN" ] && ok "mes: login production manager (20), shop supervisor (21), operator (22), maintenance (23)" || bad "mes: login production manager (20), shop supervisor (21), operator (22), maintenance (23)"
MOPID=$(uid "$MOP_TOKEN")
as "$PAYR_TOKEN" POST $FP/employees "$(EJ MF01 اپراتور آزمون 0084575948 IR270170000000100324200001 | jq -c --argjson u "$MOPID" '. + {userId:$u}')"; check "mes: personnel record of the operator is linked to the operator user" ".success and .data.id > 0 and .data.userId == $MOPID"
MFEMP=$(val .data.id)

# --- work centres, rates (finance only), machines, reason codes
call POST $MF/work-centers '{"code":"MF-LATHE","title":"تراشکاری","kind":1}'; check "mes: work centre saved with the defaults (1 shift, 8 h, efficiency 100, utilisation 85) and no invented rate" '.success and .data.id > 0 and .data.shiftsPerDay == 1 and .data.hoursPerShift == 8 and .data.efficiencyPercent == 100 and .data.utilisationPercent == 85 and .data.machineRateRial == null and .data.labourRateRial == null'
MFWC1=$(val .data.id)
call POST $MF/work-centers '{"code":"MF-QCB","title":"میز بازرسی","kind":2}'; check "mes: second work centre saved" '.success and .data.id > 0 and .data.kind == 2'
MFWC2=$(val .data.id)
call POST $MF/work-centers '{"code":"MF-LATHE","title":"تکراری","kind":1}'; nomsg "mes: a duplicate work centre code is refused" "قبلاً ثبت شده"
call POST $MF/work-centers '{"code":"MF-BAD","title":"شیفت نامعتبر","kind":1,"shiftsPerDay":4}'; nomsg "mes: more than three shifts a day is refused" "بین ۱ تا ۳"
as "$MOP_TOKEN" POST $MF/work-centers '{"code":"MF-OP","title":"x","kind":1}'; nomsg "mes: the operator cannot write master data" "مجوز"
call POST $MF/work-centers/rates "{\"id\":$MFWC1,\"machineRateRial\":1000,\"labourRateRial\":500}"; nomsg "mes: the hourly rates are set by finance only (not by the PMO)" "مالی"
as "$FM_TOKEN" POST $MF/work-centers/rates "{\"id\":$MFWC1,\"machineRateRial\":1000,\"labourRateRial\":500}"; check "mes: finance sets the hourly rates" '.success and .data.machineRateRial == 1000 and .data.labourRateRial == 500'
as "$MSV_TOKEN" GET "$MF/work-centers/$MFWC1"; check "mes: the shop supervisor reads the work centre without the rates" '.success and .data.code == "MF-LATHE" and .data.machineRateRial == null and .data.labourRateRial == null'
as "$MMT_TOKEN" GET "$MF/work-centers?PageSize=1"; check "mes: work centre list is paged" '.success and (.data.data|length) == 1'
as "$FM_TOKEN" GET "$FAU?EntityType=MfWorkCenter&EntityId=$MFWC1&PageSize=0"; check "mes: the rate change is in the audit trail" '.success and ([.data.data[]|select(.action == "Modified")]|length) >= 1'

call POST Machine '{"title":"ماشین تراش آزمون ۱"}'; MFM1=$(val .data)
call POST Machine '{"title":"ماشین تراش آزمون ۲"}'; MFM3=$(val .data)
call POST Machine '{"title":"ماشین بازرسی آزمون"}'; MFM2=$(val .data)
call POST $MF/machines/info "{\"machineId\":$MFM1,\"workCenterId\":$MFWC1,\"maker\":\"سازنده آزمون\",\"model\":\"M1\",\"serialNumber\":\"MF-SN-1\",\"status\":1}"; check "mes: machine 1 belongs to the lathe work centre" '.success and .data.machineId > 0 and .data.status == 1'
call POST $MF/machines/info "{\"machineId\":$MFM3,\"workCenterId\":$MFWC1,\"status\":1}"; check "mes: machine 3 belongs to the lathe work centre" '.success and .data.status == 1'
call POST $MF/machines/info "{\"machineId\":$MFM2,\"workCenterId\":$MFWC2,\"status\":1}"; check "mes: machine 2 belongs to the inspection work centre" '.success and .data.status == 1'
as "$MOP_TOKEN" GET "$MF/machines?WorkCenterId=$MFWC1"; check "mes: the operator lists the machines of a work centre" ".success and ([.data.data[]|select(.machineId == $MFM1 or .machineId == $MFM3)]|length) == 2"
call POST $MF/reason-codes '{"code":"MF-SCRAP-1","title":"ابعاد خارج از تلرانس","kind":1}'; check "mes: scrap reason code saved" '.success and .data.kind == 1 and .data.isActive == true'
MFRSC=$(val .data.id)
call POST $MF/reason-codes '{"code":"MF-DOWN-1","title":"خرابی ماشین","kind":2}'; check "mes: downtime reason code saved" '.success and .data.kind == 2'
MFRDN=$(val .data.id)
call POST $MF/reason-codes '{"code":"MF-SCRAP-1","title":"تکراری","kind":1}'; nomsg "mes: a duplicate reason code is refused" "قبلاً ثبت شده"

# --- items, steps (operation templates) and standards
item MFPA "{\"code\":\"MF.10.00.001.1\",\"title\":\"قطعه ساخت الف\",\"unit\":\"عدد\",\"trackingMode\":1,\"requiresIncomingInspection\":false}" "mes: finished item A (lot)"
item MFPB "{\"code\":\"MF.10.00.002.1\",\"title\":\"قطعه ساخت ب\",\"unit\":\"عدد\",\"trackingMode\":1,\"requiresIncomingInspection\":false}" "mes: finished item B (lot)"
item MFPC "{\"code\":\"MF.10.00.003.1\",\"title\":\"قطعه ساخت ج\",\"unit\":\"عدد\",\"trackingMode\":1,\"requiresIncomingInspection\":false}" "mes: finished item C (lot)"
item MFCMP "{\"code\":\"MF.20.00.001.1\",\"title\":\"ماده مصرفی ج\",\"unit\":\"عدد\",\"trackingMode\":0,\"requiresIncomingInspection\":false}" "mes: component of item C (no stock)"
call POST inventory/trace/templates "{\"itemId\":$MFPA,\"sequence\":10,\"title\":\"تراشکاری\"}"; MFT10=$(val .data.id)
call POST inventory/trace/templates "{\"itemId\":$MFPA,\"sequence\":20,\"title\":\"بازرسی ابعادی\",\"requiresQc\":true}"; MFT20=$(val .data.id)
call POST inventory/trace/templates "{\"itemId\":$MFPA,\"sequence\":30,\"title\":\"عملیات حرارتی بیرونی\"}"; MFT30=$(val .data.id)
call POST inventory/trace/templates "{\"itemId\":$MFPB,\"sequence\":10,\"title\":\"تراشکاری ب\"}"; MFTB=$(val .data.id)
call POST inventory/trace/templates "{\"itemId\":$MFPC,\"sequence\":10,\"title\":\"تراشکاری ج\"}"; MFTC=$(val .data.id)
[ "$MFT10" -gt 0 ] && [ "$MFT20" -gt 0 ] && [ "$MFT30" -gt 0 ] && [ "$MFTB" -gt 0 ] && [ "$MFTC" -gt 0 ] && ok "mes: five routing steps created" || bad "mes: five routing steps created"
call POST $MF/routings "{\"itemId\":$MFPA}"; nomsg "mes: a routing revision needs a standard (work centre, times) for every step" "استاندارد"
as "$WH_TOKEN" POST $MF/operation-standards "{\"operationTemplateId\":$MFT10,\"workCenterId\":$MFWC1,\"setupHours\":1,\"runHoursPerUnit\":0.5}"; nomsg "mes: the warehouse cannot write standards" "مجوز"
call POST $MF/operation-standards "{\"operationTemplateId\":$MFT10,\"setupHours\":1,\"runHoursPerUnit\":0.5}"; nomsg "mes: an inside step needs a work centre" "مرکز کار"
call POST $MF/operation-standards "{\"operationTemplateId\":$MFT10,\"workCenterId\":$MFWC1,\"setupHours\":-1,\"runHoursPerUnit\":0.5}"; nomsg "mes: negative times are refused" "منفی"
call POST $MF/operation-standards "{\"operationTemplateId\":$MFT10,\"workCenterId\":$MFWC1,\"setupHours\":1,\"runHoursPerUnit\":0.5,\"queueDays\":1,\"moveDays\":0.5}"; check "mes: standard of step 10 (setup 1 h, 0.5 h per unit)" '.success and .data.setupHours == 1 and .data.runHoursPerUnit == 0.5 and .data.inspectionRequirement == 0'
call POST $MF/operation-standards "{\"operationTemplateId\":$MFT20,\"workCenterId\":$MFWC2,\"setupHours\":0,\"runHoursPerUnit\":0.1,\"inspectionRequirement\":2}"; check "mes: standard of step 20 asks for QC" '.success and .data.inspectionRequirement == 2'
call POST $MF/operation-standards "{\"operationTemplateId\":$MFT30,\"isOutside\":true,\"defaultSupplierId\":${PRV:-0},\"outsideLeadDays\":7}"; check "mes: standard of step 30 is outside work at a contractor with a 7 day lead time" '.success and .data.isOutside == true and .data.outsideLeadDays == 7 and .data.workCenterId == null'
call POST $MF/operation-standards "{\"operationTemplateId\":$MFTB,\"workCenterId\":$MFWC1,\"setupHours\":0.5,\"runHoursPerUnit\":0.2,\"drawingReferenceRule\":\"ZZ-NO-SUCH-DRAWING\"}"; check "mes: standard of item B needs a drawing that does not exist" '.success and .data.drawingReferenceRule == "ZZ-NO-SUCH-DRAWING"'
call POST $MF/operation-standards "{\"operationTemplateId\":$MFTC,\"workCenterId\":$MFWC1,\"setupHours\":0.5,\"runHoursPerUnit\":0.25}"; check "mes: standard of item C" '.success'
call GET "$MF/operation-standards?ItemId=$MFPA"; check "mes: standards of item A list its three steps in order" '.success and (.data.data|length) == 3 and .data.data[0].sequence == 10 and .data.data[2].isOutside == true'

# --- routing revisions: two-person approval
mfroute() { # mfroute ITEMID LABEL : the PMO opens the revision, the production manager and engineering approve it
  call POST $MF/routings "{\"itemId\":$1}"; check "mes: routing revision of $2 opened as a draft" '.success and .data.revision == 1 and .data.status == 0 and (.data.steps|length) >= 1'
  MFREV=$(val .data.id)
  ibx "$MPM_TOKEN" "mf:routing:$MFREV"; check "mes: the production manager has the revision of $2 in the inbox" '.success and (.data.data|length) == 1'
  call POST $MF/routings/action "{\"id\":$MFREV,\"action\":\"approve-production\"}"; nomsg "mes: the PMO who opened the revision of $2 cannot approve it (production manager only)" "مدیر تولید"
  as "$ENG_TOKEN" POST $MF/routings/action "{\"id\":$MFREV,\"action\":\"approve-production\"}"; nomsg "mes: the production approval of $2 is the production manager's" "مدیر تولید"
  as "$MPM_TOKEN" POST $MF/routings/action "{\"id\":$MFREV,\"action\":\"approve-production\"}"; check "mes: production approves the revision of $2 (still a draft)" '.success and .data.status == 0 and .data.productionApprovedByName != null and .data.engineeringApprovedByName == null'
  as "$MPM_TOKEN" POST $MF/routings/action "{\"id\":$MFREV,\"action\":\"approve-engineering\"}"; nomsg "mes: the production manager cannot give the engineering approval of $2" "مهندسی"
  as "$ENG_TOKEN" POST $MF/routings/action "{\"id\":$MFREV,\"action\":\"approve-engineering\"}"; check "mes: engineering approves the revision of $2: approved, content matches" '.success and .data.status == 1 and .data.contentMatches == true and .data.approvedAtText != null'
  ibx "$MPM_TOKEN" "mf:routing:$MFREV"; check "mes: and the routing gate of $2 is closed" '.success and (.data.data|length) == 0'
}
mfroute "$MFPA" "item A"; MFREVA=$MFREV
call GET "$MF/routings/$MFREVA"; check "mes: routing revision A carries its three steps and the Persian approval date" '.success and (.data.steps|length) == 3 and (.data.approvedAtText|test("^1[34][0-9]{2}/[0-9]{2}/[0-9]{2}"))'
mfroute "$MFPB" "item B"
mfroute "$MFPC" "item C"
as "$MPM_TOKEN" POST $MF/routings "{\"itemId\":$MFPA}"; check "mes: a second revision of item A is revision 2" '.success and .data.revision == 2 and .data.status == 0'
MFREVA2=$(val .data.id)
as "$MPM_TOKEN" POST $MF/routings/action "{\"id\":$MFREVA2,\"action\":\"approve-production\"}"; nomsg "mes: the production manager cannot approve the revision he opened himself (maker-checker)" "تفکیک وظایف"
as "$MPM_TOKEN" POST $MF/routings "{\"itemId\":$MFPA}"; nomsg "mes: only one open draft revision per item" "ابتدا آن را تأیید یا حذف کنید"
as "$MPM_TOKEN" POST $MF/routings/action "{\"id\":$MFREVA2,\"action\":\"delete\"}"; check "mes: the unused draft revision is deleted" '.success'
call GET "$MF/routings?ItemId=$MFPA&Status=1"; check "mes: item A has one approved revision" '.success and (.data.data|length) == 1 and .data.data[0].id == '"$MFREVA"

# --- legacy chain: contract, equipment, work order, assembly, three BOM lines, each with a make process whose exit code is the item
call POST Contracts '{"code":"MF-CT-01","title":"قرارداد آزمون تولید","status":1,"employer":"کارفرمای آزمون"}'; MFCT=$(val .data.id)
call POST Equipments '{"code":"MF-EQ-PUMP","title":"Process Pump API 610 (production test)"}'; MFEQ=$(val .data.id)
call POST ContractEquipments "{\"contractId\":$MFCT,\"equipmentId\":$MFEQ,\"count\":1}"
call POST WorkOrders "{\"contractId\":$MFCT,\"equipmentId\":$MFEQ,\"code\":\"MF-WO-1\",\"title\":\"Pump production\",\"description\":\"x\",\"count\":1}"; check "mes: work order created" '.success and .data.id > 0'
MFWO=$(val .data.id)
as "$PLAN_TOKEN" POST $PC/terms "{\"contractId\":$MFCT,\"currencyCode\":\"IRR\",\"valueExVat\":500000000,\"calendarId\":$PCCAL,\"dataDateWeekday\":3,\"scheduleStartDate\":\"2026-11-02T00:00:00\",\"clientReviewDays\":14,\"autoSnapshot\":false}"; check "mes: contract terms" '.success'
as "$PLAN_TOKEN" POST $PC/work-order-info "{\"workOrderId\":$MFWO,\"contractId\":$MFCT,\"typeCode\":\"PU\",\"sizeCode\":\"150\",\"optionCode\":\"26\"}"; check "mes: equipment project code is built from the type, size and option" '.success and (.data.equipmentProjectCode|startswith("PU15026"))'
call POST Assemblies "{\"title\":\"Pump casing\",\"code\":\"MF-ASM-1\",\"workOrderId\":$MFWO,\"assemblerDuration\":1}"; MFASM=$(val .data.id)
mfbom() { # mfbom POSITION COUNT TITLE : echoes nothing, sets MFBOM
  call POST Boms "{\"positionNumber\":$1,\"title\":\"$3\",\"count\":$2,\"unit\":\"عدد\",\"material\":\"CS\",\"standard\":\"ASTM\",\"diagonal\":\"1\",\"length\":\"1\",\"width\":\"1\",\"height\":\"1\",\"notWetWeight\":1,\"wetWeight\":1,\"assemblyId\":$MFASM}"
  MFBOM=$(val .data.id)
}
mfproc() { # mfproc BOMID EXITCODE ENTERCODE TITLE
  call POST Processes "{\"exitCommodityCode\":\"$2\",\"enterCommodityCode\":\"$3\",\"mapDuration\":1,\"masterDuration\":1,\"processDuration\":1,\"weightPercent\":10,\"type\":5,\"sort\":1,\"programPrice\":0,\"title\":\"$4\",\"bomId\":$1}"
}
mfbom 1 5 "قطعه الف"; mfproc "$MFBOM" "MF.10.00.001.1" "MF-NO-STOCK-CODE" "ساخت قطعه الف"; check "mes: make process of item A (5 units)" '.success and .data.id > 0'
MFPRA=$(val .data.id)
mfbom 2 1 "قطعه ب"; mfproc "$MFBOM" "MF.10.00.002.1" "MF-NO-STOCK-CODE" "ساخت قطعه ب"; check "mes: make process of item B (1 unit)" '.success and .data.id > 0'
mfbom 3 2 "قطعه ج"; mfproc "$MFBOM" "MF.10.00.003.1" "MF.20.00.001.1" "ساخت قطعه ج"; check "mes: make process of item C (2 units, consumes the component)" '.success and .data.id > 0'
MFPRC=$(val .data.id)

# --- the work breakdown generation drafts a production order for every make process (hook), once
as "$PLAN_TOKEN" POST $PC/wbs/generate "{\"contractId\":$MFCT,\"workOrderIds\":[$MFWO]}"; check "mes: WBS generation for the work order succeeds" '.success'
as "$MPM_TOKEN" GET "$MF/orders?WorkOrderId=$MFWO&PageSize=0"; check "mes: the WBS hook drafted three production orders (one per make process, automatically)" '.success and (.data.data|length) == 3 and ([.data.data[]|select(.autoDrafted == true and .status == 0)]|length) == 3'
# a failed hook must not hide the rest of the section: draft by hand then (the check above has already failed)
if [ "$(val '.data.data|length')" != "3" ]; then call POST $MF/orders/draft "{\"workOrderId\":$MFWO}"; as "$MPM_TOKEN" GET "$MF/orders?WorkOrderId=$MFWO&PageSize=0"; fi
MFORDA=$(jq -r ".data.data[]|select(.itemId == $MFPA)|.id" "$B"); MFORDB=$(jq -r ".data.data[]|select(.itemId == $MFPB)|.id" "$B"); MFORDC=$(jq -r ".data.data[]|select(.itemId == $MFPC)|.id" "$B")
check "mes: order A is for the 5 units of the BOM line (a decimal compared as a number, not as text)" ".success and ([.data.data[]|select(.itemId == $MFPA)][0].quantity) == 5"
call POST $MF/orders/draft "{\"processId\":$MFPRA}"; check "mes: drafting again for the same process drafts nothing (idempotent)" '.success and .data.drafted == 0 and .data.skipped == 1 and (.data.notes|length) == 1'
call POST $MF/orders/draft "{\"workOrderId\":$MFWO}"; check "mes: drafting the whole work order again drafts nothing" '.success and .data.drafted == 0 and .data.skipped == 3'
as "$MOP_TOKEN" POST $MF/orders/draft "{\"workOrderId\":$MFWO}"; no "mes: the operator cannot draft orders"
call GET "$MF/orders/$MFORDA"; check "mes: order A has numbers, three operations in sequence, the routing revision and no components" ".success and (.data.number|startswith(\"PO-MF\")) and (.data.operations|length) == 3 and ([.data.operations[].sequence]) == [10,20,30] and .data.routingRevisionId == $MFREVA and (.data.components|length) == 0 and ([.data.operations[].status]|unique) == [0]"
MFNOA=$(val .data.number)
check "mes: operations carry the job card number/sequence and the planned hours from the standard (setup 1, run 0.5 x 5 = 2.5)" ".data.operations[0].jobCard == \"$MFNOA/10\" and .data.operations[0].setupHoursPlanned == 1 and .data.operations[0].runHoursPlanned == 2.5 and .data.operations[1].inspectionRequirement == 2 and .data.operations[2].isOutside == true"
MFOPA10=$(val '.data.operations[0].id'); MFOPA20=$(val '.data.operations[1].id'); MFOPA30=$(val '.data.operations[2].id')
call GET "$MF/orders/$MFORDC"; check "mes: order C consumes 2 of the component" '.success and (.data.components|length) == 1 and .data.components[0].quantity == 2 and (.data.operations|length) == 1'
call GET "$MF/orders/$MFORDB"; check "mes: order B has one operation" '.success and (.data.operations|length) == 1'
ibx "$MSV_TOKEN" "mf:order:$MFORDA"; check "mes: the shop supervisor has the draft order A waiting for release in the inbox" '.success and (.data.data|length) == 1'
as "$SEC_TOKEN" GET "$MF/orders?PageSize=5"; no "mes: the security user cannot read production orders"

# --- release gate: computed checks
call GET "$MF/orders/$MFORDA/release-check"; check "mes: release check of A: routing, work centres, drawings, ITP and materials all pass" '.success and .data.canRelease == true and (.data.checks|length) == 5 and ([.data.checks[]|select(.passed)]|length) == 5 and (.data.shortages|length) == 0'
call GET "$MF/orders/$MFORDB/release-check"; check "mes: release check of B fails on the missing approved drawing and cannot be overridden" '.success and .data.canRelease == false and .data.canReleaseWithOverride == false and ([.data.checks[]|select(.code == "drawings")][0].passed == false)'
call GET "$MF/orders/$MFORDC/release-check"; check "mes: release check of C fails only on the materials (short by 2) and can be overridden by the manager" '.success and .data.canRelease == false and .data.canReleaseWithOverride == true and (.data.shortages|length) == 1 and .data.shortages[0].required == 2 and .data.shortages[0].onHand == 0 and .data.shortages[0].short == 2'
call POST $MF/orders/release "{\"id\":$MFORDA}"; nomsg "mes: the PMO cannot release an order (shop supervisor or production manager only)" "سرپرست کارگاه"
as "$MSV_TOKEN" POST $MF/orders/release "{\"id\":$MFORDB}"; nomsg "mes: order B cannot be released without its approved drawing" "صدور ممکن نیست"
as "$MSV_TOKEN" POST $MF/orders/release "{\"id\":$MFORDC}"; nomsg "mes: a shortage is not released by the shop supervisor" "فقط با مدیر تولید"
as "$MPM_TOKEN" POST $MF/orders/release "{\"id\":$MFORDC}"; nomsg "mes: a shortage needs a written reason even for the production manager" "دلیل را بنویسید"
as "$MPM_TOKEN" POST $MF/orders/components "{\"orderId\":$MFORDC,\"components\":[{\"itemId\":$MFCMP,\"quantity\":2}]}"; check "mes: the production manager edits the components of order C" '.success and (.data.components|length) == 1'
as "$MPM_TOKEN" POST $MF/orders/release "{\"id\":$MFORDC,\"overrideReason\":\"ماده مصرفی در مسیر تأمین است\"}"; nomsg "mes: whoever edited the components cannot release the order (maker-checker)" "تفکیک وظایف"
as "$MSV_TOKEN" POST $MF/orders/release "{\"id\":$MFORDC,\"overrideReason\":\"x\"}"; nomsg "mes: the shop supervisor still cannot release with a shortage" "فقط با مدیر تولید"

# --- cancel of a draft
as "$PLAN_TOKEN" POST $MF/orders/action "{\"id\":$MFORDB,\"action\":\"cancel\"}"; nomsg "mes: cancelling needs a reason" "دلیل لغو"
as "$PLAN_TOKEN" POST $MF/orders/action "{\"id\":$MFORDB,\"action\":\"cancel\",\"reason\":\"نقشه تأییدنشده\"}"; check "mes: order B is cancelled with a reason" '.success and .data.status == 5 and .data.cancelReason == "نقشه تأییدنشده"'
ibx "$MSV_TOKEN" "mf:order:$MFORDB"; check "mes: and its release gate is closed" '.success and (.data.data|length) == 0'

# --- a newer approved routing revision (same content): the draft order must follow it before it is released
call POST $MF/routings "{\"itemId\":$MFPA}"; check "mes: a new revision of item A is opened as a draft" '.success and .data.status == 0'
MFREVA3=$(val .data.id)
as "$MPM_TOKEN" POST $MF/routings/action "{\"id\":$MFREVA3,\"action\":\"approve-production\"}"; check "mes: production approves the new revision of item A" '.success and .data.status == 0'
as "$ENG_TOKEN" POST $MF/routings/action "{\"id\":$MFREVA3,\"action\":\"approve-engineering\"}"; check "mes: engineering approves the new revision of item A: approved, content matches" '.success and .data.status == 1 and .data.contentMatches == true'
call GET "$MF/orders/$MFORDA/release-check"; check "mes: release check of A: only the routing check fails (the operations come from the old revision) and it cannot be overridden" ".success and .data.canRelease == false and .data.canReleaseWithOverride == false and ([.data.checks[]|select(.passed == false)]|length) == 1 and ([.data.checks[]|select(.code == \"routing\")][0] | .passed == false and (.message|contains(\"refresh-routing\")))"
as "$MSV_TOKEN" POST $MF/orders/release "{\"id\":$MFORDA}"; nomsg "mes: order A cannot be released while it follows a superseded routing revision" "به‌روزرسانی مسیر"
as "$PLAN_TOKEN" POST $MF/orders/action "{\"id\":$MFORDA,\"action\":\"refresh-routing\"}"; check "mes: refreshing the routing moves draft order A to the new revision with three fresh operations" ".success and .data.status == 0 and .data.routingRevisionId == $MFREVA3 and (.data.operations|length) == 3 and ([.data.operations[].sequence]) == [10,20,30]"
MFOPA10=$(val '.data.operations[0].id'); MFOPA20=$(val '.data.operations[1].id'); MFOPA30=$(val '.data.operations[2].id')
call GET "$MF/orders/$MFORDA/release-check"; check "mes: after the refresh all five checks of A pass again" '.success and .data.canRelease == true and ([.data.checks[]|select(.passed)]|length) == 5'

# --- release of A
as "$MSV_TOKEN" POST $MF/orders/release "{\"id\":$MFORDA}"; check "mes: the shop supervisor releases order A: first operation ready, the others wait, no kit, no override" '.success and .data.status == 1 and .data.releaseOverride == false and .data.kitRequestId == null and ([.data.operations[].status]) == [1,0,0] and (.data.releasedAtText|test("^1[34][0-9]{2}/[0-9]{2}/[0-9]{2}"))'
ibx "$MSV_TOKEN" "mf:order:$MFORDA"; check "mes: and the release gate of A is closed" '.success and (.data.data|length) == 0'
call GET "inventory/trace/operations?WorkOrderId=$MFWO&PageSize=50"; check "mes: item A is lot tracked: release made one traceability row per operation (3) on the lot named like the order, pending, QC pending only on the inspection step" ".success and (.data.data|length) == 3 and ([.data.data[]|select(.lotNumber == \"$MFNOA\" and .serialNumber == null and .status == 0)]|length) == 3 and ([.data.data|sort_by(.sequence)[]|.sequence]) == [10,20,30] and ([.data.data|sort_by(.sequence)[]|.qcStatus]) == [0,1,0]"
as "$FM_TOKEN" GET "$FAU?EntityType=MfProductionOrder&EntityId=$MFORDA&PageSize=0"; check "mes: the release is in the audit trail" '.success and ([.data.data[]|select(.action == "Modified")]|length) >= 1'
as "$MSV_TOKEN" POST $MF/orders/release "{\"id\":$MFORDA}"; nomsg "mes: an order is released once" "صادر می‌شود"

# --- release of C by the manager with a shortage: kit is a draft material request
as "$MSV_TOKEN" POST $MF/orders/components "{\"orderId\":$MFORDC,\"components\":[{\"itemId\":$MFCMP,\"quantity\":2}]}"; check "mes: the supervisor edits the components (same list) of order C" '.success'
as "$MPM_TOKEN" POST $MF/orders/release "{\"id\":$MFORDC,\"overrideReason\":\"ماده مصرفی در مسیر تأمین است\"}"; check "mes: the production manager releases C with the shortage and a reason: override recorded, kit drafted, operation waits for the kit" '.success and .data.status == 1 and .data.releaseOverride == true and .data.releaseOverrideReason == "ماده مصرفی در مسیر تأمین است" and (.data.shortageText|length) > 0 and .data.kitRequestId > 0 and (.data.kitNumber|startswith("MR")) and .data.kitIssued == false and ([.data.operations[].status]) == [0]'
as "$MSV_TOKEN" POST $MF/orders/action "{\"id\":$MFORDC,\"action\":\"kit-override\"}"; nomsg "mes: starting without the complete kit needs a reason" "دلیل"
as "$MSV_TOKEN" POST $MF/orders/action "{\"id\":$MFORDC,\"action\":\"kit-override\",\"reason\":\"شروع با تأیید سرپرست\"}"; check "mes: the supervisor lets C start without the issued kit (reason kept), the operation is ready" '.success and .data.kitOverrideReason == "شروع با تأیید سرپرست" and ([.data.operations[].status]) == [1]'
MFOPC10=$(val '.data.operations[0].id')

# --- terminal: scan, start, one running booking per person
as "$MOP_TOKEN" GET "$MF/terminal/my-operations?PageSize=0"; check "mes: the operator terminal lists the ready operations of A and C" ".success and ([.data.data[]|select(.id == $MFOPA10 or .id == $MFOPC10)]|length) == 2 and ([.data.data[]|select(.id == $MFOPA20)]|length) == 0"
as "$MOP_TOKEN" GET "$MF/terminal/scan?Code=$MFNOA/10"; check "mes: scanning the job card finds the operation" ".success and (.data.data|length) == 1 and .data.data[0].id == $MFOPA10"
as "$MOP_TOKEN" GET "$MF/terminal/scan?Code=NO-SUCH-CARD"; nomsg "mes: an unknown job card is refused" "پیدا نشد"
as "$MOP_TOKEN" POST $MF/terminal/start "{\"operationId\":$MFOPA20}"; nomsg "mes: step 20 is not ready while step 10 is open" "هنوز آماده نیست"
as "$MOP_TOKEN" POST $MF/terminal/start "{\"operationId\":$MFOPA10,\"machineId\":$MFM2}"; nomsg "mes: a machine of another work centre is refused" "متعلق به مرکز کار"
as "$MOP_TOKEN" POST $MF/terminal/start "{\"operationId\":$MFOPA10,\"machineId\":$MFM1,\"kind\":2}"; check "mes: the operator starts step 10 on machine 1 (in progress, running booking)" '.success and .data.status == 2 and .data.myRunning == true and .data.runningBookings == 1'
as "$MOP_TOKEN" POST $MF/terminal/start "{\"operationId\":$MFOPA10,\"kind\":2}"; nomsg "mes: the same job cannot be started twice" "در حال اجراست"
as "$MOP_TOKEN" POST $MF/terminal/report-quantity "{\"operationId\":$MFOPA10,\"goodQty\":3}"; check "mes: 3 good pieces reported" '.success and .data.goodQty == 3'
as "$MOP_TOKEN" POST $MF/terminal/report-quantity "{\"operationId\":$MFOPA10,\"scrapQty\":1}"; nomsg "mes: scrap needs a reason code" "دلیل ضایعات"
as "$MOP_TOKEN" POST $MF/terminal/report-quantity "{\"operationId\":$MFOPA10,\"goodQty\":3}"; nomsg "mes: good plus scrap cannot exceed the order quantity (3 + 3 > 5)" "از تعداد این عملیات"
as "$MOP_TOKEN" POST $MF/terminal/start "{\"operationId\":$MFOPC10,\"machineId\":$MFM1}"; check "mes: starting a second job stops the running booking of the person: C in progress" '.success and .data.status == 2 and .data.myRunning == true'
as "$MOP_TOKEN" GET "$MF/bookings?EmployeeId=$MFEMP&RunningOnly=true"; check "mes: the person has exactly one running booking" ".success and (.data.data|length) == 1 and .data.data[0].operationId == $MFOPC10 and .data.data[0].source == 1 and .data.data[0].running == true"
call GET "$MF/orders/$MFORDA"; check "mes: step 10 of A went to paused with 3 good pieces booked" '.success and .data.operations[0].status == 3 and .data.operations[0].goodQty == 3 and .data.operations[0].runningBookings == 0'
as "$MOP_TOKEN" POST $MF/terminal/machine-down "{\"machineId\":$MFM3,\"operationId\":$MFOPC10,\"reasonCodeId\":$MFRSC,\"description\":\"صدای غیرعادی یاتاقان\"}"; nomsg "mes: a scrap reason is not a downtime reason" "معتبر نیست"
as "$MOP_TOKEN" POST $MF/terminal/machine-down "{\"machineId\":$MFM3,\"operationId\":$MFOPC10,\"reasonCodeId\":$MFRDN}"; nomsg "mes: a breakdown report needs a description" "شرح خرابی"
as "$MOP_TOKEN" POST $MF/terminal/machine-down "{\"machineId\":$MFM3,\"operationId\":$MFOPC10,\"reasonCodeId\":$MFRDN,\"description\":\"صدای غیرعادی یاتاقان\"}"; check "mes: machine down raises a maintenance request (open) with a number" '.success and (.data.number|startswith("MNT")) and .data.status == 0 and .data.machineId > 0 and (.data.reportedAtText|test("^1[34][0-9]{2}/[0-9]{2}/[0-9]{2}"))'
MFMNT=$(val .data.id)
as "$MMT_TOKEN" GET "$MF/maintenance-requests?MachineId=$MFM3"; check "mes: maintenance sees the request" ".success and (.data.data|length) == 1 and .data.data[0].id == $MFMNT"
ibx "$MMT_TOKEN" "mf:maint:$MFMNT"; check "mes: and has it in the inbox" '.success and (.data.data|length) == 1'
as "$MOP_TOKEN" GET "$MF/machines?WorkCenterId=$MFWC1&Status=2"; check "mes: machine 3 is now down" ".success and (.data.data|length) == 1 and .data.data[0].machineId == $MFM3"
as "$MOP_TOKEN" POST $MF/terminal/start "{\"operationId\":$MFOPA10,\"machineId\":$MFM3,\"kind\":2}"; nomsg "mes: a machine that is down cannot be used" "در دسترس نیست"
as "$MOP_TOKEN" POST $MF/terminal/start "{\"operationId\":$MFOPA10,\"machineId\":$MFM1,\"kind\":2}"; check "mes: back on step 10 of A: it is in progress again and C is paused" '.success and .data.status == 2 and .data.myRunning == true'
as "$MOP_TOKEN" POST $MF/terminal/finish "{\"operationId\":$MFOPA10,\"goodQty\":1}"; nomsg "mes: finishing needs the whole quantity (3 + 1 is not 5)" "برابر نیست"
as "$MOP_TOKEN" POST $MF/terminal/finish "{\"operationId\":$MFOPA10,\"goodQty\":2}"; check "mes: step 10 of A is finished with 5 good pieces" '.success and .data.status == 4 and .data.goodQty == 5 and .data.finishedAtText != null'
as "$MOP_TOKEN" POST $MF/terminal/start "{\"operationId\":$MFOPA10,\"kind\":2}"; nomsg "mes: a finished step cannot be started again and the message names its status in Persian (not the enum name)" "وضعیت: انجام شده"
call GET "$MF/orders/$MFORDA"; check "mes: finishing step 10 made step 20 ready; the order is in progress" '.success and .data.status == 2 and ([.data.operations[].status]) == [4,1,0]'

# --- supervisor bookings for another person, the attendance difference list
call POST $MF/bookings "{\"operationId\":$MFOPC10,\"employeeId\":$OAEMP,\"startAt\":\"2026-06-27T08:00:00\",\"endAt\":\"2026-06-27T12:00:00\",\"reason\":\"ثبت از روی برگه دستی\"}"; nomsg "mes: the PMO cannot book time for somebody else" "مجوز"
as "$MSV_TOKEN" POST $MF/bookings "{\"operationId\":$MFOPC10,\"employeeId\":$OAEMP,\"startAt\":\"2026-06-27T08:00:00\",\"endAt\":\"2026-06-27T12:00:00\"}"; nomsg "mes: booking for somebody else needs a reason" "دلیل"
as "$MSV_TOKEN" POST $MF/bookings "{\"operationId\":$MFOPC10,\"employeeId\":$OAEMP,\"startAt\":\"2026-06-27T12:00:00\",\"endAt\":\"2026-06-27T08:00:00\",\"reason\":\"x\"}"; nomsg "mes: the end must be after the start" "بعد از شروع"
as "$MSV_TOKEN" POST $MF/bookings "{\"operationId\":$MFOPC10,\"employeeId\":$OAEMP,\"startAt\":\"2026-06-27T08:00:00\",\"endAt\":\"2026-06-27T12:00:00\",\"reason\":\"ثبت از روی برگه دستی\"}"; check "mes: the supervisor books 4 hours (240 minutes) for the personnel record with a reason" '.success and .data.minutes == 240 and .data.hours == 4 and .data.source == 2 and .data.supervisorReason == "ثبت از روی برگه دستی" and .data.dayClosed == true and (.data.startAtText|test("^1405/04/06"))'
MFBK=$(val .data.id)
as "$MSV_TOKEN" POST $MF/bookings "{\"operationId\":$MFOPC10,\"employeeId\":$OAEMP,\"startAt\":\"2026-06-27T11:00:00\",\"endAt\":\"2026-06-27T13:00:00\",\"reason\":\"x\"}"; nomsg "mes: two bookings of one person cannot overlap" "همپوشانی"
as "$MPM_TOKEN" POST $MF/bookings/edit "{\"id\":$MFBK,\"endAt\":\"2026-06-27T13:00:00\",\"reason\":\"اصلاح ساعت پایان\"}"; nomsg "mes: the day of a booking is closed: only the shop supervisor corrects it" "روز این ثبت زمان بسته شده است"
as "$MSV_TOKEN" POST $MF/bookings/edit "{\"id\":$MFBK,\"endAt\":\"2026-06-27T13:00:00\"}"; nomsg "mes: a correction needs a reason" "دلیل"
as "$MSV_TOKEN" POST $MF/bookings/edit "{\"id\":$MFBK,\"endAt\":\"2026-06-27T13:00:00\",\"reason\":\"اصلاح ساعت پایان\"}"; check "mes: the shop supervisor corrects the closed day with a reason (300 minutes, 5 hours)" '.success and .data.minutes == 300 and .data.hours == 5 and .data.editReason == "اصلاح ساعت پایان"'
as "$FM_TOKEN" GET "$FAU?EntityType=MfTimeBooking&EntityId=$MFBK&PageSize=0"; check "mes: the first correction is in the audit trail: exactly one Modified row of the booking" '.success and ([.data.data[]|select(.action == "Modified")]|length) == 1'
as "$MSV_TOKEN" GET "$MF/attendance-differences?From=2026-06-27&To=2026-06-27&EmployeeId=$OAEMP"; check "mes: attendance 8.33 h (500 min) against 5 h booked: 3.33 h unbooked" '.success and (.data.data|length) == 1 and .data.data[0].attendanceHours == 8.33 and .data.data[0].bookedHours == 5 and .data.data[0].differenceHours == 3.33 and .data.data[0].kind == "Unbooked" and (.data.data[0].dateText|test("^1405/04/06"))'
as "$MSV_TOKEN" GET "$MF/attendance-differences?From=2026-06-27&To=2026-06-27&EmployeeId=$OAEMP&ToleranceMinutes=300"; check "mes: a wide tolerance hides the difference" '.success and (.data.data|length) == 0'
as "$MOP_TOKEN" GET "$MF/attendance-differences?From=2026-06-27&To=2026-06-27"; no "mes: the operator cannot read the attendance difference list"
as "$MSV_TOKEN" GET "$MF/attendance-differences?From=2026-01-01&To=2026-12-31"; nomsg "mes: the range is at most 93 days" "۹۳ روز"
as "$MSV_TOKEN" POST $MF/bookings/edit "{\"id\":$MFBK,\"endAt\":\"2026-06-27T13:30:00\",\"reason\":\"اصلاح ساعت پایان\"}"; check "mes: a second correction with the very same reason (330 minutes, 5.5 hours)" '.success and .data.minutes == 330 and .data.hours == 5.5 and .data.editReason == "اصلاح ساعت پایان"'
as "$FM_TOKEN" GET "$FAU?EntityType=MfTimeBooking&EntityId=$MFBK&PageSize=0"; check "mes: two corrections with the very same reason give exactly 2 Modified audit rows of the booking" '.success and ([.data.data[]|select(.action == "Modified")]|length) == 2'
as "$MSV_TOKEN" GET "$MF/bookings?OrderId=$MFORDC&PageSize=0"; check "mes: bookings of order C: the operator's terminal booking (ended when A was resumed) and the supervisor's" '.success and (.data.data|length) == 2 and ([.data.data[]|select(.source == 1 and .running == false and .endAt != null)]|length) == 1'

# --- QC hold, decision, rework
as "$MOP_TOKEN" POST $MF/terminal/start "{\"operationId\":$MFOPA20,\"kind\":2}"; check "mes: the operator starts the inspection step 20" '.success and .data.status == 2'
as "$MOP_TOKEN" POST $MF/terminal/finish "{\"operationId\":$MFOPA20,\"goodQty\":5}"; check "mes: finishing a QC step puts it on QC hold, not done" '.success and .data.status == 5 and .data.qcRequestedAtText != null and .data.qcAccepted == false'
ibx "$QC_TOKEN" "mf:qc:$MFOPA20"; check "mes: the quality control role has the inspection request in the inbox" '.success and (.data.data|length) == 1'
as "$MOP_TOKEN" POST $MF/operations/qc-decision "{\"operationId\":$MFOPA20,\"result\":1}"; nomsg "mes: the operator cannot decide an inspection" "بازرس کنترل کیفیت"
as "$QC_TOKEN" POST $MF/operations/qc-decision "{\"operationId\":$MFOPA20,\"result\":3}"; nomsg "mes: a result other than accept needs a note" "توضیح"
as "$QC_TOKEN" POST $MF/operations/qc-decision "{\"operationId\":$MFOPA20,\"result\":3,\"note\":\"قطر خارجی بالاتر از تلرانس\"}"; check "mes: QC sends the part to rework: step 20 rejected, inspection record and NCR created" '.success and .data.status == 6 and .data.ncrId > 0 and (.data.qcRecordNumber|startswith("QIR")) and .data.reworkQty == 5'
MFNCR=$(val .data.ncrId)
ibx "$QC_TOKEN" "mf:qc:$MFOPA20"; check "mes: the QC request is closed" '.success and (.data.data|length) == 0'
call GET "$MF/orders/$MFORDA"; check "mes: a rework operation (sequence 20, -R) follows the rejected step, ready, linked to the NCR; step 30 waits" ".success and (.data.operations|length) == 4 and ([.data.operations[]|select(.isRework)][0] | .sequence == 20 and .status == 1 and .reworkOfOperationId == $MFOPA20 and .ncrId == $MFNCR and .jobCard == \"$MFNOA/20-R\") and ([.data.operations[]|select(.id == $MFOPA30)][0].status == 0)"
MFOPA20R=$(jq -r '[.data.operations[]|select(.isRework)][0].id' "$B")
as "$MSV_TOKEN" POST $MF/operations/rework "{\"operationId\":$MFOPA20,\"ncrId\":$MFNCR}"; nomsg "mes: one rework operation per rejected step (the shop supervisor asks for a second one)" "قبلاً عملیات"
call POST $MF/operations/rework "{\"operationId\":$MFOPA20,\"ncrId\":$MFNCR}"; nomsg "mes: the PMO cannot create rework operations (shop supervisor or production manager only)" "مجوز"
as "$MOP_TOKEN" POST $MF/terminal/start "{\"operationId\":$MFOPA20R,\"kind\":3}"; check "mes: the operator starts the rework operation (rework booking)" '.success and .data.status == 2'
as "$MOP_TOKEN" POST $MF/terminal/finish "{\"operationId\":$MFOPA20R,\"goodQty\":4}"; nomsg "mes: the rework covers the 5 rejected pieces, not fewer" "برابر نیست"
as "$MOP_TOKEN" POST $MF/terminal/finish "{\"operationId\":$MFOPA20R,\"goodQty\":1}"; check "mes: rework finished with 5 good pieces: QC hold again" '.success and .data.status == 5 and .data.goodQty == 5'
as "$QC_TOKEN" POST $MF/operations/qc-decision "{\"operationId\":$MFOPA20R,\"result\":1}"; check "mes: QC accepts the rework" '.success and .data.status == 4 and .data.qcAccepted == true'

# --- outside work drafts, completion, receipt
call GET "$MF/orders/$MFORDA"; check "mes: the outside step is ready and drafted a subcontract purchase request line and a subcontract issue draft" ".success and ([.data.operations[]|select(.id == $MFOPA30)][0] | .status == 1 and .isOutside == true and .purchaseRequestId > 0 and .purchaseRequestLineId > 0 and (.purchaseRequestNumber|startswith(\"PRQ\")) and .subcontractIssueId > 0 and .outsideSupplierId == ${PRV:-0})"
MFSI=$(jq -r ".data.operations[]|select(.id == $MFOPA30)|.subcontractIssueId" "$B"); MFPR=$(jq -r ".data.operations[]|select(.id == $MFOPA30)|.purchaseRequestId" "$B")
call GET "procurement/requests/$MFPR"; check "mes: the purchase request is a draft subcontract request for the work order, 5 units, and names the default contractor of the operation standard" ".success and .data.status == 0 and .data.kind == 3 and .data.workOrderId == $MFWO and (.data.lines|length) == 1 and .data.lines[0].quantity == 5 and (.data.description|contains(\"کارگاه ماشینکاری آزمون\")) and (.data.lines[0].technicalRequirements|contains(\"کارگاه ماشینکاری آزمون\"))"
ibx "$COM_TOKEN" "pr:$MFPR"; check "mes: the commercial role has the draft subcontract request in its procurement inbox" '.success and (.data.data|length) == 1 and .data.data[0].sourceModule == "procurement" and .data.data[0].recipientRole == "COMMERCIAL"'
ibx "$PLAN_TOKEN" "pr:$MFPR"; check "mes: the planning role does not get that draft-request item" '.success and (.data.data|length) == 0'
call GET "inventory/documents/$MFSI"; check "mes: the issue to the contractor is a draft document of the inventory module for item A, linked to the work order" ".success and .data.status == 0 and .data.type == 9 and .data.workOrderId == $MFWO and .data.lines[0].itemId == $MFPA and .data.lines[0].quantity == 5"
as "$MOP_TOKEN" POST $MF/terminal/start "{\"operationId\":$MFOPA30}"; nomsg "mes: outside work is not started on the shop terminal" "بیرونی"
as "$MOP_TOKEN" POST $MF/operations/action "{\"id\":$MFOPA30,\"action\":\"complete-outside\",\"documentId\":$MFSI}"; nomsg "mes: the operator cannot complete outside work" "مجوز"
as "$MSV_TOKEN" POST $MF/operations/action "{\"id\":$MFOPA30,\"action\":\"complete-outside\",\"documentId\":$MFSI}"; nomsg "mes: outside work completes only with a confirmed return document from the contractor" "برگشت از پیمانکار"
call POST inventory/documents "{\"type\":10,\"documentDate\":\"2026-10-19T00:00:00\",\"warehouseId\":$WK,\"destinationWarehouseId\":$W1,\"providerId\":${PRV:-0},\"workOrderId\":$MFWO,\"referenceNumber\":\"OTHER-JOB\",\"lines\":[{\"itemId\":$MFCMP,\"quantity\":1}]}"; check "mes: setup: a draft return from the contractor of the same work order that quotes another job" '.success and .data.id > 0 and .data.type == 10'
MFSB=$(val .data.id)
as "$MSV_TOKEN" POST $MF/operations/action "{\"id\":$MFOPA30,\"action\":\"complete-outside\",\"documentId\":$MFSB}"; nomsg "mes: a return document that does not quote this operation's job (or its issue) is refused as not belonging to the issue" "مربوط به حواله ارسال این عملیات نیست"
as "$MPM_TOKEN" POST $MF/operations/action "{\"id\":$MFOPA30,\"action\":\"skip\",\"reason\":\"x\"}"; nomsg "mes: skipping a step is the shop supervisor's alone" "سرپرست کارگاه"
as "$MSV_TOKEN" POST $MF/operations/action "{\"id\":$MFOPA30,\"action\":\"skip\"}"; nomsg "mes: skipping needs a reason" "دلیل"
as "$MSV_TOKEN" POST $MF/operations/action "{\"id\":$MFOPA30,\"action\":\"skip\",\"reason\":\"عملیات حرارتی در این آزمون انجام نمی‌شود\"}"; check "mes: the supervisor skips the outside step with a reason: every step is finished, the order is completed and a production receipt draft is made" '.success and .data.status == 7 and .data.skipReason != null'
call GET "$MF/orders/$MFORDA"; check "mes: order A is completed with a receipt draft number and the actual finish in Persian" '.success and .data.status == 3 and .data.productionReceiptId > 0 and .data.productionReceiptNumber != null and (.data.actualFinishText|test("^1[34][0-9]{2}/[0-9]{2}/[0-9]{2}"))'
MFRCT=$(val .data.productionReceiptId)
call GET "inventory/documents/$MFRCT"; check "mes: the receipt is a draft production receipt of 5 units of item A for the work order" ".success and .data.status == 0 and .data.type == 2 and .data.workOrderId == $MFWO and .data.lines[0].itemId == $MFPA and .data.lines[0].quantity == 5"
as "$MOP_TOKEN" POST $MF/orders/action "{\"id\":$MFORDA,\"action\":\"close\"}"; nomsg "mes: the operator cannot close an order" "مجوز"
as "$MPM_TOKEN" POST $MF/orders/action "{\"id\":$MFORDA,\"action\":\"close\"}"; check "mes: the production manager closes the completed order A" '.success and .data.status == 4'
as "$MSV_TOKEN" POST $MF/orders/action "{\"id\":$MFORDB,\"action\":\"close\"}"; nomsg "mes: only a completed order is closed" "بسته می‌شود"

# --- order C to completion by the operator
as "$MOP_TOKEN" POST $MF/terminal/start "{\"operationId\":$MFOPC10,\"machineId\":$MFM1}"; check "mes: the operator resumes C (paused by the supervisor booking and the other job)" '.success and .data.status == 2'
as "$MOP_TOKEN" POST $MF/terminal/pause "{\"operationId\":$MFOPC10,\"reasonCodeId\":$MFRDN,\"note\":\"استراحت\"}"; check "mes: pause with a downtime reason" '.success and .data.status == 3 and .data.myRunning == false'
as "$MOP_TOKEN" POST $MF/terminal/start "{\"operationId\":$MFOPC10,\"machineId\":$MFM1}"; check "mes: start again" '.success and .data.status == 2'
as "$MOP_TOKEN" POST $MF/terminal/finish "{\"operationId\":$MFOPC10,\"goodQty\":1,\"scrapQty\":1,\"scrapReasonCodeId\":$MFRSC}"; check "mes: C finished with 1 good and 1 scrapped piece (scrap reason kept)" '.success and .data.status == 4 and .data.goodQty == 1 and .data.scrapQty == 1'
call GET "$MF/orders/$MFORDC"; check "mes: C is completed; the receipt draft covers the 1 good piece" '.success and .data.status == 3 and .data.productionReceiptId > 0'
as "$MSV_TOKEN" GET "$MF/bookings?OperationId=$MFOPC10&PageSize=0"; check "mes: C has 4 bookings (3 terminal, 1 supervisor), none running; the supervisor booking is exactly 330 minutes = 5.5 hours; every hours value is its minutes / 60 rounded to 2 decimals" '.success and (.data.data|length) == 4 and ([.data.data[]|select(.source == 1)]|length) == 3 and ([.data.data[]|select(.running == true)]|length) == 0 and ([.data.data[]|select(.source == 2)]|length) == 1 and ([.data.data[]|select(.source == 2)][0] | .minutes == 330 and .hours == 5.5) and ([.data.data[]|select(.hours != ((.minutes / 60 * 100 | round) / 100))]|length) == 0'
as "$FM_TOKEN" GET "$FAU?EntityType=MfProductionOrder&EntityId=$MFORDC&PageSize=0"; check "mes: the override release of C is audited with its reason" '.success and ([.data.data[]|select(.action == "Modified" and (.afterJson|tostring|contains("ReleaseOverrideReason")))]|length) >= 1'

# --- reading roles
as "$MMT_TOKEN" GET "$MF/orders?OpenOnly=true&PageSize=5"; check "mes: maintenance reads the open orders (none of the three is open any more)" ".success and ([.data.data[]|select(.id == $MFORDA or .id == $MFORDB or .id == $MFORDC)]|length) == 0"
as "$MMT_TOKEN" POST $MF/orders/release "{\"id\":$MFORDA}"; nomsg "mes: maintenance cannot release orders" "سرپرست کارگاه"
as "$MOP_TOKEN" GET "$MF/routings?PageSize=5"; no "mes: the operator cannot read routing revisions"
as "$MOP_TOKEN" GET $FP/employees; [ "$CODE" = "403" ] && ok "mes: the operator cannot read the payroll employees (403)" || bad "mes: the operator cannot read the payroll employees (403) got $CODE"
as "$MOP_TOKEN" GET "$FP/employees/$MFEMP"; [ "$CODE" = "403" ] && ok "mes: the operator cannot read his own payroll record either (403)" || bad "mes: the operator cannot read his own payroll record either (403) got $CODE"
as "$MOP_TOKEN" GET "$FG/years"; [ "$CODE" = "403" ] && ok "mes: the operator cannot read the general ledger (403)" || bad "mes: the operator cannot read the general ledger (403) got $CODE"
as "$MOP_TOKEN" GET "$MF/work-centers/$MFWC1"; check "mes: the operator reads the work centre but the hourly rates are not shown to him" '.success and .data.code == "MF-LATHE" and .data.machineRateRial == null and .data.labourRateRial == null'
as "$MOP_TOKEN" GET "$MF/bookings?PageSize=0"; check "mes: the operator's booking list carries no payroll or rate field (names, personnel codes and hours only)" '.success and (.data.data|length) >= 1 and ([.. | objects | keys[] | select(test("Rial$|[Rr]ate$|[Ww]age|[Ss]alary|[Ii]ban|[Nn]ationalCode|[Cc]ost"))]|length) == 0'

# =================================================================== PRODUCTION / SHOP FLOOR (MES), release P1 part B
# daily cost posting, finite-capacity scheduler with load report and dispatch list, maintenance with KPIs, dashboards.
# Everything is dated in one fixed past week (Mon 2026-08-03 .. Fri 2026-08-07 on the Mon-Fri calendar PCCAL) and the scheduler runs with an explicit asOf,
# so every figure is exact on every run. People are only the test users and the personnel record of the operator (no names are invented).
TOKEN=$ADMIN_TOKEN

# --- work centres without rates, the CNC machine, item D (three orders on the CNC centre) and item F (one order on its own centre, linked to a project activity)
call POST $MF/work-centers "{\"code\":\"MF-CNC\",\"title\":\"فرز سی‌ان‌سی\",\"kind\":1,\"calendarId\":$PCCAL,\"shiftsPerDay\":1,\"hoursPerShift\":8,\"efficiencyPercent\":100,\"utilisationPercent\":100,\"parallelResources\":1}"; check "mes B: CNC work centre (8 h a day, efficiency 100, utilisation 100, Mon-Fri calendar) is saved without rates" '.success and .data.id > 0 and .data.machineRateRial == null and .data.labourRateRial == null'
MBWC=$(val .data.id)
call POST $MF/work-centers "{\"code\":\"MF-FC\",\"title\":\"فرز تکمیلی\",\"kind\":1,\"calendarId\":$PCCAL,\"shiftsPerDay\":1,\"hoursPerShift\":8,\"efficiencyPercent\":100,\"utilisationPercent\":100,\"parallelResources\":1}"; check "mes B: second work centre (own queue for the project-linked order)" '.success and .data.id > 0'
MBWF=$(val .data.id)
call POST Machine '{"title":"ماشین فرز سی‌ان‌سی آزمون"}'; MBM=$(val .data)
call POST $MF/machines/info "{\"machineId\":$MBM,\"workCenterId\":$MBWC,\"status\":1}"; check "mes B: the CNC machine belongs to the CNC work centre" '.success and .data.status == 1'
item MBPD "{\"code\":\"MF.10.00.004.1\",\"title\":\"قطعه ماشینکاری د\",\"unit\":\"عدد\",\"trackingMode\":0,\"requiresIncomingInspection\":false}" "mes B: item D (no tracking)"
item MBPF "{\"code\":\"MF.10.00.006.1\",\"title\":\"قطعه ماشینکاری ف\",\"unit\":\"عدد\",\"trackingMode\":0,\"requiresIncomingInspection\":false}" "mes B: item F (no tracking)"
call POST inventory/trace/templates "{\"itemId\":$MBPD,\"sequence\":10,\"title\":\"فرزکاری د\"}"; MBTD=$(val .data.id)
call POST inventory/trace/templates "{\"itemId\":$MBPF,\"sequence\":10,\"title\":\"فرزکاری ف\"}"; MBTF=$(val .data.id)
call POST $MF/operation-standards "{\"operationTemplateId\":$MBTD,\"workCenterId\":$MBWC,\"setupHours\":2,\"runHoursPerUnit\":1}"; check "mes B: standard of item D (setup 2 h, 1 h per piece, CNC centre)" '.success and .data.setupHours == 2 and .data.runHoursPerUnit == 1'
call POST $MF/operation-standards "{\"operationTemplateId\":$MBTF,\"workCenterId\":$MBWF,\"setupHours\":1,\"runHoursPerUnit\":1}"; check "mes B: standard of item F (setup 1 h, 1 h per piece)" '.success and .data.setupHours == 1'
mfroute "$MBPD" "item D"
mfroute "$MBPF" "item F"

# item D: three BOM lines of the first work order (4, 3 and 49 pieces); the processes are made after the WBS generation, so their orders are drafted by hand and have no project activity
mbdraft() { # mbdraft BOMPOSITION COUNT LABEL : BOM line + make process + draft order; sets MBO (order id) and MBN (order number)
  mfbom "$1" "$2" "قطعه د $3"; mfproc "$MFBOM" "MF.10.00.004.1" "MF-NO-STOCK-CODE" "ساخت قطعه د $3"; check "mes B: make process of order D$3 ($2 pieces)" '.success and .data.id > 0'
  call POST $MF/orders/draft "{\"processId\":$(val .data.id)}"; check "mes B: order D$3 is drafted for its process" '.success and .data.drafted == 1 and (.data.orderIds|length) == 1'
  MBO=$(val '.data.orderIds[0]')
  call GET "$MF/orders/$MBO"; check "mes B: order D$3 is a draft with one operation from the approved routing" ".success and .data.status == 0 and .data.quantity == $2 and (.data.operations|length) == 1 and .data.operations[0].setupHoursPlanned == 2 and .data.operations[0].runHoursPlanned == $2"
  MBN=$(val .data.number)
}
mbrelease() { # mbrelease ORDERID LABEL
  as "$MSV_TOKEN" POST $MF/orders/release "{\"id\":$1}"; check "mes B: the shop supervisor releases order $2: its operation is ready" '.success and .data.status == 1 and ([.data.operations[].status]) == [1]'
}
mbdraft 4 4 1; MBO1=$MBO; MBN1=$MBN
mbdraft 5 3 2; MBO2=$MBO; MBN2=$MBN
mbdraft 6 49 3; MBO3=$MBO; MBN3=$MBN
mbrelease "$MBO1" D1; mbrelease "$MBO2" D2; mbrelease "$MBO3" D3
call GET "$MF/orders/$MBO1"; MBOP1=$(val '.data.operations[0].id')
call GET "$MF/orders/$MBO2"; MBOP2=$(val '.data.operations[0].id')

# item F: its own contract and work order, so the WBS generation gives its process a project activity and the order is linked to it
call POST Contracts '{"code":"MF-CT-02","title":"قرارداد دوم آزمون تولید","status":1,"employer":"کارفرمای آزمون"}'; MBCT2=$(val .data.id)
call POST Equipments '{"code":"MF-EQ-FAN","title":"Process Fan API 673 (production test)"}'; MBEQ2=$(val .data.id)
call POST ContractEquipments "{\"contractId\":$MBCT2,\"equipmentId\":$MBEQ2,\"count\":1}"
call POST WorkOrders "{\"contractId\":$MBCT2,\"equipmentId\":$MBEQ2,\"code\":\"MF-WO-2\",\"title\":\"Fan production\",\"description\":\"x\",\"count\":1}"; check "mes B: second work order created" '.success and .data.id > 0'
MBWO2=$(val .data.id)
as "$PLAN_TOKEN" POST $PC/terms "{\"contractId\":$MBCT2,\"currencyCode\":\"IRR\",\"valueExVat\":500000000,\"calendarId\":$PCCAL,\"dataDateWeekday\":3,\"scheduleStartDate\":\"2026-11-02T00:00:00\",\"clientReviewDays\":14,\"autoSnapshot\":false}"; check "mes B: second contract terms" '.success'
as "$PLAN_TOKEN" POST $PC/work-order-info "{\"workOrderId\":$MBWO2,\"contractId\":$MBCT2,\"typeCode\":\"PU\",\"sizeCode\":\"100\",\"optionCode\":\"26\"}"; check "mes B: second equipment project code" '.success and (.data.equipmentProjectCode|startswith("PU10026"))'
call POST Assemblies "{\"title\":\"Fan casing\",\"code\":\"MF-ASM-2\",\"workOrderId\":$MBWO2,\"assemblerDuration\":1}"; MBASM2=$(val .data.id)
MBASM_KEEP=$MFASM; MFASM=$MBASM2
mfbom 1 1 "قطعه ف"; mfproc "$MFBOM" "MF.10.00.006.1" "MF-NO-STOCK-CODE" "ساخت قطعه ف"; check "mes B: make process of item F (1 piece)" '.success and .data.id > 0'
MFASM=$MBASM_KEEP
as "$PLAN_TOKEN" POST $PC/wbs/generate "{\"contractId\":$MBCT2,\"workOrderIds\":[$MBWO2]}"; check "mes B: WBS generation for the second work order succeeds" '.success'
as "$MPM_TOKEN" GET "$MF/orders?WorkOrderId=$MBWO2&PageSize=0"
if [ "$(val '.data.data|length')" != "1" ]; then call POST $MF/orders/draft "{\"workOrderId\":$MBWO2}"; as "$MPM_TOKEN" GET "$MF/orders?WorkOrderId=$MBWO2&PageSize=0"; fi
check "mes B: the WBS hook drafted exactly one production order for item F" '.success and (.data.data|length) == 1 and .data.data[0].status == 0'
MBOF=$(val '.data.data[0].id'); MBNF=$(val '.data.data[0].number')
call GET "$MF/orders/$MBOF"; check "mes B: order F is linked to the project activity of its process" '.success and .data.activityId > 0'
MBACT=$(val .data.activityId)
mbrelease "$MBOF" F

# the need date of the item D orders is the start date of their work order (no activity); the date is in the past, so the lateness is the same on every run
call PUT WorkOrders/set-start-date '{"workOrderId":'"$MFWO"',"startDate":"2026-08-04T00:00:00Z"}'; check "mes B: the start date (need date) of the first work order is set to 2026-08-04" '.success'

# --- the finite-capacity scheduler, asOf Monday 2026-08-03
as "$PLAN_TOKEN" GET "$PC/activities?ContractId=$MBCT2&PageSize=0"; MBACTB=$(jq -c "[.data.data[]|select(.id == $MBACT)|{es:.earlyStart,ef:.earlyFinish,ls:.lateStart,lf:.lateFinish,tf:.totalFloat}][0]" "$B")
[ -n "$MBACTB" ] && [ "$MBACTB" != "null" ] && ok "mes B: the schedule of the project activity of order F is read before the scheduler runs" || bad "mes B: the schedule of the project activity of order F is read before the scheduler runs"
as "$MOP_TOKEN" POST $MF/schedule/run '{"asOf":"2026-08-03T00:00:00"}'; nomsg "mes B: the operator cannot run the scheduler" "مجوز"
as "$MMT_TOKEN" POST $MF/schedule/run '{"asOf":"2026-08-03T00:00:00"}'; nomsg "mes B: maintenance cannot run the scheduler" "مجوز"
as "$MPM_TOKEN" POST $MF/schedule/run '{"asOf":"2026-08-03T00:00:00"}'; cp "$B" "$OUT/mb-schedule-1.json"
check "mes B: scheduler run: 4 open orders with 4 operations, 1 late (D3), none unscheduled, 1 activity forecast pushed" '.success and .data.id > 0 and .data.asOfText == "1405/05/12" and .data.orders == 4 and .data.operations == 4 and .data.lateOrders == 1 and .data.unscheduled == 0 and .data.forecastsPushed == 1'
check "mes B: D1 (6 h) is planned Monday 08-03 to Monday 08-03 and is not late" "([.data.items[]|select(.number == \"$MBN1\")][0]) | (.plannedStart|startswith(\"2026-08-03\")) and (.plannedFinish|startswith(\"2026-08-03\")) and .isLate == false and .daysLate == 0 and .plannedFinishText == \"1405/05/12\""
check "mes B: D2 (5 h, queued behind D1) is planned 08-03 to Tuesday 08-04 and is not late" "([.data.items[]|select(.number == \"$MBN2\")][0]) | (.plannedStart|startswith(\"2026-08-03\")) and (.plannedFinish|startswith(\"2026-08-04\")) and .isLate == false"
check "mes B: D3 (51 h) runs from 08-04 over the weekend gap to Wednesday 08-12 (1405/05/21), 8 days after its need date" "([.data.items[]|select(.number == \"$MBN3\")][0]) | (.plannedStart|startswith(\"2026-08-04\")) and (.plannedFinish|startswith(\"2026-08-12\")) and .plannedFinishText == \"1405/05/21\" and .isLate == true and .daysLate == 8 and (.needDate|startswith(\"2026-08-04\")) and .operations[0].hours == 51"
check "mes B: order F (2 h on its own centre) is planned on 08-03 only" "([.data.items[]|select(.number == \"$MBNF\")][0]) | (.plannedStart|startswith(\"2026-08-03\")) and (.plannedFinish|startswith(\"2026-08-03\")) and .operations[0].workCenterCode == \"MF-FC\""
as "$MPM_TOKEN" POST $MF/schedule/run '{"asOf":"2026-08-03T00:00:00"}'
[ "$(jq -S 'del(.data.id)' "$OUT/mb-schedule-1.json")" = "$(jq -S 'del(.data.id)' "$B")" ] && ok "mes B: the scheduler is deterministic: a second run with the same asOf gives the very same plan" || bad "mes B: the scheduler is deterministic: a second run with the same asOf gives the very same plan"
as "$MPM_TOKEN" GET "$MF/orders?WorkOrderId=$MFWO&PageSize=0"; check "mes B: the plan is kept on the orders: D3 is late by 8 days, D1 and D2 are not, all have the run time" ".success and ([.data.data[]|select(.id == $MBO3)][0] | .isLate == true and .daysLate == 8 and .scheduledAtText != null and (.plannedFinish|startswith(\"2026-08-12\"))) and ([.data.data[]|select(.id == $MBO1 or .id == $MBO2)|select(.isLate)]|length) == 0"
as "$PLAN_TOKEN" GET "$PC/activities?ContractId=$MBCT2&PageSize=0"; MBACTA=$(jq -c "[.data.data[]|select(.id == $MBACT)|{es:.earlyStart,ef:.earlyFinish,ls:.lateStart,lf:.lateFinish,tf:.totalFloat}][0]" "$B")
check "mes B: the forecast of the project activity of order F is pushed by the scheduler (08-03)" "([.data.data[]|select(.id == $MBACT)][0]) | (.forecastStart|startswith(\"2026-08-03\")) and (.forecastFinish|startswith(\"2026-08-03\"))"
[ "$MBACTA" = "$MBACTB" ] && ok "mes B: the current schedule of the activity (early, late, float; the baseline side) is untouched by the scheduler" || bad "mes B: the current schedule of the activity (early, late, float; the baseline side) is untouched by the scheduler: $MBACTB against $MBACTA"

# --- load report (12 weeks, Saturday weeks) and the dispatch list
as "$MPM_TOKEN" GET "$MF/load?From=2026-08-03&WorkCenterId=$MBWC"; check "mes B: load report: 12 weeks, week 1 (from Saturday 08-01) has 40 h capacity, 51 h load (127.5 %), 40 h scheduled and is overloaded" '.success and .data.weeks == 12 and (.data.rows|length) == 1 and .data.rows[0].code == "MF-CNC" and (.data.rows[0].cells|length) == 12 and (.data.rows[0].cells[0] | (.weekStart|startswith("2026-08-01")) and .weekStartText == "1405/05/10" and .capacityHours == 40 and .loadHours == 51 and .scheduledHours == 40 and .loadPercent == 127.5 and .overloaded == true)'
check "mes B: load report: week 2 has 11 h load against 40 h (27.5 %), 22 h scheduled; the other weeks are empty; exactly one overload" '.success and (.data.rows[0].cells[1] | .capacityHours == 40 and .loadHours == 11 and .scheduledHours == 22 and .loadPercent == 27.5 and .overloaded == false) and ([.data.rows[0].cells[2:][]|select(.loadHours != 0)]|length) == 0 and (.data.overloads|length) == 1 and .data.overloads[0].loadPercent == 127.5'
as "$MPM_TOKEN" GET "$MF/load?From=2026-08-03&WorkCenterId=$MBWC&Threshold=130"; check "mes B: with a threshold of 130 % there is no overload" '.success and .data.threshold == 130 and (.data.overloads|length) == 0'
as "$MPM_TOKEN" GET "$MF/load?From=2026-08-03&WorkCenterId=$MBWC&Weeks=4"; check "mes B: the number of weeks is a parameter" '.success and (.data.rows[0].cells|length) == 4'
as "$MOP_TOKEN" GET "$MF/load?From=2026-08-03"; nomsg "mes B: the operator cannot read the load report" "مجوز"
as "$MMT_TOKEN" GET "$MF/dispatch?WorkCenterId=$MBWC&PageSize=0"; check "mes B: dispatch list of the CNC centre: D1, D2, D3 in priority order, all ready, with the planned dates" ".success and (.data.data|length) == 3 and ([.data.data[].orderNumber]) == [\"$MBN1\",\"$MBN2\",\"$MBN3\"] and ([.data.data[].stage]|unique) == [\"Ready\"] and .data.data[0].jobCard == \"$MBN1/10\" and .data.data[2].quantity == 49 and .data.data[2].plannedFinishText == \"1405/05/21\" and .data.data[2].orderLate == true and .data.data[0].orderLate == false and ([.data.data[].priority]|map(. != null)|all)"
as "$MMT_TOKEN" GET "$MF/dispatch"; nomsg "mes B: the dispatch list needs a work centre" "مرکز کار"
as "$SEC_TOKEN" GET "$MF/dispatch?WorkCenterId=$MBWC"; nomsg "mes B: the security user cannot read the dispatch list" "مجوز"
as "$MOP_TOKEN" GET "$MF/dispatch?WorkCenterId=$MBWC&PageSize=0"; check "mes B: the operator reads the dispatch list, and it carries no cost, rate or wage field" '.success and (.data.data|length) == 3 and ([.. | objects | keys[] | select(test("Rial$|[Rr]ate$|[Ww]age|[Ss]alary|[Ii]ban|[Nn]ationalCode|[Cc]ost"))]|length) == 0'

# --- a breakdown: the machine is Down while the order is open and the scheduler leaves it out; maintenance does the work, production sees everything
as "$MOP_TOKEN" POST $MF/maintenance/orders "{\"machineId\":$MBM,\"kind\":2,\"task\":\"x\"}"; nomsg "mes B: the operator cannot create maintenance orders" "مجوز"
as "$MSV_TOKEN" POST $MF/maintenance/orders "{\"machineId\":$MBM,\"kind\":2,\"task\":\"x\"}"; nomsg "mes B: the shop supervisor cannot create maintenance orders either (maintenance and the production manager do)" "مجوز"
as "$MMT_TOKEN" POST $MF/maintenance/orders "{\"machineId\":$MBM,\"kind\":2,\"task\":\"x\",\"costCenter\":\"CC-NO-SUCH\"}"; nomsg "mes B: an unknown cost centre is refused" "مرکز هزینه"
as "$MMT_TOKEN" POST $MF/maintenance/orders "{\"machineId\":$MBM,\"kind\":2,\"task\":\"توقف اسپیندل ماشین فرز\",\"downtimeStart\":\"2026-08-05T10:00:00\",\"estimatedHours\":4}"; check "mes B: maintenance opens a breakdown order for the CNC machine (stopped Wednesday 08-05 10:00)" '.success and (.data.number|startswith("MWO")) and .data.kind == 2 and .data.status == 0 and (.data.downtimeStart|startswith("2026-08-05T10:00")) and .data.downtimeEnd == null and .data.dueDateText == "1405/05/14"'
MBMW=$(val .data.id)
ibx "$MMT_TOKEN" "mf:mwo:$MBMW"; check "mes B: the maintenance role has the order in its inbox" '.success and (.data.data|length) == 1'
as "$MOP_TOKEN" GET "$MF/machines?WorkCenterId=$MBWC&Status=2"; check "mes B: the machine is Down while its breakdown order is open" ".success and (.data.data|length) == 1 and .data.data[0].machineId == $MBM"
as "$MPM_TOKEN" POST $MF/schedule/run '{"asOf":"2026-08-03T00:00:00"}'
check "mes B: with the machine down from 08-05 10:00 the scheduler cannot finish D3 (no finish date, late, one operation unscheduled); D1 and D2 are unchanged" "(.data.unscheduled == 1) and ([.data.items[]|select(.number == \"$MBN3\")][0] | .plannedFinish == null and .isLate == true) and ([.data.items[]|select(.number == \"$MBN1\")][0].plannedFinish|startswith(\"2026-08-03\")) and ([.data.items[]|select(.number == \"$MBN2\")][0].plannedFinish|startswith(\"2026-08-04\"))"
as "$MPM_TOKEN" GET "$MF/maintenance/orders?MachineId=$MBM&PageSize=0"; check "mes B: the production manager sees the maintenance orders of the machine" ".success and (.data.data|length) == 1 and .data.data[0].id == $MBMW"
as "$MMT_TOKEN" POST $MF/maintenance/orders/action "{\"id\":$MBMW,\"action\":\"start\"}"; check "mes B: maintenance starts the work" '.success and .data.status == 1'
as "$MMT_TOKEN" POST $MF/maintenance/orders/action "{\"id\":$MBMW,\"action\":\"complete\"}"; nomsg "mes B: a breakdown order cannot be closed without the cause" "علت خرابی"
as "$MMT_TOKEN" POST $MF/maintenance/orders/save "{\"id\":$MBMW,\"cause\":\"سایش یاتاقان اسپیندل\"}"; check "mes B: the cause is saved" '.success and .data.cause == "سایش یاتاقان اسپیندل"'
as "$MMT_TOKEN" POST $MF/maintenance/orders/action "{\"id\":$MBMW,\"action\":\"complete\"}"; nomsg "mes B: and not without the action taken" "اقدام انجام"
as "$MMT_TOKEN" POST $MF/maintenance/orders/save "{\"id\":$MBMW,\"actionTaken\":\"یاتاقان تعویض و اسپیندل تراز شد\"}"; check "mes B: the action taken is saved" '.success and .data.actionTaken != null'
as "$MMT_TOKEN" POST $MF/maintenance/labour "{\"orderId\":$MBMW,\"employeeId\":$MFEMP,\"workDate\":\"2026-08-05\",\"hours\":3,\"note\":\"تعویض یاتاقان\"}"; check "mes B: labour of 3 hours is booked on the order" '.success and .data.labourHours == 3 and (.data.labour|length) == 1 and .data.labour[0].workDateText == "1405/05/14"'
as "$MMT_TOKEN" POST $MF/maintenance/labour "{\"orderId\":$MBMW,\"employeeId\":$MFEMP,\"workDate\":\"2026-08-05\",\"hours\":25}"; nomsg "mes B: labour of more than 24 hours is refused" "۲۴"
as "$MMT_TOKEN" POST $MF/maintenance/orders/action "{\"id\":$MBMW,\"action\":\"complete\",\"downtimeEnd\":\"2026-08-04T10:00:00\"}"; nomsg "mes B: the end of the stop cannot be before its start" "پایان توقف"
as "$MMT_TOKEN" POST $MF/maintenance/orders/action "{\"id\":$MBMW,\"action\":\"complete\",\"downtimeEnd\":\"2026-08-05T14:00:00\"}"; check "mes B: the order is completed: 4 hours of downtime, labour 3 h" '.success and .data.status == 2 and .data.downtimeHours == 4 and .data.labourHours == 3 and (.data.downtimeEndText|startswith("1405/05/14"))'
ibx "$MMT_TOKEN" "mf:mwo:$MBMW"; check "mes B: the inbox item of the order is closed" '.success and (.data.data|length) == 0'
as "$MOP_TOKEN" GET "$MF/machines?WorkCenterId=$MBWC&Status=1"; check "mes B: the machine is available again" ".success and ([.data.data[]|select(.machineId == $MBM)]|length) == 1"
as "$MMT_TOKEN" POST $MF/maintenance/orders/save "{\"id\":$MBMW,\"cause\":\"x\"}"; nomsg "mes B: a completed order is not edited" "بسته شده"
as "$MPM_TOKEN" POST $MF/schedule/run '{"asOf":"2026-08-03T00:00:00"}'
check "mes B: after the 4 hours of downtime (08-05 10:00 to 14:00) D3 finishes on Thursday 08-13 (1405/05/22), 9 days late; D1 and D2 are unchanged" "(.data.unscheduled == 0) and (.data.lateOrders == 1) and ([.data.items[]|select(.number == \"$MBN3\")][0] | (.plannedFinish|startswith(\"2026-08-13\")) and .plannedFinishText == \"1405/05/22\" and .daysLate == 9) and ([.data.items[]|select(.number == \"$MBN2\")][0].plannedFinish|startswith(\"2026-08-04\"))"
as "$MPM_TOKEN" GET "$MF/maintenance/kpis?From=2026-08-03&To=2026-08-07&MachineId=$MBM"; check "mes B: machine KPIs of the week: planned 40 h, downtime 4 h, availability 90 %, 1 breakdown, MTBF 36 h, MTTR 4 h" '.success and (.data.data|length) == 1 and (.data.data[0] | .plannedHours == 40 and .downtimeHours == 4 and .availabilityPercent == 90 and .breakdowns == 1 and .mtbfHours == 36 and .mttrHours == 4 and .workCenterCode == "MF-CNC")'
as "$MOP_TOKEN" GET "$MF/maintenance/kpis?From=2026-08-03&To=2026-08-07"; nomsg "mes B: the operator cannot read the KPIs" "مجوز"
as "$FM_TOKEN" GET "$FAU?EntityType=MfMaintenanceOrder&EntityId=$MBMW&PageSize=0"; check "mes B: the maintenance order is in the audit trail" '.success and ([.data.data[]|select(.action == "Modified")]|length) >= 1'

# --- the breakdown order the machine-down report of part A has already drafted: one order per report, spare parts through a material request, the issue draft carries the cost centre
as "$MMT_TOKEN" GET "$MF/maintenance/orders?MachineId=$MFM3&Kind=2&PageSize=0"; check "mes B: the machine-down report of the lathe has drafted its breakdown order by itself (one per report), open" ".success and (.data.data|length) == 1 and .data.data[0].requestId == $MFMNT and .data.data[0].status == 0 and .data.data[0].kind == 2 and (.data.data[0].number|startswith(\"MWO\"))"
MBMW3=$(val '.data.data[0].id')
ibx "$MMT_TOKEN" "mf:mwo:$MBMW3"; check "mes B: maintenance has that order in its inbox" '.success and (.data.data|length) == 1'
as "$MMT_TOKEN" POST $MF/maintenance/orders "{\"machineId\":$MFM3,\"kind\":2,\"task\":\"x\",\"requestId\":$MFMNT}"; nomsg "mes B: a second order for the same report is refused" "دستور نگهداری ساخته شده"
as "$MMT_TOKEN" POST $MF/maintenance/orders "{\"machineId\":$MBM,\"kind\":2,\"task\":\"x\",\"requestId\":$MFMNT}"; nomsg "mes B: a report of another machine is refused" "ماشین دیگری"
call POST finance/cost-centers '{"code":"CC-MNT","title":"مرکز هزینه نگهداری آزمون","isProduction":false}'; check "mes B: maintenance cost centre saved" '.success'
as "$MMT_TOKEN" POST $MF/maintenance/orders/action "{\"id\":$MBMW3,\"action\":\"request-parts\"}"; nomsg "mes B: the material request of the spare parts needs the parts list" "فهرست قطعات"
as "$MMT_TOKEN" POST $MF/maintenance/orders/save "{\"id\":$MBMW3,\"costCenter\":\"CC-MNT\",\"cause\":\"سایش یاتاقان\",\"actionTaken\":\"تعویض یاتاقان\",\"parts\":[{\"itemId\":$MFCMP,\"quantity\":2,\"note\":\"یاتاقان یدکی\"}]}"; check "mes B: cost centre, cause, action and one spare part (2 pieces) are saved" '.success and .data.costCenter == "CC-MNT" and (.data.parts|length) == 1 and .data.parts[0].quantity == 2'
as "$MMT_TOKEN" POST $MF/maintenance/orders/action "{\"id\":$MBMW3,\"action\":\"draft-issue\"}"; nomsg "mes B: the issue cannot be drafted before the material request exists" "درخواست کالای قطعات را بسازید"
as "$MMT_TOKEN" POST $MF/maintenance/orders/action "{\"id\":$MBMW3,\"action\":\"request-parts\"}"; check "mes B: the draft material request of the spare parts is made" '.success and .data.materialRequestId > 0 and .data.materialRequestNumber != null'
MBMR=$(val .data.materialRequestId)
as "$MMT_TOKEN" POST $MF/maintenance/orders/save "{\"id\":$MBMW3,\"parts\":[{\"itemId\":$MFCMP,\"quantity\":3}]}"; nomsg "mes B: the parts list is frozen once the request exists" "فهرست قطعات"
as "$MMT_TOKEN" POST $MF/maintenance/orders/action "{\"id\":$MBMW3,\"action\":\"draft-issue\"}"; nomsg "mes B: the issue waits for the final approval of the request (planning and production)" "تایید نهایی"
call POST inventory/material-requests/action "{\"id\":$MBMR,\"action\":\"submit\"}"; check "mes B: the request is submitted" '.success and .data.status == 1'
call POST inventory/material-requests/action "{\"id\":$MBMR,\"action\":\"approve-planning\"}"; check "mes B: planning approves the request" '.success and .data.planningApproved'
as "$PROD_TOKEN" POST inventory/material-requests/action "{\"id\":$MBMR,\"action\":\"approve-manufacturing\"}"; check "mes B: production approves the request: approved" '.success and .data.status == 2'
as "$MMT_TOKEN" POST $MF/maintenance/orders/action "{\"id\":$MBMW3,\"action\":\"draft-issue\"}"; check "mes B: the issue of the spare parts is drafted" '.success and .data.issueDocumentId > 0 and .data.issueDocumentNumber != null'
MBISS=$(val .data.issueDocumentId)
call GET "inventory/documents/$MBISS"; check "mes B: the issue is a draft against the request, carries the maintenance cost centre and the 2 spare parts (confirming it is a stock step for the warehouse)" ".success and .data.status == 0 and .data.type == 4 and .data.costCenter == \"CC-MNT\" and .data.materialRequestId == $MBMR and .data.lines[0].itemId == $MFCMP and .data.lines[0].quantity == 2"
as "$MMT_TOKEN" POST $MF/maintenance/orders/action "{\"id\":$MBMW3,\"action\":\"draft-issue\"}"; nomsg "mes B: the issue is drafted once" "قبلا پیش‌نویس"
as "$MMT_TOKEN" POST $MF/maintenance/orders/action "{\"id\":$MBMW3,\"action\":\"complete\"}"; check "mes B: the lathe breakdown order is completed: its request is done and the lathe is available again" '.success and .data.status == 2'
as "$MMT_TOKEN" GET "$MF/maintenance-requests?MachineId=$MFM3"; check "mes B: the report of part A is done with its order" ".success and .data.data[0].id == $MFMNT and .data.data[0].status == 2"
as "$MOP_TOKEN" GET "$MF/machines?WorkCenterId=$MFWC1&Status=1"; check "mes B: machine 3 is available again" ".success and ([.data.data[]|select(.machineId == $MFM3)]|length) == 1"

# --- time bookings of the week (the supervisor books for the personnel record of the operator, on the CNC machine)
as "$MSV_TOKEN" POST $MF/bookings "{\"operationId\":$MBOP1,\"employeeId\":$MFEMP,\"kind\":2,\"machineId\":$MBM,\"startAt\":\"2026-08-03T08:00:00\",\"endAt\":\"2026-08-03T12:00:00\",\"goodQty\":3,\"scrapQty\":1,\"scrapReasonCodeId\":$MFRSC,\"reason\":\"ثبت از روی برگه دستی\"}"; check "mes B: booking 1 on D1: 4 h on the CNC machine, 3 good and 1 scrapped piece" '.success and .data.hours == 4 and .data.minutes == 240 and .data.goodQty == 3 and .data.scrapQty == 1'
MBB1=$(val .data.id)
as "$MSV_TOKEN" POST $MF/bookings "{\"operationId\":$MBOP2,\"employeeId\":$MFEMP,\"kind\":2,\"machineId\":$MBM,\"startAt\":\"2026-08-04T08:00:00\",\"endAt\":\"2026-08-04T14:00:00\",\"goodQty\":3,\"reason\":\"ثبت از روی برگه دستی\"}"; check "mes B: booking 2 on D2: 6 h on the CNC machine, 3 good pieces" '.success and .data.hours == 6 and .data.goodQty == 3'
as "$MPM_TOKEN" GET "$MF/oee?From=2026-08-03&To=2026-08-07&WorkCenterId=$MBWC"; check "mes B: OEE of the CNC centre for the week: availability 90.0, performance 70.0 (7 standard of 10 booked hours), quality 85.7 (6 of 7 pieces), OEE 54.0" '.success and (.data|length) == 1 and (.data[0] | .plannedHours == 40 and .downtimeHours == 4 and .bookedRunHours == 10 and .standardHours == 7 and .goodQty == 6 and .scrapQty == 1 and .availabilityPercent == 90 and .performancePercent == 70 and .qualityPercent == 85.7 and .oeePercent == 54)'
as "$MOP_TOKEN" GET "$MF/oee?From=2026-08-03&To=2026-08-07"; nomsg "mes B: the operator cannot read the OEE" "مجوز"

# --- daily cost posting: a missing rate is refused in clear Persian (nothing is posted at zero), then finance enters the rates
as "$MOP_TOKEN" POST $MF/cost/run '{"date":"2026-08-03T00:00:00"}'; nomsg "mes B: the operator cannot run the cost posting" "مجوز"
as "$MSV_TOKEN" POST $MF/cost/run '{"date":"2026-08-03T00:00:00"}'; nomsg "mes B: the shop supervisor cannot run the cost posting" "مجوز"
as "$MSV_TOKEN" GET "$MF/cost/runs"; nomsg "mes B: the shop supervisor cannot read the cost runs" "مجوز"
as "$FM_TOKEN" POST $MF/cost/run "{\"date\":\"$(dd 0)T00:00:00\"}"; nomsg "mes B: today is refused (only finished days are posted)" "قبل از امروز"
as "$FM_TOKEN" POST $MF/cost/run '{"date":"2026-08-03T00:00:00"}'; nomsg "mes B: no labour rate: refused with the work centre named and the finance action (no zero-cost posting)" "نرخ ساعتی دستمزد"
check "mes B: the refusal names the CNC work centre" '.message | contains("MF-CNC")'
as "$FM_TOKEN" GET "finance/costs?contractId=$MFCT"; check "mes B: nothing was posted to the work order" "(.success) and ([.data[]|select(.workOrderId == $MFWO)|.labour + .overhead]|add // 0) == 0"
as "$FM_TOKEN" POST $MF/work-centers/rates "{\"id\":$MBWC,\"labourRateRial\":600}"; check "mes B: finance enters only the standard labour rate (600 a hour)" '.success and .data.labourRateRial == 600'
as "$FM_TOKEN" POST $MF/cost/run '{"date":"2026-08-03T00:00:00"}'; nomsg "mes B: no machine rate: refused with a clear message" "نرخ ساعتی ماشین"
as "$FM_TOKEN" POST $MF/work-centers/rates "{\"id\":$MBWC,\"machineRateRial\":2000,\"labourRateRial\":600}"; check "mes B: finance enters the machine rate (2000 a hour) and the labour rate (600 a hour)" '.success and .data.machineRateRial == 2000 and .data.labourRateRial == 600'
as "$FM_TOKEN" POST $MF/cost/run '{"date":"2026-08-03T00:00:00"}'; check "mes B: cost run for Monday 08-03: 1 booking, labour 4 h x 600 = 2400, machine 4 h x 2000 = 8000, rate from the work centre (no payroll exists)" '.success and .data.bookingsPosted == 1 and .data.bookingsSkipped == 0 and .data.labourAmount == 2400 and .data.overheadAmount == 8000 and .data.labourRateFromWorkCenter == 1 and .data.labourRateFromPayroll == 0 and (.data.runs|length) == 1 and .data.runs[0].postingDateText == "1405/05/12" and (.data.runs[0].number|startswith("MCR")) and .data.runs[0].labourHours == 4 and .data.runs[0].machineHours == 4 and .data.runs[0].accountingEventId > 0'
MBCR1=$(val '.data.runs[0].id'); MBCRN1=$(val '.data.runs[0].number')
as "$FM_TOKEN" POST $MF/cost/run '{"date":"2026-08-03T00:00:00"}'; check "mes B: the same day again posts nothing (idempotent per booking)" '.success and .data.bookingsPosted == 0 and (.data.runs|length) == 0 and .data.labourAmount == 0'
as "$MPM_TOKEN" POST $MF/cost/run '{"date":"2026-08-04T00:00:00"}'; check "mes B: the production manager may run it too: Tuesday 08-04 posts labour 6 h x 600 = 3600 and machine 6 h x 2000 = 12000" '.success and .data.bookingsPosted == 1 and .data.labourAmount == 3600 and .data.overheadAmount == 12000 and (.data.runs|length) == 1'
MBCR2=$(val '.data.runs[0].id')
as "$FM_TOKEN" GET "finance/costs?contractId=$MFCT"; check "mes B: the work order cost report shows labour 6000 and overhead 20000 (CostEntry rows)" ".success and ([.data[]|select(.workOrderId == $MFWO)|.labour]|add) == 6000 and ([.data[]|select(.workOrderId == $MFWO)|.overhead]|add) == 20000"
as "$FM_TOKEN" GET "finance/events?Contains=$MBCRN1&IncludeNotRequired=true&PageSize=10"; check "mes B: the accounting event of the day: type labour absorbed, Dr work in progress 10400 against Cr absorbed labour 2400 and absorbed overhead 8000" '.success and (.data.data|length) == 1 and (.data.data[0] | .type == 13 and .sourceType == "MfCostRun" and .amount == 10400 and ([.lines[]|select(.accountRole == "WIP")|.debit]|add) == 10400 and ([.lines[]|select(.accountRole == "LabourAbsorbed")|.credit]|add) == 2400 and ([.lines[]|select(.accountRole == "OverheadAbsorbed")|.credit]|add) == 8000 and ([.lines[]|.debit]|add) == ([.lines[]|.credit]|add) and ([.lines[]|select(.accountRole == "WIP")|.workOrderId]|unique) != [null])'
as "$FM_TOKEN" GET $FG/setup-check; check "mes B: the account roles of absorbed labour and overhead are mapped (no unmapped role problem)" '.success and ([.data.problems[]|select(.code|test("LabourAbsorbed|OverheadAbsorbed"))]|length) == 0'
as "$FM_TOKEN" GET "$MF/cost/postings?RunId=$MBCR1&PageSize=0"; check "mes B: the posting is one row per work order, work centre and day (never per person)" ".success and (.data.data|length) == 1 and (.data.data[0] | .workOrderId == $MFWO and .workCenterCode == \"MF-CNC\" and .bookingCount == 1 and .labourHours == 4 and .machineHours == 4 and .labourAmount == 2400 and .overheadAmount == 8000 and .postingDateText == \"1405/05/12\") and ([.. | objects | keys[] | select(test(\"[Ee]mployee|[Pp]erson|[Nn]ame\$\"))]|length) == 0"
as "$MPM_TOKEN" GET "$MF/cost/runs?PageSize=0"; check "mes B: the production manager reads the cost runs of both days" ".success and ([.data.data[]|select(.id == $MBCR1 or .id == $MBCR2)]|length) == 2"
as "$MSV_TOKEN" POST $MF/bookings/edit "{\"id\":$MBB1,\"endAt\":\"2026-08-03T13:00:00\",\"reason\":\"اصلاح ساعت پایان\"}"; nomsg "mes B: a booking whose cost is posted cannot be corrected any more" "در حسابداری ثبت شده"
as "$FM_TOKEN" GET "$FAU?EntityType=MfCostRun&EntityId=$MBCR1&PageSize=0"; check "mes B: the cost run is in the audit trail" '.success and (.data.data|length) >= 1'
as "$MOP_TOKEN" GET "$MF/work-centers/$MBWC"; check "mes B: the operator still sees no rate on the work centre" '.success and .data.machineRateRial == null and .data.labourRateRial == null'
as "$MOP_TOKEN" GET "$MF/terminal/my-operations?PageSize=0"; check "mes B: the operator terminal carries no cost, rate or wage field" '.success and ([.. | objects | keys[] | select(test("Rial$|[Rr]ate$|[Ww]age|[Ss]alary|[Ii]ban|[Nn]ationalCode|[Cc]ost"))]|length) == 0'

# --- maintenance plans: by calendar days and by running hours, drafted when due (7 days ahead), once
as "$MOP_TOKEN" POST $MF/maintenance/plans "{\"machineId\":$MBM,\"task\":\"x\",\"intervalKind\":1,\"intervalValue\":30}"; nomsg "mes B: the operator cannot write maintenance plans" "مجوز"
as "$MMT_TOKEN" POST $MF/maintenance/plans "{\"machineId\":$MBM,\"task\":\"x\",\"intervalKind\":1,\"intervalValue\":0}"; nomsg "mes B: a plan needs an interval above zero" "بیشتر از صفر"
as "$MPM_TOKEN" POST $MF/maintenance/plans "{\"machineId\":$MBM,\"task\":\"روغن‌کاری و بازرسی ماهانه\",\"intervalKind\":1,\"intervalValue\":30,\"startDate\":\"2026-07-10\",\"estimatedHours\":2,\"checklist\":\"سطح روغن، تسمه‌ها، فیلتر\"}"; check "mes B: the production manager saves a 30-day plan counted from 2026-07-10: next due 2026-08-09 (1405/05/18)" '.success and .data.id > 0 and .data.isActive == true and (.data.nextDueDate|startswith("2026-08-09")) and .data.nextDueDateText == "1405/05/18" and .data.openOrderId == null'
as "$MMT_TOKEN" POST $MF/maintenance/plans/draft-due '{"asOf":"2026-07-20T00:00:00"}'; check "mes B: on 07-20 the plan is not due yet (the lead is 7 days): nothing drafted" '.success and .data.plansChecked >= 1 and .data.drafted == 0'
as "$MMT_TOKEN" POST $MF/maintenance/plans/draft-due '{"asOf":"2026-08-03T00:00:00"}'; check "mes B: on 08-03 (6 days before the due date) the plan drafts its planned order, due 08-09" '.success and .data.drafted == 1 and .data.orders[0].kind == 1 and .data.orders[0].status == 0 and .data.orders[0].planId > 0 and (.data.orders[0].dueDate|startswith("2026-08-09")) and (.data.orders[0].number|startswith("MWO")) and .data.orders[0].estimatedHours == 2'
MBMW1=$(val '.data.orders[0].id')
as "$MMT_TOKEN" POST $MF/maintenance/plans/draft-due '{"asOf":"2026-08-03T00:00:00"}'; check "mes B: drafting again drafts nothing while the order is open" '.success and .data.drafted == 0'
ibx "$MMT_TOKEN" "mf:mwo:$MBMW1"; check "mes B: the planned order is in the inbox of maintenance" '.success and (.data.data|length) == 1'
as "$MMT_TOKEN" GET "$MF/maintenance/plans?MachineId=$MBM"; check "mes B: the plan shows its open order" ".success and (.data.data|length) == 1 and .data.data[0].openOrderId == $MBMW1"
as "$MMT_TOKEN" POST $MF/maintenance/plans "{\"machineId\":$MBM,\"task\":\"سرویس هر ۸ ساعت کار\",\"intervalKind\":2,\"intervalValue\":8,\"startDate\":\"2026-08-01\",\"estimatedHours\":1}"; check "mes B: a running-hours plan (every 8 h) counted from 08-01: the 10 booked hours of the machine are counted" '.success and .data.intervalKind == 2 and .data.runningHoursSince == 10'
MBPLAN2=$(val .data.id)
as "$MMT_TOKEN" POST $MF/maintenance/plans/draft-due '{}'; check "mes B: the running-hours plan is due (10 h against 8 h) and drafts its order; the day plan is not drafted twice" '.success and .data.drafted == 1 and .data.orders[0].planId == '"$MBPLAN2"
MBMW2=$(val '.data.orders[0].id')
as "$MMT_TOKEN" POST $MF/maintenance/orders/action "{\"id\":$MBMW2,\"action\":\"complete\"}"; nomsg "mes B: a planned order is closed only with the action taken" "اقدام انجام"
as "$MMT_TOKEN" POST $MF/maintenance/orders/save "{\"id\":$MBMW2,\"actionTaken\":\"سرویس انجام شد\"}"; check "mes B: the action taken is saved" '.success'
as "$MMT_TOKEN" POST $MF/maintenance/orders/action "{\"id\":$MBMW2,\"action\":\"complete\"}"; check "mes B: the planned order is completed (no cause is needed)" '.success and .data.status == 2'
as "$MMT_TOKEN" GET "$MF/maintenance/plans?MachineId=$MBM"; check "mes B: completing the order restarts the running-hours plan: 0 h since the service, last done today" ".success and ([.data.data[]|select(.id == $MBPLAN2)][0] | .runningHoursSince == 0 and .lastDoneAtText != null and .openOrderId == null)"
as "$MMT_TOKEN" GET "$MF/maintenance/orders?Kind=1&OpenOnly=true&PageSize=0"; check "mes B: the day-plan order is still open and overdue (due 08-09, long ago)" ".success and ([.data.data[]|select(.id == $MBMW1)][0] | .overdue == true and .status == 0)"

# --- dashboards: the production manager's dashboard on a chosen day (2026-08-07), then the CEO's with the real day
as "$MPM_TOKEN" GET "$MF/dashboard?AsOf=2026-08-07"; check "mes B: production dashboard on 08-07: 1 late order (D3, 9 days), no inspection waiting, no kit short, no machine down, no overdue maintenance" ".success and .data.asOfText == \"1405/05/16\" and .data.lateOrders == 1 and .data.lateOrderList[0].number == \"$MBN3\" and .data.lateOrderList[0].daysLate == 9 and .data.qcHoldOverTwoDays == 0 and .data.kitShortages == 0 and .data.machinesDown == 0 and .data.maintenanceOverdue == 0 and .data.lastScheduleRunText != null"
check "mes B: production dashboard: the month so far (from 1405/05/01) has 6 good and 1 scrapped piece (14.3 % scrap) and the CNC centre's OEE rows" '.data.monthGoodQty == 6 and .data.monthScrapQty == 1 and .data.scrapRatePercent == 14.3 and ([.data.oee[]|select(.workCenterCode == "MF-CNC")][0] | .plannedHours == 96 and .downtimeHours == 4 and .bookedRunHours == 10 and .goodQty == 6 and .availabilityPercent == 95.8 and .performancePercent == 70 and .qualityPercent == 85.7 and .oeePercent == 57.5)'
as "$MPM_TOKEN" GET "$MF/load?From=2026-08-07&Weeks=12"; MBOVL=$(val '.data.overloads|length')
as "$MPM_TOKEN" GET "$MF/dashboard?AsOf=2026-08-07"; check "mes B: production dashboard: the overload weeks equal the overloads of the load report of the same day" ".success and .data.overloadWeeks == $MBOVL and (.data.overloadList|length) == ($MBOVL | if . > 10 then 10 else . end)"
as "$MOP_TOKEN" GET "$MF/dashboard"; nomsg "mes B: the operator cannot open the production dashboard" "مجوز"
as "$MPM_TOKEN" GET "$MF/dashboard"; check "mes B: with the real day the day-plan order is overdue: 1 overdue maintenance order" '.success and .data.maintenanceOverdue == 1'

# a fourth order on the CNC centre (40 pieces, nobody has booked on it) and a final run on the real day, so the CEO figures are exact against the reports
mbdraft 7 40 4; MBO4=$MBO
mbrelease "$MBO4" D4
as "$MPM_TOKEN" POST $MF/schedule/run '{}'; check "mes B: scheduler run on the real day" '.success and .data.orders >= 5 and .data.unscheduled == 0'
MBLATE=$(val .data.lateOrders)
as "$MMT_TOKEN" POST $MF/maintenance/orders "{\"machineId\":$MBM,\"kind\":2,\"task\":\"توقف مجدد ماشین فرز\"}"; check "mes B: the CNC machine stops again (breakdown order without a given start: now)" '.success and .data.kind == 2 and .data.downtimeStart != null'
as "$MPM_TOKEN" GET "$MF/load?Weeks=4&Threshold=110"; MBOVL4=$(val '.data.overloads|length')
check "mes B: the load report of the next 4 weeks above 110 % has overloads (D3 and D4 stack up)" ".success and $MBOVL4 >= 1"
as "$CEO_TOKEN" GET finance/ceo-dashboard; check "mes B: CEO dashboard: late production orders as of the scheduler run, 1 machine down, overload weeks equal the load report, 1 overdue maintenance order" ".success and .data.productionLateOrders == $MBLATE and $MBLATE >= 1 and .data.productionMachinesDown == 1 and .data.productionOverloadWeeks == $MBOVL4 and .data.productionMaintenanceOverdue == 1"
check "mes B: CEO dashboard alerts: late orders, machine down (danger), overload above 110 %, overdue maintenance" '(.data.alerts|map(select(.link == "/production/orders" and .level == "warning"))|length) == 1 and (.data.alerts|map(select(.link == "/production/machines" and .level == "danger"))|length) == 1 and (.data.alerts|map(select(.link == "/production/load" and .level == "warning"))|length) == 1 and (.data.alerts|map(select(.link == "/production/maintenance-orders" and .level == "info"))|length) == 1'
as "$MPM_TOKEN" GET "$MF/dashboard"; check "mes B: production dashboard of the real day agrees with the CEO figures: 1 machine down (listed with its work centre)" ".success and .data.machinesDown == 1 and .data.machinesDownList[0].workCenterCode == \"MF-CNC\" and .data.lateOrders == $MBLATE"
TOKEN=$ADMIN_TOKEN
TOKEN=$ADMIN_TOKEN

# ---------------------------------------------------------------- roles of the new users
as "$SEC_TOKEN" POST inventory/documents "{\"type\":1,\"documentDate\":\"2026-10-15T00:00:00\",\"warehouseId\":$W1,\"lines\":[]}"; [ "$CODE" = "403" ] && ok "security user cannot write documents (403)" || bad "security user cannot write documents (403) got $CODE"
as "$PROD_TOKEN" POST inventory/material-requests "{\"requestDate\":\"2026-10-21T00:00:00\",\"purpose\":\"تعمیرات\",\"lines\":[{\"itemId\":$BOLT,\"quantity\":2}]}"; check "production user writes a material request" '.success'
TOKEN=$ADMIN_TOKEN

finished=1
echo "--- inventory: $pass passed, $fail failed" | tee -a "$R"
rm -f "$B"
kill "$APP_PID" 2>/dev/null; sleep 3; kill -9 "$APP_PID" 2>/dev/null || true
[ "$fail" -eq 0 ] && exit 0 || exit 1
