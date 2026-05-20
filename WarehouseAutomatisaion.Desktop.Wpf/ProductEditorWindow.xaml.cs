using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class ProductEditorWindow : Window
{
    private static readonly CultureInfo RuCulture = CultureInfo.GetCultureInfo("ru-RU");

    // Палитра «точек» для видов цен в правой колонке. Цвет берётся по индексу
    // вида цены в списке PriceTypes (стабильно: первый вид всегда красный, второй — синий, и т.д.).
    // Палитра аналогична 1С-картинке, но в наших более «холодных» тонах.
    private static readonly Color[] PriceDotPalette =
    [
        Color.FromRgb(0xEE, 0x4F, 0x4F), // 1 — красный (РРЦ)
        Color.FromRgb(0x4F, 0x5B, 0xFF), // 2 — наш primary синий
        Color.FromRgb(0xF7, 0x9A, 0x2A), // 3 — оранжевый
        Color.FromRgb(0x9B, 0x59, 0xD8), // 4 — фиолетовый
        Color.FromRgb(0x2E, 0xC4, 0x8A), // 5 — зелёный
        Color.FromRgb(0xE8, 0x6E, 0xC4), // 6 — розовый
        Color.FromRgb(0x4A, 0xC2, 0xE5), // 7 — бирюзовый
        Color.FromRgb(0xF1, 0xC4, 0x40), // 8 — жёлтый
        Color.FromRgb(0x6B, 0x7C, 0x99), // 9 — серо-синий
        Color.FromRgb(0x32, 0x86, 0xC2), // 10 — глубокий синий
    ];

    private readonly CatalogWorkspace _workspace;
    private readonly CatalogItemRecord _draft;
    private readonly IReadOnlyList<WarehouseCellBalanceRecord> _cellBalances;
    // TextBox-ы цен индексируются по Id вида цены — на сохранении пробегаемся по словарю.
    private readonly Dictionary<Guid, TextBox> _priceEditors = new();
    private readonly Dictionary<Guid, CatalogPriceTypeRecord> _priceTypesById = new();
    private bool _hostedInWorkspace;

    // Опциональные интеграции с другими модулями — если переданы,
    // активируют кнопки тулбара «Продать»/«Купить».
    private SalesWorkspace? _salesWorkspaceForActions;
    private Func<OperationalPurchasingWorkspace?>? _purchasingFactoryForActions;

    public ProductEditorWindow(
        CatalogWorkspace workspace,
        CatalogItemRecord? item = null,
        IEnumerable<WarehouseCellBalanceRecord>? cellBalances = null)
    {
        _workspace = workspace;
        _draft = item?.Clone() ?? workspace.CreateItemDraft();
        _cellBalances = (cellBalances ?? Array.Empty<WarehouseCellBalanceRecord>()).ToArray();

        InitializeComponent();

        Title = item is null ? "Новый товар" : $"Товар {_draft.Code}";
        HeaderTitleText.Text = BuildHeaderTitle(item);

        InitializeStaticCombos();
        InitializeDynamicCombos();
        LoadDraft();
        LoadPricesPanel();
        LoadCellBalances();
        HookVolumeRecalculation();
    }

    /// <summary>
    /// Опционально подключает кнопки «Продать»/«Купить» к рабочим окнам Продаж и Закупок.
    /// Если оставить null — кнопки остаются неактивными.
    /// </summary>
    public void AttachDocumentActions(
        SalesWorkspace? salesWorkspace,
        Func<OperationalPurchasingWorkspace?>? purchasingFactory)
    {
        _salesWorkspaceForActions = salesWorkspace;
        _purchasingFactoryForActions = purchasingFactory;
        ApplyDocumentActionsState();
    }

    private void ApplyDocumentActionsState()
    {
        if (SellSplitButton is not null)
        {
            SellSplitButton.IsEnabled = _salesWorkspaceForActions is not null;
            if (_salesWorkspaceForActions is not null)
            {
                SellSplitButton.ToolTip = "Создать документ продажи на основе этого товара";
            }
        }

        if (BuySplitButton is not null)
        {
            BuySplitButton.IsEnabled = _purchasingFactoryForActions is not null;
            if (_purchasingFactoryForActions is not null)
            {
                BuySplitButton.ToolTip = "Создать документ закупки на основе этого товара";
            }
        }
    }

    public CatalogItemRecord? ResultItem { get; private set; }

    public event EventHandler? HostedSaved;

    public event EventHandler? HostedCanceled;

    public FrameworkElement DetachContentForWorkspaceTab()
    {
        _hostedInWorkspace = true;
        var content = Content as FrameworkElement
            ?? throw new InvalidOperationException("Editor content is not available.");
        Content = null;
        return content;
    }

    private static string Ui(string? value)
    {
        return TextMojibakeFixer.NormalizeText(value);
    }

    private string BuildHeaderTitle(CatalogItemRecord? item)
    {
        // 1С показывает заголовок вида:
        //   «Светодиодный модуль LEDS POWER 5Вт 4000К 220В серия MODULE (Номенклатура)»
        // У нас аналогично: имя товара + раздел в скобках.
        if (item is null)
        {
            return "Новый товар (Номенклатура)";
        }

        var name = Ui(item.Name);
        return string.IsNullOrWhiteSpace(name)
            ? $"Карточка товара {Ui(item.Code)} (Номенклатура)"
            : $"{name} (Номенклатура)";
    }

    private void InitializeStaticCombos()
    {
        // Тип номенклатуры — фиксированный набор как в 1С УНФ.
        ItemTypeComboBox.ItemsSource = new[] { "Запас", "Услуга", "Работа", "Набор" };

        // Статус выгрузки на сайт — минимальный набор. Если позже понадобятся дополнительные
        // значения (например, «Архив», «Скрыт»), добавим в этот массив.
        SiteUploadStatusComboBox.ItemsSource = new[]
        {
            string.Empty, "Выгружать", "Не выгружать", "Архив"
        };
    }

    private void InitializeDynamicCombos()
    {
        // Категории, бренды, цвета, родительские группы — собираем из существующих карточек.
        // Это даёт пользователю автокомплит из уже использованных значений
        // без необходимости в отдельных справочниках.
        //
        // PERF (release 1.0.105): один проход по Items + HashSet'ы вместо 4×LINQ-цепочек
        // с NormalizeText/CurrentCulture. На каталоге 9893 товара старая реализация
        // блокировала UI-поток на десятки секунд (4 × O(n) + культурная сортировка).
        // Значения в Items уже нормализованы при загрузке из MySQL / создании карточки,
        // поэтому Ui() здесь не нужен.
        var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var brands = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var colors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parentGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in _workspace.Items)
        {
            if (!string.IsNullOrWhiteSpace(item.Category)) categories.Add(item.Category);
            if (!string.IsNullOrWhiteSpace(item.Brand)) brands.Add(item.Brand);
            if (!string.IsNullOrWhiteSpace(item.Color)) colors.Add(item.Color);
            if (!string.IsNullOrWhiteSpace(item.ParentGroup)) parentGroups.Add(item.ParentGroup);
        }

        CategoryComboBox.ItemsSource = SortForCombo(categories);
        BrandComboBox.ItemsSource = SortForCombo(brands);
        ColorComboBox.ItemsSource = SortForCombo(colors);
        ParentGroupComboBox.ItemsSource = SortForCombo(parentGroups);
    }

    private static string[] SortForCombo(HashSet<string> values)
    {
        // OrdinalIgnoreCase — на порядок быстрее, чем CurrentCultureIgnoreCase,
        // и для автокомплита кириллицы/латиницы пользовательской разницы нет.
        var array = values.ToArray();
        Array.Sort(array, StringComparer.OrdinalIgnoreCase);
        return array;
    }

    private void LoadDraft()
    {
        // === Базовые поля (раньше уже были на карточке) ===
        CodeTextBox.Text = Ui(_draft.Code);
        NameTextBox.Text = Ui(_draft.Name);
        UnitTextBox.Text = string.IsNullOrWhiteSpace(_draft.Unit) ? "шт" : Ui(_draft.Unit);
        NotesTextBox.Text = Ui(_draft.Notes);

        // Поле «Артикул» в 1С отличается от «Код». У нас в модели один Code: он же артикул.
        // Чтобы не плодить два одинаковых поля, кладём Code в оба и при сохранении
        // приоритет отдаём ArticleTextBox (более специфичное название поля).
        ArticleTextBox.Text = Ui(_draft.Code);

        // === Новые поля 1С-style карточки ===
        SelectComboValue(ItemTypeComboBox,
            string.IsNullOrWhiteSpace(_draft.ItemType) ? "Запас" : Ui(_draft.ItemType));
        NameForPrintTextBox.Text = Ui(string.IsNullOrWhiteSpace(_draft.NameForPrint) ? _draft.Name : _draft.NameForPrint);
        DescriptionTextBox.Text = Ui(_draft.Description);
        SetComboText(CategoryComboBox, Ui(_draft.Category));
        SetComboText(ParentGroupComboBox, Ui(_draft.ParentGroup));
        SetComboText(BrandComboBox, Ui(_draft.Brand));
        SetComboText(ColorComboBox, Ui(_draft.Color));
        SelectComboValue(SiteUploadStatusComboBox, Ui(_draft.SiteUploadStatus));

        WidthTextBox.Text = FormatDecimal(_draft.WidthCm);
        HeightTextBox.Text = FormatDecimal(_draft.HeightCm);
        DepthTextBox.Text = FormatDecimal(_draft.DepthCm);
        RecalculateVolume();

        WeightTextBox.Text = FormatDecimal(_draft.WeightKg);
        IsWeightCheckBox.IsChecked = _draft.IsWeight;
        ForbidFractionalCheckBox.IsChecked = _draft.ForbidFractional;
        MinSaleQuantityTextBox.Text = FormatDecimal(_draft.MinSaleQuantity);
        PackQuantityTextBox.Text = FormatDecimal(_draft.PackQuantity);

        IsInactiveCheckBox.IsChecked = _draft.IsInactive;
    }

    private void LoadPricesPanel()
    {
        ItemPricesPanel.Children.Clear();
        _priceEditors.Clear();
        _priceTypesById.Clear();

        // Подгружаем существующие цены товара, чтобы заполнить значения.
        // Release 1.0.129: если в workspace.ItemPrices пусто (мы перестали грузить
        // 70 591 строк на старте каталога) — lazy-load по item_id через store.
        // Один SELECT, ~20 строк, незаметная задержка.
        var existing = _workspace.GetPricesForItem(_draft.Id)
            .ToDictionary(price => price.PriceTypeId, price => price);
        if (existing.Count == 0 && _draft.Id != Guid.Empty)
        {
            try
            {
                var lazy = WarehouseAutomatisaion.Desktop.Data.CatalogWorkspaceStore.CreateDefault()
                    .LoadPricesForItem(_draft.Id);
                foreach (var price in lazy)
                {
                    existing[price.PriceTypeId] = price;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProductEditor] lazy LoadPricesForItem failed: {ex.Message}");
            }
        }

        // Идём по всем видам цен — даже без значения видим в карточке пустую строку.
        var priceTypes = _workspace.PriceTypes
            .OrderByDescending(item => item.IsDefault)
            .ThenBy(item => Ui(item.Name), StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        if (priceTypes.Length == 0)
        {
            // Защита от голой системы без видов цен — показываем подсказку вместо пустого блока.
            ItemPricesPanel.Children.Add(new TextBlock
            {
                Text = "Создайте виды цен в разделе «Виды цен», чтобы заполнить цены товара.",
                FontSize = 12.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x86, 0xA5)),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
            return;
        }

        for (var index = 0; index < priceTypes.Length; index++)
        {
            var priceType = priceTypes[index];
            _priceTypesById[priceType.Id] = priceType;

            var existingPrice = existing.TryGetValue(priceType.Id, out var stored) ? stored.Price : 0m;
            var currency = string.IsNullOrWhiteSpace(priceType.CurrencyCode) ? "RUB" : priceType.CurrencyCode;
            var dotColor = PriceDotPalette[index % PriceDotPalette.Length];

            ItemPricesPanel.Children.Add(BuildPriceRow(priceType, existingPrice, currency, dotColor));
        }
    }

    private Grid BuildPriceRow(CatalogPriceTypeRecord priceType, decimal value, string currency, Color dotColor)
    {
        var row = new Grid { Margin = new Thickness(0, 4, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var dot = new System.Windows.Shapes.Ellipse
        {
            Width = 9,
            Height = 9,
            Fill = new SolidColorBrush(dotColor),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(dot, 0);
        row.Children.Add(dot);

        var name = new TextBlock
        {
            Text = Ui(priceType.Name),
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x17, 0x21, 0x3A)),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(2, 0, 8, 0),
            ToolTip = Ui(priceType.Name)
        };
        Grid.SetColumn(name, 1);
        row.Children.Add(name);

        var editor = new TextBox
        {
            Style = (Style)FindResource("FormTextBoxStyle"),
            HorizontalContentAlignment = HorizontalAlignment.Right,
            Text = FormatDecimal(value),
            Margin = new Thickness(0)
        };
        editor.Tag = priceType.Id;
        Grid.SetColumn(editor, 2);
        row.Children.Add(editor);
        _priceEditors[priceType.Id] = editor;

        var currencyLabel = new TextBlock
        {
            Text = ResolveCurrencySymbol(currency),
            FontSize = 12.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x7A, 0x86, 0xA5)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        Grid.SetColumn(currencyLabel, 3);
        row.Children.Add(currencyLabel);

        return row;
    }

    private static string ResolveCurrencySymbol(string currencyCode)
    {
        // Простой маппинг код → символ. RUB/RUR/RU отрисовываем как ₽, остальное — как код.
        return currencyCode.ToUpperInvariant() switch
        {
            "RUB" or "RUR" or "RU" => "₽",
            "USD" => "$",
            "EUR" => "€",
            _ => currencyCode
        };
    }

    private void LoadCellBalances()
    {
        // Группируем остатки по складу — в 1С на скрине показан именно агрегированный вид
        // «склад → количество», без детализации по ячейкам (детализация — в отдельной вкладке).
        var rows = _cellBalances
            .Where(item => item.Quantity > 0m)
            .GroupBy(item => Ui(item.Warehouse), StringComparer.CurrentCultureIgnoreCase)
            .Select(group =>
            {
                var name = string.IsNullOrWhiteSpace(group.Key) ? "Без склада" : group.Key;
                var totalQty = group.Sum(item => item.Quantity);
                var unit = group.Select(item => Ui(item.Unit)).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
                           ?? "шт";
                return new ProductWarehouseBalanceRow(name, $"{totalQty:N0} {unit}");
            })
            .OrderByDescending(row => row.Warehouse)
            .ToArray();

        CellBalancesHintText.Text = rows.Length == 0
            ? "По товару нет остатков ни на одном складе."
            : $"Складов с остатком: {rows.Length:N0}";
        CellBalancesGrid.ItemsSource = rows;

        // Резюме в заголовке аккордеона «Хранение» (... (Орджоникидзе, 4 (1 эт) (Ижевск)))
        if (rows.Length == 1)
        {
            StorageSummaryRun.Text = $" ({rows[0].Warehouse})";
        }
        else if (rows.Length > 1)
        {
            StorageSummaryRun.Text = $" (хранится на {rows.Length:N0} складах)";
        }
        else
        {
            StorageSummaryRun.Text = string.Empty;
        }
    }

    private void HookVolumeRecalculation()
    {
        WidthTextBox.TextChanged += (_, _) => RecalculateVolume();
        HeightTextBox.TextChanged += (_, _) => RecalculateVolume();
        DepthTextBox.TextChanged += (_, _) => RecalculateVolume();
    }

    private void RecalculateVolume()
    {
        if (!TryParseDecimal(WidthTextBox?.Text, out var width)
            || !TryParseDecimal(HeightTextBox?.Text, out var height)
            || !TryParseDecimal(DepthTextBox?.Text, out var depth))
        {
            VolumeTextBox.Text = string.Empty;
            return;
        }

        // ДхШхВ в см → объём в м³ (делим на миллион).
        var volume = width * height * depth / 1_000_000m;
        VolumeTextBox.Text = volume <= 0m ? string.Empty : volume.ToString("N4", RuCulture);
    }

    private void HandleSaveAndCloseClick(object sender, RoutedEventArgs e)
    {
        TrySaveAndComplete(closeAfterSave: true);
    }

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        // В 1С кнопка «Save» сохраняет без закрытия. У нас две формы открытия:
        // в Window-режиме закрываем окно (как раньше); в Tab-режиме — закрываем вкладку.
        // Поведение симметрично «Save and close» — пользователь увидит, что данные ушли,
        // а кнопка «Save and close» отличается только визуально как primary CTA.
        TrySaveAndComplete(closeAfterSave: true);
    }

    private bool TrySaveAndComplete(bool closeAfterSave)
    {
        ValidationText.Text = string.Empty;

        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
        {
            ValidationText.Text = "Укажите наименование товара.";
            return false;
        }

        // Артикул и Код — у нас одно поле в модели; ArticleTextBox имеет приоритет,
        // CodeTextBox — fallback.
        var codeValue = !string.IsNullOrWhiteSpace(ArticleTextBox.Text)
            ? ArticleTextBox.Text.Trim()
            : CodeTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(codeValue))
        {
            ValidationText.Text = "Укажите артикул или код товара.";
            return false;
        }

        if (!TryParseDecimal(WeightTextBox.Text, out var weight))
        {
            ValidationText.Text = "Вес должен быть числом.";
            return false;
        }

        if (!TryParseDecimal(MinSaleQuantityTextBox.Text, out var minQuantity)
            || !TryParseDecimal(PackQuantityTextBox.Text, out var packQuantity))
        {
            ValidationText.Text = "Минимальное продаваемое количество и кратность упаковки должны быть числами.";
            return false;
        }

        if (!TryParseDecimal(WidthTextBox.Text, out var width)
            || !TryParseDecimal(HeightTextBox.Text, out var height)
            || !TryParseDecimal(DepthTextBox.Text, out var depth))
        {
            ValidationText.Text = "Габариты должны быть числами.";
            return false;
        }

        // Собираем цены по видам. Парсинг — внутри цикла, чтобы пометить конкретное поле в ошибке.
        var pricesToUpsert = new List<CatalogItemPriceRecord>();
        foreach (var pair in _priceEditors)
        {
            if (!TryParseDecimal(pair.Value.Text, out var price))
            {
                var name = _priceTypesById.TryGetValue(pair.Key, out var pt) ? Ui(pt.Name) : "вид цены";
                ValidationText.Text = $"Цена «{name}» должна быть числом.";
                return false;
            }

            if (!_priceTypesById.TryGetValue(pair.Key, out var priceType))
            {
                continue;
            }

            pricesToUpsert.Add(new CatalogItemPriceRecord
            {
                ItemId = _draft.Id == Guid.Empty ? Guid.NewGuid() : _draft.Id,
                PriceTypeId = priceType.Id,
                PriceTypeName = Ui(priceType.Name),
                Price = price,
                CurrencyCode = string.IsNullOrWhiteSpace(priceType.CurrencyCode) ? "RUB" : priceType.CurrencyCode
            });
        }

        // Базовая цена (DefaultPrice) — это цена по виду «по умолчанию».
        var defaultPriceType = _workspace.PriceTypes.FirstOrDefault(item => item.IsDefault);
        var defaultPrice = defaultPriceType is null
            ? 0m
            : pricesToUpsert.FirstOrDefault(p => p.PriceTypeId == defaultPriceType.Id)?.Price ?? 0m;

        var newId = _draft.Id == Guid.Empty ? Guid.NewGuid() : _draft.Id;
        // Обновим ItemId во всех ценах — на случай нового товара (CreateItemDraft даёт Guid сразу,
        // но защититься от Empty не помешает).
        foreach (var price in pricesToUpsert)
        {
            price.ItemId = newId;
        }

        ResultItem = new CatalogItemRecord
        {
            Id = newId,
            Code = codeValue,
            Name = NameTextBox.Text.Trim(),
            Unit = string.IsNullOrWhiteSpace(UnitTextBox.Text) ? "шт" : UnitTextBox.Text.Trim(),
            Category = CategoryComboBox.Text?.Trim() ?? string.Empty,
            Supplier = _draft.Supplier,                          // поставщик не редактируется в 1С-карточке (отдельный таб)
            DefaultWarehouse = _draft.DefaultWarehouse,         // склад по умолчанию — также отдельный раздел
            Status = string.IsNullOrWhiteSpace(_draft.Status) ? "Активна" : _draft.Status,
            CurrencyCode = string.IsNullOrWhiteSpace(_draft.CurrencyCode) ? "RUB" : _draft.CurrencyCode,
            DefaultPrice = defaultPrice,
            BarcodeValue = _draft.BarcodeValue,
            BarcodeFormat = string.IsNullOrWhiteSpace(_draft.BarcodeFormat) ? "Code128" : _draft.BarcodeFormat,
            QrPayload = _draft.QrPayload,
            Notes = NotesTextBox.Text.Trim(),
            SourceLabel = string.IsNullOrWhiteSpace(_draft.SourceLabel) ? "Локальный каталог" : _draft.SourceLabel,

            ItemType = string.IsNullOrWhiteSpace(ItemTypeComboBox.Text) ? "Запас" : ItemTypeComboBox.Text.Trim(),
            NameForPrint = NameForPrintTextBox.Text.Trim(),
            ParentGroup = ParentGroupComboBox.Text?.Trim() ?? string.Empty,
            Brand = BrandComboBox.Text?.Trim() ?? string.Empty,
            Color = ColorComboBox.Text?.Trim() ?? string.Empty,
            Description = DescriptionTextBox.Text.Trim(),
            WidthCm = width,
            HeightCm = height,
            DepthCm = depth,
            WeightKg = weight,
            IsWeight = IsWeightCheckBox.IsChecked == true,
            ForbidFractional = ForbidFractionalCheckBox.IsChecked == true,
            MinSaleQuantity = minQuantity,
            PackQuantity = packQuantity,
            SiteUploadStatus = SiteUploadStatusComboBox.Text?.Trim() ?? string.Empty,
            IsInactive = IsInactiveCheckBox.IsChecked == true
        };

        // Положим цены в workspace ДО UpsertItem, чтобы при общем сохранении они уже были на месте.
        // ProductsWorkspaceView сам вызовет TryPersistCatalog после возврата ResultItem.
        _workspace.UpsertItemPrices(newId, pricesToUpsert);

        if (closeAfterSave)
        {
            CompleteEditing(success: true);
        }

        return true;
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        CompleteEditing(success: false);
    }

    /// <summary>
    /// Клик по вкладке «Штрихкоды» открывает отдельное окно ProductBarcodesWindow со списком штрихкодов товара.
    /// Источник данных — таблица app_product_barcodes, заполненная скриптом scripts/Import-UnfBarcodesToMySql.ps1.
    /// </summary>
    private void HandleBarcodesTabClick(object sender, RoutedEventArgs e)
    {
        var itemCode = _draft is null ? string.Empty : Ui(_draft.Code);
        var itemName = _draft is null ? string.Empty : Ui(_draft.Name);
        var window = new ProductBarcodesWindow(itemCode, itemName)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    /// <summary>
    /// Клик по вкладке «Цены» открывает окно истории цен из app_product_price_history.
    /// Данные подтягиваются скриптом scripts/Import-UnfPriceHistoryToMySql.ps1 из 1С УНФ.
    /// </summary>
    private void HandlePricesTabClick(object sender, RoutedEventArgs e)
    {
        var itemCode = _draft is null ? string.Empty : Ui(_draft.Code);
        var itemName = _draft is null ? string.Empty : Ui(_draft.Name);
        var window = new ProductPriceHistoryWindow(itemCode, itemName)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    /// <summary>
    /// Клик по вкладке «Документы» открывает список продаж и закупок этого товара.
    /// UNION app_sales_documents + app_purchasing_documents через JOIN с lines по item_code.
    /// </summary>
    private void HandleDocumentsTabClick(object sender, RoutedEventArgs e)
    {
        var itemCode = _draft is null ? string.Empty : Ui(_draft.Code);
        var itemName = _draft is null ? string.Empty : Ui(_draft.Name);
        var window = new ProductDocumentsWindow(itemCode, itemName)
        {
            Owner = this
        };
        window.ShowDialog();
    }

    // ===== «Продать» / «Купить» — открытие редактора нужного документа =====
    // Клик по самой кнопке (не пункту меню) раскрывает прикреплённый ContextMenu —
    // пользователь видит варианты, как в split-button 1С.

    private void HandleSellClick(object sender, RoutedEventArgs e)
    {
        OpenAttachedContextMenu(sender);
    }

    private void HandleBuyClick(object sender, RoutedEventArgs e)
    {
        OpenAttachedContextMenu(sender);
    }

    private static void OpenAttachedContextMenu(object sender)
    {
        if (sender is Button button && button.ContextMenu is not null)
        {
            button.ContextMenu.PlacementTarget = button;
            button.ContextMenu.IsOpen = true;
        }
    }

    private void HandleSellOrderClick(object sender, RoutedEventArgs e)
    {
        OpenSalesEditor(SalesDocumentEditorMode.Order, "Заказ покупателя");
    }

    private void HandleSellInvoiceClick(object sender, RoutedEventArgs e)
    {
        OpenSalesEditor(SalesDocumentEditorMode.Invoice, "Счёт на оплату");
    }

    private void HandleSellShipmentClick(object sender, RoutedEventArgs e)
    {
        OpenSalesEditor(SalesDocumentEditorMode.Shipment, "Расходная накладная");
    }

    private void HandleBuyOrderClick(object sender, RoutedEventArgs e)
    {
        OpenPurchasingEditor(PurchasingDocumentEditorMode.PurchaseOrder, "Заказ поставщику");
    }

    private void HandleBuyReceiptClick(object sender, RoutedEventArgs e)
    {
        OpenPurchasingEditor(PurchasingDocumentEditorMode.PurchaseReceipt, "Приходная накладная");
    }

    private void OpenSalesEditor(SalesDocumentEditorMode mode, string kindCaption)
    {
        if (_salesWorkspaceForActions is null)
        {
            MessageBox.Show(
                this,
                "Откройте раздел «Продажи» и попробуйте ещё раз — рабочая область не подключена.",
                kindCaption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            var dialog = new SalesDocumentEditorWindow(_salesWorkspaceForActions, mode)
            {
                Owner = this
            };
            ShowPreselectionHint(kindCaption);
            dialog.ShowDialog();
        }
        catch (Exception exception)
        {
            App.WriteClientErrorLog(exception, $"ProductEditorWindow.OpenSalesEditor({mode})");
            MessageBox.Show(
                this,
                $"Не удалось открыть документ «{kindCaption}»: {exception.Message}",
                kindCaption,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void OpenPurchasingEditor(PurchasingDocumentEditorMode mode, string kindCaption)
    {
        var purchasing = _purchasingFactoryForActions?.Invoke();
        if (purchasing is null)
        {
            MessageBox.Show(
                this,
                "Откройте раздел «Закупки» и попробуйте ещё раз — рабочая область не подключена.",
                kindCaption,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            var dialog = new PurchasingDocumentEditorWindow(purchasing, mode)
            {
                Owner = this
            };
            ShowPreselectionHint(kindCaption);
            dialog.ShowDialog();
        }
        catch (Exception exception)
        {
            App.WriteClientErrorLog(exception, $"ProductEditorWindow.OpenPurchasingEditor({mode})");
            MessageBox.Show(
                this,
                $"Не удалось открыть документ «{kindCaption}»: {exception.Message}",
                kindCaption,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Подсказка для пользователя: после открытия редактора нужно вручную добавить товар через «Подобрать».
    /// Автозаполнение строки появится в следующих релизах.
    /// </summary>
    private void ShowPreselectionHint(string kindCaption)
    {
        var code = Ui(_draft.Code);
        if (string.IsNullOrWhiteSpace(code))
        {
            return;
        }
        // Заголовок окна — единственный «тостер» который у нас есть в текущей архитектуре.
        Title = $"{Title} → добавьте {code} в {kindCaption}";
    }

    private void CompleteEditing(bool success)
    {
        if (_hostedInWorkspace)
        {
            if (success)
            {
                HostedSaved?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                HostedCanceled?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        DialogResult = success;
    }

    private static void SelectComboValue(ComboBox comboBox, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            var selected = comboBox.Items
                .Cast<object>()
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

    private static void SetComboText(ComboBox comboBox, string value)
    {
        // Для редактируемых комбобоксов достаточно просто положить значение в Text —
        // ComboBox сам подсветит совпадающий item, если такой есть, без полного
        // обхода Items (что на каталоге 9893 товара блокировало UI на секунды).
        comboBox.Text = value ?? string.Empty;
    }

    private static string FormatDecimal(decimal value)
    {
        return value == 0m ? string.Empty : value.ToString("N2", RuCulture);
    }

    private static bool TryParseDecimal(string? value, out decimal result)
    {
        result = 0m;
        if (string.IsNullOrWhiteSpace(value))
        {
            // Пустое поле трактуем как 0 — это нормальное поведение для числовых полей в 1С.
            return true;
        }

        var normalized = value
            .Replace(' ', ' ')
            .Replace(" ", string.Empty);
        return decimal.TryParse(
                   normalized,
                   NumberStyles.AllowDecimalPoint | NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign,
                   RuCulture,
                   out result)
               || decimal.TryParse(
                   normalized.Replace(',', '.'),
                   NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                   CultureInfo.InvariantCulture,
                   out result);
    }

    private sealed record ProductWarehouseBalanceRow(string Warehouse, string Quantity);
}
