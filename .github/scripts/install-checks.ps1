# Checks a server installed by install.ps1 (CI only). Writes PASS/FAIL lines to install-output\checks-<Phase>.txt
param(
    [Parameter(Mandatory)][string]$Phase,
    [string]$AdminPassword = 'Ci#Adm1n-Install-2026',
    [string]$InstallRoot = 'D:\INNCO',
    [string]$BackupFolder = 'D:\INNCO\Backup'
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$out = Join-Path $PWD 'install-output'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$results = New-Object System.Collections.Generic.List[string]
$base = 'http://localhost'
$state = Join-Path $out 'state.json'

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

$token = $null
Check 'sign in as Admin' {
    $body = @{ userName = 'Admin'; password = $AdminPassword } | ConvertTo-Json
    $r = Invoke-WebRequest -UseBasicParsing -Method Post "$base/api/v1/Accounts/login" -ContentType 'application/json' -Body $body
    $script:token = [regex]::Match($r.Content, '"accessToken"\s*:\s*"([^"]+)"').Groups[1].Value
    if (-not $script:token) { throw "no token: $($r.Content)" }
    "token length=$($script:token.Length)"
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
