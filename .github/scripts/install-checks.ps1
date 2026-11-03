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
    'password removed, marker present, https issuer, audit key present'
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
    $files = @(Get-ChildItem "$BackupFolder\database" -Filter '*.bak' | Sort-Object LastWriteTime)
    if ($files.Count -eq 0) { throw 'no .bak file' }
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
    $files = @(Get-ChildItem "$BackupFolder\database\log" -Filter '*.trn' -ErrorAction Stop | Sort-Object LastWriteTime)
    if ($files.Count -eq 0) { throw 'no .trn file' }
    ($files | ForEach-Object { "$($_.Name)=$($_.Length)" }) -join ' '
}
Check 'the backup log says the full and the log backup were made' {
    $text = Get-Content -Raw -Path "$BackupFolder\backup-log.txt"
    if ($text -notmatch 'Database backup OK') { throw 'no "Database backup OK" line' }
    if ($text -notmatch 'Log backup OK') { throw 'no "Log backup OK" line' }
    if ($text -match 'BACKUP FAILED') { throw 'the log has a "BACKUP FAILED" line' }
    'both kinds of backup are in backup-log.txt'
}
Check 'database backups are encrypted (AES-256) when the installer found that this server can' {
    $newest = Get-ChildItem "$BackupFolder\database" -Filter '*.bak' | Sort-Object LastWriteTime | Select-Object -Last 1
    $header = Invoke-CheckSql 'RESTORE HEADERONLY FROM DISK = @f' @{ '@f' = $newest.FullName }
    $algorithm = [string]$header.Rows[0]['KeyAlgorithm']
    if ($encryptionAsked) {
        if ($algorithm -notmatch '(?i)aes_256') { throw "the backup task asks for encryption but $($newest.Name) has KeyAlgorithm '$algorithm'" }
        "$($newest.Name): $algorithm, encryptor type $($header.Rows[0]['EncryptorType'])"
    }
    else { "NOT encrypted: the installer found that encrypted backups do not work on this server (see the Warning in the install log). $($newest.Name): KeyAlgorithm '$algorithm'" }
}
Check 'the backup certificate and its key are in the keys folder' {
    if (-not $encryptionAsked) { return 'encryption is not used on this server' }
    foreach ($name in 'INNCO_BackupCert.cer', 'INNCO_BackupCert.pvk', 'audit-key.sealed.txt') {
        if (-not (Test-Path "$BackupFolder\keys\$name")) { throw "missing $BackupFolder\keys\$name" }
    }
    $sealed = Get-Content -Raw -Path "$BackupFolder\keys\audit-key.sealed.txt"
    $real = Get-Content -Raw -Path $settingsFile | ConvertFrom-Json
    if ($real.Identity.AuditHmacKey -and $sealed.Contains([string]$real.Identity.AuditHmacKey)) { throw 'audit-key.sealed.txt holds the audit key in clear text' }
    'INNCO_BackupCert.cer, INNCO_BackupCert.pvk and audit-key.sealed.txt (sealed)'
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
    $total = [int](Get-CheckScalar 'SELECT COUNT(*) FROM dbo.FnAuditLogs')
    if ($total -eq 0) { throw 'the audit trail has no row (the sign-in of the checks writes one)' }
    $loose = [int](Get-CheckScalar 'SELECT COUNT(*) FROM dbo.FnAuditLogs WHERE Seq IS NULL OR Hash IS NULL')
    if ($loose -ne 0) { throw "$loose of $total rows have no place in the chain" }
    $forks = [int](Get-CheckScalar 'SELECT COUNT(*) FROM (SELECT Seq FROM dbo.FnAuditLogs GROUP BY Seq HAVING COUNT(*) > 1) d')
    if ($forks -ne 0) { throw "$forks places in the chain hold more than one row" }
    "$total rows, every one chained, no fork"
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
if ($Phase -eq '2-update') {
    Check 'the update made a checked safety backup of the database before it changed anything' {
        $f = @(Get-ChildItem "$BackupFolder\database" -Filter '*pre-update*.bak')
        if ($f.Count -eq 0) { throw 'no *pre-update*.bak file' }
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
        $f = @(Get-ChildItem "$BackupFolder\database" -Filter '*before-restore*.bak')
        if ($f.Count -eq 0) { throw 'none' }
        $f[0].Name
    }
}

$file = Join-Path $out "checks-$Phase.txt"
$results | Set-Content -Path $file
$results | ForEach-Object { Write-Host $_ }
if (@($results | Where-Object { $_ -like 'FAIL*' }).Count -gt 0) { exit 1 }
exit 0
