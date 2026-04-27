param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$CreateZip = $true,
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

$projectPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectPath "WarehouseAutomatisaion.Desktop.Wpf.csproj"
$assetBaseName = "majorwarehause-$Runtime"
$outputPath = Join-Path $projectPath "..\\artifacts\\publish\\$assetBaseName"
$readmePath = Join-Path $projectPath "..\\docs\\shared-client-deployment.md"
$zipPath = Join-Path $projectPath "..\\artifacts\\publish\\$assetBaseName.zip"

$normalizedVersion = $Version.Trim()
if ($normalizedVersion.StartsWith("v")) {
    $normalizedVersion = $normalizedVersion.Substring(1)
}

$versionParts = $normalizedVersion.Split('.', [System.StringSplitOptions]::RemoveEmptyEntries)
if ($versionParts.Length -lt 3) {
    throw "Version must contain at least major.minor.patch, for example 1.0.1"
}

$assemblyVersion = if ($versionParts.Length -ge 4) {
    ($versionParts[0..3] -join '.')
} else {
    "$normalizedVersion.0"
}

Get-Process -Name "MajorWarehause" -ErrorAction SilentlyContinue | Stop-Process -Force

if (Test-Path $outputPath) {
    Remove-Item -Path $outputPath -Recurse -Force
}

if (Test-Path $zipPath) {
    Remove-Item -Path $zipPath -Force
}

$publishArguments = @(
    "publish"
    $projectFile
    "-c"
    $Configuration
    "-r"
    $Runtime
    "--self-contained"
    "true"
    "/p:PublishSingleFile=true"
    "/p:IncludeNativeLibrariesForSelfExtract=true"
    "/p:PublishTrimmed=false"
    "/p:Version=$normalizedVersion"
    "/p:InformationalVersion=$normalizedVersion"
    "/p:AssemblyVersion=$assemblyVersion"
    "/p:FileVersion=$assemblyVersion"
    "-o"
    $outputPath
)

& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

if (Test-Path $readmePath) {
    Copy-Item -Path $readmePath -Destination (Join-Path $outputPath 'README_DEPLOY.md') -Force
}

if ($CreateZip) {
    Compress-Archive -Path (Join-Path $outputPath '*') -DestinationPath $zipPath -Force
}

Write-Host "Published to $outputPath"
if ($CreateZip) {
    Write-Host "Archive created at $zipPath"
}
Write-Host "Version: $normalizedVersion"
