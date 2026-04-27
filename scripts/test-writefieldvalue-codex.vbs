On Error Resume Next

Function SafeValueToString(app, value)
    Err.Clear
    SafeValueToString = CStr(app.ValueToStringInternal(value))
    If Err.Number <> 0 Then
        SafeValueToString = "__VALUE_ERROR__|" & Err.Number & "|" & Err.Description
        Err.Clear
    End If
End Function

Function EscapeCsv(value)
    Dim s
    s = CStr(value)
    s = Replace(s, vbCrLf, "\n")
    s = Replace(s, vbCr, "\r")
    s = Replace(s, vbLf, "\n")
    s = Replace(s, """", """""")
    EscapeCsv = """" & s & """"
End Function

Sub WriteFieldValue(app, writer, sel, index)
    Dim objValue
    Dim scalarValue
    Dim scalarText
    Dim rawText

    Set objValue = Nothing

    Err.Clear
    Set objValue = sel.Get(index)
    If Err.Number = 0 Then
        rawText = SafeValueToString(app, objValue)
        writer.Write EscapeCsv(rawText)
        Exit Sub
    End If

    Err.Clear
    scalarValue = sel.Get(index)
    If Err.Number = 0 Then
        Err.Clear
        scalarText = CStr(scalarValue)
        If Err.Number = 0 Then
            rawText = scalarText
        Else
            Err.Clear
            rawText = SafeValueToString(app, scalarValue)
        End If
    Else
        rawText = "__ERROR__|" & Err.Number & "|" & Err.Description
        Err.Clear
    End If

    writer.Write EscapeCsv(rawText)
End Sub

Dim app, catalogs, item, q, res, sel, fso, file
Set app = CreateObject("V83.Application")
app.Connect "File=""C:\blagodar"";Usr=""codex"";Pwd="""";"
Set catalogs = app.Metadata.Catalogs
Set item = catalogs.Get(417)
Set q = app.NewObject("Query")
q.Text = "SELECT * FROM Catalog." & CStr(item.Name)
Set res = q.Execute()
Set sel = res.Select()
Set fso = CreateObject("Scripting.FileSystemObject")
Set file = fso.CreateTextFile("C:\blagodar\WarehouseAutomatisaion\test-writefieldvalue-codex.csv", True, True)
If sel.Next() Then
    WriteFieldValue app, file, sel, 0
    file.Write ";"
    WriteFieldValue app, file, sel, 1
    file.WriteLine ""
End If
file.Close
