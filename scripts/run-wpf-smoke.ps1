param(
    [string]$ExePath = "",
    [int]$TimeoutSeconds = 45,
    [switch]$SkipPriceListExport
)

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function ConvertFrom-CodePoints {
    param([int[]]$CodePoints)

    return -join ($CodePoints | ForEach-Object { [char]$_ })
}

$ExpectedActionsName = ConvertFrom-CodePoints @(0x0414, 0x0435, 0x0439, 0x0441, 0x0442, 0x0432, 0x0438, 0x044F, 0x20, 0x0442, 0x043E, 0x0432, 0x0430, 0x0440, 0x043E, 0x0432)
$PriceListMenuName = ConvertFrom-CodePoints @(0x0412, 0x044B, 0x0433, 0x0440, 0x0443, 0x0437, 0x0438, 0x0442, 0x044C, 0x20, 0x043F, 0x0440, 0x0430, 0x0439, 0x0441, 0x2D, 0x043B, 0x0438, 0x0441, 0x0442)
$OkButtonRuName = ConvertFrom-CodePoints @(0x041E, 0x041A)

function Resolve-AppExePath {
    param([string]$Candidate)

    if (-not [string]::IsNullOrWhiteSpace($Candidate)) {
        $resolved = Resolve-Path -Path $Candidate -ErrorAction Stop
        return $resolved.Path
    }

    $root = Split-Path -Parent $PSScriptRoot
    $debugExe = Join-Path $root "WarehouseAutomatisaion.Desktop.Wpf\bin\Debug\net8.0-windows\MajorWarehause.exe"
    $publishExe = Join-Path $root "artifacts\publish\majorwarehause-win-x64\MajorWarehause.exe"

    if (Test-Path -LiteralPath $debugExe) {
        return $debugExe
    }

    if (Test-Path -LiteralPath $publishExe) {
        return $publishExe
    }

    throw "MajorWarehause.exe not found. Build or publish the application first."
}

function Get-RootElementByProcessId {
    param(
        [int]$ProcessId,
        [datetime]$Deadline
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
        $ProcessId
    )

    while ((Get-Date) -lt $Deadline) {
        $element = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
            [System.Windows.Automation.TreeScope]::Children,
            $condition
        )

        if ($null -ne $element) {
            return $element
        }

        Start-Sleep -Milliseconds 250
    }

    throw "Application window was not found by process id $ProcessId."
}

function Find-ElementByAutomationId {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$AutomationId,
        [datetime]$Deadline
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId
    )

    while ((Get-Date) -lt $Deadline) {
        $element = $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)

        if ($null -ne $element) {
            return $element
        }

        Start-Sleep -Milliseconds 200
    }

    throw "UI element with AutomationId '$AutomationId' was not found."
}

function Find-ElementByName {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name,
        [datetime]$Deadline
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name
    )

    while ((Get-Date) -lt $Deadline) {
        $element = $Root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)

        if ($null -ne $element) {
            return $element
        }

        Start-Sleep -Milliseconds 200
    }

    return $null
}

function Find-InvokableElementByName {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name,
        [datetime]$Deadline
    )

    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name
    )

    while ((Get-Date) -lt $Deadline) {
        $elements = $Root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)

        foreach ($element in $elements) {
            if (-not $element.Current.IsEnabled) {
                continue
            }

            $invokePattern = $null
            if ($element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$invokePattern)) {
                return $element
            }
        }

        Start-Sleep -Milliseconds 200
    }

    return $null
}

function Invoke-UiElement {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [datetime]$Deadline = (Get-Date).AddSeconds(5)
    )

    while ((Get-Date) -lt $Deadline) {
        if ($Element.Current.IsEnabled) {
            $invokePattern = $null
            if (-not $Element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$invokePattern)) {
                throw "UI element '$($Element.Current.Name)' does not support InvokePattern."
            }

            $invokePattern.Invoke()
            return
        }

        Start-Sleep -Milliseconds 200
    }

    throw "UI element '$($Element.Current.Name)' is not enabled."
}

function Wait-NewPriceListExport {
    param(
        [string]$ExportsPath,
        [datetime]$StartedAt,
        [datetime]$Deadline
    )

    while ((Get-Date) -lt $Deadline) {
        if (Test-Path -LiteralPath $ExportsPath) {
            $file = Get-ChildItem -LiteralPath $ExportsPath -Filter "price-list-*.csv" -File |
                Where-Object { $_.LastWriteTime -ge $StartedAt } |
                Sort-Object LastWriteTime -Descending |
                Select-Object -First 1

            if ($null -ne $file) {
                return $file
            }
        }

        Start-Sleep -Milliseconds 300
    }

    throw "Price list export was not created in '$ExportsPath'."
}

$exe = Resolve-AppExePath -Candidate $ExePath
$process = $null

try {
    $process = Start-Process -FilePath $exe -PassThru
    $root = Get-RootElementByProcessId -ProcessId $process.Id -Deadline ((Get-Date).AddSeconds($TimeoutSeconds))

    $navigationButtons = @(
        "NavDashboardButton",
        "NavSalesButton",
        "NavCustomersButton",
        "NavInvoicesButton",
        "NavShipmentsButton",
        "NavPurchasingButton",
        "NavWarehouseButton",
        "NavCatalogButton",
        "NavAuditButton",
        "NavModelButton"
    )

    foreach ($automationId in $navigationButtons) {
        $button = Find-ElementByAutomationId -Root $root -AutomationId $automationId -Deadline ((Get-Date).AddSeconds($TimeoutSeconds))
        Invoke-UiElement -Element $button
        Start-Sleep -Milliseconds 250

        if ($process.HasExited) {
            throw "Application exited after clicking '$automationId'."
        }

        $process.Refresh()
        if (-not $process.Responding) {
            throw "Application stopped responding after clicking '$automationId'."
        }
    }

    $catalogButton = Find-ElementByAutomationId -Root $root -AutomationId "NavCatalogButton" -Deadline ((Get-Date).AddSeconds($TimeoutSeconds))
    Invoke-UiElement -Element $catalogButton
    Start-Sleep -Milliseconds 500

    $actionsButton = Find-ElementByAutomationId -Root $root -AutomationId "ActionsButton" -Deadline ((Get-Date).AddSeconds($TimeoutSeconds))
    if ($actionsButton.Current.Name -ne $ExpectedActionsName) {
        throw "ActionsButton has unexpected name '$($actionsButton.Current.Name)'."
    }

    Invoke-UiElement -Element $actionsButton
    Start-Sleep -Milliseconds 500

    $desktopRoot = [System.Windows.Automation.AutomationElement]::RootElement
    $priceListMenuItem = Find-InvokableElementByName -Root $desktopRoot -Name $PriceListMenuName -Deadline ((Get-Date).AddSeconds($TimeoutSeconds))
    if ($null -eq $priceListMenuItem) {
        throw "Price list menu item was not found."
    }

    Write-Output "WPF_SMOKE_NAVIGATION_OK=True"
    Write-Output "WPF_SMOKE_ACTIONS_NAME=$($actionsButton.Current.Name)"
    Write-Output "WPF_SMOKE_PRICE_LIST_MENU_FOUND=True"

    if (-not $SkipPriceListExport) {
        $exportsPath = Join-Path (Split-Path -Parent $exe) "exports"
        $startedAt = Get-Date
        Invoke-UiElement -Element $priceListMenuItem

        $exportFile = Wait-NewPriceListExport -ExportsPath $exportsPath -StartedAt $startedAt -Deadline ((Get-Date).AddSeconds($TimeoutSeconds))
        $lineCount = (Get-Content -LiteralPath $exportFile.FullName -Encoding UTF8 | Measure-Object -Line).Lines

        $okButton = Find-InvokableElementByName -Root $desktopRoot -Name "OK" -Deadline ((Get-Date).AddSeconds(3))
        if ($null -eq $okButton) {
            $okButton = Find-InvokableElementByName -Root $desktopRoot -Name $OkButtonRuName -Deadline ((Get-Date).AddSeconds(3))
        }

        if ($null -ne $okButton) {
            Invoke-UiElement -Element $okButton
        }

        Write-Output "WPF_SMOKE_PRICE_LIST_EXPORT_CREATED=True"
        Write-Output "WPF_SMOKE_PRICE_LIST_EXPORT_LINES=$lineCount"
        Write-Output "WPF_SMOKE_PRICE_LIST_EXPORT_PATH=$($exportFile.FullName)"
    }
}
finally {
    if ($null -ne $process -and -not $process.HasExited) {
        $process.CloseMainWindow() | Out-Null
        if (-not $process.WaitForExit(3000)) {
            $process.Kill()
            $process.WaitForExit()
        }
    }
}
