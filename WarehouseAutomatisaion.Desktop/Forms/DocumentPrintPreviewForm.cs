using System.Diagnostics;

namespace WarehouseAutomatisaion.Desktop.Forms;

public sealed class DocumentPrintPreviewForm : Form
{
    private readonly string _html;
    private readonly WebBrowser _browser = new();

    public DocumentPrintPreviewForm(string title, string html)
    {
        _html = html;

        AutoScaleMode = AutoScaleMode.Font;
        BackColor = Color.FromArgb(247, 244, 238);
        ClientSize = new Size(1160, 860);
        MinimumSize = new Size(980, 720);
        StartPosition = FormStartPosition.CenterParent;
        Text = title;

        BuildLayout();
        Load += (_, _) => _browser.DocumentText = _html;
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(CreateToolbar(), 0, 0);
        root.Controls.Add(CreateBrowserHost(), 0, 1);
        Controls.Add(root);
    }

    private Control CreateToolbar()
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0, 0, 0, 10)
        };
        panel.Controls.Add(CreateActionButton("Предпросмотр печати", (_, _) => _browser.ShowPrintPreviewDialog()));
        panel.Controls.Add(CreateActionButton("Печать", (_, _) => _browser.ShowPrintDialog()));
        panel.Controls.Add(CreateActionButton("Сохранить HTML", (_, _) => SaveHtml()));
        panel.Controls.Add(CreateActionButton("Открыть в браузере", (_, _) => OpenInBrowser()));
        panel.Controls.Add(CreateActionButton("Закрыть", (_, _) => Close()));
        return panel;
    }

    private Control CreateBrowserHost()
    {
        _browser.Dock = DockStyle.Fill;
        _browser.AllowWebBrowserDrop = false;
        _browser.IsWebBrowserContextMenuEnabled = true;
        _browser.ScriptErrorsSuppressed = true;
        _browser.WebBrowserShortcutsEnabled = true;

        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle,
            Padding = new Padding(8)
        };
        panel.Controls.Add(_browser);
        return panel;
    }

    private static Button CreateActionButton(string text, EventHandler handler)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(255, 249, 240),
            ForeColor = Color.FromArgb(63, 55, 46),
            Font = new Font("Segoe UI Semibold", 9.5f),
            Padding = new Padding(14, 8, 14, 8),
            Margin = new Padding(10, 0, 0, 0),
            Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(216, 205, 186);
        button.Click += handler;
        return button;
    }

    private void SaveHtml()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "HTML (*.html)|*.html",
            AddExtension = true,
            FileName = BuildSafeFileName(Text)
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        File.WriteAllText(dialog.FileName, _html);
    }

    private void OpenInBrowser()
    {
        var root = Path.Combine(Path.GetTempPath(), "WarehouseAutomatisaion", "print-preview");
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, BuildSafeFileName(Text));
        File.WriteAllText(path, _html);
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private static string BuildSafeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray()).Trim();
        if (!cleaned.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            cleaned += ".html";
        }

        return cleaned;
    }
}
