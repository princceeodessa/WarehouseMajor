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

Dim app, q, res, sel, fso, file, i, j
Set app = CreateObject("V83.Application")
app.Connect "File=""C:\blagodar"";Usr=""ВорожцовС"";Pwd=""Rv564524"";"
Set fso = CreateObject("Scripting.FileSystemObject")
Set file = fso.CreateTextFile("C:\blagodar\WarehouseAutomatisaion\users-catalog-sample-vorozh.txt", True, True)
Set q = app.NewObject("Query")
q.Text = "SELECT TOP 5 * FROM Catalog.Пользователи"
Set res = q.Execute()
Set sel = res.Select()
For i = 0 To 4
    If Not sel.Next() Then Exit For
    file.WriteLine "ROW|" & i
    For j = 0 To res.Columns.Count() - 1
        file.WriteLine res.Columns.Get(j).Name & "=" & AsStr(app, sel, j)
    Next
Next
file.Close
