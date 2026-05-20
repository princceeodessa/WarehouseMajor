#requires -Version 5.1
<#
.SYNOPSIS
    Импортирует штрихкоды товаров из 1С УНФ (unf-products-first-pass.csv) в MySQL-таблицу app_product_barcodes.

.DESCRIPTION
    Читает CSV с колонками ItemCode, CardBarcode, RegisterBarcodes, BarcodeSource.
    Генерирует SQL-файл с UPSERT в app_product_barcodes:
      * CardBarcode → kind='Основной', source='Card'
      * RegisterBarcodes (через ';') → kind='Дополнительный', source='Register'
      * Если CardBarcode пуст, но RegisterBarcodes есть — первый из них становится 'Основным'
    Также UPDATE app_catalog_items.barcode_value для товаров, у которых поле пустое.

    Запуск SQL производится отдельно через mysql.exe (или клиента Major при следующем запуске
    подхватит CREATE TABLE IF NOT EXISTS из EnsureCatalogTables).

.EXAMPLE
    pwsh ./scripts/Import-UnfBarcodesToMySql.ps1
    pwsh ./scripts/Import-UnfBarcodesToMySql.ps1 -RunMysql -MysqlHost 147.45.108.97 -MysqlUser app_user -MysqlPassword '****'
#>
param(
    [string]$ProductsCsvPath = "C:\blagodar\1c-migration\exports\unf-products-first-pass.csv",
    [string]$SqlOutputPath  = "C:\blagodar\WarehouseAutomatisaion\_temp_build\seed-product-barcodes.sql",
    [int]   $BatchSize      = 500,
    [switch]$RunMysql,
    [string]$MysqlHost      = "147.45.108.97",
    [int]   $MysqlPort      = 3306,
    [string]$MysqlDatabase  = "warehouse_major",
    [string]$MysqlUser      = "",
    [string]$MysqlPassword  = ""
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ProductsCsvPath)) {
    throw "CSV не найден: $ProductsCsvPath"
}

$outDir = Split-Path -Parent $SqlOutputPath
if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}

function ConvertTo-SqlString {
    param([string]$Value)
    if ($null -eq $Value) { return "NULL" }
    $escaped = $Value.Replace("\", "\\").Replace("'", "''")
    return "'$escaped'"
}

function Test-BarcodeValue {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $false }
    $trimmed = $Value.Trim()
    if ($trimmed.Length -lt 4)  { return $false }
    if ($trimmed.Length -gt 64) { return $false }
    return $trimmed -match '^[0-9A-Za-z\-_\.]+$'
}

Write-Host "Читаю CSV: $ProductsCsvPath"
$rows = Import-Csv -Path $ProductsCsvPath -Encoding UTF8
Write-Host "Прочитано строк: $($rows.Count)"

# Соберём триплеты (item_code, barcode, kind, source) — без дублей.
$entries = New-Object System.Collections.Generic.List[object]
$itemCodeToCard = New-Object 'System.Collections.Generic.Dictionary[string,string]'
$seenPairs = New-Object 'System.Collections.Generic.HashSet[string]'

foreach ($row in $rows) {
    $itemCode = ([string]$row.ItemCode).Trim()
    if ([string]::IsNullOrWhiteSpace($itemCode)) { continue }

    $card = ([string]$row.CardBarcode).Trim()
    $registerRaw = ([string]$row.RegisterBarcodes).Trim()
    $sourceHint = ([string]$row.BarcodeSource).Trim()
    if ([string]::IsNullOrWhiteSpace($sourceHint)) { $sourceHint = "Unknown" }

    $primary = $null
    if (Test-BarcodeValue $card) {
        $primary = $card
    }

    $registerList = @()
    if (-not [string]::IsNullOrWhiteSpace($registerRaw)) {
        $registerList = @($registerRaw.Split(@(';',',',"`t"), [StringSplitOptions]::RemoveEmptyEntries) |
            ForEach-Object { $_.Trim() } |
            Where-Object { Test-BarcodeValue $_ })
    }

    # Если основного нет, но в регистре что-то есть — первый из регистра становится Основным.
    if (-not $primary -and $registerList.Count -gt 0) {
        $primary = $registerList[0]
        $registerList = @($registerList | Select-Object -Skip 1)
    }

    if ($primary) {
        $key = "$itemCode|$primary"
        if ($seenPairs.Add($key)) {
            $entries.Add([pscustomobject]@{
                ItemCode = $itemCode
                Value    = $primary
                Kind     = "Основной"
                Source   = $sourceHint
            })
            $itemCodeToCard[$itemCode] = $primary
        }
    }

    foreach ($extra in $registerList) {
        $key = "$itemCode|$extra"
        if ($seenPairs.Add($key)) {
            $entries.Add([pscustomobject]@{
                ItemCode = $itemCode
                Value    = $extra
                Kind     = "Дополнительный"
                Source   = "Register"
            })
        }
    }
}

Write-Host "Уникальных штрихкодов к импорту: $($entries.Count)"
Write-Host "Товаров с основным штрихкодом: $($itemCodeToCard.Count)"

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("-- Импорт штрихкодов товаров из 1С УНФ (unf-products-first-pass.csv)")
[void]$sb.AppendLine("-- Сгенерировано: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
[void]$sb.AppendLine("-- Источник: $ProductsCsvPath")
[void]$sb.AppendLine("-- Записей: $($entries.Count) штрихкодов для $($itemCodeToCard.Count) товаров")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("SET NAMES utf8mb4;")
[void]$sb.AppendLine("SET @@SESSION.sql_mode = 'NO_AUTO_VALUE_ON_ZERO';")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("-- На случай если автомиграция ещё не запускалась — создаём таблицу.")
[void]$sb.AppendLine("CREATE TABLE IF NOT EXISTS app_product_barcodes (")
[void]$sb.AppendLine("    id BIGINT NOT NULL AUTO_INCREMENT,")
[void]$sb.AppendLine("    item_code VARCHAR(128) NOT NULL,")
[void]$sb.AppendLine("    barcode_value VARCHAR(256) NOT NULL,")
[void]$sb.AppendLine("    barcode_kind VARCHAR(32) NOT NULL DEFAULT 'Основной',")
[void]$sb.AppendLine("    barcode_source VARCHAR(64) NULL,")
[void]$sb.AppendLine("    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),")
[void]$sb.AppendLine("    CONSTRAINT pk_app_product_barcodes PRIMARY KEY (id),")
[void]$sb.AppendLine("    CONSTRAINT uq_app_product_barcodes UNIQUE KEY (item_code, barcode_value),")
[void]$sb.AppendLine("    INDEX ix_app_product_barcodes_item_code (item_code),")
[void]$sb.AppendLine("    INDEX ix_app_product_barcodes_value (barcode_value)")
[void]$sb.AppendLine(") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("-- ============================ Штрихкоды ============================")

$i = 0
while ($i -lt $entries.Count) {
    $chunk = $entries[$i..([Math]::Min($i + $BatchSize - 1, $entries.Count - 1))]
    [void]$sb.AppendLine("INSERT INTO app_product_barcodes (item_code, barcode_value, barcode_kind, barcode_source) VALUES")
    $lines = @()
    foreach ($entry in $chunk) {
        $codeSql   = ConvertTo-SqlString $entry.ItemCode
        $valueSql  = ConvertTo-SqlString $entry.Value
        $kindSql   = ConvertTo-SqlString $entry.Kind
        $sourceSql = ConvertTo-SqlString $entry.Source
        $lines += "    ($codeSql, $valueSql, $kindSql, $sourceSql)"
    }
    [void]$sb.AppendLine(($lines -join ",`n"))
    [void]$sb.AppendLine("ON DUPLICATE KEY UPDATE")
    [void]$sb.AppendLine("    barcode_kind = VALUES(barcode_kind),")
    [void]$sb.AppendLine("    barcode_source = VALUES(barcode_source);")
    [void]$sb.AppendLine("")
    $i += $BatchSize
}

[void]$sb.AppendLine("-- ============ Backfill app_catalog_items.barcode_value если пусто ============")
$i = 0
$updateEntries = @($itemCodeToCard.GetEnumerator())
while ($i -lt $updateEntries.Count) {
    $chunk = $updateEntries[$i..([Math]::Min($i + $BatchSize - 1, $updateEntries.Count - 1))]
    foreach ($pair in $chunk) {
        $codeSql  = ConvertTo-SqlString $pair.Key
        $valueSql = ConvertTo-SqlString $pair.Value
        [void]$sb.AppendLine("UPDATE app_catalog_items SET barcode_value = $valueSql WHERE code = $codeSql AND (barcode_value IS NULL OR barcode_value = '');")
    }
    $i += $BatchSize
}

[void]$sb.AppendLine("")
[void]$sb.AppendLine("-- DONE")

[System.IO.File]::WriteAllText($SqlOutputPath, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
$sqlSize = (Get-Item $SqlOutputPath).Length
Write-Host "Сгенерирован SQL: $SqlOutputPath ($([Math]::Round($sqlSize / 1KB, 1)) KB)"

if ($RunMysql) {
    if ([string]::IsNullOrWhiteSpace($MysqlUser)) {
        throw "RunMysql=true, но MysqlUser не указан"
    }
    $mysqlExe = "C:\laragon\bin\mysql\mysql-8.0.30-winx64\bin\mysql.exe"
    if (-not (Test-Path $mysqlExe)) {
        $found = Get-Command mysql.exe -ErrorAction SilentlyContinue
        if ($found) { $mysqlExe = $found.Source } else { $mysqlExe = $null }
    }
    if (-not $mysqlExe) {
        throw "mysql.exe не найден. Укажи путь руками или запусти SQL вручную."
    }
    Write-Host "Запускаю mysql.exe → ${MysqlHost}:${MysqlPort}/$MysqlDatabase"
    & $mysqlExe `
        -h $MysqlHost `
        -P $MysqlPort `
        -u $MysqlUser `
        "-p$MysqlPassword" `
        $MysqlDatabase `
        --default-character-set=utf8mb4 `
        -e "SOURCE $SqlOutputPath"
    if ($LASTEXITCODE -ne 0) {
        throw "mysql.exe вернул код $LASTEXITCODE"
    }
    Write-Host "Импорт выполнен"
} else {
    Write-Host ""
    Write-Host "Чтобы залить в прод:"
    Write-Host "  mysql -h 147.45.108.97 -u <user> -p warehouse_major < $SqlOutputPath"
    Write-Host "Или повторно с -RunMysql:"
    Write-Host "  pwsh ./scripts/Import-UnfBarcodesToMySql.ps1 -RunMysql -MysqlUser <user> -MysqlPassword '<pwd>'"
}
