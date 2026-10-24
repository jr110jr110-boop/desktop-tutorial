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
| `innco-p1-production` | Production P1 work in progress (backend parts A+B), rebased on `innco-net10-build`; merged into it once the migration `ProductionMes` is generated. Not built by CI. |
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
| Office automation O1 | **done, green** (R1 at f581787; review of the front: allowedActions on decisions/delegations, referral canCancel, MineOnly, closed-month sheet) | **done** (front f24a643 + 950d477, packaged at 58f4599: 1880/0 both SQL, install 0 FAIL x3) | test kit delivered (PMIS_Office_Test.zip, 106 tests) |
| Production P1 | part A done + independent review fixed; part B in progress (branch `innco-p1-production`) | — | migration `ProductionMes` not generated yet |
| Sales & Service S1, Local AI A1 | briefs ready (`docs/*-backend-brief.md`) | — | not started |
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
- Network (new account, 1405/08/02): with the user's claude.ai setting Capabilities > Domain allowlist = All domains, `apt-get`, NuGet, npm and pip work. apt sources must be https (`sed -i 's#http://#https://#' /etc/apt/sources.list.d/ubuntu.sources`; disable docker.list). npm and pip need the proxy env not bypassed: `export npm_config_noproxy=localhost NO_PROXY=localhost,127.0.0.1 no_proxy=localhost,127.0.0.1`. `apt-get install -y dotnet-sdk-10.0 shellcheck`.
- shellcheck runs out of memory (7 GB) on the whole smoke script now: check a temp file of lines 1-95 + the changed section with `-e SC2154,SC2034` and look for variables used before assignment separately.
- Package build of the front: `.env.production.local` with `VITE_API_URL=/` (git-ignored) before `npx vite build`, then copy `dist` to the backend `.github/ci-frontend-dist`.
- Admin password: the repo `src/WebApi/appsettings.json` and every package carry `SET-BY-INSTALL-ps1`; CI injects its test password by the env var `AdminUserSeed__Password`; the package job fails if a password slips in (commit bc79b58).
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

1. **Office O1: DONE** (backend R1 + front + test kit). Also fixed on the way: every audit row was written twice (e219bf6, triggers ran in both SaveChanges overloads); uploaded files named after the document id (C1). Front tools: `tools/mc3o` (jsdom click-through of all office routes per role with mocks generated from the C# DTOs; build the bundle with `npx vite build --config tools/mc3o/vite.chk.config.mjs` from the front folder) and `tools/officebuild` (office test kit: guide PDF, data, feedback workbook).
2. **Production P1** (branch `innco-p1-production`): part A (master data, routings with two-person approval, production orders, release gate, operations, terminal, time booking, attendance difference) built, independently reviewed (Opus, 13 findings incl. one critical: orders were never added to the context) and fixed; part B (cost posting aggregated per work order/WBS/work centre/day, scheduler, load/dispatch, maintenance + OEE, dashboards) in progress. Then: merge into `innco-net10-build` with `.github/migration-name.txt` = `ProductionMes`, copy the generated migration, set the name back, push, verify green; independent review of part B; front pages; test kit.
3. Sales & Service S1 → Local AI A1 (mock LLM in CI) per the briefs in `docs/`.
4. Engineering module after the user's Claude-team organisation.
5. Server hardware BOM.

Last fully green commit: **58f4599** (office complete; package carries the password placeholder).

## 8. Waiting on the user / company

Office kit (PMIS_Office_Test.zip, 1405/08/03): feedback workbook and the 10 company questions in it. 
Finance: 16 accountant questions (`docs/finance-r3-report.md` §2; also in the test kit workbook), feedback on the finance test kit, which system prints the legal books. Office: real letter-number format (default `{yy}/{kind}/{unit}/{seq:5}`, kind letters و/ص/د), attendance device model/format (an earlier note says US10C by Donyaye Pardazesh, TCP/IP + RS485 — confirm), Rahkaran archive export format, attendance policy values. Earlier phases: warehouse list and 85 data decisions, procurement form codes, quality/ITP limits, project-control open questions (`claude/project-control-spec.md` §7). Hardware for local AI/voice (to be settled by the BOM).

## 9. Files in this branch

- `docs/` — binding backend briefs (finance, F3, office, production, sales-service, AI, quality, project control), independent reviews (finance 1–3, office 1), reports (finance R3, office O1).
- `tools/finbuild` — Python/Node scripts that build the Persian test guide PDF (Playwright + B Zar) and the feedback workbook (openpyxl); `tools/mc3` — front mock click-through (esbuild bundle + mock API) used to check every screen per role without a backend; `tools/guide3` — earlier guide template.
