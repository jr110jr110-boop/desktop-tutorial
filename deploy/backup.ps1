#Requires -Version 5.1
<#
.SYNOPSIS
    Backups of the INNCO PMIS database and uploaded files: a full backup every day and a log backup every hour.

.DESCRIPTION
    Run by the scheduled tasks that install.ps1 creates (as SYSTEM):
      "INNCO daily backup"       every day at 01:30   -Mode Full
      "INNCO hourly log backup"  every hour           -Mode Log

    -Mode Full (default)
      - full database backup to <BackupFolder>\database\<db>_<date>.bak
      - uploaded files copied to <BackupFolder>\files (new and changed files; nothing is deleted there)
      - a REDACTED copy of the program settings (appsettings.Production.redacted.json): the sign-in key, the audit key, passwords
        and every other secret are replaced by the word REDACTED. The real settings file is never copied.
    -Mode Log
      - log backup to <BackupFolder>\database\log\<db>_<date>.trn. With the full backups it lets the database be restored to any
        moment (at most the last hour is lost when the server disk is lost). Skipped, with a line in the log, until the first full
        backup has been made.

    Both modes
      - the backups are encrypted (AES-256) with the server certificate given in -EncryptionCertificate. A backup can be restored only
        on a server that has that certificate: install.ps1 writes the certificate and its private key to <BackupFolder>\keys and shows
        the password of the key once (in the install log). Keep the password and the keys folder apart from the backups.
        If the encrypted backup cannot be made, an unencrypted one is made instead and the run ends with code 2 (see the log).
      - backups older than the retention are deleted: setting Backup:RetentionDays (default 35), in appsettings.Production.json or
        appsettings.json of the program, unless -KeepDays is given. The newest two full backups are always kept, and a log backup is
        kept as long as the oldest full backup that is kept.
      - when the setting Backup:OffsiteCopyPath holds a folder (for example \\nas\backup\innco), everything new in the backup folder is
        copied there after each run. The task runs as SYSTEM, so on a network share the computer account of this server needs write access.
      - every run adds a line to <BackupFolder>\backup-log.txt.

    Exit code: 0 everything done, 1 the backup failed, 2 the backup was made but not as asked (not encrypted, or the off-site copy failed).

    Restore, in short: restore the certificate first on a new server (CREATE CERTIFICATE ... FROM FILE ... WITH PRIVATE KEY, with the password from
    the install log), then RESTORE DATABASE from the newest full backup WITH NORECOVERY, the log backups in order WITH NORECOVERY (the last one
    WITH RECOVERY, optionally STOPAT), or run install.ps1 -RestoreBackup for the full backup alone.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SqlServer,
    [Parameter(Mandatory)][string]$DatabaseName,
    [Parameter(Mandatory)][string]$BackupFolder,
    [Parameter(Mandatory)][string]$AppDir,
    [ValidateSet('Full', 'Log')][string]$Mode = 'Full',
    [ValidateRange(0, 3650)][int]$KeepDays = 0,
    [string]$EncryptionCertificate,
    [string]$OffsiteCopyPath
)

$ErrorActionPreference = 'Stop'
$script:LogFile = Join-Path $BackupFolder 'backup-log.txt'
$script:Degraded = $false

function Write-Log([string]$Text) {
    $line = '{0:yyyy-MM-dd HH:mm:ss}  {1}' -f (Get-Date), $Text
    Add-Content -Path $script:LogFile -Value $line -Encoding UTF8
    Write-Host $line
}

# ----------------------------------------------------------------------------------------------
# Settings (Backup:RetentionDays, Backup:OffsiteCopyPath)
# ----------------------------------------------------------------------------------------------

function Read-JsonFile([string]$Path) {
    if (-not (Test-Path $Path)) { return $null }
    try { return (Get-Content -Raw -Encoding UTF8 -Path $Path | ConvertFrom-Json) } catch { return $null }
}

# The value of Backup:<Name> from appsettings.Production.json, else appsettings.json, else the default.
function Get-BackupSetting([string]$Name, $Default) {
    foreach ($file in @('appsettings.Production.json', 'appsettings.json')) {
        $json = Read-JsonFile (Join-Path $AppDir $file)
        if ($null -eq $json) { continue }
        $section = $json.PSObject.Properties['Backup']
        if ($null -eq $section -or $null -eq $section.Value) { continue }
        $property = $section.Value.PSObject.Properties[$Name]
        if ($null -ne $property -and $null -ne $property.Value -and "$($property.Value)".Trim() -ne '') { return $property.Value }
    }
    return $Default
}

# ----------------------------------------------------------------------------------------------
# Redacted copy of the settings
# ----------------------------------------------------------------------------------------------

# Names whose text value is a secret. "Key" alone is the sign-in key (BearerTokens:Key); the others cover the audit key, passwords, tokens and API keys.
$script:SecretName = '(?i)password|passwd|secret|apikey|api_key|privatekey|hmac|signingkey|^key$|accesstoken|refreshtoken'

# A copy of the settings tree in which every secret text is replaced by REDACTED. A text that carries a password inside (a connection string) is replaced too.
function ConvertTo-RedactedSettings($Node) {
    if ($null -eq $Node) { return $null }
    if ($Node -is [System.Management.Automation.PSCustomObject]) {
        $copy = [ordered]@{}
        foreach ($property in $Node.PSObject.Properties) {
            $value = $property.Value
            if ($value -is [string] -and ($property.Name -match $script:SecretName -or $value -match '(?i)(password|pwd)\s*=')) { $copy[$property.Name] = 'REDACTED' }
            else { $copy[$property.Name] = ConvertTo-RedactedSettings $value }
        }
        return [pscustomobject]$copy
    }
    if ($Node -is [System.Collections.IEnumerable] -and $Node -isnot [string]) {
        $items = New-Object System.Collections.Generic.List[object]
        foreach ($item in $Node) { $items.Add((ConvertTo-RedactedSettings $item)) }
        return , $items.ToArray()
    }
    return $Node
}

# Writes <FilesFolder>\appsettings.Production.redacted.json and removes the copy of the real file that earlier versions of this script made.
function Save-RedactedSettings([string]$FilesFolder) {
    $settings = Join-Path $AppDir 'appsettings.Production.json'
    $oldCopy = Join-Path $FilesFolder 'appsettings.Production.json'
    if (Test-Path $oldCopy) {
        Remove-Item -Path $oldCopy -Force
        Write-Log 'Removed the old copy of appsettings.Production.json from the backup (it held the sign-in key); the redacted copy replaces it.'
    }
    if (-not (Test-Path $settings)) { return }
    $json = Read-JsonFile $settings
    if ($null -eq $json) { Write-Log 'WARNING: appsettings.Production.json could not be read; no settings copy was made.'; $script:Degraded = $true; return }
    $redacted = ConvertTo-RedactedSettings $json
    $text = $redacted | ConvertTo-Json -Depth 20
    [IO.File]::WriteAllText((Join-Path $FilesFolder 'appsettings.Production.redacted.json'), $text, (New-Object System.Text.UTF8Encoding($false)))
}

# ----------------------------------------------------------------------------------------------
# Retention
# ----------------------------------------------------------------------------------------------

# Which files the retention deletes. $Full and $Log are FileInfo lists; the newest two full backups stay whatever their age, and a log backup
# older than the oldest full backup that stays is of no use any more.
function Get-ExpiredBackups($Full, $Log, [int]$Days, [datetime]$Now) {
    $limit = $Now.AddDays(-$Days)
    $fullSorted = @($Full | Sort-Object LastWriteTime -Descending)
    $expired = New-Object System.Collections.Generic.List[object]
    $kept = New-Object System.Collections.Generic.List[object]
    for ($i = 0; $i -lt $fullSorted.Count; $i++) {
        if ($i -ge 2 -and $fullSorted[$i].LastWriteTime -lt $limit) { $expired.Add($fullSorted[$i]) } else { $kept.Add($fullSorted[$i]) }
    }
    if ($kept.Count -gt 0) {
        $oldestKept = ($kept | Sort-Object LastWriteTime | Select-Object -First 1).LastWriteTime
        foreach ($file in @($Log)) { if ($file.LastWriteTime -lt $oldestKept -and $file.LastWriteTime -lt $limit) { $expired.Add($file) } }
    }
    return , $expired.ToArray()
}

function Remove-ExpiredBackups([string]$DatabaseFolder, [int]$Days) {
    if (-not (Test-Path $DatabaseFolder)) { return }
    $full = @(Get-ChildItem -Path $DatabaseFolder -Filter '*.bak' -File -ErrorAction SilentlyContinue)
    $logFolder = Join-Path $DatabaseFolder 'log'
    $log = @()
    if (Test-Path $logFolder) { $log = @(Get-ChildItem -Path $logFolder -Filter '*.trn' -File -ErrorAction SilentlyContinue) }
    $expired = Get-ExpiredBackups $full $log $Days (Get-Date)
    foreach ($file in $expired) { Remove-Item -Path $file.FullName -Force }
    if ($expired.Count -gt 0) { Write-Log "Removed $($expired.Count) backups older than $Days days from $DatabaseFolder" }
}

# ----------------------------------------------------------------------------------------------
# The backup itself
# ----------------------------------------------------------------------------------------------

function ConvertTo-SqlIdentifier([string]$Name) { return '[' + $Name.Replace(']', ']]') + ']' }

# The WITH list of a backup. Express cannot compress; with encryption a larger transfer size lets the compression work.
function Get-BackupOptions([bool]$IsExpress, [string]$Certificate) {
    $options = New-Object System.Collections.Generic.List[string]
    $options.Add('INIT'); $options.Add('CHECKSUM')
    if (-not $IsExpress) { $options.Add('COMPRESSION') }
    if ($Certificate) {
        if (-not $IsExpress) { $options.Add('MAXTRANSFERSIZE = 131072') }
        $options.Add('ENCRYPTION (ALGORITHM = AES_256, SERVER CERTIFICATE = ' + (ConvertTo-SqlIdentifier $Certificate) + ')')
    }
    return ($options -join ', ')
}

function Invoke-ServerQuery([string]$Query, [hashtable]$Parameters, [switch]$Scalar) {
    $connection = New-Object System.Data.SqlClient.SqlConnection (
        "Data Source=$SqlServer;Initial Catalog=master;Integrated Security=True;Connect Timeout=60;Application Name=INNCO-backup")
    $connection.Open()
    try {
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 0
        $command.CommandText = $Query
        if ($Parameters) { foreach ($key in $Parameters.Keys) { [void]$command.Parameters.AddWithValue($key, $Parameters[$key]) } }
        if ($Scalar) { return $command.ExecuteScalar() }
        [void]$command.ExecuteNonQuery()
    }
    finally { $connection.Dispose() }
}

# Runs BACKUP DATABASE or BACKUP LOG to $File. Encrypted when a certificate is given; when that fails, the backup is made once more without encryption.
function Invoke-Backup([string]$Statement, [string]$File, [bool]$IsExpress, [string]$Certificate) {
    $target = $Statement + ' TO DISK = @file WITH '
    if ($Certificate) {
        try {
            Invoke-ServerQuery ($target + (Get-BackupOptions $IsExpress $Certificate)) @{ '@file' = $File }
            return $true
        }
        catch {
            Write-Log "WARNING: the encrypted backup failed ($($_.Exception.Message)); an unencrypted backup is made instead. Check the certificate '$Certificate' (install.ps1 creates it)."
            $script:Degraded = $true
            if (Test-Path $File) { Remove-Item -Path $File -Force -ErrorAction SilentlyContinue }
        }
    }
    Invoke-ServerQuery ($target + (Get-BackupOptions $IsExpress $null)) @{ '@file' = $File }
    return $false
}

# ----------------------------------------------------------------------------------------------
# Off-site copy
# ----------------------------------------------------------------------------------------------

function Copy-ToOffsite([string]$Target) {
    try {
        if (-not (Test-Path $Target)) { New-Item -ItemType Directory -Force -Path $Target | Out-Null }
        foreach ($name in @('database', 'files', 'keys')) {
            $source = Join-Path $BackupFolder $name
            if (-not (Test-Path $source)) { continue }
            & robocopy.exe $source (Join-Path $Target $name) /E /R:2 /W:5 /NP /NFL /NDL /NJH /NJS | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "robocopy code $LASTEXITCODE for $source" }
        }
        # the same retention at the other place
        $days = if ($KeepDays -gt 0) { $KeepDays } else { [int](Get-BackupSetting 'RetentionDays' 35) }
        Remove-ExpiredBackups (Join-Path $Target 'database') $days
        Write-Log "Off-site copy OK: $Target"
    }
    catch {
        Write-Log "WARNING: the off-site copy to $Target failed: $($_.Exception.Message)"
        $script:Degraded = $true
    }
}

# ----------------------------------------------------------------------------------------------
# Main (skipped when the file is dot-sourced, for the tests)
# ----------------------------------------------------------------------------------------------

if ($MyInvocation.InvocationName -ne '.') {
    try {
        $databaseDir = Join-Path $BackupFolder 'database'
        $logDir = Join-Path $databaseDir 'log'
        $filesDir = Join-Path $BackupFolder 'files'
        New-Item -ItemType Directory -Force -Path $databaseDir, $logDir, $filesDir | Out-Null

        $retention = if ($KeepDays -gt 0) { $KeepDays } else { [int](Get-BackupSetting 'RetentionDays' 35) }
        if ($retention -lt 1) { $retention = 35 }
        $offsite = if ($OffsiteCopyPath) { $OffsiteCopyPath } else { [string](Get-BackupSetting 'OffsiteCopyPath' '') }
        $isExpress = [int](Invoke-ServerQuery "SELECT CAST(SERVERPROPERTY('EngineEdition') AS int)" $null -Scalar) -eq 4   # Express cannot compress backups
        $database = ConvertTo-SqlIdentifier $DatabaseName

        if ($Mode -eq 'Log') {
            # a log backup needs the full recovery model and a full backup made after it was switched on (if the catalog cannot be read, the backup itself is tried)
            $ready = 1
            try {
                $ready = Invoke-ServerQuery @"
SELECT CASE WHEN d.recovery_model = 1 AND r.last_log_backup_lsn IS NOT NULL THEN 1 ELSE 0 END
FROM sys.databases d LEFT JOIN sys.database_recovery_status r ON r.database_id = d.database_id WHERE d.name = @n
"@ @{ '@n' = $DatabaseName } -Scalar
            }
            catch { Write-Log "Could not read the recovery status ($($_.Exception.Message)); the log backup is tried anyway." }
            if ($null -eq $ready -or $ready -is [DBNull] -or [int]$ready -ne 1) {
                Write-Log 'Log backup skipped: the database is not in the full recovery model yet or no full backup has been made since (the daily backup makes it).'
                exit 0
            }
            $logFile = Join-Path $logDir ('{0}_{1:yyyyMMdd_HHmmss}.trn' -f $DatabaseName, (Get-Date))
            $encrypted = Invoke-Backup "BACKUP LOG $database" $logFile $isExpress $EncryptionCertificate
            Write-Log ('Log backup OK: {0} ({1:N1} MB){2}' -f $logFile, ((Get-Item $logFile).Length / 1MB), $(if ($encrypted) { ', encrypted' } else { ', NOT encrypted' }))
        }
        else {
            # 1. Database
            $backupFile = Join-Path $databaseDir ('{0}_{1:yyyyMMdd_HHmm}.bak' -f $DatabaseName, (Get-Date))
            $encrypted = Invoke-Backup "BACKUP DATABASE $database" $backupFile $isExpress $EncryptionCertificate
            Write-Log ('Database backup OK: {0} ({1:N1} MB){2}' -f $backupFile, ((Get-Item $backupFile).Length / 1MB), $(if ($encrypted) { ', encrypted' } else { ', NOT encrypted' }))

            # 2. Uploaded files and the redacted settings
            foreach ($relative in @('UploadedDocuments', 'wwwroot\Documents', 'wwwroot\avatars')) {
                $source = Join-Path $AppDir $relative
                if (-not (Test-Path $source)) { continue }
                & robocopy.exe $source (Join-Path $filesDir $relative) /E /R:2 /W:5 /NP /NFL /NDL /NJH /NJS | Out-Null
                if ($LASTEXITCODE -ge 8) { throw "Copying $source failed (robocopy code $LASTEXITCODE)." }
            }
            Save-RedactedSettings $filesDir
            $fileCount = @(Get-ChildItem -Path $filesDir -Recurse -File).Count
            Write-Log "Uploaded files OK: $filesDir ($fileCount files)"
        }

        # 3. Retention (the same for both modes) and old program logs
        Remove-ExpiredBackups $databaseDir $retention
        if ($Mode -eq 'Full') {
            $logsDir = Join-Path (Split-Path -Parent $AppDir) 'logs'
            if (Test-Path $logsDir) {
                Get-ChildItem -Path $logsDir -Filter 'stdout*.log' -File |
                    Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } | Remove-Item -Force
            }
        }

        # 4. Off-site copy
        if ($offsite -and $offsite.Trim()) { Copy-ToOffsite $offsite.Trim() }

        if ($script:Degraded) { exit 2 }
        exit 0
    }
    catch {
        Write-Log "BACKUP FAILED: $($_.Exception.Message)"
        exit 1
    }
}
