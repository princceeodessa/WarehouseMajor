param(
    [string]$InputPath = "C:\blagodar\1c-migration\exports\unf-products-first-pass.csv",
    [string]$CatalogSnapshotPath = "C:\blagodar\WarehouseAutomatisaion\app_data\catalog-workspace.json",
    [string]$OutputPath = "C:\blagodar\WarehouseAutomatisaion\app_data\catalog-workspace.json",
    [string]$CurrentOperator = "Ворожцов Стас",
    [string]$DefaultWarehouse = "Главный склад",
    [string]$DefaultCurrency = "RUB",
    [string]$DefaultUnit = "шт",
    [switch]$IncludeMarked
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $InputPath)) {
    throw "1C products export not found: $InputPath"
}

if (-not (Test-Path $CatalogSnapshotPath)) {
    throw "Catalog snapshot not found: $CatalogSnapshotPath"
}

function New-DeterministicGuid {
    param([Parameter(Mandatory = $true)][string]$Seed)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Seed)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($bytes)
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

function Get-StableNumericCode {
    param([string[]]$Parts)

    $seed = ($Parts | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() }) -join "|"
    if ([string]::IsNullOrWhiteSpace($seed)) {
        $seed = "ITEM"
    }

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($seed)
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($bytes)
    }
    finally {
        $sha.Dispose()
    }
    $digits = -join ($hash | ForEach-Object { ([int]$_ % 1000).ToString("000", [System.Globalization.CultureInfo]::InvariantCulture) })
    return $digits.Substring(0, 13)
}

function Normalize-CodeComparable {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ""
    }

    $normalized = -join ($Value.Trim().ToCharArray() | Where-Object { [char]::IsLetterOrDigit($_) })
    return $normalized.ToUpperInvariant()
}

function Get-FirstBarcode {
    param($Row)

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($Row.RegisterBarcodes)) {
        $candidates += $Row.RegisterBarcodes -split ";"
    }

    if (-not [string]::IsNullOrWhiteSpace($Row.CardBarcode)) {
        $candidates += $Row.CardBarcode
    }

    $itemCodeComparable = Normalize-CodeComparable $Row.ItemCode
    foreach ($candidate in $candidates) {
        $value = [string]$candidate
        $value = $value.Trim()
        if ([string]::IsNullOrWhiteSpace($value)) {
            continue
        }

        if ((Normalize-CodeComparable $value) -eq $itemCodeComparable) {
            continue
        }

        return $value
    }

    return ""
}

function Get-Notes {
    param($Row)

    $notes = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($Row.SecondaryCode) -and $Row.SecondaryCode.Trim() -ne "-") {
        $notes.Add("Артикул 1С: $($Row.SecondaryCode.Trim())")
    }

    if (-not [string]::IsNullOrWhiteSpace($Row.BarcodeSource)) {
        $notes.Add("Источник штрихкода: $($Row.BarcodeSource.Trim())")
    }

    return ($notes -join "`r`n")
}

$existing = Get-Content -Path $CatalogSnapshotPath -Raw -Encoding UTF8 | ConvertFrom-Json
$rows = Import-Csv -Path $InputPath

$items = New-Object System.Collections.Generic.List[object]
$skippedMarked = 0
$withRealBarcode = 0
$withGeneratedBarcode = 0

foreach ($row in $rows) {
    if (-not $IncludeMarked -and $row.IsMarked -eq "1") {
        $skippedMarked++
        continue
    }

    $name = [string]$row.ItemName
    $name = $name.Trim()
    if ([string]::IsNullOrWhiteSpace($name)) {
        continue
    }

    $code = [string]$row.ItemCode
    $code = $code.Trim()
    if ([string]::IsNullOrWhiteSpace($code)) {
        $code = [string]$row.ItemRef
        $code = $code.Trim()
    }

    $category = [string]$row.ParentName
    $category = $category.Trim()
    if ([string]::IsNullOrWhiteSpace($category)) {
        $category = "Без группы"
    }

    $id = New-DeterministicGuid "1c-unf-catalog-item|$($row.ItemRef)|$code|$name"
    $barcode = Get-FirstBarcode $row
    if ([string]::IsNullOrWhiteSpace($barcode)) {
        $barcode = Get-StableNumericCode @($code, $name, $DefaultWarehouse, $id.ToString("N"))
        $withGeneratedBarcode++
    }
    else {
        $withRealBarcode++
    }

    $qrPayload = @(
        "Код: $code",
        "Товар: $name",
        "Категория: $category",
        "Склад: $DefaultWarehouse",
        "Цена: 0,00 $DefaultCurrency",
        "Штрихкод: $barcode"
    ) -join "`r`n"

    $items.Add([PSCustomObject]@{
        Id               = $id.ToString()
        Code             = $code
        Name             = $name
        Unit             = $DefaultUnit
        Category         = $category
        Supplier         = ""
        DefaultWarehouse = $DefaultWarehouse
        Status           = "Активна"
        CurrencyCode     = $DefaultCurrency
        DefaultPrice     = 0
        BarcodeValue     = $barcode
        BarcodeFormat    = "Code128"
        QrPayload        = $qrPayload
        Notes            = Get-Notes $row
        SourceLabel      = "1C УНФ / номенклатура"
    })
}

$snapshot = [PSCustomObject]@{
    CurrentOperator    = if ([string]::IsNullOrWhiteSpace($existing.CurrentOperator)) { $CurrentOperator } else { $existing.CurrentOperator }
    Items              = @($items | Sort-Object Name, Code)
    PriceTypes         = @($existing.PriceTypes)
    Discounts          = @($existing.Discounts)
    PriceRegistrations = @($existing.PriceRegistrations)
    OperationLog       = @(
        [PSCustomObject]@{
            Id           = [Guid]::NewGuid().ToString()
            LoggedAt     = (Get-Date).ToString("O")
            Actor        = $CurrentOperator
            EntityType   = "Номенклатура"
            EntityId     = [Guid]::Empty.ToString()
            EntityNumber = "1C-UNF"
            Action       = "Импорт номенклатуры"
            Result       = "Успешно"
            Message      = "Импортировано $($items.Count) позиций из 1С УНФ. Реальных штрихкодов: $withRealBarcode. Сгенерировано штрихкодов: $withGeneratedBarcode."
        }
    ) + @($existing.OperationLog)
    Currencies         = @($DefaultCurrency)
    Warehouses         = @($DefaultWarehouse)
}

$directory = Split-Path -Path $OutputPath -Parent
if (-not [string]::IsNullOrWhiteSpace($directory) -and -not (Test-Path $directory)) {
    New-Item -ItemType Directory -Path $directory | Out-Null
}

if ((Test-Path $OutputPath) -and ($OutputPath -eq $CatalogSnapshotPath)) {
    $backupPath = "$OutputPath.backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Copy-Item -Path $OutputPath -Destination $backupPath -Force
}

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$json = $snapshot | ConvertTo-Json -Depth 12
[System.IO.File]::WriteAllText($OutputPath, $json, $utf8NoBom)

[PSCustomObject]@{
    OutputPath           = $OutputPath
    ImportedItems        = $items.Count
    SkippedMarkedItems   = $skippedMarked
    RealBarcodes         = $withRealBarcode
    GeneratedBarcodes    = $withGeneratedBarcode
    UniqueCategories     = (@($items | Select-Object -ExpandProperty Category -Unique)).Count
}
