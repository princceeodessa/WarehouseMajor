using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using QRCoder;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

// Sprint 3 Task 22: окно печати QR-этикеток для ячеек.
// Генерирует FixedDocument с этикетками 50x30 мм, 32 шт на A4 (4 столбца × 8 строк).
// QR payload = JSON {"bin_id": guid, "code": "Z1-R3-S2-C5"} — Major восстановит ячейку по сканированию.
public partial class CellLabelPrintWindow : Window
{
    // 96 DIU per inch; 1mm = 96/25.4 = ~3.7795 DIU.
    private const double MmToDiu = 96.0 / 25.4;
    private const double LabelWidthMm = 50;
    private const double LabelHeightMm = 30;
    private const double PageMarginMm = 6;
    private const double LabelGapMm = 2;
    private const int LabelColumns = 4;
    private const int LabelRows = 8;

    private const double PageWidthA4Mm = 210;
    private const double PageHeightA4Mm = 297;

    private readonly List<StorageCell> _cells;

    public CellLabelPrintWindow(IEnumerable<StorageCell> cells)
    {
        InitializeComponent();
        _cells = cells.Where(c => !string.IsNullOrWhiteSpace(c.Code)).ToList();
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (_cells.Count == 0)
        {
            HeaderText.Text = "Нет ячеек для печати";
            LayoutText.Text = "В отфильтрованном списке нет ни одной ячейки с кодом.";
            PrintButton.IsEnabled = false;
            return;
        }

        var perPage = LabelColumns * LabelRows;
        var pages = (int)Math.Ceiling((double)_cells.Count / perPage);

        HeaderText.Text = $"К печати: {_cells.Count} ячеек";
        LayoutText.Text = $"Этикетка {LabelWidthMm}×{LabelHeightMm} мм   ·   на A4: {LabelColumns}×{LabelRows} = {perPage}/страница   ·   страниц: {pages}";

        try
        {
            DocumentViewerControl.Document = BuildDocument();
            StatusText.Text = $"Готов к печати. Открыть диалог печати — кнопка справа.";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"❌ Ошибка генерации: {exception.Message}";
            PrintButton.IsEnabled = false;
        }
    }

    private FixedDocument BuildDocument()
    {
        var document = new FixedDocument();
        document.DocumentPaginator.PageSize = new Size(PageWidthA4Mm * MmToDiu, PageHeightA4Mm * MmToDiu);

        var labelWidth = LabelWidthMm * MmToDiu;
        var labelHeight = LabelHeightMm * MmToDiu;
        var pageMargin = PageMarginMm * MmToDiu;
        var labelGap = LabelGapMm * MmToDiu;
        var perPage = LabelColumns * LabelRows;

        for (var pageIndex = 0; pageIndex * perPage < _cells.Count; pageIndex++)
        {
            var fixedPage = new FixedPage
            {
                Width = PageWidthA4Mm * MmToDiu,
                Height = PageHeightA4Mm * MmToDiu,
                Background = Brushes.White
            };

            for (var i = 0; i < perPage; i++)
            {
                var cellIndex = pageIndex * perPage + i;
                if (cellIndex >= _cells.Count)
                {
                    break;
                }

                var label = BuildLabel(_cells[cellIndex], labelWidth, labelHeight);

                var col = i % LabelColumns;
                var row = i / LabelColumns;

                FixedPage.SetLeft(label, pageMargin + col * (labelWidth + labelGap));
                FixedPage.SetTop(label, pageMargin + row * (labelHeight + labelGap));

                fixedPage.Children.Add(label);
            }

            var pageContent = new PageContent();
            ((IAddChild)pageContent).AddChild(fixedPage);
            document.Pages.Add(pageContent);
        }

        return document;
    }

    private static UIElement BuildLabel(StorageCell cell, double width, double height)
    {
        var border = new Border
        {
            Width = width,
            Height = height,
            BorderBrush = Brushes.Black,
            BorderThickness = new Thickness(0.5),
            Background = Brushes.White
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(height) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // QR код
        var qrImage = BuildQrImage(cell);
        Grid.SetColumn(qrImage, 0);
        grid.Children.Add(qrImage);

        // Текстовый блок: код крупно + склад мелко
        var textPanel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 2, 4, 2)
        };

        textPanel.Children.Add(new TextBlock
        {
            Text = cell.Code,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            TextWrapping = TextWrapping.Wrap,
        });

        if (!string.IsNullOrWhiteSpace(cell.ZoneCode) || cell.RowNo > 0 || cell.RackNo > 0)
        {
            var addressLabel = $"R{cell.RowNo:D2}-К{cell.RackNo:D2}-П{cell.ShelfNo:D2}-Я{cell.CellNo:D2}";
            if (!string.IsNullOrWhiteSpace(cell.ZoneCode))
            {
                addressLabel = $"{cell.ZoneCode} · {addressLabel}";
            }

            textPanel.Children.Add(new TextBlock
            {
                Text = addressLabel,
                FontSize = 8,
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 0)
            });
        }

        textPanel.Children.Add(new TextBlock
        {
            Text = cell.WarehouseName,
            FontSize = 7.5,
            Foreground = Brushes.Gray,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        });

        Grid.SetColumn(textPanel, 1);
        grid.Children.Add(textPanel);

        border.Child = grid;
        return border;
    }

    private static Image BuildQrImage(StorageCell cell)
    {
        // Уважаем формат QR-payload который установлен в проекте (MWH|v=1|type=cell|...).
        // Если qr_payload в БД пустой (старая ячейка) — генерируем актуальный MWH-payload.
        // TsdScanValueParser в WarehouseAutomatisaion.Tsd распознаёт префикс MWH и
        // правильно матчит ячейку при сканировании handheld'ом.
        var payload = !string.IsNullOrWhiteSpace(cell.QrPayload)
            ? cell.QrPayload!
            : DesktopMySqlBackplaneService.BuildMwhCellPayload(cell.WarehouseName, cell.Code);

        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var qrCode = new PngByteQRCode(qrData);
        var pngBytes = qrCode.GetGraphic(20);

        var bitmap = new BitmapImage();
        using (var ms = new MemoryStream(pngBytes))
        {
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
        }
        bitmap.Freeze();

        return new Image
        {
            Source = bitmap,
            Margin = new Thickness(3),
            Stretch = Stretch.Uniform
        };
    }

    private void OnPrintClicked(object sender, RoutedEventArgs e)
    {
        var printDialog = new System.Windows.Controls.PrintDialog();
        if (printDialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var paginator = ((IDocumentPaginatorSource)DocumentViewerControl.Document).DocumentPaginator;
            printDialog.PrintDocument(paginator, $"QR-этикетки ячеек ({_cells.Count} шт)");
            StatusText.Text = $"✅ Отправлено на печать: {_cells.Count} этикеток";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"❌ Ошибка печати: {exception.Message}";
            MessageBox.Show(this, exception.Message, "Ошибка печати", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
