using System.Globalization;
using System.Text;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Printing;

public static class SalesDocumentPrintComposer
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    public static string BuildOrderHtml(SalesOrderRecord order)
    {
        var title = $"Заказ покупателя № {Display(order.Number)} от {FormatLongDate(order.OrderDate)}";
        var lines = order.Lines.ToArray();
        var builder = new StringBuilder();

        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"ru\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.AppendLine("<title>" + Encode(title) + "</title>");
        builder.AppendLine("<style>");
        builder.AppendLine("@page{size:A4;margin:7mm 8mm;}");
        builder.AppendLine("body{font-family:Arial,'Segoe UI',Tahoma,sans-serif;margin:0;color:#202020;background:#fff;font-size:12px;}");
        builder.AppendLine(".doc{max-width:1040px;margin:0 auto;}");
        builder.AppendLine(".title{font-size:22px;font-weight:700;line-height:1.1;margin:0 0 14px;padding:6px 6px 7px;border-top:1px solid #666;border-bottom:2px solid #333;}");
        builder.AppendLine(".transfer{margin:0 0 18px 4px;color:#303030;}");
        builder.AppendLine(".party{display:grid;grid-template-columns:105px 1fr;gap:8px;margin:0 0 10px 4px;align-items:baseline;font-size:15px;}");
        builder.AppendLine(".party .value{font-weight:700;}");
        builder.AppendLine("table{width:100%;border-collapse:collapse;table-layout:fixed;margin-top:12px;}");
        builder.AppendLine("th,td{border:1px solid #222;padding:2px 4px;vertical-align:top;}");
        builder.AppendLine("th{font-size:15px;font-weight:700;text-align:center;line-height:1.15;}");
        builder.AppendLine("td{font-size:12px;line-height:1.14;}");
        builder.AppendLine(".center{text-align:center;}.num{text-align:right;white-space:nowrap;}.item{word-break:break-word;}.empty{text-align:center;padding:14px;color:#606060;}");
        builder.AppendLine(".summary{display:grid;grid-template-columns:1fr 260px;gap:24px;margin-top:8px;align-items:start;}");
        builder.AppendLine(".total-box{font-size:15px;font-weight:700;}");
        builder.AppendLine(".total-row{display:grid;grid-template-columns:1fr auto;gap:18px;margin:2px 0;}");
        builder.AppendLine(".total-row .label{text-align:right;}.total-row .value{text-align:right;min-width:96px;}");
        builder.AppendLine(".result{margin-top:22px;font-size:13px;line-height:1.45;}");
        builder.AppendLine(".result strong{font-size:15px;}");
        builder.AppendLine(".signatures{margin-top:28px;border-top:2px solid #333;padding-top:16px;}");
        builder.AppendLine(".signature-row{display:grid;grid-template-columns:112px 180px 1fr;gap:28px;align-items:end;margin-top:14px;}");
        builder.AppendLine(".signature-title{font-size:15px;font-weight:700;}");
        builder.AppendLine(".signature-line{border-bottom:1px solid #333;min-height:24px;position:relative;text-align:center;}");
        builder.AppendLine(".signature-line small{position:absolute;left:0;right:0;bottom:-12px;font-size:8px;font-weight:400;color:#333;}");
        builder.AppendLine(".signature-name{font-size:12px;padding-bottom:3px;}");
        builder.AppendLine("@media print{body{margin:0;}.doc{max-width:none;}.title{margin-top:0;}}");
        builder.AppendLine("</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<main class=\"doc\">");
        builder.AppendLine("<h1 class=\"title\">" + Encode(title) + "</h1>");
        builder.AppendLine("<div class=\"transfer\">Карта для перевода:</div>");
        builder.AppendLine("<div class=\"party\"><div>Исполнитель:</div><div class=\"value\">ИП</div></div>");
        builder.AppendLine("<div class=\"party\"><div>Заказчик:</div><div class=\"value\">" + Encode(Display(order.CustomerName)) + "</div></div>");
        builder.AppendLine("<table>");
        builder.AppendLine("<thead><tr><th style=\"width:5%\">№</th><th style=\"width:8%\">Дата</th><th style=\"width:38%\">Товары (работы, услуги)</th><th style=\"width:10%\">Код</th><th style=\"width:8%\">Кол-во</th><th style=\"width:6%\">Ед.</th><th style=\"width:12%\">Цена</th><th style=\"width:13%\">Сумма</th></tr></thead>");
        builder.AppendLine("<tbody>");

        if (lines.Length == 0)
        {
            builder.AppendLine("<tr><td class=\"empty\" colspan=\"8\">Нет позиций</td></tr>");
        }
        else
        {
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                builder.AppendLine("<tr>");
                builder.AppendLine("<td class=\"center\">" + (index + 1).ToString(RuCulture) + "</td>");
                builder.AppendLine("<td></td>");
                builder.AppendLine("<td class=\"item\">" + Encode(Display(line.ItemName)) + "</td>");
                builder.AppendLine("<td>" + Encode(Display(line.ItemCode)) + "</td>");
                builder.AppendLine("<td class=\"num\">" + Encode(FormatQuantity(line.Quantity)) + "</td>");
                builder.AppendLine("<td>" + Encode(Display(line.Unit)) + "</td>");
                builder.AppendLine("<td class=\"num\">" + Encode(FormatMoney(line.Price)) + "</td>");
                builder.AppendLine("<td class=\"num\">" + Encode(FormatMoney(line.Amount)) + "</td>");
                builder.AppendLine("</tr>");
            }
        }

        builder.AppendLine("</tbody>");
        builder.AppendLine("</table>");
        builder.AppendLine("<section class=\"summary\">");
        builder.AppendLine("<div class=\"result\">");
        builder.AppendLine("Всего наименований " + lines.Length.ToString("N0", RuCulture) + ", на сумму " + Encode(FormatMoney(order.TotalAmount)) + " руб.<br>");
        builder.AppendLine("<strong>" + Encode(AmountToWords(order.TotalAmount)) + "</strong>");
        builder.AppendLine("</div>");
        builder.AppendLine("<div class=\"total-box\">");
        builder.AppendLine("<div class=\"total-row\"><span class=\"label\">Итого:</span><span class=\"value\">" + Encode(FormatMoney(order.TotalAmount)) + "</span></div>");
        builder.AppendLine("<div class=\"total-row\"><span class=\"label\">Без налога (НДС)</span><span class=\"value\"></span></div>");
        builder.AppendLine("</div>");
        builder.AppendLine("</section>");
        builder.AppendLine("<section class=\"signatures\">");
        builder.AppendLine(BuildSignatureRow("Исполнитель", Display(order.Manager)));
        builder.AppendLine(BuildSignatureRow("Заказчик", string.Empty));
        builder.AppendLine("</section>");
        builder.AppendLine("</main>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");

        return TextMojibakeFixer.NormalizeText(builder.ToString());
    }

    public static string BuildInvoiceHtml(SalesInvoiceRecord invoice)
    {
        return BuildDocumentHtml(
            "Счет на оплату",
            invoice.Number,
            invoice.InvoiceDate,
            [
                ("Покупатель", $"{invoice.CustomerName} [{invoice.CustomerCode}]"),
                ("Дата", invoice.InvoiceDate.ToString("dd.MM.yyyy")),
                ("Основание", invoice.SalesOrderNumber),
                ("Договор", invoice.ContractNumber),
                ("Оплатить до", invoice.DueDate.ToString("dd.MM.yyyy")),
                ("Менеджер", invoice.Manager),
                ("Валюта", invoice.CurrencyCode),
                ("Статус", invoice.Status),
                ("Номер", invoice.Number)
            ],
            invoice.Lines.Select(line => new PrintLine(
                line.ItemCode,
                line.ItemName,
                line.Unit,
                line.Quantity,
                line.Price,
                line.Amount)),
            invoice.TotalAmount,
            invoice.Comment);
    }

    public static string BuildShipmentHtml(SalesShipmentRecord shipment)
    {
        return BuildDocumentHtml(
            "Расходная накладная",
            shipment.Number,
            shipment.ShipmentDate,
            [
                ("Покупатель", $"{shipment.CustomerName} [{shipment.CustomerCode}]"),
                ("Дата", shipment.ShipmentDate.ToString("dd.MM.yyyy")),
                ("Основание", shipment.SalesOrderNumber),
                ("Склад", shipment.Warehouse),
                ("Статус", shipment.Status),
                ("Перевозчик", shipment.Carrier),
                ("Менеджер", shipment.Manager),
                ("Номер", shipment.Number),
                ("Комментарий", shipment.Comment)
            ],
            shipment.Lines.Select(line => new PrintLine(
                line.ItemCode,
                line.ItemName,
                line.Unit,
                line.Quantity,
                line.Price,
                line.Amount)),
            shipment.TotalAmount,
            shipment.Comment);
    }

    private static string BuildDocumentHtml(
        string title,
        string number,
        DateTime date,
        IReadOnlyList<(string Label, string Value)> facts,
        IEnumerable<PrintLine> lines,
        decimal totalAmount,
        string comment)
    {
        var printableLines = lines.ToArray();
        var builder = new StringBuilder();

        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"ru\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        builder.AppendLine("<title>" + Encode(title) + "</title>");
        builder.AppendLine("<style>");
        builder.AppendLine("@page{size:A4;margin:14mm;}");
        builder.AppendLine("body{font-family:'Segoe UI',Tahoma,sans-serif;margin:0;color:#1f2430;background:#fff;font-size:13px;}");
        builder.AppendLine(".doc{max-width:100%;}");
        builder.AppendLine(".header{display:flex;justify-content:space-between;align-items:flex-end;border-bottom:2px solid #5f6b7a;padding-bottom:8px;margin-bottom:12px;}");
        builder.AppendLine(".title{font-size:30px;font-weight:700;line-height:1.05;}");
        builder.AppendLine(".meta{font-size:13px;text-align:right;line-height:1.45;}");
        builder.AppendLine(".meta strong{font-size:16px;display:block;}");
        builder.AppendLine(".facts{width:100%;border-collapse:collapse;table-layout:fixed;margin-bottom:14px;}");
        builder.AppendLine(".facts td{border:1px solid #9aa5b1;padding:8px 10px;vertical-align:top;height:56px;}");
        builder.AppendLine(".fact-label{font-size:11px;color:#4f5a67;text-transform:uppercase;letter-spacing:.03em;margin-bottom:4px;}");
        builder.AppendLine(".fact-value{font-size:14px;font-weight:600;color:#1f2430;word-break:break-word;}");
        builder.AppendLine(".fact-empty{background:#fafbfc;}");
        builder.AppendLine(".section{font-size:22px;font-weight:700;margin:18px 0 6px;}");
        builder.AppendLine(".subtitle{font-size:13px;color:#4f5a67;margin-bottom:8px;}");
        builder.AppendLine(".lines{width:100%;border-collapse:collapse;table-layout:fixed;}");
        builder.AppendLine(".lines th,.lines td{border:1px solid #9aa5b1;padding:7px 8px;}");
        builder.AppendLine(".lines th{background:#f2f5f8;font-size:12px;text-align:left;}");
        builder.AppendLine(".lines td{font-size:13px;}");
        builder.AppendLine(".lines td.num{text-align:right;white-space:nowrap;}");
        builder.AppendLine(".lines .empty{color:#6f7986;text-align:center;padding:12px 8px;}");
        builder.AppendLine(".total{display:flex;justify-content:flex-end;margin-top:10px;}");
        builder.AppendLine(".total-box{border:2px solid #4f5a67;padding:8px 12px;min-width:260px;}");
        builder.AppendLine(".total-label{font-size:11px;text-transform:uppercase;color:#4f5a67;letter-spacing:.03em;}");
        builder.AppendLine(".total-value{font-size:24px;font-weight:700;margin-top:4px;text-align:right;}");
        builder.AppendLine(".comment{margin-top:10px;border:1px solid #9aa5b1;padding:8px 10px;}");
        builder.AppendLine(".comment-title{font-size:11px;text-transform:uppercase;color:#4f5a67;letter-spacing:.03em;margin-bottom:4px;}");
        builder.AppendLine(".comment-value{font-size:13px;white-space:pre-wrap;}");
        builder.AppendLine("</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.AppendLine("<div class=\"doc\">");
        builder.AppendLine("<div class=\"header\">");
        builder.AppendLine("<div class=\"title\">" + Encode(title) + "</div>");
        builder.AppendLine("<div class=\"meta\">");
        builder.AppendLine("<strong>Счет № " + Encode(number) + "</strong>");
        builder.AppendLine("<div>Дата " + Encode(date.ToString("dd.MM.yyyy")) + "</div>");
        builder.AppendLine("</div>");
        builder.AppendLine("</div>");

        builder.AppendLine("<table class=\"facts\"><tbody>");
        for (var index = 0; index < facts.Count; index += 3)
        {
            builder.AppendLine("<tr>");
            for (var offset = 0; offset < 3; offset++)
            {
                var factIndex = index + offset;
                if (factIndex >= facts.Count)
                {
                    builder.AppendLine("<td class=\"fact-empty\"></td>");
                    continue;
                }

                var fact = facts[factIndex];
                builder.AppendLine("<td>");
                builder.AppendLine("<div class=\"fact-label\">" + Encode(fact.Label) + "</div>");
                builder.AppendLine("<div class=\"fact-value\">" + Encode(EmptyAsDash(fact.Value)) + "</div>");
                builder.AppendLine("</td>");
            }

            builder.AppendLine("</tr>");
        }

        builder.AppendLine("</tbody></table>");

        builder.AppendLine("<div class=\"section\">Товары</div>");
        builder.AppendLine("<div class=\"subtitle\">Позиции документа, которые будут переданы в счет, резерв и отгрузку.</div>");
        builder.AppendLine("<table class=\"lines\">");
        builder.AppendLine("<thead><tr><th style=\"width:12%\">Код</th><th style=\"width:43%\">Номенклатура</th><th style=\"width:7%\">Ед.</th><th style=\"width:12%\">Кол-во</th><th style=\"width:12%\">Цена</th><th style=\"width:14%\">Сумма</th></tr></thead>");
        builder.AppendLine("<tbody>");

        if (printableLines.Length == 0)
        {
            builder.AppendLine("<tr><td class=\"empty\" colspan=\"6\">Нет позиций</td></tr>");
        }
        else
        {
            foreach (var line in printableLines)
            {
                builder.AppendLine("<tr>");
                builder.AppendLine("<td>" + Encode(line.Code) + "</td>");
                builder.AppendLine("<td>" + Encode(line.Name) + "</td>");
                builder.AppendLine("<td>" + Encode(line.Unit) + "</td>");
                builder.AppendLine("<td class=\"num\">" + Encode(line.Quantity.ToString("N2", RuCulture)) + "</td>");
                builder.AppendLine("<td class=\"num\">" + Encode(line.Price.ToString("N2", RuCulture)) + "</td>");
                builder.AppendLine("<td class=\"num\">" + Encode(line.Amount.ToString("N2", RuCulture)) + "</td>");
                builder.AppendLine("</tr>");
            }
        }

        builder.AppendLine("</tbody>");
        builder.AppendLine("</table>");

        builder.AppendLine("<div class=\"total\">");
        builder.AppendLine("<div class=\"total-box\">");
        builder.AppendLine("<div class=\"total-label\">Итого</div>");
        builder.AppendLine("<div class=\"total-value\">" + Encode(FormatMoney(totalAmount)) + " ₽</div>");
        builder.AppendLine("</div>");
        builder.AppendLine("</div>");

        if (!string.IsNullOrWhiteSpace(comment))
        {
            builder.AppendLine("<div class=\"comment\">");
            builder.AppendLine("<div class=\"comment-title\">Комментарий</div>");
            builder.AppendLine("<div class=\"comment-value\">" + Encode(comment) + "</div>");
            builder.AppendLine("</div>");
        }

        builder.AppendLine("</div>");
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
        return TextMojibakeFixer.NormalizeText(builder.ToString());
    }

    private static string FormatMoney(decimal value)
    {
        return value.ToString("N2", RuCulture);
    }

    private static string FormatQuantity(decimal value)
    {
        return value.ToString("#,0.##", RuCulture);
    }

    private static string FormatLongDate(DateTime value)
    {
        var date = value == default ? DateTime.Today : value;
        return date.ToString("d MMMM yyyy 'г.'", RuCulture);
    }

    private static string BuildSignatureRow(string title, string name)
    {
        var builder = new StringBuilder();
        builder.AppendLine("<div class=\"signature-row\">");
        builder.AppendLine("<div class=\"signature-title\">" + Encode(title) + "</div>");
        builder.AppendLine("<div class=\"signature-line\"><small>подпись</small></div>");
        builder.AppendLine("<div class=\"signature-line signature-name\">" + Encode(name) + "<small>расшифровка подписи</small></div>");
        builder.AppendLine("</div>");
        return builder.ToString();
    }

    private static string AmountToWords(decimal amount)
    {
        var rounded = Math.Round(Math.Abs(amount), 2, MidpointRounding.AwayFromZero);
        var rubles = (long)Math.Truncate(rounded);
        var kopecks = (int)((rounded - rubles) * 100m);
        var words = NumberToWords(rubles);
        return CapitalizeFirst($"{words} {Plural(rubles, "рубль", "рубля", "рублей")} {kopecks:00} {Plural(kopecks, "копейка", "копейки", "копеек")}");
    }

    private static string NumberToWords(long value)
    {
        if (value == 0)
        {
            return "ноль";
        }

        var groups = new List<string>();
        var scaleIndex = 0;
        while (value > 0)
        {
            var groupValue = (int)(value % 1000);
            if (groupValue > 0)
            {
                groups.Insert(0, BuildNumberGroup(groupValue, scaleIndex));
            }

            value /= 1000;
            scaleIndex++;
        }

        return string.Join(' ', groups.Where(group => !string.IsNullOrWhiteSpace(group)));
    }

    private static string BuildNumberGroup(int value, int scaleIndex)
    {
        string[] hundreds =
        [
            "", "сто", "двести", "триста", "четыреста", "пятьсот", "шестьсот", "семьсот", "восемьсот", "девятьсот"
        ];
        string[] tens =
        [
            "", "", "двадцать", "тридцать", "сорок", "пятьдесят", "шестьдесят", "семьдесят", "восемьдесят", "девяносто"
        ];
        string[] teens =
        [
            "десять", "одиннадцать", "двенадцать", "тринадцать", "четырнадцать", "пятнадцать", "шестнадцать", "семнадцать", "восемнадцать", "девятнадцать"
        ];
        string[] masculineUnits =
        [
            "", "один", "два", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять"
        ];
        string[] feminineUnits =
        [
            "", "одна", "две", "три", "четыре", "пять", "шесть", "семь", "восемь", "девять"
        ];
        (string One, string Few, string Many)[] scales =
        [
            ("", "", ""),
            ("тысяча", "тысячи", "тысяч"),
            ("миллион", "миллиона", "миллионов"),
            ("миллиард", "миллиарда", "миллиардов")
        ];

        var parts = new List<string>();
        parts.Add(hundreds[value / 100]);

        var rest = value % 100;
        if (rest is >= 10 and < 20)
        {
            parts.Add(teens[rest - 10]);
        }
        else
        {
            parts.Add(tens[rest / 10]);
            var units = scaleIndex == 1 ? feminineUnits : masculineUnits;
            parts.Add(units[rest % 10]);
        }

        if (scaleIndex > 0 && scaleIndex < scales.Length)
        {
            var scale = scales[scaleIndex];
            parts.Add(Plural(value, scale.One, scale.Few, scale.Many));
        }

        return string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string Plural(long value, string one, string few, string many)
    {
        var number = Math.Abs(value) % 100;
        if (number is >= 11 and <= 14)
        {
            return many;
        }

        return (number % 10) switch
        {
            1 => one,
            >= 2 and <= 4 => few,
            _ => many
        };
    }

    private static string CapitalizeFirst(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? value
            : char.ToUpper(value[0], RuCulture) + value[1..];
    }

    private static string Display(string? value)
    {
        var normalized = TextMojibakeFixer.NormalizeText(value);
        return string.IsNullOrWhiteSpace(normalized) ? "-" : normalized.Trim();
    }

    private static string EmptyAsDash(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : value;
    }

    private static string Encode(string value)
    {
        return System.Net.WebUtility.HtmlEncode(value);
    }

    private sealed record PrintLine(string Code, string Name, string Unit, decimal Quantity, decimal Price, decimal Amount);
}
