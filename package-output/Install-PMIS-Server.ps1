#Requires -Version 5.1
<#
  INNCO PMIS - install or update on the company server "PMIS" (192.168.3.17).
  Run as Administrator, logged in with the same Windows user that installed SQL Server
  (the network administrator account). No password is stored in this file.

  What it does:
    1. Checks the computer name, administrator rights and the package files.
    2. If PMIS is already installed: runs the existing nightly backup task once and waits
       for it (database + uploaded files) - the update stops if the backup fails.
    3. Runs install.ps1 (keeps settings, database, users and uploaded files; adds the new modules).
    4. Shows the addresses to open in the browser (HTTPS; plain HTTP works on the server itself only).

  HTTPS: install.ps1 keeps the certificate the site uses; the first time it makes a self-signed one and writes its public file to
  <install folder>\certificates\innco-pmis.cer - install that file as a trusted root certificate on the client computers.
  To use a certificate of the company (in the computer's personal store, with its private key), give its thumbprint:
      .\Install-PMIS-Server.ps1 -CertificateThumbprint 0123456789ABCDEF...
#>
[CmdletBinding()]
param([switch]$NoPrompt, [string]$CertificateThumbprint)
$ErrorActionPreference = 'Stop'
$ServerName = 'PMIS'
$ServerIp   = '192.168.3.17'
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path

function Say($text, $color = 'Gray') { Write-Host $text -ForegroundColor $color }

# everything this run shows also goes to install-launch-log.txt next to this script (no password is ever shown except the
# one-time temporary Admin password of a first installation, which install.ps1 prints at the end - it is kept out of the log below)
$launchLog = Join-Path $Here 'install-launch-log.txt'
try { Start-Transcript -Path $launchLog -Append -ErrorAction Stop | Out-Null; $transcript = $true } catch { $transcript = $false }
# files taken from a ZIP downloaded from the internet carry the "came from the internet" mark; remove it so Windows runs them without asking
try { Get-ChildItem -Path $Here -Recurse -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue } catch { }
trap {
    Say ''
    Say ('The installation stopped: ' + $_.Exception.Message) Red
    Say ('At: ' + $_.InvocationInfo.PositionMessage) DarkGray
    Say "Nothing after this point was changed. Send $launchLog to support." Yellow
    if ($transcript) { Stop-Transcript | Out-Null }
    exit 1
}

# 1. checks ---------------------------------------------------------------------------------
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Say 'Please run this script as Administrator (right-click > Run as administrator).' Red; exit 1
}
if ($env:COMPUTERNAME -ne $ServerName) {
    Say "This computer is '$env:COMPUTERNAME', not '$ServerName'." Yellow
    if (-not $NoPrompt) {
        $answer = Read-Host 'Continue anyway? (yes/no)'
        if ($answer -ne 'yes') { exit 1 }
    }
}
$installer = Join-Path $Here 'install.ps1'
if (-not (Test-Path $installer)) { Say "install.ps1 not found at $installer" Red; exit 1 }
Say "User: $env:USERDOMAIN\$env:USERNAME (must be the user that installed SQL Server)" Cyan

# 2. backup before update -------------------------------------------------------------------
$task = Get-ScheduledTask -TaskName 'INNCO daily backup' -ErrorAction SilentlyContinue
if ($task) {
    Say 'PMIS is already installed. Taking a backup before the update...' Cyan
    Start-ScheduledTask -TaskName 'INNCO daily backup'
    Start-Sleep -Seconds 5
    $deadline = (Get-Date).AddHours(2)
    while ((Get-ScheduledTask -TaskName 'INNCO daily backup').State -eq 'Running') {
        if ((Get-Date) -gt $deadline) { Say 'Backup still running after 2 hours - update stopped.' Red; exit 1 }
        Start-Sleep -Seconds 10
    }
    $result = (Get-ScheduledTaskInfo -TaskName 'INNCO daily backup').LastTaskResult
    # R1t (D21): code 2 = the backup was made and encrypted but the off-site copy failed or a part could not be copied (R1t review H2: a backup is never kept unencrypted; if it cannot be encrypted the run fails with code 1): the update goes on, install.ps1 makes a checked backup of its own right before it changes anything
    if ($result -eq 2) { Say 'The backup was made, but not completely (the off-site copy failed or a part could not be copied). See backup-log.txt in the backup folder. The update goes on.' Yellow }
    elseif ($result -ne 0) { Say "Backup failed (code $result). Update stopped; nothing was changed." Red; exit 1 }
    Say 'Backup finished. If the update must be undone, the backup is in the backup folder (database\*.bak or *.bak.enc; an encrypted one is opened by install.ps1 -RestoreBackup).' Green
} else {
    Say 'No earlier installation found: this is a first installation.' Cyan
}

# 3. install / update -----------------------------------------------------------------------
# keep the folders of the existing installation (if it was installed with other folders than the defaults)
$params = @{}
if ($task) {
    $taskArgs = ($task.Actions | Select-Object -First 1).Arguments
    if ($taskArgs -match '-AppDir "([^"]+)"') { $params.InstallRoot = Split-Path -Parent $Matches[1] }
    if ($taskArgs -match '-BackupFolder "([^"]+)"') { $params.BackupFolder = $Matches[1] }
    if ($taskArgs -match '-DatabaseName "([^"]+)"') { $params.DatabaseName = $Matches[1] }
    Say ("Existing installation: folder {0}, backups {1}, database {2}" -f $params.InstallRoot, $params.BackupFolder, $params.DatabaseName) Cyan
}
if ($CertificateThumbprint) { $params.CertificateThumbprint = $CertificateThumbprint }
# install.ps1 keeps its own log in <install folder>\logs; the launch log stops here so that the one-time Admin password it prints at the end is not written to a file
if ($transcript) { Stop-Transcript | Out-Null; $transcript = $false }
Push-Location (Split-Path -Parent $installer)
try { & $installer @params } finally { Pop-Location }
if ($LASTEXITCODE -and $LASTEXITCODE -ne 0) { Say 'Installation reported an error. See the log in <install folder>\logs.' Red; exit 1 }

# 4. addresses ------------------------------------------------------------------------------
Say ''
Say 'PMIS is ready. Open in Chrome or Edge from any computer in the network (HTTPS):' Green
$addresses = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue | Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } | ForEach-Object { $_.IPAddress })
foreach ($a in $addresses) { Say "   https://$a/" Green }
Say "   https://$($env:COMPUTERNAME.ToLower())/" Green
Say '   On this server itself also: http://127.0.0.1/' Green
Say 'If the browser warns about the certificate, install <install folder>\certificates\innco-pmis.cer as a trusted root certificate on that computer.' Yellow
