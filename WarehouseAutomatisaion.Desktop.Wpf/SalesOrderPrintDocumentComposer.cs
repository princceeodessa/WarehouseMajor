using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Printing;

namespace WarehouseAutomatisaion.Desktop.Wpf;

internal static class SalesOrderPrintDocumentComposer
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly FontFamily DocumentFont = new("Arial");
    private static readonly Thickness CellPadding = new(3, 2, 3, 2);
    private static readonly Brush BorderBrush = Brushes.Black;

    public static FlowDocument Build(SalesOrderRecord order, double pageWidth, double pageHeight)
    {
        var safePageWidth = IsUsable(pageWidth) ? pageWidth : 793.7;
        var safePageHeight = IsUsable(pageHeight) ? pageHeight : 1122.5;
        var pagePadding = new Thickness(22, 14, 22, 14);
        var contentWidth = Math.Max(620, safePageWidth - pagePadding.Left - pagePadding.Right);
        var lines = order.Lines.ToArray();

        var document = new FlowDocument
        {
            FontFamily = DocumentFont,
            FontSize = 11,
            PageWidth = safePageWidth,
            PageHeight = safePageHeight,
            PagePadding = pagePadding,
            ColumnWidth = contentWidth
        };

        AddTitle(document, SalesDocumentPrintComposer.BuildOrderTitle(order));
        AddPlainLine(document, "Карта для перевода:");
        AddPartyLine(document, "Исполнитель:", "ИП");
        AddPartyLine(document, "Заказчик:", SalesDocumentPrintComposer.DisplayOrderText(order.CustomerName));
        document.Blocks.Add(BuildLinesTable(lines, contentWidth));
        document.Blocks.Add(BuildSummaryTable(order, lines.Length, contentWidth));
        document.Blocks.Add(BuildSignaturesTable(order, contentWidth));

        return document;
    }

    private static void AddTitle(FlowDocument document, string title)
    {
        document.Blocks.Add(new Paragraph(new Run(title))
        {
            FontSize = 21,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 12),
            Padding = new Thickness(4, 4, 4, 5),
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(0, 0.75, 0, 1.5),
            LineHeight = 23
        });
    }

    private static void AddPlainLine(FlowDocument document, string text)
    {
        document.Blocks.Add(new Paragraph(new Run(text))
        {
            FontSize = 12,
            Margin = new Thickness(4, 0, 0, 12)
        });
    }

    private static void AddPartyLine(FlowDocument document, string label, string value)
    {
        var paragraph = new Paragraph
        {
            FontSize = 14,
            Margin = new Thickness(4, 0, 0, 7)
        };
        paragraph.Inlines.Add(new Run(label));
        paragraph.Inlines.Add(new Run("  "));
        paragraph.Inlines.Add(new Run(value) { FontWeight = FontWeights.Bold });
        document.Blocks.Add(paragraph);
    }

    private static Table BuildLinesTable(IReadOnlyList<SalesOrderLineRecord> lines, double contentWidth)
    {
        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 8, 0, 0)
        };

        var weights = new[] { 0.055, 0.085, 0.36, 0.105, 0.085, 0.055, 0.125, 0.13 };
        foreach (var weight in weights)
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(contentWidth * weight) });
        }

        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        var header = new TableRow();
        group.Rows.Add(header);
        header.Cells.Add(HeaderCell("№"));
        header.Cells.Add(HeaderCell("Дата"));
        header.Cells.Add(HeaderCell("Товары (работы, услуги)"));
        header.Cells.Add(HeaderCell("Код"));
        header.Cells.Add(HeaderCell("Кол-во"));
        header.Cells.Add(HeaderCell("Ед."));
        header.Cells.Add(HeaderCell("Цена"));
        header.Cells.Add(HeaderCell("Сумма"));

        if (lines.Count == 0)
        {
            group.Rows.Add(new TableRow
            {
                Cells =
                {
                    BodyCell("Нет позиций", TextAlignment.Center, columnSpan: 8)
                }
            });
            return table;
        }

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var row = new TableRow();
            group.Rows.Add(row);

            row.Cells.Add(BodyCell((index + 1).ToString(RuCulture), TextAlignment.Center));
            row.Cells.Add(BodyCell(string.Empty, TextAlignment.Left));
            row.Cells.Add(BodyCell(SalesDocumentPrintComposer.DisplayOrderText(line.ItemName), TextAlignment.Left));
            row.Cells.Add(BodyCell(SalesDocumentPrintComposer.DisplayOrderText(line.ItemCode), TextAlignment.Left));
            row.Cells.Add(BodyCell(SalesDocumentPrintComposer.FormatOrderQuantity(line.Quantity), TextAlignment.Right));
            row.Cells.Add(BodyCell(SalesDocumentPrintComposer.DisplayOrderText(line.Unit), TextAlignment.Left));
            row.Cells.Add(BodyCell(SalesDocumentPrintComposer.FormatOrderMoney(line.Price), TextAlignment.Right));
            row.Cells.Add(BodyCell(SalesDocumentPrintComposer.FormatOrderMoney(line.Amount), TextAlignment.Right));
        }

        return table;
    }

    private static Table BuildSummaryTable(SalesOrderRecord order, int lineCount, double contentWidth)
    {
        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 10, 0, 0)
        };
        table.Columns.Add(new TableColumn { Width = new GridLength(contentWidth * 0.68) });
        table.Columns.Add(new TableColumn { Width = new GridLength(contentWidth * 0.32) });

        var group = new TableRowGroup();
        table.RowGroups.Add(group);

        var left = new TableCell
        {
            Padding = new Thickness(3, 12, 16, 0),
            BorderThickness = new Thickness(0)
        };
        left.Blocks.Add(new Paragraph(new Run($"Всего наименований {lineCount:N0}, на сумму {SalesDocumentPrintComposer.FormatOrderMoney(order.TotalAmount)} руб."))
        {
            Margin = new Thickness(0, 0, 0, 2),
            FontSize = 12
        });
        left.Blocks.Add(new Paragraph(new Run(SalesDocumentPrintComposer.FormatOrderAmountInWords(order.TotalAmount)))
        {
            Margin = new Thickness(0),
            FontSize = 14,
            FontWeight = FontWeights.Bold
        });

        var right = new TableCell
        {
            Padding = new Thickness(0, 0, 3, 0),
            BorderThickness = new Thickness(0),
            TextAlignment = TextAlignment.Right
        };
        right.Blocks.Add(TotalParagraph("Итого:", SalesDocumentPrintComposer.FormatOrderMoney(order.TotalAmount)));
        right.Blocks.Add(TotalParagraph("Без налога (НДС)", string.Empty));

        group.Rows.Add(new TableRow { Cells = { left, right } });
        return table;
    }

    private static Table BuildSignaturesTable(SalesOrderRecord order, double contentWidth)
    {
        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 26, 0, 0),
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(0, 1.4, 0, 0)
        };
        table.Columns.Add(new TableColumn { Width = new GridLength(contentWidth * 0.12) });
        table.Columns.Add(new TableColumn { Width = new GridLength(contentWidth * 0.24) });
        table.Columns.Add(new TableColumn { Width = new GridLength(contentWidth * 0.64) });

        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        group.Rows.Add(SignatureRow("Исполнитель", SalesDocumentPrintComposer.DisplayOrderText(order.Manager)));
        group.Rows.Add(SignatureRow("Заказчик", string.Empty));
        return table;
    }

    private static TableRow SignatureRow(string title, string name)
    {
        var row = new TableRow();
        row.Cells.Add(new TableCell(new Paragraph(new Run(title))
        {
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0)
        })
        {
            Padding = new Thickness(0, 17, 10, 0),
            BorderThickness = new Thickness(0)
        });
        row.Cells.Add(SignatureCell(string.Empty, "подпись"));
        row.Cells.Add(SignatureCell(name, "расшифровка подписи"));
        return row;
    }

    private static Paragraph TotalParagraph(string label, string value)
    {
        var paragraph = new Paragraph
        {
            Margin = new Thickness(0, 0, 0, 3),
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Right
        };
        paragraph.Inlines.Add(new Run(label));
        paragraph.Inlines.Add(new Run("  "));
        paragraph.Inlines.Add(new Run(value));
        return paragraph;
    }

    private static TableCell HeaderCell(string text)
    {
        return Cell(text, TextAlignment.Center, FontWeights.Bold, 14, lineHeight: 15);
    }

    private static TableCell BodyCell(string text, TextAlignment alignment, int columnSpan = 1)
    {
        return Cell(text, alignment, FontWeights.Normal, 10.5, columnSpan, lineHeight: 12);
    }

    private static TableCell Cell(
        string text,
        TextAlignment alignment,
        FontWeight fontWeight,
        double fontSize,
        int columnSpan = 1,
        double lineHeight = 12)
    {
        return new TableCell(new Paragraph(new Run(text))
        {
            Margin = new Thickness(0),
            FontSize = fontSize,
            FontWeight = fontWeight,
            TextAlignment = alignment,
            LineHeight = lineHeight
        })
        {
            Padding = CellPadding,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(0.6),
            ColumnSpan = columnSpan
        };
    }

    private static TableCell SignatureCell(string value, string caption)
    {
        var cell = new TableCell
        {
            Padding = new Thickness(10, 8, 10, 0),
            BorderThickness = new Thickness(0)
        };
        cell.Blocks.Add(new Paragraph(new Run(value))
        {
            Margin = new Thickness(0, 0, 0, 0),
            Padding = new Thickness(0, 0, 0, 2),
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 0.75),
            FontSize = 10.5,
            TextAlignment = TextAlignment.Center,
            LineHeight = 20
        });
        cell.Blocks.Add(new Paragraph(new Run(caption))
        {
            Margin = new Thickness(0),
            FontSize = 7,
            TextAlignment = TextAlignment.Center
        });
        return cell;
    }

    private static bool IsUsable(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
    }
}
