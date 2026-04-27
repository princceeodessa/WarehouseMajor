Option Explicit

On Error Resume Next

Dim basePath
Dim userName
Dim password
Dim outputRoot
Dim batchKind
Dim nameFilter
Dim maxItems
Dim app
Dim fso
Dim manifestPath
Dim processedItems

basePath = "C:\blagodar"
userName = BuildAdminUserName()
password = "Rv564524"

If WScript.Arguments.Count < 2 Then
    WScript.Echo "Usage: cscript //nologo export-1c-batch.vbs <batch_kind> <output_root> [name_filter] [max_items]"
    WScript.Quit 2
End If

batchKind = LCase(WScript.Arguments(0))
outputRoot = WScript.Arguments(1)
If WScript.Arguments.Count >= 3 Then
    nameFilter = WScript.Arguments(2)
Else
    nameFilter = ""
End If
If WScript.Arguments.Count >= 4 Then
    maxItems = CLng(WScript.Arguments(3))
Else
    maxItems = 0
End If

Set fso = CreateObject("Scripting.FileSystemObject")
EnsureFolder outputRoot
EnsureFolder outputRoot & "\" & batchKind

manifestPath = outputRoot & "\manifest.tsv"
processedItems = 0

Set app = CreateObject("V83.Application")
If Err.Number <> 0 Then
    WScript.Echo "FATAL|CreateObject|" & Err.Number & "|" & Err.Description
    WScript.Quit 1
End If

Err.Clear
app.Connect "File=""" & basePath & """;Usr=""" & userName & """;Pwd=""" & password & """;"
If Err.Number <> 0 Then
    WScript.Echo "FATAL|Connect|" & Err.Number & "|" & Err.Description
    WScript.Quit 1
End If

Select Case batchKind
    Case "catalogs"
        ProcessCollection app.Metadata.Catalogs, "Catalog", "Catalog.", "catalog", True
    Case "documents"
        ProcessCollection app.Metadata.Documents, "Document", "Document.", "document", True
    Case "informationregisters"
        ProcessCollection app.Metadata.InformationRegisters, "InformationRegister", "InformationRegister.", "inforeg", False
    Case "accumulationregisters"
        ProcessCollection app.Metadata.AccumulationRegisters, "AccumulationRegister", "AccumulationRegister.", "accumreg", False
    Case "accountingregisters"
        ProcessCollection app.Metadata.AccountingRegisters, "AccountingRegister", "AccountingRegister.", "acctreg", False
    Case "chartsofaccounts"
        ProcessCollection app.Metadata.ChartsOfAccounts, "ChartOfAccounts", "ChartOfAccounts.", "coa", False
    Case "chartsofcharacteristictypes"
        ProcessCollection app.Metadata.ChartsOfCharacteristicTypes, "ChartOfCharacteristicTypes", "ChartOfCharacteristicTypes.", "coct", False
    Case "chartsofcalculationtypes"
        ProcessCollection app.Metadata.ChartsOfCalculationTypes, "ChartOfCalculationTypes", "ChartOfCalculationTypes.", "coctcalc", False
    Case "businessprocesses"
        ProcessCollection app.Metadata.BusinessProcesses, "BusinessProcess", "BusinessProcess.", "bp", False
    Case "tasks"
        ProcessCollection app.Metadata.Tasks, "Task", "Task.", "task", False
    Case "constants"
        ProcessConstants
    Case Else
        WScript.Echo "FATAL|UnknownBatch|" & batchKind
        WScript.Quit 2
End Select

WScript.Echo "DONE|" & batchKind & "|" & processedItems

Sub ProcessConstants()
    Dim filePath
    filePath = outputRoot & "\" & batchKind & "\constants_all.tsv"
    ExportQuery "SELECT * FROM Constants", filePath, "constants_all", "Constants", "Constants", "", "constants"
End Sub

Sub ProcessCollection(collection, objectType, queryPrefix, tablePrefix, includeTabularSections)
    Dim totalCount
    Dim i
    Dim item
    Dim itemName
    Dim tableName
    Dim filePath
    Dim sectionCount
    Dim j
    Dim section
    Dim sectionName
    Dim sectionTableName
    Dim sectionFilePath

    totalCount = collection.Count()

    For i = 0 To totalCount - 1
        Set item = collection.Get(i)
        itemName = CStr(item.Name)

        If ShouldProcess(itemName) Then
            tableName = tablePrefix & "_" & Pad4(i + 1)
            filePath = outputRoot & "\" & batchKind & "\" & tableName & ".tsv"

            ExportQuery "SELECT * FROM " & queryPrefix & itemName, filePath, tableName, objectType, itemName, "", batchKind

            If includeTabularSections Then
                sectionCount = item.TabularSections.Count()
                For j = 0 To sectionCount - 1
                    Set section = item.TabularSections.Get(j)
                    sectionName = CStr(section.Name)
                    sectionTableName = tableName & "_ts_" & Pad4(j + 1)
                    sectionFilePath = outputRoot & "\" & batchKind & "\" & sectionTableName & ".tsv"
                    ExportQuery "SELECT * FROM " & queryPrefix & itemName & "." & sectionName, sectionFilePath, sectionTableName, objectType, itemName, sectionName, batchKind
                Next
            End If

            processedItems = processedItems + 1
            If maxItems > 0 And processedItems >= maxItems Then
                Exit For
            End If
        End If
    Next
End Sub

Function ShouldProcess(itemName)
    If nameFilter = "" Then
        ShouldProcess = True
    Else
        ShouldProcess = (LCase(itemName) = LCase(nameFilter))
    End If
End Function

Sub ExportQuery(queryText, filePath, tableName, objectType, objectName, subObjectName, batchName)
    Dim q
    Dim res
    Dim sel
    Dim writer
    Dim header
    Dim line
    Dim rowCount
    Dim colCount
    Dim i

    Set q = Nothing
    Set res = Nothing
    Set sel = Nothing
    Set writer = Nothing

    Err.Clear
    Set q = app.NewObject("Query")
    If Err.Number <> 0 Then
        AppendManifest batchName, objectType, objectName, subObjectName, tableName, filePath, "ERROR", 0, 0, "NEWOBJECT|" & Err.Number & "|" & Err.Description, queryText
        WScript.Echo "ERROR|" & tableName & "|NEWOBJECT|" & Err.Number
        Err.Clear
        Exit Sub
    End If

    q.Text = queryText

    Err.Clear
    Set res = q.Execute()
    If Err.Number <> 0 Then
        AppendManifest batchName, objectType, objectName, subObjectName, tableName, filePath, "ERROR", 0, 0, Err.Description, queryText
        WScript.Echo "ERROR|" & tableName & "|EXECUTE|" & Err.Number
        Err.Clear
        Exit Sub
    End If

    colCount = res.Columns.Count()

    EnsureParentFolder filePath
    Set writer = fso.CreateTextFile(filePath, True, True)

    header = ""
    For i = 0 To colCount - 1
        If i > 0 Then
            header = header & vbTab
        End If
        header = header & EscapeTsv(CStr(res.Columns.Get(i).Name))
    Next
    writer.WriteLine header

    Set sel = res.Select()
    rowCount = 0

    Do While sel.Next()
        line = ""
        For i = 0 To colCount - 1
            If i > 0 Then
                line = line & vbTab
            End If
            line = line & EscapeTsv(GetFieldValue(sel, i))
        Next
        writer.WriteLine line
        rowCount = rowCount + 1
    Loop

    writer.Close

    AppendManifest batchName, objectType, objectName, subObjectName, tableName, filePath, "OK", rowCount, colCount, "", queryText
    WScript.Echo "OK|" & tableName & "|" & rowCount
End Sub

Function GetFieldValue(sel, index)
    Dim objValue
    Dim scalarValue

    Set objValue = Nothing

    Err.Clear
    Set objValue = sel.Get(index)
    If Err.Number = 0 Then
        GetFieldValue = CStr(app.ValueToStringInternal(objValue))
        Exit Function
    End If

    Err.Clear
    scalarValue = sel.Get(index)
    If Err.Number = 0 Then
        GetFieldValue = ScalarToText(scalarValue)
    Else
        GetFieldValue = "__ERROR__|" & Err.Number & "|" & Err.Description
        Err.Clear
    End If
End Function

Function ScalarToText(value)
    Select Case VarType(value)
        Case 0
            ScalarToText = ""
        Case 1
            ScalarToText = "NULL"
        Case 7
            ScalarToText = DateToIso(value)
        Case 11
            If value Then
                ScalarToText = "1"
            Else
                ScalarToText = "0"
            End If
        Case Else
            ScalarToText = CStr(value)
    End Select
End Function

Function DateToIso(value)
    DateToIso = _
        Year(value) & "-" & _
        Right("0" & Month(value), 2) & "-" & _
        Right("0" & Day(value), 2) & " " & _
        Right("0" & Hour(value), 2) & ":" & _
        Right("0" & Minute(value), 2) & ":" & _
        Right("0" & Second(value), 2)
End Function

Function EscapeTsv(value)
    Dim s
    s = CStr(value)
    s = Replace(s, "\", "\\")
    s = Replace(s, vbTab, "\t")
    s = Replace(s, vbCr, "\r")
    s = Replace(s, vbLf, "\n")
    EscapeTsv = s
End Function

Sub AppendManifest(batchName, objectType, objectName, subObjectName, tableName, filePath, status, rowCount, colCount, errorText, queryText)
    Dim exists
    Dim writer

    exists = fso.FileExists(manifestPath)
    Set writer = fso.OpenTextFile(manifestPath, 8, True, -1)
    If Not exists Then
        writer.WriteLine "batch_kind" & vbTab & "object_type" & vbTab & "object_name" & vbTab & "subobject_name" & vbTab & "mysql_table" & vbTab & "file_path" & vbTab & "status" & vbTab & "row_count" & vbTab & "column_count" & vbTab & "error_text" & vbTab & "query_text"
    End If
    writer.WriteLine _
        EscapeTsv(batchName) & vbTab & _
        EscapeTsv(objectType) & vbTab & _
        EscapeTsv(objectName) & vbTab & _
        EscapeTsv(subObjectName) & vbTab & _
        EscapeTsv(tableName) & vbTab & _
        EscapeTsv(filePath) & vbTab & _
        EscapeTsv(status) & vbTab & _
        EscapeTsv(CStr(rowCount)) & vbTab & _
        EscapeTsv(CStr(colCount)) & vbTab & _
        EscapeTsv(errorText) & vbTab & _
        EscapeTsv(queryText)
    writer.Close
End Sub

Sub EnsureFolder(path)
    If Not fso.FolderExists(path) Then
        fso.CreateFolder path
    End If
End Sub

Sub EnsureParentFolder(filePath)
    Dim folderPath
    folderPath = fso.GetParentFolderName(filePath)
    If folderPath <> "" Then
        EnsureFolder folderPath
    End If
End Sub

Function Pad4(value)
    Pad4 = Right("0000" & CStr(value), 4)
End Function

Function BuildAdminUserName()
    BuildAdminUserName = _
        ChrW(1042) & _
        ChrW(1086) & _
        ChrW(1088) & _
        ChrW(1086) & _
        ChrW(1078) & _
        ChrW(1094) & _
        ChrW(1086) & _
        ChrW(1074) & _
        ChrW(1057)
End Function
