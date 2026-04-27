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
        return BuildDocumentHtml(
            "Заказ покупателя",
            order.Number,
            order.OrderDate,
            [
                ("Покупатель", $"{order.CustomerName} [{order.CustomerCode}]"),
                ("Дата", order.OrderDate.ToString("dd.MM.yyyy")),
                ("Склад", order.Warehouse),
                ("Договор", order.ContractNumber),
                ("Статус", order.Status),
                ("Менеджер", order.Manager),
                ("Валюта", order.CurrencyCode),
                ("Номер", order.Number),
                ("Комментарий", order.Comment)
            ],
            order.Lines.Select(line => new PrintLine(
                line.ItemCode,
                line.ItemName,
                line.Unit,
                line.Quantity,
                line.Price,
                line.Amount)),
            order.TotalAmount,
            order.Comment);
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




