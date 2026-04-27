param(
    [string]$Server = "(localdb)\MSSQLLocalDB",
    [string]$Database = "unf18_mig",
    [string]$OutputPath = "C:\blagodar\1c-migration\exports\unf-products-first-pass.csv"
)

$ErrorActionPreference = "Stop"

$outputDirectory = Split-Path -Path $OutputPath -Parent
if (-not [string]::IsNullOrWhiteSpace($outputDirectory) -and -not (Test-Path $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory | Out-Null
}

$connectionString = "Server=$Server;Database=$Database;Integrated Security=true;TrustServerCertificate=true;"
$query = @"
SELECT
    sys.fn_varbintohexstr(p._IDRRef) AS ItemRef,
    p._Code AS ItemCode,
    p._Description AS ItemName,
    NULLIF(LTRIM(RTRIM(p._Fld10089)), N'') AS SecondaryCode,
    NULLIF(LTRIM(RTRIM(p._Fld10210)), N'') AS CardBarcode,
    sys.fn_varbintohexstr(p._ParentIDRRef) AS ParentRef,
    parent._Code AS ParentCode,
    parent._Description AS ParentName,
    CASE WHEN p._Marked = 0x01 THEN 1 ELSE 0 END AS IsMarked,
    CASE WHEN p._Folder = 0x01 THEN 1 ELSE 0 END AS IsFolder,
    barcode.Barcodes AS RegisterBarcodes,
    CASE
        WHEN barcode.Barcodes IS NOT NULL AND NULLIF(LTRIM(RTRIM(p._Fld10210)), N'') IS NOT NULL THEN N'CardAndRegister'
        WHEN barcode.Barcodes IS NOT NULL THEN N'RegisterOnly'
        WHEN NULLIF(LTRIM(RTRIM(p._Fld10210)), N'') IS NOT NULL THEN N'CardOnly'
        ELSE N'NoBarcode'
    END AS BarcodeSource
FROM dbo._Reference399X1 AS p
LEFT JOIN dbo._Reference399X1 AS parent
    ON parent._IDRRef = p._ParentIDRRef
OUTER APPLY (
    SELECT STUFF((
        SELECT N';' + barcodeItems.Barcode
        FROM (
            SELECT DISTINCT NULLIF(LTRIM(RTRIM(b._Fld53162)), N'') AS Barcode
            FROM dbo._InfoRg53161 AS b
            WHERE b._Fld53163RRef = p._IDRRef
        ) AS barcodeItems
        WHERE barcodeItems.Barcode IS NOT NULL
        FOR XML PATH(''), TYPE
    ).value('.', 'nvarchar(max)'), 1, 1, N'') AS Barcodes
) AS barcode
WHERE p._Folder = 0x01
ORDER BY p._Description;
"@

$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
$command = $connection.CreateCommand()
$command.CommandText = $query
$command.CommandTimeout = 0

$table = New-Object System.Data.DataTable

try {
    $connection.Open()
    $reader = $command.ExecuteReader()
    try {
        $table.Load($reader)
    }
    finally {
        $reader.Dispose()
    }
}
finally {
    $connection.Dispose()
}

$table.Rows | Select-Object * | Export-Csv -Path $OutputPath -NoTypeInformation -Encoding UTF8

[PSCustomObject]@{
    OutputPath = $OutputPath
    Rows       = $table.Rows.Count
}
