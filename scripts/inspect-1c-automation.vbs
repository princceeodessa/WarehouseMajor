On Error Resume Next

Dim app
Set app = CreateObject("V83.Application")
If Err.Number <> 0 Then
    WScript.Echo "CREATE_ERROR|" & Err.Number & "|" & Err.Description
    WScript.Quit 1
End If

app.Connect "File=""C:\blagodar\WarehouseAutomatisaion\restored_1c_db"";"
If Err.Number <> 0 Then
    WScript.Echo "CONNECT_ERROR|" & Err.Number & "|" & Err.Description
    WScript.Quit 1
End If

WScript.Echo "CONNECTED|" & TypeName(app)
