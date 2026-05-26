using System.Windows;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;

namespace WarehouseAutomatisaion.Desktop.Wpf;

// Фаза A: универсальное окно отображения позиций (StockLocation).
// Используется и для «Что в ячейке», и для «Где лежит товар» —
// различается только заголовком и набором строк.
public partial class StockLocationsPopupWindow : Window
{
    public StockLocationsPopupWindow(string header, string subheader, IReadOnlyList<StockLocation> locations)
    {
        InitializeComponent();
        HeaderText.Text = header;
        SubheaderText.Text = subheader;
        LocationsGrid.ItemsSource = locations;

        if (locations.Count == 0)
        {
            StatusText.Text = "Нет позиций. Возможно ещё не было приёмки — заполняется через workflow приёмки (Sprint 4) или импорт.";
        }
        else
        {
            var totalQty = locations.Sum(l => l.Quantity);
            var totalReserved = locations.Sum(l => l.ReservedQuantity);
            StatusText.Text = $"Позиций: {locations.Count}   ·   общее количество: {totalQty:N3}   ·   резерв: {totalReserved:N3}";
        }
    }

    private void OnCloseClicked(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
