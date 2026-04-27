namespace WarehouseAutomatisaion.Desktop.Controls;

public sealed class DeferredPageHostControl : UserControl
{
    private readonly string _title;
    private readonly string _description;
    private readonly Func<Task<Control>> _factory;
    private readonly Label _statusLabel = new();
    private readonly Button _retryButton = new();
    private bool _loadStarted;

    public DeferredPageHostControl(string title, string description, Func<Task<Control>> factory)
    {
        _title = title;
        _description = description;
        _factory = factory;

        Dock = DockStyle.Fill;
        BackColor = DesktopTheme.AppBackground;
        BuildPlaceholder();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        BeginLoadIfNeeded();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible)
        {
            BeginLoadIfNeeded();
        }
    }

    private void BuildPlaceholder()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(36)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            Height = 36,
            Font = DesktopTheme.TitleFont(18f),
            ForeColor = DesktopTheme.TextPrimary,
            Text = _title
        }, 0, 0);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            MaximumSize = new Size(960, 0),
            Font = DesktopTheme.BodyFont(10f),
            ForeColor = DesktopTheme.TextSecondary,
            Text = _description
        }, 0, 1);

        _statusLabel.Dock = DockStyle.Top;
        _statusLabel.AutoSize = true;
        _statusLabel.MaximumSize = new Size(960, 0);
        _statusLabel.Margin = new Padding(0, 18, 0, 0);
        _statusLabel.Font = DesktopTheme.BodyFont(10f);
        _statusLabel.ForeColor = DesktopTheme.TextSecondary;
        _statusLabel.Text = $"Загрузка раздела \"{_title}\"...";
        root.Controls.Add(_statusLabel, 0, 2);

        _retryButton.AutoSize = true;
        _retryButton.Visible = false;
        _retryButton.Margin = new Padding(0, 18, 0, 0);
        _retryButton.Text = "Повторить загрузку";
        _retryButton.FlatStyle = FlatStyle.Flat;
        _retryButton.Font = DesktopTheme.EmphasisFont(9.2f);
        _retryButton.Padding = new Padding(14, 8, 14, 8);
        _retryButton.Cursor = Cursors.Hand;
        DesktopSurfaceFactory.ApplyTone(_retryButton, DesktopButtonTone.Secondary);
        _retryButton.Click += (_, _) =>
        {
            _retryButton.Visible = false;
            BeginLoad(force: true);
        };
        root.Controls.Add(_retryButton, 0, 3);

        Controls.Add(root);
    }

    private void BeginLoadIfNeeded()
    {
        if (_loadStarted || !Visible)
        {
            return;
        }

        BeginLoad();
    }

    private void BeginLoad(bool force = false)
    {
        if ((_loadStarted && !force) || IsDisposed)
        {
            return;
        }

        _loadStarted = true;
        _statusLabel.ForeColor = DesktopTheme.TextSecondary;
        _statusLabel.Text = $"Загрузка раздела \"{_title}\"...";
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var content = await _factory();
            if (IsDisposed)
            {
                content.Dispose();
                return;
            }

            SuspendLayout();
            Controls.Clear();
            content.Dock = DockStyle.Fill;
            Controls.Add(content);
            ResumeLayout(true);
        }
        catch (Exception exception)
        {
            _loadStarted = false;
            _statusLabel.ForeColor = DesktopTheme.Danger;
            _statusLabel.Text = $"Не удалось загрузить раздел \"{_title}\". {exception.Message}";
            _retryButton.Visible = true;
        }
    }
}
