# ENG-F1: the engineering inbox block for the inventory smoke. UNVERIFIED: it was only syntax-checked (bash -n), never run against a started API.
#
# How to use: source it from the smoke (or paste it before the summary lines) AFTER the users and tokens exist. It uses what inventory-smoke.sh defines:
# call, as, check, nomsg, r403, ok, bad, $B, $CODE, $API, $OUT and the tokens $ADMIN_TOKEN (PMO), $ENG_TOKEN (ENGINEERING), $QC_TOKEN, $CEO_TOKEN, $COM_TOKEN.
# The smoke user of $ENG_TOKEN must have the login name eng.ci (the smoke registers it); the sample package is given that login as its preparer.
#
# The folder: the smoke starts the API with the working directory src/WebApi and sets no Engineering__InboxFolder, so the default folder is used:
# the sibling of the application folder, i.e. src/Engineering-Inbox. That folder is created by the program and is writable in CI, so no edit of inventory-smoke.sh is needed.
# ONE optional environment variable: ENGINBOX_DIR = the inbox folder, when the API was started with another Engineering__InboxFolder (default: $PWD/src/Engineering-Inbox).
# Needs python3 (rewrites the preparer inside the sample workbook) and curl. The package must be stable for 20 seconds (Engineering:StableSeconds), so the flow sleeps 22 seconds between scans.
# The ingest agent must not scan by itself meanwhile (the CI starts the API with Agents__Jobs=false; with the agent running the exact counts below can move to its run).

EI=engineering/inbox
ENGBASE="${ENGINBOX_DIR:-$PWD/src/Engineering-Inbox}"
ENGPKG="$ENGBASE/Platforms/TEST-PLT-01/API 610"

# ---- roles and the status
as "$ENG_TOKEN" GET $EI/status
check "eng-inbox: the engineering unit reads the status: a configured folder source, a count for each of the six statuses and the scan button allowed" '.success and .data.sourceName == "Folder" and .data.isConfigured == true and (.data.counts|length) == 6 and .data.canScan == true and .data.canDismiss == true'
as "$ADMIN_TOKEN" GET $EI/status
check "eng-inbox: the PMO may read and scan" '.success and .data.canScan == true'
as "$COM_TOKEN" GET $EI/status
r403 "eng-inbox: a user without an engineering role cannot read the status (403)"
for who in QC CEO COM; do
  eval "t=\$${who}_TOKEN"
  as "$t" POST $EI/scan
  r403 "eng-inbox: the $who role cannot start a scan (403)"
  as "$t" POST $EI/packages/1/dismiss '{"reason":"علت کافی برای کنار گذاشتن بسته","rowVersion":"AAAA"}'
  r403 "eng-inbox: the $who role cannot dismiss a package (403)"
done

# ---- the list and the refusals of dismiss
as "$ENG_TOKEN" GET "$EI/packages?PageNumber=1&PageSize=10"
check "eng-inbox: the package list is a paged answer (an array in data.data)" '.success and (.data.data|type) == "array"'
as "$ENG_TOKEN" GET "$EI/packages/0"
nomsg "eng-inbox: an unknown package answers «بسته پیدا نشد.»" "بسته پیدا نشد."
as "$ENG_TOKEN" POST $EI/packages/1/dismiss '{"reason":"کوتاه","rowVersion":"AAAA"}'
nomsg "eng-inbox: a reason shorter than 15 characters is refused in Persian" "علت را دست‌کم ۱۵ نویسه بنویسید."
as "$ENG_TOKEN" POST $EI/packages/1/dismiss '{"reason":"علت کافی برای کنار گذاشتن بسته","rowVersion":""}'
nomsg "eng-inbox: dismiss without a RowVersion is refused in Persian" "نسخهٔ ردیف (RowVersion) بسته لازم است"
as "$ENG_TOKEN" POST $EI/packages/999999/dismiss '{"reason":"علت کافی برای کنار گذاشتن بسته","rowVersion":"AAAA"}'
nomsg "eng-inbox: dismissing an unknown package answers «بسته پیدا نشد.»" "بسته پیدا نشد."

# ---- the template and the sample are files
for f in template sample; do
  CODE=$(curl -s -o "$OUT/eng-$f.bin" -w '%{http_code}' "$API/$EI/$f" -H "Authorization: Bearer $ENG_TOKEN")
  if [ "$CODE" = "200" ] && [ "$(head -c 2 "$OUT/eng-$f.bin")" = "PK" ]; then ok "eng-inbox: the $f is a zip-based file (xlsx or zip)"; else bad "eng-inbox: the $f is a zip-based file (xlsx or zip)"; fi
done

# ---- a real flow through the folder: drop, scan (not stable yet), scan (ingested), re-drop (duplicate), corrupt copy (rejected)
eng_drop() { # eng_drop FOLDERNAME [corrupt]
  python3 - "$OUT/eng-sample.bin" "$ENGPKG/$1" "${2:-}" <<'PY'
import sys, zipfile, os, io
src, dest, corrupt = sys.argv[1], sys.argv[2], sys.argv[3]
os.makedirs(dest, exist_ok=True)
with zipfile.ZipFile(src) as z:
    for info in z.infolist():
        if info.is_dir():
            continue
        name = os.path.basename(info.filename)
        data = z.read(info)
        if name.lower().endswith(".xlsx"):
            if corrupt:
                data = data[:80]
            else:
                zin = zipfile.ZipFile(io.BytesIO(data))
                out = io.BytesIO()
                with zipfile.ZipFile(out, "w", zipfile.ZIP_DEFLATED) as zout:
                    for item in zin.infolist():
                        content = zin.read(item)
                        if item.filename.endswith(".xml"):
                            content = content.replace(b"eng.sample", b"eng.ci")
                        zout.writestr(item.filename, content)
                data = out.getvalue()
        with open(os.path.join(dest, name), "wb") as f:
            f.write(data)
PY
}
CODE=$(curl -s -o "$OUT/eng-sample.bin" -w '%{http_code}' "$API/$EI/sample" -H "Authorization: Bearer $ENG_TOKEN")
eng_drop smoke-v1
as "$ENG_TOKEN" POST $EI/scan
check "eng-inbox: the first scan sees the package but it is not stable yet (exactly 0 ingested, 0 rejected, 0 duplicates)" '.success and .data.notConfigured == false and .data.ingested == 0 and .data.rejected == 0 and .data.duplicates == 0'
sleep 22
as "$ENG_TOKEN" POST $EI/scan
check "eng-inbox: the second scan ingests exactly 1 package (0 rejected, 0 duplicates, 0 errors)" '.success and .data.found == 1 and .data.ingested == 1 and .data.rejected == 0 and .data.duplicates == 0 and .data.errors == 0'
as "$ENG_TOKEN" GET "$EI/packages?Status=1&Search=TEST-PLT-01"
check "eng-inbox: the ingested package is listed with its exact line counts (12 BOM, 13 OPC, 5 documents)" '.success and (.data.data|length) == 1 and .data.data[0].platformCode == "TEST-PLT-01" and .data.data[0].bomCount == 12 and .data.data[0].opcCount == 13 and .data.data[0].docCount == 5 and .data.data[0].status == 1'
eng_drop smoke-v1-again
as "$ENG_TOKEN" POST $EI/scan
sleep 22
as "$ENG_TOKEN" POST $EI/scan
check "eng-inbox: the same package dropped again is exactly 1 duplicate and no new package" '.success and .data.found == 1 and .data.ingested == 0 and .data.rejected == 0 and .data.duplicates == 1 and .data.errors == 0'
as "$ENG_TOKEN" GET "$EI/packages?Search=TEST-PLT-01"
check "eng-inbox: still exactly one package of the platform after the duplicate" '.success and (.data.data|length) == 1'
eng_drop smoke-corrupt corrupt
as "$ENG_TOKEN" POST $EI/scan
sleep 22
as "$ENG_TOKEN" POST $EI/scan
check "eng-inbox: a corrupt copy is exactly 1 rejected package" '.success and .data.found == 1 and .data.rejected == 1 and .data.ingested == 0 and .data.errors == 0'
as "$ENG_TOKEN" GET "$EI/packages?Status=2"
check "eng-inbox: the rejected package is stored once with its findings counted" '.success and (.data.data|length) == 1 and .data.data[0].status == 2 and .data.data[0].errorCount == 1'

# ---- the catalogue
as "$ADMIN_TOKEN" GET agents/steps
check "eng-inbox: the catalogue lists exactly the three inbox steps" '[.data[]? | select(.code|startswith("ENG.INBOX."))] | map(.code) | sort == ["ENG.INBOX.APPLY","ENG.INBOX.FIX","ENG.INBOX.INGEST"]'
