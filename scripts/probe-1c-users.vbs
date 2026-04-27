On Error Resume Next

Dim users, i, app, cs
users = Array("", "?????????????", "Administrator", "admin", "?????")

For i = 0 To UBound(users)
    Err.Clear
    Set app = CreateObject("V83.Application")
    If Err.Number <> 0 Then
        WScript.Echo "CREATE_ERROR|" & Err.Number & "|" & Err.Description
        WScript.Quit 1
    End If

    cs = "File=""C:\blagodar\WarehouseAutomatisaion\restored_1c_db"";"
    If users(i) <> "" Then
        cs = cs & "Usr=""" & users(i) & """;Pwd="""";"
    End If

    Err.Clear
    app.Connect cs
    If Err.Number = 0 Then
        WScript.Echo "SUCCESS|" & users(i)
        WScript.Quit 0
    Else
        WScript.Echo "FAIL|" & users(i) & "|" & Err.Number & "|" & Err.Description
    End If
Next

WScript.Quit 1
