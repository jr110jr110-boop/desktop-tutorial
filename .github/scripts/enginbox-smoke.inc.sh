# ENG-F1: the engineering inbox block for the inventory smoke. UNVERIFIED: it was only syntax-checked (bash -n), never run against a started API.
# It is meant to be sourced by .github/scripts/inventory-smoke.sh (or pasted before its summary lines) and uses what that script defines:
# call/as/check/val/ok/bad, $B, $API, $ADMIN_TOKEN (PMO), $ENG_TOKEN (ENGINEERING), $COM_TOKEN (a user without an engineering role).
# The server folder is not set by the smoke (Engineering:InboxFolder defaults to a folder beside the application), so the block asserts the API contract, the roles and the template/sample downloads,
# not a package that arrived through the folder.
EI=engineering/inbox

as "$ENG_TOKEN" GET $EI/status
check "eng-inbox: the engineering unit reads the status (source, folder and a count for every status)" '.success and (.data.counts|length) == 6 and (.data.location|type) == "string" and .data.canScan == true'
as "$ADMIN_TOKEN" GET $EI/status
check "eng-inbox: the PMO reads the status and may scan" '.success and .data.canScan == true'
as "$COM_TOKEN" GET $EI/status
[ "$CODE" = "403" ] && ok "eng-inbox: a user without an engineering or planning role cannot read the status (403)" || bad "eng-inbox: a user without an engineering or planning role cannot read the status (403)"

as "$ENG_TOKEN" GET "$EI/packages?PageNumber=1&PageSize=10"
check "eng-inbox: the package list is a paged answer" '.success != false and (.data|type) == "array"'
as "$ENG_TOKEN" GET "$EI/packages/0"
check "eng-inbox: an unknown package is a Persian refusal, not an error" '.success == false and (.message|length) > 0'

as "$ENG_TOKEN" POST $EI/scan
check "eng-inbox: a scan answers with its counts or a Persian note, never an error" '.success and (.data.found|type) == "number"'
as "$COM_TOKEN" POST $EI/scan
[ "$CODE" = "403" ] && ok "eng-inbox: a user without an engineering role cannot start a scan (403)" || bad "eng-inbox: a user without an engineering role cannot start a scan (403)"

as "$ENG_TOKEN" POST $EI/packages/0/dismiss '{"reason":"کوتاه","rowVersion":""}'
check "eng-inbox: dismissing without a real reason or an existing package is refused in Persian" '.success == false and (.message|length) > 0'

# the template and the sample are files
for f in template sample; do
  CODE=$(curl -s -o "$B" -w '%{http_code}' "$API/$EI/$f" -H "Authorization: Bearer $ENG_TOKEN")
  if [ "$CODE" = "200" ] && [ "$(head -c 2 "$B")" = "PK" ]; then ok "eng-inbox: the $f is a zip-based file (xlsx or zip)"; else bad "eng-inbox: the $f is a zip-based file (xlsx or zip)"; fi
done

# the agent steps are in the catalogue
as "$ADMIN_TOKEN" GET agents/steps
check "eng-inbox: the catalogue lists the three inbox steps (ingest and apply by the engineering agent, fix as an input task)" '[.data[]? | select(.code|startswith("ENG.INBOX."))] | length == 3'
