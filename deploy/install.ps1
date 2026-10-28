#Requires -Version 5.1
<#
.SYNOPSIS
    Installs or updates INNCO PMIS on this Windows Server.

.DESCRIPTION
    One run sets up everything the program needs on a Windows Server that already has SQL Server
    (2019 or newer) installed on the same machine:

      - IIS and the .NET 10 Hosting Bundle (downloaded, or taken from the "installers" folder)
      - the program: API and user interface together, as one IIS site
      - the SQL Server database: empty, or restored from a backup of the current system
      - Windows authentication from the program to SQL Server (no database password in any file)
      - HTTPS for the program (a certificate you give, or a self-signed one made here); plain HTTP only on this computer itself
      - a daily backup of the database and of the uploaded files (scheduled task)

    Run it again at any time to update the program. Settings, uploaded files and the database are kept.
    The settings file of the site (appsettings.Production.json) is merged, not rewritten: only the database connection,
    the sign-in key and issuer, the log levels and the audit key are set by this script; every other key stays as it is.

    HTTPS: the program is reached at https://<computer name>/ (port 443). The plain HTTP address works on this computer only
    (127.0.0.1), for the installer's own checks and for the recovery sign-in. Use -CertificateThumbprint for a certificate of
    the company's own authority (or a purchased one). Without it, the certificate the site uses now is kept, otherwise a
    self-signed certificate is made; browsers warn about a self-signed certificate until its public file
    (<InstallRoot>\certificates\innco-pmis.cer) is installed as a trusted root on the client computers.

    Run as Administrator, from the folder that contains this script:

        powershell -ExecutionPolicy Bypass -File .\install.ps1

    The full log is written to <InstallRoot>\logs\install-<date>.log. It contains no passwords.

.PARAMETER InstallRoot
    Where the program, its settings and its logs live. Default: D:\INNCO when D: is a local disk,
    otherwise C:\INNCO. Uploaded documents are stored inside <InstallRoot>\app.

.PARAMETER BackupFolder
    Where the daily backups go. Default: E:\INNCO-Backup when E: is a local disk, otherwise
    <InstallRoot>\Backup.

.PARAMETER SqlServer
    The local SQL Server instance, for example "." or ".\SQLEXPRESS". Default: found automatically.

.PARAMETER RestoreBackup
    A .bak file of the current database. It is restored as the program's database (real data).

.PARAMETER ReplaceDatabase
    Needed together with -RestoreBackup when the database already exists on this server. The existing
    database is backed up first and then replaced.

.PARAMETER UploadedFilesFrom
    Folder of the old program, the one that contains "UploadedDocuments" and "wwwroot". Its uploaded
    files are copied into the new program.

.PARAMETER Port
    The plain HTTP port (default 80). It is bound to 127.0.0.1 only: the program answers on this computer itself, not on the network.

.PARAMETER HttpsPort
    The HTTPS port that people use (default 443).

.PARAMETER CertificateThumbprint
    Thumbprint of the TLS certificate for the HTTPS address. The certificate must be in the computer's personal store
    (Cert:\LocalMachine\My) with its private key. If omitted: the certificate the site already uses is kept; on a first
    installation a self-signed certificate for this computer's name and addresses is made (valid 5 years).

.PARAMETER AdminPassword
    Temporary password of the "Admin" user, used only when the program has to create that account (a new, empty database).
    If omitted, one is generated and shown at the end. The program asks for a new password at the first sign-in, and the
    temporary one is taken out of the settings file as soon as the account exists.

.PARAMETER HostingBundle
    Path or URL of the .NET 10 Hosting Bundle installer (dotnet-hosting-10.x.x-win.exe). Default: the
    "installers" folder, otherwise the latest version is downloaded from Microsoft.

.EXAMPLE
    .\install.ps1
    First installation with an empty database.

.EXAMPLE
    .\install.ps1 -RestoreBackup D:\Import\IRNNCO.bak -ReplaceDatabase -UploadedFilesFrom D:\Import\old-app
    Replace the database with the real data and bring over the uploaded files of the old program.
#>
[CmdletBinding()]
param(
    [string]$InstallRoot,
    [string]$BackupFolder,
    [string]$SqlServer,
    [string]$DatabaseName = 'IRNNCO',
    [string]$SiteName = 'INNCO',
    [ValidateRange(1, 65535)][int]$Port = 80,
    [ValidateRange(1, 65535)][int]$HttpsPort = 443,
    [string]$CertificateThumbprint,
    [string]$RestoreBackup,
    [switch]$ReplaceDatabase,
    [string]$UploadedFilesFrom,
    [string]$AdminPassword,
    [string]$HostingBundle,
    [switch]$SkipFirstBackup
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$PackageDir = $PSScriptRoot
$ScriptVersion = '1.1'
$SelfSignedName = 'INNCO PMIS (self-signed)'   # friendly name of the certificate this script makes
$script:Warnings = New-Object System.Collections.Generic.List[string]
$script:TranscriptOn = $false

# Uploaded files live inside the program folder (see DocumentService and UsersPhotoService).
$DataFolders = @('UploadedDocuments', 'wwwroot\Documents', 'wwwroot\avatars')

# ----------------------------------------------------------------------------------------------
# Helpers
# ----------------------------------------------------------------------------------------------

function Write-Step([string]$Text) { Write-Host ''; Write-Host "==> $Text" -ForegroundColor Cyan }
function Write-Info([string]$Text) { Write-Host "    $Text" }
function Write-Ok([string]$Text) { Write-Host "    OK: $Text" -ForegroundColor Green }
function Write-Warn([string]$Text) {
    Write-Host "    WARNING: $Text" -ForegroundColor Yellow
    $script:Warnings.Add($Text)
}

function Test-LocalDisk([string]$Letter) {
    $disk = Get-CimInstance -ClassName Win32_LogicalDisk -Filter "DeviceID='$($Letter):'" -ErrorAction SilentlyContinue
    return ($null -ne $disk -and $disk.DriveType -eq 3)
}

function ConvertTo-SqlName([string]$Name) { return '[' + $Name.Replace(']', ']]') + ']' }
function ConvertTo-SqlText([string]$Text) { return "N'" + $Text.Replace("'", "''") + "'" }

function New-RandomText([int]$Length, [switch]$Password) {
    $alphabet = 'ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789'.ToCharArray()
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        while ($true) {
            $bytes = New-Object byte[] $Length
            $rng.GetBytes($bytes)
            $text = -join ($bytes | ForEach-Object { $alphabet[$_ % $alphabet.Length] })
            if (-not $Password) { return $text }
            if ($text -cmatch '[A-Z]' -and $text -cmatch '[a-z]' -and $text -match '[0-9]') { return $text }
        }
    }
    finally { $rng.Dispose() }
}

function Invoke-Sql {
    param(
        [Parameter(Mandatory)][string]$Query,
        [string]$Database = 'master',
        [hashtable]$Parameters,
        [switch]$Scalar,
        [int]$Timeout = 0
    )
    $connectionString = "Data Source=$script:SqlInstance;Initial Catalog=$Database;Integrated Security=True;" +
                        "Connect Timeout=30;Application Name=INNCO-install"
    $connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
    $connection.add_InfoMessage([System.Data.SqlClient.SqlInfoMessageEventHandler] {
        param($sender, $e)
        Write-Host "    sql: $($e.Message)"
    })
    $connection.Open()
    try {
        $command = $connection.CreateCommand()
        $command.CommandText = $Query
        $command.CommandTimeout = $Timeout
        if ($Parameters) {
            foreach ($key in $Parameters.Keys) { [void]$command.Parameters.AddWithValue($key, $Parameters[$key]) }
        }
        if ($Scalar) { return $command.ExecuteScalar() }
        $table = New-Object System.Data.DataTable
        $reader = $command.ExecuteReader()
        try { $table.Load($reader) } finally { $reader.Dispose() }
        return , $table
    }
    finally { $connection.Dispose() }
}

function Invoke-Icacls([string[]]$Arguments) {
    # Windows PowerShell turns any stderr line of a native program into an error when $ErrorActionPreference
    # is Stop, so run icacls with Continue and judge it by its exit code.
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = & icacls.exe @Arguments 2>&1 | Out-String }
    finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) { throw "icacls $($Arguments -join ' ') failed: $($output.Trim())" }
}

function Grant-FolderAccess([string]$Path, [string]$Identity, [string]$Rights) {
    Invoke-Icacls @($Path, '/grant', "$($Identity):(OI)(CI)$Rights", '/Q')
}

function Test-DatabaseExists([string]$Name) {
    $id = Invoke-Sql -Scalar -Query 'SELECT DB_ID(@n)' -Parameters @{ '@n' = $Name }
    return -not ($null -eq $id -or $id -is [DBNull])
}

function Invoke-Robocopy([string]$Source, [string]$Target, [string[]]$Extra = @()) {
    $arguments = @($Source, $Target, '/E', '/R:2', '/W:3', '/NP', '/NFL', '/NDL', '/NJH', '/NJS') + $Extra
    & robocopy.exe @arguments | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying $Source to $Target failed (robocopy code $LASTEXITCODE)." }
}

function Wait-Http([string]$Url, [int]$Seconds) {
    $deadline = (Get-Date).AddSeconds($Seconds)
    $lastError = $null
    while ((Get-Date) -lt $deadline) {
        try { return Invoke-WebRequest -UseBasicParsing -Uri $Url -TimeoutSec 180 }
        catch { $lastError = $_.Exception.Message; Start-Sleep -Seconds 5 }
    }
    throw "The program did not answer at $Url ($lastError)."
}

function Stop-AppPoolAndWait([string]$Name) {
    $state = (Get-WebAppPoolState -Name $Name).Value
    if ($state -eq 'Started' -or $state -eq 'Starting') {
        Stop-WebAppPool -Name $Name
        for ($i = 0; $i -lt 60; $i++) {
            if ((Get-WebAppPoolState -Name $Name).Value -eq 'Stopped') { break }
            Start-Sleep -Seconds 1
        }
        Start-Sleep -Seconds 2   # let the worker process release the program files
        Write-Info "Stopped application pool '$Name'."
    }
}

function Get-SqlServiceAccount([string]$InstanceName) {
    $serviceName = if ($InstanceName -eq 'MSSQLSERVER') { 'MSSQLSERVER' } else { 'MSSQL$' + $InstanceName }
    $service = Get-CimInstance -ClassName Win32_Service -Filter "Name='$serviceName'" -ErrorAction SilentlyContinue
    if ($null -eq $service) { return $null }
    if ($service.StartName -eq 'LocalSystem') { return 'NT AUTHORITY\SYSTEM' }
    return $service.StartName
}

function Find-HostingBundleInstaller {
    if ($HostingBundle -and $HostingBundle -notmatch '^https?://') {
        if (-not (Test-Path $HostingBundle)) { throw "Hosting Bundle file not found: $HostingBundle" }
        return (Resolve-Path $HostingBundle).Path
    }
    $local = Get-ChildItem -Path (Join-Path $PackageDir 'installers') -Filter 'dotnet-hosting-10*.exe' -ErrorAction SilentlyContinue |
             Sort-Object Name -Descending | Select-Object -First 1
    if ($local) { Write-Info "Using $($local.FullName)"; return $local.FullName }

    $url = $null; $hash = $null
    if ($HostingBundle) { $url = $HostingBundle }
    else {
        try {
            $meta = Invoke-RestMethod -UseBasicParsing -Uri 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
            $release = $meta.releases | Where-Object { $_.'release-version' -eq $meta.'latest-release' } | Select-Object -First 1
            $file = $release.'aspnetcore-runtime'.files | Where-Object { $_.name -eq 'dotnet-hosting-win.exe' } | Select-Object -First 1
            if ($file) { $url = $file.url; $hash = $file.hash; Write-Info "Latest .NET 10 release: $($meta.'latest-release')" }
        }
        catch { Write-Info "Could not read the .NET release list ($($_.Exception.Message))." }
        if (-not $url) { $url = 'https://aka.ms/dotnet/10.0/dotnet-hosting-win.exe' }
    }

    $target = Join-Path $env:TEMP 'dotnet-hosting-10-win.exe'
    Write-Info "Downloading $url"
    try { Invoke-WebRequest -UseBasicParsing -Uri $url -OutFile $target }
    catch {
        throw ("Could not download the .NET 10 Hosting Bundle ($($_.Exception.Message)). On a computer with " +
               "internet access, download 'Hosting Bundle' from https://dotnet.microsoft.com/download/dotnet/10.0, " +
               "put the .exe file in the 'installers' folder next to this script, and run the script again.")
    }
    if ($hash) {
        $actual = (Get-FileHash -Algorithm SHA512 -Path $target).Hash
        if ($actual -ne $hash) { throw 'The downloaded Hosting Bundle failed the SHA-512 check. Run the script again.' }
        Write-Info 'Download verified (SHA-512).'
    }
    return $target
}

function Test-HostingBundle {
    $framework = Join-Path $env:ProgramFiles 'dotnet\shared\Microsoft.AspNetCore.App'
    $hasRuntime = (Test-Path $framework) -and
                  (@(Get-ChildItem -Path $framework -Directory | Where-Object { $_.Name -like '10.*' }).Count -gt 0)
    $hasModuleFile = Test-Path (Join-Path $env:ProgramFiles 'IIS\Asp.Net Core Module\V2\aspnetcorev2.dll')
    $registered = $null -ne (Get-WebGlobalModule -Name 'AspNetCoreModuleV2' -ErrorAction SilentlyContinue)
    return [pscustomobject]@{ Runtime = $hasRuntime; ModuleFile = $hasModuleFile; Module = ($hasModuleFile -and $registered) }
}

# ----------------------------------------------------------------------------------------------
# Settings file: the existing appsettings.Production.json is merged, never rewritten (R1a2, M7)
# ----------------------------------------------------------------------------------------------

function Get-JsonSetting($Root, [string[]]$Path) {
    $node = $Root
    foreach ($name in $Path) {
        if ($null -eq $node) { return $null }
        $property = $node.PSObject.Properties[$name]
        if ($null -eq $property) { return $null }
        $node = $property.Value
    }
    return $node
}

# Sets Root.Path[0].Path[1]... to Value and creates the objects on the way; nothing else in the file is touched.
function Set-JsonSetting($Root, [string[]]$Path, $Value) {
    $node = $Root
    for ($i = 0; $i -lt $Path.Count - 1; $i++) {
        $property = $node.PSObject.Properties[$Path[$i]]
        if ($null -eq $property) {
            $child = [pscustomobject]@{}
            $node | Add-Member -NotePropertyName $Path[$i] -NotePropertyValue $child
            $node = $child
        }
        elseif ($property.Value -is [System.Management.Automation.PSCustomObject]) { $node = $property.Value }
        else {
            $child = [pscustomobject]@{}
            $property.Value = $child
            $node = $child
        }
    }
    $leaf = $Path[$Path.Count - 1]
    $existingLeaf = $node.PSObject.Properties[$leaf]
    if ($null -ne $existingLeaf) { $existingLeaf.Value = $Value }
    else { $node | Add-Member -NotePropertyName $leaf -NotePropertyValue $Value }
}

# Removes the last name of Path (and the object that held it when nothing else is left in it).
function Remove-JsonSetting($Root, [string[]]$Path) {
    $parent = $Root
    if ($Path.Count -gt 1) { $parent = Get-JsonSetting $Root ([string[]]$Path[0..($Path.Count - 2)]) }
    if ($null -eq $parent) { return }
    $leaf = $Path[$Path.Count - 1]
    if ($null -ne $parent.PSObject.Properties[$leaf]) { $parent.PSObject.Properties.Remove($leaf) }
    if ($Path.Count -eq 2 -and @($parent.PSObject.Properties).Count -eq 0) { $Root.PSObject.Properties.Remove($Path[0]) }
}

function Write-JsonSettings($Settings, [string]$Path) {
    $json = $Settings | ConvertTo-Json -Depth 20
    [IO.File]::WriteAllText($Path, $json, (New-Object System.Text.UTF8Encoding($false)))
}

# True when the program has already made the administrator account (the table exists only after the program's first start).
function Test-AdminAccount([string]$UserName) {
    $count = Invoke-Sql -Database $DatabaseName -Scalar -Parameters @{ '@n' = $UserName } -Query @"
IF OBJECT_ID(N'dbo.AppUsers', N'U') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM dbo.AppUsers WHERE UserName = @n
"@
    return ([int]$count) -gt 0
}

# ----------------------------------------------------------------------------------------------
# HTTPS (R1a2, M6)
# ----------------------------------------------------------------------------------------------

# The DNS names and IPv4 addresses a self-signed certificate of this computer is made for.
function Get-CertificateNames {
    $names = New-Object System.Collections.Generic.List[string]
    $names.Add($env:COMPUTERNAME.ToLowerInvariant())
    try {
        $fullName = [System.Net.Dns]::GetHostEntry([string]$env:COMPUTERNAME).HostName.ToLowerInvariant()
        if ($fullName -and -not $names.Contains($fullName)) { $names.Add($fullName) }
    }
    catch { }
    if (-not $names.Contains('localhost')) { $names.Add('localhost') }
    $addresses = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
                   Where-Object { $_.IPAddress -notlike '169.254.*' } |
                   ForEach-Object { $_.IPAddress } | Select-Object -Unique)
    return [pscustomobject]@{ Dns = $names.ToArray(); Addresses = $addresses }
}

# True when the subject alternative names of the certificate contain every name and address of Names.
function Test-CertificateCovers($Certificate, $Names) {
    $extension = $Certificate.Extensions | Where-Object { $_.Oid.Value -eq '2.5.29.17' } | Select-Object -First 1
    if ($null -eq $extension) { return $false }
    $values = @(($extension.Format($true) -split '[,\r\n]+') | ForEach-Object { ($_ -split '=', 2)[-1].Trim().ToLowerInvariant() })
    foreach ($wanted in (@($Names.Dns) + @($Names.Addresses))) {
        if (-not $wanted) { continue }
        if ($values -notcontains ([string]$wanted).ToLowerInvariant()) { return $false }
    }
    return $true
}

# The certificate for the HTTPS address: the one asked for, else the one the site uses now, else one of ours that is still good, else a new self-signed one.
function Resolve-SiteCertificate([string]$Requested, [string]$CurrentHash) {
    $store = 'Cert:\LocalMachine\My'
    if ($Requested) {
        $thumbprint = ($Requested -replace '[^0-9A-Fa-f]', '').ToUpperInvariant()
        $certificate = Get-Item -Path "$store\$thumbprint" -ErrorAction SilentlyContinue
        if ($null -eq $certificate) { throw "No certificate with thumbprint $thumbprint in the computer's personal certificate store ($store). Import it there, with its private key, and run the script again." }
        if (-not $certificate.HasPrivateKey) { throw "The certificate $thumbprint has no private key in the computer store, so IIS cannot use it." }
        if ($certificate.NotAfter -lt (Get-Date)) { throw "The certificate $thumbprint expired on $($certificate.NotAfter.ToString('yyyy-MM-dd'))." }
        Write-Info "Certificate: $($certificate.Subject) (given thumbprint, valid until $($certificate.NotAfter.ToString('yyyy-MM-dd')))."
        return $certificate
    }
    if ($CurrentHash) {
        $current = Get-Item -Path "$store\$CurrentHash" -ErrorAction SilentlyContinue
        if ($null -ne $current -and $current.HasPrivateKey -and $current.NotAfter -gt (Get-Date).AddDays(14)) {
            Write-Info "Certificate: kept the one the site uses now ($($current.Subject), valid until $($current.NotAfter.ToString('yyyy-MM-dd')))."
            return $current
        }
    }
    $names = Get-CertificateNames
    $own = @(Get-ChildItem -Path $store | Where-Object { $_.FriendlyName -eq $SelfSignedName -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(30) } | Sort-Object NotAfter -Descending)
    foreach ($candidate in $own) {
        if (Test-CertificateCovers $candidate $names) {
            Write-Info "Certificate: reused the self-signed certificate made earlier (valid until $($candidate.NotAfter.ToString('yyyy-MM-dd')))."
            return $candidate
        }
    }
    $alternative = (@($names.Dns | ForEach-Object { "DNS=$_" }) + @($names.Addresses | ForEach-Object { "IPAddress=$_" })) -join '&'
    $made = New-SelfSignedCertificate -Subject ('CN=' + $env:COMPUTERNAME.ToLowerInvariant()) -FriendlyName $SelfSignedName -CertStoreLocation $store `
                -KeyAlgorithm RSA -KeyLength 2048 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(5) `
                -TextExtension @("2.5.29.17={text}$alternative")
    Write-Info "Certificate: made a self-signed certificate for $($names.Dns -join ', ') and $($names.Addresses -join ', ') (valid until $($made.NotAfter.ToString('yyyy-MM-dd')))."
    return $made
}

# Connects with TLS and returns the thumbprint of the certificate the server presents. It only reads the certificate, so the validation accepts everything.
function Get-ServedCertificateThumbprint([string]$HostName, [int]$TcpPort) {
    $client = New-Object System.Net.Sockets.TcpClient
    try {
        $client.Connect($HostName, $TcpPort)
        $accept = [System.Net.Security.RemoteCertificateValidationCallback] { param($sender, $certificate, $chain, $errors) $true }
        $ssl = New-Object System.Net.Security.SslStream($client.GetStream(), $false, $accept)
        try {
            $ssl.AuthenticateAsClient($HostName, $null, [System.Security.Authentication.SslProtocols]::Tls12, $false)
            return (New-Object System.Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList $ssl.RemoteCertificate).Thumbprint
        }
        finally { $ssl.Dispose() }
    }
    finally { $client.Close() }
}

# ----------------------------------------------------------------------------------------------
# Main
# ----------------------------------------------------------------------------------------------

$exitCode = 0
$newAdminPassword = $null
$siteUrls = @()
try {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this script as Administrator (right-click PowerShell > Run as administrator).'
    }
    if (-not (Test-Path (Join-Path $PackageDir 'app\WebApi.dll'))) {
        throw "The 'app' folder is missing next to this script. Extract the whole package and run install.ps1 from it."
    }

    # Paths -------------------------------------------------------------------------------------
    if (-not $InstallRoot) { $InstallRoot = if (Test-LocalDisk 'D') { 'D:\INNCO' } else { 'C:\INNCO' } }
    if (-not $BackupFolder) { $BackupFolder = if (Test-LocalDisk 'E') { 'E:\INNCO-Backup' } else { Join-Path $InstallRoot 'Backup' } }
    $AppDir = Join-Path $InstallRoot 'app'
    $ToolsDir = Join-Path $InstallRoot 'tools'
    $LogsDir = Join-Path $InstallRoot 'logs'
    foreach ($dir in @($InstallRoot, $AppDir, $ToolsDir, $LogsDir, $BackupFolder)) {
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
    }

    $logFile = Join-Path $LogsDir ("install-{0:yyyyMMdd-HHmmss}.log" -f (Get-Date))
    Start-Transcript -Path $logFile | Out-Null
    $script:TranscriptOn = $true

    Write-Host "INNCO PMIS installer $ScriptVersion"
    $os = Get-CimInstance -ClassName Win32_OperatingSystem
    Write-Info "Windows:        $($os.Caption) ($($os.Version))"
    Write-Info "Computer:       $env:COMPUTERNAME"
    Write-Info "Program folder: $AppDir"
    Write-Info "Backup folder:  $BackupFolder"
    $buildInfo = Join-Path $PackageDir 'BUILD-INFO.txt'
    if (Test-Path $buildInfo) { Get-Content $buildInfo | ForEach-Object { Write-Info "Package:        $_" } }
    try {
        $timeZone = Get-TimeZone
        if ($timeZone.Id -ne 'Iran Standard Time') {
            Write-Warn "The Windows time zone is '$($timeZone.DisplayName)'. The program records some times in server time; set it to (UTC+03:30) Tehran unless you have a reason not to."
        }
    }
    catch { }
    Get-ChildItem -Path $PackageDir -Recurse -File | Unblock-File -ErrorAction SilentlyContinue

    # 1. IIS ------------------------------------------------------------------------------------
    Write-Step 'IIS (web server)'
    if ($os.ProductType -eq 1) {
        $features = 'IIS-WebServerRole', 'IIS-WebServer', 'IIS-CommonHttpFeatures', 'IIS-StaticContent', 'IIS-DefaultDocument',
                    'IIS-HttpErrors', 'IIS-HttpLogging', 'IIS-RequestFiltering', 'IIS-HttpCompressionStatic',
                    'IIS-ManagementConsole', 'IIS-ManagementScriptingTools'
        $result = Enable-WindowsOptionalFeature -Online -FeatureName $features -All -NoRestart
        if ($result.RestartNeeded) { Write-Warn 'Windows asks for a restart to finish installing IIS.' }
    }
    else {
        Import-Module ServerManager
        $result = Install-WindowsFeature -Name 'Web-Server', 'Web-Mgmt-Console', 'Web-Scripting-Tools'
        if (-not $result.Success) { throw 'Installing IIS failed.' }
        if ($result.RestartNeeded -eq 'Yes') { Write-Warn 'Windows asks for a restart to finish installing IIS.' }
    }
    Import-Module WebAdministration
    Write-Ok 'IIS is installed.'

    # 2. .NET 10 Hosting Bundle -----------------------------------------------------------------
    Write-Step '.NET 10 Hosting Bundle'
    $bundle = Test-HostingBundle
    if ($bundle.Runtime -and $bundle.Module) {
        Write-Ok 'Already installed.'
    }
    else {
        $installer = Find-HostingBundleInstaller
        # The bundle was installed before IIS: a repair registers its IIS module. Otherwise a normal install.
        $mode = if ($bundle.ModuleFile -and -not $bundle.Module) { '/repair' } else { '/install' }
        $process = Start-Process -FilePath $installer -ArgumentList $mode, '/quiet', '/norestart', '/log', "`"$LogsDir\hosting-bundle.log`"" -Wait -PassThru
        if ($process.ExitCode -eq 3010) { Write-Warn 'Windows asks for a restart to finish the .NET installation. Restart the server when convenient.' }
        elseif ($process.ExitCode -ne 0 -and $process.ExitCode -ne 1638) {
            throw "The Hosting Bundle installer failed (exit code $($process.ExitCode)). See $LogsDir\hosting-bundle.log"
        }
        & net.exe stop was /y | Out-Null
        & net.exe start w3svc | Out-Null
        $bundle = Test-HostingBundle
        if (-not ($bundle.Runtime -and $bundle.Module)) { throw 'The .NET 10 Hosting Bundle is still not complete after installation.' }
        Write-Ok 'Installed.'
    }

    # 3. SQL Server -----------------------------------------------------------------------------
    Write-Step 'SQL Server'
    $instanceName = $null
    if ($SqlServer) {
        $script:SqlInstance = $SqlServer
        $serverPart = $SqlServer.Split('\')[0]
        if (@('.', 'localhost', '(local)', '127.0.0.1', $env:COMPUTERNAME) -notcontains $serverPart) {
            throw "SQL Server must be on this computer (the program signs in to it with Windows authentication). Got: $SqlServer"
        }
        $instanceName = if ($SqlServer.Contains('\')) { $SqlServer.Split('\')[1] } else { 'MSSQLSERVER' }
    }
    else {
        $instances = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL' -ErrorAction SilentlyContinue
        $names = @()
        if ($instances) { $names = @($instances.PSObject.Properties | Where-Object { $_.Name -notlike 'PS*' } | ForEach-Object { $_.Name }) }
        if ($names.Count -eq 0) { throw 'SQL Server was not found on this computer. Install SQL Server 2022 first (or give -SqlServer).' }
        $instanceName = if ($names -contains 'MSSQLSERVER') { 'MSSQLSERVER' } else { $names[0] }
        if ($names.Count -gt 1) { Write-Warn "Several SQL Server instances found ($($names -join ', ')); using $instanceName. Use -SqlServer to choose another." }
        $script:SqlInstance = if ($instanceName -eq 'MSSQLSERVER') { '.' } else { ".\$instanceName" }
    }
    $sqlServiceName = if ($instanceName -eq 'MSSQLSERVER') { 'MSSQLSERVER' } else { 'MSSQL$' + $instanceName }
    $sqlService = Get-Service -Name $sqlServiceName -ErrorAction SilentlyContinue
    if ($sqlService -and $sqlService.Status -ne 'Running') { Start-Service -Name $sqlServiceName; Write-Info "Started service $sqlServiceName." }

    $versionText = [string](Invoke-Sql -Scalar -Query 'SELECT @@VERSION')
    $major = [int](Invoke-Sql -Scalar -Query "SELECT CAST(SERVERPROPERTY('ProductMajorVersion') AS int)")
    $edition = [string](Invoke-Sql -Scalar -Query "SELECT CAST(SERVERPROPERTY('Edition') AS nvarchar(128))")
    $firstVersionLine = ($versionText -split "`n")[0].Trim()
    Write-Info "Instance: $script:SqlInstance"
    Write-Info "Version:  $firstVersionLine"
    Write-Info "Edition:  $edition"
    if ($major -lt 15) { throw 'SQL Server 2019 or newer is needed (this server has an older version). Install SQL Server 2022.' }
    if ([int](Invoke-Sql -Scalar -Query "SELECT IS_SRVROLEMEMBER('sysadmin')") -ne 1) {
        throw "The Windows user '$env:USERDOMAIN\$env:USERNAME' is not a SQL Server administrator. Run the script as the user that was added during SQL Server setup ('Add Current User')."
    }
    $sqlAccount = Get-SqlServiceAccount $instanceName
    Write-Ok "Connected (SQL Server service account: $sqlAccount)."

    # 4. Folders and permissions ----------------------------------------------------------------
    Write-Step 'Folders and permissions'
    # Only Administrators and SYSTEM by default; the program and SQL Server get exactly what they need below.
    foreach ($dir in @($InstallRoot, $BackupFolder)) {
        Invoke-Icacls @($dir, '/inheritance:r', '/grant:r', '*S-1-5-32-544:(OI)(CI)F', '*S-1-5-18:(OI)(CI)F', '/Q')
    }
    if ($sqlAccount) { Grant-FolderAccess $BackupFolder $sqlAccount 'M' }
    Write-Ok "$InstallRoot and $BackupFolder are limited to Administrators, SYSTEM and the services that need them."

    # 5. Application pool -----------------------------------------------------------------------
    Write-Step 'IIS application pool'
    $poolPath = "IIS:\AppPools\$SiteName"
    if (-not (Test-Path $poolPath)) { New-WebAppPool -Name $SiteName | Out-Null; Write-Info "Created application pool '$SiteName'." }
    Set-ItemProperty $poolPath -Name managedRuntimeVersion -Value ''
    Set-ItemProperty $poolPath -Name managedPipelineMode -Value 'Integrated'
    Set-ItemProperty $poolPath -Name startMode -Value 'AlwaysRunning'
    Set-ItemProperty $poolPath -Name processModel.identityType -Value 'ApplicationPoolIdentity'
    Set-ItemProperty $poolPath -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
    Set-ItemProperty $poolPath -Name processModel.loadUserProfile -Value $true
    Set-ItemProperty $poolPath -Name recycling.periodicRestart.time -Value ([TimeSpan]::Zero)
    try { Clear-ItemProperty $poolPath -Name recycling.periodicRestart.schedule } catch { }
    Set-ItemProperty $poolPath -Name recycling.periodicRestart.schedule -Value @{ value = '03:00:00' }
    $poolIdentity = "IIS AppPool\$SiteName"
    Stop-AppPoolAndWait $SiteName
    Write-Ok "No managed code, always running, restarts daily at 03:00, runs as '$poolIdentity'."

    # 6. Program files --------------------------------------------------------------------------
    Write-Step 'Program files'
    $assets = Join-Path $AppDir 'wwwroot\assets'
    if (Test-Path $assets) { Remove-Item -Recurse -Force $assets }   # old user-interface build; uploaded files are elsewhere
    Invoke-Robocopy (Join-Path $PackageDir 'app') $AppDir @('/XF', 'appsettings.Production.json')
    foreach ($rel in $DataFolders) { New-Item -ItemType Directory -Force -Path (Join-Path $AppDir $rel) | Out-Null }
    Copy-Item -Path (Join-Path $PackageDir 'backup.ps1') -Destination $ToolsDir -Force

    $webConfigPath = Join-Path $AppDir 'web.config'
    [xml]$webConfig = Get-Content -Raw -Path $webConfigPath
    $module = $webConfig.SelectSingleNode('//aspNetCore')
    $module.SetAttribute('stdoutLogEnabled', 'true')
    $module.SetAttribute('stdoutLogFile', '..\logs\stdout')
    $module.SetAttribute('startupTimeLimit', '600')    # the first start applies database updates
    $webConfig.Save($webConfigPath)

    Grant-FolderAccess $AppDir $poolIdentity 'RX'
    foreach ($rel in $DataFolders) { Grant-FolderAccess (Join-Path $AppDir $rel) $poolIdentity 'M' }
    Grant-FolderAccess $LogsDir $poolIdentity 'M'

    if ($UploadedFilesFrom) {
        if (-not (Test-Path $UploadedFilesFrom)) { throw "Folder not found: $UploadedFilesFrom" }
        foreach ($rel in $DataFolders) {
            $source = Join-Path $UploadedFilesFrom $rel
            if (Test-Path $source) {
                Invoke-Robocopy $source (Join-Path $AppDir $rel)
                $count = @(Get-ChildItem -Path (Join-Path $AppDir $rel) -Recurse -File).Count
                Write-Info "Copied uploaded files from $source ($count files now in $rel)."
            }
            else { Write-Info "Not found in the old program folder (skipped): $rel" }
        }
    }
    Write-Ok "Program copied to $AppDir"

    # 7. Database -------------------------------------------------------------------------------
    Write-Step "Database '$DatabaseName'"
    $db = ConvertTo-SqlName $DatabaseName
    $dbExists = Test-DatabaseExists $DatabaseName
    $createdNewDatabase = $false

    if ($RestoreBackup) {
        if (-not (Test-Path $RestoreBackup)) { throw "Backup file not found: $RestoreBackup" }
        if ($dbExists -and -not $ReplaceDatabase) {
            throw "Database $DatabaseName already exists. To replace it with the backup, run again with -ReplaceDatabase (the existing database is backed up first)."
        }
        $restoreDir = Join-Path $BackupFolder 'restore'
        New-Item -ItemType Directory -Force -Path $restoreDir | Out-Null
        $backupCopy = Join-Path $restoreDir ([IO.Path]::GetFileName($RestoreBackup))
        if ((Resolve-Path $RestoreBackup).Path -ne $backupCopy) {
            Write-Info 'Copying the backup file to a folder SQL Server can read...'
            Copy-Item -Path $RestoreBackup -Destination $backupCopy -Force
        }
        if ($dbExists) {
            $safety = Join-Path $BackupFolder ("database\{0}_before-restore_{1:yyyyMMdd_HHmm}.bak" -f $DatabaseName, (Get-Date))
            New-Item -ItemType Directory -Force -Path (Split-Path $safety) | Out-Null
            Write-Info "Backing up the existing database first: $safety"
            Invoke-Sql -Query "BACKUP DATABASE $db TO DISK = @f WITH COPY_ONLY, INIT, CHECKSUM" -Parameters @{ '@f' = $safety } | Out-Null
            Invoke-Sql -Query "ALTER DATABASE $db SET SINGLE_USER WITH ROLLBACK IMMEDIATE" | Out-Null
        }
        $dataPath = [string](Invoke-Sql -Scalar -Query "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(260))")
        $logPath = [string](Invoke-Sql -Scalar -Query "SELECT CAST(SERVERPROPERTY('InstanceDefaultLogPath') AS nvarchar(260))")
        $fileList = Invoke-Sql -Query 'RESTORE FILELISTONLY FROM DISK = @f' -Parameters @{ '@f' = $backupCopy }
        $moves = New-Object System.Collections.Generic.List[string]
        $dataCount = 0; $logCount = 0
        foreach ($file in $fileList.Rows) {
            if ($file.Type -eq 'L') {
                $logCount++
                $suffix = if ($logCount -gt 1) { "_log$logCount" } else { '_log' }
                $target = Join-Path $logPath "$DatabaseName$suffix.ldf"
            }
            else {
                $dataCount++
                $suffix = if ($dataCount -gt 1) { "_$dataCount.ndf" } else { '.mdf' }
                if ($file.Type -eq 'S') { $suffix = "_filestream$dataCount" }
                $target = Join-Path $dataPath "$DatabaseName$suffix"
            }
            $moves.Add("MOVE $(ConvertTo-SqlText ([string]$file.LogicalName)) TO $(ConvertTo-SqlText $target)")
        }
        $replace = if ($dbExists) { ', REPLACE' } else { '' }
        Write-Info "Restoring $backupCopy ..."
        try {
            Invoke-Sql -Query ("RESTORE DATABASE $db FROM DISK = @f WITH " + ($moves -join ', ') + "$replace, RECOVERY, STATS = 10") `
                       -Parameters @{ '@f' = $backupCopy } | Out-Null
        }
        catch {
            if ($dbExists) { try { Invoke-Sql -Query "ALTER DATABASE $db SET MULTI_USER" | Out-Null } catch { } }
            throw
        }
        Invoke-Sql -Query "ALTER DATABASE $db SET MULTI_USER" | Out-Null
        Invoke-Sql -Query "ALTER AUTHORIZATION ON DATABASE::$db TO [sa]" | Out-Null
        Remove-Item -Path $backupCopy -Force -ErrorAction SilentlyContinue
        Write-Ok 'Backup restored.'
    }
    elseif (-not $dbExists) {
        Invoke-Sql -Query "CREATE DATABASE $db" | Out-Null
        $createdNewDatabase = $true
        Write-Ok 'Created a new, empty database. The program creates its tables when it starts.'
    }
    else {
        Write-Ok 'The existing database is kept.'
    }

    $level = [int](Invoke-Sql -Scalar -Query 'SELECT compatibility_level FROM sys.databases WHERE name = @n' -Parameters @{ '@n' = $DatabaseName })
    if ($level -lt 150) {
        Invoke-Sql -Query "ALTER DATABASE $db SET COMPATIBILITY_LEVEL = 150" | Out-Null
        Write-Info "Compatibility level raised from $level to 150 (what the program expects)."
    }
    Invoke-Sql -Query "ALTER DATABASE $db SET RECOVERY SIMPLE" | Out-Null   # daily full backups; keeps the log file small

    # Windows authentication for the program (application pool) and the backup task (SYSTEM).
    $poolName = ConvertTo-SqlName $poolIdentity
    $poolText = ConvertTo-SqlText $poolIdentity
    Invoke-Sql -Query @"
IF SUSER_ID($poolText) IS NULL CREATE LOGIN $poolName FROM WINDOWS;
IF SUSER_ID(N'NT AUTHORITY\SYSTEM') IS NULL CREATE LOGIN [NT AUTHORITY\SYSTEM] FROM WINDOWS;
"@ | Out-Null
    Invoke-Sql -Database $DatabaseName -Query @"
IF USER_ID($poolText) IS NULL BEGIN CREATE USER $poolName FOR LOGIN $poolName; END
ELSE BEGIN ALTER USER $poolName WITH LOGIN = $poolName; END
ALTER ROLE db_owner ADD MEMBER $poolName;
IF USER_ID(N'NT AUTHORITY\SYSTEM') IS NULL CREATE USER [NT AUTHORITY\SYSTEM] FOR LOGIN [NT AUTHORITY\SYSTEM];
ALTER ROLE db_backupoperator ADD MEMBER [NT AUTHORITY\SYSTEM];
"@ | Out-Null
    Write-Ok "'$poolIdentity' can use the database (no password needed); the backup task can back it up."

    # 8. Settings -------------------------------------------------------------------------------
    Write-Step 'Settings (appsettings.Production.json)'
    # R1a2 (M7): the settings file of the site is MERGED, not rewritten. This script sets only: the database connection, the sign-in key (kept when there is one) and the
    # token issuer (https), the log levels, the audit key (kept when there is one) and, until the program has created the Admin account, the temporary password of that account.
    # Every other key of the file (allowed origins, AI endpoints, limits under "Identity" ...) stays exactly as it is.
    $settingsPath = Join-Path $AppDir 'appsettings.Production.json'
    $seedMarker = Join-Path $InstallRoot 'admin-seed-done.txt'
    $settings = $null
    if (Test-Path $settingsPath) {
        try { $settings = Get-Content -Raw -Path $settingsPath -Encoding UTF8 | ConvertFrom-Json }
        catch {
            $unreadable = "$settingsPath.unreadable-{0:yyyyMMdd-HHmmss}" -f (Get-Date)
            Copy-Item -Path $settingsPath -Destination $unreadable -Force
            Write-Warn "The existing settings file could not be read, a new one is made. The old file is kept as $unreadable ($($_.Exception.Message))"
        }
    }
    if ($null -eq $settings -or $settings -isnot [System.Management.Automation.PSCustomObject]) { $settings = [pscustomobject]@{} }

    # the name of the administrator account the program creates (the shipped settings say which)
    $adminName = [string](Get-JsonSetting $settings @('AdminUserSeed', 'Username'))
    if (-not $adminName) {
        try { $adminName = [string](Get-JsonSetting (Get-Content -Raw -Path (Join-Path $AppDir 'appsettings.json') -Encoding UTF8 | ConvertFrom-Json) @('AdminUserSeed', 'Username')) }
        catch { }
    }
    if (-not $adminName) { $adminName = 'Admin' }

    $jwtKey = [string](Get-JsonSetting $settings @('BearerTokens', 'Key'))
    if (-not $jwtKey -or $jwtKey.Length -lt 32) { $jwtKey = New-RandomText 64 }
    $auditKey = [string](Get-JsonSetting $settings @('Identity', 'AuditHmacKey'))   # keyed hash of the masked values in the audit trail; never replaced once it exists
    if (-not $auditKey) { $auditKey = New-RandomText 64 }

    # The seed password is needed only while the account does not exist; it goes into the file now and comes out again as soon as the program has made the account (step 11).
    $seedNeeded = -not (Test-AdminAccount $adminName)
    $seedPassword = $null
    if ($seedNeeded) {
        if ($AdminPassword) { $seedPassword = $AdminPassword }
        else { $seedPassword = [string](Get-JsonSetting $settings @('AdminUserSeed', 'Password')) }
        if (-not $seedPassword -or $seedPassword -eq 'SET-BY-INSTALL-ps1') { $seedPassword = New-RandomText 14 -Password }
        $newAdminPassword = $seedPassword
    }
    elseif ($AdminPassword) { Write-Info "-AdminPassword is not used: the account '$adminName' exists already (its password is changed in the program)." }

    $httpsSuffix = if ($HttpsPort -eq 443) { '' } else { ":$HttpsPort" }
    Set-JsonSetting $settings @('ConnectionStrings', 'SqlServer', 'ApplicationDbContextConnection') "Data Source=$script:SqlInstance;Initial Catalog=$DatabaseName;Integrated Security=True;TrustServerCertificate=True;"
    Set-JsonSetting $settings @('BearerTokens', 'Key') $jwtKey
    Set-JsonSetting $settings @('BearerTokens', 'Issuer') "https://$($env:COMPUTERNAME.ToLowerInvariant())$httpsSuffix/"
    Set-JsonSetting $settings @('Identity', 'AuditHmacKey') $auditKey
    Set-JsonSetting $settings @('Logging', 'LogLevel') ([pscustomobject]@{ Default = 'Warning'; System = 'Warning'; Microsoft = 'Warning' })
    if ($seedNeeded) { Set-JsonSetting $settings @('AdminUserSeed', 'Password') $seedPassword }
    else { Remove-JsonSetting $settings @('AdminUserSeed', 'Password') }   # an older installation kept the password of the first installation here
    Write-JsonSettings $settings $settingsPath
    Write-Ok 'Database connection uses Windows authentication; sign-in key and audit key kept or created; the other settings were left as they are.'

    # 9. Web site -------------------------------------------------------------------------------
    Write-Step "IIS site '$SiteName' (HTTPS on port $HttpsPort, plain HTTP on this computer only)"
    # HTTPS (R1a2, M6). The sign-in passwords and the tokens travel over this network, so the program is reached over HTTPS only:
    #   - the HTTPS address (*:$HttpsPort) is open to the network and carries the certificate chosen below;
    #   - the plain HTTP address is bound to 127.0.0.1, so it answers on this computer itself only (the installer's own checks, health checks, the recovery sign-in);
    #   - the program sends the HSTS header on its HTTPS answers (one year), so a browser that has seen the HTTPS address does not go back to plain HTTP.
    # Certificate: -CertificateThumbprint (a certificate of the company's own authority or a purchased one, in the computer's personal store with its private key). Without it
    # the certificate the site uses now is kept; on a first installation a self-signed certificate for this computer's names and addresses is made. Browsers warn about a
    # self-signed certificate until its public file (<InstallRoot>\certificates\innco-pmis.cer) is installed as a trusted root certificate on the client computers.
    foreach ($other in @(Get-Website | Where-Object { $_.Name -ne $SiteName })) {
        $usesPort = @($other.bindings.Collection | Where-Object {
            ($_.protocol -eq 'http' -and $_.bindingInformation -like "*:$($Port):") -or ($_.protocol -eq 'https' -and $_.bindingInformation -like "*:$($HttpsPort):")
        }).Count -gt 0
        $autoStart = "$($other.serverAutoStart)" -eq 'True'
        if ($usesPort -and ($other.State -eq 'Started' -or $autoStart)) {
            if ($other.State -eq 'Started') { Stop-Website -Name $other.Name }
            Set-ItemProperty "IIS:\Sites\$($other.Name)" -Name serverAutoStart -Value $false
            Write-Info "Stopped site '$($other.Name)' because it used port $Port or $HttpsPort."
        }
    }
    $loopbackBinding = "127.0.0.1:$($Port):"
    $site = Get-Website | Where-Object { $_.Name -eq $SiteName }
    if (-not $site) {
        $siteId = 1 + (@(Get-Website | ForEach-Object { [int]$_.id }) + 0 | Measure-Object -Maximum).Maximum
        New-Website -Name $SiteName -Id $siteId -PhysicalPath $AppDir -ApplicationPool $SiteName -Port $Port -IPAddress '127.0.0.1' | Out-Null
        Write-Info "Created site '$SiteName'."
    }
    else {
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name physicalPath -Value $AppDir
        Set-ItemProperty "IIS:\Sites\$SiteName" -Name applicationPool -Value $SiteName
    }
    # plain HTTP on the loopback address only: an earlier version bound it to every address
    foreach ($binding in @(Get-WebBinding -Name $SiteName -Protocol http)) {
        if ($binding.bindingInformation -ne $loopbackBinding) {
            # bindingInformation reads "<ip>:<port>:<host name>", for example "*:80:"
            if (([string]$binding.bindingInformation) -match '^(.*):(\d+):(.*)$') {
                $removal = @{ Name = $SiteName; Protocol = 'http'; IPAddress = $Matches[1]; Port = [int]$Matches[2] }
                if ($Matches[3]) { $removal.HostHeader = $Matches[3] }
                Remove-WebBinding @removal
            }
            else { throw "Cannot read the address of the HTTP binding '$($binding.bindingInformation)'." }
            Write-Info "Removed the HTTP address $($binding.bindingInformation) (plain HTTP is for this computer only now)."
        }
    }
    if (@(Get-WebBinding -Name $SiteName -Protocol http | Where-Object { $_.bindingInformation -eq $loopbackBinding }).Count -eq 0) {
        New-WebBinding -Name $SiteName -Protocol http -Port $Port -IPAddress '127.0.0.1'
    }

    # the HTTPS address and its certificate
    $httpsBinding = Get-WebBinding -Name $SiteName -Protocol https | Where-Object { $_.bindingInformation -like "*:$($HttpsPort):*" } | Select-Object -First 1
    $currentHash = $null
    if ($httpsBinding) { $currentHash = [string]$httpsBinding.certificateHash }
    $certificate = Resolve-SiteCertificate $CertificateThumbprint $currentHash
    $certificateThumbprint = $certificate.Thumbprint
    $certificateSelfSigned = $certificate.Subject -eq $certificate.Issuer
    if (-not $httpsBinding) { New-WebBinding -Name $SiteName -Protocol https -Port $HttpsPort -IPAddress '*' -SslFlags 0 }
    $sslPath = "IIS:\SslBindings\0.0.0.0!$HttpsPort"
    if (Test-Path $sslPath) { Remove-Item -Path $sslPath -Force }   # replaces the certificate of an earlier run
    New-Item -Path $sslPath -Value $certificate | Out-Null
    $certificatePublicFile = $null
    if ($certificateSelfSigned) {
        $certificateDir = Join-Path $InstallRoot 'certificates'
        New-Item -ItemType Directory -Force -Path $certificateDir | Out-Null
        $certificatePublicFile = Join-Path $certificateDir 'innco-pmis.cer'
        Export-Certificate -Cert $certificate -FilePath $certificatePublicFile -Force | Out-Null   # public part only; the private key cannot be exported
        Write-Info "Public file of the certificate (for the client computers): $certificatePublicFile"
    }
    Set-ItemProperty "IIS:\Sites\$SiteName" -Name serverAutoStart -Value $true

    $ruleName = "INNCO PMIS (HTTPS TCP $HttpsPort)"
    if (-not (Get-NetFirewallRule -DisplayName $ruleName -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName $ruleName -Direction Inbound -Action Allow -Protocol TCP -LocalPort $HttpsPort -Profile Any | Out-Null
        Write-Info "Firewall: port $HttpsPort opened."
    }
    $oldRuleName = "INNCO PMIS (TCP $Port)"   # the rule of earlier versions opened the plain HTTP port to the network
    if (Get-NetFirewallRule -DisplayName $oldRuleName -ErrorAction SilentlyContinue) {
        Remove-NetFirewallRule -DisplayName $oldRuleName
        Write-Info "Firewall: the rule that opened plain HTTP (port $Port) to the network was removed."
    }
    Write-Ok "Site configured: https on port $HttpsPort (certificate $certificateThumbprint), http on 127.0.0.1:$Port only."

    # 10. Daily backup task ---------------------------------------------------------------------
    Write-Step 'Daily backup (scheduled task, 01:30)'
    $taskName = 'INNCO daily backup'
    $taskArgs = '-NoProfile -ExecutionPolicy Bypass -File "{0}" -SqlServer "{1}" -DatabaseName "{2}" -BackupFolder "{3}" -AppDir "{4}" -KeepDays 14' -f `
                (Join-Path $ToolsDir 'backup.ps1'), $script:SqlInstance, $DatabaseName, $BackupFolder, $AppDir
    $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $taskArgs
    $trigger = New-ScheduledTaskTrigger -Daily -At '01:30'
    $taskPrincipal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $taskSettings = New-ScheduledTaskSettingsSet -StartWhenAvailable -ExecutionTimeLimit (New-TimeSpan -Hours 6)
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $taskPrincipal -Settings $taskSettings -Force | Out-Null
    Write-Ok "Task '$taskName': database to $BackupFolder\database (kept 14 days), uploaded files to $BackupFolder\files."

    # 11. Start and check -----------------------------------------------------------------------
    Write-Step 'Start and check'
    Start-WebAppPool -Name $SiteName -ErrorAction SilentlyContinue
    Start-Website -Name $SiteName
    $base = "http://127.0.0.1:$Port"   # plain HTTP exists on this computer only
    Write-Info 'Waiting for the program to start (the first start can take a few minutes)...'
    $swagger = Wait-Http "$base/swagger/v1/swagger.json" 900
    Write-Ok "API answers ($([int]$swagger.StatusCode))."
    $page = Wait-Http "$base/" 60
    if ($page.Content -notmatch 'id="app"') { throw 'The home page did not return the user interface.' }
    Write-Ok 'User interface answers.'
    $deepPage = Wait-Http "$base/contract/list" 60
    if ($deepPage.Content -notmatch 'id="app"') { throw 'Page addresses inside the program do not return the user interface.' }
    $migrations = [int](Invoke-Sql -Database $DatabaseName -Scalar -Query 'SELECT COUNT(*) FROM dbo.__EFMigrationsHistory')
    $lastMigration = [string](Invoke-Sql -Database $DatabaseName -Scalar -Query 'SELECT TOP (1) MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC')
    Write-Ok "Database structure is up to date ($migrations updates applied, last: $lastMigration)."

    # HTTPS must answer with the certificate that was chosen above
    $served = Get-ServedCertificateThumbprint '127.0.0.1' $HttpsPort
    if ($served -ne $certificateThumbprint) { throw "HTTPS on port $HttpsPort answers with another certificate ($served) than the chosen one ($certificateThumbprint)." }
    Write-Ok "HTTPS answers on port $HttpsPort with the chosen certificate."

    # The administrator account: the program made it when it started. The temporary password is proven once (the program asks for a new password at the first sign-in,
    # which is the right answer here), then it is taken out of the settings file for good and a marker records that.
    if ($seedNeeded) {
        if (-not (Test-AdminAccount $adminName)) { throw "The program did not create the account '$adminName'. See the program logs in $LogsDir." }
        $body = @{ userName = $adminName; password = $seedPassword } | ConvertTo-Json
        $login = Invoke-WebRequest -UseBasicParsing -Method Post -Uri "$base/api/v1/Accounts/login" -ContentType 'application/json' -Body $body
        if ($login.Content -notmatch '"accessToken"\s*:\s*"[^"]+' -and $login.Content -notmatch 'MUST_CHANGE_PASSWORD') { throw "Signing in as '$adminName' failed: $($login.Content)" }
        Write-Ok "The account '$adminName' exists and its temporary password works (the program asks for a new password at the first sign-in)."
        Remove-JsonSetting $settings @('AdminUserSeed', 'Password')
        Write-JsonSettings $settings $settingsPath
        Write-Ok 'The temporary password is no longer in the settings file.'
    }
    if (-not (Test-Path $seedMarker)) {
        $markerDate = Get-Date -Format 'yyyy-MM-dd HH:mm'
        $markerText = @(
            "INNCO PMIS: the seed password of the administrator account '$adminName' was removed from appsettings.Production.json ($markerDate).",
            'The program asked for a new password at the first sign-in; the temporary one is not stored anywhere.',
            'Do not put a password back into AdminUserSeed:Password. A forgotten password is reset in the program by a PMO user.'
        )
        Set-Content -Path $seedMarker -Value $markerText -Encoding ASCII
    }

    if (-not $SkipFirstBackup) {
        Write-Step 'First backup (runs the scheduled task once)'
        $started = (Get-Date).AddSeconds(-5)
        Start-ScheduledTask -TaskName $taskName
        $deadline = (Get-Date).AddHours(2)
        do {
            Start-Sleep -Seconds 5
            $info = Get-ScheduledTaskInfo -TaskName $taskName
            $running = (Get-ScheduledTask -TaskName $taskName).State -eq 'Running'
        } while (($running -or $info.LastRunTime -lt $started) -and (Get-Date) -lt $deadline)
        $taskResult = $info.LastTaskResult
        $backupLog = Join-Path $BackupFolder 'backup-log.txt'
        if (Test-Path $backupLog) { Get-Content $backupLog -Tail 4 | ForEach-Object { Write-Info $_ } }
        if ($taskResult -eq 0) { Write-Ok 'Backup works.' } else { Write-Warn "The backup task ended with code $taskResult. See $backupLog" }
    }

    $ips = @(Get-NetIPAddress -AddressFamily IPv4 -ErrorAction SilentlyContinue |
             Where-Object { $_.IPAddress -notlike '127.*' -and $_.IPAddress -notlike '169.254.*' } |
             ForEach-Object { $_.IPAddress })
    $suffix = if ($HttpsPort -eq 443) { '' } else { ":$HttpsPort" }
    $siteUrls = @("https://$($env:COMPUTERNAME.ToLowerInvariant())$suffix/") + @($ips | ForEach-Object { "https://$_$suffix/" })
}
catch {
    $exitCode = 1
    Write-Host ''
    Write-Host "INSTALLATION STOPPED: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.InvocationInfo) { Write-Host "    at line $($_.InvocationInfo.ScriptLineNumber): $($_.InvocationInfo.Line.Trim())" -ForegroundColor DarkGray }
    if ($LogsDir) {
        Write-Host "    Program logs: $LogsDir (stdout_*.log), and Windows Event Viewer > Application (source: IIS AspNetCore Module V2)."
    }
}
finally {
    if ($script:Warnings.Count -gt 0) {
        Write-Host ''
        Write-Host 'Warnings:' -ForegroundColor Yellow
        foreach ($w in $script:Warnings) { Write-Host "  - $w" -ForegroundColor Yellow }
    }
    if ($script:TranscriptOn) {
        Write-Host ''
        Write-Host "Log file (send this one if something went wrong): $logFile"
        Stop-Transcript | Out-Null
    }
}

if ($exitCode -eq 0) {
    Write-Host ''
    Write-Host 'INSTALLATION FINISHED' -ForegroundColor Green
    Write-Host 'Open the program in a browser on any computer of the network (HTTPS; plain HTTP works on this server only):'
    foreach ($url in $siteUrls) { Write-Host "    $url" }
    if ($certificatePublicFile) {
        Write-Host ''
        Write-Host 'HTTPS certificate (self-signed):' -ForegroundColor Green
        Write-Host '    Browsers warn until this public file is installed as a trusted root certificate on every client computer'
        Write-Host '    (group policy, or double-click the file > Install Certificate > Local Machine > Trusted Root Certification Authorities):'
        Write-Host "        $certificatePublicFile"
        Write-Host '    To use a certificate of the company or a purchased one, run this script again with -CertificateThumbprint <thumbprint>.'
    }
    if ($newAdminPassword) {
        Write-Host ''
        Write-Host 'First sign-in (new account):' -ForegroundColor Green
        Write-Host "    User name: $adminName"
        Write-Host "    Password:  $newAdminPassword   (temporary)"
        Write-Host '    The program asks for a new password at the first sign-in. Write the temporary one down now:'
        Write-Host '    it is not in the log file and it is no longer in the settings file.'
    }
}
exit $exitCode
