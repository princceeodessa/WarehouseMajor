Option Explicit
On Error Resume Next

Dim outputRoot, userName, password, basePath, queryList
Dim app, fso, writer

If WScript.Arguments.Count < 5 Then
    WScript.Echo "Usage: cscript //nologo count-1c-queries.vbs <output_root> <user_name> <password|__EMPTY__> <base_path> <table|table>"
    WScript.Quit 2
End If

outputRoot = WScript.Arguments(0)
userName = WScript.Arguments(1)
password = WScript.Arguments(2)
If password = "__EMPTY__" Then password = ""
basePath = WScript.Arguments(3)
queryList = Split(WScript.Arguments(4), "|")

Set fso = CreateObject("Scripting.FileSystemObject")
EnsureFolder outputRoot
Set writer = fso.CreateTextFile(outputRoot & "\counts.csv", True, True)
writer.WriteLine CsvLine(Array("table_name", "status", "row_count", "error_text"))

Set app = CreateObject("V83.Application")
If Err.Number <> 0 Then
    WScript.Echo "FATAL|CreateObject|" & Err.Number & "|" & Err.Description
    writer.Close
    WScript.Quit 1
End If

Err.Clear
app.Connect "File=""" & basePath & """;Usr=""" & userName & """;Pwd=""" & password & """;"
If Err.Number <> 0 Then
    WScript.Echo "FATAL|Connect|" & Err.Number & "|" & Err.Description
    writer.Close
    WScript.Quit 1
End If

Dim i, tableName
For i = LBound(queryList) To UBound(queryList)
    tableName = CStr(queryList(i))
    If Len(tableName) > 0 Then
        WriteCount tableName
    End If
Next

writer.Close
WScript.Echo "DONE|" & outputRoot

Sub WriteCount(tableName)
    Dim q, res, sel, rowCount
    Err.Clear
    Set q = app.NewObject("Query")
    If Err.Number <> 0 Then
        writer.WriteLine CsvLine(Array(tableName, "ERROR", "", "NEW_QUERY|" & Err.Number & "|" & Err.Description))
        Err.Clear
        Exit Sub
    End If

    q.Text = "SELECT COUNT(*) AS RowCount FROM " & tableName
    Err.Clear
    Set res = q.Execute()
    If Err.Number <> 0 Then
        writer.WriteLine CsvLine(Array(tableName, "ERROR", "", "EXECUTE|" & Err.Number & "|" & Err.Description))
        Err.Clear
        Exit Sub
    End If

    Set sel = res.Select()
    If Err.Number <> 0 Then
        writer.WriteLine CsvLine(Array(tableName, "ERROR", "", "SELECT|" & Err.Number & "|" & Err.Description))
        Err.Clear
        Exit Sub
    End If

    rowCount = ""
    If sel.Next() Then
        Err.Clear
        rowCount = CStr(sel.Get(0))
        If Err.Number <> 0 Then
            writer.WriteLine CsvLine(Array(tableName, "ERROR", "", "VALUE|" & Err.Number & "|" & Err.Description))
            Err.Clear
            Exit Sub
        End If
    End If

    writer.WriteLine CsvLine(Array(tableName, "OK", rowCount, ""))
End Sub

Function CsvLine(values)
    Dim i, result
    result = ""
    For i = LBound(values) To UBound(values)
        If i > LBound(values) Then result = result & ";"
        result = result & EscapeCsv(values(i))
    Next
    CsvLine = result
End Function

Function EscapeCsv(value)
    Dim s
    s = CStr(value)
    s = Replace(s, vbCrLf, "\n")
    s = Replace(s, vbCr, "\r")
    s = Replace(s, vbLf, "\n")
    s = Replace(s, """", """""")
    EscapeCsv = """" & s & """"
End Function

Sub EnsureFolder(path)
    If fso.FolderExists(path) Then Exit Sub

    Dim parent
    parent = fso.GetParentFolderName(path)
    If Len(parent) > 0 And Not fso.FolderExists(parent) Then EnsureFolder parent
    fso.CreateFolder path
End Sub

