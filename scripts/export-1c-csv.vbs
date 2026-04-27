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
Dim debugLogPath
Dim processedObjects
Dim processedFiles

basePath = "C:\blagodar"
userName = BuildPrivilegedUserName()
password = "Rv564524"

If WScript.Arguments.Count < 2 Then
    WScript.Echo "Usage: cscript //nologo export-1c-csv.vbs <batch_kind|all> <output_root> [name_filter] [max_items] [user_name] [password] [base_path]"
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
If WScript.Arguments.Count >= 5 Then
    userName = WScript.Arguments(4)
End If
If WScript.Arguments.Count >= 6 Then
    If WScript.Arguments(5) = "__EMPTY__" Then
        password = ""
    Else
        password = WScript.Arguments(5)
    End If
End If
If WScript.Arguments.Count >= 7 Then
    basePath = WScript.Arguments(6)
End If

Set fso = CreateObject("Scripting.FileSystemObject")
EnsureFolder outputRoot
manifestPath = outputRoot & "\manifest.csv"
debugLogPath = outputRoot & "\debug.log"
InitializeManifest

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

processedObjects = 0
processedFiles = 0

If batchKind = "all" Then
    ProcessBatch "catalogs"
    ProcessBatch "documents"
    ProcessBatch "informationregisters"
    ProcessBatch "accumulationregisters"
    ProcessBatch "accountingregisters"
    ProcessBatch "chartsofaccounts"
    ProcessBatch "chartsofcharacteristictypes"
    ProcessBatch "chartsofcalculationtypes"
    ProcessBatch "businessprocesses"
    ProcessBatch "tasks"
    ProcessBatch "constants"
Else
    ProcessBatch batchKind
End If

WScript.Echo "DONE|objects=" & processedObjects & "|files=" & processedFiles

Sub Trace(message)
    On Error Resume Next
    Dim writer
    Set writer = fso.OpenTextFile(debugLogPath, 8, True, -1)
    writer.WriteLine SafeStr(message)
    writer.Close
End Sub

Sub ProcessBatch(kind)
    EnsureFolder outputRoot & "\" & kind

    Select Case LCase(kind)
        Case "catalogs"
            ProcessCollection app.Metadata.Catalogs, "Catalog", "Catalog.", "catalog", True, kind
        Case "documents"
            ProcessCollection app.Metadata.Documents, "Document", "Document.", "document", True, kind
        Case "informationregisters"
            ProcessCollection app.Metadata.InformationRegisters, "InformationRegister", "InformationRegister.", "inforeg", False, kind
        Case "accumulationregisters"
            ProcessCollection app.Metadata.AccumulationRegisters, "AccumulationRegister", "AccumulationRegister.", "accumreg", False, kind
        Case "accountingregisters"
            ProcessCollection app.Metadata.AccountingRegisters, "AccountingRegister", "AccountingRegister.", "acctreg", False, kind
        Case "chartsofaccounts"
            ProcessCollection app.Metadata.ChartsOfAccounts, "ChartOfAccounts", "ChartOfAccounts.", "coa", False, kind
        Case "chartsofcharacteristictypes"
            ProcessCollection app.Metadata.ChartsOfCharacteristicTypes, "ChartOfCharacteristicTypes", "ChartOfCharacteristicTypes.", "coct", False, kind
        Case "chartsofcalculationtypes"
            ProcessCollection app.Metadata.ChartsOfCalculationTypes, "ChartOfCalculationTypes", "ChartOfCalculationTypes.", "coctcalc", False, kind
        Case "businessprocesses"
            ProcessCollection app.Metadata.BusinessProcesses, "BusinessProcess", "BusinessProcess.", "bp", False, kind
        Case "tasks"
            ProcessCollection app.Metadata.Tasks, "Task", "Task.", "task", False, kind
        Case "constants"
            ProcessConstants kind
        Case Else
            WScript.Echo "SKIP|UnknownBatch|" & kind
    End Select
End Sub

Sub ProcessConstants(kind)
    Dim filePath
    filePath = outputRoot & "\" & kind & "\constants_all.csv"
    ExportQuery "SELECT * FROM Constants", filePath, "constants_all", "Constants", "Constants", "", kind
    processedObjects = processedObjects + 1
End Sub

Sub ProcessCollection(collection, objectType, queryPrefix, tablePrefix, includeTabularSections, kind)
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
        itemName = SafeStr(item.Name)

        If ShouldProcess(itemName) Then
            processedObjects = processedObjects + 1

            tableName = tablePrefix & "_" & Pad4(i + 1)
            filePath = outputRoot & "\" & kind & "\" & tableName & ".csv"
            ExportQuery "SELECT * FROM " & queryPrefix & itemName, filePath, tableName, objectType, itemName, "", kind

            If includeTabularSections Then
                Err.Clear
                sectionCount = item.TabularSections.Count()
                If Err.Number = 0 Then
                    For j = 0 To sectionCount - 1
                        Set section = item.TabularSections.Get(j)
                        sectionName = SafeStr(section.Name)
                        sectionTableName = tableName & "_ts_" & Pad4(j + 1)
                        sectionFilePath = outputRoot & "\" & kind & "\" & sectionTableName & ".csv"
                        ExportQuery "SELECT * FROM " & queryPrefix & itemName & "." & sectionName, sectionFilePath, sectionTableName, objectType, itemName, sectionName, kind
                    Next
                Else
                    AppendManifest kind, objectType, itemName, "__tabular_sections__", tableName & "_tabsections", "", "ERROR", 0, 0, "TABULARSECTIONS|" & Err.Number & "|" & Err.Description, ""
                    Err.Clear
                End If
            End If

            If maxItems > 0 And processedObjects >= maxItems Then
                Exit For
            End If
        End If
    Next
End Sub

Function ShouldProcess(itemName)
    If nameFilter = "" Or nameFilter = "*" Then
        ShouldProcess = True
    Else
        ShouldProcess = (LCase(itemName) = LCase(nameFilter))
    End If
End Function

Sub ExportQuery(queryText, filePath, tableName, objectType, objectName, subObjectName, kind)
    Dim q
    Dim res
    Dim sel
    Dim writer
    Dim headerLine
    Dim rowCount
    Dim colCount
    Dim i
    Dim rowIndex

    Set q = Nothing
    Set res = Nothing
    Set sel = Nothing
    Set writer = Nothing

    Trace "START|" & tableName & "|" & queryText

    Err.Clear
    Set q = app.NewObject("Query")
    If Err.Number <> 0 Then
        Trace "FAIL_NEWOBJECT|" & tableName & "|" & Err.Number & "|" & Err.Description
        AppendManifest kind, objectType, objectName, subObjectName, tableName, filePath, "ERROR", 0, 0, "NEWOBJECT|" & Err.Number & "|" & Err.Description, queryText
        WScript.Echo "ERROR|" & tableName & "|NEWOBJECT|" & Err.Number
        Err.Clear
        Exit Sub
    End If

    q.Text = queryText

    Err.Clear
    Set res = q.Execute()
    If Err.Number <> 0 Then
        Trace "FAIL_EXECUTE|" & tableName & "|" & Err.Number & "|" & Err.Description
        AppendManifest kind, objectType, objectName, subObjectName, tableName, filePath, "ERROR", 0, 0, "EXECUTE|" & Err.Number & "|" & Err.Description, queryText
        WScript.Echo "ERROR|" & tableName & "|EXECUTE|" & Err.Number
        Err.Clear
        Exit Sub
    End If
    Trace "EXECUTE_OK|" & tableName

    Err.Clear
    colCount = res.Columns.Count()
    If Err.Number <> 0 Then
        Trace "FAIL_COLUMNS|" & tableName & "|" & Err.Number & "|" & Err.Description
        AppendManifest kind, objectType, objectName, subObjectName, tableName, filePath, "ERROR", 0, 0, "COLUMNS|" & Err.Number & "|" & Err.Description, queryText
        WScript.Echo "ERROR|" & tableName & "|COLUMNS|" & Err.Number
        Err.Clear
        Exit Sub
    End If
    Trace "COLUMNS_OK|" & tableName & "|" & colCount

    EnsureParentFolder filePath

    Err.Clear
    Set writer = fso.CreateTextFile(filePath, True, True)
    If Err.Number <> 0 Then
        Trace "FAIL_CREATEFILE|" & tableName & "|" & Err.Number & "|" & Err.Description
        AppendManifest kind, objectType, objectName, subObjectName, tableName, filePath, "ERROR", 0, colCount, "CREATEFILE|" & Err.Number & "|" & Err.Description, queryText
        WScript.Echo "ERROR|" & tableName & "|CREATEFILE|" & Err.Number
        Err.Clear
        Exit Sub
    End If

    headerLine = ""
    For i = 0 To colCount - 1
        If i > 0 Then
            headerLine = headerLine & ";"
        End If
        headerLine = headerLine & EscapeCsv(SafeStr(res.Columns.Get(i).Name))
    Next
    writer.WriteLine headerLine
    Trace "HEADER_WRITTEN|" & tableName

    Err.Clear
    Set sel = res.Select()
    If Err.Number <> 0 Then
        writer.Close
        Trace "FAIL_SELECT|" & tableName & "|" & Err.Number & "|" & Err.Description
        AppendManifest kind, objectType, objectName, subObjectName, tableName, filePath, "ERROR", 0, colCount, "SELECT|" & Err.Number & "|" & Err.Description, queryText
        WScript.Echo "ERROR|" & tableName & "|SELECT|" & Err.Number
        Err.Clear
        Exit Sub
    End If
    Trace "SELECT_OK|" & tableName

    rowCount = 0
    For rowIndex = 0 To 2147483646
        Err.Clear
        If Not sel.Next() Then
            If Err.Number <> 0 Then
                Trace "FAIL_NEXT|" & tableName & "|" & Err.Number & "|" & Err.Description
                Err.Clear
            End If
            Trace "SELECT_EOF|" & tableName & "|" & rowCount
            Exit For
        End If
        For i = 0 To colCount - 1
            If i > 0 Then
                writer.Write ";"
            End If
            WriteFieldValue writer, sel, i
        Next
        writer.WriteLine ""
        rowCount = rowCount + 1
        If rowCount = 1 Then
            Trace "FIRST_ROW_WRITTEN|" & tableName
        End If
    Next

    writer.Close
    Trace "FILE_CLOSED|" & tableName & "|" & rowCount

    Err.Clear
    AppendManifest kind, objectType, objectName, subObjectName, tableName, filePath, "OK", rowCount, colCount, "", queryText
    If Err.Number <> 0 Then
        Trace "FAIL_MANIFEST|" & tableName & "|" & Err.Number & "|" & Err.Description
        WScript.Echo "ERROR|" & tableName & "|MANIFEST|" & Err.Number
        Err.Clear
        Exit Sub
    End If
    Trace "MANIFEST_OK|" & tableName

    processedFiles = processedFiles + 1
    Trace "FINISH|" & tableName & "|" & rowCount
    WScript.Echo "OK|" & tableName & "|" & rowCount
End Sub

Sub WriteFieldValue(writer, sel, index)
    On Error Resume Next
    Dim objValue
    Dim scalarValue
    Dim scalarText
    Dim rawText

    Set objValue = Nothing

    Err.Clear
    Set objValue = sel.Get(index)
    If Err.Number = 0 Then
        rawText = SafeValueToString(objValue)
        writer.Write EscapeCsv(rawText)
        Exit Sub
    End If

    Err.Clear
    scalarValue = sel.Get(index)
    If Err.Number = 0 Then
        Err.Clear
        scalarText = CStr(scalarValue)
        If Err.Number = 0 Then
            rawText = scalarText
        Else
            Err.Clear
            rawText = SafeValueToString(scalarValue)
        End If
    Else
        rawText = "__ERROR__|" & Err.Number & "|" & Err.Description
        Err.Clear
    End If
    writer.Write EscapeCsv(rawText)
End Sub

Function SafeValueToString(value)
    On Error Resume Next
    Err.Clear
    SafeValueToString = CStr(app.ValueToStringInternal(value))
    If Err.Number <> 0 Then
        Err.Clear
        SafeValueToString = ScalarToText(value)
        If Err.Number <> 0 Then
            SafeValueToString = "__VALUE_ERROR__|" & Err.Number & "|" & Err.Description
            Err.Clear
        End If
    End If
End Function

Function ScalarToText(value)
    On Error Resume Next
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
            ScalarToText = SafeStr(value)
    End Select
End Function

Function DateToIso(value)
    On Error Resume Next
    DateToIso = _
        Year(value) & "-" & _
        Right("0" & Month(value), 2) & "-" & _
        Right("0" & Day(value), 2) & " " & _
        Right("0" & Hour(value), 2) & ":" & _
        Right("0" & Minute(value), 2) & ":" & _
        Right("0" & Second(value), 2)
End Function

Function EscapeCsv(value)
    On Error Resume Next
    Dim s
    s = SafeStr(value)
    s = Replace(s, vbCrLf, "\n")
    s = Replace(s, vbCr, "\r")
    s = Replace(s, vbLf, "\n")
    s = Replace(s, """", """""")
    EscapeCsv = """" & s & """"
End Function

Function SafeStr(value)
    On Error Resume Next
    Err.Clear
    SafeStr = CStr(value)
    If Err.Number <> 0 Then
        SafeStr = ""
        Err.Clear
    End If
End Function

Sub InitializeManifest()
    Dim writer

    If fso.FileExists(manifestPath) Then
        Exit Sub
    End If

    Set writer = fso.CreateTextFile(manifestPath, True, True)
    writer.WriteLine _
        EscapeCsv("batch_kind") & ";" & _
        EscapeCsv("object_type") & ";" & _
        EscapeCsv("object_name") & ";" & _
        EscapeCsv("subobject_name") & ";" & _
        EscapeCsv("csv_name") & ";" & _
        EscapeCsv("file_path") & ";" & _
        EscapeCsv("status") & ";" & _
        EscapeCsv("row_count") & ";" & _
        EscapeCsv("column_count") & ";" & _
        EscapeCsv("error_text") & ";" & _
        EscapeCsv("query_text")
    writer.Close
End Sub

Sub AppendManifest(batchName, objectType, objectName, subObjectName, csvName, filePath, status, rowCount, colCount, errorText, queryText)
    Dim writer

    Set writer = fso.OpenTextFile(manifestPath, 8, True, -1)
    writer.WriteLine _
        EscapeCsv(batchName) & ";" & _
        EscapeCsv(objectType) & ";" & _
        EscapeCsv(objectName) & ";" & _
        EscapeCsv(subObjectName) & ";" & _
        EscapeCsv(csvName) & ";" & _
        EscapeCsv(filePath) & ";" & _
        EscapeCsv(status) & ";" & _
        EscapeCsv(CStr(rowCount)) & ";" & _
        EscapeCsv(CStr(colCount)) & ";" & _
        EscapeCsv(errorText) & ";" & _
        EscapeCsv(queryText)
    writer.Close
End Sub

Sub EnsureFolder(path)
    If path = "" Then
        Exit Sub
    End If

    If fso.FolderExists(path) Then
        Exit Sub
    End If

    Dim parentPath
    parentPath = fso.GetParentFolderName(path)
    If parentPath <> "" And Not fso.FolderExists(parentPath) Then
        EnsureFolder parentPath
    End If

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

Function BuildPrivilegedUserName()
    BuildPrivilegedUserName = _
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
