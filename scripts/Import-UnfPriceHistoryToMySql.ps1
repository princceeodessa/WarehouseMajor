#requires -Version 5.1
<#
.SYNOPSIS
    Импортирует историю цен товаров из 1С УНФ (unf-prices-sql.csv) в MySQL-таблицу app_product_price_history.

.DESCRIPTION
    Читает CSV с колонками Period, ItemCode, PriceType, Price, CurrencyName, UnitName (~82k записей).
    Генерирует SQL-файл с пакетными INSERT в app_product_price_history (UPSERT по item_code+price_type+period).

.EXAMPLE
    pwsh ./scripts/Import-UnfPriceHistoryToMySql.ps1
    pwsh ./scripts/Import-UnfPriceHistoryToMySql.ps1 -RunMysql -MysqlUser app_user -MysqlPassword '****'
#>
param(
    [string]$PricesCsvPath = "C:\blagodar\1c-migration\exports\unf-prices-sql.csv",
    [string]$SqlOutputPath = "C:\blagodar\WarehouseAutomatisaion\_temp_build\seed-product-price-history.sql",
    [int]   $BatchSize     = 1000,
    [switch]$RunMysql,
    [string]$MysqlHost     = "147.45.108.97",
    [int]   $MysqlPort     = 3306,
    [string]$MysqlDatabase = "warehouse_major",
    [string]$MysqlUser     = "",
    [string]$MysqlPassword = ""
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $PricesCsvPath)) {
    throw "CSV не найден: $PricesCsvPath"
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

function ConvertTo-SqlDecimal {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return "0" }
    $normalized = $Value.Replace(",", ".").Replace(" ", "").Trim()
    [decimal]$d = 0
    if ([decimal]::TryParse($normalized, [Globalization.NumberStyles]::Any, [Globalization.CultureInfo]::InvariantCulture, [ref]$d)) {
        return $d.ToString([Globalization.CultureInfo]::InvariantCulture)
    }
    return "0"
}

function ConvertTo-SqlDateTime {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return "NULL" }
    # 1С формат: "yyyy-MM-dd HH:mm:ss" (год в CSV сдвинут — но мы храним как есть, это историческая метка)
    [datetime]$dt = [datetime]::MinValue
    if ([datetime]::TryParse($Value.Trim(), [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$dt)) {
        return "'" + $dt.ToString("yyyy-MM-dd HH:mm:ss") + "'"
    }
    return "NULL"
}

function Normalize-Currency {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return "RUB" }
    $v = $Value.Trim().TrimEnd('.').ToLowerInvariant()
    if ($v -eq "руб" -or $v -eq "rub" -or $v -eq "ruble") { return "RUB" }
    if ($v -eq "usd")  { return "USD" }
    if ($v -eq "eur")  { return "EUR" }
    return $Value.Trim().ToUpperInvariant().Substring(0, [Math]::Min(8, $Value.Trim().Length))
}

Write-Host "Читаю CSV: $PricesCsvPath"
$rows = Import-Csv -Path $PricesCsvPath -Encoding UTF8 -Delimiter ','
Write-Host "Прочитано строк: $($rows.Count)"

# Дедупликация по (item_code, price_type, period) — оставляем последнее значение в файле.
$dedup = New-Object 'System.Collections.Generic.Dictionary[string,object]'
$skipped = 0
foreach ($row in $rows) {
    $itemCode = ([string]$row.ItemCode).Trim()
    $priceType = ([string]$row.PriceType).Trim()
    $period = ([string]$row.Period).Trim()
    if ([string]::IsNullOrWhiteSpace($itemCode) -or [string]::IsNullOrWhiteSpace($priceType) -or [string]::IsNullOrWhiteSpace($period)) {
        $skipped++
        continue
    }
    $key = "$itemCode|$priceType|$period"
    $dedup[$key] = [pscustomobject]@{
        Period       = $period
        ItemCode     = $itemCode
        PriceType    = $priceType
        Price        = ([string]$row.Price).Trim()
        CurrencyName = ([string]$row.CurrencyName).Trim()
        UnitName     = ([string]$row.UnitName).Trim()
    }
}

$entries = @($dedup.Values)
Write-Host "Уникальных записей: $($entries.Count) (пропущено невалидных: $skipped)"

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine("-- Импорт истории цен товаров из 1С УНФ (unf-prices-sql.csv)")
[void]$sb.AppendLine("-- Сгенерировано: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')")
[void]$sb.AppendLine("-- Записей: $($entries.Count)")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("SET NAMES utf8mb4;")
[void]$sb.AppendLine("")
[void]$sb.AppendLine("CREATE TABLE IF NOT EXISTS app_product_price_history (")
[void]$sb.AppendLine("    id BIGINT NOT NULL AUTO_INCREMENT,")
[void]$sb.AppendLine("    period DATETIME(6) NOT NULL,")
[void]$sb.AppendLine("    item_code VARCHAR(128) NOT NULL,")
[void]$sb.AppendLine("    price_type VARCHAR(128) NOT NULL,")
[void]$sb.AppendLine("    price_value DECIMAL(18, 4) NOT NULL DEFAULT 0,")
[void]$sb.AppendLine("    currency_code VARCHAR(16) NOT NULL DEFAULT 'RUB',")
[void]$sb.AppendLine("    unit_name VARCHAR(64) NULL,")
[void]$sb.AppendLine("    created_at_utc DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),")
[void]$sb.AppendLine("    CONSTRAINT pk_app_product_price_history PRIMARY KEY (id),")
[void]$sb.AppendLine("    CONSTRAINT uq_app_product_price_history UNIQUE KEY (item_code, price_type, period),")
[void]$sb.AppendLine("    INDEX ix_app_product_price_history_item_code (item_code),")
[void]$sb.AppendLine("    INDEX ix_app_product_price_history_period (period)")
[void]$sb.AppendLine(") ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;")
[void]$sb.AppendLine("")

$i = 0
$batchNo = 0
while ($i -lt $entries.Count) {
    $end = [Math]::Min($i + $BatchSize - 1, $entries.Count - 1)
    $chunk = $entries[$i..$end]
    $batchNo++
    [void]$sb.AppendLine("-- Batch $batchNo")
    [void]$sb.AppendLine("INSERT INTO app_product_price_history (period, item_code, price_type, price_value, currency_code, unit_name) VALUES")
    $lines = @()
    foreach ($entry in $chunk) {
        $periodSql   = ConvertTo-SqlDateTime $entry.Period
        $codeSql     = ConvertTo-SqlString $entry.ItemCode
        $priceTypeSql= ConvertTo-SqlString $entry.PriceType
        $priceVal    = ConvertTo-SqlDecimal $entry.Price
        $currency    = Normalize-Currency $entry.CurrencyName
        $currencySql = ConvertTo-SqlString $currency
        $unitSql     = ConvertTo-SqlString $entry.UnitName
        $lines += "    ($periodSql, $codeSql, $priceTypeSql, $priceVal, $currencySql, $unitSql)"
    }
    [void]$sb.AppendLine(($lines -join ",`n"))
    [void]$sb.AppendLine("ON DUPLICATE KEY UPDATE")
    [void]$sb.AppendLine("    price_value = VALUES(price_value),")
    [void]$sb.AppendLine("    currency_code = VALUES(currency_code),")
    [void]$sb.AppendLine("    unit_name = VALUES(unit_name);")
    [void]$sb.AppendLine("")
    $i += $BatchSize
}

[void]$sb.AppendLine("-- DONE: $($entries.Count) записей, $batchNo пакетов по $BatchSize")

[System.IO.File]::WriteAllText($SqlOutputPath, $sb.ToString(), [System.Text.UTF8Encoding]::new($false))
$sqlSize = (Get-Item $SqlOutputPath).Length
Write-Host "Сгенерирован SQL: $SqlOutputPath ($([Math]::Round($sqlSize / 1MB, 2)) MB)"

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
    Write-Host "Или повторно с -RunMysql"
}
