using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Forms;
using WarehouseAutomatisaion.Desktop.Printing;
using WarehouseAutomatisaion.Infrastructure.Importing;

#nullable disable warnings

namespace WarehouseAutomatisaion.Desktop.Controls;

public sealed class WarehouseWorkspaceControl : UserControl
{
	private sealed class DocumentTabContext
	{
		public string Summary { get; }

		public IReadOnlyList<DocumentViewRecord> Records { get; set; } = Array.Empty<DocumentViewRecord>();


		public TextBox SearchTextBox { get; } = new TextBox();


		public Label CountLabel { get; } = new Label();


		public Label NumberLabel { get; } = new Label();


		public Label DateLabel { get; } = new Label();


		public Label StatusLabel { get; } = new Label();


		public Label RouteLabel { get; } = new Label();


		public Label LinkLabel { get; } = new Label();


		public Label SourceLabel { get; } = new Label();


		public Label CommentLabel { get; } = new Label();


		public Label TotalLabel { get; } = new Label();


		public BindingSource RecordBindingSource { get; } = new BindingSource();


		public BindingSource LineBindingSource { get; } = new BindingSource();


		public BindingSource FieldBindingSource { get; } = new BindingSource();


		public DataGridView RecordGrid { get; } = DesktopGridFactory.CreateGrid(Array.Empty<DocumentGridRow>());


		public DataGridView LineGrid { get; } = DesktopGridFactory.CreateGrid(Array.Empty<DocumentLineGridRow>());


		public DataGridView FieldGrid { get; } = DesktopGridFactory.CreateGrid(Array.Empty<FieldGridRow>());


		public DocumentTabContext(string summary)
		{
			Summary = summary;
		}
	}

	private sealed class DocumentViewRecord
	{
		public Guid? OperationalId { get; }

		public WarehouseDocumentRecord Record { get; }

		public DocumentViewRecord(Guid? operationalId, WarehouseDocumentRecord record)
		{
			OperationalId = operationalId;
			Record = record;
		}
	}

	private sealed class QueueDocumentRow
	{
		[Browsable(false)]
		public Guid? DocumentId { get; }

		[DisplayName("Документ")]
		public string Number { get; }

		[DisplayName("Дата")]
		public string Date { get; }

		[DisplayName("Маршрут")]
		public string Route { get; }

		[DisplayName("Статус")]
		public string Status { get; }

		public QueueDocumentRow(Guid? documentId, WarehouseDocumentRecord record)
		{
			DocumentId = documentId;
			Number = record.Number;
			Date = record.Date?.ToString("dd.MM") ?? "—";
			Route = (string.IsNullOrWhiteSpace(record.TargetWarehouse) ? record.SourceWarehouse : (record.SourceWarehouse + " -> " + record.TargetWarehouse));
			Status = record.Status;
		}
	}

	private sealed class StockGridRow
	{
		[Browsable(false)]
		public WarehouseStockBalanceRecord Record { get; }

		public string Code { get; }

		public string Item { get; }

		public string Warehouse { get; }

		public string BalanceSummary { get; }

		public decimal ReservedQuantity { get; }

		public decimal InTransitQuantity { get; }

		public decimal MinimumQuantity { get; }

		public string Status { get; }

		public string Actions { get; }

		public StockGridRow(WarehouseStockBalanceRecord record)
		{
			Record = record;
			Code = record.ItemCode;
			Item = record.ItemName;
			Warehouse = record.Warehouse;
			BalanceSummary = $"{record.FreeQuantity:N0} / {record.ReservedQuantity:N0} / {record.ShippedQuantity:N0}";
			ReservedQuantity = record.ReservedQuantity;
			InTransitQuantity = record.ShippedQuantity;
			MinimumQuantity = ResolveMinimumStock(record);
			Status = record.Status;
			Actions = "?";
		}
	}

	private sealed class DocumentGridRow
	{
		[Browsable(false)]
		public DocumentViewRecord View { get; }

		[Browsable(false)]
		public Guid? DocumentId { get; }

		[DisplayName("Номер")]
		public string Number { get; }

		[DisplayName("Дата")]
		public DateTime? Date { get; }

		[DisplayName("Статус")]
		public string Status { get; }

		[DisplayName("Откуда")]
		public string SourceWarehouse { get; }

		[DisplayName("Куда")]
		public string TargetWarehouse { get; }

		[DisplayName("Основание")]
		public string RelatedDocument { get; }

		[DisplayName("Строк")]
		public int Positions { get; }

		public DocumentGridRow(DocumentViewRecord view)
		{
			View = view;
			DocumentId = view.OperationalId;
			Number = view.Record.Number;
			Date = view.Record.Date;
			Status = view.Record.Status;
			SourceWarehouse = view.Record.SourceWarehouse;
			TargetWarehouse = view.Record.TargetWarehouse;
			RelatedDocument = view.Record.RelatedDocument;
			Positions = view.Record.Lines.Count;
		}
	}

	private sealed class DocumentLineGridRow
	{
		[DisplayName("№")]
		public int Number { get; }

		[DisplayName("Номенклатура")]
		public string Item { get; }

		[DisplayName("Количество")]
		public decimal Quantity { get; }

		[DisplayName("Ед.")]
		public string Unit { get; }

		[DisplayName("Источник")]
		public string SourceLocation { get; }

		[DisplayName("Назначение")]
		public string TargetLocation { get; }

		[DisplayName("Основание")]
		public string RelatedDocument { get; }

		public DocumentLineGridRow(WarehouseDocumentLineRecord record)
		{
			Number = record.RowNumber;
			Item = record.Item;
			Quantity = record.Quantity;
			Unit = record.Unit;
			SourceLocation = record.SourceLocation;
			TargetLocation = record.TargetLocation;
			RelatedDocument = record.RelatedDocument;
		}
	}

	private sealed class FieldGridRow
	{
		[DisplayName("Поле")]
		public string Name { get; }

		[DisplayName("Значение")]
		public string Value { get; }

		[DisplayName("Raw")]
		public string RawValue { get; }

		public FieldGridRow(OneCFieldValue field)
		{
			Name = field.Name;
			Value = field.DisplayValue;
			RawValue = field.RawValue;
		}
	}

	private sealed class OperationGridRow
	{
		[DisplayName("Время")]
		public DateTime LoggedAt { get; }

		[DisplayName("Пользователь")]
		public string Actor { get; }

		[DisplayName("Сущность")]
		public string EntityType { get; }

		[DisplayName("Номер")]
		public string EntityNumber { get; }

		[DisplayName("Действие")]
		public string Action { get; }

		[DisplayName("Результат")]
		public string Result { get; }

		[DisplayName("Сообщение")]
		public string Message { get; }

		public OperationGridRow(WarehouseOperationLogEntry entry)
		{
			LoggedAt = entry.LoggedAt;
			Actor = entry.Actor;
			EntityType = entry.EntityType;
			EntityNumber = entry.EntityNumber;
			Action = entry.Action;
			Result = entry.Result;
			Message = entry.Message;
		}
	}

	private const string AllWarehousesFilter = "Все склады";

	private const string AllStockModeFilter = "Все типы";

	private const string AllStockStatusFilter = "Все статусы";

	private const string CriticalStockModeFilter = "Критично";

	private const string AttentionStockModeFilter = "Под контроль";

	private const string ReservedStockModeFilter = "С резервом";

	private const string FreeStockModeFilter = "Свободный остаток";

	private readonly SalesWorkspace _salesWorkspace;

	private readonly WarehouseOperationalWorkspaceStore _store;

	private readonly OperationalWarehouseWorkspace _workspace;

	private WarehouseWorkspace _runtimeView;

	private readonly BindingSource _stockBindingSource = new BindingSource();

	private readonly BindingSource _operationBindingSource = new BindingSource();

	private readonly BindingSource _transferQueueBindingSource = new BindingSource();

	private readonly BindingSource _reservationQueueBindingSource = new BindingSource();

	private readonly BindingSource _inventoryQueueBindingSource = new BindingSource();

	private readonly BindingSource _writeOffQueueBindingSource = new BindingSource();

	private readonly DataGridView _stockGrid = DesktopGridFactory.CreateGrid(Array.Empty<StockGridRow>());

	private readonly DataGridView _transferQueueGrid = DesktopGridFactory.CreateGrid(Array.Empty<QueueDocumentRow>());

	private readonly DataGridView _reservationQueueGrid = DesktopGridFactory.CreateGrid(Array.Empty<QueueDocumentRow>());

	private readonly DataGridView _inventoryQueueGrid = DesktopGridFactory.CreateGrid(Array.Empty<QueueDocumentRow>());

	private readonly DataGridView _writeOffQueueGrid = DesktopGridFactory.CreateGrid(Array.Empty<QueueDocumentRow>());

	private readonly TextBox _stockSearchTextBox = new TextBox();

	private readonly Label _stockFilteredLabel = new Label();

	private readonly ComboBox _warehouseFilterComboBox = new ComboBox();

	private readonly ComboBox _stockModeComboBox = new ComboBox();

	private readonly ComboBox _stockStatusComboBox = new ComboBox();

	private readonly CheckBox _stockOnlyProblemsCheckBox = new CheckBox();

	private readonly TextBox _warehouseHeroSearchTextBox = new TextBox();

	private readonly TabControl _tabs = DesktopSurfaceFactory.CreateTabControl();

	private readonly Label _noteLabel = new Label();

	private readonly Label _stockPositionsLabel = new Label();

	private readonly Label _reservationsLabel = new Label();

	private readonly Label _transfersLabel = new Label();

	private readonly Label _controlLabel = new Label();

	private readonly Label _criticalItemsLabel = new Label();

	private readonly Label _attentionItemsLabel = new Label();

	private readonly Label _executionItemsLabel = new Label();

	private readonly Label _pickingItemsLabel = new Label();

	private readonly Label _criticalActionCountLabel = new Label();

	private readonly Label _transferActionCountLabel = new Label();

	private readonly Label _reservationActionCountLabel = new Label();

	private readonly Label _controlActionCountLabel = new Label();

	private readonly Label _stockItemLabel = new Label();

	private readonly Label _stockWarehouseLabel = new Label();

	private readonly Label _stockStatusLabel = new Label();

	private readonly Label _stockNumbersLabel = new Label();

	private readonly Label _transferQueueCountLabel = new Label();

	private readonly Label _reservationQueueCountLabel = new Label();

	private readonly Label _inventoryQueueCountLabel = new Label();

	private readonly Label _writeOffQueueCountLabel = new Label();

	private readonly Label _warehouseLocationLabel = new Label();

	private readonly Label _warehouseOperatorLabel = new Label();

	private readonly Label _warehouseUpdatedLabel = new Label();

	private readonly Label _stockBadgeLabel = new Label();

	private readonly Label _stockCodeValueLabel = new Label();

	private readonly Label _stockNameValueLabel = new Label();

	private readonly Label _stockUnitValueLabel = new Label();

	private readonly Label _stockBarcodeValueLabel = new Label();

	private readonly Label _stockFreeValueLabel = new Label();

	private readonly Label _stockReservedValueLabel = new Label();

	private readonly Label _stockTransitValueLabel = new Label();

	private readonly Label _stockMinimumValueLabel = new Label();

	private readonly Label _stockDeficitValueLabel = new Label();

	private readonly Label _stockMovementLogLabel = new Label();

	private readonly Label _stockRelatedDocumentsLabel = new Label();

	private readonly DataGridView _operationsGrid = DesktopGridFactory.CreateGrid(Array.Empty<OperationGridRow>());

	private readonly DocumentTabContext _transferContext = new DocumentTabContext("Внутренние перемещения и маршрут между складами.");

	private readonly DocumentTabContext _reservationContext = new DocumentTabContext("Резервы формируются из текущих заказов продаж и обновляются автоматически.");

	private readonly DocumentTabContext _inventoryContext = new DocumentTabContext("Инвентаризации работают локально и корректируют остаток после проведения.");

	private readonly DocumentTabContext _writeOffContext = new DocumentTabContext("Списания и потери фиксируются как локальные складские документы.");

	private readonly System.Windows.Forms.Timer _searchDebounceTimer = new System.Windows.Forms.Timer();

	private readonly System.Windows.Forms.Timer _persistDebounceTimer = new System.Windows.Forms.Timer();

	private readonly System.Windows.Forms.Timer _refreshDebounceTimer = new System.Windows.Forms.Timer();

	private Action? _pendingSearchRefresh;

	private bool _stockFilterEventsSuspended;

	private bool _refreshPendingWhileHidden;

	private bool _savePending;

	private bool _refreshReferenceDataPending;

	private bool _rebuildRuntimeViewPending;

	private bool _notifySalesWorkspacePending;

	private bool _suppressSalesWorkspaceChangedHandler;

	private bool _syncingWarehouseSearch;

	public WarehouseWorkspaceControl(SalesWorkspace salesWorkspace, WarehouseOperationalWorkspaceStore? store = null, OperationalWarehouseWorkspace? workspace = null, WarehouseWorkspace? runtimeView = null)
	{
		_salesWorkspace = salesWorkspace;
		_store = store ?? WarehouseOperationalWorkspaceStore.CreateDefault();
		_workspace = workspace ?? _store.LoadOrCreate(string.IsNullOrWhiteSpace(salesWorkspace.CurrentOperator) ? Environment.UserName : salesWorkspace.CurrentOperator, salesWorkspace);
		_runtimeView = runtimeView ?? WarehouseWorkspace.Create(salesWorkspace);
		Dock = DockStyle.Fill;
		BackColor = DesktopTheme.AppBackground;
		ConfigureStockGrid();
		_stockGrid.DataSource = _stockBindingSource;
		_stockGrid.SelectionChanged += delegate
		{
			RefreshStockDetails();
		};
		_stockGrid.CellPainting += HandleStockGridCellPainting;
		_stockSearchTextBox.TextChanged += delegate
		{
			SynchronizeWarehouseSearch(_stockSearchTextBox, _warehouseHeroSearchTextBox);
			ScheduleSearchRefresh(RefreshStockGrid);
		};
		_warehouseHeroSearchTextBox.TextChanged += delegate
		{
			SynchronizeWarehouseSearch(_warehouseHeroSearchTextBox, _stockSearchTextBox);
		};
		_stockGrid.CellFormatting += HandleStatusCellFormatting;
		_transferQueueGrid.CellFormatting += HandleStatusCellFormatting;
		_reservationQueueGrid.CellFormatting += HandleStatusCellFormatting;
		_inventoryQueueGrid.CellFormatting += HandleStatusCellFormatting;
		_writeOffQueueGrid.CellFormatting += HandleStatusCellFormatting;
		_searchDebounceTimer.Interval = 180;
		_searchDebounceTimer.Tick += HandleSearchDebounceTick;
		_persistDebounceTimer.Interval = 750;
		_persistDebounceTimer.Tick += HandlePersistDebounceTick;
		_refreshDebounceTimer.Interval = 120;
		_refreshDebounceTimer.Tick += HandleRefreshDebounceTick;
		ConfigureFilterComboBox(_warehouseFilterComboBox);
		ConfigureFilterComboBox(_stockModeComboBox, 176);
		ConfigureFilterComboBox(_stockStatusComboBox, 164);
		_warehouseFilterComboBox.SelectedIndexChanged += delegate
		{
			if (!_stockFilterEventsSuspended)
			{
				RefreshStockGrid();
			}
		};
		_stockModeComboBox.SelectedIndexChanged += delegate
		{
			if (!_stockFilterEventsSuspended)
			{
				RefreshStockGrid();
			}
		};
		_stockStatusComboBox.SelectedIndexChanged += delegate
		{
			if (!_stockFilterEventsSuspended)
			{
				RefreshStockGrid();
			}
		};
		_stockOnlyProblemsCheckBox.CheckedChanged += delegate
		{
			if (!_stockFilterEventsSuspended)
			{
				RefreshStockGrid();
			}
		};
		ConfigureQueueGrid(_transferQueueGrid, _transferQueueBindingSource, _transferContext);
		ConfigureQueueGrid(_reservationQueueGrid, _reservationQueueBindingSource, _reservationContext);
		ConfigureQueueGrid(_inventoryQueueGrid, _inventoryQueueBindingSource, _inventoryContext);
		ConfigureQueueGrid(_writeOffQueueGrid, _writeOffQueueBindingSource, _writeOffContext);
		ConfigureDocumentContext(_transferContext);
		ConfigureDocumentContext(_reservationContext);
		ConfigureDocumentContext(_inventoryContext);
		ConfigureDocumentContext(_writeOffContext);
		BuildLayout();
		RefreshAll();
		_workspace.Changed += HandleWorkspaceChanged;
		_salesWorkspace.Changed += HandleSalesWorkspaceChanged;
		base.VisibleChanged += HandleVisibilityChanged;
		base.Disposed += delegate
		{
			FlushPendingSave();
			_workspace.Changed -= HandleWorkspaceChanged;
			_salesWorkspace.Changed -= HandleSalesWorkspaceChanged;
			base.VisibleChanged -= HandleVisibilityChanged;
			_searchDebounceTimer.Stop();
			_searchDebounceTimer.Tick -= HandleSearchDebounceTick;
			_searchDebounceTimer.Dispose();
			_persistDebounceTimer.Stop();
			_persistDebounceTimer.Tick -= HandlePersistDebounceTick;
			_persistDebounceTimer.Dispose();
			_refreshDebounceTimer.Stop();
			_refreshDebounceTimer.Tick -= HandleRefreshDebounceTick;
			_refreshDebounceTimer.Dispose();
		};
	}

	private void HandleWorkspaceChanged(object? sender, EventArgs e)
	{
		object sender2 = sender;
		EventArgs e2 = e;
		if (base.IsDisposed)
		{
			return;
		}
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				HandleWorkspaceChanged(sender2, e2);
			});
			return;
		}
		if (!CanRefreshNow())
		{
			_refreshPendingWhileHidden = true;
		}
		SchedulePersist();
		_rebuildRuntimeViewPending = true;
		ScheduleRefresh(notifySalesWorkspace: true);
	}

	private void HandleSalesWorkspaceChanged(object? sender, EventArgs e)
	{
		object sender2 = sender;
		EventArgs e2 = e;
		if (base.IsDisposed)
		{
			return;
		}
		if (base.InvokeRequired)
		{
			BeginInvoke(delegate
			{
				HandleSalesWorkspaceChanged(sender2, e2);
			});
		}
		else if (!_suppressSalesWorkspaceChangedHandler)
		{
			if (!CanRefreshNow())
			{
				_refreshPendingWhileHidden = true;
			}
			_refreshReferenceDataPending = true;
			_rebuildRuntimeViewPending = true;
			ScheduleRefresh();
		}
	}

	private void HandleVisibilityChanged(object? sender, EventArgs e)
	{
		if (CanRefreshNow() && _refreshPendingWhileHidden)
		{
			_refreshPendingWhileHidden = false;
			RunPendingRefresh();
		}
	}

	private bool CanRefreshNow()
	{
		return !base.IsDisposed && base.IsHandleCreated && base.Visible && base.Parent != null;
	}

	private void SchedulePersist()
	{
		_savePending = true;
		_persistDebounceTimer.Stop();
		_persistDebounceTimer.Start();
	}

	private void HandlePersistDebounceTick(object? sender, EventArgs e)
	{
		_persistDebounceTimer.Stop();
		FlushPendingSave();
	}

	private void ScheduleRefresh(bool notifySalesWorkspace = false)
	{
		if (notifySalesWorkspace)
		{
			_notifySalesWorkspacePending = true;
		}
		_refreshDebounceTimer.Stop();
		_refreshDebounceTimer.Start();
	}

	private void HandleRefreshDebounceTick(object? sender, EventArgs e)
	{
		_refreshDebounceTimer.Stop();
		RunPendingRefresh();
	}

	private void RunPendingRefresh()
	{
		if (!CanRefreshNow())
		{
			_refreshPendingWhileHidden = true;
			return;
		}
		if (_refreshReferenceDataPending)
		{
			_refreshReferenceDataPending = false;
			_workspace.RefreshReferenceData(_salesWorkspace);
		}
		if (_rebuildRuntimeViewPending)
		{
			_rebuildRuntimeViewPending = false;
			_runtimeView = WarehouseWorkspace.Create(_salesWorkspace);
		}
		RefreshAll();
		if (!_notifySalesWorkspacePending)
		{
			return;
		}
		_notifySalesWorkspacePending = false;
		_suppressSalesWorkspaceChangedHandler = true;
		try
		{
			_salesWorkspace.NotifyExternalChange();
		}
		finally
		{
			_suppressSalesWorkspaceChangedHandler = false;
		}
	}

	private void FlushPendingSave()
	{
		if (_savePending)
		{
			_savePending = false;
			_store.Save(_workspace);
		}
	}

	private void BuildLayout()
	{
		Controls.Clear();
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 3,
			Padding = new Padding(18, 16, 18, 18),
			BackColor = DesktopTheme.AppBackground
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.Controls.Add(CreateWarehouseHero(), 0, 0);
		tableLayoutPanel.Controls.Add(CreateWarehouseMetricsStrip(), 0, 1);
		tableLayoutPanel.Controls.Add(CreateTabs(), 0, 2);
		base.Controls.Add(tableLayoutPanel);
	}

	private Control CreateWarehouseHero()
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			ColumnCount = 2,
			RowCount = 1,
			Height = 104,
			Margin = new Padding(0, 0, 0, 16)
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(0, 0, 12, 0)
		};
		panel.Controls.Add(CreateWarehouseMetaStrip());
		panel.Controls.Add(new Label
		{
			Text = "Остатки, перемещения, резервы и инвентаризация",
			Dock = DockStyle.Top,
			Height = 26,
			Font = DesktopTheme.SubtitleFont(11f),
			ForeColor = DesktopTheme.TextSecondary
		});
		panel.Controls.Add(new Label
		{
			Text = "Склад",
			Dock = DockStyle.Top,
			Height = 52,
			Font = DesktopTheme.TitleFont(23f),
			ForeColor = DesktopTheme.TextPrimary
		});
		tableLayoutPanel.Controls.Add(panel, 0, 0);
		tableLayoutPanel.Controls.Add(CreateWarehouseHeroActions(), 1, 0);
		return tableLayoutPanel;
	}

	private Control CreateWarehouseMetaStrip()
	{
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 28,
			AutoSize = false,
			WrapContents = false,
			Margin = new Padding(0, 10, 0, 0)
		};
		StyleMetaLabel(_warehouseLocationLabel);
		StyleMetaLabel(_warehouseOperatorLabel);
		StyleMetaLabel(_warehouseUpdatedLabel);
		flowLayoutPanel.Controls.Add(_warehouseLocationLabel);
		flowLayoutPanel.Controls.Add(CreateMetaSeparator());
		flowLayoutPanel.Controls.Add(_warehouseOperatorLabel);
		flowLayoutPanel.Controls.Add(CreateMetaSeparator());
		flowLayoutPanel.Controls.Add(_warehouseUpdatedLabel);
		return flowLayoutPanel;
	}

	private Control CreateWarehouseHeroActions()
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 3,
			RowCount = 1,
			Margin = new Padding(0)
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		tableLayoutPanel.Controls.Add(CreateSearchChrome(_warehouseHeroSearchTextBox, "Поиск по коду, товару или штрихкоду...", 0), 0, 0);
		Control control = CreateReferenceButton("Экспорт", delegate
		{
			ExportCurrentStockView();
		}, primary: false, 116);
		control.Margin = new Padding(12, 0, 0, 0);
		tableLayoutPanel.Controls.Add(control, 1, 0);
		Control control2 = CreateCreateActionButton();
		control2.Margin = new Padding(12, 0, 0, 0);
		tableLayoutPanel.Controls.Add(control2, 2, 0);
		return tableLayoutPanel;
	}

	private Control CreateWarehouseMetricsStrip()
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			ColumnCount = 4,
			RowCount = 1,
			Height = 110,
			Margin = new Padding(0, 0, 0, 16)
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
		tableLayoutPanel.Controls.Add(CreateWarehouseMetricCard("Критичных остатков", "Требуют внимания", _criticalItemsLabel, Color.FromArgb(255, 244, 244), Color.FromArgb(255, 84, 84), Color.FromArgb(255, 92, 92)), 0, 0);
		tableLayoutPanel.Controls.Add(CreateWarehouseMetricCard("Перемещений в работе", "Идет выполнение", _executionItemsLabel, Color.FromArgb(255, 248, 237), Color.FromArgb(255, 176, 32), Color.FromArgb(255, 151, 34)), 1, 0);
		tableLayoutPanel.Controls.Add(CreateWarehouseMetricCard("Резервов к сборке", "Ожидают отгрузки", _pickingItemsLabel, Color.FromArgb(239, 244, 255), Color.FromArgb(84, 97, 245), Color.FromArgb(55, 83, 239)), 2, 0);
		tableLayoutPanel.Controls.Add(CreateWarehouseMetricCard("Расхождений инвентаризации", "Требуют проверки", _controlLabel, Color.FromArgb(240, 250, 244), Color.FromArgb(38, 168, 91), Color.FromArgb(42, 158, 82)), 3, 0);
		return tableLayoutPanel;
	}

	private Control CreateWarehouseMetricCard(string title, string subtitle, Label valueLabel, Color iconBackColor, Color valueColor, Color iconColor)
	{
		valueLabel.Dock = DockStyle.Top;
		valueLabel.Height = 40;
		valueLabel.Font = DesktopTheme.TitleFont(18f);
		valueLabel.ForeColor = valueColor;
		valueLabel.Margin = new Padding(0);
		RoundedSurfacePanel roundedSurfacePanel = new RoundedSurfacePanel
		{
			Dock = DockStyle.Fill,
			BackColor = DesktopTheme.Surface,
			BorderColor = DesktopTheme.Border,
			BorderThickness = 1,
			CornerRadius = 18,
			DrawShadow = false,
			Margin = new Padding(0, 0, 16, 0),
			Padding = new Padding(18)
		};
		Panel panel = new Panel
		{
			Dock = DockStyle.Top,
			Height = 34
		};
		RoundedSurfacePanel roundedSurfacePanel2 = new RoundedSurfacePanel
		{
			Dock = DockStyle.Left,
			Width = 36,
			BackColor = iconBackColor,
			BorderColor = iconBackColor,
			BorderThickness = 0,
			CornerRadius = 12,
			DrawShadow = false,
			Margin = new Padding(0)
		};
		roundedSurfacePanel2.Controls.Add(new Label
		{
			Text = "●",
			Dock = DockStyle.Fill,
			TextAlign = ContentAlignment.MiddleCenter,
			Font = DesktopTheme.EmphasisFont(11f),
			ForeColor = iconColor
		});
		panel.Controls.Add(new Label
		{
			Text = title,
			Dock = DockStyle.Fill,
			Padding = new Padding(14, 6, 0, 0),
			Font = DesktopTheme.EmphasisFont(10f),
			ForeColor = DesktopTheme.TextPrimary
		});
		panel.Controls.Add(roundedSurfacePanel2);
		roundedSurfacePanel.Controls.Add(new Label
		{
			Text = subtitle,
			Dock = DockStyle.Top,
			Height = 18,
			Font = DesktopTheme.SubtitleFont(9.4f),
			ForeColor = DesktopTheme.TextSecondary
		});
		roundedSurfacePanel.Controls.Add(valueLabel);
		roundedSurfacePanel.Controls.Add(panel);
		return roundedSurfacePanel;
	}

	private Control CreateCreateActionButton()
	{
		ContextMenuStrip contextMenuStrip = new ContextMenuStrip
		{
			ShowImageMargin = false,
			BackColor = DesktopTheme.Surface,
			Font = DesktopTheme.BodyFont(9.6f)
		};
		contextMenuStrip.Items.Add("Перемещение", null, delegate
		{
			CreateTransfer();
		});
		contextMenuStrip.Items.Add("Инвентаризация", null, delegate
		{
			CreateInventory();
		});
		contextMenuStrip.Items.Add("Списание", null, delegate
		{
			CreateWriteOff();
		});
		contextMenuStrip.Items.Add("Резерв", null, delegate
		{
			OpenQueueTab(2, _reservationContext);
		});
		Button button = CreateReferenceButton("Создать ▾", delegate
		{
		}, primary: true, 146);
		button.Click += delegate
		{
			contextMenuStrip.Show(button, new Point(0, button.Height + 4));
		};
		return button;
	}

	private static Button CreateReferenceButton(string text, EventHandler handler, bool primary, int width)
	{
		Button button = new Button
		{
			Text = text,
			Height = 42,
			Margin = new Padding(12, 0, 0, 0),
			FlatStyle = FlatStyle.Flat,
			Font = DesktopTheme.EmphasisFont(9.8f),
			Cursor = Cursors.Hand,
			UseVisualStyleBackColor = false,
			BackColor = primary ? DesktopTheme.Primary : DesktopTheme.Surface,
			ForeColor = primary ? Color.White : DesktopTheme.TextSecondary
		};
		if (width > 0)
		{
			button.Width = width;
		}
		else
		{
			button.Dock = DockStyle.Fill;
			button.Margin = new Padding(0, 0, 10, 10);
		}
		button.FlatAppearance.BorderSize = 1;
		button.FlatAppearance.BorderColor = primary ? DesktopTheme.Primary : DesktopTheme.Border;
		button.FlatAppearance.MouseDownBackColor = primary ? DesktopTheme.PrimaryHover : DesktopTheme.SurfaceMuted;
		button.FlatAppearance.MouseOverBackColor = primary ? DesktopTheme.PrimaryHover : DesktopTheme.SurfaceMuted;
		ApplyRoundedRegion(button, 12);
		button.Click += handler;
		return button;
	}

	private static Control CreateSearchChrome(TextBox textBox, string placeholder, int width)
	{
		textBox.BorderStyle = BorderStyle.None;
		textBox.Font = DesktopTheme.BodyFont(10f);
		textBox.ForeColor = DesktopTheme.TextPrimary;
		textBox.BackColor = DesktopTheme.Surface;
		textBox.PlaceholderText = placeholder;
		textBox.Margin = new Padding(0);
		textBox.Dock = DockStyle.Fill;
		RoundedSurfacePanel roundedSurfacePanel = new RoundedSurfacePanel
		{
			Height = 42,
			BackColor = DesktopTheme.Surface,
			BorderColor = DesktopTheme.Border,
			BorderThickness = 1,
			CornerRadius = 12,
			DrawShadow = false,
			Margin = new Padding(0)
		};
		if (width > 0)
		{
			roundedSurfacePanel.Width = width;
		}
		else
		{
			roundedSurfacePanel.Dock = DockStyle.Fill;
		}
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(16, 11, 16, 9),
			BackColor = Color.Transparent
		};
		panel.Controls.Add(textBox);
		roundedSurfacePanel.Controls.Add(panel);
		return roundedSurfacePanel;
	}

	private static Control CreateFilterChrome(ComboBox comboBox, int width)
	{
		RoundedSurfacePanel roundedSurfacePanel = new RoundedSurfacePanel
		{
			Width = width,
			Height = 40,
			BackColor = DesktopTheme.Surface,
			BorderColor = DesktopTheme.Border,
			BorderThickness = 1,
			CornerRadius = 12,
			DrawShadow = false,
			Margin = new Padding(0, 0, 12, 0),
			Padding = new Padding(10, 6, 10, 6)
		};
		comboBox.Dock = DockStyle.Fill;
		comboBox.Margin = new Padding(0);
		roundedSurfacePanel.Controls.Add(comboBox);
		return roundedSurfacePanel;
	}

	private static void StyleMetaLabel(Label label)
	{
		label.AutoSize = true;
		label.Margin = new Padding(0);
		label.Font = DesktopTheme.BodyFont(9.4f);
		label.ForeColor = DesktopTheme.TextSecondary;
	}

	private static Control CreateMetaSeparator()
	{
		return new Label
		{
			AutoSize = true,
			Text = "?",
			Margin = new Padding(10, 0, 10, 0),
			Font = DesktopTheme.BodyFont(9.4f),
			ForeColor = DesktopTheme.TextMuted
		};
	}

	private static void ApplyRoundedRegion(Control control, int radius)
	{
		void Apply()
		{
			if (control.Width <= 1 || control.Height <= 1)
			{
				return;
			}
			using GraphicsPath graphicsPath = new GraphicsPath();
			int num = radius * 2;
			graphicsPath.AddArc(0, 0, num, num, 180f, 90f);
			graphicsPath.AddArc(control.Width - num, 0, num, num, 270f, 90f);
			graphicsPath.AddArc(control.Width - num, control.Height - num, num, num, 0f, 90f);
			graphicsPath.AddArc(0, control.Height - num, num, num, 90f, 90f);
			graphicsPath.CloseFigure();
			control.Region?.Dispose();
			control.Region = new Region(graphicsPath);
		}
		control.Resize += delegate
		{
			Apply();
		};
		control.HandleCreated += delegate
		{
			Apply();
		};
	}

	private Control CreateHeader()
	{
		Panel panel = new Panel
		{
			Dock = DockStyle.Top,
			Height = 66,
			Padding = new Padding(0, 0, 0, 8)
		};
		panel.Controls.Add(new Label
		{
			Text = "Склад работает автономно: остатки, резервы, перемещения, инвентаризация и списания в одном рабочем месте.",
			Dock = DockStyle.Top,
			Height = 22,
			Font = new Font("Segoe UI", 9.5f),
			ForeColor = Color.FromArgb(114, 104, 93)
		});
		panel.Controls.Add(new Label
		{
			Text = "Склад и логистика",
			Dock = DockStyle.Top,
			Height = 36,
			Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold),
			ForeColor = Color.FromArgb(40, 36, 31)
		});
		return panel;
	}

	private Control CreateControlTower()
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 2,
			RowCount = 1,
			Padding = new Padding(0, 0, 0, 12)
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37f));
		tableLayoutPanel.Controls.Add(CreateTowerOverviewPanel(), 0, 0);
		tableLayoutPanel.Controls.Add(CreateActionCenterPanel(), 1, 0);
		return tableLayoutPanel;
	}

	private Control CreateTowerOverviewPanel()
	{
		Panel panel = DesktopSurfaceFactory.CreateCardShell();
		panel.Margin = new Padding(0, 0, 12, 12);
		panel.Padding = new Padding(20);
		panel.Height = 220;
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			Height = 32,
			WrapContents = false,
			Margin = new Padding(0)
		};
		flowLayoutPanel.Controls.Add(DesktopSurfaceFactory.CreateInfoChip("MySQL", DesktopTheme.PrimarySoft, DesktopTheme.SidebarButtonActiveText));
		flowLayoutPanel.Controls.Add(DesktopSurfaceFactory.CreateInfoChip("Desktop only"));
		flowLayoutPanel.Controls.Add(DesktopSurfaceFactory.CreateInfoChip("Task-driven", DesktopTheme.InfoSoft, DesktopTheme.Info));
		FlowLayoutPanel flowLayoutPanel2 = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			AutoScroll = false,
			WrapContents = true,
			Margin = new Padding(0, 0, 0, 12)
		};
		flowLayoutPanel2.Controls.Add(DesktopSurfaceFactory.CreateActionButton("Новое перемещение", delegate
		{
			CreateTransfer();
		}, DesktopButtonTone.Primary, new Padding(0, 0, 10, 0)));
		flowLayoutPanel2.Controls.Add(DesktopSurfaceFactory.CreateActionButton("Инвентаризация", delegate
		{
			CreateInventory();
		}, DesktopButtonTone.Secondary, new Padding(0, 0, 10, 0)));
		flowLayoutPanel2.Controls.Add(DesktopSurfaceFactory.CreateActionButton("Списание", delegate
		{
			CreateWriteOff();
		}, DesktopButtonTone.Ghost, new Padding(0, 0, 0, 0)));
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 4,
			RowCount = 1,
			Margin = new Padding(0)
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
		tableLayoutPanel.Controls.Add(CreateTowerMetricCard("Критичные остатки", "Позиции без свободного остатка.", _criticalItemsLabel, DesktopTheme.Danger), 0, 0);
		tableLayoutPanel.Controls.Add(CreateTowerMetricCard("Под контролем", "Резервы и позиции с риском.", _attentionItemsLabel, DesktopTheme.Warning), 1, 0);
		tableLayoutPanel.Controls.Add(CreateTowerMetricCard("Документы в работе", "Открытые складские задачи.", _executionItemsLabel, DesktopTheme.Info), 2, 0);
		tableLayoutPanel.Controls.Add(CreateTowerMetricCard("Резервы к сборке", "Что нужно обеспечить и подготовить.", _pickingItemsLabel, DesktopTheme.Primary), 3, 0);
		panel.Controls.Add(tableLayoutPanel);
		panel.Controls.Add(flowLayoutPanel2);
		panel.Controls.Add(new Label
		{
			Text = "Операционный центр склада. Здесь начинается день кладовщика: дефицит, маршруты между складами, инвентаризация и работа по резервам.",
			Dock = DockStyle.Top,
			Height = 38,
			Font = DesktopTheme.BodyFont(9.2f),
			ForeColor = DesktopTheme.TextSecondary
		});
		panel.Controls.Add(new Label
		{
			Text = "Сегодня на складе",
			Dock = DockStyle.Top,
			Height = 30,
			Font = DesktopTheme.TitleFont(13f),
			ForeColor = DesktopTheme.TextPrimary
		});
		panel.Controls.Add(flowLayoutPanel);
		return panel;
	}

	private Control CreateActionCenterPanel()
	{
		Panel panel = DesktopSurfaceFactory.CreateCardShell();
		panel.Padding = new Padding(18);
		panel.Height = 220;
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 4
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));
		tableLayoutPanel.Controls.Add(CreateActionCenterRow("Критичные остатки", "Позиции, где товар уже закончился или ушел в дефицит.", _criticalActionCountLabel, delegate
		{
			ActivateCriticalStockView();
		}), 0, 0);
		tableLayoutPanel.Controls.Add(CreateActionCenterRow("Перемещения в работе", "Маршруты, которые нужно собрать и довести до завершения.", _transferActionCountLabel, delegate
		{
			OpenQueueTab(1, _transferContext);
		}), 0, 1);
		tableLayoutPanel.Controls.Add(CreateActionCenterRow("Резервы к сборке", "Заказы продаж, которые уже держат остаток на складе.", _reservationActionCountLabel, delegate
		{
			OpenQueueTab(2, _reservationContext);
		}), 0, 2);
		tableLayoutPanel.Controls.Add(CreateActionCenterRow("Контроль и корректировки", "Инвентаризации и списания, которые еще не проведены.", _controlActionCountLabel, delegate
		{
			OpenControlFocusView();
		}), 0, 3);
		panel.Controls.Add(tableLayoutPanel);
		panel.Controls.Add(new Label
		{
			Text = "Сначала разберите эти точки. Блок показывает только то, что реально требует действия, а не весь архив документов.",
			Dock = DockStyle.Top,
			Height = 36,
			Font = DesktopTheme.BodyFont(9f),
			ForeColor = DesktopTheme.TextSecondary
		});
		panel.Controls.Add(new Label
		{
			Text = "Что требует внимания",
			Dock = DockStyle.Top,
			Height = 28,
			Font = DesktopTheme.TitleFont(12f),
			ForeColor = DesktopTheme.TextPrimary
		});
		return panel;
	}

	private static Control CreateTowerMetricCard(string title, string summary, Label valueLabel, Color accentColor)
	{
		valueLabel.Dock = DockStyle.Top;
		valueLabel.Height = 36;
		valueLabel.Font = DesktopTheme.TitleFont(16f);
		valueLabel.ForeColor = DesktopTheme.TextPrimary;
		Panel panel = DesktopSurfaceFactory.CreateCardShell();
		panel.Dock = DockStyle.Fill;
		panel.BackColor = DesktopTheme.SurfaceAlt;
		panel.Margin = new Padding(0, 0, 10, 0);
		panel.Padding = new Padding(14, 12, 14, 12);
		panel.Controls.Add(new Label
		{
			Text = summary,
			Dock = DockStyle.Top,
			Height = 34,
			Font = DesktopTheme.BodyFont(8.8f),
			ForeColor = DesktopTheme.TextSecondary
		});
		panel.Controls.Add(valueLabel);
		panel.Controls.Add(new Label
		{
			Text = title,
			Dock = DockStyle.Top,
			Height = 22,
			Font = DesktopTheme.EmphasisFont(9.6f),
			ForeColor = accentColor
		});
		return panel;
	}

	private static Control CreateActionCenterRow(string title, string summary, Label countLabel, EventHandler openHandler)
	{
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = Color.Transparent,
			Padding = new Padding(0, 8, 0, 8)
		};
		countLabel.AutoSize = true;
		countLabel.Dock = DockStyle.Right;
		countLabel.Font = DesktopTheme.TitleFont(14f);
		countLabel.ForeColor = DesktopTheme.TextPrimary;
		Button button = DesktopSurfaceFactory.CreateActionButton("Открыть", openHandler, DesktopButtonTone.Ghost, new Padding(0));
		button.Dock = DockStyle.Right;
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Right,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = false,
			Margin = new Padding(0)
		};
		flowLayoutPanel.Controls.Add(countLabel);
		flowLayoutPanel.Controls.Add(button);
		panel.Controls.Add(flowLayoutPanel);
		panel.Controls.Add(new Label
		{
			Text = summary,
			Dock = DockStyle.Top,
			Height = 18,
			Font = DesktopTheme.BodyFont(8.8f),
			ForeColor = DesktopTheme.TextSecondary
		});
		panel.Controls.Add(new Label
		{
			Text = title,
			Dock = DockStyle.Top,
			Height = 20,
			Font = DesktopTheme.EmphasisFont(),
			ForeColor = DesktopTheme.TextPrimary
		});
		return panel;
	}

	private Control CreateNote()
	{
		_noteLabel.Dock = DockStyle.Top;
		_noteLabel.Height = 48;
		_noteLabel.Font = new Font("Segoe UI", 9.2f);
		_noteLabel.ForeColor = Color.FromArgb(97, 88, 80);
		Panel panel = new Panel
		{
			Dock = DockStyle.Top,
			Height = 62,
			Padding = new Padding(12, 10, 12, 0),
			BackColor = Color.FromArgb(255, 250, 241)
		};
		panel.Controls.Add(_noteLabel);
		return panel;
	}

	private Control CreateSummaryCards()
	{
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			FlowDirection = FlowDirection.LeftToRight,
			WrapContents = true,
			Padding = new Padding(0, 0, 0, 12)
		};
		flowLayoutPanel.Controls.Add(CreateSummaryCard("Позиции", "Что сейчас видно по складским остаткам.", _stockPositionsLabel, Color.FromArgb(79, 174, 92)));
		flowLayoutPanel.Controls.Add(CreateSummaryCard("Резервы", "Что уже закреплено под продажи.", _reservationsLabel, Color.FromArgb(123, 104, 163)));
		flowLayoutPanel.Controls.Add(CreateSummaryCard("Перемещения", "Активные и завершенные складские документы.", _transfersLabel, Color.FromArgb(201, 134, 64)));
		flowLayoutPanel.Controls.Add(CreateSummaryCard("Контроль", "Инвентаризации и списания в локальном контуре.", _controlLabel, Color.FromArgb(196, 92, 83)));
		return flowLayoutPanel;
	}

	private static Control CreateSummaryCard(string title, string hint, Label valueLabel, Color accentColor)
	{
		valueLabel.Dock = DockStyle.Top;
		valueLabel.Height = 36;
		valueLabel.Font = new Font("Segoe UI Semibold", 18f, FontStyle.Bold);
		valueLabel.ForeColor = Color.FromArgb(43, 39, 34);
		Panel panel = DesktopSurfaceFactory.CreateCardShell();
		panel.Width = 244;
		panel.Height = 96;
		panel.Margin = new Padding(0, 0, 12, 12);
		panel.Padding = new Padding(14, 12, 14, 12);
		Panel value = new Panel
		{
			Dock = DockStyle.Left,
			Width = 5,
			BackColor = accentColor
		};
		Panel panel2 = new Panel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(12, 0, 0, 0)
		};
		panel2.Controls.Add(new Label
		{
			Text = hint,
			Dock = DockStyle.Top,
			Height = 34,
			Font = new Font("Segoe UI", 9f),
			ForeColor = Color.FromArgb(112, 103, 92)
		});
		panel2.Controls.Add(valueLabel);
		panel2.Controls.Add(new Label
		{
			Text = title,
			Dock = DockStyle.Top,
			Height = 22,
			Font = new Font("Segoe UI Semibold", 10.5f, FontStyle.Bold),
			ForeColor = Color.FromArgb(68, 61, 53)
		});
		panel.Controls.Add(panel2);
		panel.Controls.Add(value);
		return panel;
	}

	private Control CreateTabs()
	{
		_tabs.TabPages.Clear();
		TabControl tabs = _tabs;
		tabs.TabPages.Add(CreateStockTab());
		tabs.TabPages.Add(CreateDocumentTab("Перемещения", _transferContext, CreateTransfersToolbar()));
		tabs.TabPages.Add(CreateDocumentTab("Резервы", _reservationContext, CreateReservationToolbar()));
		tabs.TabPages.Add(CreateDocumentTab("Инвентаризация", _inventoryContext, CreateInventoryToolbar()));
		tabs.TabPages.Add(CreateDocumentTab("Списания", _writeOffContext, CreateWriteOffToolbar()));
		return tabs;
	}

	private TabPage CreateStockTab()
	{
		ConfigureDigestLabel(_stockFilteredLabel);
		StyleStockDetailLabel(_stockBadgeLabel, 9.2f, DesktopTheme.Danger, Color.FromArgb(255, 241, 241), padding: new Padding(10, 5, 10, 5), autoSize: true);
		StyleStockDetailValue(_stockItemLabel, 13.5f, FontStyle.Bold, DesktopTheme.TextPrimary);
		StyleStockDetailValue(_stockWarehouseLabel, 9.8f, FontStyle.Regular, DesktopTheme.TextSecondary);
		StyleStockDetailValue(_stockStatusLabel, 9.6f, FontStyle.Regular, DesktopTheme.TextSecondary);
		StyleStockDetailValue(_stockNumbersLabel, 9.4f, FontStyle.Regular, DesktopTheme.TextSecondary);
		StyleStockDetailValue(_stockCodeValueLabel, 9.6f, FontStyle.Regular, DesktopTheme.TextPrimary);
		StyleStockDetailValue(_stockNameValueLabel, 9.6f, FontStyle.Regular, DesktopTheme.TextPrimary);
		StyleStockDetailValue(_stockUnitValueLabel, 9.6f, FontStyle.Regular, DesktopTheme.TextPrimary);
		StyleStockDetailValue(_stockBarcodeValueLabel, 9.6f, FontStyle.Regular, DesktopTheme.TextPrimary);
		StyleStockMetricValue(_stockFreeValueLabel, Color.FromArgb(38, 168, 91));
		StyleStockMetricValue(_stockReservedValueLabel, Color.FromArgb(255, 151, 34));
		StyleStockMetricValue(_stockTransitValueLabel, DesktopTheme.Primary);
		StyleStockMetricText(_stockMinimumValueLabel);
		StyleStockMetricText(_stockDeficitValueLabel);
		StyleStockMultiline(_stockMovementLogLabel);
		StyleStockMultiline(_stockRelatedDocumentsLabel);
		_stockOnlyProblemsCheckBox.AutoSize = true;
		_stockOnlyProblemsCheckBox.Margin = new Padding(0, 9, 12, 0);
		_stockOnlyProblemsCheckBox.Font = DesktopTheme.BodyFont(9.3f);
		_stockOnlyProblemsCheckBox.ForeColor = DesktopTheme.TextSecondary;
		_stockOnlyProblemsCheckBox.Text = "Только проблемные";
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 2,
			BackColor = DesktopTheme.AppBackground,
			Padding = new Padding(0, 8, 0, 0)
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.Controls.Add(CreateWarehouseStockFiltersBar(), 0, 0);
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1,
			Margin = new Padding(0)
		};
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 71f));
		tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 29f));
		tableLayoutPanel2.Controls.Add(CreateWarehouseStockTableCard(), 0, 0);
		tableLayoutPanel2.Controls.Add(CreateWarehouseStockDetailsCard(), 1, 0);
		tableLayoutPanel.Controls.Add(tableLayoutPanel2, 0, 1);
		TabPage tabPage = new TabPage("Остатки")
		{
			Padding = new Padding(0),
			BackColor = DesktopTheme.AppBackground
		};
		tabPage.Controls.Add(tableLayoutPanel);
		return tabPage;
	}

	private Control CreateWarehouseStockFiltersBar()
	{
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			WrapContents = true,
			Margin = new Padding(0, 0, 0, 14),
			Padding = new Padding(0, 0, 0, 2)
		};
		flowLayoutPanel.Controls.Add(CreateSearchChrome(_stockSearchTextBox, "Поиск по коду, товару или штрихкоду...", 320));
		flowLayoutPanel.Controls.Add(CreateFilterChrome(_warehouseFilterComboBox, 132));
		flowLayoutPanel.Controls.Add(CreateFilterChrome(_stockModeComboBox, 118));
		flowLayoutPanel.Controls.Add(CreateFilterChrome(_stockStatusComboBox, 132));
		flowLayoutPanel.Controls.Add(_stockOnlyProblemsCheckBox);
		flowLayoutPanel.Controls.Add(CreateReferenceButton("Фильтры", delegate
		{
			RefreshStockGrid();
		}, primary: false, 102));
		flowLayoutPanel.Controls.Add(CreateReferenceButton("Сбросить", delegate
		{
			ResetStockFilters();
		}, primary: false, 98));
		return flowLayoutPanel;
	}

	private Control CreateWarehouseStockTableCard()
	{
		RoundedSurfacePanel roundedSurfacePanel = new RoundedSurfacePanel
		{
			Dock = DockStyle.Fill,
			BackColor = DesktopTheme.Surface,
			BorderColor = DesktopTheme.Border,
			BorderThickness = 1,
			CornerRadius = 18,
			DrawShadow = false,
			Margin = new Padding(0, 0, 18, 0),
			Padding = new Padding(0)
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 2
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill,
			Padding = new Padding(0)
		};
		panel.Controls.Add(_stockGrid);
		tableLayoutPanel.Controls.Add(panel, 0, 0);
		tableLayoutPanel.Controls.Add(CreateWarehouseStockFooter(), 0, 1);
		roundedSurfacePanel.Controls.Add(tableLayoutPanel);
		return roundedSurfacePanel;
	}

	private Control CreateWarehouseStockFooter()
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			ColumnCount = 2,
			RowCount = 1,
			Height = 52,
			Padding = new Padding(18, 10, 18, 10),
			BackColor = DesktopTheme.Surface
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30f));
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Fill,
			WrapContents = false,
			AutoSize = false,
			Margin = new Padding(0)
		};
		_stockFilteredLabel.Margin = new Padding(0, 4, 14, 0);
		flowLayoutPanel.Controls.Add(_stockFilteredLabel);
		flowLayoutPanel.Controls.Add(CreateLegendLabel("Свободно", Color.FromArgb(38, 168, 91)));
		flowLayoutPanel.Controls.Add(CreateLegendLabel("В резерве", Color.FromArgb(255, 151, 34)));
		flowLayoutPanel.Controls.Add(CreateLegendLabel("В пути", DesktopTheme.Primary));
		tableLayoutPanel.Controls.Add(flowLayoutPanel, 0, 0);
		tableLayoutPanel.Controls.Add(CreateWarehousePager(), 1, 0);
		return tableLayoutPanel;
	}

	private Control CreateWarehousePager()
	{
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			Dock = DockStyle.Right,
			WrapContents = false,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			Margin = new Padding(0)
		};
		flowLayoutPanel.Controls.Add(CreatePagerChip("?", active: false));
		flowLayoutPanel.Controls.Add(CreatePagerChip("1", active: true));
		flowLayoutPanel.Controls.Add(CreatePagerChip("2", active: false));
		flowLayoutPanel.Controls.Add(CreatePagerChip("3", active: false));
		flowLayoutPanel.Controls.Add(CreatePagerChip("?", active: false));
		flowLayoutPanel.Controls.Add(CreatePagerChip("31", active: false));
		flowLayoutPanel.Controls.Add(CreatePagerChip("?", active: false));
		return flowLayoutPanel;
	}

	private static Control CreatePagerChip(string text, bool active)
	{
		Label label = new Label
		{
			AutoSize = false,
			Width = 28,
			Height = 28,
			Text = text,
			TextAlign = ContentAlignment.MiddleCenter,
			Font = DesktopTheme.EmphasisFont(8.8f),
			ForeColor = active ? DesktopTheme.Primary : DesktopTheme.TextSecondary,
			BackColor = active ? DesktopTheme.PrimarySoft : DesktopTheme.Surface,
			Margin = new Padding(4, 0, 0, 0)
		};
		ApplyRoundedRegion(label, 8);
		return label;
	}

	private static Control CreateLegendLabel(string text, Color dotColor)
	{
		FlowLayoutPanel flowLayoutPanel = new FlowLayoutPanel
		{
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			WrapContents = false,
			Margin = new Padding(0, 6, 14, 0)
		};
		flowLayoutPanel.Controls.Add(new Label
		{
			AutoSize = true,
			Text = "●",
			Font = DesktopTheme.EmphasisFont(8.6f),
			ForeColor = dotColor,
			Margin = new Padding(0, 0, 6, 0)
		});
		flowLayoutPanel.Controls.Add(new Label
		{
			AutoSize = true,
			Text = text,
			Font = DesktopTheme.SubtitleFont(8.9f),
			ForeColor = DesktopTheme.TextSecondary,
			Margin = new Padding(0)
		});
		return flowLayoutPanel;
	}

	private Control CreateWarehouseStockDetailsCard()
	{
		RoundedSurfacePanel roundedSurfacePanel = new RoundedSurfacePanel
		{
			Dock = DockStyle.Fill,
			BackColor = DesktopTheme.Surface,
			BorderColor = DesktopTheme.Border,
			BorderThickness = 1,
			CornerRadius = 18,
			DrawShadow = false,
			Margin = new Padding(0),
			Padding = new Padding(18)
		};
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 8
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.Controls.Add(CreateWarehouseStockHeaderBlock(), 0, 0);
		tableLayoutPanel.Controls.Add(CreateWarehouseStockInfoRows(), 0, 1);
		tableLayoutPanel.Controls.Add(CreateWarehouseStockMetricsBlock(), 0, 2);
		tableLayoutPanel.Controls.Add(CreateWarehouseStockLimitBlock(), 0, 3);
		tableLayoutPanel.Controls.Add(CreateWarehouseSideSection("Последние движения", _stockMovementLogLabel), 0, 4);
		tableLayoutPanel.Controls.Add(CreateWarehouseSideSection("Связанные документы", _stockRelatedDocumentsLabel), 0, 5);
		tableLayoutPanel.Controls.Add(new Panel
		{
			Dock = DockStyle.Fill
		}, 0, 6);
		tableLayoutPanel.Controls.Add(CreateWarehouseQuickActions(), 0, 7);
		roundedSurfacePanel.Controls.Add(tableLayoutPanel);
		return roundedSurfacePanel;
	}

	private Control CreateWarehouseStockHeaderBlock()
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			ColumnCount = 2,
			RowCount = 1,
			Height = 64
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
		Panel panel = new Panel
		{
			Dock = DockStyle.Fill
		};
		panel.Controls.Add(_stockWarehouseLabel);
		panel.Controls.Add(_stockItemLabel);
		tableLayoutPanel.Controls.Add(panel, 0, 0);
		tableLayoutPanel.Controls.Add(_stockBadgeLabel, 1, 0);
		_stockBadgeLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
		return tableLayoutPanel;
	}

	private Control CreateWarehouseStockInfoRows()
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			ColumnCount = 2,
			RowCount = 3,
			Margin = new Padding(0, 0, 0, 12)
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.Controls.Add(CreateInfoCell("Код", _stockCodeValueLabel), 0, 0);
		tableLayoutPanel.Controls.Add(CreateInfoCell("Номенклатура", _stockNameValueLabel), 1, 0);
		tableLayoutPanel.Controls.Add(CreateInfoCell("Склад", _stockStatusLabel), 0, 1);
		tableLayoutPanel.Controls.Add(CreateInfoCell("Ед. изм.", _stockUnitValueLabel), 1, 1);
		tableLayoutPanel.Controls.Add(CreateInfoCell("Штрихкод", _stockBarcodeValueLabel), 0, 2);
		tableLayoutPanel.Controls.Add(CreateInfoCell("Баланс", _stockNumbersLabel), 1, 2);
		return tableLayoutPanel;
	}

	private Control CreateWarehouseStockMetricsBlock()
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			ColumnCount = 3,
			RowCount = 1,
			Height = 88,
			Margin = new Padding(0, 0, 0, 12)
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.4f));
		tableLayoutPanel.Controls.Add(CreateStockMetricCard("Свободно", _stockFreeValueLabel, Color.FromArgb(240, 250, 244), Color.FromArgb(38, 168, 91)), 0, 0);
		tableLayoutPanel.Controls.Add(CreateStockMetricCard("В резерве", _stockReservedValueLabel, Color.FromArgb(255, 248, 237), Color.FromArgb(255, 151, 34)), 1, 0);
		tableLayoutPanel.Controls.Add(CreateStockMetricCard("В пути", _stockTransitValueLabel, Color.FromArgb(239, 244, 255), DesktopTheme.Primary), 2, 0);
		return tableLayoutPanel;
	}

	private Control CreateWarehouseStockLimitBlock()
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			ColumnCount = 2,
			RowCount = 1,
			Height = 52,
			Margin = new Padding(0, 0, 0, 12)
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel.Controls.Add(CreateInfoCell("Минимальный остаток", _stockMinimumValueLabel), 0, 0);
		tableLayoutPanel.Controls.Add(CreateInfoCell("Дефицит", _stockDeficitValueLabel), 1, 0);
		return tableLayoutPanel;
	}

	private Control CreateWarehouseSideSection(string title, Label contentLabel)
	{
		Panel panel = new Panel
		{
			Dock = DockStyle.Top,
			Height = 132,
			Margin = new Padding(0, 0, 0, 12)
		};
		panel.Controls.Add(contentLabel);
		panel.Controls.Add(new Label
		{
			Text = title,
			Dock = DockStyle.Top,
			Height = 24,
			Font = DesktopTheme.EmphasisFont(10f),
			ForeColor = DesktopTheme.TextPrimary
		});
		return panel;
	}

	private Control CreateWarehouseQuickActions()
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			ColumnCount = 2,
			RowCount = 2,
			Height = 96
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel.Controls.Add(CreateReferenceButton("Переместить", delegate
		{
			CreateTransfer();
		}, primary: false, 0), 0, 0);
		tableLayoutPanel.Controls.Add(CreateReferenceButton("Зарезервировать", delegate
		{
			OpenQueueTab(2, _reservationContext);
		}, primary: false, 0), 1, 0);
		tableLayoutPanel.Controls.Add(CreateReferenceButton("Инвентаризация", delegate
		{
			CreateInventory();
		}, primary: false, 0), 0, 1);
		tableLayoutPanel.Controls.Add(CreateReferenceButton("Списать", delegate
		{
			CreateWriteOff();
		}, primary: false, 0), 1, 1);
		return tableLayoutPanel;
	}

	private static Control CreateStockMetricCard(string title, Label valueLabel, Color backColor, Color valueColor)
	{
		valueLabel.ForeColor = valueColor;
		RoundedSurfacePanel roundedSurfacePanel = new RoundedSurfacePanel
		{
			Dock = DockStyle.Fill,
			BackColor = backColor,
			BorderColor = backColor,
			BorderThickness = 0,
			CornerRadius = 14,
			DrawShadow = false,
			Margin = new Padding(0, 0, 10, 0),
			Padding = new Padding(12)
		};
		roundedSurfacePanel.Controls.Add(new Label
		{
			Text = title,
			Dock = DockStyle.Top,
			Height = 18,
			Font = DesktopTheme.SubtitleFont(8.8f),
			ForeColor = DesktopTheme.TextSecondary
		});
		roundedSurfacePanel.Controls.Add(valueLabel);
		return roundedSurfacePanel;
	}

	private static Control CreateInfoCell(string title, Label valueLabel)
	{
		Panel panel = new Panel
		{
			Dock = DockStyle.Top,
			Height = 52,
			Margin = new Padding(0, 0, 0, 8)
		};
		panel.Controls.Add(valueLabel);
		panel.Controls.Add(new Label
		{
			Text = title,
			Dock = DockStyle.Top,
			Height = 18,
			Font = DesktopTheme.SubtitleFont(8.8f),
			ForeColor = DesktopTheme.TextSecondary
		});
		return panel;
	}

	private static void StyleStockDetailLabel(Label label, float fontSize, Color foreColor, Color backColor, Padding padding, bool autoSize)
	{
		label.AutoSize = autoSize;
		label.BackColor = backColor;
		label.ForeColor = foreColor;
		label.Font = DesktopTheme.EmphasisFont(fontSize);
		label.Padding = padding;
		label.Margin = new Padding(0);
	}

	private static void StyleStockDetailValue(Label label, float fontSize, FontStyle fontStyle, Color foreColor)
	{
		label.Dock = DockStyle.Top;
		label.AutoSize = true;
		label.Margin = new Padding(0, 0, 0, 2);
		label.Font = new Font("Segoe UI", fontSize, fontStyle);
		label.ForeColor = foreColor;
	}

	private static void StyleStockMetricValue(Label label, Color foreColor)
	{
		label.Dock = DockStyle.Top;
		label.AutoSize = true;
		label.Margin = new Padding(0, 10, 0, 0);
		label.Font = DesktopTheme.TitleFont(13f);
		label.ForeColor = foreColor;
	}

	private static void StyleStockMetricText(Label label)
	{
		label.Dock = DockStyle.Top;
		label.AutoSize = true;
		label.Margin = new Padding(0, 10, 0, 0);
		label.Font = DesktopTheme.EmphasisFont(10.2f);
		label.ForeColor = DesktopTheme.TextPrimary;
	}

	private static void StyleStockMultiline(Label label)
	{
		label.Dock = DockStyle.Fill;
		label.AutoSize = false;
		label.Font = DesktopTheme.BodyFont(8.9f);
		label.ForeColor = DesktopTheme.TextSecondary;
		label.MaximumSize = new Size(320, 0);
	}

	private Control CreateWorkspaceQueuesPanel()
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 2
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
		tableLayoutPanel.Controls.Add(CreateWorkspaceQueuesRow(CreateQueueCard("Перемещения", "Оперативные маршруты между складами.", _transferQueueCountLabel, _transferQueueGrid, delegate
		{
			OpenQueueTab(1, _transferContext);
		}), CreateQueueCard("Резервы", "Товар, уже закрепленный под продажи.", _reservationQueueCountLabel, _reservationQueueGrid, delegate
		{
			OpenQueueTab(2, _reservationContext);
		})), 0, 0);
		tableLayoutPanel.Controls.Add(CreateWorkspaceQueuesRow(CreateQueueCard("Инвентаризации", "Пересчет и контроль фактического остатка.", _inventoryQueueCountLabel, _inventoryQueueGrid, delegate
		{
			OpenQueueTab(3, _inventoryContext);
		}), CreateQueueCard("Списания", "Потери, брак и внутренние корректировки.", _writeOffQueueCountLabel, _writeOffQueueGrid, delegate
		{
			OpenQueueTab(4, _writeOffContext);
		})), 0, 1);
		return tableLayoutPanel;
	}

	private static Control CreateWorkspaceQueuesRow(Control left, Control right)
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 2,
			RowCount = 1
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
		tableLayoutPanel.Controls.Add(left, 0, 0);
		tableLayoutPanel.Controls.Add(right, 1, 0);
		return tableLayoutPanel;
	}

	private Control CreateQueueCard(string title, string subtitle, Label countLabel, DataGridView grid, EventHandler openHandler)
	{
		Panel panel = DesktopSurfaceFactory.CreateCardShell();
		panel.Padding = new Padding(14);
		countLabel.AutoSize = true;
		countLabel.Dock = DockStyle.Right;
		countLabel.Font = DesktopTheme.EmphasisFont(9.4f);
		countLabel.ForeColor = DesktopTheme.SidebarButtonActiveText;
		Panel panel2 = new Panel
		{
			Dock = DockStyle.Top,
			Height = 52
		};
		panel2.Controls.Add(countLabel);
		panel2.Controls.Add(new Label
		{
			Text = subtitle,
			Dock = DockStyle.Top,
			Height = 20,
			Font = DesktopTheme.BodyFont(8.8f),
			ForeColor = DesktopTheme.TextSecondary
		});
		panel2.Controls.Add(new Label
		{
			Text = title,
			Dock = DockStyle.Top,
			Height = 24,
			Font = DesktopTheme.TitleFont(11f),
			ForeColor = DesktopTheme.TextPrimary
		});
		Button button = DesktopSurfaceFactory.CreateActionButton("Открыть", openHandler, DesktopButtonTone.Ghost, new Padding(0));
		button.Dock = DockStyle.Right;
		Panel panel3 = new Panel
		{
			Dock = DockStyle.Bottom,
			Height = 34
		};
		panel3.Controls.Add(button);
		panel.Controls.Add(grid);
		panel.Controls.Add(panel3);
		panel.Controls.Add(panel2);
		return panel;
	}

	private TabPage CreateDocumentTab(string title, DocumentTabContext context, Control toolbar)
	{
		SplitContainer splitContainer = new SplitContainer
		{
			Dock = DockStyle.Fill,
			Orientation = Orientation.Vertical,
			SplitterDistance = 680
		};
		splitContainer.Panel1.Controls.Add(CreateGridShell("Реестр документов", context.Summary, context.RecordGrid));
		splitContainer.Panel2.Controls.Add(CreateDocumentDetails(context));
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 2
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel.Controls.Add(toolbar, 0, 0);
		tableLayoutPanel.Controls.Add(splitContainer, 0, 1);
		TabPage tabPage = new TabPage(title)
		{
			Padding = new Padding(10)
		};
		tabPage.Controls.Add(tableLayoutPanel);
		return tabPage;
	}

	private TabPage CreateOperationsTab()
	{
		_operationsGrid.DataSource = _operationBindingSource;
		TabPage tabPage = new TabPage("Журнал")
		{
			Padding = new Padding(10)
		};
		tabPage.Controls.Add(CreateGridShell("Журнал операций", "Кто и когда создал, подготовил, провел или завершил складской документ.", _operationsGrid));
		return tabPage;
	}

	private Control CreateTransfersToolbar()
	{
		PrepareSearchTextBox(_transferContext.SearchTextBox, "Поиск перемещения");
		FlowLayoutPanel flowLayoutPanel = CreateToolbarBase(_transferContext);
		flowLayoutPanel.Controls.Add(CreateActionButton("Печать перемещения", delegate
		{
			PrintSelectedTransfer();
		}));
		flowLayoutPanel.Controls.Add(CreateActionButton("Новое перемещение", delegate
		{
			CreateTransfer();
		}));
		flowLayoutPanel.Controls.Add(CreateActionButton("Изменить", delegate
		{
			EditSelectedTransfer();
		}));
		flowLayoutPanel.Controls.Add(CreateActionButton("К перемещению", delegate
		{
			PrepareSelectedTransfer();
		}));
		flowLayoutPanel.Controls.Add(CreateActionButton("Завершить", delegate
		{
			CompleteSelectedTransfer();
		}));
		return flowLayoutPanel;
	}

	private Control CreateReservationToolbar()
	{
		PrepareSearchTextBox(_reservationContext.SearchTextBox, "Поиск резерва");
		return CreateToolbarBase(_reservationContext);
	}

	private Control CreateInventoryToolbar()
	{
		PrepareSearchTextBox(_inventoryContext.SearchTextBox, "Поиск инвентаризации");
		FlowLayoutPanel flowLayoutPanel = CreateToolbarBase(_inventoryContext);
		flowLayoutPanel.Controls.Add(CreateActionButton("Печать акта", delegate
		{
			PrintSelectedInventory();
		}));
		flowLayoutPanel.Controls.Add(CreateActionButton("Новая инвентаризация", delegate
		{
			CreateInventory();
		}));
		flowLayoutPanel.Controls.Add(CreateActionButton("Изменить", delegate
		{
			EditSelectedInventory();
		}));
		flowLayoutPanel.Controls.Add(CreateActionButton("Провести", delegate
		{
			PostSelectedInventory();
		}));
		return flowLayoutPanel;
	}

	private Control CreateWriteOffToolbar()
	{
		PrepareSearchTextBox(_writeOffContext.SearchTextBox, "Поиск списания");
		FlowLayoutPanel flowLayoutPanel = CreateToolbarBase(_writeOffContext);
		flowLayoutPanel.Controls.Add(CreateActionButton("Печать акта", delegate
		{
			PrintSelectedWriteOff();
		}));
		flowLayoutPanel.Controls.Add(CreateActionButton("Новое списание", delegate
		{
			CreateWriteOff();
		}));
		flowLayoutPanel.Controls.Add(CreateActionButton("Изменить", delegate
		{
			EditSelectedWriteOff();
		}));
		flowLayoutPanel.Controls.Add(CreateActionButton("Провести", delegate
		{
			PostSelectedWriteOff();
		}));
		return flowLayoutPanel;
	}

	private static void PrepareSearchTextBox(TextBox textBox, string placeholder)
	{
		textBox.Width = 240;
		textBox.Font = new Font("Segoe UI", 10f);
		textBox.PlaceholderText = placeholder;
	}

	private static FlowLayoutPanel CreateToolbarBase(DocumentTabContext context)
	{
		context.SearchTextBox.Margin = new Padding(0, 2, 8, 8);
		context.CountLabel.Margin = new Padding(0, 7, 8, 8);
		FlowLayoutPanel flowLayoutPanel = DesktopSurfaceFactory.CreateToolbarStrip(wrapContents: true, 10);
		flowLayoutPanel.Controls.Add(context.SearchTextBox);
		flowLayoutPanel.Controls.Add(context.CountLabel);
		return flowLayoutPanel;
	}

	private Control CreateDocumentDetails(DocumentTabContext context)
	{
		PrepareDetailLabel(context.NumberLabel);
		PrepareDetailLabel(context.DateLabel);
		PrepareDetailLabel(context.StatusLabel);
		PrepareDetailLabel(context.RouteLabel);
		PrepareDetailLabel(context.LinkLabel);
		PrepareDetailLabel(context.SourceLabel);
		PrepareDetailLabel(context.CommentLabel);
		PrepareDetailLabel(context.TotalLabel);
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			ColumnCount = 1,
			RowCount = 2,
			AutoSize = true,
			BackColor = Color.White,
			Padding = new Padding(14)
		};
		tableLayoutPanel.Controls.Add(new Label
		{
			Text = "Карточка документа",
			Dock = DockStyle.Top,
			Height = 28,
			Font = new Font("Segoe UI Semibold", 12f, FontStyle.Bold),
			ForeColor = Color.FromArgb(53, 47, 41)
		}, 0, 0);
		tableLayoutPanel.Controls.Add(CreateDetailGrid(("Номер", context.NumberLabel), ("Дата", context.DateLabel), ("Статус", context.StatusLabel), ("Маршрут", context.RouteLabel), ("Основание", context.LinkLabel), ("Источник", context.SourceLabel), ("Количество", context.TotalLabel), ("Комментарий", context.CommentLabel)), 0, 1);
		SplitContainer splitContainer = new SplitContainer
		{
			Dock = DockStyle.Fill,
			Orientation = Orientation.Horizontal,
			SplitterDistance = 220,
			BackColor = Color.White
		};
		splitContainer.Panel1.Padding = new Padding(14, 0, 14, 14);
		splitContainer.Panel1.Controls.Add(CreateGridShell("Строки документа", "Табличная часть и количественные поля.", context.LineGrid));
		splitContainer.Panel2.Padding = new Padding(14, 0, 14, 14);
		splitContainer.Panel2.Controls.Add(CreateGridShell("Поля 1С / миграции", "Импортированные поля сохраняются для контроля перехода.", context.FieldGrid));
		TableLayoutPanel tableLayoutPanel2 = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 2,
			BackColor = Color.White
		};
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		tableLayoutPanel2.Controls.Add(tableLayoutPanel, 0, 0);
		tableLayoutPanel2.Controls.Add(splitContainer, 0, 1);
		return tableLayoutPanel2;
	}

	private static Control CreateGridShell(string title, string summary, Control grid)
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Fill,
			ColumnCount = 1,
			RowCount = 2,
			BackColor = Color.White,
			Padding = new Padding(14)
		};
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
		tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
		Panel panel = new Panel
		{
			Dock = DockStyle.Top,
			Height = 52
		};
		panel.Controls.Add(new Label
		{
			Text = summary,
			Dock = DockStyle.Top,
			Height = 22,
			Font = new Font("Segoe UI", 9f),
			ForeColor = Color.FromArgb(107, 98, 88)
		});
		panel.Controls.Add(new Label
		{
			Text = title,
			Dock = DockStyle.Top,
			Height = 28,
			Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold),
			ForeColor = Color.FromArgb(53, 47, 41)
		});
		tableLayoutPanel.Controls.Add(panel, 0, 0);
		tableLayoutPanel.Controls.Add(grid, 0, 1);
		return tableLayoutPanel;
	}

	private static Control CreateDetailGrid(params (string Caption, Label ValueLabel)[] items)
	{
		TableLayoutPanel tableLayoutPanel = new TableLayoutPanel
		{
			Dock = DockStyle.Top,
			AutoSize = true,
			AutoSizeMode = AutoSizeMode.GrowAndShrink,
			ColumnCount = 2
		};
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130f));
		tableLayoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
		for (int i = 0; i < items.Length; i++)
		{
			tableLayoutPanel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
			tableLayoutPanel.Controls.Add(new Label
			{
				Text = items[i].Caption,
				AutoSize = true,
				Font = new Font("Segoe UI Semibold", 9.1f, FontStyle.Bold),
				ForeColor = Color.FromArgb(88, 79, 70),
				Margin = new Padding(0, 0, 8, 10)
			}, 0, i);
			tableLayoutPanel.Controls.Add(items[i].ValueLabel, 1, i);
		}
		return tableLayoutPanel;
	}

	private static void PrepareDetailLabel(Label label)
	{
		label.Dock = DockStyle.Top;
		label.AutoSize = true;
		label.MaximumSize = new Size(420, 0);
		label.Font = new Font("Segoe UI", 9.2f);
		label.ForeColor = Color.FromArgb(49, 44, 38);
		label.Margin = new Padding(0, 0, 0, 10);
	}

	private static Button CreateActionButton(string text, EventHandler handler)
	{
		return DesktopSurfaceFactory.CreateActionButton(text, handler);
	}

	private static void ConfigureDigestLabel(Label label)
	{
		label.AutoSize = true;
		label.Padding = new Padding(10, 4, 10, 4);
		label.Margin = new Padding(0, 7, 6, 0);
		label.Font = DesktopTheme.EmphasisFont(8.8f);
		label.BackColor = DesktopTheme.SurfaceMuted;
		label.ForeColor = DesktopTheme.TextSecondary;
	}

	private void ConfigureQueueGrid(DataGridView grid, BindingSource bindingSource, DocumentTabContext context)
	{
		DataGridView grid2 = grid;
		DocumentTabContext context2 = context;
		grid2.DataSource = bindingSource;
		grid2.DoubleClick += delegate
		{
			Guid? selectedId = (grid2.CurrentRow?.DataBoundItem as QueueDocumentRow)?.DocumentId;
			OpenQueueTab(ResolveQueueTabIndex(context2), context2, selectedId);
		};
	}

	private int ResolveQueueTabIndex(DocumentTabContext context)
	{
		if (context == _transferContext)
		{
			return 1;
		}
		if (context == _reservationContext)
		{
			return 2;
		}
		if (context == _inventoryContext)
		{
			return 3;
		}
		return 4;
	}

	private void OpenQueueTab(int tabIndex, DocumentTabContext context, Guid? selectedId = null)
	{
		if (selectedId.HasValue)
		{
			RefreshDocumentGrid(context, selectedId);
		}
		if (_tabs.TabPages.Count > tabIndex)
		{
			_tabs.SelectedIndex = tabIndex;
		}
	}

	private void ConfigureDocumentContext(DocumentTabContext context)
	{
		DocumentTabContext context2 = context;
		context2.CountLabel.AutoSize = true;
		context2.CountLabel.Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold);
		context2.CountLabel.ForeColor = Color.FromArgb(82, 74, 66);
		context2.CountLabel.Margin = new Padding(10, 9, 0, 0);
		context2.RecordGrid.DataSource = context2.RecordBindingSource;
		context2.LineGrid.DataSource = context2.LineBindingSource;
		context2.FieldGrid.DataSource = context2.FieldBindingSource;
		context2.RecordGrid.SelectionChanged += delegate
		{
			RefreshDocumentDetails(context2);
		};
		context2.RecordGrid.CellFormatting += HandleStatusCellFormatting;
		context2.SearchTextBox.TextChanged += delegate
		{
			ScheduleSearchRefresh(delegate
			{
				RefreshDocumentGrid(context2);
			});
		};
	}

	private void ScheduleSearchRefresh(Action refreshAction)
	{
		_pendingSearchRefresh = refreshAction;
		_searchDebounceTimer.Stop();
		_searchDebounceTimer.Start();
	}

	private void HandleSearchDebounceTick(object? sender, EventArgs e)
	{
		_searchDebounceTimer.Stop();
		Action pendingSearchRefresh = _pendingSearchRefresh;
		_pendingSearchRefresh = null;
		pendingSearchRefresh?.Invoke();
	}

	private void ConfigureStockGrid()
	{
		_stockGrid.AutoGenerateColumns = false;
		_stockGrid.Columns.Clear();
		_stockGrid.RowTemplate.Height = 64;
		_stockGrid.ColumnHeadersHeight = 44;
		_stockGrid.AllowUserToResizeRows = false;
		_stockGrid.ScrollBars = ScrollBars.Vertical;
		_stockGrid.DefaultCellStyle.Padding = new Padding(12, 0, 12, 0);
		_stockGrid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(245, 247, 255);
		_stockGrid.DefaultCellStyle.SelectionForeColor = DesktopTheme.TextPrimary;
		_stockGrid.AlternatingRowsDefaultCellStyle.BackColor = DesktopTheme.Surface;
		_stockGrid.Columns.Add(CreateStockTextColumn("Code", "Код", "Code", 118));
		_stockGrid.Columns.Add(CreateStockTextColumn("Item", "Товар", "Item", 300));
		_stockGrid.Columns.Add(CreateStockTextColumn("Warehouse", "Склад", "Warehouse", 132));
		_stockGrid.Columns.Add(CreateStockTextColumn("BalanceSummary", "Остаток", "BalanceSummary", 186));
		_stockGrid.Columns.Add(CreateStockNumericColumn("ReservedQuantity", "Резерв", "ReservedQuantity", 82));
		_stockGrid.Columns.Add(CreateStockNumericColumn("InTransitQuantity", "В пути", "InTransitQuantity", 82));
		_stockGrid.Columns.Add(CreateStockNumericColumn("MinimumQuantity", "Мин. остаток", "MinimumQuantity", 102));
		_stockGrid.Columns.Add(CreateStockTextColumn("Status", "Статус", "Status", 124));
		_stockGrid.Columns.Add(CreateStockTextColumn("Actions", string.Empty, "Actions", 46, DataGridViewContentAlignment.MiddleCenter));
	}

	private static DataGridViewTextBoxColumn CreateStockTextColumn(string name, string header, string propertyName, int width, DataGridViewContentAlignment alignment = DataGridViewContentAlignment.MiddleLeft)
	{
		return new DataGridViewTextBoxColumn
		{
			Name = name,
			HeaderText = header,
			DataPropertyName = propertyName,
			Width = width,
			MinimumWidth = width,
			SortMode = DataGridViewColumnSortMode.Automatic,
			DefaultCellStyle = new DataGridViewCellStyle
			{
				Alignment = alignment,
				Font = DesktopTheme.BodyFont(9.2f),
				ForeColor = DesktopTheme.TextPrimary,
				SelectionBackColor = Color.FromArgb(245, 247, 255),
				SelectionForeColor = DesktopTheme.TextPrimary
			}
		};
	}

	private static DataGridViewTextBoxColumn CreateStockNumericColumn(string name, string header, string propertyName, int width)
	{
		return new DataGridViewTextBoxColumn
		{
			Name = name,
			HeaderText = header,
			DataPropertyName = propertyName,
			Width = width,
			MinimumWidth = width,
			SortMode = DataGridViewColumnSortMode.Automatic,
			DefaultCellStyle = new DataGridViewCellStyle
			{
				Alignment = DataGridViewContentAlignment.MiddleCenter,
				Format = "N0",
				Font = DesktopTheme.BodyFont(9.2f),
				ForeColor = DesktopTheme.TextPrimary,
				SelectionBackColor = Color.FromArgb(245, 247, 255),
				SelectionForeColor = DesktopTheme.TextPrimary
			}
		};
	}

	private void SynchronizeWarehouseSearch(TextBox source, TextBox target)
	{
		if (_syncingWarehouseSearch || string.Equals(source.Text, target.Text, StringComparison.Ordinal))
		{
			return;
		}
		_syncingWarehouseSearch = true;
		try
		{
			target.Text = source.Text;
			target.SelectionStart = target.TextLength;
		}
		finally
		{
			_syncingWarehouseSearch = false;
		}
	}

	private void HandleStockGridCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
	{
		if (sender is not DataGridView dataGridView || e.RowIndex < 0 || e.ColumnIndex < 0)
		{
			return;
		}
		DataGridViewColumn dataGridViewColumn = dataGridView.Columns[e.ColumnIndex];
		if (string.Equals(dataGridViewColumn.Name, "BalanceSummary", StringComparison.Ordinal))
		{
			e.Handled = true;
			e.PaintBackground(e.CellBounds, true);
			e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
			StockGridRow stockGridRow = dataGridView.Rows[e.RowIndex].DataBoundItem as StockGridRow;
			if (stockGridRow == null)
			{
				return;
			}
			Rectangle rectangle = Rectangle.Inflate(e.CellBounds, -10, -8);
			using Font font = DesktopTheme.EmphasisFont(10.6f);
			using Font font2 = DesktopTheme.SubtitleFont(8.7f);
			TextRenderer.DrawText(e.Graphics, stockGridRow.Record.FreeQuantity.ToString("N0"), font, new Rectangle(rectangle.X, rectangle.Y - 1, rectangle.Width, 18), DesktopTheme.TextPrimary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
			Rectangle rectangle2 = new Rectangle(rectangle.X, rectangle.Y + 22, Math.Max(40, rectangle.Width - 10), 6);
			PaintBalanceBar(e.Graphics, rectangle2, stockGridRow.Record);
			TextRenderer.DrawText(e.Graphics, stockGridRow.BalanceSummary, font2, new Rectangle(rectangle.X, rectangle.Y + 30, rectangle.Width, 18), DesktopTheme.TextSecondary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
			return;
		}
		if (string.Equals(dataGridViewColumn.Name, "Actions", StringComparison.Ordinal))
		{
			e.Handled = true;
			e.PaintBackground(e.CellBounds, true);
			e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
			using Font font3 = DesktopTheme.EmphasisFont(11f);
			TextRenderer.DrawText(e.Graphics, "?", font3, e.CellBounds, DesktopTheme.TextMuted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
		}
	}

	private static void PaintBalanceBar(Graphics graphics, Rectangle bounds, WarehouseStockBalanceRecord record)
	{
		decimal num = Math.Max(1m, record.FreeQuantity + record.ReservedQuantity + record.ShippedQuantity);
		using SolidBrush solidBrush = new SolidBrush(Color.FromArgb(236, 241, 249));
		graphics.FillRectangle(solidBrush, bounds);
		int width = bounds.Width;
		int num2 = Math.Max(6, (int)Math.Round(width * (double)(record.FreeQuantity / num)));
		int num3 = Math.Max(0, (int)Math.Round(width * (double)(record.ReservedQuantity / num)));
		int num4 = Math.Max(0, width - num2 - num3);
		using SolidBrush solidBrush2 = new SolidBrush(Color.FromArgb(38, 168, 91));
		using SolidBrush solidBrush3 = new SolidBrush(Color.FromArgb(255, 151, 34));
		using SolidBrush solidBrush4 = new SolidBrush(DesktopTheme.Primary);
		graphics.FillRectangle(solidBrush2, new Rectangle(bounds.X, bounds.Y, num2, bounds.Height));
		if (num3 > 0)
		{
			graphics.FillRectangle(solidBrush3, new Rectangle(bounds.X + num2, bounds.Y, num3, bounds.Height));
		}
		if (num4 > 0)
		{
			graphics.FillRectangle(solidBrush4, new Rectangle(bounds.Right - num4, bounds.Y, num4, bounds.Height));
		}
	}

	private static void ConfigureFilterComboBox(ComboBox comboBox, int width = 164)
	{
		comboBox.Width = width;
		comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
		comboBox.FlatStyle = FlatStyle.Flat;
		comboBox.Font = DesktopTheme.BodyFont(9.4f);
		comboBox.Margin = new Padding(10, 0, 0, 0);
		comboBox.BackColor = DesktopTheme.Surface;
		comboBox.ForeColor = DesktopTheme.TextPrimary;
	}

	private void RefreshStockFilterOptions()
	{
		_stockFilterEventsSuspended = true;
		try
		{
			string selectedValue = (_warehouseFilterComboBox.SelectedItem as string) ?? "Все склады";
			string selectedValue2 = (_stockModeComboBox.SelectedItem as string) ?? "Все типы";
			string selectedValue3 = (_stockStatusComboBox.SelectedItem as string) ?? "Все статусы";
			string[] options = new string[1] { "Все склады" }.Concat((from item in _runtimeView.StockBalances
				select item.Warehouse into item
				where !string.IsNullOrWhiteSpace(item)
				select item).Distinct<string>(StringComparer.OrdinalIgnoreCase).OrderBy<string, string>((string item) => item, StringComparer.OrdinalIgnoreCase)).ToArray();
			ReplaceComboBoxItems(_warehouseFilterComboBox, options, selectedValue);
			string[] options2 = new string[3] { "Все типы", "С резервом", "Свободный остаток" };
			ReplaceComboBoxItems(_stockModeComboBox, options2, selectedValue2);
			string[] options3 = new string[4] { "Все статусы", "Критично", "Под контроль", "Норма" };
			ReplaceComboBoxItems(_stockStatusComboBox, options3, selectedValue3);
		}
		finally
		{
			_stockFilterEventsSuspended = false;
		}
	}

	private static void ReplaceComboBoxItems(ComboBox comboBox, IReadOnlyList<string> options, string selectedValue)
	{
		comboBox.BeginUpdate();
		try
		{
			comboBox.Items.Clear();
			foreach (string option in options)
			{
				comboBox.Items.Add(option);
			}
			string selectedItem = (options.Contains(selectedValue) ? selectedValue : options[0]);
			comboBox.SelectedItem = selectedItem;
		}
		finally
		{
			comboBox.EndUpdate();
		}
	}

	private void RefreshControlTower()
	{
		int num = _runtimeView.StockBalances.Count((WarehouseStockBalanceRecord item) => string.Equals(item.Status, "Критично", StringComparison.OrdinalIgnoreCase));
		int num2 = _runtimeView.StockBalances.Count((WarehouseStockBalanceRecord item) => string.Equals(item.Status, "Под контроль", StringComparison.OrdinalIgnoreCase));
		int num3 = _workspace.TransferOrders.Count((OperationalWarehouseDocumentRecord item) => !string.Equals(item.Status, "Перемещен", StringComparison.OrdinalIgnoreCase));
		int num4 = _workspace.InventoryCounts.Count((OperationalWarehouseDocumentRecord item) => !string.Equals(item.Status, "Проведена", StringComparison.OrdinalIgnoreCase)) + _workspace.WriteOffs.Count((OperationalWarehouseDocumentRecord item) => !string.Equals(item.Status, "Списано", StringComparison.OrdinalIgnoreCase));
		_criticalItemsLabel.Text = num.ToString("N0");
		_attentionItemsLabel.Text = num2.ToString("N0");
		_executionItemsLabel.Text = (num3 + num4).ToString("N0");
		_pickingItemsLabel.Text = _runtimeView.Reservations.Count.ToString("N0");
		_criticalActionCountLabel.Text = num.ToString("N0");
		_transferActionCountLabel.Text = num3.ToString("N0");
		_reservationActionCountLabel.Text = _runtimeView.Reservations.Count.ToString("N0");
		_controlActionCountLabel.Text = num4.ToString("N0");
	}

	private void ResetStockFilters()
	{
		_stockSearchTextBox.Clear();
		_stockOnlyProblemsCheckBox.Checked = false;
		ApplyStockView("Все типы");
	}

	private void ActivateCriticalStockView()
	{
		ApplyStockView("Критично");
		OpenStockTab();
	}

	private void ActivateAttentionStockView()
	{
		ApplyStockView("Под контроль");
		OpenStockTab();
	}

	private void OpenControlFocusView()
	{
		bool flag = _workspace.InventoryCounts.Any((OperationalWarehouseDocumentRecord item) => !string.Equals(item.Status, "Проведена", StringComparison.OrdinalIgnoreCase));
		OpenQueueTab(flag ? 3 : 4, flag ? _inventoryContext : _writeOffContext);
	}

	private void ApplyStockView(string mode, string? warehouse = null)
	{
		RefreshStockFilterOptions();
		_stockFilterEventsSuspended = true;
		try
		{
			if (string.Equals(mode, "Критично", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "Под контроль", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "Норма", StringComparison.OrdinalIgnoreCase))
			{
				_stockStatusComboBox.SelectedItem = (_stockStatusComboBox.Items.Contains(mode) ? mode : "Все статусы");
				if (_stockModeComboBox.Items.Contains("Все типы"))
				{
					_stockModeComboBox.SelectedItem = "Все типы";
				}
			}
			else
			{
				_stockModeComboBox.SelectedItem = (_stockModeComboBox.Items.Contains(mode) ? mode : "Все типы");
				if (_stockStatusComboBox.Items.Contains("Все статусы"))
				{
					_stockStatusComboBox.SelectedItem = "Все статусы";
				}
			}
			if (!string.IsNullOrWhiteSpace(warehouse) && _warehouseFilterComboBox.Items.Contains(warehouse))
			{
				_warehouseFilterComboBox.SelectedItem = warehouse;
			}
			else if (_warehouseFilterComboBox.Items.Contains("Все склады"))
			{
				_warehouseFilterComboBox.SelectedItem = "Все склады";
			}
		}
		finally
		{
			_stockFilterEventsSuspended = false;
		}
		RefreshStockGrid();
	}

	private void OpenStockTab()
	{
		if (_tabs.TabPages.Count > 0)
		{
			_tabs.SelectedIndex = 0;
		}
	}

	private void RefreshAll()
	{
		SuspendLayout();
		try
		{
			_searchDebounceTimer.Stop();
			_pendingSearchRefresh = null;
			_noteLabel.Text = "Складской контур работает локально. Оператор: " + _workspace.CurrentOperator + ". Остатки считаются с учетом продаж, приемки, перемещений, инвентаризаций и списаний.";
			_warehouseLocationLabel.Text = ResolvePrimaryWarehouseLabel();
			_warehouseOperatorLabel.Text = "Оператор: " + _workspace.CurrentOperator;
			_warehouseUpdatedLabel.Text = "Обновлено: " + DateTime.Now.ToString("HH:mm");
			_stockPositionsLabel.Text = _runtimeView.StockBalances.Count.ToString("N0");
			_reservationsLabel.Text = _runtimeView.Reservations.Count.ToString("N0");
			_transfersLabel.Text = _workspace.TransferOrders.Count.ToString("N0");
			_controlLabel.Text = (_workspace.InventoryCounts.Count + _workspace.WriteOffs.Count).ToString("N0");
			_transferContext.Records = MapOperationalDocuments(_workspace.TransferOrders);
			_reservationContext.Records = (from item in _runtimeView.Reservations
				orderby item.Date ?? DateTime.MinValue descending
				select new DocumentViewRecord(null, item)).ToArray();
			_inventoryContext.Records = MapOperationalDocuments(_workspace.InventoryCounts);
			_writeOffContext.Records = MapOperationalDocuments(_workspace.WriteOffs);
			RefreshStockFilterOptions();
			RefreshControlTower();
			RefreshStockGrid();
			RefreshDocumentGrid(_transferContext);
			RefreshDocumentGrid(_reservationContext);
			RefreshDocumentGrid(_inventoryContext);
			RefreshDocumentGrid(_writeOffContext);
			RefreshWorkspaceQueues();
			RefreshOperationsLog();
		}
		finally
		{
			ResumeLayout(performLayout: true);
		}
	}

	private void RefreshStockGrid()
	{
		string search = _stockSearchTextBox.Text.Trim();
		string selectedWarehouse = (_warehouseFilterComboBox.SelectedItem as string) ?? "Все склады";
		string selectedMode = (_stockModeComboBox.SelectedItem as string) ?? "Все типы";
		string selectedStatus = (_stockStatusComboBox.SelectedItem as string) ?? "Все статусы";
		bool flag = _stockOnlyProblemsCheckBox.Checked;
		StockGridRow[] array = (from item in (from item in (from item in _runtimeView.StockBalances
					where string.IsNullOrWhiteSpace(search) || Contains(item.ItemCode, search) || Contains(item.ItemName, search) || Contains(item.Warehouse, search) || Contains(item.Status, search)
					where string.Equals(selectedWarehouse, "Все склады", StringComparison.OrdinalIgnoreCase) || string.Equals(item.Warehouse, selectedWarehouse, StringComparison.OrdinalIgnoreCase)
					select item).Where(delegate(WarehouseStockBalanceRecord item)
				{
					if (1 == 0)
					{
					}
					bool result = selectedMode switch
					{
						"С резервом" => item.ReservedQuantity > 0m, 
						"Свободный остаток" => item.FreeQuantity > 0m, 
						_ => true, 
					};
					if (1 == 0)
					{
					}
					return result;
				}).Where(delegate(WarehouseStockBalanceRecord item)
				{
					if (!string.Equals(selectedStatus, "Все статусы", StringComparison.OrdinalIgnoreCase) && !string.Equals(item.Status, selectedStatus, StringComparison.OrdinalIgnoreCase))
					{
						return false;
					}
					if (!flag)
					{
						return true;
					}
					return string.Equals(item.Status, "Критично", StringComparison.OrdinalIgnoreCase) || string.Equals(item.Status, "Под контроль", StringComparison.OrdinalIgnoreCase);
				})
				orderby ResolveStockPriority(item)
				select item).ThenBy<WarehouseStockBalanceRecord, string>((WarehouseStockBalanceRecord item) => item.Warehouse, StringComparer.OrdinalIgnoreCase).ThenBy<WarehouseStockBalanceRecord, string>((WarehouseStockBalanceRecord item) => item.ItemName, StringComparer.OrdinalIgnoreCase)
			select new StockGridRow(item)).ToArray();
		_stockBindingSource.DataSource = array;
		_stockFilteredLabel.Text = $"Показано: {array.Length:N0} из {_runtimeView.StockBalances.Count:N0}";
		if (array.Length > 0 && _stockGrid.Rows.Count > 0)
		{
			_stockGrid.ClearSelection();
			_stockGrid.Rows[0].Selected = true;
			_stockGrid.CurrentCell = _stockGrid.Rows[0].Cells[Math.Min(1, _stockGrid.Columns.Count - 1)];
		}
		RefreshStockDetails();
	}

	private void RefreshStockDetails()
	{
		WarehouseStockBalanceRecord warehouseStockBalanceRecord = (_stockGrid.CurrentRow?.DataBoundItem as StockGridRow)?.Record;
		_stockItemLabel.Text = warehouseStockBalanceRecord?.ItemName ?? "—";
		_stockWarehouseLabel.Text = warehouseStockBalanceRecord?.Warehouse ?? "—";
		_stockStatusLabel.Text = warehouseStockBalanceRecord?.Warehouse ?? "—";
		_stockNumbersLabel.Text = ((warehouseStockBalanceRecord == null) ? "—" : $"{warehouseStockBalanceRecord.FreeQuantity:N0} / {warehouseStockBalanceRecord.ReservedQuantity:N0} / {warehouseStockBalanceRecord.ShippedQuantity:N0}");
		_stockBadgeLabel.Text = warehouseStockBalanceRecord?.Status ?? "—";
		ApplyStockBadgeStyle(_stockBadgeLabel, warehouseStockBalanceRecord?.Status);
		_stockCodeValueLabel.Text = warehouseStockBalanceRecord?.ItemCode ?? "—";
		_stockNameValueLabel.Text = warehouseStockBalanceRecord?.ItemName ?? "—";
		_stockUnitValueLabel.Text = warehouseStockBalanceRecord?.Unit ?? "—";
		_stockBarcodeValueLabel.Text = ((warehouseStockBalanceRecord == null) ? "—" : ResolvePseudoBarcode(warehouseStockBalanceRecord));
		_stockFreeValueLabel.Text = ((warehouseStockBalanceRecord == null) ? "0" : warehouseStockBalanceRecord.FreeQuantity.ToString("N0"));
		_stockReservedValueLabel.Text = ((warehouseStockBalanceRecord == null) ? "0" : warehouseStockBalanceRecord.ReservedQuantity.ToString("N0"));
		_stockTransitValueLabel.Text = ((warehouseStockBalanceRecord == null) ? "0" : warehouseStockBalanceRecord.ShippedQuantity.ToString("N0"));
		_stockMinimumValueLabel.Text = ((warehouseStockBalanceRecord == null) ? "—" : $"{ResolveMinimumStock(warehouseStockBalanceRecord):N0} {warehouseStockBalanceRecord.Unit}");
		_stockDeficitValueLabel.Text = ((warehouseStockBalanceRecord == null) ? "—" : $"{Math.Max(0m, ResolveMinimumStock(warehouseStockBalanceRecord) - warehouseStockBalanceRecord.FreeQuantity):N0} {warehouseStockBalanceRecord.Unit}");
		_stockMovementLogLabel.Text = ((warehouseStockBalanceRecord == null) ? "Нет связанных движений." : BuildStockMovementDigest(warehouseStockBalanceRecord));
		_stockRelatedDocumentsLabel.Text = ((warehouseStockBalanceRecord == null) ? "Нет связанных документов." : BuildRelatedDocumentsDigest(warehouseStockBalanceRecord));
	}

	private void RefreshDocumentGrid(DocumentTabContext context, Guid? selectedId = null)
	{
		Guid? selectedId2 = selectedId ?? GetSelectedDocumentId(context);
		string search = context.SearchTextBox.Text.Trim();
		DocumentGridRow[] array = (from item in context.Records
			where string.IsNullOrWhiteSpace(search) || MatchesDocumentSearch(item.Record, search)
			orderby item.Record.Date ?? DateTime.MinValue descending, item.Record.Number descending
			select new DocumentGridRow(item)).ToArray();
		context.RecordBindingSource.DataSource = array;
		context.CountLabel.Text = $"Показано: {array.Length:N0} из {context.Records.Count:N0}";
		RestoreGridSelection(context.RecordGrid, selectedId2);
		RefreshDocumentDetails(context);
	}

	private void RefreshDocumentDetails(DocumentTabContext context)
	{
		WarehouseDocumentRecord warehouseDocumentRecord = ((context.RecordGrid.CurrentRow?.DataBoundItem as DocumentGridRow)?.View)?.Record;
		context.NumberLabel.Text = warehouseDocumentRecord?.Number ?? "—";
		context.DateLabel.Text = warehouseDocumentRecord?.Date?.ToString("dd.MM.yyyy") ?? "—";
		context.StatusLabel.Text = warehouseDocumentRecord?.Status ?? "—";
		context.RouteLabel.Text = ((warehouseDocumentRecord == null) ? "—" : (warehouseDocumentRecord.SourceWarehouse + " -> " + warehouseDocumentRecord.TargetWarehouse));
		context.LinkLabel.Text = (string.IsNullOrWhiteSpace(warehouseDocumentRecord?.RelatedDocument) ? "—" : warehouseDocumentRecord.RelatedDocument);
		context.SourceLabel.Text = warehouseDocumentRecord?.SourceLabel ?? "—";
		context.CommentLabel.Text = (string.IsNullOrWhiteSpace(warehouseDocumentRecord?.Comment) ? "—" : warehouseDocumentRecord.Comment);
		context.TotalLabel.Text = ((warehouseDocumentRecord == null) ? "—" : $"{warehouseDocumentRecord.Lines.Sum((WarehouseDocumentLineRecord item) => item.Quantity):N2}");
		context.LineBindingSource.DataSource = warehouseDocumentRecord?.Lines.Select((WarehouseDocumentLineRecord item) => new DocumentLineGridRow(item)).ToArray() ?? Array.Empty<DocumentLineGridRow>();
		context.FieldBindingSource.DataSource = warehouseDocumentRecord?.Fields.Select((OneCFieldValue item) => new FieldGridRow(item)).ToArray() ?? Array.Empty<FieldGridRow>();
	}

	private void RefreshWorkspaceQueues()
	{
		_transferQueueBindingSource.DataSource = (from item in (from item in _transferContext.Records
				orderby ResolveDocumentPriority(item.Record), item.Record.Date ?? DateTime.MinValue descending
				select item).Take(8)
			select new QueueDocumentRow(item.OperationalId, item.Record)).ToArray();
		_reservationQueueBindingSource.DataSource = (from item in (from item in _reservationContext.Records
				orderby ResolveDocumentPriority(item.Record), item.Record.Date ?? DateTime.MinValue descending
				select item).Take(8)
			select new QueueDocumentRow(item.OperationalId, item.Record)).ToArray();
		_inventoryQueueBindingSource.DataSource = (from item in (from item in _inventoryContext.Records
				orderby ResolveDocumentPriority(item.Record), item.Record.Date ?? DateTime.MinValue descending
				select item).Take(8)
			select new QueueDocumentRow(item.OperationalId, item.Record)).ToArray();
		_writeOffQueueBindingSource.DataSource = (from item in (from item in _writeOffContext.Records
				orderby ResolveDocumentPriority(item.Record), item.Record.Date ?? DateTime.MinValue descending
				select item).Take(8)
			select new QueueDocumentRow(item.OperationalId, item.Record)).ToArray();
		_transferQueueCountLabel.Text = $"{_transferContext.Records.Count:N0}";
		_reservationQueueCountLabel.Text = $"{_reservationContext.Records.Count:N0}";
		_inventoryQueueCountLabel.Text = $"{_inventoryContext.Records.Count:N0}";
		_writeOffQueueCountLabel.Text = $"{_writeOffContext.Records.Count:N0}";
	}

	private void RefreshOperationsLog()
	{
		_operationBindingSource.DataSource = _workspace.OperationLog.Select((WarehouseOperationLogEntry item) => new OperationGridRow(item)).ToArray();
	}

	private void CreateTransfer()
	{
		using WarehouseDocumentEditorForm warehouseDocumentEditorForm = new WarehouseDocumentEditorForm(_workspace, WarehouseDocumentEditorMode.Transfer);
		if (DialogTabsHost.ShowDialog(warehouseDocumentEditorForm, FindForm()) == DialogResult.OK && warehouseDocumentEditorForm.ResultDocument != null)
		{
			_workspace.AddTransferOrder(warehouseDocumentEditorForm.ResultDocument);
			RefreshDocumentGrid(_transferContext, warehouseDocumentEditorForm.ResultDocument.Id);
		}
	}

	private void EditSelectedTransfer()
	{
		OperationalWarehouseDocumentRecord operationalDocument = GetOperationalDocument(_transferContext, _workspace.TransferOrders);
		if (operationalDocument == null)
		{
			ShowSelectionWarning("Сначала выберите перемещение.");
			return;
		}
		using WarehouseDocumentEditorForm warehouseDocumentEditorForm = new WarehouseDocumentEditorForm(_workspace, WarehouseDocumentEditorMode.Transfer, operationalDocument);
		if (DialogTabsHost.ShowDialog(warehouseDocumentEditorForm, FindForm()) == DialogResult.OK && warehouseDocumentEditorForm.ResultDocument != null)
		{
			_workspace.UpdateTransferOrder(warehouseDocumentEditorForm.ResultDocument);
			RefreshDocumentGrid(_transferContext, warehouseDocumentEditorForm.ResultDocument.Id);
		}
	}

	private void PrepareSelectedTransfer()
	{
		OperationalWarehouseDocumentRecord operationalDocument = GetOperationalDocument(_transferContext, _workspace.TransferOrders);
		if (operationalDocument == null)
		{
			ShowSelectionWarning("Сначала выберите перемещение.");
			return;
		}
		ShowWorkflowResult(_workspace.MarkTransferReady(operationalDocument.Id));
		RefreshDocumentGrid(_transferContext, operationalDocument.Id);
	}

	private void CompleteSelectedTransfer()
	{
		OperationalWarehouseDocumentRecord operationalDocument = GetOperationalDocument(_transferContext, _workspace.TransferOrders);
		if (operationalDocument == null)
		{
			ShowSelectionWarning("Сначала выберите перемещение.");
			return;
		}
		ShowWorkflowResult(_workspace.CompleteTransfer(operationalDocument.Id));
		RefreshDocumentGrid(_transferContext, operationalDocument.Id);
	}

	private void PrintSelectedTransfer()
	{
		OperationalWarehouseDocumentRecord operationalDocument = GetOperationalDocument(_transferContext, _workspace.TransferOrders);
		if (operationalDocument == null)
		{
			ShowSelectionWarning("Сначала выберите перемещение.");
			return;
		}
		using DocumentPrintPreviewForm dialog = new DocumentPrintPreviewForm("Печать перемещения " + operationalDocument.Number, OperationalDocumentPrintComposer.BuildWarehouseDocumentHtml(operationalDocument));
		DialogTabsHost.ShowDialog(dialog, FindForm());
	}

	private void CreateInventory()
	{
		using WarehouseDocumentEditorForm warehouseDocumentEditorForm = new WarehouseDocumentEditorForm(_workspace, WarehouseDocumentEditorMode.Inventory);
		if (DialogTabsHost.ShowDialog(warehouseDocumentEditorForm, FindForm()) == DialogResult.OK && warehouseDocumentEditorForm.ResultDocument != null)
		{
			_workspace.AddInventoryCount(warehouseDocumentEditorForm.ResultDocument);
			RefreshDocumentGrid(_inventoryContext, warehouseDocumentEditorForm.ResultDocument.Id);
		}
	}

	private void EditSelectedInventory()
	{
		OperationalWarehouseDocumentRecord operationalDocument = GetOperationalDocument(_inventoryContext, _workspace.InventoryCounts);
		if (operationalDocument == null)
		{
			ShowSelectionWarning("Сначала выберите инвентаризацию.");
			return;
		}
		using WarehouseDocumentEditorForm warehouseDocumentEditorForm = new WarehouseDocumentEditorForm(_workspace, WarehouseDocumentEditorMode.Inventory, operationalDocument);
		if (DialogTabsHost.ShowDialog(warehouseDocumentEditorForm, FindForm()) == DialogResult.OK && warehouseDocumentEditorForm.ResultDocument != null)
		{
			_workspace.UpdateInventoryCount(warehouseDocumentEditorForm.ResultDocument);
			RefreshDocumentGrid(_inventoryContext, warehouseDocumentEditorForm.ResultDocument.Id);
		}
	}

	private void PostSelectedInventory()
	{
		OperationalWarehouseDocumentRecord operationalDocument = GetOperationalDocument(_inventoryContext, _workspace.InventoryCounts);
		if (operationalDocument == null)
		{
			ShowSelectionWarning("Сначала выберите инвентаризацию.");
			return;
		}
		ShowWorkflowResult(_workspace.PostInventoryCount(operationalDocument.Id));
		RefreshDocumentGrid(_inventoryContext, operationalDocument.Id);
	}

	private void PrintSelectedInventory()
	{
		OperationalWarehouseDocumentRecord operationalDocument = GetOperationalDocument(_inventoryContext, _workspace.InventoryCounts);
		if (operationalDocument == null)
		{
			ShowSelectionWarning("Сначала выберите инвентаризацию.");
			return;
		}
		using DocumentPrintPreviewForm dialog = new DocumentPrintPreviewForm("Печать инвентаризации " + operationalDocument.Number, OperationalDocumentPrintComposer.BuildWarehouseDocumentHtml(operationalDocument));
		DialogTabsHost.ShowDialog(dialog, FindForm());
	}

	private void CreateWriteOff()
	{
		using WarehouseDocumentEditorForm warehouseDocumentEditorForm = new WarehouseDocumentEditorForm(_workspace, WarehouseDocumentEditorMode.WriteOff);
		if (DialogTabsHost.ShowDialog(warehouseDocumentEditorForm, FindForm()) == DialogResult.OK && warehouseDocumentEditorForm.ResultDocument != null)
		{
			_workspace.AddWriteOff(warehouseDocumentEditorForm.ResultDocument);
			RefreshDocumentGrid(_writeOffContext, warehouseDocumentEditorForm.ResultDocument.Id);
		}
	}

	private void EditSelectedWriteOff()
	{
		OperationalWarehouseDocumentRecord operationalDocument = GetOperationalDocument(_writeOffContext, _workspace.WriteOffs);
		if (operationalDocument == null)
		{
			ShowSelectionWarning("Сначала выберите списание.");
			return;
		}
		using WarehouseDocumentEditorForm warehouseDocumentEditorForm = new WarehouseDocumentEditorForm(_workspace, WarehouseDocumentEditorMode.WriteOff, operationalDocument);
		if (DialogTabsHost.ShowDialog(warehouseDocumentEditorForm, FindForm()) == DialogResult.OK && warehouseDocumentEditorForm.ResultDocument != null)
		{
			_workspace.UpdateWriteOff(warehouseDocumentEditorForm.ResultDocument);
			RefreshDocumentGrid(_writeOffContext, warehouseDocumentEditorForm.ResultDocument.Id);
		}
	}

	private void PrintSelectedWriteOff()
	{
		OperationalWarehouseDocumentRecord operationalDocument = GetOperationalDocument(_writeOffContext, _workspace.WriteOffs);
		if (operationalDocument == null)
		{
			ShowSelectionWarning("Сначала выберите списание.");
			return;
		}
		using DocumentPrintPreviewForm dialog = new DocumentPrintPreviewForm("Печать списания " + operationalDocument.Number, OperationalDocumentPrintComposer.BuildWarehouseDocumentHtml(operationalDocument));
		DialogTabsHost.ShowDialog(dialog, FindForm());
	}

	private void PostSelectedWriteOff()
	{
		OperationalWarehouseDocumentRecord operationalDocument = GetOperationalDocument(_writeOffContext, _workspace.WriteOffs);
		if (operationalDocument == null)
		{
			ShowSelectionWarning("Сначала выберите списание.");
			return;
		}
		ShowWorkflowResult(_workspace.PostWriteOff(operationalDocument.Id));
		RefreshDocumentGrid(_writeOffContext, operationalDocument.Id);
	}

	private OperationalWarehouseDocumentRecord? GetOperationalDocument(DocumentTabContext context, IEnumerable<OperationalWarehouseDocumentRecord> source)
	{
		Guid? selectedId = GetSelectedDocumentId(context);
		return (!selectedId.HasValue) ? null : source.FirstOrDefault((OperationalWarehouseDocumentRecord item) => item.Id == selectedId.Value);
	}

	private Guid? GetSelectedDocumentId(DocumentTabContext context)
	{
		return (context.RecordGrid.CurrentRow?.DataBoundItem as DocumentGridRow)?.DocumentId;
	}

	private void HandleStatusCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
	{
		if (sender is DataGridView dataGridView && e.RowIndex >= 0 && e.ColumnIndex >= 0 && e.ColumnIndex < dataGridView.Columns.Count)
		{
			DataGridViewColumn dataGridViewColumn = dataGridView.Columns[e.ColumnIndex];
			if (string.Equals(dataGridViewColumn.DataPropertyName, "Status", StringComparison.OrdinalIgnoreCase) && e.Value is string text && !string.IsNullOrWhiteSpace(text))
			{
				ApplyStatusCellStyle(dataGridView, e, text);
			}
		}
	}

	private static void ApplyStatusCellStyle(DataGridView grid, DataGridViewCellFormattingEventArgs e, string status)
	{
		DataGridViewCellStyle dataGridViewCellStyle2 = (e.CellStyle = e.CellStyle ?? new DataGridViewCellStyle(grid.DefaultCellStyle));
		if (status.Contains("ошиб", StringComparison.OrdinalIgnoreCase) || status.Contains("error", StringComparison.OrdinalIgnoreCase) || status.Contains("отмен", StringComparison.OrdinalIgnoreCase) || status.Contains("cancel", StringComparison.OrdinalIgnoreCase))
		{
			dataGridViewCellStyle2.BackColor = Color.FromArgb(251, 231, 227);
			dataGridViewCellStyle2.ForeColor = DesktopTheme.Danger;
		}
		else if (status.Contains("критич", StringComparison.OrdinalIgnoreCase) || status.Contains("critical", StringComparison.OrdinalIgnoreCase))
		{
			dataGridViewCellStyle2.BackColor = Color.FromArgb(255, 241, 241);
			dataGridViewCellStyle2.ForeColor = DesktopTheme.Danger;
		}
		else if (status.Contains("норм", StringComparison.OrdinalIgnoreCase) || status.Contains("normal", StringComparison.OrdinalIgnoreCase))
		{
			dataGridViewCellStyle2.BackColor = Color.FromArgb(240, 250, 244);
			dataGridViewCellStyle2.ForeColor = Color.FromArgb(38, 168, 91);
		}
		else if (status.Contains("архив", StringComparison.OrdinalIgnoreCase) || status.Contains("заверш", StringComparison.OrdinalIgnoreCase) || status.Contains("проведен", StringComparison.OrdinalIgnoreCase) || status.Contains("списан", StringComparison.OrdinalIgnoreCase) || status.Contains("complete", StringComparison.OrdinalIgnoreCase))
		{
			dataGridViewCellStyle2.BackColor = DesktopTheme.SurfaceMuted;
			dataGridViewCellStyle2.ForeColor = DesktopTheme.TextMuted;
		}
		else if (status.Contains("чернов", StringComparison.OrdinalIgnoreCase) || status.Contains("нов", StringComparison.OrdinalIgnoreCase) || status.Contains("резерв", StringComparison.OrdinalIgnoreCase) || status.Contains("под контроль", StringComparison.OrdinalIgnoreCase) || status.Contains("draft", StringComparison.OrdinalIgnoreCase) || status.Contains("new", StringComparison.OrdinalIgnoreCase))
		{
			dataGridViewCellStyle2.BackColor = DesktopTheme.PrimarySoft;
			dataGridViewCellStyle2.ForeColor = DesktopTheme.SidebarButtonActiveText;
		}
		else
		{
			dataGridViewCellStyle2.BackColor = DesktopTheme.InfoSoft;
			dataGridViewCellStyle2.ForeColor = DesktopTheme.Info;
		}
	}

	private string ResolvePrimaryWarehouseLabel()
	{
		string text = (from item in _runtimeView.StockBalances
			where !string.IsNullOrWhiteSpace(item.Warehouse)
			group item by item.Warehouse into item
			orderby item.Count() descending
			select item.Key).FirstOrDefault() ?? "Главный склад";
		return text;
	}

	private static void ApplyStockBadgeStyle(Label label, string? status)
	{
		string text = status ?? string.Empty;
		if (text.Contains("критич", StringComparison.OrdinalIgnoreCase))
		{
			label.ForeColor = DesktopTheme.Danger;
			label.BackColor = Color.FromArgb(255, 241, 241);
		}
		else if (text.Contains("под контроль", StringComparison.OrdinalIgnoreCase))
		{
			label.ForeColor = Color.FromArgb(255, 151, 34);
			label.BackColor = Color.FromArgb(255, 248, 237);
		}
		else
		{
			label.ForeColor = Color.FromArgb(38, 168, 91);
			label.BackColor = Color.FromArgb(240, 250, 244);
		}
	}

	private string BuildStockMovementDigest(WarehouseStockBalanceRecord record)
	{
		List<(DateTime Date, string Text)> list = new List<(DateTime, string)>();
		foreach (OperationalWarehouseDocumentRecord item in _workspace.TransferOrders)
		{
			decimal num = item.Lines.Where((OperationalWarehouseLineRecord line) => MatchesStockLine(record, line.ItemCode, line.ItemName)).Sum((OperationalWarehouseLineRecord line) => line.Quantity);
			if (!(num <= 0m))
			{
				string text = string.Equals(item.TargetWarehouse, record.Warehouse, StringComparison.OrdinalIgnoreCase) ? "+" : "-";
				list.Add((item.DocumentDate, $"{item.DocumentDate:dd.MM.yyyy HH:mm}   Перемещение {item.Number}   {text}{num:N0} {record.Unit}"));
			}
		}
		foreach (OperationalWarehouseDocumentRecord item2 in _workspace.InventoryCounts)
		{
			decimal num2 = item2.Lines.Where((OperationalWarehouseLineRecord line) => MatchesStockLine(record, line.ItemCode, line.ItemName)).Sum((OperationalWarehouseLineRecord line) => line.Quantity);
			if (!(num2 <= 0m))
			{
				list.Add((item2.DocumentDate, $"{item2.DocumentDate:dd.MM.yyyy HH:mm}   Инвентаризация {item2.Number}   +{num2:N0} {record.Unit}"));
			}
		}
		foreach (OperationalWarehouseDocumentRecord item3 in _workspace.WriteOffs)
		{
			decimal num3 = item3.Lines.Where((OperationalWarehouseLineRecord line) => MatchesStockLine(record, line.ItemCode, line.ItemName)).Sum((OperationalWarehouseLineRecord line) => line.Quantity);
			if (!(num3 <= 0m))
			{
				list.Add((item3.DocumentDate, $"{item3.DocumentDate:dd.MM.yyyy HH:mm}   Списание {item3.Number}   -{num3:N0} {record.Unit}"));
			}
		}
		foreach (WarehouseDocumentRecord item4 in _runtimeView.Reservations)
		{
			decimal num4 = item4.Lines.Where((WarehouseDocumentLineRecord line) => MatchesStockLine(record, string.Empty, line.Item)).Sum((WarehouseDocumentLineRecord line) => line.Quantity);
			if (!(num4 <= 0m))
			{
				string text2 = item4.Date?.ToString("dd.MM.yyyy HH:mm") ?? "—";
				list.Add((item4.Date ?? DateTime.MinValue, $"{text2}   Резерв {item4.Number}   -{num4:N0} {record.Unit}"));
			}
		}
		string[] array = list.OrderByDescending(((DateTime Date, string Text) item) => item.Date).Take(5).Select(((DateTime Date, string Text) item) => item.Text).ToArray();
		return (array.Length == 0) ? "Нет связанных движений." : string.Join(Environment.NewLine, array);
	}

	private string BuildRelatedDocumentsDigest(WarehouseStockBalanceRecord record)
	{
		List<string> list = new List<string>();
		list.AddRange(_workspace.TransferOrders.Where((OperationalWarehouseDocumentRecord item) => item.Lines.Any((OperationalWarehouseLineRecord line) => MatchesStockLine(record, line.ItemCode, line.ItemName))).OrderByDescending((OperationalWarehouseDocumentRecord item) => item.DocumentDate).Take(3).Select((OperationalWarehouseDocumentRecord item) => $"{item.Number}   ({item.Status})"));
		list.AddRange(_runtimeView.Reservations.Where((WarehouseDocumentRecord item) => item.Lines.Any((WarehouseDocumentLineRecord line) => MatchesStockLine(record, string.Empty, line.Item))).OrderByDescending((WarehouseDocumentRecord item) => item.Date ?? DateTime.MinValue).Take(3).Select((WarehouseDocumentRecord item) => $"{item.Number}   ({item.Status})"));
		list.AddRange(_workspace.InventoryCounts.Where((OperationalWarehouseDocumentRecord item) => item.Lines.Any((OperationalWarehouseLineRecord line) => MatchesStockLine(record, line.ItemCode, line.ItemName))).OrderByDescending((OperationalWarehouseDocumentRecord item) => item.DocumentDate).Take(2).Select((OperationalWarehouseDocumentRecord item) => $"{item.Number}   ({item.Status})"));
		string[] array = list.Distinct(StringComparer.OrdinalIgnoreCase).Take(5).ToArray();
		return (array.Length == 0) ? "Нет связанных документов." : string.Join(Environment.NewLine, array);
	}

	private static bool MatchesStockLine(WarehouseStockBalanceRecord record, string itemCode, string itemName)
	{
		return string.Equals(record.ItemCode, itemCode, StringComparison.OrdinalIgnoreCase) || string.Equals(record.ItemName, itemName, StringComparison.OrdinalIgnoreCase);
	}

	private static decimal ResolveMinimumStock(WarehouseStockBalanceRecord record)
	{
		decimal num = Math.Max(record.ReservedQuantity + record.ShippedQuantity, 10m);
		return Math.Ceiling(num / 10m) * 10m;
	}

	private static string ResolvePseudoBarcode(WarehouseStockBalanceRecord record)
	{
		string text = new string(record.ItemCode.Where(char.IsLetterOrDigit).ToArray());
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "SKU";
		}
		int hashCode = Math.Abs((record.ItemCode + "|" + record.ItemName).GetHashCode());
		return $"{hashCode % 1000000000:000000000}{Math.Abs(text.GetHashCode()) % 1000:000}";
	}

	private void ExportCurrentStockView()
	{
		StockGridRow[] array = (_stockBindingSource.DataSource as IEnumerable<StockGridRow>)?.ToArray() ?? Array.Empty<StockGridRow>();
		if (array.Length == 0)
		{
			MessageBox.Show(this, "Нет данных для экспорта.", "Склад", MessageBoxButtons.OK, MessageBoxIcon.Information);
			return;
		}
		using SaveFileDialog saveFileDialog = new SaveFileDialog
		{
			Filter = "CSV (*.csv)|*.csv",
			FileName = $"warehouse-stock-{DateTime.Now:yyyyMMdd-HHmm}.csv",
			Title = "Экспорт остатков"
		};
		if (saveFileDialog.ShowDialog(FindForm()) != DialogResult.OK || string.IsNullOrWhiteSpace(saveFileDialog.FileName))
		{
			return;
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("Код;Товар;Склад;Свободно;Резерв;В пути;Мин. остаток;Статус");
		foreach (StockGridRow stockGridRow in array)
		{
			stringBuilder.AppendLine(string.Join(";", EscapeCsv(stockGridRow.Code), EscapeCsv(stockGridRow.Item), EscapeCsv(stockGridRow.Warehouse), stockGridRow.Record.FreeQuantity.ToString("N0"), stockGridRow.Record.ReservedQuantity.ToString("N0"), stockGridRow.Record.ShippedQuantity.ToString("N0"), stockGridRow.MinimumQuantity.ToString("N0"), EscapeCsv(stockGridRow.Status)));
		}
		File.WriteAllText(saveFileDialog.FileName, stringBuilder.ToString(), Encoding.UTF8);
		MessageBox.Show(this, "Экспорт сохранен.", "Склад", MessageBoxButtons.OK, MessageBoxIcon.Information);
	}

	private static string EscapeCsv(string text)
	{
		return "\"" + (text ?? string.Empty).Replace("\"", "\"\"") + "\"";
	}

	private static bool MatchesDocumentSearch(WarehouseDocumentRecord record, string search)
	{
		return Contains(record.Number, search) || Contains(record.Status, search) || Contains(record.SourceWarehouse, search) || Contains(record.TargetWarehouse, search) || Contains(record.RelatedDocument, search) || Contains(record.Comment, search);
	}

	private static int ResolveStockPriority(WarehouseStockBalanceRecord record)
	{
		if (string.Equals(record.Status, "Критично", StringComparison.OrdinalIgnoreCase))
		{
			return 0;
		}
		if (string.Equals(record.Status, "Под контроль", StringComparison.OrdinalIgnoreCase))
		{
			return 1;
		}
		return 2;
	}

	private static int ResolveDocumentPriority(WarehouseDocumentRecord record)
	{
		if (string.Equals(record.Status, "Черновик", StringComparison.OrdinalIgnoreCase) || string.Equals(record.Status, "К перемещению", StringComparison.OrdinalIgnoreCase))
		{
			return 0;
		}
		if (string.Equals(record.Status, "Проведена", StringComparison.OrdinalIgnoreCase) || string.Equals(record.Status, "Списано", StringComparison.OrdinalIgnoreCase) || string.Equals(record.Status, "Перемещен", StringComparison.OrdinalIgnoreCase))
		{
			return 2;
		}
		return 1;
	}

	private static bool Contains(string source, string value)
	{
		return source.Contains(value, StringComparison.OrdinalIgnoreCase);
	}

	private void RestoreGridSelection(DataGridView grid, Guid? selectedId)
	{
		if (!selectedId.HasValue)
		{
			return;
		}
		foreach (DataGridViewRow item in (IEnumerable)grid.Rows)
		{
			DocumentGridRow obj = item.DataBoundItem as DocumentGridRow;
			bool num;
			if (obj == null)
			{
				num = !selectedId.HasValue;
			}
			else
			{
				Guid? documentId = obj.DocumentId;
				Guid? guid = selectedId;
				if (documentId.HasValue != guid.HasValue)
				{
					continue;
				}
				if (!documentId.HasValue)
				{
					goto IL_008f;
				}
				num = documentId.GetValueOrDefault() == guid.GetValueOrDefault();
			}
			if (!num)
			{
				continue;
			}
			goto IL_008f;
			IL_008f:
			item.Selected = true;
			grid.CurrentCell = item.Cells[0];
			break;
		}
	}

	private IReadOnlyList<DocumentViewRecord> MapOperationalDocuments(IEnumerable<OperationalWarehouseDocumentRecord> documents)
	{
		return (from item in documents
			orderby item.DocumentDate descending, item.Number descending
			select new DocumentViewRecord(item.Id, new WarehouseDocumentRecord
			{
				DocumentType = item.DocumentType,
				Number = item.Number,
				Date = item.DocumentDate,
				Status = item.Status,
				SourceWarehouse = item.SourceWarehouse,
				TargetWarehouse = item.TargetWarehouse,
				RelatedDocument = item.RelatedDocument,
				Comment = item.Comment,
				SourceLabel = item.SourceLabel,
				Title = item.DocumentType,
				Subtitle = item.SourceWarehouse + " -> " + item.TargetWarehouse,
				Fields = item.Fields.ToArray(),
				Lines = item.Lines.Select((OperationalWarehouseLineRecord line, int index) => new WarehouseDocumentLineRecord
				{
					RowNumber = index + 1,
					Item = line.ItemName,
					Quantity = line.Quantity,
					Unit = line.Unit,
					SourceLocation = line.SourceLocation,
					TargetLocation = line.TargetLocation,
					RelatedDocument = line.RelatedDocument,
					Fields = line.Fields.ToArray()
				}).ToArray()
			})).ToArray();
	}

	private void ShowSelectionWarning(string message)
	{
		MessageBox.Show(this, message, "Склад", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
	}

	private void ShowWorkflowResult(WarehouseWorkflowActionResult result)
	{
		MessageBox.Show(this, result.Detail, result.Message, MessageBoxButtons.OK, result.Succeeded ? MessageBoxIcon.Asterisk : MessageBoxIcon.Exclamation);
	}
}


