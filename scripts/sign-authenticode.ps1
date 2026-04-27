param(
    [Parameter(Mandatory = $true)]
    [string[]]$Path,

    [string]$CertificateThumbprint = $env:MAJORWAREHAUSE_CODESIGN_THUMBPRINT,
    [string]$CertificatePath = $env:MAJORWAREHAUSE_CODESIGN_PFX_PATH,
    [string]$CertificatePassword = $env:MAJORWAREHAUSE_CODESIGN_PFX_PASSWORD,
    [string]$TimestampServer = $env:MAJORWAREHAUSE_CODESIGN_TIMESTAMP_SERVER,
    [switch]$SkipIfNoCertificate
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($TimestampServer)) {
    $TimestampServer = "http://timestamp.digicert.com"
}

function Get-CodeSigningCertificate {
    if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
        if (-not (Test-Path $CertificatePath)) {
            throw "Code signing certificate file was not found: $CertificatePath"
        }

        $storageFlags =
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::Exportable -bor
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::UserKeySet

        return [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
            $CertificatePath,
            $CertificatePassword,
            $storageFlags)
    }

    if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        $normalizedThumbprint = $CertificateThumbprint.Replace(" ", "").ToUpperInvariant()
        $stores = @("Cert:\CurrentUser\My", "Cert:\LocalMachine\My")

        foreach ($store in $stores) {
            $certificate = Get-ChildItem -Path $store -ErrorAction SilentlyContinue |
                Where-Object { $_.Thumbprint.Replace(" ", "").ToUpperInvariant() -eq $normalizedThumbprint } |
                Select-Object -First 1

            if ($null -ne $certificate) {
                return $certificate
            }
        }

        throw "Code signing certificate with thumbprint $CertificateThumbprint was not found."
    }

    return $null
}

$certificate = Get-CodeSigningCertificate
if ($null -eq $certificate) {
    if ($SkipIfNoCertificate) {
        Write-Host "Code signing skipped: no certificate was provided."
        exit 0
    }

    throw "Code signing certificate was not provided. Use -CertificateThumbprint or -CertificatePath."
}

if (-not $certificate.HasPrivateKey) {
    throw "Code signing certificate does not contain a private key."
}

foreach ($targetPath in $Path) {
    if (-not (Test-Path $targetPath)) {
        throw "File to sign was not found: $targetPath"
    }

    $signature = Set-AuthenticodeSignature `
        -FilePath $targetPath `
        -Certificate $certificate `
        -HashAlgorithm SHA256 `
        -TimestampServer $TimestampServer

    if ($null -eq $signature -or $signature.Status -eq "HashMismatch" -or $signature.Status -eq "NotSigned") {
        throw "Code signing failed for $targetPath. Status: $($signature.Status)"
    }

    Write-Host "Signed $targetPath. Status: $($signature.Status)"
}
