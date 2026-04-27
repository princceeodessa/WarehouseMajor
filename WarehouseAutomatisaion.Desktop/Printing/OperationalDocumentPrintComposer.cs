using System.Text;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Printing;

public static class OperationalDocumentPrintComposer
{
    public static string BuildPurchaseOrderHtml(OperationalPurchasingDocumentRecord document)
    {
        return BuildPurchasingDocumentHtml(
            "Заказ поставщику",
            document,
            [
                ("Поставщик", document.SupplierName),
                ("Договор", document.Contract),
                ("Склад", document.Warehouse),
                ("Статус", document.Status),
                ("Основание", document.RelatedOrderNumber),
                ("Источник", document.SourceLabel)
            ]);
    }

    public static string BuildSupplierInvoiceHtml(OperationalPurchasingDocumentRecord document)
    {
        return BuildPurchasingDocumentHtml(
            "Счет поставщика",
            document,
            [
                ("Поставщик", document.SupplierName),
                ("Договор", document.Contract),
                ("Склад", document.Warehouse),
                ("Статус", document.Status),
                ("Основание", document.RelatedOrderNumber),
                ("Оплатить до", document.DueDate?.ToString("dd.MM.yyyy") ?? "-")
            ]);
    }

    public static string BuildPurchaseReceiptHtml(OperationalPurchasingDocumentRecord document)
    {
        return BuildPurchasingDocumentHtml(
            "Приемка",
            document,
            [
                ("Поставщик", document.SupplierName),
                ("Договор", document.Contract),
                ("Склад", document.Warehouse),
                ("Статус", document.Status),
                ("Основание", document.RelatedOrderNumber),
                ("Источник", document.SourceLabel)
            ]);
    }

    public static string BuildWarehouseDocumentHtml(OperationalWarehouseDocumentRecord document)
    {
        return BuildWarehouseDocumentHtml(
            document.DocumentType,
            document,
            [
                ("Статус", document.Status),
                ("Склад-источник", document.SourceWarehouse),
                ("Склад-получатель", document.TargetWarehouse),
                ("Основание", document.RelatedDocument),
                ("Источник", document.SourceLabel)
            ]);
    }

    private static string BuildPurchasingDocumentHtml(
        string title,
        OperationalPurchasingDocumentRecord document,
        IReadOnlyList<(string Label, string Value)> facts)
    {
        var builder = new StringBuilder();
        AppendHtmlHead(builder, title);
        AppendDocumentHeader(builder, title, document.Number, document.DocumentDate);
        AppendFacts(builder, facts);

        builder.AppendLine("<table>");
        builder.AppendLine("<thead><tr><th>Код</th><th>Номенклатура</th><th>Ед.</th><th>Кол-во</th><th>Цена</th><th>Сумма</th><th>План</th></tr></thead>");
        builder.AppendLine("<tbody>");
        foreach (var line in document.Lines)
        {
            builder.AppendLine("<tr>");
            builder.AppendLine("<td>" + Encode(line.ItemCode) + "</td>");
            builder.AppendLine("<td>" + Encode(line.ItemName) + "</td>");
            builder.AppendLine("<td>" + Encode(line.Unit) + "</td>");
            builder.AppendLine("<td class=\"num\">" + Encode(line.Quantity.ToString("N2")) + "</td>");
            builder.AppendLine("<td class=\"num\">" + Encode(line.Price.ToString("N2")) + "</td>");
            builder.AppendLine("<td class=\"num\">" + Encode(line.Amount.ToString("N2")) + "</td>");
            builder.AppendLine("<td>" + Encode(line.PlannedDate?.ToString("dd.MM.yyyy") ?? "-") + "</td>");
            builder.AppendLine("</tr>");
        }

        builder.AppendLine("</tbody>");
        builder.AppendLine("</table>");
        AppendTotal(builder, "Итого", $"{document.TotalAmount:N2} ₽");
        AppendComment(builder, document.Comment);
        AppendHtmlTail(builder);
        return builder.ToString();
    }

    private static string BuildWarehouseDocumentHtml(
        string title,
        OperationalWarehouseDocumentRecord document,
        IReadOnlyList<(string Label, string Value)> facts)
    {
        var builder = new StringBuilder();
        AppendHtmlHead(builder, title);
        AppendDocumentHeader(builder, title, document.Number, document.DocumentDate);
        AppendFacts(builder, facts);

        builder.AppendLine("<table>");
        builder.AppendLine("<thead><tr><th>Код</th><th>Номенклатура</th><th>Ед.</th><th>Кол-во</th><th>Ячейка-источник</th><th>Ячейка-получатель</th><th>Основание</th></tr></thead>");
        builder.AppendLine("<tbody>");
        foreach (var line in document.Lines)
        {
            builder.AppendLine("<tr>");
            builder.AppendLine("<td>" + Encode(line.ItemCode) + "</td>");
            builder.AppendLine("<td>" + Encode(line.ItemName) + "</td>");
            builder.AppendLine("<td>" + Encode(line.Unit) + "</td>");
            builder.AppendLine("<td class=\"num\">" + Encode(line.Quantity.ToString("N2")) + "</td>");
            builder.AppendLine("<td>" + Encode(EmptyAsDash(line.SourceLocation)) + "</td>");
            builder.AppendLine("<td>" + Encode(EmptyAsDash(line.TargetLocation)) + "</td>");
            builder.AppendLine("<td>" + Encode(EmptyAsDash(line.RelatedDocument)) + "</td>");
            builder.AppendLine("</tr>");
        }

        builder.AppendLine("</tbody>");
        builder.AppendLine("</table>");
        AppendTotal(builder, "Итого количество", $"{document.TotalQuantity:N2}");
        AppendComment(builder, document.Comment);
        AppendHtmlTail(builder);
        return builder.ToString();
    }

    private static void AppendHtmlHead(StringBuilder builder, string title)
    {
        builder.AppendLine("<!DOCTYPE html>");
        builder.AppendLine("<html lang=\"ru\">");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<title>" + Encode(title) + "</title>");
        builder.AppendLine("<style>");
        builder.AppendLine("body{font-family:'Segoe UI',Tahoma,sans-serif;margin:28px;color:#26231f;background:#fff;}");
        builder.AppendLine(".header{display:flex;justify-content:space-between;align-items:flex-start;border-bottom:2px solid #d9cdbd;padding-bottom:12px;margin-bottom:18px;}");
        builder.AppendLine(".title{font-size:30px;font-weight:700;line-height:1.1;color:#241f1a;}");
        builder.AppendLine(".meta{font-size:14px;color:#5d5348;text-align:right;}");
        builder.AppendLine(".facts{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px 22px;margin-bottom:18px;}");
        builder.AppendLine(".fact{padding:10px 12px;border:1px solid #e2ddd5;background:#fbf8f2;}");
        builder.AppendLine(".fact-label{font-size:12px;color:#7a6f61;text-transform:uppercase;letter-spacing:0.04em;margin-bottom:4px;}");
        builder.AppendLine(".fact-value{font-size:15px;font-weight:600;color:#2d2924;}");
        builder.AppendLine("table{width:100%;border-collapse:collapse;margin-top:8px;}");
        builder.AppendLine("th,td{border:1px solid #ddd4c7;padding:10px 8px;font-size:14px;}");
        builder.AppendLine("th{background:#f5efe5;text-align:left;color:#433b33;}");
        builder.AppendLine("td.num{text-align:right;white-space:nowrap;}");
        builder.AppendLine(".total{margin-top:14px;display:flex;justify-content:flex-end;}");
        builder.AppendLine(".total-box{min-width:260px;border:2px solid #d5c6b1;padding:14px 16px;background:#fff8ed;}");
        builder.AppendLine(".total-label{font-size:13px;color:#7a6f61;text-transform:uppercase;letter-spacing:0.04em;}");
        builder.AppendLine(".total-value{font-size:28px;font-weight:700;color:#2f2a24;margin-top:4px;}");
        builder.AppendLine(".comment{margin-top:18px;padding:14px 16px;background:#faf6ef;border:1px solid #e2ddd5;}");
        builder.AppendLine(".comment-title{font-size:13px;color:#7a6f61;text-transform:uppercase;letter-spacing:0.04em;margin-bottom:6px;}");
        builder.AppendLine(".comment-value{font-size:14px;color:#39332d;white-space:pre-wrap;}");
        builder.AppendLine("</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
    }

    private static void AppendDocumentHeader(StringBuilder builder, string title, string number, DateTime date)
    {
        builder.AppendLine("<div class=\"header\">");
        builder.AppendLine("<div class=\"title\">" + Encode(title) + "</div>");
        builder.AppendLine("<div class=\"meta\">");
        builder.AppendLine("<div><strong>№ " + Encode(number) + "</strong></div>");
        builder.AppendLine("<div>от " + Encode(date.ToString("dd.MM.yyyy")) + "</div>");
        builder.AppendLine("</div>");
        builder.AppendLine("</div>");
    }

    private static void AppendFacts(StringBuilder builder, IReadOnlyList<(string Label, string Value)> facts)
    {
        builder.AppendLine("<div class=\"facts\">");
        foreach (var fact in facts)
        {
            builder.AppendLine("<div class=\"fact\">");
            builder.AppendLine("<div class=\"fact-label\">" + Encode(fact.Label) + "</div>");
            builder.AppendLine("<div class=\"fact-value\">" + Encode(EmptyAsDash(fact.Value)) + "</div>");
            builder.AppendLine("</div>");
        }

        builder.AppendLine("</div>");
    }

    private static void AppendTotal(StringBuilder builder, string title, string value)
    {
        builder.AppendLine("<div class=\"total\"><div class=\"total-box\"><div class=\"total-label\">" + Encode(title) + "</div><div class=\"total-value\">" + Encode(value) + "</div></div></div>");
    }

    private static void AppendComment(StringBuilder builder, string comment)
    {
        if (string.IsNullOrWhiteSpace(comment))
        {
            return;
        }

        builder.AppendLine("<div class=\"comment\"><div class=\"comment-title\">Комментарий</div><div class=\"comment-value\">" + Encode(comment) + "</div></div>");
    }

    private static void AppendHtmlTail(StringBuilder builder)
    {
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");
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
