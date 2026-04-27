On Error Resume Next

Dim app, catalogs, item, q, res, sel, fso, file, v

Set app = CreateObject("V83.Application")
app.Connect "File=""C:\blagodar"";Usr=""codex"";Pwd="""";"

Set catalogs = app.Metadata.Catalogs
Set item = catalogs.Get(417)

Set q = app.NewObject("Query")
q.Text = "SELECT * FROM Catalog." & CStr(item.Name)
Set res = q.Execute()
Set sel = res.Select()

Set fso = CreateObject("Scripting.FileSystemObject")
Set file = fso.CreateTextFile("C:\blagodar\WarehouseAutomatisaion\test-users-twofields-csv-codex.csv", True, True)
file.WriteLine """Ссылка"";""ВерсияДанных"""

If sel.Next() Then
    file.Write """" & Replace(app.ValueToStringInternal(sel.Get(0)), """", """""") & """"
    file.Write ";"
    v = sel.Get(1)
    file.Write """" & Replace(CStr(v), """", """""") & """"
    file.WriteLine ""
End If

file.Close
