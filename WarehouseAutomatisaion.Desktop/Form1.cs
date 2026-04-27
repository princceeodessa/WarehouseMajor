using WarehouseAutomatisaion.Desktop.Controls;
using WarehouseAutomatisaion.Desktop.Data;
using WarehouseAutomatisaion.Desktop.Forms;

namespace WarehouseAutomatisaion.Desktop;

public sealed class Form1 : Form
{
    private const int SidebarExpandedWidth = 252;
    private const int SidebarCollapsedWidth = 78;

    private readonly DemoWorkspace _workspace = DemoWorkspace.Create();
    private readonly SalesWorkspaceStore _salesWorkspaceStore;
    private readonly SalesWorkspace _salesWorkspace;
    private readonly OneCLiveSyncDesktopService _liveSyncService;
    private readonly FunctionalCoverageSnapshot _coverage = FunctionalCoverageSnapshot.Create();
    private readonly Dictionary<AppSection, Control> _pages = [];
    private readonly Dictionary<AppSection, Button> _navButtons = [];
    private readonly Panel _pageHost = new();
    private readonly TabControl _documentTabs = new();
    private readonly Dictionary<Form, TabPage> _dialogTabsByForm = [];
    private readonly Dictionary<TabPage, Form> _dialogFormsByTab = [];
    private readonly Label _pageTitleLabel = new();
    private readonly Label _pageSubtitleLabel = new();
    private readonly Label _workspaceLabel = new();
    private readonly Label _operatorLabel = new();
    private readonly Label _dateLabel = new();
    private readonly Label _statusLabel = new();
    private readonly FlowLayoutPanel _pageMetaFlow = new();
    private readonly Label _sectionChip = DesktopSurfaceFactory.CreateInfoChip("Главная", DesktopTheme.PrimarySoft, DesktopTheme.SidebarButtonActiveText);
    private readonly Label _platformChip = DesktopSurfaceFactory.CreateInfoChip("Desktop only");
    private readonly Label _dataSourceChip = DesktopSurfaceFactory.CreateInfoChip("MySQL");
    private readonly TextBox _globalSearchTextBox = new();
    private readonly Button _headerSyncButton = new();
    private readonly Button _headerActionsButton = new();
    private readonly ContextMenuStrip _headerActionsMenu = new();
    private readonly ToolStripMenuItem _exportModuleMenuItem = new("Выгрузить текущий раздел");
    private readonly ToolStripMenuItem _syncFromOneCMenuItem = new("Синхронизация с 1С");
    private readonly ToolTip _headerToolTip = new();
    private readonly System.Windows.Forms.Timer _clockTimer = new();
    private readonly System.Windows.Forms.Timer _workspacePersistDebounceTimer = new();
    private readonly Panel _sidebarPanel = new();
    private readonly FlowLayoutPanel _navFlow = new();
    private readonly Label _sidebarFooterLabel = new();
    private readonly Button _sidebarToggleButton = new();
    private readonly List<Label> _sidebarGroupLabels = [];
    private readonly Font _sidebarIconFont = new("Segoe MDL2 Assets", 12f, FontStyle.Regular);
    private readonly Font _sidebarIconFontActive = new("Segoe MDL2 Assets", 13f, FontStyle.Regular);
    private readonly DesktopMySqlBackplaneService? _backplane = DesktopMySqlBackplaneService.TryCreateDefault();
    private AppSection _currentSection = AppSection.Dashboard;
    private bool _initialLoadStarted;
    private bool _oneCSyncInProgress;
    private bool _workspacePersistenceEnabled;
    private bool _workspaceSavePending;
    private bool _sidebarCollapsed;
    private TabPage? _workspaceTabPage;
    private Panel? _sidebarStatusCard;
    private Panel? _brandPanel;
    private Panel? _headerPanel;
    private Panel? _statusPanel;

    public Form1(SalesWorkspaceStore salesWorkspaceStore, SalesWorkspace salesWorkspace)
    {
        _salesWorkspaceStore = salesWorkspaceStore;
        _salesWorkspace = salesWorkspace;
        _liveSyncService = OneCLiveSyncDesktopService.CreateDefault(salesWorkspaceStore);
        _salesWorkspace.Changed += HandleSalesWorkspaceChanged;
        KeyPreview = true;
        KeyDown += HandleMainFormKeyDown;

        BuildShell();
        TextMojibakeFixer.NormalizeControlTree(this);
        DialogTabsHost.Attach(this);
        _headerToolTip.ShowAlways = true;
        SeedPages();
        ActivateSection(AppSection.Dashboard);
        ConfigureClock();
        _workspacePersistDebounceTimer.Interval = 700;
        _workspacePersistDebounceTimer.Tick += HandleWorkspacePersistDebounceTick;

        Shown += (_, _) => BeginInitialLoad();
        FormClosing += (_, _) =>
        {
            FlushPendingWorkspaceSave();
        };
        FormClosed += (_, _) =>
        {
            _salesWorkspace.Changed -= HandleSalesWorkspaceChanged;
            KeyDown -= HandleMainFormKeyDown;
            _workspacePersistDebounceTimer.Stop();
            _workspacePersistDebounceTimer.Tick -= HandleWorkspacePersistDebounceTick;
            _workspacePersistDebounceTimer.Dispose();
            foreach (var dialog in _dialogTabsByForm.Keys.ToList())
            {
                if (!dialog.IsDisposed)
                {
                    if (dialog.DialogResult == DialogResult.None)
                    {
                        dialog.DialogResult = DialogResult.Cancel;
                    }

                    dialog.Close();
                }
            }

            DialogTabsHost.Detach(this);
            _sidebarIconFont.Dispose();
            _sidebarIconFontActive.Dispose();
        };
    }

    private void HandleSalesWorkspaceChanged(object? sender, EventArgs e)
    {
        if (_workspacePersistenceEnabled)
        {
            ScheduleWorkspaceSave();
        }
    }

    private void ScheduleWorkspaceSave()
    {
        _workspaceSavePending = true;
        _workspacePersistDebounceTimer.Stop();
        _workspacePersistDebounceTimer.Start();
    }

    private void HandleWorkspacePersistDebounceTick(object? sender, EventArgs e)
    {
        _workspacePersistDebounceTimer.Stop();
        FlushPendingWorkspaceSave();
    }

    private void FlushPendingWorkspaceSave()
    {
        if (!_workspacePersistenceEnabled || !_workspaceSavePending)
        {
            return;
        }

        _workspaceSavePending = false;
        _salesWorkspaceStore.Save(_salesWorkspace);
    }

    private void HandleMainFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Control && !e.Shift)
        {
            if (e.KeyCode == Keys.W)
            {
                if (TryCloseActiveDialogTab())
                {
                    e.SuppressKeyPress = true;
                    return;
                }
            }

            if (e.KeyCode == Keys.D1)
            {
                ActivateSection(AppSection.Dashboard);
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.D2)
            {
                ActivateSection(AppSection.Sales);
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.D3)
            {
                ActivateSection(AppSection.Purchasing);
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.D4)
            {
                ActivateSection(AppSection.Warehouse);
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.D5)
            {
                ActivateSection(AppSection.Catalog);
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.F)
            {
                _globalSearchTextBox.Focus();
                _globalSearchTextBox.SelectAll();
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.E)
            {
                ExportCurrentModule();
                e.SuppressKeyPress = true;
                return;
            }
        }

        if (e.Control && e.Shift && e.KeyCode == Keys.S)
        {
            _ = SyncFromOneCAsync();
            e.SuppressKeyPress = true;
            return;
        }

        if (e.KeyCode == Keys.F5)
        {
            RefreshCurrentSection();
            e.SuppressKeyPress = true;
        }
    }

    private void RefreshCurrentSection()
    {
        if (_currentSection is AppSection.Sales or AppSection.Purchasing or AppSection.Warehouse or AppSection.Audit or AppSection.Catalog)
        {
            InvalidateDataPages();
            SetStatus($"Обновлен раздел: {AppSectionCatalog.All.First(item => item.Key == _currentSection).Caption}.");
            return;
        }

        ActivateSection(_currentSection);
    }

    private void BuildShell()
    {
        SuspendLayout();

        AutoScaleMode = AutoScaleMode.Font;
        BackColor = DesktopTheme.AppBackground;
        ClientSize = new Size(1620, 980);
        MinimumSize = new Size(1400, 860);
        StartPosition = FormStartPosition.CenterScreen;
        Text = "МойБизнес";
        WindowState = FormWindowState.Maximized;

        _sidebarPanel.Dock = DockStyle.Left;
        _sidebarPanel.Width = SidebarExpandedWidth;
        _sidebarPanel.BackColor = DesktopTheme.SidebarBackground;
        _sidebarPanel.Padding = new Padding(16, 12, 16, 18);

        _sidebarFooterLabel.Dock = DockStyle.Bottom;
        _sidebarFooterLabel.Height = 44;
        _sidebarFooterLabel.Text = "Desktop shell для замены 1С";
        _sidebarFooterLabel.Font = DesktopTheme.BodyFont(8.8f);
        _sidebarFooterLabel.ForeColor = DesktopTheme.TextMuted;

        _navFlow.Dock = DockStyle.Fill;
        _navFlow.FlowDirection = FlowDirection.TopDown;
        _navFlow.WrapContents = false;
        _navFlow.AutoScroll = true;
        _navFlow.Padding = new Padding(0, 16, 0, 0);

        foreach (var section in AppSectionCatalog.All)
        {
            var button = CreateNavButton(section);
            _navButtons[section.Key] = button;
            _navFlow.Controls.Add(button);
        }

        _sidebarToggleButton.Dock = DockStyle.Top;
        _sidebarToggleButton.Height = 34;
        _sidebarToggleButton.Text = "\u25C0";
        _sidebarToggleButton.FlatStyle = FlatStyle.Flat;
        _sidebarToggleButton.FlatAppearance.BorderSize = 0;
        _sidebarToggleButton.BackColor = DesktopTheme.SidebarBackground;
        _sidebarToggleButton.ForeColor = DesktopTheme.TextSecondary;
        _sidebarToggleButton.Font = DesktopTheme.EmphasisFont(11f);
        _sidebarToggleButton.Cursor = Cursors.Hand;
        _sidebarToggleButton.TextAlign = ContentAlignment.MiddleRight;
        _sidebarToggleButton.Padding = new Padding(0, 0, 2, 0);
        _sidebarToggleButton.Click += (_, _) =>
        {
            _sidebarCollapsed = !_sidebarCollapsed;
            ApplySidebarState();
        };

        _sidebarStatusCard = CreateSidebarStatusCard() as Panel;
        _brandPanel = CreateBrandPanel() as Panel;

        _sidebarPanel.Controls.Add(_navFlow);
        _sidebarPanel.Controls.Add(_sidebarFooterLabel);
        if (_sidebarStatusCard is not null)
        {
            _sidebarPanel.Controls.Add(_sidebarStatusCard);
        }

        if (_brandPanel is not null)
        {
            _sidebarPanel.Controls.Add(_brandPanel);
        }

        _sidebarPanel.Controls.Add(_sidebarToggleButton);

        var mainShell = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = DesktopTheme.AppBackground
        };

        var headerPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 94,
            Padding = new Padding(22, 18, 22, 12),
            BackColor = DesktopTheme.HeaderBackground
        };
        _headerPanel = headerPanel;
        headerPanel.Controls.Add(CreateHeaderRightPanel());
        headerPanel.Controls.Add(CreateHeaderTitlePanel());

        var statusPanel = CreateStatusPanel();
        _statusPanel = statusPanel as Panel;

        var pageCanvas = DesktopSurfaceFactory.CreateCanvasPanel();
        _documentTabs.Dock = DockStyle.Fill;
        _documentTabs.Multiline = false;
        _documentTabs.Padding = new Point(18, 6);
        _documentTabs.Appearance = TabAppearance.Normal;
        _documentTabs.HotTrack = true;
        _pageHost.Dock = DockStyle.Fill;
        _pageHost.BackColor = DesktopTheme.AppBackground;
        _workspaceTabPage = new TabPage("Workspace")
        {
            BackColor = DesktopTheme.AppBackground,
            Padding = new Padding(0)
        };
        _workspaceTabPage.Controls.Add(_pageHost);
        _documentTabs.TabPages.Add(_workspaceTabPage);
        pageCanvas.Controls.Add(_documentTabs);

        mainShell.Controls.Add(pageCanvas);
        mainShell.Controls.Add(statusPanel);
        mainShell.Controls.Add(headerPanel);

        Controls.Add(mainShell);
        Controls.Add(_sidebarPanel);

        ApplySidebarState();

        ResumeLayout();
    }

    private Control CreateBrandPanel()
    {
        var brandPanel = new Panel
        {
            Dock = DockStyle.Top,
            Height = 82,
            Padding = new Padding(2, 8, 2, 8)
        };

        var iconBadge = new RoundedSurfacePanel
        {
            Width = 28,
            Height = 28,
            BackColor = Color.FromArgb(84, 97, 245),
            BorderColor = Color.FromArgb(84, 97, 245),
            BorderThickness = 0,
            CornerRadius = 8,
            DrawShadow = false,
            Margin = new Padding(0, 2, 8, 0)
        };
        iconBadge.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "?",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Color.White,
            Font = DesktopTheme.EmphasisFont(12f)
        });

        var title = new Label
        {
            AutoSize = true,
            Text = "МойБизнес",
            Margin = new Padding(0, 4, 0, 0),
            Font = DesktopTheme.TitleFont(17f),
            ForeColor = Color.FromArgb(28, 38, 63)
        };

        var row = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        row.Controls.Add(iconBadge);
        row.Controls.Add(title);
        brandPanel.Controls.Add(row);

        return brandPanel;
    }

    private Control CreateSidebarGroupLabel(string text)
    {
        var label = new Label
        {
            AutoSize = false,
            Width = 220,
            Height = 24,
            Text = text.ToUpperInvariant(),
            TextAlign = ContentAlignment.BottomLeft,
            Margin = new Padding(0, 8, 0, 8),
            Font = DesktopTheme.EmphasisFont(8.4f),
            ForeColor = DesktopTheme.TextMuted
        };

        _sidebarGroupLabels.Add(label);
        return label;
    }

    private Control CreateSidebarStatusCard()
    {
        var card = DesktopSurfaceFactory.CreateSidebarCard();
        card.Height = 124;
        var mysqlReady = IsMySqlReady();

        var chipFlow = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            Height = 36,
            Margin = new Padding(0),
            WrapContents = true
        };
        chipFlow.Controls.Add(DesktopSurfaceFactory.CreateInfoChip("Desktop", DesktopTheme.PrimarySoft, DesktopTheme.SidebarButtonActiveText));
        chipFlow.Controls.Add(DesktopSurfaceFactory.CreateInfoChip(mysqlReady ? "MySQL ready" : "MySQL off", mysqlReady ? DesktopTheme.SurfaceMuted : DesktopTheme.InfoSoft, mysqlReady ? DesktopTheme.TextSecondary : DesktopTheme.Info));

        card.Controls.Add(chipFlow);
        card.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 18,
            Text = "Оператор: " + _workspace.CurrentOperator,
            Font = DesktopTheme.BodyFont(8.9f),
            ForeColor = DesktopTheme.TextSecondary
        });
        card.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 18,
            Text = "Режим: desktop-only",
            Font = DesktopTheme.BodyFont(8.9f),
            ForeColor = DesktopTheme.TextSecondary
        });
        card.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            Text = "Операционный контур",
            Font = DesktopTheme.TitleFont(11f),
            ForeColor = DesktopTheme.TextPrimary
        });

        return card;
    }

    private Control CreateHeaderTitlePanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill
        };

        _pageMetaFlow.Dock = DockStyle.Top;
        _pageMetaFlow.Height = 30;
        _pageMetaFlow.WrapContents = false;
        _pageMetaFlow.Margin = new Padding(0);
        _pageMetaFlow.Controls.Clear();
        _pageMetaFlow.Controls.Add(_sectionChip);
        _pageMetaFlow.Controls.Add(_platformChip);
        _pageMetaFlow.Controls.Add(_dataSourceChip);

        _pageSubtitleLabel.Dock = DockStyle.Top;
        _pageSubtitleLabel.Height = 24;
        _pageSubtitleLabel.Font = DesktopTheme.BodyFont(10f);
        _pageSubtitleLabel.ForeColor = DesktopTheme.TextSecondary;

        _pageTitleLabel.Dock = DockStyle.Top;
        _pageTitleLabel.Height = 38;
        _pageTitleLabel.Font = DesktopTheme.TitleFont(22f);
        _pageTitleLabel.ForeColor = DesktopTheme.TextPrimary;

        panel.Controls.Add(_pageMetaFlow);
        panel.Controls.Add(_pageSubtitleLabel);
        panel.Controls.Add(_pageTitleLabel);
        return panel;
    }

    private Control CreateHeaderRightPanel()
    {
        var panel = new Panel
        {
            Dock = DockStyle.Right,
            Width = 620,
            Padding = new Padding(0, 8, 0, 0)
        };

        _globalSearchTextBox.Dock = DockStyle.Fill;
        _globalSearchTextBox.Margin = new Padding(0);
        _globalSearchTextBox.BorderStyle = BorderStyle.None;
        _globalSearchTextBox.BackColor = DesktopTheme.Surface;
        _globalSearchTextBox.ForeColor = DesktopTheme.TextPrimary;
        _globalSearchTextBox.Font = DesktopTheme.BodyFont(9.5f);
        _globalSearchTextBox.PlaceholderText = "Глобальный поиск по документам, товарам и операциям";
        _globalSearchTextBox.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Enter)
            {
                eventArgs.SuppressKeyPress = true;
                RunGlobalSearch();
            }
        };

        var searchHost = new RoundedSurfacePanel
        {
            Dock = DockStyle.Fill,
            BackColor = DesktopTheme.Surface,
            BorderColor = DesktopTheme.BorderStrong,
            BorderThickness = 1,
            CornerRadius = 10,
            DrawShadow = false,
            Padding = new Padding(10, 8, 10, 0)
        };

        var searchLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };
        searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 22));
        searchLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        searchLayout.Controls.Add(new Label
        {
            Text = "⌕",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = DesktopTheme.EmphasisFont(10f),
            ForeColor = DesktopTheme.TextMuted
        }, 0, 0);
        searchLayout.Controls.Add(_globalSearchTextBox, 1, 0);
        if (searchLayout.Controls[0] is Label searchIconLabel)
        {
            searchIconLabel.Text = "\u2315";
        }
        searchHost.Controls.Add(searchLayout);

        _headerSyncButton.Dock = DockStyle.Fill;
        _headerSyncButton.Text = "Update";
        _headerSyncButton.FlatStyle = FlatStyle.Flat;
        _headerSyncButton.Font = DesktopTheme.EmphasisFont(8.8f);
        _headerSyncButton.Cursor = Cursors.Hand;
        _headerSyncButton.MinimumSize = new Size(84, 0);
        _headerSyncButton.UseMnemonic = false;
        _headerSyncButton.UseVisualStyleBackColor = false;
        _headerSyncButton.AutoEllipsis = true;
        DesktopSurfaceFactory.ApplyTone(_headerSyncButton, DesktopButtonTone.Secondary);
        _headerSyncButton.Click += async (_, _) => await SyncFromOneCAsync();

        _headerActionsButton.Dock = DockStyle.Fill;
        _headerActionsButton.Text = "Menu";
        _headerActionsButton.FlatStyle = FlatStyle.Flat;
        _headerActionsButton.Font = DesktopTheme.EmphasisFont(9f);
        _headerActionsButton.Cursor = Cursors.Hand;
        _headerActionsButton.MinimumSize = new Size(64, 0);
        _headerActionsButton.UseMnemonic = false;
        _headerActionsButton.UseVisualStyleBackColor = false;
        _headerActionsButton.AutoEllipsis = true;
        DesktopSurfaceFactory.ApplyTone(_headerActionsButton, DesktopButtonTone.Secondary);

        _headerActionsMenu.ShowImageMargin = false;
        _headerActionsMenu.Font = DesktopTheme.BodyFont(9.2f);
        _headerActionsMenu.Items.Clear();
        _exportModuleMenuItem.Text = "Выгрузить текущий раздел";
        _syncFromOneCMenuItem.Text = "Синхронизировать из 1С";
        _exportModuleMenuItem.Click -= HandleExportModuleMenuClick;
        _exportModuleMenuItem.Click += HandleExportModuleMenuClick;
        _syncFromOneCMenuItem.Click -= HandleSyncFromOneCMenuClick;
        _syncFromOneCMenuItem.Click += HandleSyncFromOneCMenuClick;
        _headerActionsMenu.Items.Add(_exportModuleMenuItem);
        _headerActionsMenu.Items.Add(new ToolStripSeparator());
        _headerActionsMenu.Items.Add(_syncFromOneCMenuItem);
        _headerActionsButton.Click += (_, _) => _headerActionsMenu.Show(_headerActionsButton, new Point(0, _headerActionsButton.Height));

        var actions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Margin = new Padding(0)
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72));
        actions.Controls.Add(searchHost, 0, 0);
        actions.Controls.Add(_headerSyncButton, 1, 0);
        actions.Controls.Add(_headerActionsButton, 2, 0);

        _headerToolTip.SetToolTip(_globalSearchTextBox, "Глобальный поиск (Ctrl+F, Enter).");
        _headerToolTip.SetToolTip(_headerActionsButton, "Действия: выгрузка и синхронизация.");
        _headerToolTip.SetToolTip(_headerSyncButton, "Синхронизировать данные из 1С.");
        _headerToolTip.SetToolTip(_headerActionsButton, "Меню действий.");
        _headerToolTip.SetToolTip(_globalSearchTextBox, "Глобальный поиск (Ctrl+F, Enter).");
        _headerToolTip.SetToolTip(_headerSyncButton, "Синхронизировать данные из 1С.");
        _headerToolTip.SetToolTip(_headerActionsButton, "Меню действий.");
        panel.Controls.Add(actions);
        UpdateHeaderButtonsState();
        return panel;
    }

    private void HandleExportModuleMenuClick(object? sender, EventArgs e)
    {
        ExportCurrentModule();
    }

    private async void HandleSyncFromOneCMenuClick(object? sender, EventArgs e)
    {
        await SyncFromOneCAsync();
    }

    private Control CreateStatusPanel()
    {
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;
        _statusLabel.Font = DesktopTheme.BodyFont(9f);
        _statusLabel.ForeColor = DesktopTheme.TextSecondary;
        _statusLabel.Text = "Запуск рабочего окна...";

        var panel = new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 34,
            Padding = new Padding(22, 0, 22, 0),
            BackColor = DesktopTheme.StatusBackground
        };
        panel.Controls.Add(_statusLabel);
        return panel;
    }

    private Button CreateNavButton(AppSectionDefinition section)
    {
        var button = new Button
        {
            Width = 220,
            Height = 44,
            Text = section.Caption,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(14, 0, 12, 0),
            Margin = new Padding(0, 0, 0, 6),
            FlatStyle = FlatStyle.Flat,
            BackColor = DesktopTheme.SidebarButton,
            ForeColor = DesktopTheme.SidebarButtonText,
            Font = DesktopTheme.BodyFont(10f),
            Cursor = Cursors.Hand
        };

        button.FlatAppearance.BorderColor = DesktopTheme.SidebarBackground;
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseDownBackColor = Color.Empty;
        button.FlatAppearance.MouseOverBackColor = Color.Empty;
        button.MouseEnter += (_, _) =>
        {
            if (_currentSection != section.Key)
            {
                button.BackColor = Color.FromArgb(245, 247, 255);
            }
        };
        button.MouseLeave += (_, _) =>
        {
            if (_currentSection != section.Key)
            {
                button.BackColor = DesktopTheme.SidebarButton;
            }
        };
        button.Click += (_, _) => ActivateSection(section.Key);
        return button;
    }

    private void SeedPages()
    {
        _pages[AppSection.Dashboard] = CreateDashboardPage();
    }

    private void ActivateSection(AppSection section)
    {
        _currentSection = section;

        foreach (var pair in _navButtons)
        {
            ApplyNavButtonState(pair.Value, pair.Key == section);
        }

        var page = GetOrCreatePage(section);
        _pageHost.SuspendLayout();
        _pageHost.Controls.Clear();
        _pageHost.Controls.Add(page);
        _pageHost.ResumeLayout(true);
        TextMojibakeFixer.NormalizeControlTree(page);

        var sectionInfo = AppSectionCatalog.All.First(item => item.Key == section);
        if (_workspaceTabPage is not null)
        {
            _workspaceTabPage.Text = TextMojibakeFixer.NormalizeText(sectionInfo.Caption);
            _documentTabs.SelectedTab = _workspaceTabPage;
        }

        _pageTitleLabel.Text = sectionInfo.Caption;
        _pageSubtitleLabel.Text = sectionInfo.Description;
        UpdateMetaChips(sectionInfo);
        UpdateHeaderButtonsState();
        ApplySectionChrome(section);
    }

    private void ApplySectionChrome(AppSection section)
    {
        var compactModeEnabled = true;
        if (_headerPanel is not null)
        {
            _headerPanel.Visible = !compactModeEnabled;
        }

        if (_statusPanel is not null)
        {
            _statusPanel.Visible = !compactModeEnabled;
        }
    }

    internal DialogResult ShowDialogInDocumentTab(Form dialog)
    {
        if (InvokeRequired)
        {
            return (DialogResult)Invoke(new Func<DialogResult>(() => ShowDialogInDocumentTab(dialog)));
        }

        if (dialog.IsDisposed)
        {
            return DialogResult.Cancel;
        }

        var tabPage = new TabPage(BuildDialogTabTitle(dialog))
        {
            BackColor = DesktopTheme.AppBackground,
            Padding = new Padding(0)
        };

        dialog.TopLevel = false;
        dialog.FormBorderStyle = FormBorderStyle.None;
        dialog.ControlBox = false;
        dialog.ShowInTaskbar = false;
        dialog.Dock = DockStyle.Fill;

        var closed = false;
        var result = DialogResult.Cancel;

        void HandleTextChanged(object? sender, EventArgs eventArgs)
        {
            if (!tabPage.IsDisposed)
            {
                tabPage.Text = BuildDialogTabTitle(dialog);
            }
        }

        void HandleFormClosed(object? sender, FormClosedEventArgs eventArgs)
        {
            dialog.TextChanged -= HandleTextChanged;
            dialog.FormClosed -= HandleFormClosed;
            result = dialog.DialogResult == DialogResult.None ? DialogResult.Cancel : dialog.DialogResult;
            closed = true;
            RemoveDialogTab(dialog, tabPage);
        }

        dialog.TextChanged += HandleTextChanged;
        dialog.FormClosed += HandleFormClosed;

        tabPage.Controls.Add(dialog);
        _dialogTabsByForm[dialog] = tabPage;
        _dialogFormsByTab[tabPage] = dialog;
        _documentTabs.TabPages.Add(tabPage);
        _documentTabs.SelectedTab = tabPage;
        TextMojibakeFixer.NormalizeControlTree(dialog);
        dialog.Show();

        while (!closed && !IsDisposed)
        {
            System.Windows.Forms.Application.DoEvents();
        }

        return result;
    }

    private static string BuildDialogTabTitle(Form dialog)
    {
        var title = dialog.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            title = dialog.Name;
        }

        return string.IsNullOrWhiteSpace(title) ? "Document" : TextMojibakeFixer.NormalizeText(title);
    }

    private void RemoveDialogTab(Form dialog, TabPage tabPage)
    {
        _dialogTabsByForm.Remove(dialog);
        _dialogFormsByTab.Remove(tabPage);

        if (!IsDisposed && _documentTabs.TabPages.Contains(tabPage))
        {
            _documentTabs.TabPages.Remove(tabPage);
        }

        if (_workspaceTabPage is not null
            && !IsDisposed
            && _documentTabs.TabPages.Contains(_workspaceTabPage))
        {
            _documentTabs.SelectedTab = _workspaceTabPage;
        }

        if (!tabPage.IsDisposed)
        {
            tabPage.Dispose();
        }
    }

    private bool TryCloseActiveDialogTab()
    {
        var selectedTab = _documentTabs.SelectedTab;
        if (selectedTab is null || ReferenceEquals(selectedTab, _workspaceTabPage))
        {
            return false;
        }

        if (_dialogFormsByTab.TryGetValue(selectedTab, out var dialog) && !dialog.IsDisposed)
        {
            if (dialog.DialogResult == DialogResult.None)
            {
                dialog.DialogResult = DialogResult.Cancel;
            }

            dialog.Close();
            return true;
        }

        _dialogFormsByTab.Remove(selectedTab);
        if (_documentTabs.TabPages.Contains(selectedTab))
        {
            _documentTabs.TabPages.Remove(selectedTab);
        }

        if (!selectedTab.IsDisposed)
        {
            selectedTab.Dispose();
        }

        return true;
    }

    private void ApplyNavButtonState(Button button, bool isActive)
    {
        button.BackColor = isActive ? Color.FromArgb(238, 241, 255) : DesktopTheme.SidebarButton;
        button.ForeColor = isActive ? Color.FromArgb(79, 99, 246) : Color.FromArgb(47, 57, 84);
        button.FlatAppearance.BorderColor = DesktopTheme.SidebarBackground;
        button.FlatAppearance.BorderSize = 0;
        button.Font = _sidebarCollapsed
            ? (isActive ? _sidebarIconFontActive : _sidebarIconFont)
            : (isActive ? DesktopTheme.EmphasisFont(10f) : DesktopTheme.BodyFont(10f));
    }

    private static string ResolveSectionCompactLabel(AppSection section)
    {
        return section switch
        {
            AppSection.Dashboard => "\uE80F",
            AppSection.Sales => "\uE719",
            AppSection.Purchasing => "\uE8D2",
            AppSection.Warehouse => "\uE8B7",
            AppSection.Catalog => "\uE8EC",
            AppSection.Audit => "\uE9D5",
            AppSection.Coverage => "\uE73E",
            AppSection.Model => "\uE71B",
            _ => "\uE10A"
        };
    }

    private void ApplySidebarState()
    {
        _sidebarPanel.Width = _sidebarCollapsed ? SidebarCollapsedWidth : SidebarExpandedWidth;
        _sidebarPanel.Padding = _sidebarCollapsed ? new Padding(10, 12, 10, 12) : new Padding(16, 12, 16, 18);
        _navFlow.Padding = _sidebarCollapsed ? new Padding(0, 10, 0, 0) : new Padding(0, 16, 0, 0);

        _sidebarToggleButton.Text = _sidebarCollapsed ? "\u25B6" : "\u25C0";
        _sidebarToggleButton.TextAlign = _sidebarCollapsed ? ContentAlignment.MiddleCenter : ContentAlignment.MiddleRight;
        _sidebarToggleButton.Padding = _sidebarCollapsed ? new Padding(0) : new Padding(0, 0, 2, 0);
        _headerToolTip.SetToolTip(_sidebarToggleButton, _sidebarCollapsed ? "Развернуть меню" : "Свернуть меню");

        _sidebarFooterLabel.Visible = !_sidebarCollapsed;
        if (_sidebarStatusCard is not null)
        {
            _sidebarStatusCard.Visible = !_sidebarCollapsed;
        }

        if (_brandPanel is not null)
        {
            _brandPanel.Visible = !_sidebarCollapsed;
        }

        foreach (var groupLabel in _sidebarGroupLabels)
        {
            groupLabel.Visible = !_sidebarCollapsed;
        }

        foreach (var pair in _navButtons)
        {
            var sectionInfo = AppSectionCatalog.All.First(item => item.Key == pair.Key);
            var button = pair.Value;
            button.Width = _sidebarCollapsed ? 56 : 220;
            button.Height = _sidebarCollapsed ? 48 : 44;
            button.Margin = _sidebarCollapsed ? new Padding(0, 0, 0, 6) : new Padding(0, 0, 0, 6);
            button.TextAlign = _sidebarCollapsed ? ContentAlignment.MiddleCenter : ContentAlignment.MiddleLeft;
            button.Padding = _sidebarCollapsed ? new Padding(0) : new Padding(14, 0, 12, 0);
            button.Text = _sidebarCollapsed ? ResolveSectionCompactLabel(pair.Key) : sectionInfo.Caption;
            _headerToolTip.SetToolTip(button, sectionInfo.Caption);
        }

        foreach (var pair in _navButtons)
        {
            ApplyNavButtonState(pair.Value, pair.Key == _currentSection);
        }
    }

    private Control GetOrCreatePage(AppSection section)
    {
        if (_pages.TryGetValue(section, out var page))
        {
            return page;
        }

        page = CreatePage(section);
        _pages[section] = page;
        return page;
    }

    private Control CreatePage(AppSection section)
    {
        return section switch
        {
            AppSection.Dashboard => CreateDashboardPage(),
            AppSection.Sales => CreateDeferredPage(
                "Продажи",
                "Готовлю реестр покупателей, заказов, счетов и отгрузок без блокировки главного окна.",
                async () =>
                {
                    await Task.Yield();
                    return new SalesWorkspaceControl(_workspace, _salesWorkspace);
                }),
            AppSection.Purchasing => CreateDeferredPage(
                "Закупки",
                "Загружаю поставщиков, заказы, счета и приемку в фоне.",
                async () =>
                {
                    var currentOperator = ResolveCurrentOperator();
                    var loaded = await Task.Run(() =>
                    {
                        var store = PurchasingOperationalWorkspaceStore.CreateDefault();
                        var workspace = store.LoadOrCreate(currentOperator, _salesWorkspace);
                        return (store, workspace);
                    });
                    return new PurchasingWorkspaceControl(_salesWorkspace, loaded.store, loaded.workspace);
                }),
            AppSection.Warehouse => CreateDeferredPage(
                "Склад",
                "Подготавливаю остатки, перемещения, резервы и инвентаризации в фоне.",
                async () =>
                {
                    var currentOperator = ResolveCurrentOperator();
                    var loaded = await Task.Run(() =>
                    {
                        var store = WarehouseOperationalWorkspaceStore.CreateDefault();
                        var workspace = store.LoadOrCreate(currentOperator, _salesWorkspace);
                        var runtimeView = WarehouseWorkspace.Create(_salesWorkspace);
                        return (store, workspace, runtimeView);
                    });
                    return new WarehouseWorkspaceControl(_salesWorkspace, loaded.store, loaded.workspace, loaded.runtimeView);
                }),
            AppSection.Audit => CreateDeferredPage(
                "Единый аудит",
                "Собираю общий журнал действий из backplane без подвисания интерфейса.",
                async () =>
                {
                    var snapshot = await Task.Run(() => AuditWorkspaceSnapshot.Create(_salesWorkspace));
                    return new AuditWorkspaceControl(_salesWorkspace, snapshot);
                }),
            AppSection.Catalog => CreateDeferredPage(
                "Номенклатура",
                "Поднимаю карточки товаров, цены и скидки асинхронно, чтобы не стопорить навигацию.",
                async () =>
                {
                    var currentOperator = ResolveCurrentOperator();
                    var loaded = await Task.Run(() =>
                    {
                        var store = CatalogWorkspaceStore.CreateDefault();
                        var workspace = store.LoadOrCreate(currentOperator, _salesWorkspace);
                        return (store, workspace);
                    });
                    return new CatalogWorkspaceControl(_salesWorkspace, loaded.store, loaded.workspace);
                }),
            AppSection.Coverage => new FunctionalCoverageControl(_coverage),
            AppSection.Model => CreateDeferredPage(
                "Связи данных",
                "Строю карту сущностей и связей отдельно от основного окна.",
                async () =>
                {
                    await Task.Yield();
                    return new ModelWorkspaceControl();
                }),
            _ => CreateDashboardPage()
        };
    }

    private Control CreateDeferredPage(string title, string description, Func<Task<Control>> factory)
    {
        return new DeferredPageHostControl(title, description, factory);
    }

    private string ResolveCurrentOperator()
    {
        return string.IsNullOrWhiteSpace(_salesWorkspace.CurrentOperator)
            ? _workspace.CurrentOperator
            : _salesWorkspace.CurrentOperator;
    }

    private Control CreateDashboardPage()
    {
        var dashboardPage = new DashboardControl(_workspace);
        dashboardPage.NavigationRequested += (_, targetKey) =>
        {
            if (AppSectionCatalog.TryParse(targetKey, out var section))
            {
                ActivateSection(section);
            }
        };

        return dashboardPage;
    }

    private void ConfigureClock()
    {
        UpdateClock();
        _clockTimer.Interval = 1000;
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
    }

    private void BeginInitialLoad()
    {
        if (_initialLoadStarted)
        {
            return;
        }

        _initialLoadStarted = true;
        _ = LoadInitialWorkspaceAsync();
    }

    private async Task LoadInitialWorkspaceAsync()
    {
        try
        {
            SetStatus("Загружаю продажи, склад и связи из локальной базы...");
            var loadedWorkspace = await Task.Run(() => _salesWorkspaceStore.LoadOrCreate(_workspace.CurrentOperator));
            if (IsDisposed)
            {
                return;
            }

            _salesWorkspace.ReplaceFrom(loadedWorkspace);
            InvalidateDataPages();
            _workspacePersistenceEnabled = true;
            SetStatus("Данные загружены. Приложение работает в desktop-режиме.");
            SetStatus("Данные загружены. Горячие клавиши: Ctrl+1..5 разделы, Ctrl+F поиск, F5 обновить раздел.");
        }
        catch (Exception exception)
        {
            _workspacePersistenceEnabled = true;
            SetStatus("Открыт безопасный режим. Часть данных не загрузилась.");
            MessageBox.Show(
                this,
                $"Не удалось полностью загрузить данные. Приложение открыто в безопасном режиме.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Мажор Flow",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void SetStatus(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(message));
            return;
        }

        _statusLabel.Text = message;
    }

    private void UpdateClock()
    {
        _dateLabel.Text = DateTime.Now.ToString("dd.MM.yyyy HH:mm");
    }

    private void RunGlobalSearch()
    {
        var query = _globalSearchTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            MessageBox.Show(
                this,
                "Введите текст для поиска по документам, товарам или журналу.",
                "Мажор Flow",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (!IsMySqlReady())
        {
            MessageBox.Show(
                this,
                "Глобальный поиск сейчас недоступен: MySQL backplane не инициализирован.",
                "Мажор Flow",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (_backplane is null)
        {
            return;
        }

        var results = _backplane.TrySearch(query);
        if (results.Count == 0)
        {
            SetStatus($"По запросу \"{query}\" ничего не найдено.");
            MessageBox.Show(
                this,
                $"По запросу \"{query}\" ничего не найдено в snapshot и журнале.",
                "Мажор Flow",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        using var dialog = new GlobalSearchResultsForm(query, results);
        if (DialogTabsHost.ShowDialog(dialog, this) == DialogResult.OK
            && !string.IsNullOrWhiteSpace(dialog.SelectedModuleCode)
            && AppSectionCatalog.TryParse(dialog.SelectedModuleCode, out var section))
        {
            ActivateSection(section);
            SetStatus($"Глобальный поиск: найдено {results.Count:N0}, открыт модуль {AppSectionCatalog.All.First(item => item.Key == section).Caption}.");
        }
        else
        {
            SetStatus($"Глобальный поиск: найдено {results.Count:N0} результатов.");
        }
    }

    private void ExportCurrentModule()
    {
        if (!TryMapCurrentSectionToModuleCode(out var moduleCode))
        {
            MessageBox.Show(
                this,
                "Для текущего раздела экспорт snapshot не предусмотрен. Доступны Продажи, Закупки, Склад и Номенклатура.",
                "Мажор Flow",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        if (!IsMySqlReady())
        {
            MessageBox.Show(
                this,
                "Экспорт недоступен: MySQL backplane не инициализирован.",
                "Мажор Flow",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (_backplane is null)
        {
            return;
        }

        var export = _backplane.TryExportModuleSnapshot(moduleCode, _workspace.CurrentOperator);
        if (export is null)
        {
            MessageBox.Show(
                this,
                $"Не удалось выгрузить snapshot модуля `{moduleCode}`. Сначала сохраните изменения в модуле.",
                "Мажор Flow",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        SetStatus($"Экспортирован snapshot `{moduleCode}` v{export.VersionNo} в {export.StoragePath}.");
        MessageBox.Show(
            this,
            $"Snapshot модуля `{moduleCode}` сохранен.{Environment.NewLine}{Environment.NewLine}{export.StoragePath}",
            "Мажор Flow",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private bool TryMapCurrentSectionToModuleCode(out string moduleCode)
    {
        moduleCode = _currentSection switch
        {
            AppSection.Sales => "sales",
            AppSection.Purchasing => "purchasing",
            AppSection.Warehouse => "warehouse",
            AppSection.Catalog => "catalog",
            _ => string.Empty
        };

        return !string.IsNullOrWhiteSpace(moduleCode);
    }

    private async Task SyncFromOneCAsync()
    {
        if (_oneCSyncInProgress)
        {
            return;
        }

        try
        {
            _oneCSyncInProgress = true;
            UpdateHeaderButtonsState();
            SetStatus("Запрашиваю свежие данные из 1С и обновляю MySQL-контур. Это может занять несколько минут.");

            var result = await _liveSyncService.SyncAsync(_workspace.CurrentOperator);
            if (IsDisposed)
            {
                return;
            }

            _salesWorkspace.ReplaceFrom(result.Workspace);
            InvalidateDataPages();
            _workspacePersistenceEnabled = true;
            SetStatus(BuildSyncCompletedStatus(result));
        }
        catch (Exception exception)
        {
            if (IsDisposed)
            {
                return;
            }

            SetStatus("Не удалось обновить данные из 1С.");
            MessageBox.Show(
                this,
                $"Не удалось получить живые данные из 1С.{Environment.NewLine}{Environment.NewLine}{exception.Message}",
                "Мажор Flow",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            _oneCSyncInProgress = false;
            if (!IsDisposed)
            {
                UpdateHeaderButtonsState();
            }
        }
    }

    private void UpdateHeaderButtonsState()
    {
        var mysqlReady = IsMySqlReady();
        var canExport = mysqlReady && !_oneCSyncInProgress && TryMapCurrentSectionToModuleCode(out _);
        _headerSyncButton.Text = _oneCSyncInProgress ? "Обновление..." : "Обновить";
        _headerSyncButton.Enabled = !_oneCSyncInProgress;
        _headerActionsButton.Text = "Меню";
        _headerToolTip.SetToolTip(
            _headerSyncButton,
            _oneCSyncInProgress
                ? "Синхронизация выполняется."
                : "Действия: выгрузка и синхронизация.");
        _headerToolTip.SetToolTip(_headerSyncButton, _oneCSyncInProgress ? "Синхронизация выполняется." : "Синхронизировать данные из 1С.");
        _exportModuleMenuItem.Enabled = canExport;
        _syncFromOneCMenuItem.Enabled = !_oneCSyncInProgress;
        _syncFromOneCMenuItem.Text = _oneCSyncInProgress ? "Синхронизация выполняется..." : "Синхронизировать из 1С";
        _platformChip.Text = _oneCSyncInProgress ? "Обновление..." : "Desktop";
        _platformChip.BackColor = _oneCSyncInProgress ? DesktopTheme.InfoSoft : DesktopTheme.SurfaceMuted;
        _platformChip.ForeColor = _oneCSyncInProgress ? DesktopTheme.Info : DesktopTheme.TextSecondary;
        _dataSourceChip.Text = mysqlReady ? "MySQL online" : "MySQL offline";
        _dataSourceChip.BackColor = mysqlReady ? DesktopTheme.PrimarySoft : Color.FromArgb(250, 238, 235);
        _dataSourceChip.ForeColor = mysqlReady ? DesktopTheme.SidebarButtonActiveText : DesktopTheme.Danger;
        _headerSyncButton.Text = _oneCSyncInProgress ? "Updating..." : "Update";
        _headerActionsButton.Text = "Menu";
        _headerToolTip.SetToolTip(_headerSyncButton, _oneCSyncInProgress ? "Synchronization is running." : "Sync data from 1C.");
        _headerToolTip.SetToolTip(_headerActionsButton, "Actions menu.");
    }

    private bool IsMySqlReady()
    {
        return _backplane is not null && _backplane.IsConnectionHealthy;
    }

    private void UpdateMetaChips(AppSectionDefinition sectionInfo)
    {
        _sectionChip.Text = sectionInfo.Caption;
    }

    private static string BuildSyncCompletedStatus(OneCLiveSyncDesktopResult result)
    {
        var seconds = Math.Max(1, Math.Round(result.ExportResult.Duration.TotalSeconds));
        var workspace = result.Workspace;
        if (result.MySqlRefresh.Succeeded)
        {
            var raw = result.MySqlRefresh.RawSyncResult;
            var projection = result.MySqlRefresh.ProjectionResult;
            return
                $"1С -> MySQL завершено за {seconds:N0} сек. " +
                $"Raw-слой: объектов {raw?.ObjectCount ?? 0:N0}, полей {raw?.FieldCount ?? 0:N0}, строк {raw?.TabularRowCount ?? 0:N0}. " +
                $"Операционный контур: контрагентов {projection?.PartnerCount ?? workspace.Customers.Count:N0}, номенклатуры {projection?.ItemCount ?? workspace.CatalogItems.Count:N0}, счетов {projection?.SalesInvoiceCount ?? workspace.Invoices.Count:N0}, заказов поставщику {projection?.PurchaseOrderCount ?? 0:N0}, приемок {projection?.PurchaseReceiptCount ?? 0:N0}.";
        }

        return
            $"Снимок 1С загружен за {seconds:N0} сек, но обновление MySQL не завершилось. " +
            $"Включен fallback-импорт. Продажи: покупателей {workspace.Customers.Count:N0}, заказов {workspace.Orders.Count:N0}, счетов {workspace.Invoices.Count:N0}, отгрузок {workspace.Shipments.Count:N0}.";
    }

    private void InvalidateDataPages()
    {
        var sections = new[] { AppSection.Sales, AppSection.Purchasing, AppSection.Warehouse, AppSection.Audit, AppSection.Catalog };
        foreach (var section in sections)
        {
            if (_pages.Remove(section, out var control))
            {
                if (_pageHost.Controls.Contains(control))
                {
                    _pageHost.Controls.Remove(control);
                }

                control.Dispose();
            }
        }

        if (_currentSection is AppSection.Sales or AppSection.Purchasing or AppSection.Warehouse or AppSection.Audit or AppSection.Catalog)
        {
            ActivateSection(_currentSection);
        }
    }

    private enum AppSection
    {
        Dashboard,
        Sales,
        Purchasing,
        Warehouse,
        Audit,
        Catalog,
        Coverage,
        Model
    }

    private sealed record AppSectionDefinition(AppSection Key, string Caption, string Description, string Icon = "");

    private static class AppSectionCatalog
    {
        public static IReadOnlyList<AppSectionDefinition> All { get; } =
        [
            new(AppSection.Dashboard, "Главная", "Ежедневная работа, сигналы и очередь команды."),
            new(AppSection.Sales, "Продажи", "Покупатели, заказы, счета и отгрузки."),
            new(AppSection.Purchasing, "Закупки", "Поставщики, закупки, приемка и счета."),
            new(AppSection.Warehouse, "Склад", "Остатки, резервы, перемещения и инвентаризация."),
            new(AppSection.Audit, "Единый аудит", "Общий журнал действий по продажам, закупкам и складу."),
            new(AppSection.Catalog, "Номенклатура", "Товары, цены и скидки."),
            new(AppSection.Coverage, "Контур 1С", "Основной функционал 1С, который переносится в desktop."),
            new(AppSection.Model, "Связи данных", "Служебная карта сущностей и связей.")
        ];

        public static bool TryParse(string key, out AppSection section)
        {
            section = key switch
            {
                "sales" => AppSection.Sales,
                "purchasing" => AppSection.Purchasing,
                "warehouse" => AppSection.Warehouse,
                "audit" => AppSection.Audit,
                "catalog" => AppSection.Catalog,
                "coverage" => AppSection.Coverage,
                "model" => AppSection.Model,
                _ => AppSection.Dashboard
            };

            return key is "sales" or "purchasing" or "warehouse" or "audit" or "catalog" or "coverage" or "model" or "dashboard";
        }
    }
}
