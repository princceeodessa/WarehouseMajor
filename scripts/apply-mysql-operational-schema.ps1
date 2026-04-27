param(
    [Parameter(Mandatory = $true)]
    [string]$DatabaseName,

    [string]$Host = '127.0.0.1',

    [int]$Port = 3306,

    [string]$User = 'root',

    [string]$Password = '',

    [string]$MysqlExe = '',

    [string]$SchemaPath = 'C:\blagodar\WarehouseAutomatisaion\WarehouseAutomatisaion.Infrastructure\Persistence\Sql\mysql-operational-schema.sql',

    [switch]$SkipCreateDatabase
)

$ErrorActionPreference = 'Stop'

function Resolve-MysqlExe {
    param([string]$PreferredPath)

    if (-not [string]::IsNullOrWhiteSpace($PreferredPath) -and (Test-Path $PreferredPath)) {
        return (Resolve-Path $PreferredPath).Path
    }

    $command = Get-Command mysql -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $knownLaragonPaths = @(
        'C:\laragon\bin\mysql\mysql-8.4.3-winx64\bin\mysql.exe',
        'C:\laragon\bin\mysql\mysql-8.3.0-winx64\bin\mysql.exe',
        'C:\laragon\bin\mysql\mysql-8.0.30-winx64\bin\mysql.exe'
    )

    foreach ($path in $knownLaragonPaths) {
        if (Test-Path $path) {
            return $path
        }
    }

    throw 'mysql.exe not found. Pass -MysqlExe explicitly.'
}

function New-MysqlArgs {
    param(
        [string]$TargetDatabase = ''
    )

    $args = @(
        "--host=$Host",
        "--port=$Port",
        "--user=$User",
        '--default-character-set=utf8mb4'
    )

    if (-not [string]::IsNullOrEmpty($Password)) {
        $args += "--password=$Password"
    }

    if (-not [string]::IsNullOrWhiteSpace($TargetDatabase)) {
        $args += $TargetDatabase
    }

    return $args
}

function Invoke-MysqlCommand {
    param(
        [string]$Sql,
        [string]$TargetDatabase = ''
    )

    $args = New-MysqlArgs -TargetDatabase $TargetDatabase
    $Sql | & $script:MysqlExecutable @args

    if ($LASTEXITCODE -ne 0) {
        throw "mysql.exe returned exit code $LASTEXITCODE."
    }
}

if ($DatabaseName -notmatch '^[A-Za-z0-9_]+$') {
    throw 'DatabaseName may contain only letters, numbers and underscore.'
}

if (-not (Test-Path $SchemaPath)) {
    throw "Schema file not found: $SchemaPath"
}

$script:MysqlExecutable = Resolve-MysqlExe -PreferredPath $MysqlExe
$schemaText = Get-Content -Path $SchemaPath -Raw -Encoding UTF8

Invoke-MysqlCommand -Sql 'SELECT VERSION();'

if (-not $SkipCreateDatabase) {
    Invoke-MysqlCommand -Sql "CREATE DATABASE IF NOT EXISTS $DatabaseName CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;"
}

Invoke-MysqlCommand -Sql $schemaText -TargetDatabase $DatabaseName

Write-Host "Operational schema applied to database $DatabaseName using $script:MysqlExecutable"
