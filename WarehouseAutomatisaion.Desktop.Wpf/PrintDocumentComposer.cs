using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
            WpfDialogOwner.TrySetOwner(preview, owner);

            return preview.ShowDialog() == true;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                WpfDialogOwner.Resolve(owner),
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
        if (definition.BankBlock is not null)
        {
            document.Blocks.Add(BuildBankBlockTable(definition.BankBlock, contentWidth, breakBefore));
        }

        var title = new Paragraph(new Run(Clean(definition.Title)))
        {
            BreakPageBefore = breakBefore && definition.BankBlock is null,
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

    private static Table BuildBankBlockTable(InvoiceBankBlock block, double contentWidth, bool breakBefore)
    {
        var qrColumnWidth = block.QrPngBytes is { Length: > 0 } ? 140d : 0d;
        var infoColumnWidth = Math.Max(420d, contentWidth - qrColumnWidth);

        var table = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0, 0, 0, 12),
            BreakPageBefore = breakBefore
        };
        table.Columns.Add(new TableColumn { Width = new GridLength(infoColumnWidth) });
        if (qrColumnWidth > 0)
        {
            table.Columns.Add(new TableColumn { Width = new GridLength(qrColumnWidth) });
        }

        var rowGroup = new TableRowGroup();
        table.RowGroups.Add(rowGroup);

        var infoCell = new TableCell
        {
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(0.6),
            Padding = new Thickness(0)
        };
        infoCell.Blocks.Add(BuildBankInfoInnerTable(block));

        var bodyRow = new TableRow();
        bodyRow.Cells.Add(infoCell);

        if (qrColumnWidth > 0)
        {
            var qrCell = new TableCell
            {
                BorderBrush = BorderBrush,
                BorderThickness = new Thickness(0, 0.6, 0.6, 0.6),
                Padding = new Thickness(4)
            };
            qrCell.Blocks.Add(BuildQrBlock(block.QrPngBytes!));
            bodyRow.Cells.Add(qrCell);
        }

        rowGroup.Rows.Add(bodyRow);
        return table;
    }

    private static Table BuildBankInfoInnerTable(InvoiceBankBlock block)
    {
        var innerTable = new Table
        {
            CellSpacing = 0,
            Margin = new Thickness(0)
        };
        innerTable.Columns.Add(new TableColumn { Width = new GridLength(2.6, GridUnitType.Star) });
        innerTable.Columns.Add(new TableColumn { Width = new GridLength(0.8, GridUnitType.Star) });
        innerTable.Columns.Add(new TableColumn { Width = new GridLength(2.0, GridUnitType.Star) });

        var group = new TableRowGroup();
        innerTable.RowGroups.Add(group);

        var bankRow = new TableRow();
        bankRow.Cells.Add(BankCell(block.BankName, "Банк получателя", rowSpan: 2));
        bankRow.Cells.Add(BankLabelCell("БИК"));
        bankRow.Cells.Add(BankCell(block.Bik));
        group.Rows.Add(bankRow);

        var corrRow = new TableRow();
        corrRow.Cells.Add(BankLabelCell("Сч. №"));
        corrRow.Cells.Add(BankCell(block.CorrespondentAccount));
        group.Rows.Add(corrRow);

        var innRow = new TableRow();
        var innCell = new TableCell
        {
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(0.6),
            Padding = CellPadding
        };
        innCell.Blocks.Add(new Paragraph(new Run("ИНН " + Clean(block.Inn)))
        {
            Margin = new Thickness(0),
            FontSize = 11
        });
        if (!string.IsNullOrWhiteSpace(block.Kpp))
        {
            innCell.Blocks.Add(new Paragraph(new Run("КПП " + Clean(block.Kpp)))
            {
                Margin = new Thickness(0, 2, 0, 0),
                FontSize = 11
            });
        }
        innRow.Cells.Add(innCell);
        innRow.Cells.Add(BankLabelCell("Сч. №", rowSpan: 2));
        innRow.Cells.Add(BankCell(block.PaymentAccount, rowSpan: 2));
        group.Rows.Add(innRow);

        var receiverRow = new TableRow();
        receiverRow.Cells.Add(BankCell(block.OrganizationName, "Получатель"));
        group.Rows.Add(receiverRow);

        return innerTable;
    }

    private static TableCell BankCell(string value, string? caption = null, int rowSpan = 1)
    {
        var cell = new TableCell
        {
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(0.6),
            Padding = CellPadding,
            RowSpan = rowSpan
        };
        cell.Blocks.Add(new Paragraph(new Run(Clean(value)))
        {
            Margin = new Thickness(0),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold
        });
        if (!string.IsNullOrWhiteSpace(caption))
        {
            cell.Blocks.Add(new Paragraph(new Run(Clean(caption)))
            {
                Margin = new Thickness(0, 2, 0, 0),
                FontSize = 8.5,
                Foreground = MutedBrush
            });
        }

        return cell;
    }

    private static TableCell BankLabelCell(string label, int rowSpan = 1)
    {
        var cell = new TableCell
        {
            BorderBrush = BorderBrush,
            BorderThickness = new Thickness(0.6),
            Padding = CellPadding,
            RowSpan = rowSpan
        };
        cell.Blocks.Add(new Paragraph(new Run(Clean(label)))
        {
            Margin = new Thickness(0),
            FontSize = 10
        });
        return cell;
    }

    private static BlockUIContainer BuildQrBlock(byte[] qrPngBytes)
    {
        var bitmap = new BitmapImage();
        using (var stream = new MemoryStream(qrPngBytes))
        {
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
        }
        bitmap.Freeze();

        var image = new Image
        {
            Source = bitmap,
            Width = 120,
            Height = 120,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        stack.Children.Add(image);
        stack.Children.Add(new TextBlock
        {
            Text = Clean("Отсканируйте для оплаты"),
            FontSize = 8.5,
            Foreground = MutedBrush,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });

        return new BlockUIContainer(stack)
        {
            Margin = new Thickness(0),
            Padding = new Thickness(0)
        };
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
    string Comment = "",
    InvoiceBankBlock? BankBlock = null);

internal sealed record InvoiceBankBlock(
    string OrganizationName,
    string Inn,
    string Kpp,
    string BankName,
    string Bik,
    string CorrespondentAccount,
    string PaymentAccount,
    byte[]? QrPngBytes);

internal sealed record PrintableLabelDefinition(
    string Title,
    string Name,
    string Status,
    IReadOnlyList<PrintableField> Fields,
    string Marker,
    string Payload,
    string Footer);
