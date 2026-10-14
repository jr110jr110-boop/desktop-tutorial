#Requires -Version 5.1
<#
.SYNOPSIS
    Daily backup of the INNCO PMIS database and uploaded files.

.DESCRIPTION
    Run by the "INNCO daily backup" scheduled task that install.ps1 creates (as SYSTEM, every day at 01:30).
      - full database backup to <BackupFolder>\database\<db>_<date>.bak, kept for -KeepDays days
      - uploaded files copied to <BackupFolder>\files (new and changed files; nothing is deleted there)
      - program settings (appsettings.Production.json) copied to <BackupFolder>\files
    Every run adds a line to <BackupFolder>\backup-log.txt. Copy the backup folder to another device
    (for example the NAS) regularly: a backup on the same server does not survive a lost server.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$SqlServer,
    [Parameter(Mandatory)][string]$DatabaseName,
    [Parameter(Mandatory)][string]$BackupFolder,
    [Parameter(Mandatory)][string]$AppDir,
    [ValidateRange(1, 3650)][int]$KeepDays = 14
)

$ErrorActionPreference = 'Stop'
$logFile = Join-Path $BackupFolder 'backup-log.txt'

function Write-Log([string]$Text) {
    $line = '{0:yyyy-MM-dd HH:mm:ss}  {1}' -f (Get-Date), $Text
    Add-Content -Path $logFile -Value $line -Encoding UTF8
    Write-Host $line
}

try {
    $databaseDir = Join-Path $BackupFolder 'database'
    $filesDir = Join-Path $BackupFolder 'files'
    New-Item -ItemType Directory -Force -Path $databaseDir, $filesDir | Out-Null

    # 1. Database
    $backupFile = Join-Path $databaseDir ('{0}_{1:yyyyMMdd_HHmm}.bak' -f $DatabaseName, (Get-Date))
    $connection = New-Object System.Data.SqlClient.SqlConnection (
        "Data Source=$SqlServer;Initial Catalog=master;Integrated Security=True;Connect Timeout=60;Application Name=INNCO-backup")
    $connection.Open()
    try {
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 0
        $command.CommandText = "SELECT CAST(SERVERPROPERTY('EngineEdition') AS int)"
        $isExpress = [int]$command.ExecuteScalar() -eq 4          # Express cannot compress backups
        $options = if ($isExpress) { 'INIT, CHECKSUM' } else { 'INIT, CHECKSUM, COMPRESSION' }
        $command.CommandText = 'BACKUP DATABASE [' + $DatabaseName.Replace(']', ']]') + "] TO DISK = @file WITH $options"
        [void]$command.Parameters.AddWithValue('@file', $backupFile)
        [void]$command.ExecuteNonQuery()
    }
    finally { $connection.Dispose() }
    Write-Log ('Database backup OK: {0} ({1:N1} MB)' -f $backupFile, ((Get-Item $backupFile).Length / 1MB))

    # 2. Uploaded files and settings
    foreach ($relative in @('UploadedDocuments', 'wwwroot\Documents', 'wwwroot\avatars')) {
        $source = Join-Path $AppDir $relative
        if (-not (Test-Path $source)) { continue }
        & robocopy.exe $source (Join-Path $filesDir $relative) /E /R:2 /W:5 /NP /NFL /NDL /NJH /NJS | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "Copying $source failed (robocopy code $LASTEXITCODE)." }
    }
    $settings = Join-Path $AppDir 'appsettings.Production.json'
    if (Test-Path $settings) { Copy-Item -Path $settings -Destination $filesDir -Force }
    $fileCount = @(Get-ChildItem -Path $filesDir -Recurse -File).Count
    Write-Log "Uploaded files OK: $filesDir ($fileCount files)"

    # 3. Clean up old database backups and old program logs
    $limit = (Get-Date).AddDays(-$KeepDays)
    $old = @(Get-ChildItem -Path $databaseDir -Filter '*.bak' -File | Where-Object { $_.LastWriteTime -lt $limit })
    $old | Remove-Item -Force
    if ($old.Count -gt 0) { Write-Log "Removed $($old.Count) database backups older than $KeepDays days" }
    $logsDir = Join-Path (Split-Path -Parent $AppDir) 'logs'
    if (Test-Path $logsDir) {
        Get-ChildItem -Path $logsDir -Filter 'stdout*.log' -File |
            Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } | Remove-Item -Force
    }
    exit 0
}
catch {
    Write-Log "BACKUP FAILED: $($_.Exception.Message)"
    exit 1
}
