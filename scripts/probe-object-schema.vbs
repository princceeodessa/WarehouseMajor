On Error Resume Next

Function AsText(app, sel, idx)
    On Error Resume Next
    Dim objValue, scalarValue, scalarText
    Set objValue = Nothing

    Err.Clear
    Set objValue = sel.Get(idx)
    If Err.Number = 0 Then
        AsText = app.ValueToStringInternal(objValue)
        Exit Function
    End If

    Err.Clear
    scalarValue = sel.Get(idx)
    If Err.Number = 0 Then
        scalarText = CStr(scalarValue)
        If Err.Number = 0 Then
            AsText = scalarText
        Else
            Err.Clear
            AsText = app.ValueToStringInternal(scalarValue)
        End If
    Else
        AsText = "<ERR " & Err.Number & ": " & Err.Description & ">"
        Err.Clear
    End If
End Function

Dim kind, idx, outputPath, app, md, coll, item, q, res, sel, fso, file, i, ts, j, prefix, sectionRes, sectionSel
kind = LCase(WScript.Arguments(0))
idx = CLng(WScript.Arguments(1))
outputPath = WScript.Arguments(2)

Set app = CreateObject("V83.Application")
app.Connect "File=""C:\blagodar"";Usr=""codex"";Pwd="""";"
Set md = app.Metadata

If kind = "document" Then
    Set coll = md.Documents
    prefix = "Document."
ElseIf kind = "catalog" Then
    Set coll = md.Catalogs
    prefix = "Catalog."
Else
    WScript.Echo "BAD_KIND"
    WScript.Quit 1
End If

Set item = coll.Get(idx)
Set fso = CreateObject("Scripting.FileSystemObject")
Set file = fso.CreateTextFile(outputPath, True, True)

file.WriteLine "OBJECT|" & kind & "|" & idx & "|" & item.Name
Set q = app.NewObject("Query")
q.Text = "SELECT TOP 1 * FROM " & prefix & item.Name
Set res = q.Execute()
If Err.Number <> 0 Then
    file.WriteLine "EXEC_ERROR|" & Err.Number & "|" & Err.Description
    file.Close
    WScript.Quit 1
End If

file.WriteLine "COLUMNS|" & res.Columns.Count()
For i = 0 To res.Columns.Count() - 1
    file.WriteLine "COL|" & i & "|" & res.Columns.Get(i).Name
Next
Set sel = res.Select()
If sel.Next() Then
    file.WriteLine "ROW|0"
    For i = 0 To res.Columns.Count() - 1
        file.WriteLine "VAL|" & res.Columns.Get(i).Name & "|" & AsText(app, sel, i)
    Next
Else
    file.WriteLine "NO_ROWS"
End If

file.WriteLine "TABULAR|" & item.TabularSections.Count()
For j = 0 To item.TabularSections.Count() - 1
    Set ts = item.TabularSections.Get(j)
    file.WriteLine "TS|" & j & "|" & ts.Name
    Set q = app.NewObject("Query")
    q.Text = "SELECT TOP 1 * FROM " & prefix & item.Name & "." & ts.Name
    Set sectionRes = q.Execute()
    If Err.Number <> 0 Then
        file.WriteLine "TS_EXEC_ERROR|" & ts.Name & "|" & Err.Number & "|" & Err.Description
        Err.Clear
    Else
        file.WriteLine "TS_COLUMNS|" & ts.Name & "|" & sectionRes.Columns.Count()
        For i = 0 To sectionRes.Columns.Count() - 1
            file.WriteLine "TS_COL|" & ts.Name & "|" & i & "|" & sectionRes.Columns.Get(i).Name
        Next
        Set sectionSel = sectionRes.Select()
        If sectionSel.Next() Then
            file.WriteLine "TS_ROW|" & ts.Name & "|0"
            For i = 0 To sectionRes.Columns.Count() - 1
                file.WriteLine "TS_VAL|" & ts.Name & "|" & sectionRes.Columns.Get(i).Name & "|" & AsText(app, sectionSel, i)
            Next
        Else
            file.WriteLine "TS_NO_ROWS|" & ts.Name
        End If
    End If
Next

file.Close
