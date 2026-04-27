On Error Resume Next

Dim app, catalogs, i, item, targetName, queryText, q, res, sel, fso, file

targetName = WScript.Arguments(0)

Set app = CreateObject("V83.Application")
app.Connect "File=""C:\blagodar"";Usr=""codex"";Pwd="""";"

Set fso = CreateObject("Scripting.FileSystemObject")
Set file = fso.CreateTextFile("C:\blagodar\WarehouseAutomatisaion\debug-query-from-metadata-codex.txt", True, True)

Set catalogs = app.Metadata.Catalogs
queryText = ""
For i = 0 To catalogs.Count() - 1
    Set item = catalogs.Get(i)
    If CStr(item.Name) = targetName Then
        queryText = "SELECT * FROM Catalog." & CStr(item.Name)
        Exit For
    End If
Next

If queryText = "" Then
    file.WriteLine "NOT_FOUND"
    file.Close
    WScript.Quit 1
End If

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
If Not sel.Next() Then
    file.WriteLine "NO_ROWS"
Else
    file.WriteLine "HAS_ROW"
End If

file.Close
