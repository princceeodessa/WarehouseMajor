param(
    [Parameter(Mandatory = $true)]
    [int]$TargetPid,
    [Parameter(Mandatory = $true)]
    [string]$LogPath,
    [int]$IntervalSec = 2
)

$ErrorActionPreference = "SilentlyContinue"

"=== WATCH START $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') PID=$TargetPid ===" | Add-Content -Path $LogPath

while ($true) {
    $proc = Get-Process -Id $TargetPid
    if (-not $proc) {
        "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') | EXIT" | Add-Content -Path $LogPath
        break
    }

    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') | PID=$($proc.Id) | Responding=$($proc.Responding) | CPU=$([math]::Round($proc.CPU, 2)) | WS_MB=$([math]::Round($proc.WorkingSet64 / 1MB, 1)) | PM_MB=$([math]::Round($proc.PrivateMemorySize64 / 1MB, 1))" |
        Add-Content -Path $LogPath

    Start-Sleep -Seconds $IntervalSec
}
