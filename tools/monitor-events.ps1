param(
    [Parameter(Mandatory = $true)]
    [string]$LogPath,
    [long]$StartRecordId = 0,
    [int]$IntervalSec = 2
)

$ErrorActionPreference = "SilentlyContinue"
$providers = @(".NET Runtime", "Application Error", "Windows Error Reporting")

if ($StartRecordId -le 0) {
    $latest = Get-WinEvent -FilterHashtable @{ LogName = "Application" } -MaxEvents 1
    if ($latest) {
        $StartRecordId = [long]$latest.RecordId
    }
}

$seen = [long]$StartRecordId
"=== EVENT WATCH START $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') LAST_ID=$seen ===" | Add-Content -Path $LogPath

while ($true) {
    $events = Get-WinEvent -FilterHashtable @{ LogName = "Application" } -MaxEvents 200 | Sort-Object RecordId
    foreach ($eventItem in $events) {
        $recordId = [long]$eventItem.RecordId
        if ($recordId -le $seen) {
            continue
        }

        if ($providers -contains $eventItem.ProviderName) {
            $message = ($eventItem.Message -replace "`r`n", " ")
            if ($message.Length -gt 500) {
                $message = $message.Substring(0, 500) + "..."
            }

            "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') | ID=$($eventItem.Id) | Provider=$($eventItem.ProviderName) | Level=$($eventItem.LevelDisplayName) | RecordId=$recordId | $message" |
                Add-Content -Path $LogPath
        }

        $seen = $recordId
    }

    Start-Sleep -Seconds $IntervalSec
}
