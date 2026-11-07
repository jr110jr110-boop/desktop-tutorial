#Requires -Version 5.1
<#
.SYNOPSIS
    File-level encryption of the backups (R1t review H2). Used by backup.ps1 and install.ps1; it does nothing by itself.

.DESCRIPTION
    SQL Server Express cannot encrypt a backup (BACKUP ... WITH ENCRYPTION is refused), and the copy of the uploaded files (letters, attachments, payroll papers)
    is a plain file copy on every edition. So the backup files are encrypted after they are made:

      - format "PMISENC1": 8 bytes magic, 16 bytes random IV, AES-256-CBC ciphertext, 32 bytes HMAC-SHA256 over everything before it (encrypt-then-MAC: a changed or cut file is
        refused before one byte is decrypted). The file gets the extension .enc next to the old one (x.bak.enc, x.trn.enc, files\...\name.pdf.enc).
        Windows PowerShell 5.1 runs on .NET Framework, which has no AES-GCM class; AES-CBC with a separate HMAC key gives the same protection (confidentiality and integrity).
      - the key is 64 random bytes (32 for the cipher, 32 for the HMAC). It lives sealed with DPAPI (scope LocalMachine) in <BackupFolder>\keys\file-encryption.key.dpapi and is
        readable on this server only. The installer shows an ESCROW copy of the key once (in the install log); keep it apart from the backups AND from the keys folder.
        The keys folder is not copied off-site (see backup.ps1).
#>

$script:FileCryptoSource = @'
using System;
using System.IO;
using System.Security.Cryptography;

public static class PmisFileCrypto
{
    private static readonly byte[] Magic = new byte[] { (byte)'P', (byte)'M', (byte)'I', (byte)'S', (byte)'E', (byte)'N', (byte)'C', (byte)'1' };
    private const int MacLength = 32;

    // writes to the inner stream and feeds every byte to the HMAC; disposing it leaves the inner stream open
    private sealed class MacWriteStream : Stream
    {
        private readonly Stream _inner; private readonly HMAC _mac;
        public MacWriteStream(Stream inner, HMAC mac) { _inner = inner; _mac = mac; }
        public override void Write(byte[] buffer, int offset, int count) { _inner.Write(buffer, offset, count); _mac.TransformBlock(buffer, offset, count, null, 0); }
        public override void Flush() { _inner.Flush(); }
        public override bool CanRead { get { return false; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return true; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override int Read(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
        protected override void Dispose(bool disposing) { /* the caller owns the inner stream */ }
    }

    private static void Split(byte[] key, out byte[] encryptionKey, out byte[] macKey)
    {
        if (key == null || key.Length != 64) throw new ArgumentException("The file key must be 64 bytes.");
        encryptionKey = new byte[32]; macKey = new byte[32];
        Buffer.BlockCopy(key, 0, encryptionKey, 0, 32);
        Buffer.BlockCopy(key, 32, macKey, 0, 32);
    }

    public static void Encrypt(string source, string destination, byte[] key)
    {
        byte[] encryptionKey, macKey;
        Split(key, out encryptionKey, out macKey);
        string temp = destination + ".partial";
        using (Aes aes = Aes.Create())
        using (HMACSHA256 mac = new HMACSHA256(macKey))
        {
            aes.KeySize = 256; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; aes.Key = encryptionKey; aes.GenerateIV();
            using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20))
            using (FileStream output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            {
                MacWriteStream sealedOutput = new MacWriteStream(output, mac);
                sealedOutput.Write(Magic, 0, Magic.Length);
                sealedOutput.Write(aes.IV, 0, aes.IV.Length);
                using (CryptoStream crypto = new CryptoStream(sealedOutput, aes.CreateEncryptor(), CryptoStreamMode.Write))
                {
                    input.CopyTo(crypto, 1 << 20);
                    crypto.FlushFinalBlock();
                }
                mac.TransformFinalBlock(new byte[0], 0, 0);
                output.Write(mac.Hash, 0, MacLength);
            }
        }
        if (File.Exists(destination)) File.Delete(destination);
        File.Move(temp, destination);
    }

    public static void Decrypt(string source, string destination, byte[] key)
    {
        byte[] encryptionKey, macKey;
        Split(key, out encryptionKey, out macKey);
        long length = new FileInfo(source).Length;
        if (length < Magic.Length + 16 + MacLength) throw new InvalidDataException("The file is too short to be an encrypted backup.");
        // 1. the seal is checked first: nothing is decrypted from a file that was changed or cut
        using (FileStream check = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
        using (HMACSHA256 mac = new HMACSHA256(macKey))
        {
            byte[] buffer = new byte[1 << 20];
            long left = length - MacLength;
            while (left > 0)
            {
                int read = check.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                if (read <= 0) throw new InvalidDataException("The file ended early.");
                mac.TransformBlock(buffer, 0, read, null, 0);
                left -= read;
            }
            mac.TransformFinalBlock(new byte[0], 0, 0);
            byte[] stored = new byte[MacLength];
            int got = 0;
            while (got < MacLength) { int n = check.Read(stored, got, MacLength - got); if (n <= 0) break; got += n; }
            int difference = got == MacLength ? 0 : 1;
            for (int i = 0; i < MacLength && got == MacLength; i++) difference |= stored[i] ^ mac.Hash[i];
            if (difference != 0) throw new CryptographicException("The seal of the file does not fit: the key is not the key of this file, or the file was changed.");
        }
        // 2. decrypt
        string temp = destination + ".partial";
        using (Aes aes = Aes.Create())
        using (FileStream input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20))
        {
            byte[] magic = new byte[Magic.Length];
            input.Read(magic, 0, magic.Length);
            for (int i = 0; i < Magic.Length; i++) if (magic[i] != Magic[i]) throw new InvalidDataException("This is not an encrypted backup of the PMIS (wrong header).");
            byte[] iv = new byte[16];
            input.Read(iv, 0, 16);
            aes.KeySize = 256; aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.PKCS7; aes.Key = encryptionKey; aes.IV = iv;
            long cipherLeft = length - Magic.Length - 16 - MacLength;
            using (FileStream output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20))
            using (CryptoStream crypto = new CryptoStream(output, aes.CreateDecryptor(), CryptoStreamMode.Write))
            {
                byte[] buffer = new byte[1 << 20];
                while (cipherLeft > 0)
                {
                    int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, cipherLeft));
                    if (read <= 0) throw new InvalidDataException("The file ended early.");
                    crypto.Write(buffer, 0, read);
                    cipherLeft -= read;
                }
                crypto.FlushFinalBlock();
            }
        }
        if (File.Exists(destination)) File.Delete(destination);
        File.Move(temp, destination);
    }
}
'@

function Initialize-FileCrypto {
    if (-not ('PmisFileCrypto' -as [type])) { Add-Type -TypeDefinition $script:FileCryptoSource -Language CSharp }
    Add-Type -AssemblyName System.Security
}

$script:FileKeyName = 'file-encryption.key.dpapi'
$script:FileKeyEntropy = [Text.Encoding]::UTF8.GetBytes('INNCO PMIS backup file key v1')

# The key of the file encryption. An explicit key (base64 of the 64 bytes, the escrow copy) wins; else the DPAPI-sealed file in the keys folder. With -Create a missing key is made.
# Returns an object: Bytes, Created (true when made now), Escrow (base64, only when Created).
function Get-FileCryptoKey([string]$KeysFolder, [string]$ExplicitKey, [switch]$Create) {
    Initialize-FileCrypto
    if ($ExplicitKey) {
        $bytes = [Convert]::FromBase64String($ExplicitKey.Trim())
        if ($bytes.Length -ne 64) { throw 'The backup file key must be the 64-byte escrow key (base64).' }
        $file = Join-Path $KeysFolder $script:FileKeyName
        if ($Create -and -not (Test-Path $file)) {
            # a restore on a new server: the escrow key becomes the key of this server, so that old and new backups open with the same key
            New-Item -ItemType Directory -Force -Path $KeysFolder | Out-Null
            [IO.File]::WriteAllBytes($file, [Security.Cryptography.ProtectedData]::Protect($bytes, $script:FileKeyEntropy, [Security.Cryptography.DataProtectionScope]::LocalMachine))
        }
        return [pscustomobject]@{ Bytes = $bytes; Created = $false; Escrow = $null }
    }
    $file = Join-Path $KeysFolder $script:FileKeyName
    if (Test-Path $file) {
        $sealed = [IO.File]::ReadAllBytes($file)
        $bytes = [Security.Cryptography.ProtectedData]::Unprotect($sealed, $script:FileKeyEntropy, [Security.Cryptography.DataProtectionScope]::LocalMachine)
        return [pscustomobject]@{ Bytes = $bytes; Created = $false; Escrow = $null }
    }
    if (-not $Create) { throw "The key of the backup file encryption is missing: $file (install.ps1 makes it; restore it from the escrow copy with -BackupFileKey)." }
    New-Item -ItemType Directory -Force -Path $KeysFolder | Out-Null
    $bytes = New-Object byte[] 64
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $sealed = [Security.Cryptography.ProtectedData]::Protect($bytes, $script:FileKeyEntropy, [Security.Cryptography.DataProtectionScope]::LocalMachine)
    [IO.File]::WriteAllBytes($file, $sealed)
    return [pscustomobject]@{ Bytes = $bytes; Created = $true; Escrow = [Convert]::ToBase64String($bytes) }
}

# Encrypts $Path to "$Path.enc" and removes $Path when the encrypted file can be opened again (the seal is checked by a trial decryption of its header and tail).
function Protect-BackupFile([string]$Path, [byte[]]$Key) {
    Initialize-FileCrypto
    $target = $Path + '.enc'
    [PmisFileCrypto]::Encrypt($Path, $target, $Key)
    if ((Get-Item $target).Length -lt (Get-Item $Path).Length) { Remove-Item -Path $target -Force; throw "The encrypted copy of $Path is shorter than the file; the plain file is kept." }
    Remove-Item -Path $Path -Force
    return $target
}

# Decrypts "x.ext.enc" to $Destination (default: the same name without .enc, next to it). The seal is checked before anything is written.
function Unprotect-BackupFile([string]$Path, [byte[]]$Key, [string]$Destination) {
    Initialize-FileCrypto
    if (-not $Destination) { $Destination = if ($Path.EndsWith('.enc', [StringComparison]::OrdinalIgnoreCase)) { $Path.Substring(0, $Path.Length - 4) } else { $Path + '.dec' } }
    [PmisFileCrypto]::Decrypt($Path, $Destination, $Key)
    return $Destination
}
