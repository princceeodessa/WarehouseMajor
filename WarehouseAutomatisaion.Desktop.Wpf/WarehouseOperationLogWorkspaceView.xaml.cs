using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WarehouseAutomatisaion.Application.Abstractions.Persistence;
using WarehouseAutomatisaion.Application.Contracts.Warehouse;
using WarehouseAutomatisaion.Desktop.Data;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class WarehouseOperationLogWorkspaceView : UserControl
{
    private IWarehouseOperationLogReader? _reader;
    private DispatcherTimer? _searchDebounceTimer;
    private bool _isInitialized;

    public WarehouseOperationLogWorkspaceView()
    {
        InitializeComponent();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_isInitialized)
        {
            return;
        }

        var services = DesktopScanLookupFactory.TryCreate();
        if (services is null)
        {
            StatusText.Text = "Нет подключения к MySQL. Проверьте RemoteDatabase в appsettings.local.json.";
            return;
        }

        _reader = services.OperationLogReader;
        _isInitialized = true;
        await ReloadAsync();
    }

    private async void OnRefreshClicked(object sender, RoutedEventArgs e)
    {
        await ReloadAsync();
    }

    private void OnSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        _searchDebounceTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Tick -= OnSearchDebounceTick;
        _searchDebounceTimer.Tick += OnSearchDebounceTick;
        _searchDebounceTimer.Start();
    }

    private async void OnSearchDebounceTick(object? sender, EventArgs e)
    {
        _searchDebounceTimer?.Stop();
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        if (_reader is null)
        {
            return;
        }

        try
        {
            StatusText.Text = "Загрузка журнала...";
            var records = await _reader.GetRecentAsync(500, SearchTextBox.Text);
            var rows = records.Select(LogRow.FromRecord).ToList();
            LogGrid.ItemsSource = rows;
            StatusText.Text = $"Показано операций: {rows.Count:N0}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Ошибка загрузки журнала: {exception.Message}";
        }
    }

    private sealed record LogRow(
        string LoggedAtText,
        string ActorUserName,
        string EntityType,
        string EntityNumber,
        string ActionText,
        string ResultText,
        string MessageText)
    {
        public static LogRow FromRecord(WarehouseOperationLogRecord record)
        {
            return new LogRow(
                record.LoggedAt == DateTime.MinValue ? string.Empty : record.LoggedAt.ToString("dd.MM HH:mm"),
                record.ActorUserName,
                record.EntityType,
                record.EntityNumber,
                record.ActionText,
                record.ResultText,
                record.MessageText);
        }
    }
}
