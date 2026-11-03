# TreasuryR1 upgrade notes (R1t: general ledger, treasury, period close, files, Sepidar outbox, backups)

Applies when the `TreasuryR1` migration is applied on top of the build branch (`20253d1`). Everything below is done by the program or by `install.ps1`, except the items marked **by hand**.

## What the upgrade does by itself

1. **Close checklist is switched on once (review M2).** The first start after the upgrade sets the finance setting `CloseChecklist.Enforce` to `1` where it was `0` and writes an audit row
   (`SeedUpgrade`, user 0 = the system). A marker setting `Upgrade.TreasuryR1.CloseEnforce` keeps the step from running again, so a company that really wants the gate off can set it back to `0`
   in the finance settings and it stays off. A fresh installation already seeds `1`.
2. **Single-manager checklist overrides no longer count (review M2).** An old `FnCloseChecklist` override that one manager made alone does not pass the gate any more. A period that was locked relying on it needs a new
   override before it can be closed: one finance manager requests it, another finance manager approves (the approver approves exactly what was requested; a new red item means a new request - review M3).
3. **A year closes only after every period was closed through the two-step period close (review H1).** The accountant locks, the CEO (setting `PeriodClose.CloseBy`) closes, then the finance manager closes the year.
   A year whose periods are only locked is refused with the list of periods. Periods that were locked before the period log existed have no known locker: reopen and lock them again before closing.
4. **Treasury documents** (receipts, payments, petty-cash settlements and the drafts of loan, letter-of-credit and guarantee legs) dated in a closed period or closed year are refused (review M7). Late posting of events is unchanged.
5. **Bank reconciliation:** the items of kind 1 (bank fee or interest not booked yet) and 4 (error in our books) must carry the final voucher that corrects them before the reconciliation can be approved (review M8). Old unapproved reconciliations need the link added.
6. **Foreign-currency loans** (disbursement and instalments) are refused until their currency legs go through treasury (review M5). The foreign-currency feature is off by default.
7. **Guarantee fees:** when treasury cancels the document of a guarantee fee, the action `fee-leg` drafts it again (review M6).
8. **The audit chain** uses a key derived from `Identity:AuditHmacKey` (HMAC of the label `PMIS-AUDIT-CHAIN`), not the key itself. The chain is new in this release, so no existing row is re-sealed. One verify call walks at most 50,000 rows; a longer chain is verified in ranges (`from`/`to`).

## Backups (review H2, M10, M11)

* `install.ps1` / `backup.ps1` now need the file `backup-crypto.ps1` next to them (it is in the package).
* **SQL Server Express** cannot encrypt a backup. There the backup is made plain, encrypted at file level at once (`x.bak.enc`, `x.trn.enc`) and the plain file is deleted. Other editions keep the native AES-256 encryption.
* The copy of the **uploaded files** (`files\`) is encrypted at file level on every edition (`<name>.enc`). Only `appsettings.Production.redacted.json` stays readable.
* Format `PMISENC1`: AES-256-CBC with an HMAC-SHA256 seal over the whole file (a changed file is refused before anything is decrypted). Windows PowerShell 5.1 has no AES-GCM class; this construction gives the same protection.
* The key (64 random bytes) is sealed with **DPAPI, scope LocalMachine**, in `<BackupFolder>\keys\file-encryption.key.dpapi`. **Escrow (by hand):** the first installation shows the key once as text in its install log
  ("ESCROW COPY OF THE BACKUP FILE KEY"). Copy it into a safe place that is **not** the backup folder, **not** the keys folder and **not** the off-site target.
* The `keys` folder is **no longer copied to the off-site target** (it used to be). Keep the certificate files, the key password and the escrow text apart from the backups.
* Restore: `install.ps1 -RestoreBackup X.bak.enc` decrypts with the key of the same server; on another server add `-BackupFileKey <escrow text>`. The seal is checked first.
* `backup.ps1` ends with code 0 when everything was done and encrypted, 2 when the off-site copy (or a part) failed, 1 when no encrypted backup could be made (no plain backup is left behind).
* `install-checks.ps1`: the audit-chain check counts only rows written after the first sealed row and is skipped when the restored database has no sealed row yet.

## Settings and keys

* CI sets a fixed test `Identity__AuditHmacKey` (not a secret); servers get a random one from `install.ps1`.
* `install.ps1` merges the settings file: `Database:MigrateAtStart` and `Backup:OffsiteCopyPath` are kept. The start-up security checks run when the site starts, not in `WebApi.dll --migrate-only`.

## Audit key copy (CI fix round)

`<Backup>\keys\audit-key.sealed.txt` is written on every edition: it holds the audit key sealed with the backup file key (always) and, where SQL Server allows, also with the backup certificate. The audit key is not in the settings copy of the backup. To read it on a new server use the escrow copy of the backup file key (see above) with `Unprotect-BackupFile` from `backup-crypto.ps1`.
