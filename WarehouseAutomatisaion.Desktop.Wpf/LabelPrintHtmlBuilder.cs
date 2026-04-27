using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;
using WarehouseAutomatisaion.Desktop.Text;
using ZXing;
using ZXing.Common;

namespace WarehouseAutomatisaion.Desktop.Wpf;

internal static class LabelPrintHtmlBuilder
{
    internal sealed record LabelCard(
        string Caption,
        string Name,
        string Badge,
        IReadOnlyList<(string Label, string Value)> Facts,
        string BarcodeValue,
        string QrPayload,
        string FooterLeft,
        string FooterRight);

    public static string Build(string title, IEnumerable<LabelCard> labels)
    {
        var cards = labels.ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"ru\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.AppendLine($"<title>{Html(Display(title))}</title>");
        builder.AppendLine("<style>");
        builder.AppendLine("body{font-family:'Segoe UI',Tahoma,Arial,sans-serif;margin:18px;color:#24201b;background:#fff;}");
        builder.AppendLine(".page-title{font-size:20px;font-weight:700;color:#2d2822;margin:0 0 12px;}");
        builder.AppendLine(".stack{display:flex;flex-direction:column;gap:18px;}");
        builder.AppendLine(".sheet{max-width:860px;margin:0 auto;border:2px solid #dccdb8;background:#fffdf8;padding:16px 18px 20px;break-inside:avoid;page-break-inside:avoid;}");
        builder.AppendLine(".title-row{display:flex;justify-content:space-between;align-items:flex-start;gap:16px;margin-bottom:10px;}");
        builder.AppendLine(".caption{font-size:20px;font-weight:700;color:#2d2822;letter-spacing:0.01em;}");
        builder.AppendLine(".status{display:inline-flex;align-items:center;padding:6px 12px;border-radius:999px;background:#f1ede6;color:#6a5b49;font-size:12px;font-weight:700;white-space:nowrap;}");
        builder.AppendLine(".meta{display:grid;grid-template-columns:minmax(0,1fr) 190px;gap:16px;align-items:start;}");
        builder.AppendLine(".name{font-size:24px;font-weight:700;line-height:1.2;color:#241f1a;margin:2px 0 10px;}");
        builder.AppendLine(".facts{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:8px;}");
        builder.AppendLine(".fact{border:1px solid #e2d8cb;background:#fff;padding:8px 10px;}");
        builder.AppendLine(".fact-label{font-size:11px;color:#7b6f60;text-transform:uppercase;letter-spacing:0.05em;margin-bottom:3px;}");
        builder.AppendLine(".fact-value{font-size:14px;font-weight:600;color:#2f2923;word-break:break-word;}");
        builder.AppendLine(".qr{border:1px solid #dfd2c0;background:#fff;padding:10px;text-align:center;}");
        builder.AppendLine(".qr img{width:168px;height:168px;object-fit:contain;}");
        builder.AppendLine(".qr-text{margin-top:8px;border-top:1px dashed #d8cab7;padding-top:8px;font-size:11px;color:#6f6559;white-space:pre-wrap;word-break:break-word;text-align:left;}");
        builder.AppendLine(".barcode-wrap{margin-top:14px;border:1px solid #dfd2c0;background:#fff;padding:12px 14px;text-align:center;}");
        builder.AppendLine(".barcode-wrap img{width:100%;height:112px;object-fit:contain;}");
        builder.AppendLine(".barcode-value{margin-top:6px;font-family:'Consolas','Courier New',monospace;font-size:18px;font-weight:700;color:#2a251f;letter-spacing:0.08em;}");
        builder.AppendLine(".footer{margin-top:10px;display:flex;justify-content:space-between;gap:16px;font-size:12px;color:#7b6f60;}");
        builder.AppendLine(".footer span:last-child{text-align:right;}");
        builder.AppendLine("@page{margin:10mm;}");
        builder.AppendLine("@media print{body{margin:0;} .page-title{display:none;} .sheet{margin:0 auto;}}");
        builder.AppendLine("@media (max-width:860px){body{margin:12px;} .sheet{padding:14px 14px 16px;} .meta{grid-template-columns:1fr;} .facts{grid-template-columns:1fr;} .qr img{width:136px;height:136px;} .footer{flex-direction:column;}}");
        builder.AppendLine("</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine($"<div class=\"page-title\">{Html(Display(title))}</div>");
        builder.AppendLine("<div class=\"stack\">");

        foreach (var card in cards)
        {
            builder.AppendLine("<section class=\"sheet\">");
            builder.AppendLine("<div class=\"title-row\">");
            builder.AppendLine($"<div class=\"caption\">{Html(Display(card.Caption))}</div>");
            builder.AppendLine($"<div class=\"status\">{Html(Display(card.Badge))}</div>");
            builder.AppendLine("</div>");
            builder.AppendLine("<div class=\"meta\">");
            builder.AppendLine("<div>");
            builder.AppendLine($"<div class=\"name\">{Html(Display(card.Name))}</div>");
            builder.AppendLine("<div class=\"facts\">");

            foreach (var fact in card.Facts)
            {
                builder.AppendLine("<div class=\"fact\">");
                builder.AppendLine($"<div class=\"fact-label\">{Html(Display(fact.Label))}</div>");
                builder.AppendLine($"<div class=\"fact-value\">{Html(Display(fact.Value))}</div>");
                builder.AppendLine("</div>");
            }

            builder.AppendLine("</div>");
            builder.AppendLine("</div>");
            builder.AppendLine("<div class=\"qr\">");
            builder.AppendLine($"<img src=\"{BuildQrCodeDataUri(card.QrPayload)}\" alt=\"QR\">");
            builder.AppendLine($"<div class=\"qr-text\">{Html(Display(card.QrPayload))}</div>");
            builder.AppendLine("</div>");
            builder.AppendLine("</div>");
            builder.AppendLine("<div class=\"barcode-wrap\">");
            builder.AppendLine($"<img src=\"{BuildBarcodeDataUri(card.BarcodeValue)}\" alt=\"Штрихкод\">");
            builder.AppendLine($"<div class=\"barcode-value\">{Html(Display(card.BarcodeValue))}</div>");
            builder.AppendLine("</div>");
            builder.AppendLine("<div class=\"footer\">");
            builder.AppendLine($"<span>{Html(Display(card.FooterLeft))}</span>");
            builder.AppendLine($"<span>{Html(Display(card.FooterRight))}</span>");
            builder.AppendLine("</div>");
            builder.AppendLine("</section>");
        }

        builder.AppendLine("</div>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return builder.ToString();
    }

    public static string BuildStableNumericCode(params string?[] parts)
    {
        var seed = string.Join("|", parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));
        if (string.IsNullOrWhiteSpace(seed))
        {
            seed = "ITEM";
        }

        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(seed));
        var digits = string.Concat(hash.Select(value => (value % 1000).ToString("000", CultureInfo.InvariantCulture)));
        return digits[..13];
    }

    private static string BuildQrCodeDataUri(string payload)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(string.IsNullOrWhiteSpace(payload) ? " " : payload.Trim(), QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(data);
        var bytes = qrCode.GetGraphic(14);
        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }

    private static string BuildBarcodeDataUri(string value)
    {
        var writer = new BarcodeWriterPixelData
        {
            Format = BarcodeFormat.CODE_128,
            Options = new EncodingOptions
            {
                Width = 720,
                Height = 170,
                Margin = 6,
                PureBarcode = true
            }
        };

        var pixelData = writer.Write(string.IsNullOrWhiteSpace(value) ? "ITEM" : value.Trim());
        var bitmap = BitmapSource.Create(pixelData.Width, pixelData.Height, 96, 96, PixelFormats.Bgra32, null, pixelData.Pixels, pixelData.Width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return $"data:image/png;base64,{Convert.ToBase64String(stream.ToArray())}";
    }

    private static string Display(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "-";
        }

        var normalized = TextMojibakeFixer.NormalizeText(value);
        return string.IsNullOrWhiteSpace(normalized) ? "-" : normalized.Trim();
    }

    private static string Html(string value)
    {
        return WebUtility.HtmlEncode(value);
    }
}
