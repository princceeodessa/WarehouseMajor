using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Printing;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

internal enum SalesDocumentExportFormat
{
    Pdf,
    Excel
}

internal static class SalesDocumentExportService
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private const double A4Width = 793.7;
    private const double A4Height = 1122.5;

    public static void ExportOrder(Window? owner, SalesOrderRecord order, SalesDocumentExportFormat format)
    {
        var path = PromptExportPath(owner, $"Заказ покупателя {CleanFilePart(order.Number)}", format);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (format == SalesDocumentExportFormat.Pdf)
            {
                var document = SalesOrderPrintDocumentComposer.Build(order, A4Width, A4Height);
                PdfImageDocumentWriter.Write(document, path);
            }
            else
            {
                SimpleXlsxDocumentWriter.Write(CreateOrderWorkbook(order), path);
            }

            NotifyExportReady(owner, path);
        }
        catch (Exception exception)
        {
            ShowExportError(owner, exception);
        }
    }

    public static void ExportTableDocument(
        Window? owner,
        string suggestedName,
        PrintableTableDocumentDefinition definition,
        SalesDocumentExportFormat format)
    {
        var path = PromptExportPath(owner, suggestedName, format);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (format == SalesDocumentExportFormat.Pdf)
            {
                var document = PrintDocumentComposer.BuildTableDocument(definition, A4Width, A4Height);
                PdfImageDocumentWriter.Write(document, path);
            }
            else
            {
                SimpleXlsxDocumentWriter.Write(CreateWorkbook(definition), path);
            }

            NotifyExportReady(owner, path);
        }
        catch (Exception exception)
        {
            ShowExportError(owner, exception);
        }
    }

    private static string? PromptExportPath(Window? owner, string suggestedName, SalesDocumentExportFormat format)
    {
        var extension = format == SalesDocumentExportFormat.Pdf ? ".pdf" : ".xlsx";
        var filter = format == SalesDocumentExportFormat.Pdf
            ? "PDF (*.pdf)|*.pdf"
            : "Excel (*.xlsx)|*.xlsx";
        var dialog = new SaveFileDialog
        {
            Title = "Выгрузка документа",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            FileName = $"{CleanFilePart(suggestedName)}{extension}",
            DefaultExt = extension,
            AddExtension = true,
            Filter = filter,
            OverwritePrompt = true
        };

        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    private static ExportWorkbookDocument CreateOrderWorkbook(SalesOrderRecord order)
    {
        var lines = order.Lines.ToArray();
        return new ExportWorkbookDocument(
            SalesDocumentPrintComposer.BuildOrderTitle(order),
            $"Заказчик: {Clean(order.CustomerName)}",
            [
                new ExportField("Заказчик", Clean(order.CustomerName)),
                new ExportField("Код клиента", Clean(order.CustomerCode)),
                new ExportField("Дата заказа", order.OrderDate.ToString("dd.MM.yyyy", RuCulture)),
                new ExportField("Склад", Clean(order.Warehouse)),
                new ExportField("Договор", Clean(order.ContractNumber)),
                new ExportField("Менеджер", Clean(order.Manager)),
                new ExportField("Статус", Clean(order.Status)),
                new ExportField("Валюта", Clean(order.CurrencyCode))
            ],
            ["№", "Дата", "Товары (работы, услуги)", "Код", "Кол-во", "Ед.", "Цена", "Сумма"],
            lines.Select((line, index) => new ExportRow(
            [
                (index + 1).ToString("N0", RuCulture),
                order.OrderDate.ToString("dd.MM.yy", RuCulture),
                Clean(line.ItemName),
                Clean(line.ItemCode),
                FormatQuantity(line.Quantity),
                Clean(line.Unit),
                FormatMoney(line.Price),
                FormatMoney(line.Amount)
            ])).ToArray(),
            BuildTotals(order.SubtotalAmount, order.EffectiveDiscountAmount, order.TotalAmount),
            order.Comment);
    }

    private static ExportWorkbookDocument CreateWorkbook(PrintableTableDocumentDefinition definition)
    {
        return new ExportWorkbookDocument(
            Clean(definition.Title),
            Clean(definition.Subtitle),
            definition.Facts.Select(item => new ExportField(Clean(item.Label), Clean(item.Value))).ToArray(),
            definition.Columns.Select(item => Clean(item.Header)).ToArray(),
            definition.Rows.Select(item => new ExportRow(item.Cells.Select(Clean).ToArray())).ToArray(),
            definition.Totals.Select(item => new ExportField(Clean(item.Label), Clean(item.Value))).ToArray(),
            definition.Comment);
    }

    private static IReadOnlyList<ExportField> BuildTotals(decimal subtotal, decimal discount, decimal total)
    {
        var totals = new List<ExportField>();
        if (discount > 0m)
        {
            totals.Add(new ExportField("Сумма без скидки", FormatMoney(subtotal)));
            totals.Add(new ExportField("Скидка", FormatMoney(discount)));
        }

        totals.Add(new ExportField("Итого", FormatMoney(total)));
        totals.Add(new ExportField("Без налога (НДС)", string.Empty));
        return totals;
    }

    private static void NotifyExportReady(Window? owner, string path)
    {
        var result = MessageBox.Show(
            owner,
            $"Документ выгружен.{Environment.NewLine}{path}{Environment.NewLine}{Environment.NewLine}Открыть папку с файлом?",
            AppBranding.MessageBoxTitle,
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{path}\"",
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private static void ShowExportError(Window? owner, Exception exception)
    {
        MessageBox.Show(
            owner,
            $"Не удалось выгрузить документ.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
            AppBranding.MessageBoxTitle,
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }

    private static string Clean(string? value)
    {
        var normalized = TextMojibakeFixer.NormalizeText(value);
        return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized.Trim();
    }

    private static string CleanFilePart(string? value)
    {
        var cleaned = Clean(value);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            cleaned = "document";
        }

        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            cleaned = cleaned.Replace(invalidChar, '-');
        }

        return cleaned.Trim();
    }

    private static string FormatMoney(decimal value)
    {
        return value.ToString("N2", RuCulture);
    }

    private static string FormatQuantity(decimal value)
    {
        return value.ToString("#,0.##", RuCulture);
    }

    private sealed record ExportWorkbookDocument(
        string Title,
        string Subtitle,
        IReadOnlyList<ExportField> Facts,
        IReadOnlyList<string> Columns,
        IReadOnlyList<ExportRow> Rows,
        IReadOnlyList<ExportField> Totals,
        string Comment);

    private sealed record ExportField(string Label, string Value);

    private sealed record ExportRow(IReadOnlyList<string> Cells);

    private static class PdfImageDocumentWriter
    {
        public static void Write(FlowDocument document, string path)
        {
            var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
            paginator.ComputePageCount();
            var pages = new List<PdfPageImage>();
            for (var pageIndex = 0; pageIndex < paginator.PageCount; pageIndex++)
            {
                pages.Add(RenderPage(paginator.GetPage(pageIndex)));
            }

            WritePdf(path, pages);
        }

        private static PdfPageImage RenderPage(DocumentPage page)
        {
            const double scale = 2d;
            var width = Math.Max(1, (int)Math.Ceiling(page.Size.Width * scale));
            var height = Math.Max(1, (int)Math.Ceiling(page.Size.Height * scale));
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, page.Size.Width, page.Size.Height));
                context.DrawRectangle(new VisualBrush(page.Visual), null, new Rect(0, 0, page.Size.Width, page.Size.Height));
            }

            var bitmap = new RenderTargetBitmap(width, height, 96d * scale, 96d * scale, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var encoder = new JpegBitmapEncoder { QualityLevel = 88 };
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return new PdfPageImage(stream.ToArray(), width, height, page.Size.Width * 0.75d, page.Size.Height * 0.75d);
        }

        private static void WritePdf(string path, IReadOnlyList<PdfPageImage> pages)
        {
            using var stream = File.Create(path);
            using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
            var offsets = new List<long> { 0 };
            var pageObjectIds = new List<int>();
            var nextObjectId = 3;
            var imageObjects = new List<(int ImageId, int ContentId, int PageId, PdfPageImage Page)>();
            foreach (var page in pages)
            {
                var imageId = nextObjectId++;
                var contentId = nextObjectId++;
                var pageId = nextObjectId++;
                pageObjectIds.Add(pageId);
                imageObjects.Add((imageId, contentId, pageId, page));
            }

            WriteAscii(writer, "%PDF-1.4\n%\n");
            WriteObject(writer, offsets, 1, "<< /Type /Catalog /Pages 2 0 R >>");
            WriteObject(writer, offsets, 2, $"<< /Type /Pages /Count {pages.Count} /Kids [{string.Join(' ', pageObjectIds.Select(id => $"{id} 0 R"))}] >>");

            foreach (var item in imageObjects)
            {
                offsets.Add(stream.Position);
                WriteAscii(writer, $"{item.ImageId} 0 obj\n");
                WriteAscii(writer, $"<< /Type /XObject /Subtype /Image /Width {item.Page.PixelWidth} /Height {item.Page.PixelHeight} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {item.Page.JpegBytes.Length} >>\nstream\n");
                writer.Write(item.Page.JpegBytes);
                WriteAscii(writer, "\nendstream\nendobj\n");

                var content = $"q\n{item.Page.PageWidth.ToString("0.###", CultureInfo.InvariantCulture)} 0 0 {item.Page.PageHeight.ToString("0.###", CultureInfo.InvariantCulture)} 0 0 cm\n/Im0 Do\nQ\n";
                WriteObject(writer, offsets, item.ContentId, $"<< /Length {Encoding.ASCII.GetByteCount(content)} >>\nstream\n{content}endstream");
                WriteObject(
                    writer,
                    offsets,
                    item.PageId,
                    $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {item.Page.PageWidth.ToString("0.###", CultureInfo.InvariantCulture)} {item.Page.PageHeight.ToString("0.###", CultureInfo.InvariantCulture)}] /Resources << /XObject << /Im0 {item.ImageId} 0 R >> >> /Contents {item.ContentId} 0 R >>");
            }

            var xrefPosition = stream.Position;
            WriteAscii(writer, $"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
            for (var index = 1; index < offsets.Count; index++)
            {
                WriteAscii(writer, $"{offsets[index]:0000000000} 00000 n \n");
            }

            WriteAscii(writer, $"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xrefPosition}\n%%EOF");
        }

        private static void WriteObject(BinaryWriter writer, ICollection<long> offsets, int id, string body)
        {
            offsets.Add(writer.BaseStream.Position);
            WriteAscii(writer, $"{id} 0 obj\n{body}\nendobj\n");
        }

        private static void WriteAscii(BinaryWriter writer, string value)
        {
            writer.Write(Encoding.ASCII.GetBytes(value));
        }

        private sealed record PdfPageImage(byte[] JpegBytes, int PixelWidth, int PixelHeight, double PageWidth, double PageHeight);
    }

    private static class SimpleXlsxDocumentWriter
    {
        public static void Write(ExportWorkbookDocument document, string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
            AddText(archive, "[Content_Types].xml", ContentTypesXml);
            AddText(archive, "_rels/.rels", RootRelsXml);
            AddText(archive, "xl/workbook.xml", WorkbookXml);
            AddText(archive, "xl/_rels/workbook.xml.rels", WorkbookRelsXml);
            AddText(archive, "xl/styles.xml", StylesXml);
            AddText(archive, "xl/worksheets/sheet1.xml", BuildWorksheetXml(document));
        }

        private static string BuildWorksheetXml(ExportWorkbookDocument document)
        {
            var rows = new List<IReadOnlyList<string>>();
            rows.Add([document.Title]);
            if (!string.IsNullOrWhiteSpace(document.Subtitle))
            {
                rows.Add([document.Subtitle]);
            }

            rows.Add([]);
            foreach (var fact in document.Facts)
            {
                rows.Add([fact.Label, fact.Value]);
            }

            rows.Add([]);
            var tableHeaderRowIndex = rows.Count;
            rows.Add(document.Columns);
            rows.AddRange(document.Rows.Select(item => item.Cells));
            rows.Add([]);
            foreach (var total in document.Totals)
            {
                rows.Add([total.Label, total.Value]);
            }

            if (!string.IsNullOrWhiteSpace(document.Comment))
            {
                rows.Add([]);
                rows.Add(["Комментарий", document.Comment]);
            }

            var builder = new StringBuilder();
            builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            builder.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews><cols>");
            for (var index = 1; index <= Math.Max(8, document.Columns.Count); index++)
            {
                var width = index == 3 ? 44 : 16;
                builder.Append(CultureInfo.InvariantCulture, $"<col min=\"{index}\" max=\"{index}\" width=\"{width}\" customWidth=\"1\"/>");
            }

            builder.Append("</cols><sheetData>");
            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var excelRow = rowIndex + 1;
                builder.Append(CultureInfo.InvariantCulture, $"<row r=\"{excelRow}\">");
                var cells = rows[rowIndex];
                for (var columnIndex = 0; columnIndex < cells.Count; columnIndex++)
                {
                    var reference = $"{ColumnName(columnIndex + 1)}{excelRow}";
                    var style = rowIndex == 0 || rowIndex == tableHeaderRowIndex ? " s=\"1\"" : string.Empty;
                    builder.Append(CultureInfo.InvariantCulture, $"<c r=\"{reference}\" t=\"inlineStr\"{style}><is><t>{Xml(cells[columnIndex])}</t></is></c>");
                }

                builder.Append("</row>");
            }

            builder.Append("</sheetData></worksheet>");
            return builder.ToString();
        }

        private static void AddText(ZipArchive archive, string path, string content)
        {
            var entry = archive.CreateEntry(path, CompressionLevel.Optimal);
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.Write(content);
        }

        private static string ColumnName(int index)
        {
            var name = string.Empty;
            while (index > 0)
            {
                index--;
                name = (char)('A' + index % 26) + name;
                index /= 26;
            }

            return name;
        }

        private static string Xml(string? value)
        {
            return System.Security.SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
        }

        private const string ContentTypesXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>";
        private const string RootRelsXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>";
        private const string WorkbookXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Документ\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";
        private const string WorkbookRelsXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>";
        private const string StylesXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?><styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"12\"/><name val=\"Calibri\"/></font></fonts><fills count=\"1\"><fill><patternFill patternType=\"none\"/></fill></fills><borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs></styleSheet>";
    }
}
