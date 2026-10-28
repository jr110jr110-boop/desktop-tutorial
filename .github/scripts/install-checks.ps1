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
