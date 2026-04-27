using System.ComponentModel;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Infrastructure.Importing;

namespace WarehouseAutomatisaion.Desktop.Controls;

public sealed class OneCImportWorkspaceControl : UserControl
{
    private readonly OneCImportSnapshot _snapshot;
    private readonly BindingSource _schemaBindingSource = new();
    private readonly BindingSource _schemaFieldBindingSource = new();
    private readonly BindingSource _schemaSectionBindingSource = new();
    private readonly DataGridView _schemaGrid = DesktopGridFactory.CreateGrid(Array.Empty<SchemaRow>());
    private readonly DataGridView _schemaFieldGrid = DesktopGridFactory.CreateGrid(Array.Empty<FieldRow>());
    private readonly DataGridView _schemaSectionGrid = DesktopGridFactory.CreateGrid(Array.Empty<SchemaSectionRow>());

    public OneCImportWorkspaceControl(OneCImportSnapshot snapshot)
    {
        _snapshot = snapshot;
        Dock = DockStyle.Fill;
        BackColor = Color.FromArgb(247, 244, 238);
        BuildLayout();
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(16, 14, 16, 16) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(CreateHeader(), 0, 0);
        root.Controls.Add(CreateTabs(), 0, 1);
        Controls.Add(root);
    }

    private Control CreateHeader()
    {
        var sourceSummary = _snapshot.SourceFolders.Count == 0
            ? "Источник выгрузки не найден."
            : $"Найдено источников: {_snapshot.SourceFolders.Count}. Данные и схемы подтягиваются из exports_* и live-папки one-c-live.";

        var panel = new Panel { Dock = DockStyle.Top, Height = 72, Padding = new Padding(0, 0, 0, 8) };
        panel.Controls.Add(new Label { Text = sourceSummary, Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI", 9.5f), ForeColor = Color.FromArgb(114, 104, 93) });
        panel.Controls.Add(new Label { Text = "Снимок данных и полей 1С", Dock = DockStyle.Top, Height = 36, Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold), ForeColor = Color.FromArgb(40, 36, 31) });
        return panel;
    }

    private Control CreateTabs()
    {
        var tabs = DesktopSurfaceFactory.CreateTabControl();
        tabs.TabPages.Add(CreateDatasetTab(_snapshot.Customers, "Контрагенты 1С"));
        tabs.TabPages.Add(CreateDatasetTab(_snapshot.Items, "Номенклатура 1С"));
        tabs.TabPages.Add(CreateDatasetTab(_snapshot.SalesOrders, "Заказы 1С"));
        tabs.TabPages.Add(CreateDatasetTab(_snapshot.SalesInvoices, "Счета 1С"));
        tabs.TabPages.Add(CreateDatasetTab(_snapshot.SalesShipments, "Отгрузки 1С"));
        tabs.TabPages.Add(CreateDatasetTab(_snapshot.PurchaseOrders, "Заказы поставщику 1С"));
        tabs.TabPages.Add(CreateDatasetTab(_snapshot.SupplierInvoices, "Счета поставщиков 1С"));
        tabs.TabPages.Add(CreateDatasetTab(_snapshot.PurchaseReceipts, "Приемка 1С"));
        tabs.TabPages.Add(CreateDatasetTab(_snapshot.TransferOrders, "Перемещения 1С"));
        tabs.TabPages.Add(CreateDatasetTab(_snapshot.StockReservations, "Резервы 1С"));
        tabs.TabPages.Add(CreateDatasetTab(_snapshot.InventoryCounts, "Инвентаризации 1С"));
        tabs.TabPages.Add(CreateDatasetTab(_snapshot.StockWriteOffs, "Списания 1С"));
        tabs.TabPages.Add(CreateSchemaTab());
        return tabs;
    }

    private TabPage CreateDatasetTab(OneCEntityDataset dataset, string title)
    {
        var context = new DatasetTabContext(dataset);
        ConfigureDatasetContext(context);

        var tab = new TabPage($"{title} ({dataset.Records.Count:N0})") { Padding = new Padding(10) };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(CreateDatasetToolbar(context), 0, 0);
        root.Controls.Add(CreateDatasetBody(context), 0, 1);
        tab.Controls.Add(root);

        RefreshDatasetRecords(context);
        return tab;
    }

    private void ConfigureDatasetContext(DatasetTabContext context)
    {
        context.RecordGrid.DataSource = context.RecordBindingSource;
        context.RecordGrid.SelectionChanged += (_, _) => RefreshRecordDetails(context);
        context.FieldGrid.DataSource = context.FieldBindingSource;
        context.SectionRowGrid.DataSource = context.SectionRowBindingSource;
        context.SectionRowGrid.SelectionChanged += (_, _) => RefreshSectionRowFields(context);
        context.SectionFieldGrid.DataSource = context.SectionFieldBindingSource;
        context.SearchTextBox.Width = 260;
        context.SearchTextBox.Font = new Font("Segoe UI", 10f);
        context.SearchTextBox.PlaceholderText = $"Поиск по {context.Dataset.DisplayName.ToLowerInvariant()}";
        context.SearchTextBox.TextChanged += (_, _) => RefreshDatasetRecords(context);
        context.SectionListBox.Dock = DockStyle.Fill;
        context.SectionListBox.Font = new Font("Segoe UI", 9.5f);
        context.SectionListBox.SelectedIndexChanged += (_, _) => RefreshSectionRows(context);
    }

    private Control CreateDatasetToolbar(DatasetTabContext context)
    {
        var panel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, 0, 0, 8) };
        panel.Controls.Add(context.SearchTextBox);
        panel.Controls.Add(new Label { AutoSize = true, Padding = new Padding(16, 10, 0, 0), Font = new Font("Segoe UI", 9f), ForeColor = Color.FromArgb(106, 97, 87), Text = context.Dataset.Summary });
        return panel;
    }

    private Control CreateDatasetBody(DatasetTabContext context)
    {
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
        content.Controls.Add(CreateCardShell("Карточки", "Реальные строки из выгрузки 1С.", context.RecordGrid), 0, 0);
        content.Controls.Add(CreateDetailsShell(context), 1, 0);
        return content;
    }

    private Control CreateDetailsShell(DatasetTabContext context)
    {
        var shell = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(16) };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(CreateRecordSummary(context), 0, 0);

        var tabs = DesktopSurfaceFactory.CreateTabControl();
        var fieldsTab = new TabPage("Поля 1С") { Padding = new Padding(8) };
        fieldsTab.Controls.Add(context.FieldGrid);
        var sectionsTab = new TabPage("Табличные части") { Padding = new Padding(8) };
        sectionsTab.Controls.Add(CreateSectionsLayout(context));
        tabs.TabPages.Add(fieldsTab);
        tabs.TabPages.Add(sectionsTab);

        root.Controls.Add(tabs, 0, 1);
        shell.Controls.Add(root);
        return shell;
    }

    private Control CreateRecordSummary(DatasetTabContext context)
    {
        context.TitleLabel.Dock = DockStyle.Top;
        context.TitleLabel.Height = 28;
        context.TitleLabel.Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold);
        context.TitleLabel.ForeColor = Color.FromArgb(47, 42, 36);
        context.SubtitleLabel.Dock = DockStyle.Top;
        context.SubtitleLabel.Height = 22;
        context.SubtitleLabel.Font = new Font("Segoe UI", 9.5f);
        context.SubtitleLabel.ForeColor = Color.FromArgb(114, 104, 93);
        context.StatusLabel.Dock = DockStyle.Top;
        context.StatusLabel.Height = 22;
        context.StatusLabel.Font = new Font("Segoe UI", 9.2f);
        context.StatusLabel.ForeColor = Color.FromArgb(88, 79, 69);

        var panel = new Panel { Dock = DockStyle.Top, Height = 82, Padding = new Padding(0, 0, 0, 10) };
        panel.Controls.Add(context.StatusLabel);
        panel.Controls.Add(context.SubtitleLabel);
        panel.Controls.Add(context.TitleLabel);
        return panel;
    }

    private Control CreateSectionsLayout(DatasetTabContext context)
    {
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 220 };
        split.Panel1.Padding = new Padding(0, 0, 8, 0);
        split.Panel1.Controls.Add(CreateCardShell("Секции", "Табличные части выбранной карточки.", context.SectionListBox));

        var right = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 220 };
        right.Panel1.Padding = new Padding(8, 0, 0, 6);
        right.Panel2.Padding = new Padding(8, 6, 0, 0);
        right.Panel1.Controls.Add(CreateCardShell("Строки секции", "Содержимое выбранной табличной части.", context.SectionRowGrid));
        right.Panel2.Controls.Add(CreateCardShell("Поля строки", "Поля конкретной строки табличной части.", context.SectionFieldGrid));

        split.Panel2.Controls.Add(right);
        return split;
    }

    private TabPage CreateSchemaTab()
    {
        var tab = new TabPage($"Схемы ({_snapshot.Schemas.Count:N0})") { Padding = new Padding(10) };
        var content = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));

        _schemaBindingSource.DataSource = _snapshot.Schemas
            .Select(schema => new SchemaRow(schema.ObjectName, schema.ObjectName, schema.Columns.Count, schema.TabularSections.Count, schema.SourceFileName))
            .OrderBy(row => row.ObjectName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _schemaGrid.DataSource = _schemaBindingSource;
        _schemaGrid.SelectionChanged += (_, _) => RefreshSchemaDetails();
        _schemaFieldGrid.DataSource = _schemaFieldBindingSource;
        _schemaSectionGrid.DataSource = _schemaSectionBindingSource;

        content.Controls.Add(CreateCardShell("Схемы 1С", "Структуры объектов из model_schema.", _schemaGrid), 0, 0);
        content.Controls.Add(CreateSchemaDetailsShell(), 1, 0);
        tab.Controls.Add(content);
        RefreshSchemaDetails();
        return tab;
    }

    private Control CreateSchemaDetailsShell()
    {
        var shell = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(16) };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        root.Controls.Add(CreateCardShell("Поля", "Полный состав полей объекта.", _schemaFieldGrid), 0, 0);
        root.Controls.Add(CreateCardShell("Табличные части", "Секции и количество колонок в них.", _schemaSectionGrid), 0, 1);
        shell.Controls.Add(root);
        return shell;
    }

    private void RefreshSchemaDetails()
    {
        var selected = GetSelectedSchema();
        if (selected is null)
        {
            _schemaFieldBindingSource.DataSource = Array.Empty<FieldRow>();
            _schemaSectionBindingSource.DataSource = Array.Empty<SchemaSectionRow>();
            return;
        }

        _schemaFieldBindingSource.DataSource = selected.Columns.Select((fieldName, index) => new FieldRow($"{index + 1:000}. {fieldName}", string.Empty)).ToArray();
        _schemaSectionBindingSource.DataSource = selected.TabularSections.Select(section => new SchemaSectionRow(section.Name, section.Columns.Count, string.Join(", ", section.Columns.Take(5)))).ToArray();
    }

    private OneCSchemaDefinition? GetSelectedSchema()
    {
        return _schemaGrid.CurrentRow?.DataBoundItem is SchemaRow row
            ? _snapshot.FindSchema(row.ObjectName)
            : _snapshot.Schemas.FirstOrDefault();
    }

    private void RefreshDatasetRecords(DatasetTabContext context)
    {
        var search = context.SearchTextBox.Text.Trim();
        context.RecordBindingSource.DataSource = context.Dataset.Records
            .Where(record => string.IsNullOrWhiteSpace(search) || MatchesSearch(record, search))
            .OrderByDescending(record => record.Date)
            .ThenBy(record => record.Title, StringComparer.OrdinalIgnoreCase)
            .Select(record => new DatasetRow(record.Reference, FirstNonEmpty(record.Code, record.Number), record.Title, record.Subtitle, record.Status, record.Date?.ToString("dd.MM.yyyy") ?? string.Empty))
            .ToArray();

        if (context.RecordGrid.Rows.Count > 0)
        {
            context.RecordGrid.Rows[0].Selected = true;
            context.RecordGrid.CurrentCell = context.RecordGrid.Rows[0].Cells[0];
        }

        RefreshRecordDetails(context);
    }

    private void RefreshRecordDetails(DatasetTabContext context)
    {
        var record = GetSelectedRecord(context);
        if (record is null)
        {
            context.TitleLabel.Text = "Карточка не выбрана";
            context.SubtitleLabel.Text = context.Dataset.Summary;
            context.StatusLabel.Text = "Выберите строку слева, чтобы посмотреть поля и табличные части.";
            context.FieldBindingSource.DataSource = Array.Empty<FieldRow>();
            context.SectionListBox.DataSource = Array.Empty<SectionListItem>();
            context.SectionRowBindingSource.DataSource = Array.Empty<SectionRow>();
            context.SectionFieldBindingSource.DataSource = Array.Empty<FieldRow>();
            return;
        }

        context.TitleLabel.Text = record.Title;
        context.SubtitleLabel.Text = string.IsNullOrWhiteSpace(record.Subtitle) ? "-" : record.Subtitle;
        context.StatusLabel.Text = string.IsNullOrWhiteSpace(record.Status) ? "-" : record.Status;
        context.FieldBindingSource.DataSource = record.Fields.Select(field => new FieldRow(field.Name, field.DisplayValue)).ToArray();
        context.SectionListBox.DataSource = record.TabularSections.Select(section => new SectionListItem(section.Name, $"{section.Name} ({section.Rows.Count:N0})")).ToArray();
        if (context.SectionListBox.Items.Count > 0)
        {
            context.SectionListBox.SelectedIndex = 0;
        }
        else
        {
            context.SectionRowBindingSource.DataSource = Array.Empty<SectionRow>();
            context.SectionFieldBindingSource.DataSource = Array.Empty<FieldRow>();
        }
    }

    private void RefreshSectionRows(DatasetTabContext context)
    {
        var section = GetSelectedSection(context);
        if (section is null)
        {
            context.SectionRowBindingSource.DataSource = Array.Empty<SectionRow>();
            context.SectionFieldBindingSource.DataSource = Array.Empty<FieldRow>();
            return;
        }

        context.SectionRowBindingSource.DataSource = section.Rows.Select(row => new SectionRow(row.RowNumber, BuildSectionPreview(row))).ToArray();
        if (context.SectionRowGrid.Rows.Count > 0)
        {
            context.SectionRowGrid.Rows[0].Selected = true;
            context.SectionRowGrid.CurrentCell = context.SectionRowGrid.Rows[0].Cells[0];
        }

        RefreshSectionRowFields(context);
    }

    private void RefreshSectionRowFields(DatasetTabContext context)
    {
        var section = GetSelectedSection(context);
        if (section is null || context.SectionRowGrid.CurrentRow?.DataBoundItem is not SectionRow row)
        {
            context.SectionFieldBindingSource.DataSource = Array.Empty<FieldRow>();
            return;
        }

        var source = section.Rows.FirstOrDefault(item => item.RowNumber == row.RowNumber);
        context.SectionFieldBindingSource.DataSource = source is null ? Array.Empty<FieldRow>() : source.Fields.Select(field => new FieldRow(field.Name, field.DisplayValue)).ToArray();
    }

    private OneCRecordSnapshot? GetSelectedRecord(DatasetTabContext context)
    {
        return context.RecordGrid.CurrentRow?.DataBoundItem is DatasetRow row
            ? context.Dataset.Records.FirstOrDefault(record => record.Reference == row.Reference)
            : context.Dataset.Records.FirstOrDefault();
    }

    private OneCTabularSectionSnapshot? GetSelectedSection(DatasetTabContext context)
    {
        var record = GetSelectedRecord(context);
        if (record is null)
        {
            return null;
        }

        return context.SectionListBox.SelectedItem is SectionListItem item
            ? record.TabularSections.FirstOrDefault(section => string.Equals(section.Name, item.Name, StringComparison.OrdinalIgnoreCase))
            : record.TabularSections.FirstOrDefault();
    }

    private static bool MatchesSearch(OneCRecordSnapshot record, string search)
    {
        return record.Title.Contains(search, StringComparison.OrdinalIgnoreCase)
            || record.Subtitle.Contains(search, StringComparison.OrdinalIgnoreCase)
            || record.Status.Contains(search, StringComparison.OrdinalIgnoreCase)
            || record.Code.Contains(search, StringComparison.OrdinalIgnoreCase)
            || record.Number.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildSectionPreview(OneCTabularSectionRowSnapshot row)
    {
        var preview = row.Fields
            .Where(field => !string.IsNullOrWhiteSpace(field.DisplayValue)
                && !string.Equals(field.Name, "Ссылка", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(field.Name, "НомерСтроки", StringComparison.OrdinalIgnoreCase))
            .Take(3)
            .Select(field => $"{field.Name}: {field.DisplayValue}")
            .ToArray();

        return preview.Length == 0 ? "Пустая строка" : string.Join(" | ", preview);
    }

    private static string FirstNonEmpty(params string[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
    }

    private static Control CreateCardShell(string title, string subtitle, Control content)
    {
        var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Padding = new Padding(16) };
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var header = new Panel { Dock = DockStyle.Top, Height = 52 };
        header.Controls.Add(new Label { Text = subtitle, Dock = DockStyle.Top, Height = 22, Font = new Font("Segoe UI", 9f), ForeColor = Color.FromArgb(114, 104, 93) });
        header.Controls.Add(new Label { Text = title, Dock = DockStyle.Top, Height = 28, Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold), ForeColor = Color.FromArgb(47, 42, 36) });
        root.Controls.Add(header, 0, 0);
        root.Controls.Add(content, 0, 1);
        panel.Controls.Add(root);
        return panel;
    }

    private sealed class DatasetTabContext
    {
        public DatasetTabContext(OneCEntityDataset dataset) => Dataset = dataset;
        public OneCEntityDataset Dataset { get; }
        public BindingSource RecordBindingSource { get; } = new();
        public BindingSource FieldBindingSource { get; } = new();
        public BindingSource SectionRowBindingSource { get; } = new();
        public BindingSource SectionFieldBindingSource { get; } = new();
        public TextBox SearchTextBox { get; } = new();
        public Label TitleLabel { get; } = new();
        public Label SubtitleLabel { get; } = new();
        public Label StatusLabel { get; } = new();
        public ListBox SectionListBox { get; } = new();
        public DataGridView RecordGrid { get; } = DesktopGridFactory.CreateGrid(Array.Empty<DatasetRow>());
        public DataGridView FieldGrid { get; } = DesktopGridFactory.CreateGrid(Array.Empty<FieldRow>());
        public DataGridView SectionRowGrid { get; } = DesktopGridFactory.CreateGrid(Array.Empty<SectionRow>());
        public DataGridView SectionFieldGrid { get; } = DesktopGridFactory.CreateGrid(Array.Empty<FieldRow>());
    }

    private sealed record DatasetRow([property: Browsable(false)] string Reference, [property: DisplayName("Код / №")] string CodeOrNumber, [property: DisplayName("Карточка")] string Title, [property: DisplayName("Контекст")] string Subtitle, [property: DisplayName("Статус")] string Status, [property: DisplayName("Дата")] string Date);
    private sealed record FieldRow([property: DisplayName("Поле")] string Name, [property: DisplayName("Значение")] string Value);
    private sealed record SectionRow([property: DisplayName("№")] int RowNumber, [property: DisplayName("Содержимое")] string Preview);
    private sealed record SectionListItem([property: Browsable(false)] string Name, string Caption) { public override string ToString() => Caption; }
    private sealed record SchemaRow([property: Browsable(false)] string ObjectName, [property: DisplayName("Объект")] string Title, [property: DisplayName("Полей")] int FieldCount, [property: DisplayName("Табличных частей")] int SectionCount, [property: DisplayName("Файл")] string SourceFile);
    private sealed record SchemaSectionRow([property: DisplayName("Секция")] string Name, [property: DisplayName("Колонок")] int ColumnCount, [property: DisplayName("Первые поля")] string Preview);
}
