#Requires -Version 5.1
<#
.SYNOPSIS
    Monthly restore test of the INNCO PMIS database backup (Package D).

.DESCRIPTION
    Run by the scheduled task "INNCO monthly restore test" that install.ps1 creates (as SYSTEM, on day 1 of every month at 03:00).
    The program's application pool must not hold sysadmin, so the test runs here and the program only READS the result file.

      1. takes the newest full backup in <BackupFolder>\database (x.bak, or x.bak.enc: it is decrypted with the key of backup-crypto.ps1 into a work folder first);
      2. restores it into the scratch database IRNNCO_RESTORETEST (never the live database; the scratch name is checked);
      3. checks: the migration history of the restored database is in the live database and the restored database has no migration the live one lacks;
         the row counts of the key tables (a table with rows in the live database has rows in the restored one); the audit chain of the restored database:
         the sequence has no gap, every entry points to the hash of the entry before it, and the head entry is the same as the entry of the live database;
         (the HMAC of each entry is checked by the program itself in its nightly and weekly verification; this script does not hold the audit key);
      4. drops the scratch database and removes the decrypted work files;
      5. writes restore-test-<runId>.json (and a line in restore-test-log.txt) to -ResultDirectory. The program reads that folder
         (setting RestoreTest:ResultDirectory) and shows the result on the page «سلامت سوابق» and on the CEO dashboard.

    Exit code: 0 the test passed, 1 it failed (the result file says which check), 2 the test could not even start (no backup, no key).
    No secret is written to the result or the log.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SqlServer,
    [Parameter(Mandatory)][string]$DatabaseName,
    [Parameter(Mandatory)][string]$BackupFolder,
    [string]$ResultDirectory,
    [string]$ScratchName = 'IRNNCO_RESTORETEST',
    [string]$BackupFileKey
)

$ErrorActionPreference = 'Stop'
if (-not $ResultDirectory) { $ResultDirectory = Join-Path $BackupFolder 'restore-test' }
$script:LogFile = Join-Path $ResultDirectory 'restore-test-log.txt'
. (Join-Path $PSScriptRoot 'backup-crypto.ps1')

function Write-Log([string]$Text) {
    $line = '{0:yyyy-MM-dd HH:mm:ss}  {1}' -f (Get-Date), $Text
    Write-Host $line
    try { Add-Content -Path $script:LogFile -Value $line -Encoding UTF8 } catch { }
}

if ($ScratchName -notmatch '^[A-Za-z][A-Za-z0-9_]{2,60}$') { Write-Host 'The scratch database name is not valid.'; exit 2 }
if ($ScratchName -ieq $DatabaseName) { Write-Host 'The scratch database must not be the live database.'; exit 2 }
if ($DatabaseName -notmatch '^[A-Za-z][A-Za-z0-9_]{0,100}$') { Write-Host 'The database name is not valid.'; exit 2 }
New-Item -ItemType Directory -Force -Path $ResultDirectory | Out-Null

function Invoke-Sql {
    param([Parameter(Mandatory)][string]$Query, [string]$Database = 'master', [hashtable]$Parameters, [switch]$Scalar)
    $cs = "Data Source=$SqlServer;Initial Catalog=$Database;Integrated Security=True;Connect Timeout=30;Application Name=INNCO-restore-test"
    $c = New-Object System.Data.SqlClient.SqlConnection $cs
    $c.Open()
    try {
        $cmd = $c.CreateCommand(); $cmd.CommandText = $Query; $cmd.CommandTimeout = 0
        if ($Parameters) { foreach ($k in $Parameters.Keys) { [void]$cmd.Parameters.AddWithValue($k, $Parameters[$k]) } }
        if ($Scalar) { return $cmd.ExecuteScalar() }
        $t = New-Object System.Data.DataTable
        $t.Load($cmd.ExecuteReader())
        return , $t
    }
    finally { $c.Dispose() }
}

$runId = 'rt-{0:yyyyMMddHHmmss}' -f (Get-Date)
$started = Get-Date
$checks = New-Object System.Collections.Generic.List[object]
$counts = [ordered]@{}
$result = [ordered]@{
    runId = $runId; startedAt = $started.ToString('o'); finishedAt = $null; success = $false; backupFile = $null; backupTakenAt = $null
    migrationsMatch = $false; chainOk = $false; chainEntriesChecked = 0; scratchDropped = $false; rowCounts = $counts; checks = $checks; message = $null
}
function Add-Check([string]$Name, [bool]$Passed, [string]$Detail) {
    $checks.Add([ordered]@{ name = $Name; passed = $Passed; detail = $Detail })
    Write-Log ("  check {0}: {1} {2}" -f $Name, $(if ($Passed) { 'OK' } else { 'FAILED' }), $Detail)
}

$work = Join-Path $ResultDirectory ('work-' + $runId)
$scratchCreated = $false
$exit = 0
try {
    Write-Log "Restore test $runId starts (live database $DatabaseName, scratch database $ScratchName)."
    $dbFolder = Join-Path $BackupFolder 'database'
    $backup = Get-ChildItem -Path $dbFolder -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "${DatabaseName}_*" -and ($_.Name -like '*.bak' -or $_.Name -like '*.bak.enc') -and $_.Name -notlike '*before-restore*' -and $_.Name -notlike '*before-update*' } |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $backup) { throw [System.InvalidOperationException]::new("No full backup of $DatabaseName in $dbFolder.") }
    $result.backupFile = $backup.Name
    Write-Log "Latest backup: $($backup.Name) ($([math]::Round($backup.Length / 1MB, 1)) MB)"

    New-Item -ItemType Directory -Force -Path $work | Out-Null
    $bak = $backup.FullName
    if ($backup.Name.EndsWith('.enc', [StringComparison]::OrdinalIgnoreCase)) {
        $key = Get-FileCryptoKey (Join-Path $BackupFolder 'keys') $BackupFileKey
        $bak = Unprotect-BackupFile $backup.FullName $key.Bytes (Join-Path $work ($backup.Name.Substring(0, $backup.Name.Length - 4)))
        Add-Check 'decrypt' $true 'The file-level seal is valid.'
    }
    else { Copy-Item -Path $backup.FullName -Destination $work -Force; $bak = Join-Path $work $backup.Name }

    $header = Invoke-Sql -Query 'RESTORE HEADERONLY FROM DISK = @f' -Parameters @{ '@f' = $bak }
    if ($header.Rows.Count -gt 0 -and $header.Rows[0].BackupFinishDate -isnot [DBNull]) { $result.backupTakenAt = ([datetime]$header.Rows[0].BackupFinishDate).ToString('o') }

    # the scratch database: an old one left by a failed run is dropped first
    $exists = Invoke-Sql -Scalar -Query 'SELECT COUNT(*) FROM sys.databases WHERE name = @n' -Parameters @{ '@n' = $ScratchName }
    if ([int]$exists -gt 0) { Invoke-Sql -Query "ALTER DATABASE [$ScratchName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$ScratchName];" | Out-Null }

    $dataPath = [string](Invoke-Sql -Scalar -Query "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(260))")
    $logPath = [string](Invoke-Sql -Scalar -Query "SELECT CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(260))")
    $files = Invoke-Sql -Query 'RESTORE FILELISTONLY FROM DISK = @f' -Parameters @{ '@f' = $bak }
    $moves = New-Object System.Collections.Generic.List[string]; $d = 0; $l = 0
    foreach ($f in $files.Rows) {
        if ($f.Type -eq 'L') { $l++; $target = Join-Path $logPath ("{0}_log{1}.ldf" -f $ScratchName, $(if ($l -gt 1) { $l } else { '' })) }
        else { $d++; $target = Join-Path $dataPath ("{0}_{1}.mdf" -f $ScratchName, $d); if ($d -gt 1) { $target = Join-Path $dataPath ("{0}_{1}.ndf" -f $ScratchName, $d) } }
        $moves.Add("MOVE N'" + ([string]$f.LogicalName).Replace("'", "''") + "' TO N'" + $target.Replace("'", "''") + "'")
    }
    $scratchCreated = $true
    Invoke-Sql -Query ("RESTORE DATABASE [$ScratchName] FROM DISK = @f WITH " + ($moves -join ', ') + ', RECOVERY, REPLACE') -Parameters @{ '@f' = $bak } | Out-Null
    Add-Check 'restore' $true "Restored into $ScratchName."

    # migration history
    $live = @((Invoke-Sql -Database $DatabaseName -Query 'SELECT MigrationId FROM dbo.__EFMigrationsHistory').Rows | ForEach-Object { [string]$_.MigrationId })
    $rest = @((Invoke-Sql -Database $ScratchName -Query 'SELECT MigrationId FROM dbo.__EFMigrationsHistory').Rows | ForEach-Object { [string]$_.MigrationId })
    $onlyRestored = @($rest | Where-Object { $live -notcontains $_ })
    $result.migrationsMatch = ($onlyRestored.Count -eq 0 -and $rest.Count -gt 0)
    Add-Check 'migrations' $result.migrationsMatch ("restored $($rest.Count), live $($live.Count)" + $(if ($onlyRestored.Count) { '; only in the backup: ' + ($onlyRestored -join ', ') } else { '' }))

    # row counts of the key tables
    $tables = @('FnAuditLogs', 'InvItems', 'InvLots', 'InvDocuments', 'QaRecords', 'QaMeasurements', 'PcProgressEvents')
    $countsOk = $true
    foreach ($t in $tables) {
        $inLive = [int](Invoke-Sql -Database $DatabaseName -Scalar -Query 'SELECT COUNT(*) FROM sys.tables WHERE name = @n' -Parameters @{ '@n' = $t })
        if ($inLive -eq 0) { continue }
        $liveCount = [long](Invoke-Sql -Database $DatabaseName -Scalar -Query "SELECT COUNT_BIG(*) FROM dbo.[$t]")
        $inRest = [int](Invoke-Sql -Database $ScratchName -Scalar -Query 'SELECT COUNT(*) FROM sys.tables WHERE name = @n' -Parameters @{ '@n' = $t })
        $restCount = if ($inRest -gt 0) { [long](Invoke-Sql -Database $ScratchName -Scalar -Query "SELECT COUNT_BIG(*) FROM dbo.[$t]") } else { -1 }
        $counts[$t] = $restCount
        $ok = ($restCount -ge 0) -and ($liveCount -eq 0 -or $restCount -gt 0)
        if (-not $ok) { $countsOk = $false }
        Add-Check "rows:$t" $ok "backup $restCount, live $liveCount"
    }

    # the audit chain of the restored database
    $chainSql = @'
;WITH s AS (SELECT Seq, PrevHash, Hash, LAG(Seq) OVER (ORDER BY Seq) AS PrevSeq, LAG(Hash) OVER (ORDER BY Seq) AS PrevRowHash FROM dbo.FnAuditLogs WHERE Seq IS NOT NULL)
SELECT COUNT_BIG(*) AS Entries,
       SUM(CASE WHEN PrevSeq IS NOT NULL AND Seq <> PrevSeq + 1 THEN 1 ELSE 0 END) AS Gaps,
       SUM(CASE WHEN PrevSeq IS NOT NULL AND ISNULL(PrevHash, '') <> ISNULL(PrevRowHash, '') THEN 1 ELSE 0 END) AS BadLinks,
       MAX(Seq) AS HeadSeq
FROM s
'@
    $chain = (Invoke-Sql -Database $ScratchName -Query $chainSql).Rows[0]
    $entries = [long]$chain.Entries
    $result.chainEntriesChecked = $entries
    $headOk = $true; $headText = 'no entries'
    if ($entries -gt 0) {
        $headSeq = [long]$chain.HeadSeq
        $headRest = [string](Invoke-Sql -Database $ScratchName -Scalar -Query 'SELECT Hash FROM dbo.FnAuditLogs WHERE Seq = @s' -Parameters @{ '@s' = $headSeq })
        $headLive = Invoke-Sql -Database $DatabaseName -Scalar -Query 'SELECT Hash FROM dbo.FnAuditLogs WHERE Seq = @s' -Parameters @{ '@s' = $headSeq }
        $headOk = ($headLive -isnot [DBNull]) -and $null -ne $headLive -and ([string]$headLive -eq $headRest)
        $headText = "head $headSeq " + $(if ($headOk) { 'equals the live entry' } else { 'DIFFERS from the live entry' })
    }
    $result.chainOk = ([long]$chain.Gaps -eq 0 -and [long]$chain.BadLinks -eq 0 -and $headOk)
    Add-Check 'audit-chain' $result.chainOk ("entries $entries, gaps $($chain.Gaps), broken links $($chain.BadLinks), $headText")

    $result.success = ($result.migrationsMatch -and $countsOk -and $result.chainOk)
    $result.message = if ($result.success) { 'The latest backup restored and passed every check.' } else { 'The backup restored but a check failed; see the checks.' }
    if (-not $result.success) { $exit = 1 }
}
catch {
    $result.success = $false
    $result.message = 'The restore test failed: ' + ($_.Exception.Message -replace '\s+', ' ')
    if ($result.message.Length -gt 900) { $result.message = $result.message.Substring(0, 900) }
    Write-Log $result.message
    $exit = if ($_.Exception -is [System.InvalidOperationException]) { 2 } else { 1 }
}
finally {
    if ($scratchCreated) {
        try {
            Invoke-Sql -Query "IF DB_ID(N'$ScratchName') IS NOT NULL BEGIN ALTER DATABASE [$ScratchName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$ScratchName]; END" | Out-Null
            $result.scratchDropped = ([int](Invoke-Sql -Scalar -Query 'SELECT COUNT(*) FROM sys.databases WHERE name = @n' -Parameters @{ '@n' = $ScratchName }) -eq 0)
        }
        catch { Write-Log ("The scratch database could not be dropped: " + $_.Exception.Message) }
    }
    else { $result.scratchDropped = ([int](Invoke-Sql -Scalar -Query 'SELECT COUNT(*) FROM sys.databases WHERE name = @n' -Parameters @{ '@n' = $ScratchName }) -eq 0) }
    if (-not $result.scratchDropped -and $result.success) { $result.success = $false; $result.message = 'The scratch database could not be dropped.'; $exit = 1 }
    Remove-Item -Path $work -Recurse -Force -ErrorAction SilentlyContinue
    $result.finishedAt = (Get-Date).ToString('o')
    $json = $result | ConvertTo-Json -Depth 6
    $file = Join-Path $ResultDirectory ("restore-test-{0}.json" -f $runId)
    [IO.File]::WriteAllText($file, $json, (New-Object System.Text.UTF8Encoding($false)))
    Write-Log ("Restore test {0}: {1}. Result: {2}" -f $runId, $(if ($result.success) { 'PASSED' } else { 'FAILED' }), $file)
}
exit $exit
