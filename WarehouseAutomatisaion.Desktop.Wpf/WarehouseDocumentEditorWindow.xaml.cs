using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class WarehouseDocumentEditorWindow : Window
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    private readonly OperationalWarehouseWorkspace _workspace;
    private readonly WarehouseDocumentEditorMode _mode;
    private readonly OperationalWarehouseDocumentRecord _draft;
    private readonly ObservableCollection<OperationalWarehouseLineRecord> _lines;

    public WarehouseDocumentEditorWindow(
        OperationalWarehouseWorkspace workspace,
        WarehouseDocumentEditorMode mode,
        OperationalWarehouseDocumentRecord? document = null)
    {
        _workspace = workspace;
        _mode = mode;
        _draft = document?.Clone() ?? CreateDraft(workspace, mode);
        _lines = new ObservableCollection<OperationalWarehouseLineRecord>(_draft.Lines.Select(line => line.Clone()));

        InitializeComponent();
        WpfTextNormalizer.NormalizeTree(this);

        var headerCaption = Ui(BuildHeaderCaption());
        Title = document is null ? headerCaption : $"{headerCaption} {document.Number}";
        HeaderTitleText.Text = headerCaption;
        HeaderSubtitleText.Text = Ui(BuildSubtitle());
        LinesSubtitleText.Text = Ui(BuildLinesSubtitle());
        TargetWarehouseLabelText.Text = _mode == WarehouseDocumentEditorMode.Transfer ? Ui("Склад-получатель") : Ui("Не используется");

        SourceWarehouseComboBox.ItemsSource = _workspace.Warehouses.ToArray();
        TargetWarehouseComboBox.ItemsSource = _workspace.Warehouses.ToArray();
        StatusComboBox.ItemsSource = GetStatuses().ToArray();
        TargetWarehouseComboBox.IsEnabled = _mode == WarehouseDocumentEditorMode.Transfer;
        if (_mode != WarehouseDocumentEditorMode.Transfer)
        {
            TargetWarehouseComboBox.Opacity = 0.65;
        }

        LinesGrid.ItemsSource = _lines;
        LoadDraft();
        RefreshTotals();
    }

    public OperationalWarehouseDocumentRecord? ResultDocument { get; private set; }

    private static string Ui(string? value) => TextMojibakeFixer.NormalizeText(value);

    private void LoadDraft()
    {
        NumberTextBox.Text = _draft.Number;
        DocumentDatePicker.SelectedDate = _draft.DocumentDate == default ? DateTime.Today : _draft.DocumentDate;
        SelectComboValue(SourceWarehouseComboBox, _draft.SourceWarehouse);
        if (_mode == WarehouseDocumentEditorMode.Transfer)
        {
            SelectComboValue(TargetWarehouseComboBox, _draft.TargetWarehouse);
        }
        else
        {
            TargetWarehouseComboBox.SelectedItem = null;
            TargetWarehouseComboBox.Text = string.Empty;
        }

        SelectComboValue(StatusComboBox, _draft.Status);
        RelatedDocumentTextBox.Text = Ui(_draft.RelatedDocument);
        CommentTextBox.Text = Ui(_draft.Comment);
    }

    private void HandleAddLineClick(object sender, RoutedEventArgs e)
    {
        var dialog = new WarehouseLineEditorWindow(
        Ui(BuildLineTitle()),
        Ui(BuildLineSubtitle()),
        _workspace.CatalogItems,
        allowNegativeQuantity: _mode == WarehouseDocumentEditorMode.Inventory,
        allowTargetLocation: _mode == WarehouseDocumentEditorMode.Transfer)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || dialog.ResultLine is null)
        {
            return;
        }

        _lines.Add(dialog.ResultLine);
        LinesGrid.SelectedItem = dialog.ResultLine;
        RefreshTotals();
    }

    private void HandleEditLineClick(object sender, RoutedEventArgs e)
    {
        if (LinesGrid.SelectedItem is not OperationalWarehouseLineRecord line)
        {
            ValidationText.Text = Ui("Выберите позицию документа.");
            return;
        }

        var dialog = new WarehouseLineEditorWindow(
            Ui(BuildLineTitle()),
            Ui(BuildLineSubtitle()),
            _workspace.CatalogItems,
            line,
            allowNegativeQuantity: _mode == WarehouseDocumentEditorMode.Inventory,
            allowTargetLocation: _mode == WarehouseDocumentEditorMode.Transfer)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true || dialog.ResultLine is null)
        {
            return;
        }

        var index = _lines.IndexOf(line);
        if (index >= 0)
        {
            _lines[index] = dialog.ResultLine;
            LinesGrid.SelectedItem = dialog.ResultLine;
            RefreshTotals();
        }
    }

    private void HandleRemoveLineClick(object sender, RoutedEventArgs e)
    {
        if (LinesGrid.SelectedItem is not OperationalWarehouseLineRecord line)
        {
            ValidationText.Text = Ui("Выберите позицию документа.");
            return;
        }

        var result = MessageBox.Show(
            this,
            Ui("Удалить выбранную позицию?"),
            Ui("Документ склада"),
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        _lines.Remove(line);
        RefreshTotals();
    }

    private void HandleLinesSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(ValidationText.Text) && LinesGrid.SelectedItem is not null)
        {
            ValidationText.Text = string.Empty;
        }
    }

    private void RefreshTotals()
    {
        var total = _lines.Sum(item => item.Quantity);
        TotalText.Text = _mode == WarehouseDocumentEditorMode.Inventory
            ? Ui($"Итоговая корректировка: {total:N2}")
            : Ui($"Всего к движению: {total:N2}");
    }

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        ValidationText.Text = string.Empty;

        var sourceWarehouse = SourceWarehouseComboBox.SelectedItem?.ToString() ?? string.Empty;
        var targetWarehouse = TargetWarehouseComboBox.SelectedItem?.ToString() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(NumberTextBox.Text))
        {
            ValidationText.Text = Ui("Укажите номер документа.");
            return;
        }

        if (string.IsNullOrWhiteSpace(sourceWarehouse))
        {
            ValidationText.Text = Ui("Укажите склад-источник.");
            return;
        }

        if (_mode == WarehouseDocumentEditorMode.Transfer)
        {
            if (string.IsNullOrWhiteSpace(targetWarehouse))
            {
                ValidationText.Text = Ui("Укажите склад-получатель.");
                return;
            }

            if (sourceWarehouse.Equals(targetWarehouse, StringComparison.OrdinalIgnoreCase))
            {
                ValidationText.Text = Ui("Склад-источник и склад-получатель должны отличаться.");
                return;
            }
        }

        if (_lines.Count == 0)
        {
            ValidationText.Text = Ui("Добавьте хотя бы одну позицию.");
            return;
        }

        ResultDocument = new OperationalWarehouseDocumentRecord
        {
            Id = _draft.Id == Guid.Empty ? Guid.NewGuid() : _draft.Id,
            DocumentType = _draft.DocumentType,
            Number = NumberTextBox.Text.Trim(),
            DocumentDate = (DocumentDatePicker.SelectedDate ?? DateTime.Today).Date,
            Status = StatusComboBox.SelectedItem?.ToString() ?? GetStatuses().First(),
            SourceWarehouse = sourceWarehouse,
            TargetWarehouse = _mode == WarehouseDocumentEditorMode.Transfer ? targetWarehouse : string.Empty,
            RelatedDocument = RelatedDocumentTextBox.Text.Trim(),
            Comment = CommentTextBox.Text.Trim(),
            SourceLabel = string.IsNullOrWhiteSpace(_draft.SourceLabel) ? Ui("Локальный контур") : Ui(_draft.SourceLabel),
            Fields = _draft.Fields.ToArray(),
            Lines = new BindingList<OperationalWarehouseLineRecord>(_lines.Select(item => item.Clone()).ToList())
        };

        DialogResult = true;
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private IReadOnlyList<string> GetStatuses()
    {
        return _mode switch
        {
            WarehouseDocumentEditorMode.Transfer => _workspace.TransferStatuses,
            WarehouseDocumentEditorMode.Inventory => _workspace.InventoryStatuses,
            _ => _workspace.WriteOffStatuses
        };
    }

    private string BuildHeaderCaption()
    {
        return _mode switch
        {
            WarehouseDocumentEditorMode.Transfer => "Перемещение",
            WarehouseDocumentEditorMode.Inventory => "Инвентаризация",
            _ => "Списание"
        };
    }

    private string BuildSubtitle()
    {
        return _mode switch
        {
            WarehouseDocumentEditorMode.Transfer => "Склад-источник, склад-получатель и список перемещаемых товаров.",
            WarehouseDocumentEditorMode.Inventory => "Фиксируйте склад и строки корректировки, чтобы провести инвентаризацию локально.",
            _ => "Причина списания и позиции движения сохраняются внутри desktop-контура."
        };
    }

    private string BuildLinesSubtitle()
    {
        return _mode switch
        {
            WarehouseDocumentEditorMode.Transfer => "Позиции будут перенесены между складами после завершения документа.",
            WarehouseDocumentEditorMode.Inventory => "Используйте положительные и отрицательные корректировки по каждой позиции.",
            _ => "Позиции будут списаны со склада после проведения документа."
        };
    }

    private string BuildLineTitle()
    {
        return _mode switch
        {
            WarehouseDocumentEditorMode.Transfer => "Позиция перемещения",
            WarehouseDocumentEditorMode.Inventory => "Данные инвентаризации",
            _ => "Данные списания"
        };
    }

    private string BuildLineSubtitle()
    {
        return _mode switch
        {
            WarehouseDocumentEditorMode.Transfer => "Выберите товар и укажите количество для перемещения между складами.",
            WarehouseDocumentEditorMode.Inventory => "Задайте товар и корректировку остатка на складе.",
            _ => "Выберите товар и укажите количество для списания."
        };
    }

    private static OperationalWarehouseDocumentRecord CreateDraft(
        OperationalWarehouseWorkspace workspace,
        WarehouseDocumentEditorMode mode)
    {
        return mode switch
        {
            WarehouseDocumentEditorMode.Transfer => workspace.CreateTransferDraft(),
            WarehouseDocumentEditorMode.Inventory => workspace.CreateInventoryDraft(),
            _ => workspace.CreateWriteOffDraft()
        };
    }

    private static void SelectComboValue(System.Windows.Controls.ComboBox comboBox, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var selected = comboBox.Items.Cast<object>()
                .Select(item => item?.ToString() ?? string.Empty)
                .FirstOrDefault(item => item.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (selected is not null)
            {
                comboBox.SelectedItem = selected;
                return;
            }
        }

        if (comboBox.Items.Count > 0)
        {
            comboBox.SelectedIndex = 0;
        }
    }
}
