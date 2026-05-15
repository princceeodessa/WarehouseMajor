using System.Windows;
using System.Windows.Controls;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class ComingSoonView : UserControl
{
    public ComingSoonView(string caption)
    {
        InitializeComponent();
        HeaderTitleText.Text = string.IsNullOrWhiteSpace(caption)
            ? "Раздел в разработке"
            : $"Раздел «{caption}» в разработке";
        HeaderSubtitleText.Text = "Эта команда появится в одном из следующих релизов. Закройте вкладку или вернитесь к рабочим разделам.";
    }

    private void HandleGoDashboardClick(object sender, RoutedEventArgs e)
    {
        var mainWindow = System.Windows.Application.Current?.MainWindow as MainWindow;
        mainWindow?.OpenSection("dashboard");
    }
}
