On Error Resume Next

Dim connector
Dim connection

Set connector = CreateObject("V83.COMConnector")
If Err.Number <> 0 Then
    WScript.Echo "CREATE_ERROR|" & Err.Number & "|" & Err.Description
    WScript.Quit 1
End If

Set connection = connector.Connect("File=""C:\blagodar\WarehouseAutomatisaion\restored_1c_db"";")
If Err.Number <> 0 Then
    WScript.Echo "CONNECT_ERROR|" & Err.Number & "|" & Err.Description
    WScript.Quit 1
End If

WScript.Echo "CONNECTED|" & TypeName(connection)
