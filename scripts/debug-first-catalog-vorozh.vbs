On Error Resume Next

Dim app, catalogs, item, queryText, q, res, sel, fso, file

Set app = CreateObject("V83.Application")
app.Connect "File=""C:\blagodar"";Usr=""ВорожцовС"";Pwd=""Rv564524"";"

Set fso = CreateObject("Scripting.FileSystemObject")
Set file = fso.CreateTextFile("C:\blagodar\WarehouseAutomatisaion\debug-first-catalog-vorozh.txt", True, True)

Set catalogs = app.Metadata.Catalogs
Set item = catalogs.Get(0)
queryText = "SELECT * FROM Catalog." & CStr(item.Name)

file.WriteLine "NAME=" & CStr(item.Name)
file.WriteLine "QUERY_READY"

Set q = app.NewObject("Query")
q.Text = queryText
Set res = q.Execute()
If Err.Number <> 0 Then
    file.WriteLine "EXEC_ERROR|" & Err.Number & "|" & Err.Description
    file.Close
    WScript.Quit 1
End If

file.WriteLine "EXEC_OK"
Set sel = res.Select()
If Err.Number <> 0 Then
    file.WriteLine "SELECT_ERROR|" & Err.Number & "|" & Err.Description
    file.Close
    WScript.Quit 1
End If

If Not sel.Next() Then
    If Err.Number <> 0 Then
        file.WriteLine "NEXT_ERROR|" & Err.Number & "|" & Err.Description
    Else
        file.WriteLine "NO_ROWS"
    End If
Else
    file.WriteLine "HAS_ROW"
End If

file.Close
