param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$CreateZip = $true,
    [string]$Version = "1.0.0",
    [string]$CodeSigningCertificateThumbprint = $env:MAJORWAREHAUSE_CODESIGN_THUMBPRINT,
    [string]$CodeSigningCertificatePath = $env:MAJORWAREHAUSE_CODESIGN_PFX_PATH,
    [string]$CodeSigningCertificatePassword = $env:MAJORWAREHAUSE_CODESIGN_PFX_PASSWORD,
    [string]$CodeSigningTimestampServer = $env:MAJORWAREHAUSE_CODESIGN_TIMESTAMP_SERVER,
    [string]$RemoteDatabaseEnabled = $env:WAREHOUSE_REMOTE_DB_ENABLED,
    [string]$RemoteDatabaseHost = $env:WAREHOUSE_MYSQL_HOST,
    [string]$RemoteDatabasePort = $env:WAREHOUSE_MYSQL_PORT,
    [string]$RemoteDatabaseName = $env:WAREHOUSE_MYSQL_DATABASE,
    [string]$RemoteDatabaseUser = $env:WAREHOUSE_MYSQL_USER,
    [string]$RemoteDatabasePassword = $env:WAREHOUSE_MYSQL_PASSWORD,
    [string]$RemoteDatabaseMysqlPath = $env:WAREHOUSE_MYSQL_PATH,
    [switch]$RequireCodeSigning
)

$ErrorActionPreference = "Stop"

$projectPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectFile = Join-Path $projectPath "WarehouseAutomatisaion.Desktop.Wpf.csproj"
$assetBaseName = "majorwarehause-$Runtime"
$outputPath = Join-Path $projectPath "..\\artifacts\\publish\\$assetBaseName"
$readmePath = Join-Path $projectPath "..\\docs\\shared-client-deployment.md"
$zipPath = Join-Path $projectPath "..\\artifacts\\publish\\$assetBaseName.zip"
$signScript = Join-Path $projectPath "..\\scripts\\sign-authenticode.ps1"

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

function Set-JsonProperty {
    param(
        [Parameter(Mandatory = $true)][object]$Target,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)]$Value
    )

    if ($Target.PSObject.Properties.Name -contains $Name) {
        $Target.$Name = $Value
        return
    }

    $Target | Add-Member -NotePropertyName $Name -NotePropertyValue $Value
}

function Merge-JsonObjectProperties {
    param(
        [Parameter(Mandatory = $true)]$Source,
        [Parameter(Mandatory = $true)]$Target
    )

    foreach ($property in $Source.PSObject.Properties) {
        Set-JsonProperty -Target $Target -Name $property.Name -Value $property.Value
    }
}

function Update-PublishedAppSettings {
    $hasRemoteOverride =
        -not [string]::IsNullOrWhiteSpace($RemoteDatabaseEnabled) -or
        -not [string]::IsNullOrWhiteSpace($RemoteDatabaseHost) -or
        -not [string]::IsNullOrWhiteSpace($RemoteDatabasePort) -or
        -not [string]::IsNullOrWhiteSpace($RemoteDatabaseName) -or
        -not [string]::IsNullOrWhiteSpace($RemoteDatabaseUser) -or
        -not [string]::IsNullOrWhiteSpace($RemoteDatabasePassword) -or
        -not [string]::IsNullOrWhiteSpace($RemoteDatabaseMysqlPath)

    $appSettingsPath = Join-Path $outputPath "appsettings.json"
    $localSettingsPath = Join-Path $outputPath "appsettings.local.json"
    $hasPublishedLocalSettings = Test-Path $localSettingsPath

    if (-not $hasRemoteOverride -and -not $hasPublishedLocalSettings) {
        return
    }

    if (-not (Test-Path $appSettingsPath)) {
        throw "Published appsettings.json was not found: $appSettingsPath"
    }

    $settings = Get-Content -LiteralPath $appSettingsPath -Raw | ConvertFrom-Json
    if ($null -eq $settings.RemoteDatabase) {
        Set-JsonProperty -Target $settings -Name "RemoteDatabase" -Value ([pscustomobject]@{})
    }

    if ($hasPublishedLocalSettings) {
        $localSettings = Get-Content -LiteralPath $localSettingsPath -Raw | ConvertFrom-Json
        if ($null -ne $localSettings.RemoteDatabase) {
            Merge-JsonObjectProperties -Source $localSettings.RemoteDatabase -Target $settings.RemoteDatabase
        }

        if ($null -ne $localSettings.ApplicationUpdate) {
            if ($null -eq $settings.ApplicationUpdate) {
                Set-JsonProperty -Target $settings -Name "ApplicationUpdate" -Value ([pscustomobject]@{})
            }

            Merge-JsonObjectProperties -Source $localSettings.ApplicationUpdate -Target $settings.ApplicationUpdate
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($RemoteDatabaseEnabled)) {
        $enabled = $false
        if (-not [bool]::TryParse($RemoteDatabaseEnabled, [ref]$enabled)) {
            throw "RemoteDatabaseEnabled must be true or false."
        }

        Set-JsonProperty -Target $settings.RemoteDatabase -Name "Enabled" -Value $enabled
    }
    elseif ($hasRemoteOverride) {
        Set-JsonProperty -Target $settings.RemoteDatabase -Name "Enabled" -Value $true
    }

    if (-not [string]::IsNullOrWhiteSpace($RemoteDatabaseHost)) {
        Set-JsonProperty -Target $settings.RemoteDatabase -Name "Host" -Value $RemoteDatabaseHost
    }

    if (-not [string]::IsNullOrWhiteSpace($RemoteDatabasePort)) {
        $port = 0
        if (-not [int]::TryParse($RemoteDatabasePort, [ref]$port)) {
            throw "RemoteDatabasePort must be a number."
        }

        Set-JsonProperty -Target $settings.RemoteDatabase -Name "Port" -Value $port
    }

    if (-not [string]::IsNullOrWhiteSpace($RemoteDatabaseName)) {
        Set-JsonProperty -Target $settings.RemoteDatabase -Name "Database" -Value $RemoteDatabaseName
    }

    if (-not [string]::IsNullOrWhiteSpace($RemoteDatabaseUser)) {
        Set-JsonProperty -Target $settings.RemoteDatabase -Name "User" -Value $RemoteDatabaseUser
    }

    if (-not [string]::IsNullOrWhiteSpace($RemoteDatabasePassword)) {
        Set-JsonProperty -Target $settings.RemoteDatabase -Name "Password" -Value $RemoteDatabasePassword
    }

    if (-not [string]::IsNullOrWhiteSpace($RemoteDatabaseMysqlPath)) {
        Set-JsonProperty -Target $settings.RemoteDatabase -Name "MysqlExecutablePath" -Value $RemoteDatabaseMysqlPath
    }

    $settings | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $appSettingsPath -Encoding UTF8
    Write-Host "Remote database settings were embedded into published appsettings.json"
}

Update-PublishedAppSettings

if (Test-Path $readmePath) {
    Copy-Item -Path $readmePath -Destination (Join-Path $outputPath 'README_DEPLOY.md') -Force
}

$publishedExePath = Join-Path $outputPath "MajorWarehause.exe"
$hasCodeSigningCertificate =
    -not [string]::IsNullOrWhiteSpace($CodeSigningCertificateThumbprint) -or
    -not [string]::IsNullOrWhiteSpace($CodeSigningCertificatePath)

if ($RequireCodeSigning -or $hasCodeSigningCertificate) {
    $signArguments = @(
        "-ExecutionPolicy"
        "Bypass"
        "-File"
        $signScript
        "-Path"
        $publishedExePath
    )

    if (-not [string]::IsNullOrWhiteSpace($CodeSigningCertificateThumbprint)) {
        $signArguments += @("-CertificateThumbprint", $CodeSigningCertificateThumbprint)
    }

    if (-not [string]::IsNullOrWhiteSpace($CodeSigningCertificatePath)) {
        $signArguments += @("-CertificatePath", $CodeSigningCertificatePath)
    }

    if (-not [string]::IsNullOrWhiteSpace($CodeSigningCertificatePassword)) {
        $signArguments += @("-CertificatePassword", $CodeSigningCertificatePassword)
    }

    if (-not [string]::IsNullOrWhiteSpace($CodeSigningTimestampServer)) {
        $signArguments += @("-TimestampServer", $CodeSigningTimestampServer)
    }

    if (-not $RequireCodeSigning) {
        $signArguments += "-SkipIfNoCertificate"
    }

    & powershell @signArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Code signing failed with exit code $LASTEXITCODE"
    }
}

if ($CreateZip) {
    Compress-Archive -Path (Join-Path $outputPath '*') -DestinationPath $zipPath -Force
}

Write-Host "Published to $outputPath"
if ($CreateZip) {
    Write-Host "Archive created at $zipPath"
}
Write-Host "Version: $normalizedVersion"
