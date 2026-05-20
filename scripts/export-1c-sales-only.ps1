[CmdletBinding()]
param(
    [string]$OutputRoot = '',
    [int]$StepTimeoutMinutes = 120,
    [string]$OnlyOneStep = '',
    [string]$BasePath = 'C:\blagodar'
)

$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$workspaceRoot = Split-Path -Parent $scriptDir
$vbsPath = Join-Path $scriptDir 'export-1c-csv.vbs'

if (-not (Test-Path $vbsPath)) {
    throw "Не найден $vbsPath"
}

if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $workspaceRoot 'app_data/one-c-live/sales-only'
}

$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path $OutputRoot) {
    Write-Host "Очищаю предыдущую выгрузку: $OutputRoot"
    Remove-Item -Recurse -Force $OutputRoot
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$targets = @(
    @{ Kind = 'documents'; Name = 'ЗаказПокупателя' },
    @{ Kind = 'documents'; Name = 'СчетНаОплату' },
    @{ Kind = 'documents'; Name = 'РасходнаяНакладная' },
    @{ Kind = 'documents'; Name = 'ВозвратТоваровОтПокупателя' },
    @{ Kind = 'documents'; Name = 'ПриходнаяНакладная' }
)

if (-not [string]::IsNullOrWhiteSpace($OnlyOneStep)) {
    $targets = @(@{ Kind = 'documents'; Name = $OnlyOneStep })
    Write-Host "TEST mode: $OnlyOneStep"
}

$logPath = Join-Path $OutputRoot 'last-sync.log'
"StartedUtc=$([DateTime]::UtcNow.ToString('o'))" | Out-File -FilePath $logPath -Encoding utf8
"WorkspaceRoot=$workspaceRoot" | Out-File -FilePath $logPath -Append -Encoding utf8
"OutputRoot=$OutputRoot" | Out-File -FilePath $logPath -Append -Encoding utf8
"BasePath=$BasePath" | Out-File -FilePath $logPath -Append -Encoding utf8
"" | Out-File -FilePath $logPath -Append -Encoding utf8

$totalStart = Get-Date
foreach ($t in $targets) {
    $stepStart = Get-Date
    $kind = $t.Kind
    $name = $t.Name
    Write-Host ""
    Write-Host "=== [$kind / $name] start $(Get-Date -Format HH:mm:ss) ==="

    "[$kind / $name] StartedUtc=$($stepStart.ToUniversalTime().ToString('o'))" | Out-File -FilePath $logPath -Append -Encoding utf8

    $cscriptArgs = @(
        '//nologo',
        $vbsPath,
        $kind,
        $OutputRoot,
        $name,
        '0'
    )

    $proc = Start-Process -FilePath 'cscript.exe' -ArgumentList $cscriptArgs `
        -WorkingDirectory $workspaceRoot -NoNewWindow -PassThru `
        -RedirectStandardOutput (Join-Path $OutputRoot "stdout_$name.log") `
        -RedirectStandardError  (Join-Path $OutputRoot "stderr_$name.log")

    $completed = $false
    if ($StepTimeoutMinutes -gt 0) {
        $completed = $proc.WaitForExit($StepTimeoutMinutes * 60 * 1000)
        if (-not $completed) {
            Write-Host "TIMEOUT после $StepTimeoutMinutes мин — kill процесса"
            try { $proc.Kill() } catch {}
            "[$kind / $name] TIMEOUT killed after $StepTimeoutMinutes min" | Out-File -FilePath $logPath -Append -Encoding utf8
            continue
        }
    } else {
        $proc.WaitForExit()
        $completed = $true
    }

    $stepEnd = Get-Date
    $duration = ($stepEnd - $stepStart)
    $exit = $proc.ExitCode

    "[$kind / $name] ExitCode=$exit Duration=$([Math]::Round($duration.TotalMinutes, 2))min" | Out-File -FilePath $logPath -Append -Encoding utf8
    "" | Out-File -FilePath $logPath -Append -Encoding utf8

    Write-Host "Exit=$exit Duration=$([Math]::Round($duration.TotalMinutes, 2)) min"
}

$totalEnd = Get-Date
$totalDuration = $totalEnd - $totalStart
"CompletedUtc=$($totalEnd.ToUniversalTime().ToString('o'))" | Out-File -FilePath $logPath -Append -Encoding utf8
"TotalDuration=$([Math]::Round($totalDuration.TotalMinutes, 2))min" | Out-File -FilePath $logPath -Append -Encoding utf8

Write-Host ""
Write-Host "=== DONE total=$([Math]::Round($totalDuration.TotalMinutes, 2)) min ==="
Write-Host "Output: $OutputRoot"
Write-Host "Log: $logPath"