param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.0.0",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$publishScript = Join-Path $root "WarehouseAutomatisaion.Desktop.Wpf\publish-win-x64.ps1"
$publishAssetName = "majorwarehause-$Runtime"
$publishRoot = Join-Path $root "artifacts\publish"
$publishZipPath = Join-Path $publishRoot "$publishAssetName.zip"
$installerRoot = Join-Path $root "artifacts\installers"
$stagingRoot = Join-Path $root "artifacts\installer-staging"
$setupPath = Join-Path $installerRoot "MajorWarehauseSetup.exe"

if (-not $SkipPublish) {
    & powershell -ExecutionPolicy Bypass -File $publishScript `
        -Configuration $Configuration `
        -Runtime $Runtime `
        -Version $Version

    if ($LASTEXITCODE -ne 0) {
        throw "Publish failed with exit code $LASTEXITCODE"
    }
}

if (-not (Test-Path $publishZipPath)) {
    throw "Publish archive was not found: $publishZipPath"
}

$iexpressCommand = Get-Command iexpress.exe -ErrorAction SilentlyContinue
$iexpress = if ($null -eq $iexpressCommand) { $null } else { $iexpressCommand.Source }
if ([string]::IsNullOrWhiteSpace($iexpress)) {
    throw "iexpress.exe was not found. This installer builder requires the built-in Windows IExpress tool."
}

if (Test-Path $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
New-Item -ItemType Directory -Path $installerRoot -Force | Out-Null

$packageFileName = Split-Path -Leaf $publishZipPath
Copy-Item -LiteralPath $publishZipPath -Destination (Join-Path $stagingRoot $packageFileName) -Force

$installCmd = @'
@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
exit /b %ERRORLEVEL%
'@

$installPs1 = @'
$ErrorActionPreference = "Stop"

$appName = "MajorWarehause"
$installRoot = Join-Path $env:LOCALAPPDATA "Programs\MajorWarehause"
$zipPath = Join-Path $PSScriptRoot "majorwarehause-win-x64.zip"
$extractRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("MajorWarehause-install-" + [Guid]::NewGuid().ToString("N"))
$backupRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("MajorWarehause-backup-" + [Guid]::NewGuid().ToString("N"))

if (-not (Test-Path $zipPath)) {
    throw "Package archive was not found: $zipPath"
}

New-Item -ItemType Directory -Path $installRoot -Force | Out-Null
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null

$localConfigPath = Join-Path $installRoot "appsettings.local.json"
$appDataPath = Join-Path $installRoot "app_data"
$localConfigBackup = Join-Path $backupRoot "appsettings.local.json"
$appDataBackup = Join-Path $backupRoot "app_data"

if (Test-Path $localConfigPath) {
    Copy-Item -LiteralPath $localConfigPath -Destination $localConfigBackup -Force
}

if (Test-Path $appDataPath) {
    Copy-Item -LiteralPath $appDataPath -Destination $appDataBackup -Recurse -Force
}

Get-Process -Name $appName -ErrorAction SilentlyContinue | Stop-Process -Force

if (Test-Path $extractRoot) {
    Remove-Item -LiteralPath $extractRoot -Recurse -Force
}

Expand-Archive -LiteralPath $zipPath -DestinationPath $extractRoot -Force
Copy-Item -Path (Join-Path $extractRoot "*") -Destination $installRoot -Recurse -Force

if (Test-Path $localConfigBackup) {
    Copy-Item -LiteralPath $localConfigBackup -Destination $localConfigPath -Force
}

if (Test-Path $appDataBackup) {
    Copy-Item -LiteralPath $appDataBackup -Destination $appDataPath -Recurse -Force
}

$exePath = Join-Path $installRoot "MajorWarehause.exe"
if (-not (Test-Path $exePath)) {
    throw "Installed executable was not found: $exePath"
}

function New-AppShortcut {
    param(
        [Parameter(Mandatory = $true)][string]$ShortcutPath,
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.IconLocation = "$TargetPath,0"
    $shortcut.Save()
}

$desktop = [Environment]::GetFolderPath("DesktopDirectory")
$programs = [Environment]::GetFolderPath("Programs")

New-AppShortcut `
    -ShortcutPath (Join-Path $desktop "MajorWarehause.lnk") `
    -TargetPath $exePath `
    -WorkingDirectory $installRoot

New-AppShortcut `
    -ShortcutPath (Join-Path $programs "MajorWarehause.lnk") `
    -TargetPath $exePath `
    -WorkingDirectory $installRoot

Remove-Item -LiteralPath $extractRoot -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $backupRoot -Recurse -Force -ErrorAction SilentlyContinue

Start-Process -FilePath $exePath -WorkingDirectory $installRoot
'@

Set-Content -LiteralPath (Join-Path $stagingRoot "install.cmd") -Value $installCmd -Encoding ASCII
Set-Content -LiteralPath (Join-Path $stagingRoot "install.ps1") -Value $installPs1 -Encoding UTF8

if (Test-Path $setupPath) {
    Remove-Item -LiteralPath $setupPath -Force
}

$sedPath = Join-Path $stagingRoot "MajorWarehauseSetup.sed"
$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3

[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=0
HideExtractAnimation=1
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=%AppLaunched%
PostInstallCmd=%PostInstallCmd%
AdminQuietInstCmd=%AdminQuietInstCmd%
UserQuietInstCmd=%UserQuietInstCmd%
SourceFiles=SourceFiles

[Strings]
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=$setupPath
FriendlyName=MajorWarehause Setup
AppLaunched=install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
FILE0="install.cmd"
FILE1="install.ps1"
FILE2="$packageFileName"

[SourceFiles]
SourceFiles0=$stagingRoot\

[SourceFiles0]
%FILE0%=
%FILE1%=
%FILE2%=
"@

Set-Content -LiteralPath $sedPath -Value $sed -Encoding ASCII

& $iexpress /N /Q $sedPath
$iexpressExitCode = $LASTEXITCODE

$deadline = (Get-Date).AddMinutes(5)
$lastSetupSize = -1
$stableChecks = 0
while ((Get-Date) -lt $deadline) {
    if (Test-Path $setupPath) {
        $setupSize = (Get-Item -LiteralPath $setupPath).Length
        if ($setupSize -gt 0 -and $setupSize -eq $lastSetupSize) {
            $stableChecks++
        }
        else {
            $lastSetupSize = $setupSize
            $stableChecks = 0
        }

        if ($stableChecks -ge 2) {
            break
        }
    }

    Start-Sleep -Seconds 2
}

if ($null -ne $iexpressExitCode -and $iexpressExitCode -ne 0 -and -not (Test-Path $setupPath)) {
    throw "IExpress failed with exit code $iexpressExitCode"
}

if (-not (Test-Path $setupPath)) {
    throw "Setup executable was not created: $setupPath"
}

if ($null -ne $iexpressExitCode -and $iexpressExitCode -ne 0) {
    Write-Warning "IExpress returned exit code $iexpressExitCode, but setup executable was created."
}

Write-Host "Installer created at $setupPath"
