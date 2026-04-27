param(
    [string]$Subject = "CN=MajorWarehause Local Code Signing",
    [int]$Years = 3,
    [switch]$TrustForCurrentUser
)

$ErrorActionPreference = "Stop"

$certificate = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyAlgorithm RSA `
    -KeyLength 3072 `
    -HashAlgorithm SHA256 `
    -KeyExportPolicy Exportable `
    -NotAfter (Get-Date).AddYears($Years)

if ($TrustForCurrentUser) {
    $certificatePath = Join-Path $env:TEMP "MajorWarehauseLocalCodeSigning.cer"
    Export-Certificate -Cert $certificate -FilePath $certificatePath | Out-Null
    Import-Certificate -FilePath $certificatePath -CertStoreLocation "Cert:\CurrentUser\Root" | Out-Null
    Import-Certificate -FilePath $certificatePath -CertStoreLocation "Cert:\CurrentUser\TrustedPublisher" | Out-Null
    Remove-Item -LiteralPath $certificatePath -Force -ErrorAction SilentlyContinue
}

Write-Host "Certificate created."
Write-Host "Subject: $($certificate.Subject)"
Write-Host "Thumbprint: $($certificate.Thumbprint)"
Write-Host "Use: -CodeSigningCertificateThumbprint $($certificate.Thumbprint)"

if (-not $TrustForCurrentUser) {
    Write-Host "This self-signed certificate is not trusted yet. Use -TrustForCurrentUser for local trust."
}
