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

function Register($u, [string]$pw, [string]$token) {
    $body = @{ userName = $u.User; name = $u.Title; lastName = $FaTest; email = $u.Email; roleIds = @($u.Roles); password = $pw
               phoneNumber = $u.Phone; birthDate = '1990-01-01T00:00:00+03:30'; nationalCode = $u.Nid }
    return (Call-Api POST 'Accounts/register-user' $body $token)
}
function Is-Existing($r) {
    # The API refuses a second account with the same user name ("... '<name>' <hamakonoon> mored-e estefade ast", code DuplicateUserName)
    # or the same e-mail (the same text with the word "e-mail", code DuplicateEmail). Both mean "exists already", not a failure.
    $faInUse = -join ([char[]](0x0627, 0x0633, 0x062A, 0x0641, 0x0627, 0x062F, 0x0647))   # "in use"
    $faDup   = -join ([char[]](0x062A, 0x06A9, 0x0631, 0x0627, 0x0631, 0x06CC))         # "duplicate"
    $text = "$($r.message) $(($r.errors | Out-String)) $(($r.data | Out-String))"
    # unify the Arabic letter forms (yeh, kaf) that some texts carry
    $text = $text.Replace([string][char]0x064A, [string][char]0x06CC).Replace([string][char]0x0643, [string][char]0x06A9)
    return ($text -match ('DuplicateUserName|DuplicateEmail|already exists|' + $faInUse + '|' + $faDup))
}

# Who does what (two different people for every sensitive role, as the program demands):
#  1. Admin makes the first PMO user of the file (the first row whose roles include PMO = role 1). While fewer than two PMO users
#     exist the program gives that role at once (the audited start).
#  2. That PMO user makes all the other users. A sensitive role (PMO, CEO, finance manager, payroll, HR, secretariat, auditor) is then
#     only requested: the user signs in once with a temporary password and sets the password of the file himself.
#  3. Admin - a person other than the one who asked - approves the requested sensitive roles.
$firstPmo = $users | Where-Object { $_.Roles -contains 1 } | Select-Object -First 1
$made = 0; $existing = 0; $failed = 0
$creatorToken = $adminToken
if ($firstPmo) {
    $r = Register $firstPmo $firstPmo.Pw $adminToken
    if ($r.success) { Write-Host ("made: " + $firstPmo.User + " (first PMO user, makes the other users)") -ForegroundColor Green; $made++ }
    elseif (Is-Existing $r) { Write-Host ("exists already, skipped: " + $firstPmo.User) -ForegroundColor Yellow; $existing++ }
    else { Write-Host ("FAILED " + $firstPmo.User + " : " + $r.message) -ForegroundColor Red; $failed++ }
    $p = Sign-In $firstPmo.User $firstPmo.Pw
    if ($p.success) { $creatorToken = $p.data.accessToken }
    else { Write-Host ("$($firstPmo.User) could not sign in ($($p.message)); Admin makes the other users, and the sensitive roles must then be approved on screen by $($firstPmo.User).") -ForegroundColor Yellow }
}
$requested = @()
foreach ($u in @($users | Where-Object { $_ -ne $firstPmo })) {
    $pw = $u.Pw
    if ($u.Sens) { $pw = 'Tmp-' + [guid]::NewGuid().ToString('N').Substring(0, 12) + 'aA1' }
    $r = Register $u $pw $creatorToken
    if (-not $r.success) {
        if (Is-Existing $r) { Write-Host ("exists already, skipped: " + $u.User) -ForegroundColor Yellow; $existing++; $requested += $u.User; continue }
        Write-Host ("FAILED " + $u.User + " : " + $r.message) -ForegroundColor Red; $failed++; continue
    }
    $id = $r.data.id
    if ($u.Sens) {
        $t = Sign-In $u.User $pw
        if (-not $t.success) { Write-Host ("FAILED " + $u.User + " : sign-in with the temporary password: " + $t.message) -ForegroundColor Red; $failed++; continue }
        $c = Call-Api POST 'Accounts/change-password' @{ currentPassword = $pw; newPassword = $u.Pw } $t.data.accessToken
        if (-not $c.success) { Write-Host ("FAILED " + $u.User + " : password change: " + $c.message) -ForegroundColor Red; $failed++; continue }
        $requested += $u.User
        Write-Host ("made, its sensitive role waits for the second approval: " + $u.User) -ForegroundColor Cyan
    } elseif ($ForceChange) {
        $f = Call-Api POST 'Accounts/admin-reset-password' @{ userId = $id; temporaryPassword = $u.Pw } $adminToken
        if ($f.success) { Write-Host ("made (must change the password at the first sign-in): " + $u.User) -ForegroundColor Green }
        else { Write-Host ("made, but the forced change was not set: " + $u.User + " " + $f.message) -ForegroundColor Yellow }
    } else { Write-Host ("made: " + $u.User) -ForegroundColor Green }
    $made++
}

# approvals: every open request of a user in the file (also requests of an earlier run)
$approved = 0; $waiting = 0
$approverToken = if ($creatorToken -ne $adminToken) { $adminToken } elseif ($firstPmo -and $p.success) { $p.data.accessToken } else { $null }
$names = @($users | ForEach-Object { $_.User })
if ($approverToken) {
    $g = Call-Api GET 'Accounts/role-grants' $null $approverToken
    foreach ($x in @($g.data)) {
        if ($names -notcontains $x.userName) { continue }
        if ($x.canApprove) {
            $a = Call-Api POST ("Accounts/role-grants/" + $x.id + "/approve") $null $approverToken
            if ($a.success) { $approved++; Write-Host ("sensitive role approved: " + $x.userName) -ForegroundColor Green }
            else { $waiting++; Write-Host ("not approved: " + $x.userName + " " + $a.message) -ForegroundColor Red }
        } else { $waiting++; Write-Host ("waits for approval: " + $x.userName + " (" + $x.cannotApproveReason + ") - approve it in the users page with another PMO user, or run this script again later") -ForegroundColor Yellow }
    }
}
Write-Host ''
Write-Host ("Done. made: $made, existed already: $existing, failed: $failed, sensitive roles approved: $approved, still waiting: $waiting") -ForegroundColor White
Write-Host 'The passwords are only in Test-Users.xlsx. After the test, disable the test users and delete that file.'
if ($failed -gt 0) { exit 2 }
exit 0
