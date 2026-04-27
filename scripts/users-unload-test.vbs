On Error Resume Next

Function Conv(app, row, idx)
    On Error Resume Next
    Dim v
    Set v = Nothing
    Err.Clear
    Set v = row.Get(idx)
    If Err.Number = 0 Then
        Conv = "OBJ|" & app.ValueToStringInternal(v)
        Exit Function
    End If
    Err.Clear
    v = row.Get(idx)
    If Err.Number = 0 Then
        Conv = "VAL|" & app.ValueToStringInternal(v)
    Else
        Conv = "ERR|" & Err.Number & "|" & Err.Description
        Err.Clear
    End If
End Function

Dim app, q, res, vt, row, fso, file, j
Set app = CreateObject("V83.Application")
app.Connect "File=""C:\blagodar"";Usr=""codex"";Pwd="""";"
Set fso = CreateObject("Scripting.FileSystemObject")
Set file = fso.CreateTextFile("C:\blagodar\WarehouseAutomatisaion\users-unload-test.txt", True, True)
Set q = app.NewObject("Query")
q.Text = "SELECT TOP 1 * FROM Catalog.Пользователи"
Set res = q.Execute()
If Err.Number <> 0 Then
    file.WriteLine "EXEC_ERROR|" & Err.Number & "|" & Err.Description
    file.Close
    WScript.Quit 1
End If
Set vt = res.Unload()
If Err.Number <> 0 Then
    file.WriteLine "UNLOAD_ERROR|" & Err.Number & "|" & Err.Description
    file.Close
    WScript.Quit 1
End If
file.WriteLine "UNLOAD_OK|" & vt.Count() & "|" & vt.Columns.Count()
If vt.Count() > 0 Then
    Set row = vt.Get(0)
    For j = 0 To vt.Columns.Count() - 1
        file.WriteLine vt.Columns.Get(j).Name & "=" & Conv(app, row, j)
    Next
End If
file.Close
