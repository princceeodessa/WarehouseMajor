On Error Resume Next

Dim app, md, docs, item, i, fso, file
Set app = CreateObject("V83.Application")
app.Connect "File=""C:\blagodar"";Usr=""codex"";Pwd="""";"
If Err.Number <> 0 Then
    WScript.Echo "CONNECT_ERROR|" & Err.Number & "|" & Err.Description
    WScript.Quit 1
End If

Set md = app.Metadata
Set docs = md.Documents

Set fso = CreateObject("Scripting.FileSystemObject")
Set file = fso.CreateTextFile("C:\blagodar\WarehouseAutomatisaion\document-names.txt", True, True)

For i = 0 To docs.Count() - 1
    Set item = docs.Get(i)
    file.WriteLine i & "|" & item.Name
Next

file.Close
