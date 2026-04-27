using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using QRCoder;
using WarehouseAutomatisaion.Desktop.Data;
using ZXing;
using ZXing.Common;

namespace WarehouseAutomatisaion.Desktop.Printing;

public static class CatalogLabelPrintComposer
{
    public static string BuildItemLabelHtml(CatalogItemRecord item)
    {
        var barcodeFormat = NormalizeBarcodeFormat(item.BarcodeFormat);
        var barcodeValue = PrepareBarcodeValue(item.BarcodeValue, item, barcodeFormat);
        var qrPayload = string.IsNullOrWhiteSpace(item.QrPayload)
            ? BuildFallbackQrPayload(item)
            : item.QrPayload.Trim();

        var qrDataUri = BuildQrCodeDataUri(qrPayload);
        var barcodeDataUri = BuildBarcodeDataUri(barcodeValue, barcodeFormat);

        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"ru\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<title>Этикетка номенклатуры</title>");
        builder.AppendLine("<style>");
        builder.AppendLine("body{font-family:'Segoe UI',Tahoma,sans-serif;margin:18px;color:#24201b;background:#fff;}");
        builder.AppendLine(".sheet{max-width:860px;margin:0 auto;border:2px solid #dccdb8;background:#fffdf8;padding:16px 18px 20px;}");
        builder.AppendLine(".title{font-size:20px;font-weight:700;color:#2d2822;letter-spacing:0.01em;margin-bottom:10px;}");
        builder.AppendLine(".meta{display:grid;grid-template-columns:minmax(0,1fr) 190px;gap:16px;align-items:start;}");
        builder.AppendLine(".name{font-size:24px;font-weight:700;line-height:1.2;color:#241f1a;margin:2px 0 10px;}");
        builder.AppendLine(".facts{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:8px;}");
        builder.AppendLine(".fact{border:1px solid #e2d8cb;background:#fff;padding:8px 10px;}");
        builder.AppendLine(".fact-label{font-size:11px;color:#7b6f60;text-transform:uppercase;letter-spacing:0.05em;margin-bottom:3px;}");
        builder.AppendLine(".fact-value{font-size:14px;font-weight:600;color:#2f2923;}");
        builder.AppendLine(".qr{border:1px solid #dfd2c0;background:#fff;padding:10px;text-align:center;}");
        builder.AppendLine(".qr img{width:168px;height:168px;object-fit:contain;}");
        builder.AppendLine(".barcode-wrap{margin-top:14px;border:1px solid #dfd2c0;background:#fff;padding:12px 14px;text-align:center;}");
        builder.AppendLine(".barcode-wrap img{width:100%;height:112px;object-fit:contain;}");
        builder.AppendLine(".barcode-value{margin-top:6px;font-family:'Consolas','Courier New',monospace;font-size:18px;font-weight:700;color:#2a251f;letter-spacing:0.08em;}");
        builder.AppendLine(".footer{margin-top:10px;display:flex;justify-content:space-between;font-size:12px;color:#7b6f60;}");
        builder.AppendLine(".qr-text{margin-top:8px;border-top:1px dashed #d8cab7;padding-top:8px;font-size:11px;color:#6f6559;white-space:pre-wrap;word-break:break-word;text-align:left;}");
        builder.AppendLine("</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<div class=\"sheet\">");
        builder.AppendLine("<div class=\"title\">Этикетка номенклатуры</div>");
        builder.AppendLine("<div class=\"meta\">");
        builder.AppendLine("<div>");
        builder.AppendLine("<div class=\"name\">" + Encode(EmptyAsDash(item.Name)) + "</div>");
        builder.AppendLine("<div class=\"facts\">");
        AppendFact(builder, "Код", item.Code);
        AppendFact(builder, "Категория", item.Category);
        AppendFact(builder, "Единица", item.Unit);
        AppendFact(builder, "Склад", item.DefaultWarehouse);
        AppendFact(builder, "Цена", BuildPriceLabel(item));
        AppendFact(builder, "Поставщик", item.Supplier);
        AppendFact(builder, "Статус", item.Status);
        AppendFact(builder, "Формат штрихкода", BarcodeFormatToLabel(barcodeFormat));
        builder.AppendLine("</div>");
        builder.AppendLine("</div>");
        builder.AppendLine("<div class=\"qr\">");
        builder.AppendLine("<img src=\"" + qrDataUri + "\" alt=\"QR\">");
        builder.AppendLine("<div class=\"qr-text\">" + Encode(qrPayload) + "</div>");
        builder.AppendLine("</div>");
        builder.AppendLine("</div>");
        builder.AppendLine("<div class=\"barcode-wrap\">");
        builder.AppendLine("<img src=\"" + barcodeDataUri + "\" alt=\"Штрихкод\">");
        builder.AppendLine("<div class=\"barcode-value\">" + Encode(barcodeValue) + "</div>");
        builder.AppendLine("</div>");
        builder.AppendLine("<div class=\"footer\">");
        builder.AppendLine("<span>Источник: " + Encode(EmptyAsDash(item.SourceLabel)) + "</span>");
        builder.AppendLine("<span>Сформировано: " + Encode(DateTime.Now.ToString("dd.MM.yyyy HH:mm")) + "</span>");
        builder.AppendLine("</div>");
        builder.AppendLine("</div>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    private static void AppendFact(StringBuilder builder, string caption, string value)
    {
        builder.AppendLine("<div class=\"fact\">");
        builder.AppendLine("<div class=\"fact-label\">" + Encode(caption) + "</div>");
        builder.AppendLine("<div class=\"fact-value\">" + Encode(EmptyAsDash(value)) + "</div>");
        builder.AppendLine("</div>");
    }

    private static string BuildPriceLabel(CatalogItemRecord item)
    {
        return item.DefaultPrice <= 0m ? "-" : $"{item.DefaultPrice:N2} {item.CurrencyCode}";
    }

    private static string BuildQrCodeDataUri(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(data);
        var bytes = qrCode.GetGraphic(14);
        return BuildDataUri(bytes);
    }

    private static string BuildBarcodeDataUri(string value, BarcodeFormat format)
    {
        try
        {
            return BuildDataUri(RenderBarcodePng(value, format));
        }
        catch
        {
            var fallback = PrepareBarcodeValue(value, null, BarcodeFormat.CODE_128);
            return BuildDataUri(RenderBarcodePng(fallback, BarcodeFormat.CODE_128));
        }
    }

    private static byte[] RenderBarcodePng(string value, BarcodeFormat format)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = format,
            Options = new EncodingOptions
            {
                Width = 720,
                Height = 170,
                Margin = 6,
                PureBarcode = true
            }
        };

        var pixelData = writer.Write(value);
        using var bitmap = new Bitmap(pixelData.Width, pixelData.Height, PixelFormat.Format32bppArgb);
        var rectangle = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
        var data = bitmap.LockBits(rectangle, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            Marshal.Copy(pixelData.Pixels, 0, data.Scan0, pixelData.Pixels.Length);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    private static string BuildDataUri(byte[] bytes)
    {
        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }

    private static BarcodeFormat NormalizeBarcodeFormat(string? value)
    {
        return string.Equals(value, "Code39", StringComparison.OrdinalIgnoreCase)
            ? BarcodeFormat.CODE_39
            : BarcodeFormat.CODE_128;
    }

    private static string BarcodeFormatToLabel(BarcodeFormat format)
    {
        return format == BarcodeFormat.CODE_39 ? "Code39" : "Code128";
    }

    private static string PrepareBarcodeValue(string? rawValue, CatalogItemRecord? item, BarcodeFormat format)
    {
        var source = !string.IsNullOrWhiteSpace(rawValue)
            ? rawValue.Trim()
            : item is null
                ? "ITEM"
                : BuildFallbackBarcodeValue(item);

        if (format == BarcodeFormat.CODE_39)
        {
            source = source.ToUpperInvariant();
            source = new string(source
                .Select(ch => IsCode39Char(ch) ? ch : '-')
                .ToArray())
                .Trim('-');
        }

        return string.IsNullOrWhiteSpace(source) ? "ITEM" : source;
    }

    private static bool IsCode39Char(char value)
    {
        return char.IsLetterOrDigit(value) || value is '-' or '.' or '$' or '/' or '+' or '%' or ' ';
    }

    private static string BuildFallbackBarcodeValue(CatalogItemRecord item)
    {
        if (!string.IsNullOrWhiteSpace(item.Code))
        {
            return item.Code.Trim().ToUpperInvariant();
        }

        if (!string.IsNullOrWhiteSpace(item.Name))
        {
            var raw = new string(item.Name.Trim().ToUpperInvariant().Where(ch => !char.IsWhiteSpace(ch)).ToArray());
            return string.IsNullOrWhiteSpace(raw) ? $"ITEM-{item.Id:N}"[..13] : raw;
        }

        return $"ITEM-{item.Id:N}"[..13];
    }

    private static string BuildFallbackQrPayload(CatalogItemRecord item)
    {
        var lines = new List<string>
        {
            $"Код: {EmptyAsDash(item.Code)}",
            $"Номенклатура: {EmptyAsDash(item.Name)}"
        };

        if (!string.IsNullOrWhiteSpace(item.Category))
        {
            lines.Add($"Категория: {item.Category}");
        }

        if (item.DefaultPrice > 0m)
        {
            lines.Add($"Цена: {item.DefaultPrice:N2} {item.CurrencyCode}");
        }

        if (!string.IsNullOrWhiteSpace(item.DefaultWarehouse))
        {
            lines.Add($"Склад: {item.DefaultWarehouse}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string EmptyAsDash(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private static string Encode(string value)
    {
        return System.Net.WebUtility.HtmlEncode(value);
    }
}
