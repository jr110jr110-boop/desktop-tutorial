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

# ---------------------------------------------------------------- master data
call POST inventory/groups '{"code":"TRB","title":"توربین بخار"}'; check "group create" '.success and .data.id > 0'
G1=$(val .data.id)
call POST inventory/groups "{\"code\":\"TRB-ROT\",\"title\":\"روتور\",\"parentId\":$G1}"; check "sub-group create" ".success and .data.parentId == $G1"
G2=$(val .data.id)
call POST inventory/groups "{\"id\":$G1,\"code\":\"TRB\",\"title\":\"توربین\",\"parentId\":$G2}"; check "group cycle rejected" '.success == false'

item() { call POST inventory/items "$1"; check "item $2" '.success and .data.id > 0'; val .data.id; }
RAW=$(item "{\"code\":\"RM-001\",\"title\":\"میلگرد فورج 26NiCrMoV\",\"itemGroupId\":$G2,\"unit\":\"kg\",\"trackingMode\":1,\"material\":\"26NiCrMoV14-5\",\"minStock\":100,\"sepidarCode\":\"5001\"}" "RM-001 (lot)")
SHAFT=$(item "{\"code\":\"SH-100\",\"title\":\"شفت روتور\",\"itemGroupId\":$G2,\"unit\":\"عدد\",\"trackingMode\":2,\"technicalCode\":\"DWG-SH-100\"}" "SH-100 (serial)")
BOLT=$(item "{\"code\":\"BT-300\",\"title\":\"پیچ M24\",\"unit\":\"عدد\",\"trackingMode\":0,\"minStock\":500}" "BT-300 (none)")
ROTOR=$(item "{\"code\":\"RT-900\",\"title\":\"روتور کامل\",\"itemGroupId\":$G1,\"unit\":\"عدد\",\"trackingMode\":2}" "RT-900 (serial)")
call POST inventory/items '{"code":"RM-001","title":"تکراری","unit":"kg"}'; check "duplicate item code rejected" '.success == false'
call GET "inventory/items/by-code?code=sh-100"; check "item by code (case-insensitive)" ".data.id == $SHAFT"

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
call POST inventory/documents "{\"type\":4,\"documentDate\":\"2026-10-15T00:00:00\",\"warehouseId\":$W1,\"lines\":[{\"itemId\":$BOLT,\"quantity\":1000}]}"
D=$(val .data.id); call POST "inventory/documents/$D/confirm"; check "issue beyond stock rejected" '.success == false and (.message|length) > 0'
call DELETE "inventory/documents/$D"

# ---------------------------------------------------------------- transfer
call POST inventory/documents "{\"type\":6,\"documentDate\":\"2026-10-15T00:00:00\",\"warehouseId\":$W1,\"destinationWarehouseId\":$W2,\"lines\":[{\"itemId\":$SHAFT,\"quantity\":1,\"serialNumbers\":[\"S-1\"],\"destinationLocationId\":$L3},{\"itemId\":$BOLT,\"quantity\":40,\"destinationLocationId\":$L3}]}"
TR=$(val .data.id)
call POST "inventory/documents/$TR/confirm"; check "transfer confirmed" '.success and (.data.number|startswith("TR-"))'
call GET "inventory/stock/serials?Contains=S-1&ItemId=$SHAFT"; check "S-1 now in WH-WIP / M-01" ".data.data[0].warehouseId == $W2 and .data.data[0].locationCode == \"M-01\""
call GET "inventory/stock?ItemId=$BOLT"; check "bolts split 60 / 40" '[.data.data[]|.quantity]|sort == [40,60]'

# ---------------------------------------------------------------- issue to production
call POST inventory/documents "{\"type\":4,\"documentDate\":\"2026-10-16T00:00:00\",\"warehouseId\":$W1,\"lines\":[{\"itemId\":$RAW,\"quantity\":100,\"lotId\":$LOT},{\"itemId\":$SHAFT,\"quantity\":1,\"serialNumbers\":[\"S-2\"]}]}"
IS=$(val .data.id)
call POST "inventory/documents/$IS/confirm"; check "issue confirmed" '.success and (.data.number|startswith("IS-"))'
call GET "inventory/stock/items?ItemId=$RAW"; check "stock RM-001 = 400.5" '.data.data[0].onHand == 400.5'
call GET "inventory/stock/serials?Contains=S-2&ItemId=$SHAFT"; check "S-2 in production" '.data.data[0].status == 1'
call POST inventory/documents "{\"type\":4,\"documentDate\":\"2026-10-16T00:00:00\",\"warehouseId\":$W1,\"lines\":[{\"itemId\":$RAW,\"quantity\":1}]}"
D=$(val .data.id); call POST "inventory/documents/$D/confirm"; check "lot item issue without lot rejected" '.success == false'
call DELETE "inventory/documents/$D"
call GET "inventory/stock/kardex?ItemId=$RAW"; check "kardex RM-001: 2 rows, closing 400.5" '(.data.rows|length) == 2 and .data.closingBalance == 400.5 and .data.rows[1].balance == 400.5'
call GET "inventory/stock/items?LowStockOnly=true"; check "low-stock list has BT-300 (100 < 500)" '[.data.data[]|.itemCode]|index("BT-300") != null'

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
call POST inventory/trace/serials "{\"itemId\":$ROTOR,\"applyRouting\":true}"; check "rotor serial created (auto number)" '.success and (.data.serialNumber|startswith("SN-")) and .data.status == 1'
RT=$(val .data.id)
call POST inventory/trace/assembly/install "{\"parentSerialUnitId\":$RT,\"childSerialUnitId\":$S1}"; check "install of a serial still in stock rejected" '.success == false'
call POST inventory/trace/assembly/install "{\"parentSerialUnitId\":$RT,\"childSerialUnitId\":$S2}"; check "S-2 installed in rotor" '.success'
LINK=$(val .data.id)
call POST inventory/trace/assembly/install "{\"parentSerialUnitId\":$S2,\"childSerialUnitId\":$RT}"; check "assembly cycle rejected" '.success == false'
call POST inventory/trace/assembly/install "{\"parentSerialUnitId\":$RT,\"childLotId\":$LOT,\"quantity\":5}"; check "lot material installed in rotor" '.success'
call GET "inventory/trace/serial/$RT"; check "rotor trace: child S-2 + lot H-7781" '(.data.children|map(.serialNumber)|index("S-2")) != null and (.data.lotComponents|map(.childLotNumber)|index("H-7781")) != null'
call GET "inventory/trace/serial/$S2"; check "S-2 trace: parent rotor, 2 operations, receipt + issue moves" ".data.unit.status == 3 and .data.ancestors[0].id == $RT and (.data.operations|length) == 2 and (.data.movements|length) >= 2"
call GET "inventory/trace/lot/$LOT"; check "lot trace: used in rotor (where-used)" "(.data.topAssemblies|map(.id)|index($RT)) != null and (.data.movements|length) >= 2"
call GET "inventory/trace/search?q=S/SH-100/S-2"; check "scan label S/SH-100/S-2" ".data.serials[0].id == $S2"
call GET "inventory/trace/search?q=L/RM-001/H-7781"; check "scan label L/RM-001/H-7781" ".data.lots[0].id == $LOT"
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
call POST inventory/items "{\"id\":$BOLT,\"code\":\"BT-300\",\"title\":\"پیچ M24\",\"unit\":\"عدد\",\"trackingMode\":1}"; check "tracking mode locked after movements" '.success == false'

# ---------------------------------------------------------------- Excel import
call POST inventory/items/import '[{"code":"BL-200","title":"پره ردیف 1","groupCode":"TRB-BLD","groupTitle":"پره‌ها","unit":"عدد","trackingMode":"سریال"},{"code":"RM-001","title":"میلگرد فورج (به‌روز)","unit":"kg","trackingMode":"بچ"}]'
check "import: 1 new, 1 updated, 1 group" '.success and .data.created == 1 and .data.updated == 1 and .data.groupsCreated == 1'

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
TOKEN=$(login fin.ci 'Ci#Fin-User-2026')
if [ -n "$TOKEN" ]; then
  ok "login financial user"
  call POST inventory/documents "{\"type\":1,\"documentDate\":\"2026-10-15T00:00:00\",\"warehouseId\":$W1,\"lines\":[]}"; [ "$CODE" = "403" ] && ok "financial user cannot write documents (403)" || bad "financial user cannot write documents (403) got $CODE"
  call GET inventory/sepidar/check; [ "$CODE" = "200" ] && ok "financial user can use Sepidar page" || bad "financial user can use Sepidar page got $CODE"
  call GET "inventory/stock/items"; [ "$CODE" = "200" ] && ok "financial user can read stock" || bad "financial user can read stock got $CODE"
else
  bad "login financial user"
fi
TOKEN=$ADMIN_TOKEN

echo "--- inventory: $pass passed, $fail failed" | tee -a "$R"
rm -f "$B"
kill "$APP_PID" 2>/dev/null; sleep 3; kill -9 "$APP_PID" 2>/dev/null || true
exit 0
