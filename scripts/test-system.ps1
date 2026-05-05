param(
    [switch]$SkipWpfSmoke,
    [switch]$FailOnDataIssues
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'WarehouseAutomatisaion.sln'

function Invoke-CheckedCommand {
    param(
        [string]$Name,
        [scriptblock]$Command
    )

    Write-Output "SYSTEM_CHECK_STEP=$Name"
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "Step failed: $Name"
    }
}

function Read-JsonFile {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $null
    }

    return Get-Content -Raw -Encoding UTF8 -LiteralPath $Path | ConvertFrom-Json
}

function Get-ArrayCount {
    param($Value)

    if ($null -eq $Value) {
        return 0
    }

    return @($Value).Count
}

function Get-Text {
    param($Value)

    if ($null -eq $Value) {
        return ''
    }

    return ([string]$Value).Trim()
}

function Get-WarehouseName {
    param($Value)

    if ($null -eq $Value) {
        return ''
    }

    if ($Value -is [string]) {
        return $Value.Trim()
    }

    return (Get-Text $Value.Name)
}

function Count-DuplicateValues {
    param(
        [array]$Items,
        [string]$Property
    )

    return @(
        $Items |
            Where-Object { -not [string]::IsNullOrWhiteSpace((Get-Text $_.$Property)) } |
            Group-Object -Property $Property |
            Where-Object Count -gt 1
    ).Count
}

function Count-EmptyValues {
    param(
        [array]$Items,
        [string]$Property
    )

    return @($Items | Where-Object { [string]::IsNullOrWhiteSpace((Get-Text $_.$Property)) }).Count
}

function Test-DataIntegrity {
    param([string]$WorkspaceRoot)

    $catalog = Read-JsonFile (Join-Path $WorkspaceRoot 'app_data\catalog-workspace.json')
    $sales = Read-JsonFile (Join-Path $WorkspaceRoot 'app_data\sales-workspace.json')
    $purchase = Read-JsonFile (Join-Path $WorkspaceRoot 'app_data\purchasing-workspace.json')
    $warehouse = Read-JsonFile (Join-Path $WorkspaceRoot 'app_data\warehouse-workspace.json')

    $issues = New-Object System.Collections.Generic.List[object]

    if ($catalog -ne $null) {
        $items = @($catalog.Items)
        $warehouseNames = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
        @($catalog.Warehouses) | ForEach-Object {
            $name = Get-WarehouseName $_
            if (-not [string]::IsNullOrWhiteSpace($name)) {
                [void]$warehouseNames.Add($name)
            }
        }

        $checks = [ordered]@{
            CatalogDuplicateIds = Count-DuplicateValues $items 'Id'
            CatalogDuplicateCodes = Count-DuplicateValues $items 'Code'
            CatalogEmptyCode = Count-EmptyValues $items 'Code'
            CatalogEmptyName = Count-EmptyValues $items 'Name'
            CatalogZeroOrMissingPrice = @($items | Where-Object { $null -eq $_.DefaultPrice -or [decimal]$_.DefaultPrice -le 0 }).Count
            CatalogMissingSupplier = Count-EmptyValues $items 'Supplier'
            CatalogUnknownWarehouse = @($items | Where-Object {
                $name = Get-Text $_.DefaultWarehouse
                -not [string]::IsNullOrWhiteSpace($name) -and -not $warehouseNames.Contains($name)
            }).Count
        }

        foreach ($item in $checks.GetEnumerator()) {
            $severity = if ($item.Key -in @('CatalogDuplicateIds', 'CatalogDuplicateCodes', 'CatalogEmptyCode', 'CatalogEmptyName', 'CatalogUnknownWarehouse')) { 'CRITICAL' } elseif ($item.Key -eq 'CatalogMissingSupplier') { 'INFO' } else { 'WARNING' }
            $issues.Add([pscustomobject]@{ Key = $item.Key; Count = $item.Value; Severity = $severity })
        }
    }

    if ($sales -ne $null) {
        $customers = @($sales.Customers)
        $orders = @($sales.Orders)
        $invoices = @($sales.Invoices)
        $customerIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
        $orderIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

        $customers | ForEach-Object { if (-not [string]::IsNullOrWhiteSpace((Get-Text $_.Id))) { [void]$customerIds.Add((Get-Text $_.Id)) } }
        $orders | ForEach-Object { if (-not [string]::IsNullOrWhiteSpace((Get-Text $_.Id))) { [void]$orderIds.Add((Get-Text $_.Id)) } }

        $salesChecks = [ordered]@{
            CustomerDuplicateIds = Count-DuplicateValues $customers 'Id'
            CustomerDuplicateCodes = Count-DuplicateValues $customers 'Code'
            CustomerEmptyName = Count-EmptyValues $customers 'Name'
            OrderDuplicateIds = Count-DuplicateValues $orders 'Id'
            OrderDuplicateNumbers = Count-DuplicateValues $orders 'Number'
            OrdersBrokenCustomerRef = @($orders | Where-Object { -not $customerIds.Contains((Get-Text $_.CustomerId)) }).Count
            OrdersWithoutLines = @($orders | Where-Object { (Get-ArrayCount $_.Lines) -eq 0 }).Count
            OrdersZeroTotal = @($orders | Where-Object { $null -eq $_.TotalAmount -or [decimal]$_.TotalAmount -eq 0 }).Count
            InvoiceDuplicateIds = Count-DuplicateValues $invoices 'Id'
            InvoiceDuplicateNumbers = Count-DuplicateValues $invoices 'Number'
            InvoicesBrokenOrderRef = @($invoices | Where-Object { -not $orderIds.Contains((Get-Text $_.SalesOrderId)) }).Count
            InvoicesBrokenCustomerRef = @($invoices | Where-Object { -not $customerIds.Contains((Get-Text $_.CustomerId)) }).Count
            InvoicesWithoutLines = @($invoices | Where-Object { (Get-ArrayCount $_.Lines) -eq 0 }).Count
        }

        foreach ($item in $salesChecks.GetEnumerator()) {
            $severity = if ($item.Key -match 'Duplicate|EmptyName|Broken') { 'CRITICAL' } else { 'WARNING' }
            $issues.Add([pscustomobject]@{ Key = $item.Key; Count = $item.Value; Severity = $severity })
        }
    }

    if ($purchase -ne $null) {
        $suppliers = @($purchase.Suppliers)
        $purchaseOrders = @($purchase.PurchaseOrders)
        $supplierIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)
        $suppliers | ForEach-Object { if (-not [string]::IsNullOrWhiteSpace((Get-Text $_.Id))) { [void]$supplierIds.Add((Get-Text $_.Id)) } }

        $issues.Add([pscustomobject]@{ Key = 'PurchaseOrdersBrokenSupplierRef'; Count = @($purchaseOrders | Where-Object { -not $supplierIds.Contains((Get-Text $_.SupplierId)) }).Count; Severity = 'CRITICAL' })
        $issues.Add([pscustomobject]@{ Key = 'PurchaseOrdersWithoutLines'; Count = @($purchaseOrders | Where-Object { (Get-ArrayCount $_.Lines) -eq 0 }).Count; Severity = 'WARNING' })
    }

    if ($warehouse -ne $null) {
        $cells = @($warehouse.StorageCells)
        $issues.Add([pscustomobject]@{ Key = 'StorageCellDuplicateIds'; Count = Count-DuplicateValues $cells 'Id'; Severity = 'CRITICAL' })
        $issues.Add([pscustomobject]@{ Key = 'StorageCellDuplicateCodes'; Count = Count-DuplicateValues $cells 'Code'; Severity = 'CRITICAL' })
    }

    foreach ($issue in $issues | Where-Object Count -gt 0 | Sort-Object Severity,Key) {
        Write-Output ("DATA_INTEGRITY_{0}={1};SEVERITY={2}" -f $issue.Key,$issue.Count,$issue.Severity)
    }

    $critical = @($issues | Where-Object { $_.Severity -eq 'CRITICAL' -and $_.Count -gt 0 }).Count
    $warnings = @($issues | Where-Object { $_.Severity -eq 'WARNING' -and $_.Count -gt 0 }).Count
    $info = @($issues | Where-Object { $_.Severity -eq 'INFO' -and $_.Count -gt 0 }).Count

    Write-Output "DATA_INTEGRITY_CRITICAL_GROUPS=$critical"
    Write-Output "DATA_INTEGRITY_WARNING_GROUPS=$warnings"
    Write-Output "DATA_INTEGRITY_INFO_GROUPS=$info"

    if ($critical -gt 0 -or ($FailOnDataIssues -and ($warnings -gt 0 -or $info -gt 0))) {
        throw 'Data integrity check failed.'
    }
}

Invoke-CheckedCommand 'build' { dotnet build $solution }
Invoke-CheckedCommand 'test' { dotnet test $solution --no-build }
Invoke-CheckedCommand 'nuget-vulnerable' { dotnet list $solution package --vulnerable --include-transitive }

if (-not $SkipWpfSmoke) {
    $smoke = Join-Path $PSScriptRoot 'run-wpf-smoke.ps1'
    Invoke-CheckedCommand 'wpf-smoke' { powershell -ExecutionPolicy Bypass -File $smoke -TimeoutSeconds 60 }
}

Test-DataIntegrity -WorkspaceRoot $root
Write-Output 'SYSTEM_CHECK_OK=True'
