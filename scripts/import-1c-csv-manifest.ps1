param(
    [Parameter(Mandatory = $true)]
    [string]$ManifestPath,

    [Parameter(Mandatory = $true)]
    [string]$DatabaseName,

    [string]$MysqlExe = 'C:\laragon\bin\mysql\mysql-8.4.3-winx64\bin\mysql.exe'
)

$ErrorActionPreference = 'Stop'
$bt = [char]96

Add-Type -AssemblyName Microsoft.VisualBasic

function Normalize-NullableText {
    param($Value)

    if ($null -eq $Value) {
        return ''
    }

    return [string]$Value
}

function Escape-MySqlIdentifier {
    param([string]$Value)

    return ($Value -replace [regex]::Escape($bt), ($bt.ToString() + $bt.ToString()))
}

function Escape-TsvField {
    param([string]$Value)

    $text = Normalize-NullableText $Value
    $text = $text -replace '\\', '\\\\'
    $text = $text -replace "`t", '\t'
    $text = $text -replace "`r", '\r'
    $text = $text -replace "`n", '\n'
    return $text
}

function New-CsvParser {
    param([string]$Path)

    $parser = New-Object Microsoft.VisualBasic.FileIO.TextFieldParser($Path, [System.Text.Encoding]::Unicode)
    $parser.SetDelimiters(';')
    $parser.HasFieldsEnclosedInQuotes = $true
    return $parser
}

function Read-CsvHeader {
    param([string]$Path)

    $parser = New-CsvParser -Path $Path
    try {
        if ($parser.EndOfData) {
            return @()
        }

        return @($parser.ReadFields())
    }
    finally {
        $parser.Close()
    }
}

function Convert-UnicodeCsvToUtf8Tsv {
    param(
        [string]$SourcePath,
        [string]$TargetPath
    )

    $parser = New-CsvParser -Path $SourcePath
    $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
    $writer = New-Object System.IO.StreamWriter($TargetPath, $false, $utf8NoBom)

    try {
        while (-not $parser.EndOfData) {
            $fields = @($parser.ReadFields())
            $escapedFields = foreach ($field in $fields) {
                Escape-TsvField -Value $field
            }

            $writer.WriteLine(($escapedFields -join "`t"))
        }
    }
    finally {
        $writer.Dispose()
        $parser.Close()
    }
}

function Invoke-MySql {
    param(
        [string]$Sql,
        [string]$TargetDatabase = $null,
        [switch]$UseLocalInfile
    )

    $args = @('--default-character-set=utf8mb4')
    if ($UseLocalInfile) {
        $args += '--local-infile=1'
    }
    $args += '-uroot'
    if (-not [string]::IsNullOrWhiteSpace($TargetDatabase)) {
        $args += $TargetDatabase
    }

    $Sql | & $MysqlExe @args
}

if (-not (Test-Path $ManifestPath)) {
    throw "Manifest not found: $ManifestPath"
}

$manifest = Import-Csv -Path $ManifestPath -Delimiter ';' -Encoding Unicode

Invoke-MySql -Sql "SET GLOBAL local_infile = 1;"
Invoke-MySql -Sql "CREATE DATABASE IF NOT EXISTS ${DatabaseName} CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;"

$metaSql = @"
CREATE TABLE IF NOT EXISTS ${bt}__objects${bt} (
  ${bt}mysql_table${bt} varchar(128) NOT NULL,
  ${bt}batch_kind${bt} varchar(64) NULL,
  ${bt}object_type${bt} varchar(128) NULL,
  ${bt}object_name${bt} longtext NULL,
  ${bt}subobject_name${bt} longtext NULL,
  ${bt}file_path${bt} longtext NULL,
  ${bt}status${bt} varchar(32) NULL,
  ${bt}row_count${bt} bigint NULL,
  ${bt}column_count${bt} int NULL,
  ${bt}error_text${bt} longtext NULL,
  ${bt}query_text${bt} longtext NULL,
  PRIMARY KEY (${bt}mysql_table${bt})
) CHARACTER SET utf8mb4;

CREATE TABLE IF NOT EXISTS ${bt}__columns${bt} (
  ${bt}mysql_table${bt} varchar(128) NOT NULL,
  ${bt}ordinal_position${bt} int NOT NULL,
  ${bt}source_column_name${bt} longtext NULL,
  PRIMARY KEY (${bt}mysql_table${bt}, ${bt}ordinal_position${bt})
) CHARACTER SET utf8mb4;
"@

Invoke-MySql -Sql $metaSql -TargetDatabase $DatabaseName

foreach ($row in $manifest) {
    $tableName = $row.csv_name
    if ([string]::IsNullOrWhiteSpace($tableName)) {
        continue
    }

    $escapedTable = Escape-MySqlIdentifier $tableName
    $escapedObjectType = (Normalize-NullableText $row.object_type) -replace "'", "''"
    $escapedObjectName = (Normalize-NullableText $row.object_name) -replace "'", "''"
    $escapedSubObjectName = (Normalize-NullableText $row.subobject_name) -replace "'", "''"
    $escapedBatchKind = (Normalize-NullableText $row.batch_kind) -replace "'", "''"
    $escapedFilePath = (Normalize-NullableText $row.file_path) -replace "'", "''"
    $escapedStatus = (Normalize-NullableText $row.status) -replace "'", "''"
    $escapedErrorText = (Normalize-NullableText $row.error_text) -replace "'", "''"
    $escapedQueryText = (Normalize-NullableText $row.query_text) -replace "'", "''"
    $rowCountSql = if ([string]::IsNullOrWhiteSpace($row.row_count)) { 'NULL' } else { [string]$row.row_count }
    $columnCountSql = if ([string]::IsNullOrWhiteSpace($row.column_count)) { 'NULL' } else { [string]$row.column_count }

    $upsertObject = @"
REPLACE INTO ${bt}__objects${bt}
(${bt}mysql_table${bt},${bt}batch_kind${bt},${bt}object_type${bt},${bt}object_name${bt},${bt}subobject_name${bt},${bt}file_path${bt},${bt}status${bt},${bt}row_count${bt},${bt}column_count${bt},${bt}error_text${bt},${bt}query_text${bt})
VALUES
('$escapedTable','$escapedBatchKind','$escapedObjectType','$escapedObjectName','$escapedSubObjectName','$escapedFilePath','$escapedStatus',$rowCountSql,$columnCountSql,'$escapedErrorText','$escapedQueryText');
"@
    Invoke-MySql -Sql $upsertObject -TargetDatabase $DatabaseName

    if ($row.status -ne 'OK') {
        continue
    }

    $filePath = $row.file_path
    if (-not (Test-Path $filePath)) {
        continue
    }

    $sourceColumns = @(Read-CsvHeader -Path $filePath)
    if ($sourceColumns.Count -eq 0) {
        continue
    }

    $columnDefs = @()
    $loadColumns = @()
    $columnMetaSql = @("DELETE FROM ${bt}__columns${bt} WHERE ${bt}mysql_table${bt} = '$escapedTable';")

    for ($i = 0; $i -lt $sourceColumns.Count; $i++) {
        $ordinal = $i + 1
        $columnName = 'c' + $ordinal.ToString('000')
        $escapedColumn = Escape-MySqlIdentifier $columnName
        $columnDefs += "  ${bt}$escapedColumn${bt} LONGTEXT NULL"
        $loadColumns += "${bt}$escapedColumn${bt}"

        $sourceColumn = (Normalize-NullableText $sourceColumns[$i]) -replace "'", "''"
        $columnMetaSql += "INSERT INTO ${bt}__columns${bt} (${bt}mysql_table${bt},${bt}ordinal_position${bt},${bt}source_column_name${bt}) VALUES ('$escapedTable',$ordinal,'$sourceColumn');"
    }

    $commentParts = @(
        "1C",
        (Normalize-NullableText $row.object_type),
        (Normalize-NullableText $row.object_name)
    )
    if (-not [string]::IsNullOrWhiteSpace($row.subobject_name)) {
        $commentParts += (Normalize-NullableText $row.subobject_name)
    }
    $tableComment = (($commentParts -join ' | ') -replace "'", "''")

    $createSql = @"
DROP TABLE IF EXISTS ${bt}$escapedTable${bt};
CREATE TABLE ${bt}$escapedTable${bt} (
  ${bt}__rowid${bt} BIGINT NOT NULL AUTO_INCREMENT,
$(($columnDefs -join ",`r`n"))
,  PRIMARY KEY (${bt}__rowid${bt})
) CHARACTER SET utf8mb4 COMMENT='$tableComment';
$($columnMetaSql -join "`r`n")
"@
    Invoke-MySql -Sql $createSql -TargetDatabase $DatabaseName

    $utf8Path = "$filePath.utf8.tsv"
    Convert-UnicodeCsvToUtf8Tsv -SourcePath $filePath -TargetPath $utf8Path

    $escapedUtf8Path = ($utf8Path -replace '\\', '\\\\') -replace "'", "''"
    $loadSql = @"
LOAD DATA LOCAL INFILE '$escapedUtf8Path'
INTO TABLE ${bt}$escapedTable${bt}
CHARACTER SET utf8mb4
FIELDS TERMINATED BY '\t'
ESCAPED BY '\\'
LINES TERMINATED BY '\n'
IGNORE 1 LINES
($(($loadColumns -join ',')));
"@
    Invoke-MySql -Sql $loadSql -TargetDatabase $DatabaseName -UseLocalInfile
}
