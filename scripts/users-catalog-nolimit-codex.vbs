On Error Resume Next

Function AsStr(app, sel, idx)
    On Error Resume Next
    Dim o, v
    Set o = Nothing
    Err.Clear
    Set o = sel.Get(idx)
    If Err.Number = 0 Then
        AsStr = app.ValueToStringInternal(o)
        Exit Function
    End If
    Err.Clear
    v = sel.Get(idx)
    If Err.Number = 0 Then
        AsStr = CStr(v)
    Else
        AsStr = "<ERR " & Err.Number & ": " & Err.Description & ">"
        Err.Clear
    End If
End Function

Dim app, q, res, sel, fso, file, j
Set app = CreateObject("V83.Application")
app.Connect "File=""C:\blagodar"";Usr=""codex"";Pwd="""";"
Set fso = CreateObject("Scripting.FileSystemObject")
Set file = fso.CreateTextFile("C:\blagodar\WarehouseAutomatisaion\users-catalog-nolimit-codex.txt", True, True)
Set q = app.NewObject("Query")
q.Text = "SELECT * FROM Catalog.Пользователи"
Set res = q.Execute()
If Err.Number <> 0 Then
    file.WriteLine "EXEC_ERROR|" & Err.Number & "|" & Err.Description
    file.Close
    WScript.Quit 1
End If
Set sel = res.Select()
If Not sel.Next() Then
    file.WriteLine "NO_ROWS"
Else
    file.WriteLine "HAS_ROW"
    For j = 0 To res.Columns.Count() - 1
        file.WriteLine res.Columns.Get(j).Name & "=" & AsStr(app, sel, j)
    Next
End If
file.Close
