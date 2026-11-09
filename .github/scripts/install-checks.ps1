# Checks a server installed by install.ps1 (CI only). Writes PASS/FAIL lines to install-output\checks-<Phase>.txt
param(
    [Parameter(Mandatory)][string]$Phase,
    # the temporary password given to install.ps1: the seeded Admin must change it at the first sign-in (R1a2, M7)
    [string]$AdminPassword = 'Ci#Adm1n-Install-2026',
    # what Admin chooses at the first sign-in (phase 1-first); the later phases sign in with it (it is in the database backup of phase 3 too)
    [string]$NewAdminPassword = 'Own-Chosen-Pass-2026',
    [string]$InstallRoot = 'D:\INNCO',
    [string]$BackupFolder = 'D:\INNCO\Backup'
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$out = Join-Path $PWD 'install-output'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$results = New-Object System.Collections.Generic.List[string]
$base = 'http://127.0.0.1'   # R1a2 (M6): plain HTTP is bound to the loopback address of the server itself
$hostName = $env:COMPUTERNAME.ToLowerInvariant()
$state = Join-Path $out 'state.json'
# the rule that says which backup file is a regular backup is the one the restore test uses (fix round M2)
. (Join-Path $PSScriptRoot '..\..\deploy\backup-crypto.ps1')

# The certificate of the install test is self-signed: the HTTPS checks accept any certificate (they check what is served, not who signed it).
Add-Type -TypeDefinition @'
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
public static class InstallTestTrust
{
    public static void AcceptAnyCertificate()
    {
        ServicePointManager.ServerCertificateValidationCallback = delegate (object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors errors) { return true; };
    }
}
'@
[InstallTestTrust]::AcceptAnyCertificate()
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12

function Check([string]$Name, [scriptblock]$Test) {
    try { $r = & $Test; $results.Add("PASS  $Name  $r") }
    catch { $results.Add("FAIL  $Name  : $($_.Exception.Message)") }
}

Check 'home page shows the user interface' {
    $r = Invoke-WebRequest -UseBasicParsing "$base/"
    if ($r.Content -notmatch 'id="app"') { throw 'no <div id="app">' }
    "http=$([int]$r.StatusCode)"
}
Check 'page address inside the program' {
    $r = Invoke-WebRequest -UseBasicParsing "$base/contract/update/5"
    if ($r.Content -notmatch 'id="app"') { throw 'not index.html' }
    "http=$([int]$r.StatusCode)"
}
Check 'javascript asset' {
    $index = (Invoke-WebRequest -UseBasicParsing "$base/").Content
    $asset = [regex]::Match($index, '/assets/index\.[0-9a-f]+\.js').Value
    $r = Invoke-WebRequest -UseBasicParsing "$base$asset"
    "$asset http=$([int]$r.StatusCode) type=$($r.Headers['Content-Type'])"
}
Check 'unknown API address answers 404' {
    try { Invoke-WebRequest -UseBasicParsing "$base/api/v1/NoSuchThing/x" | Out-Null; throw 'answered 200' }
    catch [System.Net.WebException] { $code = [int]$_.Exception.Response.StatusCode; if ($code -ne 404) { throw "http=$code" }; 'http=404' }
}

Check 'https answers with the user interface' {
    $r = Invoke-WebRequest -UseBasicParsing "https://$hostName/"
    if ($r.Content -notmatch 'id="app"') { throw 'no <div id="app"> over https' }
    "https://$hostName/ http=$([int]$r.StatusCode)"
}
Check 'https answers carry the HSTS header' {
    $r = Invoke-WebRequest -UseBasicParsing "https://$hostName/"
    $value = [string]$r.Headers['Strict-Transport-Security']
    if ($value -notmatch 'max-age=\d+') { throw "no Strict-Transport-Security header (value: '$value')" }
    $value
}
Check 'plain http is for the server itself only' {
    $address = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
                 Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
                 ForEach-Object { $_.IPAddress }) | Select-Object -First 1
    if (-not $address) { return 'no network address on this computer (nothing to test)' }
    try { $r = Invoke-WebRequest -UseBasicParsing "http://$address/" -TimeoutSec 20; throw "http://$address/ answered http=$([int]$r.StatusCode)" }
    catch [System.Net.WebException] { "http://$address/ is not served ($($_.Exception.Status))" }
}

$token = $null
Check 'sign in as Admin (the first time the program asks for a new password)' {
    if ($Phase -eq '1-first') {
        # R1a2 (M7): the Admin made by the installer must choose his own password before he gets a token
        $body = @{ userName = 'Admin'; password = $AdminPassword } | ConvertTo-Json
        $r = Invoke-WebRequest -UseBasicParsing -Method Post "$base/api/v1/Accounts/login" -ContentType 'application/json' -Body $body
        if ($r.Content -match '"accessToken"\s*:\s*"[^"]+') { throw 'the installer password gave a token at once; the seeded Admin must change it first' }
        if ($r.Content -notmatch 'MUST_CHANGE_PASSWORD') { throw "expected MUST_CHANGE_PASSWORD: $($r.Content)" }
        $body = @{ userName = 'Admin'; temporaryPassword = $AdminPassword; newPassword = $NewAdminPassword } | ConvertTo-Json
        $r = Invoke-WebRequest -UseBasicParsing -Method Post "$base/api/v1/Accounts/change-temporary-password" -ContentType 'application/json' -Body $body
        $script:token = [regex]::Match($r.Content, '"accessToken"\s*:\s*"([^"]+)"').Groups[1].Value
        if (-not $script:token) { throw "no token after choosing the new password: $($r.Content)" }
        $body = @{ userName = 'Admin'; password = $AdminPassword } | ConvertTo-Json
        $r = Invoke-WebRequest -UseBasicParsing -Method Post "$base/api/v1/Accounts/login" -ContentType 'application/json' -Body $body
        if ($r.Content -match '"accessToken"\s*:\s*"[^"]+') { throw 'the installer password still signs in after the new one was chosen' }
        "new password chosen; the installer password is dead; token length=$($script:token.Length)"
    }
    else {
        $body = @{ userName = 'Admin'; password = $NewAdminPassword } | ConvertTo-Json
        $r = Invoke-WebRequest -UseBasicParsing -Method Post "$base/api/v1/Accounts/login" -ContentType 'application/json' -Body $body
        $script:token = [regex]::Match($r.Content, '"accessToken"\s*:\s*"([^"]+)"').Groups[1].Value
        if (-not $script:token) { throw "no token: $($r.Content)" }
        "token length=$($script:token.Length)"
    }
}
Check 'the installer password is no longer in the settings file' {
    $text = Get-Content -Raw -Path "$InstallRoot\app\appsettings.Production.json"
    if ($text.Contains($AdminPassword)) { throw 'appsettings.Production.json still holds the installer password' }
    if (-not (Test-Path "$InstallRoot\admin-seed-done.txt")) { throw 'no admin-seed-done.txt marker' }
    if ($text -notmatch '"https://') { throw 'the token issuer is not an https address' }
    if ($text -notmatch '"AuditHmacKey"') { throw 'no Identity:AuditHmacKey in the settings file' }
    $json = $text | ConvertFrom-Json
    if ($json.Remote.Enabled -ne $false) { throw "Remote:Enabled is not false: $($json.Remote.Enabled)" }
    if ($null -eq $json.Dnc -or $json.Dnc.FolderRoots -ne '') { throw 'Dnc:FolderRoots is not present and empty' }
    if ($null -ne $json.Dnc.PSObject.Properties['AllowLoopback']) { throw 'Dnc:AllowLoopback is on the server' }
    'password removed, marker present, https issuer, audit key present, remote access off, no machine folder'
}
$headers = @{ Authorization = "Bearer $token" }
Check 'contracts list (database query)' {
    $r = Invoke-WebRequest -UseBasicParsing -Headers $headers "$base/api/v1/Contracts?PageNumber=1&PageSize=10"
    "http=$([int]$r.StatusCode) bytes=$($r.Content.Length)"
}

$saved = @{}
if (Test-Path $state) { (Get-Content -Raw $state | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $saved[$_.Name] = $_.Value } }
if ($Phase -eq '1-first') {
    Check 'upload a document (program writes to UploadedDocuments)' {
        $file = Join-Path $out 'ci-upload.txt'
        Set-Content -Path $file -Value 'INNCO install test file' -Encoding ASCII
        $response = & curl.exe -s -X POST -H "Authorization: Bearer $token" -F "File=@$file" "$base/api/v1/Documents?Description=ci&Name=ci-upload&Code=ci1"
        $id = [regex]::Match([string]$response, '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}').Value
        if (-not $id) { throw "upload answer: $response" }
        $saved['documentId'] = $id
        "document id=$id"
    }
}
if ($saved['documentId']) {
    Check 'download the uploaded document' {
        $r = Invoke-WebRequest -UseBasicParsing -Headers $headers "$base/api/v1/Documents?DocumentId=$($saved['documentId'])"
        $text = [Text.Encoding]::ASCII.GetString($r.Content)
        if ($text -notmatch 'INNCO install test file') { throw "content: $text" }
        "http=$([int]$r.StatusCode)"
    }
}
$saved | ConvertTo-Json | Set-Content -Path $state

Check 'uploaded files on disk' { "$(@(Get-ChildItem "$InstallRoot\app\UploadedDocuments" -File).Count) files in UploadedDocuments" }
Check 'backup task exists' { "state=$((Get-ScheduledTask -TaskName 'INNCO daily backup').State) lastResult=$((Get-ScheduledTaskInfo -TaskName 'INNCO daily backup').LastTaskResult)" }
Check 'database backup files' {
    $files = @(Get-ChildItem "$BackupFolder\database" -File | Where-Object { $_.Name -match '\.bak(\.enc)?$' } | Sort-Object LastWriteTime)
    if ($files.Count -eq 0) { throw 'no .bak or .bak.enc file' }
    ($files | ForEach-Object { "$($_.Name)=$($_.Length)" }) -join ' '
}
Check 'file backup copy' { "$(@(Get-ChildItem "$BackupFolder\files" -Recurse -File).Count) files" }

# ---- R1t (D20, D21): recovery model and log backups, encrypted backups, settings copy without secrets, protection of the audit trail ----
$settingsFile = "$InstallRoot\app\appsettings.Production.json"
function Invoke-CheckSql([string]$Query, [hashtable]$Parameters) {
    # the connection string of the program, used with the account that runs this script (an administrator of the instance), not with the account of the application pool
    $program = Get-Content -Raw -Path $settingsFile | ConvertFrom-Json
    $connection = New-Object System.Data.SqlClient.SqlConnection ([string]$program.ConnectionStrings.SqlServer.ApplicationDbContextConnection)
    $connection.Open()
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $Query
        if ($Parameters) { foreach ($key in $Parameters.Keys) { [void]$command.Parameters.AddWithValue($key, $Parameters[$key]) } }
        $table = New-Object System.Data.DataTable
        $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $command
        [void]$adapter.Fill($table)
        return , $table
    }
    finally { $connection.Dispose() }
}
function Get-CheckScalar([string]$Query) { $t = Invoke-CheckSql $Query $null; if ($t.Rows.Count -eq 0) { return $null }; return $t.Rows[0][0] }

$dailyTask = Get-ScheduledTask -TaskName 'INNCO daily backup' -ErrorAction SilentlyContinue
$encryptionAsked = $dailyTask -and ([string]($dailyTask.Actions | Select-Object -First 1).Arguments) -match '-EncryptionCertificate'

Check 'the database is in the full recovery model' {
    $model = [string](Get-CheckScalar 'SELECT recovery_model_desc FROM sys.databases WHERE name = DB_NAME()')
    if ($model -ne 'FULL') { throw "recovery model is '$model'" }
    $model
}
Check 'the hourly log backup task repeats every hour' {
    $task = Get-ScheduledTask -TaskName 'INNCO hourly log backup'
    $trigger = $task.Triggers | Select-Object -First 1
    $interval = [string]$trigger.Repetition.Interval
    if ($interval -ne 'PT1H') { throw "repetition interval is '$interval'" }
    if (([string]($task.Actions | Select-Object -First 1).Arguments) -notmatch '-Mode Log') { throw 'the task does not run backup.ps1 -Mode Log' }
    "state=$($task.State) lastResult=$((Get-ScheduledTaskInfo -TaskName 'INNCO hourly log backup').LastTaskResult)"
}
Check 'a log backup was made (database\log)' {
    $files = @(Get-ChildItem "$BackupFolder\database\log" -File -ErrorAction Stop | Where-Object { $_.Name -match '\.trn(\.enc)?$' } | Sort-Object LastWriteTime)
    if ($files.Count -eq 0) { throw 'no .trn or .trn.enc file' }
    ($files | ForEach-Object { "$($_.Name)=$($_.Length)" }) -join ' '
}
Check 'the backup log says the full and the log backup were made' {
    $text = Get-Content -Raw -Path "$BackupFolder\backup-log.txt"
    if ($text -notmatch 'Database backup OK') { throw 'no "Database backup OK" line' }
    if ($text -notmatch 'Log backup OK') { throw 'no "Log backup OK" line' }
    if ($text -match 'BACKUP FAILED') { throw 'the log has a "BACKUP FAILED" line' }
    'both kinds of backup are in backup-log.txt'
}
Check 'database backups are encrypted: by SQL Server where the edition can, else at file level (no plain .bak or .trn stays)' {
    $newest = Get-ChildItem "$BackupFolder\database" -File | Where-Object { Test-RegularBackupName $_.Name } | Sort-Object LastWriteTime | Select-Object -Last 1
    if ($encryptionAsked) {
        $header = Invoke-CheckSql 'RESTORE HEADERONLY FROM DISK = @f' @{ '@f' = $newest.FullName }
        $algorithm = [string]$header.Rows[0]['KeyAlgorithm']
        if ($algorithm -notmatch '(?i)aes_256') { throw "the backup task asks for encryption but $($newest.Name) has KeyAlgorithm '$algorithm'" }
        return "$($newest.Name): $algorithm, encryptor type $($header.Rows[0]['EncryptorType'])"
    }
    # file level: the newest backup is x.bak.enc, with the PMISENC1 header, and no plain .bak / .trn is left in the backup folders (the safety copies are encrypted as well)
    if ($newest.Name -notlike '*.bak.enc') { throw "the newest backup $($newest.Name) is not encrypted" }
    $head = New-Object byte[] 8
    $stream = [IO.File]::OpenRead($newest.FullName); try { [void]$stream.Read($head, 0, 8) } finally { $stream.Dispose() }
    if ([Text.Encoding]::ASCII.GetString($head) -ne 'PMISENC1') { throw "$($newest.Name) does not start with the PMISENC1 header" }
    $plain = @(Get-ChildItem "$BackupFolder\database" -Recurse -File | Where-Object { $_.Name -match '\.(bak|trn)$' })
    if ($plain.Count -gt 0) { throw "plain backup files are left: $(($plain | ForEach-Object Name) -join ', ')" }
    "$($newest.Name): file level (AES-256 with an HMAC seal), no plain .bak or .trn in the backup folder"
}
Check 'the uploaded files in the backup are encrypted at file level' {
    $copies = @(Get-ChildItem "$BackupFolder\files" -Recurse -File | Where-Object { $_.Name -ne 'appsettings.Production.redacted.json' })
    $onDisk = @(Get-ChildItem "$InstallRoot\app\UploadedDocuments" -Recurse -File -ErrorAction SilentlyContinue).Count
    if ($copies.Count -eq 0 -and $onDisk -gt 0) {
        # the document of the upload check above was written after the installer's own first backup: run the daily backup once more, then look again
        Start-ScheduledTask -TaskName 'INNCO daily backup'; Start-Sleep -Seconds 5
        $until = (Get-Date).AddMinutes(10)
        do { Start-Sleep -Seconds 3 } while ((Get-ScheduledTask -TaskName 'INNCO daily backup').State -eq 'Running' -and (Get-Date) -lt $until)
        $copies = @(Get-ChildItem "$BackupFolder\files" -Recurse -File | Where-Object { $_.Name -ne 'appsettings.Production.redacted.json' })
    }
    if ($copies.Count -eq 0) { throw "no uploaded file in the files backup ($onDisk file(s) are in UploadedDocuments)" }
    $plain = @($copies | Where-Object { $_.Name -notlike '*.enc' })
    if ($plain.Count -gt 0) { throw "$($plain.Count) file(s) of the files backup are not encrypted, for example $($plain[0].FullName)" }
    "$($copies.Count) files, all .enc"
}
Check 'the keys folder holds the file key, and the off-site copy never takes the keys' {
    if (-not (Test-Path "$BackupFolder\keys\file-encryption.key.dpapi")) { throw "missing $BackupFolder\keys\file-encryption.key.dpapi" }
    $script = Get-Content -Raw -Path "$InstallRoot\tools\backup.ps1" -ErrorAction SilentlyContinue
    if ($script -and $script -match "@\('database', 'files', 'keys'\)") { throw 'backup.ps1 still copies the keys folder off-site' }
    'file-encryption.key.dpapi is there; backup.ps1 copies database and files only'
}
Check 'the backup certificate and its key are in the keys folder, and the audit key is sealed there' {
    $names = @('audit-key.sealed.txt')
    $hasCertificate = Test-Path "$BackupFolder\keys\INNCO_BackupCert.cer"
    if ($hasCertificate) { $names = @('INNCO_BackupCert.cer', 'INNCO_BackupCert.pvk') + $names }
    foreach ($name in $names) {
        if (-not (Test-Path "$BackupFolder\keys\$name")) { throw "missing $BackupFolder\keys\$name" }
    }
    $sealed = Get-Content -Raw -Path "$BackupFolder\keys\audit-key.sealed.txt"
    $real = Get-Content -Raw -Path $settingsFile | ConvertFrom-Json
    if ($real.Identity.AuditHmacKey -and $sealed.Contains([string]$real.Identity.AuditHmacKey)) { throw 'audit-key.sealed.txt holds the audit key in clear text' }
    if ($sealed -notmatch 'sealed-with-file-key: \S+') { throw 'audit-key.sealed.txt has no seal with the backup file key' }
    if ($hasCertificate) { 'INNCO_BackupCert.cer, INNCO_BackupCert.pvk and audit-key.sealed.txt (sealed with the file key' + $(if ($sealed -match 'sealed-with-certificate: \S+') { ' and the certificate)' } else { ')' }) }
    else { 'audit-key.sealed.txt (sealed with the file key); no backup certificate on this server' }
}
Check 'the settings copy of the backup holds no secret' {
    $copy = "$BackupFolder\files\appsettings.Production.redacted.json"
    if (-not (Test-Path $copy)) { throw 'no appsettings.Production.redacted.json in the files backup' }
    if (Test-Path "$BackupFolder\files\appsettings.Production.json") { throw 'the files backup holds appsettings.Production.json itself' }
    $text = Get-Content -Raw -Path $copy
    $real = Get-Content -Raw -Path $settingsFile | ConvertFrom-Json
    foreach ($secret in @($real.BearerTokens.Key, $real.Identity.AuditHmacKey, $real.AdminUserSeed.Password)) {
        if ($secret -and $text.Contains([string]$secret)) { throw 'a secret of the settings file is in the copy' }
    }
    if ($text -notmatch 'REDACTED') { throw 'the copy has no REDACTED marker (nothing was replaced?)' }
    if ($text -notmatch '(?i)ApplicationDbContextConnection') { throw 'the copy lost the structure of the settings' }
    'secrets replaced by REDACTED; the real file is not in the backup'
}
Check 'the install log that holds the backup key password is readable by administrators only' {
    $logs = @(Get-ChildItem "$InstallRoot\logs" -Filter 'install-*.log' -ErrorAction Stop | Where-Object { Select-String -Path $_.FullName -SimpleMatch 'PASSWORD OF THE BACKUP KEY' -Quiet })
    if ($logs.Count -eq 0) { return 'no install log shows a key password (the certificate was made in an earlier run, or encryption is not used)' }
    foreach ($log in $logs) {
        $acl = (& icacls.exe $log.FullName) -join ' '
        if ($acl -match '(?i)APPPOOL|Everyone|BUILTIN\\Users|Authenticated Users') { throw "$($log.Name) can be read by more than administrators: $acl" }
    }
    "$($logs.Count) log file(s), access: administrators and SYSTEM"
}
Check 'the audit trail is hash-chained' {
    # R1t review M11: only the rows written after the chain began are counted (the first sealed row marks the start). A database restored from a backup that was taken before the chain
    # existed has older rows without a place in the chain: they are not an error, and a trail without any sealed row yet is not a failure either.
    $sealed = [int](Get-CheckScalar 'SELECT COUNT(*) FROM dbo.FnAuditLogs WHERE Seq IS NOT NULL')
    $legacy = [int](Get-CheckScalar 'SELECT COUNT(*) FROM dbo.FnAuditLogs WHERE Seq IS NULL')
    if ($sealed -eq 0) { return "skipped: no row has a place in the chain yet ($legacy rows from before the chain; the chain starts with the next audited change)" }
    $firstId = [int](Get-CheckScalar 'SELECT MIN(Id) FROM dbo.FnAuditLogs WHERE Seq IS NOT NULL')
    $after = [int](Get-CheckScalar "SELECT COUNT(*) FROM dbo.FnAuditLogs WHERE Id >= $firstId")
    $loose = [int](Get-CheckScalar "SELECT COUNT(*) FROM dbo.FnAuditLogs WHERE Id >= $firstId AND (Seq IS NULL OR Hash IS NULL)")
    if ($loose -ne 0) { throw "$loose of $after rows written since the chain began have no place in the chain" }
    $forks = [int](Get-CheckScalar 'SELECT COUNT(*) FROM (SELECT Seq FROM dbo.FnAuditLogs WHERE Seq IS NOT NULL GROUP BY Seq HAVING COUNT(*) > 1) d')
    if ($forks -ne 0) { throw "$forks places in the chain hold more than one row" }
    "$after rows since the chain began, every one chained, no fork; $legacy older rows from before the chain are not counted"
}
Check 'the database refuses the program account to change or delete a row of the audit trail' {
    $user = [string](Get-CheckScalar "SELECT TOP 1 u.name FROM sys.database_role_members m JOIN sys.database_principals r ON r.principal_id = m.role_principal_id JOIN sys.database_principals u ON u.principal_id = m.member_principal_id WHERE r.name = N'pmis_app'")
    if (-not $user) { throw 'no account is a member of the role pmis_app' }
    $quoted = $user.Replace("'", "''")
    $denied = [int](Get-CheckScalar "SELECT COUNT(*) FROM sys.database_permissions p JOIN sys.database_principals g ON g.principal_id = p.grantee_principal_id WHERE g.name = N'pmis_app' AND p.class = 1 AND p.major_id = OBJECT_ID(N'dbo.FnAuditLogs') AND p.state = 'D' AND p.permission_name IN ('UPDATE', 'DELETE')")
    if ($denied -ne 2) { throw "expected DENY UPDATE and DENY DELETE on dbo.FnAuditLogs for the role pmis_app, found $denied" }
    $owner = [int](Get-CheckScalar "SELECT COUNT(*) FROM sys.database_role_members m JOIN sys.database_principals r ON r.principal_id = m.role_principal_id WHERE r.name = N'db_owner' AND m.member_principal_id = USER_ID(N'$quoted')")
    if ($owner -ne 0) { throw "$user is db_owner" }
    $code = [int](Get-CheckScalar "DECLARE @n int = 0; BEGIN TRY EXECUTE AS USER = N'$quoted'; UPDATE dbo.FnAuditLogs SET Summary = Summary WHERE 1 = 0; REVERT; END TRY BEGIN CATCH SET @n = ERROR_NUMBER(); REVERT; END CATCH; SELECT @n;")
    if ($code -ne 229) { throw "an UPDATE as $user gave SQL error $code (229 = refused was expected)" }
    "$user is in pmis_app and not db_owner; DENY UPDATE, DELETE on dbo.FnAuditLogs; a test UPDATE was refused (229)"
}
Check 'the program does not change the database structure itself (Database:MigrateAtStart is false)' {
    $real = Get-Content -Raw -Path $settingsFile | ConvertFrom-Json
    $value = $real.Database.MigrateAtStart
    if ($null -eq $value -or $value -ne $false) { throw "Database:MigrateAtStart is '$value'" }
    'Database:MigrateAtStart = false; the installer applied the updates (WebApi.dll --migrate-only)'
}
Check 'the backup and restore-test account NT AUTHORITY\SYSTEM is NOT a sysadmin, but may create databases (dbcreator) and read the restore-test tables' {
    $sys = [int](Get-CheckScalar "SELECT IS_SRVROLEMEMBER('sysadmin', N'NT AUTHORITY\SYSTEM')")
    if ($sys -ne 0) { throw "SYSTEM is a member of sysadmin (IS_SRVROLEMEMBER=$sys)" }
    $dbc = [int](Get-CheckScalar "SELECT IS_SRVROLEMEMBER('dbcreator', N'NT AUTHORITY\SYSTEM')")
    if ($dbc -ne 1) { throw "SYSTEM is not a member of dbcreator (IS_SRVROLEMEMBER=$dbc)" }
    $read = [int](Get-CheckScalar "SELECT COUNT(*) FROM sys.database_permissions p JOIN sys.database_principals g ON g.principal_id = p.grantee_principal_id WHERE g.name = N'NT AUTHORITY\SYSTEM' AND p.permission_name = N'SELECT' AND p.state = 'G' AND p.class = 1 AND p.major_id IN (OBJECT_ID(N'dbo.FnAuditLogs'), OBJECT_ID(N'dbo.__EFMigrationsHistory'))")
    if ($read -ne 2) { throw "expected SELECT on dbo.FnAuditLogs and dbo.__EFMigrationsHistory for SYSTEM, found $read" }
    "sysadmin=0 dbcreator=1 SELECT on the compared tables"
}
if ($Phase -eq '2-update') {
    Check 'the update made a checked safety backup of the database before it changed anything' {
        $f = @(Get-ChildItem "$BackupFolder\database" -File | Where-Object { $_.Name -match 'pre-update.*\.bak(\.enc)?$' })
        if ($f.Count -eq 0) { throw 'no pre-update backup file (.bak or .bak.enc)' }
        ($f | ForEach-Object { "$($_.Name)=$($_.Length)" }) -join ' '
    }
}

if ($Phase -eq '3-restore') {
    Check 'old program files were brought over' {
        foreach ($f in "$InstallRoot\app\UploadedDocuments\old-upload.txt", "$InstallRoot\app\wwwroot\Documents\old-doc.txt") {
            if (-not (Test-Path $f)) { throw "missing $f" }
        }
        'old-upload.txt and old-doc.txt present'
    }
    Check 'old document served as a static file' {
        $r = Invoke-WebRequest -UseBasicParsing "$base/Documents/old-doc.txt"
        "http=$([int]$r.StatusCode)"
    }
    Check 'safety backup made before the restore' {
        $f = @(Get-ChildItem "$BackupFolder\database" -File | Where-Object { $_.Name -match 'before-restore.*\.bak(\.enc)?$' })
        if ($f.Count -eq 0) { throw 'none' }
        $f[0].Name
    }
}

if ($Phase -eq '5-restore-test') {
    $restoreDir = Join-Path $BackupFolder 'restore-test'
    $script:restoreResult = $null
    Check 'the monthly restore test task is registered (day 1, 03:00, SYSTEM)' {
        $t = Get-ScheduledTask -TaskName 'INNCO monthly restore test'
        [xml]$x = [string](Export-ScheduledTask -TaskName 'INNCO monthly restore test')   # a monthly trigger is described only in the task XML
        $cal = @($x.Task.Triggers.CalendarTrigger)
        if ($cal.Count -ne 1) { throw "calendar triggers=$($cal.Count)" }
        if ($null -eq $cal[0].ScheduleByMonth) { throw 'the trigger is not monthly' }
        if ([string]$cal[0].ScheduleByMonth.DaysOfMonth.Day -ne '1') { throw "days of month=$($cal[0].ScheduleByMonth.DaysOfMonth.Day)" }
        if (@($cal[0].ScheduleByMonth.Months.ChildNodes).Count -ne 12) { throw "months=$(@($cal[0].ScheduleByMonth.Months.ChildNodes).Count)" }
        if (([datetime]$cal[0].StartBoundary).Hour -ne 3) { throw "start=$($cal[0].StartBoundary)" }
        if ($t.Principal.UserId -notmatch 'SYSTEM') { throw "runs as $($t.Principal.UserId)" }
        "state=$($t.State) lastResult=$((Get-ScheduledTaskInfo -TaskName 'INNCO monthly restore test').LastTaskResult)"
    }
    Check 'the restore test passed (task result 0)' {
        $r = (Get-ScheduledTaskInfo -TaskName 'INNCO monthly restore test').LastTaskResult
        if ($r -ne 0) { throw "last result=$r" }
        'last result=0'
    }
    Check 'the result file says the backup restored and every check passed' {
        $f = Get-ChildItem $restoreDir -Filter 'restore-test-*.json' -File | Sort-Object LastWriteTime | Select-Object -Last 1
        if (-not $f) { throw "no restore-test-*.json in $restoreDir" }
        $j = Get-Content -Raw -Path $f.FullName | ConvertFrom-Json
        $script:restoreResult = $j
        if (-not $j.success) { throw "success=false: $($j.message)" }
        if (-not $j.migrationsMatch) { throw 'migrationsMatch=false' }
        if (-not $j.chainOk) { throw 'chainOk=false' }
        if (-not $j.scratchDropped) { throw 'scratchDropped=false' }
        if (@($j.checks | Where-Object { -not $_.passed }).Count -gt 0) { throw 'a check failed' }
        "$($f.Name) backup=$($j.backupFile) chainEntries=$($j.chainEntriesChecked) checks=$(@($j.checks).Count)"
    }
    Check 'the backup used was the newest one and its encrypted form was opened when it is .enc' {
        if (-not $script:restoreResult) { throw 'no result' }
        $newest = Get-ChildItem "$BackupFolder\database" -File | Where-Object { (Test-RegularBackupName $_.Name) } | Sort-Object LastWriteTime | Select-Object -Last 1
        if ($newest.Name -ne $script:restoreResult.backupFile) { throw "newest=$($newest.Name) used=$($script:restoreResult.backupFile)" }
        if ($newest.Name -like '*.enc' -and -not (@($script:restoreResult.checks | Where-Object { $_.name -eq 'decrypt' -and $_.passed }).Count)) { throw 'no decrypt check' }
        $newest.Name
    }
    Check 'the decrypted work folder is restricted (Administrators, SYSTEM, the SQL service only; not the program account) and empty after the run' {
        if (-not $script:restoreResult) { throw 'no result' }
        $wf = [string]$script:restoreResult.workFolder
        if ($wf -ne (Join-Path $BackupFolder 'restore-work')) { throw "workFolder='$wf'" }
        if ($wf -like "$restoreDir*") { throw 'the work folder is inside the result folder the program can read' }
        $acl = Get-Acl $wf
        if (-not $acl.AreAccessRulesProtected) { throw 'the work folder inherits permissions' }
        $names = @($acl.Access | ForEach-Object { $_.IdentityReference.Value })
        $bad = @($names | Where-Object { $_ -match 'Users$|Everyone|Authenticated Users|IIS APPPOOL' })
        if ($bad.Count -gt 0) { throw "too open: $($bad -join ', ')" }
        $left = @(Get-ChildItem $wf -Force -ErrorAction SilentlyContinue)
        if ($left.Count -gt 0) { throw "$($left.Count) item(s) left in the work folder" }
        "acl=" + ($names -join '; ')
    }
    Check 'the restore test ran as SYSTEM without sysadmin (the task result above is the proof: restore, read of the live tables and drop all worked with dbcreator + SELECT only)' {
        $sys = [int](Get-CheckScalar "SELECT IS_SRVROLEMEMBER('sysadmin', N'NT AUTHORITY\SYSTEM')")
        if ($sys -ne 0) { throw 'SYSTEM is a sysadmin' }
        if (-not $script:restoreResult.success) { throw 'the test did not pass' }
        'sysadmin=0, success=true'
    }
    Check 'the scratch database IRNNCO_RESTORETEST is gone' {
        $c = New-Object System.Data.SqlClient.SqlConnection 'Data Source=.\SQLEXPRESS;Initial Catalog=master;Integrated Security=True;Connect Timeout=30'
        $c.Open()
        try { $cmd = $c.CreateCommand(); $cmd.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = N'IRNNCO_RESTORETEST'"; $n = [int]$cmd.ExecuteScalar() } finally { $c.Dispose() }
        if ($n -ne 0) { throw "sys.databases has $n row(s) named IRNNCO_RESTORETEST" }
        'not in sys.databases'
    }
    Check 'the program is told where the result files are (RestoreTest:ResultDirectory)' {
        $real = Get-Content -Raw -Path "$InstallRoot\app\appsettings.Production.json" | ConvertFrom-Json
        if ($real.RestoreTest.ResultDirectory -ne $restoreDir) { throw "ResultDirectory='$($real.RestoreTest.ResultDirectory)'" }
        $real.RestoreTest.ResultDirectory
    }
    Check 'the program reads the result and shows it (import, then summary: restore status Ok)' {
        $r = Invoke-RestMethod -UseBasicParsing -Method Post -Headers $headers "$base/api/v1/integrity/restore-tests/import"
        if (-not $r.success) { throw "import: $($r.message)" }
        $s = Invoke-RestMethod -UseBasicParsing -Headers $headers "$base/api/v1/integrity/summary"
        if (-not $s.success) { throw "summary: $($s.message)" }
        if ($s.data.restoreStatus -ne 'Ok') { throw "restoreStatus=$($s.data.restoreStatus)" }
        if ($s.data.red) { throw 'the summary is red' }
        "imported=$($r.data.imported) restoreStatus=$($s.data.restoreStatus)"
    }
}

$file = Join-Path $out "checks-$Phase.txt"
$results | Set-Content -Path $file
$results | ForEach-Object { Write-Host $_ }
if (@($results | Where-Object { $_ -like 'FAIL*' }).Count -gt 0) { exit 1 }
exit 0
