using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

internal static class PrintDocumentComposer
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");
    private static readonly FontFamily DocumentFont = new("Arial");
    private static readonly Brush BorderBrush = Brushes.Black;
    private static readonly Brush HeaderBrush = new SolidColorBrush(Color.FromRgb(242, 244, 248));
    private static readonly Brush MutedBrush = new SolidColorBrush(Color.FromRgb(98, 112, 138));
    private static readonly Thickness PagePadding = new(28, 24, 28, 24);
    private static readonly Thickness CellPadding = new(5, 3, 5, 3);

    public static bool Print(Window? owner, string jobTitle, Func<double, double, FlowDocument> buildDocument)
    {
        try
        {
            var preview = new PrintPreviewWindow(Clean(jobTitle), buildDocument);
            if (owner is not null)
            {
                preview.Owner = owner;
            }

            return preview.ShowDialog() == true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                owner,
                $"Не удалось открыть предпросмотр печати.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                AppBranding.MessageBoxTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
    }

    public static FlowDocument BuildTableDocument(PrintableTableDocumentDefinition definition, double pageWidth, double pageHeight)
    {
        return BuildTableDocument(new[] { definition }, pageWidth, pageHeight);
    }

    public static FlowDocument BuildTableDocument(IReadOnlyList<PrintableTableDocumentDefinition> definitions, double pageWidth, double pageHeight)
    {
        var document = CreateDocument(pageWidth, pageHeight, out var contentWidth);
        for (var index = 0; index < definitions.Count; index++)
        {
            AppendTableDocument(document, definitions[index], contentWidth, breakBefore: index > 0);
        }

        return document;
    }

    public static FlowDocument BuildLabelsDocument(string title, IReadOnlyList<PrintableLabelDefinition> labels, double pageWidth, double pageHeight)
    {
        var document = CreateDocument(pageWidth, pageHeight, out var contentWidth);
        document.Blocks.Add(new Paragraph(new Run(Clean(title)))
        {
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 14)
        });

        var table = new Table
        {
            CellSpacing = 10,
            Margin = new Thickness(0)
        };
        table.Columns.Add(new TableColumn { Width = new GridLength(contentWidth * 0.5) });
        table.Columns.Add(new TableColumn { Width = new GridLength(contentWidth * 0.5) });

        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        for (var index = 0; index < labels.Count; index += 2)
        {
            var row = new TableRow();
            row.Cells.Add(BuildLabelCell(labels[index]));
            if (index + 1 < labels.Count)
            {
                row.Cells.Add(BuildLabelCell(labels[index + 1]));
            }
            else
            {
                row.Cells.Add(new TableCell { BorderThickness = new Thickness(0) });
            }

            group.Rows.Add(row);
        }

        document.Blocks.Add(table);
        return document;
    }

    private static FlowDocument CreateDocument(double pageWidth, double pageHeight, out double contentWidth)
    {
        var safePageWidth = IsUsable(pageWidth) ? pageWidth : 793.7;
        var safePageHeight = IsUsable(pageHeight) ? pageHeight : 1122.5;
        contentWidth = Math.Max(640, safePageWidth - PagePadding.Left - PagePadding.Right);
        return new FlowDocument
        {
            FontFamily = DocumentFont,
            FontSize = 11,
            PageWidth = safePageWidth,
            PageHeight = safePageHeight,
            PagePadding = PagePadding,
            ColumnWidth = contentWidth
        };
    }

    private static void AppendTableDocument(FlowDocument document, PrintableTableDocumentDefinition definition, double contentWidth, bool breakBefore)
    {
        var title = new Paragraph(new Run(Clean(definition.Title)))
        {
            BreakPageBefore = breakBefore,
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 4)
        };
        document.Blocks.Add(title);

        if (!string.IsNullOrWhiteSpace(definition.Subtitle))
        {
            document.Blocks.Add(new Paragraph(new Run(Clean(definition.Subtitle)))
            {
                FontSize = 12,
                Foreground = MutedBrush,
                Margin = new Thickness(0, 0, 0, 10)
            });
        }

        if (definition.Facts.Count > 0)
        {
            document.Blocks.Add(BuildFactsTable(definition.Facts, contentWidth));
        }

        document.Blocks.Add(BuildRowsTable(definition.Columns, definition.Rows, contentWidth));

        if (definition.Totals.Count > 0)
        {
            document.Blocks.Add(BuildTotalsTable(definition.Totals, contentWidth));
        }

        if (!string.IsNullOrWhiteSpace(definition.Comment))
        {
            document.Blocks.Add(new Paragraph(new Run(Clean(definition.Comment)))
            {
                FontSize = 11,
                Margin = new Thickness(0, 12, 0, 0),
                Padding = new Thickness(8),
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(0.6)
            });
        }
    }

    private static Table BuildFactsTable(IReadOnlyList<PrintableField> facts, double contentWidth)
    {
        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 6, 0, 12)
        };
        table.Columns.Add(new TableColumn { Width = new GridLength(contentWidth * 0.5) });
        table.Columns.Add(new TableColumn { Width = new GridLength(contentWidth * 0.5) });

        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        for (var index = 0; index < facts.Count; index += 2)
        {
            var row = new TableRow();
            row.Cells.Add(FactCell(facts[index]));
            row.Cells.Add(index + 1 < facts.Count ? FactCell(facts[index + 1]) : new TableCell { BorderThickness = new Thickness(0) });
            group.Rows.Add(row);
        }

        return table;
    }

    private static Table BuildRowsTable(IReadOnlyList<PrintableTableColumn> columns, IReadOnlyList<PrintableTableRow> rows, double contentWidth)
    {
        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 4, 0, 0)
        };

        var totalWeight = Math.Max(1d, columns.Sum(item => Math.Max(0.1d, item.Weight)));
        foreach (var column in columns)
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(contentWidth * Math.Max(0.1d, column.Weight) / totalWeight) });
        }

        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        var header = new TableRow();
        group.Rows.Add(header);
        foreach (var column in columns)
        {
            header.Cells.Add(Cell(column.Header, TextAlignment.Center, FontWeights.Bold, 10.5, HeaderBrush));
        }

        if (rows.Count == 0)
        {
            group.Rows.Add(new TableRow
            {
                Cells =
                {
                    Cell("Нет данных", TextAlignment.Center, FontWeights.Normal, 10.5, columnSpan: columns.Count)
                }
            });
            return table;
        }

        foreach (var sourceRow in rows)
        {
            var row = new TableRow();
            group.Rows.Add(row);
            for (var index = 0; index < columns.Count; index++)
            {
                var value = index < sourceRow.Cells.Count ? sourceRow.Cells[index] : string.Empty;
                row.Cells.Add(Cell(value, columns[index].Alignment, FontWeights.Normal, 10));
            }
        }

        return table;
    }

    private static Table BuildTotalsTable(IReadOnlyList<PrintableField> totals, double contentWidth)
    {
        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 10, 0, 0)
        };
        table.Columns.Add(new TableColumn { Width = new GridLength(contentWidth * 0.62) });
        table.Columns.Add(new TableColumn { Width = new GridLength(contentWidth * 0.38) });

        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        foreach (var total in totals)
        {
            var row = new TableRow();
            row.Cells.Add(new TableCell { BorderThickness = new Thickness(0) });
            row.Cells.Add(Cell($"{Clean(total.Label)}: {Clean(total.Value)}", TextAlignment.Right, FontWeights.Bold, 13));
            group.Rows.Add(row);
        }

        return table;
    }

    private static TableCell BuildLabelCell(PrintableLabelDefinition label)
    {
        var cell = new TableCell
        {
            Padding = new Thickness(10),
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(0.8)
        };
        cell.Blocks.Add(new Paragraph(new Run(Clean(label.Title)))
        {
            Margin = new Thickness(0, 0, 0, 3),
            FontSize = 10,
            Foreground = MutedBrush
        });
        cell.Blocks.Add(new Paragraph(new Run(Clean(label.Name)))
        {
            Margin = new Thickness(0, 0, 0, 5),
            FontSize = 15,
            FontWeight = FontWeights.Bold
        });
        cell.Blocks.Add(new Paragraph(new Run(Clean(label.Status)))
        {
            Margin = new Thickness(0, 0, 0, 8),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold
        });
        cell.Blocks.Add(BuildLabelFieldsTable(label.Fields));
        cell.Blocks.Add(new Paragraph(new Run(Clean(label.Marker)))
        {
            Margin = new Thickness(0, 8, 0, 0),
            Padding = new Thickness(6, 5, 6, 5),
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(0.6),
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            TextAlignment = TextAlignment.Center
        });
        cell.Blocks.Add(new Paragraph(new Run(Clean(label.Payload)))
        {
            Margin = new Thickness(0, 5, 0, 0),
            FontSize = 8.5,
            Foreground = MutedBrush
        });
        cell.Blocks.Add(new Paragraph(new Run(Clean(label.Footer)))
        {
            Margin = new Thickness(0, 6, 0, 0),
            FontSize = 8.5,
            Foreground = MutedBrush
        });
        return cell;
    }

    private static Table BuildLabelFieldsTable(IReadOnlyList<PrintableField> fields)
    {
        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0)
        };
        table.Columns.Add(new TableColumn { Width = new GridLength(0.36, GridUnitType.Star) });
        table.Columns.Add(new TableColumn { Width = new GridLength(0.64, GridUnitType.Star) });

        var group = new TableRowGroup();
        table.RowGroups.Add(group);
        foreach (var field in fields)
        {
            var row = new TableRow();
            row.Cells.Add(LabelFieldCell(field.Label, FontWeights.Normal, MutedBrush));
            row.Cells.Add(LabelFieldCell(field.Value, FontWeights.SemiBold, Brushes.Black));
            group.Rows.Add(row);
        }

        return table;
    }

    private static TableCell FactCell(PrintableField field)
    {
        var cell = new TableCell
        {
            Padding = new Thickness(0, 0, 16, 5),
            BorderThickness = new Thickness(0)
        };
        cell.Blocks.Add(new Paragraph(new Run(Clean(field.Label)))
        {
            Margin = new Thickness(0, 0, 0, 1),
            FontSize = 9,
            Foreground = MutedBrush
        });
        cell.Blocks.Add(new Paragraph(new Run(Clean(field.Value)))
        {
            Margin = new Thickness(0),
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold
        });
        return cell;
    }

    private static TableCell Cell(
        string text,
        TextAlignment alignment,
        FontWeight fontWeight,
        double fontSize,
        Brush? background = null,
        int columnSpan = 1)
    {
        return new TableCell(new Paragraph(new Run(Clean(text)))
        {
            Margin = new Thickness(0),
            TextAlignment = alignment,
            FontSize = fontSize,
            FontWeight = fontWeight,
            LineHeight = fontSize + 2
        })
        {
            Padding = CellPadding,
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(0.45),
            Background = background ?? Brushes.Transparent,
            ColumnSpan = columnSpan
        };
    }

    private static TableCell LabelFieldCell(string text, FontWeight fontWeight, Brush foreground)
    {
        return new TableCell(new Paragraph(new Run(Clean(text)))
        {
            Margin = new Thickness(0),
            FontSize = 9,
            FontWeight = fontWeight,
            Foreground = foreground
        })
        {
            Padding = new Thickness(0, 1, 8, 1),
            BorderThickness = new Thickness(0)
        };
    }

    private static string Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "-" : TextMojibakeFixer.NormalizeText(value.Trim());
    }

    private static bool IsUsable(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;
    }
}

internal sealed record PrintableField(string Label, string Value);

internal sealed record PrintableTableColumn(string Header, double Weight = 1, TextAlignment Alignment = TextAlignment.Left);

internal sealed record PrintableTableRow(IReadOnlyList<string> Cells);

internal sealed record PrintableTableDocumentDefinition(
    string Title,
    string Subtitle,
    IReadOnlyList<PrintableField> Facts,
    IReadOnlyList<PrintableTableColumn> Columns,
    IReadOnlyList<PrintableTableRow> Rows,
    IReadOnlyList<PrintableField> Totals,
    string Comment = "");

internal sealed record PrintableLabelDefinition(
    string Title,
    string Name,
    string Status,
    IReadOnlyList<PrintableField> Fields,
    string Marker,
    string Payload,
    string Footer);
