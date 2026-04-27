param(
    [string]$CatalogSnapshotPath = "C:\blagodar\WarehouseAutomatisaion\app_data\catalog-workspace.json",
    [string]$PricesCsvPath = "C:\blagodar\1c-migration\exports\unf-prices-sql.csv",
    [string]$StockTsvPath = "C:\blagodar\1c-migration\exports\unf-stock-balances.tsv",
    [string]$SqlOutputPath = "C:\blagodar\WarehouseAutomatisaion\_temp_build\seed-operational-from-1c.sql",
    [string]$Operator = "Ворожцов Стас"
)

$ErrorActionPreference = "Stop"
$ru = [System.Globalization.CultureInfo]::GetCultureInfo("ru-RU")

function New-DeterministicGuid {
    param([Parameter(Mandatory = $true)][string]$Seed)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($Seed))
    }
    finally {
        $sha.Dispose()
    }

    $buffer = New-Object byte[] 16
    [Array]::Copy($hash, 0, $buffer, 0, 16)
    $buffer[7] = [byte](($buffer[7] -band 0x0F) -bor 0x40)
    $buffer[8] = [byte](($buffer[8] -band 0x3F) -bor 0x80)
    return [Guid]::new($buffer)
}

function Get-Code {
    param([string]$Prefix, [string]$Value)

    $raw = if ([string]::IsNullOrWhiteSpace($Value)) { "EMPTY" } else { $Value.Trim() }
    $normalized = -join ($raw.ToCharArray() | ForEach-Object {
        if ([char]::IsLetterOrDigit($_)) { [char]::ToUpperInvariant($_) } else { "-" }
    })
    $normalized = ($normalized -replace "-+", "-").Trim("-")
    if ([string]::IsNullOrWhiteSpace($normalized)) {
        $normalized = (New-DeterministicGuid $raw).ToString("N").Substring(0, 8).ToUpperInvariant()
    }
    $code = "$Prefix-$normalized"
    return $code.Substring(0, [Math]::Min(64, $code.Length))
}

function Convert-PriceTypeScore {
    param([string]$PriceType)

    if ($PriceType -eq "Розничная цена УДМ") { return 100 }
    if ($PriceType -match "Рознич") { return 90 }
    if ($PriceType -match "Оптов") { return 80 }
    if ($PriceType -match "Учет") { return 70 }
    if ($PriceType -match "Закуп") { return 60 }
    if ($PriceType -match "Дилер") { return 50 }
    if ($PriceType -match "Миним") { return 40 }
    return 10
}

function Convert-Decimal {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return [decimal]0
    }

    $normalized = $Value.Replace([char]0x00A0, " ").Replace(" ", "")
    [decimal]$result = 0
    if ([decimal]::TryParse($normalized, [System.Globalization.NumberStyles]::Any, $ru, [ref]$result)) {
        return $result
    }

    if ([decimal]::TryParse($normalized, [System.Globalization.NumberStyles]::Any, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$result)) {
        return $result
    }

    return [decimal]0
}

function Convert-Date {
    param([string]$Value)

    [datetime]$date = [datetime]::MinValue
    if ([datetime]::TryParse($Value, $ru, [System.Globalization.DateTimeStyles]::AssumeLocal, [ref]$date)) {
        if ($date.Year -gt 3000) {
            $date = $date.AddYears(-2000)
        }
        return $date
    }

    return [datetime]::MinValue
}

function Sql {
    param([AllowNull()][string]$Value)

    if ($null -eq $Value) {
        return "NULL"
    }

    return "'" + $Value.Replace("\", "\\").Replace("'", "''") + "'"
}

function SqlDate {
    param([datetime]$Value)
    return "'" + $Value.ToString("yyyy-MM-dd HH:mm:ss", [System.Globalization.CultureInfo]::InvariantCulture) + "'"
}

function Truncate {
    param([string]$Value, [int]$Length)
    $text = if ($null -eq $Value) { "" } else { $Value.Trim() }
    return $text.Substring(0, [Math]::Min($Length, $text.Length))
}

if (-not (Test-Path $CatalogSnapshotPath)) {
    throw "Catalog snapshot not found: $CatalogSnapshotPath"
}
if (-not (Test-Path $PricesCsvPath)) {
    throw "Prices export not found: $PricesCsvPath"
}
if (-not (Test-Path $StockTsvPath)) {
    throw "Stock export not found: $StockTsvPath"
}

$catalog = [System.IO.File]::ReadAllText($CatalogSnapshotPath, [System.Text.Encoding]::UTF8) | ConvertFrom-Json
$items = @($catalog.Items)
$itemsByCode = @{}
foreach ($item in $items) {
    if (-not [string]::IsNullOrWhiteSpace($item.Code)) {
        $itemsByCode[$item.Code.Trim()] = $item
    }
}

$priceRows = Import-Csv -Path $PricesCsvPath
$bestPriceByItem = @{}
foreach ($row in $priceRows) {
    $code = [string]$row.ItemCode
    if ([string]::IsNullOrWhiteSpace($code) -or -not $itemsByCode.ContainsKey($code.Trim())) {
        continue
    }

    $price = Convert-Decimal $row.Price
    if ($price -le 0) {
        continue
    }

    $candidate = [PSCustomObject]@{
        ItemCode   = $code.Trim()
        Price      = $price
        PriceType  = ([string]$row.PriceType).Trim()
        UnitName   = ([string]$row.UnitName).Trim()
        Currency   = if (([string]$row.CurrencyName) -match "руб|RUB|₽") { "RUB" } else { ([string]$row.CurrencyName).Trim() }
        Period     = Convert-Date $row.Period
        TypeScore  = Convert-PriceTypeScore ([string]$row.PriceType)
    }

    if (-not $bestPriceByItem.ContainsKey($candidate.ItemCode)) {
        $bestPriceByItem[$candidate.ItemCode] = $candidate
        continue
    }

    $current = $bestPriceByItem[$candidate.ItemCode]
    if ($candidate.TypeScore -gt $current.TypeScore -or
        ($candidate.TypeScore -eq $current.TypeScore -and $candidate.Period -gt $current.Period)) {
        $bestPriceByItem[$candidate.ItemCode] = $candidate
    }
}

$updatedPrices = 0
foreach ($entry in $bestPriceByItem.GetEnumerator()) {
    $item = $itemsByCode[$entry.Key]
    $price = $entry.Value
    $item.DefaultPrice = $price.Price
    if (-not [string]::IsNullOrWhiteSpace($price.UnitName)) {
        $item.Unit = $price.UnitName
    }
    $item.CurrencyCode = if ([string]::IsNullOrWhiteSpace($price.Currency)) { "RUB" } else { $price.Currency }
    $item.QrPayload = @(
        "Код: $($item.Code)",
        "Товар: $($item.Name)",
        "Категория: $($item.Category)",
        "Склад: $($item.DefaultWarehouse)",
        "Цена: $($item.DefaultPrice.ToString("N2", $ru)) $($item.CurrencyCode)",
        "Штрихкод: $($item.BarcodeValue)"
    ) -join "`r`n"

    $sourceLine = "Вид цены 1С: $($price.PriceType)"
    if ([string]::IsNullOrWhiteSpace($item.Notes)) {
        $item.Notes = $sourceLine
    }
    elseif ($item.Notes -notmatch [regex]::Escape("Вид цены 1С:")) {
        $item.Notes = "$($item.Notes)`r`n$sourceLine"
    }
    $updatedPrices++
}

$priceTypes = $priceRows |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_.PriceType) } |
    Group-Object PriceType |
    ForEach-Object {
        $name = $_.Name.Trim()
        [PSCustomObject]@{
            Id = (New-DeterministicGuid "1c-price-type|$name").ToString()
            Code = Get-Code "PT" $name
            Name = $name
            CurrencyCode = "RUB"
            BasePriceTypeName = ""
            RoundingRule = "Без округления"
            IsDefault = ($name -eq "Розничная цена УДМ")
            IsManualEntryOnly = $false
            UsesPsychologicalRounding = $false
            Status = "Рабочий"
        }
    } |
    Sort-Object Name

if (-not ($priceTypes | Where-Object { $_.IsDefault } | Select-Object -First 1)) {
    $default = $priceTypes | Where-Object { $_.Name -match "Рознич" } | Select-Object -First 1
    if ($default) {
        $default.IsDefault = $true
    }
}

$registrationLines = $bestPriceByItem.GetEnumerator() |
    Sort-Object Key |
    ForEach-Object {
        $sourceItem = $itemsByCode[$_.Key]
        [PSCustomObject]@{
            Id = (New-DeterministicGuid "1c-price-line|$($_.Key)").ToString()
            ItemCode = $sourceItem.Code
            ItemName = $sourceItem.Name
            Unit = $sourceItem.Unit
            PreviousPrice = 0
            NewPrice = $_.Value.Price
        }
    }

$catalog.PriceTypes = @($priceTypes)
$catalog.PriceRegistrations = @(
    [PSCustomObject]@{
        Id = (New-DeterministicGuid "1c-price-registration|latest").ToString()
        Number = "1C-PRICE-IMPORT"
        DocumentDate = (Get-Date).ToString("O")
        PriceTypeName = (($priceTypes | Where-Object { $_.IsDefault } | Select-Object -First 1).Name)
        CurrencyCode = "RUB"
        Status = "Проведен"
        Comment = "Импорт актуальных цен из 1С УНФ"
        Lines = @($registrationLines)
    }
) + @($catalog.PriceRegistrations)
$catalog.OperationLog = @(
    [PSCustomObject]@{
        Id = [Guid]::NewGuid().ToString()
        LoggedAt = (Get-Date).ToString("O")
        Actor = $Operator
        EntityType = "Номенклатура"
        EntityId = [Guid]::Empty.ToString()
        EntityNumber = "1C-UNF"
        Action = "Импорт цен"
        Result = "Успешно"
        Message = "Обновлены цены для $updatedPrices товаров. Загружено видов цен: $(@($priceTypes).Count)."
    }
) + @($catalog.OperationLog)

$backupPath = "$CatalogSnapshotPath.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
Copy-Item -Path $CatalogSnapshotPath -Destination $backupPath -Force
[System.IO.File]::WriteAllText(
    $CatalogSnapshotPath,
    ($catalog | ConvertTo-Json -Depth 16),
    [System.Text.UTF8Encoding]::new($false))

$stockRowsRaw = Import-Csv -Path $StockTsvPath -Delimiter "`t" -Encoding Unicode
$stockRows = $stockRowsRaw |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_.ItemCode) -and $itemsByCode.ContainsKey($_.ItemCode.Trim()) } |
    Group-Object { "$($_.ItemCode.Trim())|$($_.Warehouse.Trim())" } |
    ForEach-Object {
        $first = $_.Group[0]
        [PSCustomObject]@{
            ItemCode = $first.ItemCode.Trim()
            Warehouse = if ([string]::IsNullOrWhiteSpace($first.Warehouse)) { "Главный склад" } else { $first.Warehouse.Trim() }
            UnitName = if ([string]::IsNullOrWhiteSpace($first.UnitName)) { $itemsByCode[$first.ItemCode.Trim()].Unit } else { $first.UnitName.Trim() }
            Quantity = ($_.Group | ForEach-Object { Convert-Decimal $_.Quantity } | Measure-Object -Sum).Sum
            Reserve = ($_.Group | ForEach-Object { Convert-Decimal $_.Reserve } | Measure-Object -Sum).Sum
        }
    }

$allUnits = @($items | ForEach-Object { $_.Unit }) + @($stockRows | ForEach-Object { $_.UnitName }) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Sort-Object -Unique
$allCategories = @($items | ForEach-Object { $_.Category }) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Sort-Object -Unique
$allWarehouses = @($items | ForEach-Object { $_.DefaultWarehouse }) + @($stockRows | ForEach-Object { $_.Warehouse }) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Sort-Object -Unique

$unitByName = @{}
foreach ($name in $allUnits) {
    $unitByName[$name] = [PSCustomObject]@{
        Id = (New-DeterministicGuid "unit|$name").ToString()
        Code = Get-Code "U" $name
        Name = Truncate $name 128
        Symbol = Truncate $name 32
    }
}

$categoryByName = @{}
foreach ($name in $allCategories) {
    $categoryByName[$name] = [PSCustomObject]@{
        Id = (New-DeterministicGuid "category|$name").ToString()
        Code = Get-Code "CAT" $name
        Name = Truncate $name 256
    }
}

$warehouseByName = @{}
foreach ($name in $allWarehouses) {
    $warehouseByName[$name] = [PSCustomObject]@{
        Id = (New-DeterministicGuid "warehouse|$name").ToString()
        Code = Get-Code "WH" $name
        Name = Truncate $name 256
    }
}

$sqlLines = New-Object System.Collections.Generic.List[string]
$sqlLines.Add("SET NAMES utf8mb4;")
$sqlLines.Add("SET FOREIGN_KEY_CHECKS = 0;")
$sqlLines.Add("INSERT INTO organizations (id, code, name, tax_id) VALUES ('03efb773-1ea1-4d63-9172-345dbb3e1c00', '1C-UNF', 'Организация из 1С УНФ', NULL) ON DUPLICATE KEY UPDATE name = VALUES(name);")

foreach ($unit in $unitByName.Values) {
    $sqlLines.Add("INSERT INTO units_of_measure (id, code, name, symbol) VALUES ('$($unit.Id)', $(Sql $unit.Code), $(Sql $unit.Name), $(Sql $unit.Symbol)) ON DUPLICATE KEY UPDATE name = VALUES(name), symbol = VALUES(symbol);")
}

foreach ($category in $categoryByName.Values) {
    $sqlLines.Add("INSERT INTO item_categories (id, parent_id, code, name) VALUES ('$($category.Id)', NULL, $(Sql $category.Code), $(Sql $category.Name)) ON DUPLICATE KEY UPDATE name = VALUES(name);")
}

foreach ($warehouse in $warehouseByName.Values) {
    $sqlLines.Add("INSERT INTO warehouse_nodes (id, parent_id, code, name, type, is_reserve_area) VALUES ('$($warehouse.Id)', NULL, $(Sql $warehouse.Code), $(Sql $warehouse.Name), 1, 0) ON DUPLICATE KEY UPDATE name = VALUES(name), type = VALUES(type), is_reserve_area = VALUES(is_reserve_area);")
}

foreach ($priceType in $priceTypes) {
    $sqlLines.Add("INSERT INTO price_types (id, code, name, currency_code, base_price_type_id, is_manual_entry_only, uses_psychological_rounding) VALUES ('$($priceType.Id)', $(Sql $priceType.Code), $(Sql $priceType.Name), 'RUB', NULL, 0, 0) ON DUPLICATE KEY UPDATE name = VALUES(name), currency_code = VALUES(currency_code);")
}

foreach ($item in $items) {
    $unit = if ($unitByName.ContainsKey($item.Unit)) { $unitByName[$item.Unit].Id } else { "NULL" }
    $category = if ($categoryByName.ContainsKey($item.Category)) { "'" + $categoryByName[$item.Category].Id + "'" } else { "NULL" }
    $warehouse = if ($warehouseByName.ContainsKey($item.DefaultWarehouse)) { "'" + $warehouseByName[$item.DefaultWarehouse].Id + "'" } else { "NULL" }
    $unitSql = if ($unit -eq "NULL") { "NULL" } else { "'$unit'" }
    $sqlLines.Add("INSERT INTO nomenclature_items (id, parent_id, code, sku, name, unit_of_measure_id, category_id, default_supplier_id, default_warehouse_node_id, default_storage_bin_id, price_group_id, item_kind, vat_rate_code, tracks_batches, tracks_serials) VALUES ('$($item.Id)', NULL, $(Sql (Truncate $item.Code 64)), $(Sql (Truncate $item.Code 128)), $(Sql (Truncate $item.Name 256)), $unitSql, $category, NULL, $warehouse, NULL, NULL, 'Товар', NULL, 0, 0) ON DUPLICATE KEY UPDATE name = VALUES(name), unit_of_measure_id = VALUES(unit_of_measure_id), category_id = VALUES(category_id), default_warehouse_node_id = VALUES(default_warehouse_node_id);")
}

$now = Get-Date
foreach ($stock in $stockRows) {
    $item = $itemsByCode[$stock.ItemCode]
    $warehouse = $warehouseByName[$stock.Warehouse]
    $stockId = (New-DeterministicGuid "stock|$($item.Id)|$($warehouse.Id)").ToString()
    $quantity = ([decimal]$stock.Quantity).ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $reserve = ([decimal]$stock.Reserve).ToString([System.Globalization.CultureInfo]::InvariantCulture)
    $sqlLines.Add("INSERT INTO stock_balances (id, item_id, warehouse_node_id, storage_bin_id, batch_id, quantity, reserved_quantity, last_movement_at_utc) VALUES ('$stockId', '$($item.Id)', '$($warehouse.Id)', NULL, NULL, $quantity, $reserve, $(SqlDate $now)) ON DUPLICATE KEY UPDATE quantity = VALUES(quantity), reserved_quantity = VALUES(reserved_quantity), last_movement_at_utc = VALUES(last_movement_at_utc);")
}

$defaultPriceType = $priceTypes | Where-Object { $_.IsDefault } | Select-Object -First 1
if ($defaultPriceType -and $registrationLines.Count -gt 0) {
    $priceDocumentId = (New-DeterministicGuid "price-registration-doc|1c-latest").ToString()
    $sqlLines.Add("INSERT INTO price_registration_documents (id, number, document_date, posting_state, organization_id, author_id, responsible_employee_id, comment_text, base_document_id, project_id) VALUES ('$priceDocumentId', '1C-PRICE-IMPORT', $(SqlDate $now), 1, '03efb773-1ea1-4d63-9172-345dbb3e1c00', NULL, NULL, 'Импорт актуальных цен из 1С УНФ', NULL, NULL) ON DUPLICATE KEY UPDATE document_date = VALUES(document_date), comment_text = VALUES(comment_text);")
    $lineNo = 1
    foreach ($line in $registrationLines) {
        $item = $itemsByCode[$line.ItemCode]
        $unit = if ($unitByName.ContainsKey($line.Unit)) { "'" + $unitByName[$line.Unit].Id + "'" } else { "NULL" }
        $price = ([decimal]$line.NewPrice).ToString([System.Globalization.CultureInfo]::InvariantCulture)
        $sqlLines.Add("INSERT INTO price_registration_lines (id, price_registration_document_id, line_no, item_id, characteristic_id, unit_of_measure_id, price_type_id, new_price, previous_price, currency_code) VALUES ('$($line.Id)', '$priceDocumentId', $lineNo, '$($item.Id)', NULL, $unit, '$($defaultPriceType.Id)', $price, NULL, 'RUB') ON DUPLICATE KEY UPDATE new_price = VALUES(new_price), unit_of_measure_id = VALUES(unit_of_measure_id);")
        $lineNo++
    }
}

$sqlLines.Add("SET FOREIGN_KEY_CHECKS = 1;")

$sqlDirectory = Split-Path -Path $SqlOutputPath -Parent
if (-not [string]::IsNullOrWhiteSpace($sqlDirectory) -and -not (Test-Path $sqlDirectory)) {
    New-Item -ItemType Directory -Path $sqlDirectory | Out-Null
}
[System.IO.File]::WriteAllText($SqlOutputPath, ($sqlLines -join "`n"), [System.Text.UTF8Encoding]::new($false))

[PSCustomObject]@{
    UpdatedCatalogItemsWithPrices = $updatedPrices
    PriceTypes = @($priceTypes).Count
    StockRows = @($stockRows).Count
    Warehouses = @($allWarehouses).Count
    Units = @($allUnits).Count
    Categories = @($allCategories).Count
    BackupPath = $backupPath
    SqlOutputPath = $SqlOutputPath
}
