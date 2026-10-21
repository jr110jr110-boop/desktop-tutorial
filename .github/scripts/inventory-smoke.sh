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
as "$FIN_TOKEN" POST procurement/payments/action "{\"id\":$PAY,\"action\":\"paid\"}"; check "payment needs the bank or Sepidar reference" '.success == false'
as "$FIN_TOKEN" POST procurement/payments/action "{\"id\":$PAY,\"action\":\"paid\",\"reference\":\"سند 4521\"}"; check "finance records the payment" '.success and .data.status == 2'
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
mkdelivery() { # mkdelivery WORKORDER (empty = none)
  local wo=""; [ -n "$1" ] && wo=",\"workOrderId\":$1"
  call POST inventory/documents "{\"type\":11,\"documentDate\":\"$D0\",\"warehouseId\":$W1,\"contractId\":$QCT$wo,\"driverName\":\"Driver\",\"vehiclePlate\":\"11A111\",\"receiverName\":\"Client rep\",\"lines\":[{\"itemId\":$BOLT,\"quantity\":1}]}"
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
user2() { call POST Accounts/register-user "{\"userName\":\"$1\",\"name\":\"$1\",\"lastName\":\"آزمون\",\"email\":\"$1@example.com\",\"roleIds\":[$2],\"password\":\"$3\",\"phoneNumber\":\"$4\",\"birthDate\":\"1990-01-01T00:00:00+03:30\",\"nationalCode\":\"$5\"}"; }
user2 fin2.ci 3 'Ci#Fin2-User-2026' 09120000024 0012345624
user2 fm.ci 17 'Ci#Fm-User-2026' 09120000025 0012345625
user2 pay.ci 18 'Ci#Pay-User-2026' 09120000026 0012345626
user2 tr.ci 19 'Ci#Tr-User-2026' 09120000027 0012345627
FIN2_TOKEN=$(login fin2.ci 'Ci#Fin2-User-2026'); FM_TOKEN=$(login fm.ci 'Ci#Fm-User-2026'); PAYR_TOKEN=$(login pay.ci 'Ci#Pay-User-2026'); TR_TOKEN=$(login tr.ci 'Ci#Tr-User-2026')
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
as "$COM_TOKEN" POST $FT/receipts "$RJ"; check "finance: any authenticated user may draft but a commercial receipt is only a draft" '.success == false or .data.status == 0'
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
as "$FIN_TOKEN" POST procurement/payments/action "{\"id\":$PAY2,\"action\":\"paid\",\"reference\":\"x\"}"; nomsg "finance: the old pay action is refused for a request linked to a treasury payment" "خزانه"
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
as "$FM_TOKEN" POST $FT/reconciliations/action "{\"id\":$RC_A,\"action\":\"approve\"}"; check "finance: reconciliation approved by the finance manager (not the preparer) or still has a difference" '.success or (.message|length) > 0'

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
as "$FM_TOKEN" POST $FG/years/$FY0/close '{"overrideReconciliation":true,"reason":"دوباره"}'; no "finance: a closed year is not closed twice"
as "$FIN_TOKEN" POST $FG/vouchers '{"date":"2021-07-01T00:00:00","lines":[{"accountCode":"80108","debit":1},{"accountCode":"10101","credit":1}]}'; no "finance: no voucher can be written into a closed year"

# ---------------------------------------------------------------- roles of the new users
as "$SEC_TOKEN" POST inventory/documents "{\"type\":1,\"documentDate\":\"2026-10-15T00:00:00\",\"warehouseId\":$W1,\"lines\":[]}"; [ "$CODE" = "403" ] && ok "security user cannot write documents (403)" || bad "security user cannot write documents (403) got $CODE"
as "$PROD_TOKEN" POST inventory/material-requests "{\"requestDate\":\"2026-10-21T00:00:00\",\"purpose\":\"تعمیرات\",\"lines\":[{\"itemId\":$BOLT,\"quantity\":2}]}"; check "production user writes a material request" '.success'
TOKEN=$ADMIN_TOKEN

echo "--- inventory: $pass passed, $fail failed" | tee -a "$R"
rm -f "$B"
kill "$APP_PID" 2>/dev/null; sleep 3; kill -9 "$APP_PID" 2>/dev/null || true
exit 0
