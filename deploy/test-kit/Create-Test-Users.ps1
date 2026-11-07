#Requires -Version 5.1
<#
  INNCO PMIS - creates the test users (one per unit) through the real API of an installed PMIS.
  The Admin password is typed here (or given with -AdminPassword) and is never stored.
  The users and their passwords come from Test-Users.xlsx (or a CSV file with the same columns:
  username,password,roleIds,displayName,sensitive,phone,nationalCode,email).
  Console text is plain English on purpose: the Windows console often cannot show Persian letters.

  Examples:
      .\Create-Test-Users.ps1
      .\Create-Test-Users.ps1 -BaseUrl https://pmis-test -UsersFile .\Test-Users.xlsx
#>
[CmdletBinding()]
param(
    [string]$UsersFile = '',
    [string]$BaseUrl = '',
    [string]$AdminUser = 'Admin',
    [string]$AdminPassword = '',
    [switch]$ForceChange   # non-sensitive users get a temporary password (valid 24 h) and must change it at the first sign-in
)
$ErrorActionPreference = 'Stop'
$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $UsersFile) {
    $UsersFile = Join-Path $Here 'Test-Users.xlsx'
    if (-not (Test-Path $UsersFile) -and (Test-Path (Join-Path $Here 'Test-Users.csv'))) { $UsersFile = Join-Path $Here 'Test-Users.csv' }
}
try { Get-ChildItem -Path $Here -File -ErrorAction SilentlyContinue | Unblock-File -ErrorAction SilentlyContinue } catch { }

# Persian words used in the data, written as character codes so that this file stays plain ASCII
$FaYes  = -join ([char[]](0x0628, 0x0644, 0x0647))                 # "yes" in the "sensitive" column
$FaTest = -join ([char[]](0x0622, 0x0632, 0x0645, 0x0648, 0x0646)) # "test", used as the last name

# the site uses its own self-signed certificate: accept it for this run only (this script talks only to the address you type)
$script:TlsArgs = @{}
if ($PSVersionTable.PSVersion.Major -ge 6) { $script:TlsArgs['SkipCertificateCheck'] = $true }
else {
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    if (-not ('PmisTrustAll' -as [type])) {
        Add-Type -TypeDefinition @'
using System.Net; using System.Net.Security; using System.Security.Cryptography.X509Certificates;
public static class PmisTrustAll { public static void Enable() { ServicePointManager.ServerCertificateValidationCallback = delegate (object s, X509Certificate c, X509Chain ch, SslPolicyErrors e) { return true; }; } }
'@
    }
    [PmisTrustAll]::Enable()
}
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Read-Xlsx([string]$path) {
    $zip = [System.IO.Compression.ZipFile]::OpenRead($path)
    try {
        $shared = @()
        $entry = $zip.GetEntry('xl/sharedStrings.xml')
        if ($entry) {
            $reader = New-Object System.IO.StreamReader($entry.Open(), [System.Text.Encoding]::UTF8); $xml = [xml]$reader.ReadToEnd(); $reader.Close()
            foreach ($si in $xml.sst.si) {
                if ($si.t) { $t = if ($si.t -is [string]) { $si.t } else { [string]$si.t.InnerText } }
                else { $t = (@($si.r) | ForEach-Object { if ($_.t -is [string]) { $_.t } else { $_.t.InnerText } }) -join '' }
                $shared += [string]$t
            }
        }
        $entry = $zip.GetEntry('xl/worksheets/sheet1.xml')
        $reader = New-Object System.IO.StreamReader($entry.Open(), [System.Text.Encoding]::UTF8); $xml = [xml]$reader.ReadToEnd(); $reader.Close()
        $rows = @()
        foreach ($row in @($xml.worksheet.sheetData.row)) {
            $cells = @{}
            foreach ($c in @($row.c)) {
                $col = ($c.r -replace '\d', '')
                if ($c.t -eq 's') { $v = $shared[[int]$c.v] } elseif ($c.t -eq 'inlineStr') { $v = [string]$c.is.InnerText } else { $v = [string]$c.v }
                $cells[$col] = $v
            }
            $rows += , $cells
        }
        # first row = column names
        $names = @{}; foreach ($k in $rows[0].Keys) { $names[$k] = [string]$rows[0][$k] }
        $out = @()
        for ($i = 1; $i -lt $rows.Count; $i++) {
            $o = @{}; foreach ($k in $names.Keys) { $o[$names[$k]] = [string]$rows[$i][$k] }
            $out += [pscustomobject]$o
        }
        return , $out
    } finally { $zip.Dispose() }
}

function Call-Api([string]$method, [string]$path, $body, [string]$token) {
    $uri = $script:Api + '/' + $path
    $headers = @{}; if ($token) { $headers['Authorization'] = 'Bearer ' + $token }
    try {
        if ($null -ne $body) {
            $json = $body | ConvertTo-Json -Depth 6 -Compress
            return Invoke-RestMethod -UseBasicParsing -Method $method -Uri $uri -Headers $headers -ContentType 'application/json; charset=utf-8' -Body ([System.Text.Encoding]::UTF8.GetBytes($json)) -TimeoutSec 60 @script:TlsArgs
        }
        return Invoke-RestMethod -UseBasicParsing -Method $method -Uri $uri -Headers $headers -TimeoutSec 60 @script:TlsArgs
    } catch {
        $msg = $_.Exception.Message
        $parsed = $null
        if ($_.ErrorDetails -and $_.ErrorDetails.Message) {
            $msg = $_.ErrorDetails.Message
            try { $parsed = $msg | ConvertFrom-Json } catch { $parsed = $null }
        }
        if ($parsed -and $parsed.PSObject.Properties['message']) {
            $parsed | Add-Member -NotePropertyName transport -NotePropertyValue $false -Force
            if ($parsed.PSObject.Properties['success']) { $parsed.success = $false } else { $parsed | Add-Member -NotePropertyName success -NotePropertyValue $false }
            return $parsed
        }
        return [pscustomobject]@{ success = $false; message = $msg; data = $null; transport = ($null -eq $_.Exception.Response) }
    }
}
function Sign-In([string]$user, [string]$password) {
    return (Call-Api POST 'Accounts/login' @{ userName = $user; password = $password } $null)
}
function Plain([Security.SecureString]$s) { [Runtime.InteropServices.Marshal]::PtrToStringBSTR([Runtime.InteropServices.Marshal]::SecureStringToBSTR($s)) }
function Stop-Here([string]$text) { Write-Host $text -ForegroundColor Red; exit 1 }

Write-Host 'INNCO PMIS - create the test users' -ForegroundColor Cyan
if (-not (Test-Path $UsersFile)) { Stop-Here "The users file was not found: $UsersFile (keep Test-Users.xlsx next to this script)." }
$list = if ($UsersFile -like '*.csv') { @(Import-Csv -Path $UsersFile -Encoding UTF8) } else { Read-Xlsx $UsersFile }
$users = @()
foreach ($r in $list) {
    if (-not $r.username) { continue }
    $users += [pscustomobject]@{
        User = $r.username.Trim(); Pw = $r.password; Roles = @(($r.roleIds -split '[,;]') | Where-Object { $_.Trim() } | ForEach-Object { [int]$_.Trim() })
        Sens = ($r.sensitive -eq $FaYes -or $r.sensitive -eq 'yes' -or $r.sensitive -eq 'true'); Title = $r.displayName
        Phone = $r.phone; Nid = $r.nationalCode; Email = $r.email
    }
}
Write-Host ("Users in the file: " + $users.Count)
if ($users.Count -eq 0) { Stop-Here 'The users file has no rows.' }

if (-not $BaseUrl) {
    Write-Host 'Server address: on the server itself type  http://127.0.0.1  - from another computer type  https://<server name or IP>'
    $BaseUrl = Read-Host 'Server address'
}
$BaseUrl = $BaseUrl.Trim().TrimEnd('/')
if ($BaseUrl -notmatch '^https?://') { $BaseUrl = $(if ($BaseUrl -match '^(127\.0\.0\.1|localhost)([:/]|$)') { 'http://' } else { 'https://' }) + $BaseUrl }
$script:Api = if ($BaseUrl -match '/api/v\d+$') { $BaseUrl } else { $BaseUrl + '/api/v1' }

# the server must answer before anything else
$probe = Call-Api POST 'Accounts/login' @{ userName = '-'; password = '-' } $null
if ($probe.transport) { Stop-Here ("The server did not answer at $BaseUrl : " + $probe.message + "`nCheck the address (on the server itself use http://127.0.0.1) and that the IIS site INNCO is started.") }

if (-not $AdminPassword) { $AdminPassword = Plain (Read-Host "Password of $AdminUser (not shown, not stored)" -AsSecureString) }
$login = Sign-In $AdminUser $AdminPassword
if (-not $login.success -and ("$($login.message) $(($login.data | ConvertTo-Json -Depth 4 -Compress)) $(($login.errors | ConvertTo-Json -Depth 4 -Compress)) $($login.code)" -match 'MUST_CHANGE_PASSWORD')) {
    Write-Host "$AdminUser still has the temporary password of the installation; choose the new password now." -ForegroundColor Yellow
    $new1 = Plain (Read-Host 'New password (10+ characters, letters and digits)' -AsSecureString)
    $new2 = Plain (Read-Host 'New password again' -AsSecureString)
    if ($new1 -ne $new2) { Stop-Here 'The two passwords are different. Run the script again.' }
    $ch = Call-Api POST 'Accounts/change-temporary-password' @{ userName = $AdminUser; temporaryPassword = $AdminPassword; newPassword = $new1 } $null
    if (-not $ch.success) { Stop-Here ("The password was not changed: " + $ch.message) }
    $AdminPassword = $new1
    $login = Sign-In $AdminUser $AdminPassword
}
if (-not $login.success -or -not $login.data.accessToken) { Stop-Here ("Sign-in of $AdminUser failed: " + $login.message) }
$adminToken = $login.data.accessToken
$AdminPassword = $null
Write-Host "Signed in as $AdminUser." -ForegroundColor Green

function Register($u, [string]$pw) {
    $body = @{ userName = $u.User; name = $u.Title; lastName = $FaTest; email = $u.Email; roleIds = @($u.Roles); password = $pw
               phoneNumber = $u.Phone; birthDate = '1990-01-01T00:00:00+03:30'; nationalCode = $u.Nid }
    return (Call-Api POST 'Accounts/register-user' $body $adminToken)
}

# order: the first PMO manager (gets PMO at once while fewer than two PMO managers exist), then the other users, then the sensitive roles
# (made with a temporary password, the user sets his own, and pmo.test - a second person - approves the role; Admin, who asked, cannot)
$firstPmo = $users | Where-Object { $_.User -eq 'pmo.test' } | Select-Object -First 1
$order = @()
if ($firstPmo) { $order += $firstPmo }
$order += @($users | Where-Object { $_.User -ne 'pmo.test' -and -not $_.Sens })
$order += @($users | Where-Object { $_.User -ne 'pmo.test' -and $_.Sens })
$made = 0; $existing = 0; $failed = 0; $pending = @()
foreach ($u in $order) {
    $pw = $u.Pw
    $twoStep = $u.Sens -and $u.User -ne 'pmo.test'
    if ($twoStep) { $pw = 'Tmp-' + [guid]::NewGuid().ToString('N').Substring(0, 12) + 'aA1' }
    $r = Register $u $pw
    if (-not $r.success) {
        $faInUse = -join ([char[]](0x0627, 0x0633, 0x062A, 0x0641, 0x0627, 0x062F, 0x0647))   # "in use" (the duplicate user name message)
        $faDup   = -join ([char[]](0x062A, 0x06A9, 0x0631, 0x0627, 0x0631, 0x06CC))         # "duplicate"
        $text = "$($r.message) $(($r.errors | Out-String)) $(($r.data | Out-String))"
        if ($text -match ('DuplicateUserName|already exists|' + $faInUse + '|' + $faDup)) {
            Write-Host ("exists already, skipped: " + $u.User) -ForegroundColor Yellow; $existing++; continue
        }
        Write-Host ("FAILED " + $u.User + " : " + $r.message) -ForegroundColor Red; $failed++; continue
    }
    $id = $r.data.id
    if ($twoStep) {
        $t = Sign-In $u.User $pw
        if (-not $t.success) { Write-Host ("FAILED " + $u.User + " : sign-in with the temporary password: " + $t.message) -ForegroundColor Red; $failed++; continue }
        $c = Call-Api POST 'Accounts/change-password' @{ currentPassword = $pw; newPassword = $u.Pw } $t.data.accessToken
        if (-not $c.success) { Write-Host ("FAILED " + $u.User + " : password change: " + $c.message) -ForegroundColor Red; $failed++; continue }
        $pending += $u.User
        Write-Host ("made, waits for the second approval: " + $u.User) -ForegroundColor Cyan
    } elseif ($ForceChange -and $u.User -ne 'pmo.test') {
        $f = Call-Api POST 'Accounts/admin-reset-password' @{ userId = $id; temporaryPassword = $u.Pw } $adminToken
        if ($f.success) { Write-Host ("made (must change the password at the first sign-in): " + $u.User) -ForegroundColor Green }
        else { Write-Host ("made, but the forced change was not set: " + $u.User + " " + $f.message) -ForegroundColor Yellow }
    } else { Write-Host ("made: " + $u.User) -ForegroundColor Green }
    $made++
}

$approved = 0
if ($pending.Count -gt 0) {
    $p = Sign-In 'pmo.test' $firstPmo.Pw
    if (-not $p.success) {
        Write-Host 'pmo.test could not sign in, so the sensitive roles were not approved: sign in as pmo.test and approve them in the users page.' -ForegroundColor Yellow
    } else {
        $g = Call-Api GET 'Accounts/role-grants' $null $p.data.accessToken
        foreach ($x in @($g.data)) {
            if ($pending -notcontains $x.userName) { continue }
            if ($x.canApprove) {
                $a = Call-Api POST ("Accounts/role-grants/" + $x.id + "/approve") $null $p.data.accessToken
                if ($a.success) { $approved++; Write-Host ("sensitive role approved: " + $x.userName) -ForegroundColor Green }
                else { Write-Host ("not approved: " + $x.userName + " " + $a.message) -ForegroundColor Red }
            } else { Write-Host ("cannot be approved by pmo.test: " + $x.userName + " " + $x.cannotApproveReason) -ForegroundColor Yellow }
        }
    }
}
Write-Host ''
Write-Host ("Done. made: $made, existed already: $existing, failed: $failed, sensitive roles approved: $approved of " + $pending.Count) -ForegroundColor White
Write-Host 'The passwords are only in Test-Users.xlsx. After the test, disable the test users and delete that file.'
if ($failed -gt 0) { exit 2 }
exit 0
