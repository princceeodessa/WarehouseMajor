On Error Resume Next

Dim app, catalogs, item, queryText, q, res, sel, fso, file, v, o

Set app = CreateObject("V83.Application")
app.Connect "File=""C:\blagodar"";Usr=""codex"";Pwd="""";"

Set fso = CreateObject("Scripting.FileSystemObject")
Set file = fso.CreateTextFile("C:\blagodar\WarehouseAutomatisaion\test-users-versiondata-codex.txt", True, True)

Set catalogs = app.Metadata.Catalogs
Set item = catalogs.Get(417)
queryText = "SELECT * FROM Catalog." & CStr(item.Name)

Set q = app.NewObject("Query")
q.Text = queryText
Set res = q.Execute()
Set sel = res.Select()
If Not sel.Next() Then
    file.WriteLine "NO_ROWS"
    file.Close
    WScript.Quit 1
End If

file.WriteLine "ROW_READY"

Set o = Nothing
Err.Clear
Set o = sel.Get(1)
If Err.Number = 0 Then
    file.WriteLine "SET_OK|" & app.ValueToStringInternal(o)
Else
    file.WriteLine "SET_ERR|" & Err.Number & "|" & Err.Description
End If

Err.Clear
v = sel.Get(1)
If Err.Number = 0 Then
    file.WriteLine "VAL_OK"
    Err.Clear
    file.WriteLine "CSTR=" & CStr(v)
    If Err.Number <> 0 Then
        file.WriteLine "CSTR_ERR|" & Err.Number & "|" & Err.Description
        Err.Clear
    End If
    Err.Clear
    file.WriteLine "VTOS=" & app.ValueToStringInternal(v)
    If Err.Number <> 0 Then
        file.WriteLine "VTOS_ERR|" & Err.Number & "|" & Err.Description
        Err.Clear
    End If
Else
    file.WriteLine "VAL_ERR|" & Err.Number & "|" & Err.Description
    Err.Clear
End If

file.Close
