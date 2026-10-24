# PMIS (INN ORCHESTRA) — handoff for the next account

Date: 1405/08/02. Written by the orchestrating Claude session that built the phases below. Read this file first, then `docs/`.

## 1. Where everything is

All in the user's private GitHub repo `jr110jr110-boop/desktop-tutorial`:

| Branch | Contents |
|---|---|
| `innco-net10-build` | **Backend source of truth** (ASP.NET Core .NET 10, EF Core 10, SQL Server). CI runs only on pushes to this branch. |
| `innco-front` | **Front source** (Vue 3 + Vite + TS). Was a local-only repo `phase2-front`; pushed here at commit f3e664c. The built copy used by the install package lives in the backend branch under `.github/ci-frontend-dist/`. |
| `innco-handoff` | This file + all design briefs, reviews and reports (`docs/`) + helper scripts (`tools/`). |
| `innco-ci-smoke-2022`, `innco-ci-smoke-2025-express` | CI smoke results (`smoke-output/7-inventory.txt`, failures with bodies in `7-inventory-fail.txt`). |
| `innco-ci-install` | Windows IIS + SQL Express install test results (`install-output/checks-1-first.txt`, `checks-2-update.txt`, `checks-3-restore.txt`). |
| `innco-ci-results` | Generated EF migrations (`ci-output/migration/*`). |
| `innco-package` | Last built server package (`package-output/`). |
| `innco-nuget-cache` / `innco-nuget-pkgs` | Workflow + split tarball of the NuGet cache for offline local builds (see 4). |

The user-facing specs that lived in the old Claude Project (`claude/*.md`) are copied into `project-docs/` in this branch (strategy, warehouse, procurement, processes/data review, quality spec + status, project-control spec, finance spec, remaining-phases plan, .NET 10 upgrade guide). The old "codebase overview" is not copied — the repo itself and `project-docs/upgrade-net10.md` replace it. `docs/` holds the developer briefs, reviews and reports.

## 2. State at handoff

| Phase | Backend | Front | Notes |
|---|---|---|---|
| 0 .NET 10 upgrade, install package | done | done | `deploy/install.ps1`, nightly backup |
| 1 Warehouse | done | done | waiting: user approval of warehouses / 85 data decisions |
| 2 Procurement + cost accounting + CEO dashboard | done | done | |
| 3 Project control | done | done | |
| 4 Quality QC/QA | done | done | |
| Finance F1, F2, F3, F3b + reviews R1, R2, R3 | **done, green** at 7865e2d (1530/0 both SQL, install 0 FAIL x3) | done (f3e664c) | test kit delivered to user (PMIS_Finance_Test.zip). **One company, rial only** (see 5). |
| Office automation O1 | built; review-1 fixes pushed as ae0ebcc | **not started** | last verified green: 97f4c54 (1725/0). ae0ebcc result: see section 7 (fill in). |
| Production P1, Sales & Service S1, Local AI A1 | briefs ready (`docs/*-backend-brief.md`) | — | not started |
| Engineering module | waits for the user's Claude-team organisation | — | |
| Final: server hardware BOM | — | — | after all phases (PMIS + local AI + voice + backup, UPS) |

## 3. CI loop (binding discipline)

- Push to `innco-net10-build` → jobs: build → smoke (SQL 2022, SQL 2025 Express) → package → install-test (Windows IIS + SQL Express: first install, update, restore).
- **Green = both** `--- inventory: N passed, 0 failed` lines (one per SQL branch) **and** 0 lines matching `^FAIL` in the three install files (strip NULs: `tr -d '\0'`). Job "success" alone is not enough (read the files).
- Fetch result branches with explicit refspec: `git fetch origin '+innco-ci-smoke-2022:refs/remotes/origin/innco-ci-smoke-2022'` (same for the others).
- One run at a time: workflow `concurrency: innco-build, cancel-in-progress` — **a push cancels the running run**. Never push while a run is going; batch fixes. Never force-push.
- Migrations: write the name into `.github/migration-name.txt`, push; CI generates the migration into `innco-ci-results` `ci-output/migration/*`; copy those files into `src/DataLayer/Migrations`, set the name file back to `Net10ModelCheck`, push. Never edit an existing migration.
- Smoke script: `.github/scripts/inventory-smoke.sh` (runs with `set -u`; EXIT trap prints the summary). Every fix needs an exact-number check that fails on the old behaviour; no vacuous checks (no `>= 0`, no comparing the service with itself). Before pushing: `bash -n` + `shellcheck -S warning` (look for SC2154).

## 4. Local build (no .NET download allowed in the Claude sandbox)

- SDK: `apt-get install -y dotnet-sdk-10.0` (Ubuntu package works through the proxy).
- NuGet is blocked; restore from the cache branch: `git clone --depth 1 --branch innco-nuget-pkgs <repo> p && mkdir -p ~/.nuget/packages ~/.nuget/emptysrc && cat p/nuget-packages.tgz.part-* | tar -xzf - -C ~/.nuget/packages`
- Build: `dotnet build Base.sln --source ~/.nuget/emptysrc -v q -nologo` → must print "Build succeeded". If packages change, push to `innco-nuget-cache` to regenerate (does not run the main CI).
- No SQL Server locally (Docker/Microsoft repos blocked): smoke and migrations only in CI.
- Front: `npm`/`npx vite build` work locally; `npx vue-tsc --noEmit` has 5 legacy errors elsewhere — check only your files.

## 5. Binding user decisions (do not change without asking)

- Everything of the company runs in PMIS, offline-capable. Only external links: **Sepidar** (receives only tax-office and social-insurance data; fallback = full voucher export to Sepidar) and **Claude seats in engineering** (may pause when internet is down).
- Humans only at control gates (review/approve system drafts) and physical work; dashboards/reports read-only.
- **Persian (Shamsi) dates only** in anything a user reads (screens, files, exports, file names, audit text, letter numbers). API JSON dates stay ISO (front converts). Gregorian is deliberately not offered now (sensitive); keep the seam (`IFiscalCalendar`) so it can be added later.
- **One company, rial only** (1405/08/02): flags `Finance:MultiEntity` and `Finance:ForeignCurrency` default **false** (`FinanceFeatures` in `F3Common.cs`); front `src/utils/features.ts`. Tables/columns stay. The user will say later how to start the second company.
- Payroll: month basis always 30 days (29/31-day months paid as 30); employee 7% social insurance fully deducted from taxable base; part months from HireDate/EndDate.
- Maker-checker everywhere: creator and every editor excluded from check/approve/finalise. Payroll personal data only PAYROLL, FINANCEMANAGER, CEO.
- Office: **the secretariat does not see confidential/secret letters — it only registers them** (metadata only: number, date, kind, classification, parties, delivery).
- No invented statutory rates: rate tables with Source/Verify flags; missing rates fail with a clear Persian message.
- Printouts: Persian B Zar font, RTL, A4.
- Privacy/safety: no people's names in seed/test data (job titles only); never store/copy secrets; the admin password in packages must be the placeholder `SET-BY-INSTALL-ps1` (install.ps1 sets the real one) — **check `app/appsettings.json` line 10 of every package before delivery**. Never use the password PDFs the user has.
- Cost: cheapest route without loss of accuracy — Sonnet sub-agents for bulk work, Opus for design and independent reviews; verify every result yourself; batch pushes.
- Get user approval before each new phase (user has repeatedly said "continue").

## 6. Roles

PMO 1, COMMERCIAL 2, FINANCIAL 3, ENGINEERING 4, QC 5, WAREHOUSE 6, ASSEMBLER 7, ENGINEER 8, CONSTRUCTION 9, SECURITY 10, PLANNING 11, WHSUPERVISOR 12, CEO 13, QA 14, PROJECTMANAGER 15, DCC 16, FINANCEMANAGER 17, PAYROLL 18, TREASURY 19, (planned) PRODUCTIONMANAGER 20, SHOPSUPERVISOR 21, OPERATOR 22, MAINTENANCE 23, SALES 24, SERVICE 25, SECRETARIAT 26, HR 27, AUDITOR 28 (read-only).

## 7. Next steps, in order

1. **Office R1**: read the CI result of ae0ebcc (see table below). If not green, fix from `7-inventory-fail.txt`. Open items: remove unused `Counts` helper in `LeaveService.cs`; check possible duplicate audit rows for numbering rules (`docs/office-o1-report.md`, section "R1 status").
2. Office front pages (endpoint list in `docs/office-o1-report.md`), then a Persian test kit (install package + guide + feedback workbook, same style as the finance kit; build scripts in `tools/finbuild`).
3. Production P1 → Sales & Service S1 → Local AI A1 (mock LLM in CI) per the briefs in `docs/`, each: backend → independent Opus review → fixes → front → test kit.
4. Engineering module after the user's Claude-team organisation.
5. Server hardware BOM.

ae0ebcc CI result at handoff (read by the orchestrator, run 40118232662): **NOT green — 1863 passed, 3 failed on both SQL versions; install 0 FAIL x3.** The working tree is clean and equals origin (nothing half-committed). Failures (bodies in `smoke-output/7-inventory-fail.txt` on the smoke branches):
- `office r1 C1: the stored bytes of the attachment are unchanged after the attempts (file '')` and `... after the bytes of the attachment are swapped on disk verify fails and names the file` — the smoke script cannot find the uploaded file on the CI disk (`file ''`; path lookup `src/WebApi/UploadedDocuments/<id>*` still wrong — check where `DocumentService` writes on the CI runner, e.g. the published content root). The C1 server fix itself (auth on download, owner check on change-file, SHA-256 of attachment bytes in the letter hash) is therefore **not yet proven** — prove it before any office test kit.
- `office r1 M5: so the voucher gate is closed` — the "finalise vouchers" inbox gate item is still not closed when nothing is left to finalise; read the body and the sweep added in ae0ebcc (finance `EventPostingService.cs` / `GlService` + `OrgInboxService.cs`).
Last fully green commits: **7865e2d** (finance complete) and **97f4c54** (office O1 before review fixes). Do not deliver a package built from ae0ebcc.

## 8. Waiting on the user / company

Finance: 16 accountant questions (`docs/finance-r3-report.md` §2; also in the test kit workbook), feedback on the finance test kit, which system prints the legal books. Office: real letter-number format (default `{yy}/{kind}/{unit}/{seq:5}`, kind letters و/ص/د), attendance device model/format (an earlier note says US10C by Donyaye Pardazesh, TCP/IP + RS485 — confirm), Rahkaran archive export format, attendance policy values. Earlier phases: warehouse list and 85 data decisions, procurement form codes, quality/ITP limits, project-control open questions (`claude/project-control-spec.md` §7). Hardware for local AI/voice (to be settled by the BOM).

## 9. Files in this branch

- `docs/` — binding backend briefs (finance, F3, office, production, sales-service, AI, quality, project control), independent reviews (finance 1–3, office 1), reports (finance R3, office O1).
- `tools/finbuild` — Python/Node scripts that build the Persian test guide PDF (Playwright + B Zar) and the feedback workbook (openpyxl); `tools/mc3` — front mock click-through (esbuild bundle + mock API) used to check every screen per role without a backend; `tools/guide3` — earlier guide template.
