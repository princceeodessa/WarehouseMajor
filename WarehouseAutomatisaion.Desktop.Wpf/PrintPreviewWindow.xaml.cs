using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class PrintPreviewWindow : Window
{
    private const double PreviewPageWidth = 793.7;
    private const double PreviewPageHeight = 1122.5;

    private readonly string _jobTitle;
    private readonly Func<double, double, FlowDocument> _buildDocument;

    public PrintPreviewWindow(string jobTitle, Func<double, double, FlowDocument> buildDocument)
    {
        _jobTitle = Clean(jobTitle);
        _buildDocument = buildDocument ?? throw new ArgumentNullException(nameof(buildDocument));

        InitializeComponent();

        Title = $"Предпросмотр печати - {_jobTitle}";
        HeaderTitleText.Text = _jobTitle;
        PreviewReader.Document = _buildDocument(PreviewPageWidth, PreviewPageHeight);
        StatusText.Text = "Готово к печати";
    }

    private void HandlePrintClick(object sender, RoutedEventArgs e)
    {
        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() != true)
        {
            return;
        }

        PrintButton.IsEnabled = false;
        StatusText.Text = "Отправка на принтер...";

        try
        {
            var document = _buildDocument(printDialog.PrintableAreaWidth, printDialog.PrintableAreaHeight);
            printDialog.PrintDocument(((IDocumentPaginatorSource)document).DocumentPaginator, _jobTitle);
            DialogResult = true;
        }
        catch (Exception exception)
        {
            StatusText.Text = "Печать не выполнена";
            MessageBox.Show(
                this,
                $"Не удалось отправить документ в печать.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                AppBranding.MessageBoxTitle,
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        finally
        {
            PrintButton.IsEnabled = true;
        }
    }

    private void HandleCloseClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private static string Clean(string? value)
    {
        var normalized = TextMojibakeFixer.NormalizeText(value);
        return string.IsNullOrWhiteSpace(normalized) ? "Документ" : normalized.Trim();
    }
}
