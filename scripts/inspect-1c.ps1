$ErrorActionPreference = "Stop"

$connectionString = 'File="C:\blagodar\WarehouseAutomatisaion\restored_1c_db";'

try {
    $com = New-Object -ComObject "V83.COMConnector"
    $conn = $com.Connect($connectionString)

    Write-Output "CONNECTED"
    $conn | Get-Member | Select-Object -First 50 Name, MemberType | Format-Table -AutoSize
}
catch {
    Write-Error $_
    exit 1
}
