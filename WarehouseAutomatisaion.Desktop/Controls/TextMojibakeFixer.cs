using System.Collections;
using System.Runtime.CompilerServices;
using System.Text;

namespace WarehouseAutomatisaion.Desktop.Controls;

public static class TextMojibakeFixer
{
    private static readonly ConditionalWeakTable<Control, object> HookedControls = new();
    private static readonly ConditionalWeakTable<ToolStripItem, object> HookedToolStripItems = new();
    private static readonly ConditionalWeakTable<DataGridView, object> HookedDataGrids = new();
    private static readonly ConditionalWeakTable<ListControl, object> HookedListControls = new();
    private static readonly Encoding Utf8 = new UTF8Encoding(false, false);
    private static readonly Encoding Cp1251;

    static TextMojibakeFixer()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp1251 = Encoding.GetEncoding(1251);
    }

    public static void NormalizeControlTree(Control? root)
    {
        if (root is null || root.IsDisposed)
        {
            return;
        }

        NormalizeSingleControl(root);

        foreach (Control child in root.Controls)
        {
            NormalizeControlTree(child);
        }

        if (root.ContextMenuStrip is not null)
        {
            NormalizeToolStripItems(root.ContextMenuStrip.Items);
        }

        if (root is MenuStrip menuStrip)
        {
            NormalizeToolStripItems(menuStrip.Items);
        }
        else if (root is ToolStrip toolStrip)
        {
            NormalizeToolStripItems(toolStrip.Items);
        }
    }

    public static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value ?? string.Empty;
        }

        return NormalizeCandidate(ReplaceKnownArtifacts(value));
    }

    private static string ReplaceKnownArtifacts(string value)
    {
        return value
            .Replace("\u0421\u20AC\u0421\u201A", "\u0448\u0442", StringComparison.Ordinal)
            .Replace("\u0421\u20AC\u0421", "\u0448\u0442", StringComparison.Ordinal)
            .Replace("\u0421\u20AC\u0421\u201A", "\u0448\u0442", StringComparison.Ordinal)
            .Replace("\u0420\u0458", "\u043C", StringComparison.Ordinal)
            .Replace("\u0421\u20AC\u0421\u201A", "\u0448\u0442", StringComparison.Ordinal)
            .Replace("\u0421\u0454\u0421\u201A", "\u0448\u0442", StringComparison.Ordinal)
            .Replace("\u0420\u0406,S", "\u20BD", StringComparison.Ordinal)
            .Replace("\u0432\u201A\u0405", "\u20BD", StringComparison.Ordinal)
            .Replace("\u00E2\u201A\u00BD", "\u20BD", StringComparison.Ordinal)
            .Replace("\u0413\u045E\u0432\u201A\u0412\u0405", "\u20BD", StringComparison.Ordinal)
            .Replace("\u0420\u0406\u0420\u201A\u0432\u20AC\u045A", "\u2014", StringComparison.Ordinal)
            .Replace("\u0420\u0406\u0420\u201A\u0432\u20AC\u045A", "\u2014", StringComparison.Ordinal)
            .Replace("\u0432\u20AC\u201D", "\u2014", StringComparison.Ordinal);
    }

    private static void NormalizeSingleControl(Control control)
    {
        AttachControlWatcher(control);

        var fixedText = NormalizeText(control.Text);
        if (!string.Equals(fixedText, control.Text, StringComparison.Ordinal))
        {
            control.Text = fixedText;
        }

        if (control is TextBox textBox)
        {
            var fixedPlaceholder = NormalizeText(textBox.PlaceholderText);
            if (!string.Equals(fixedPlaceholder, textBox.PlaceholderText, StringComparison.Ordinal))
            {
                textBox.PlaceholderText = fixedPlaceholder;
            }
        }

        if (control is ComboBox comboBox && comboBox.DataSource is null)
        {
            NormalizeListItems(comboBox.Items);
        }

        if (control is ListBox listBox && listBox.DataSource is null)
        {
            NormalizeListItems(listBox.Items);
        }

        if (control is ListControl listControl)
        {
            AttachListControlWatcher(listControl);
        }

        if (control is TabControl tabControl)
        {
            foreach (TabPage tab in tabControl.TabPages)
            {
                var fixedTitle = NormalizeText(tab.Text);
                if (!string.Equals(fixedTitle, tab.Text, StringComparison.Ordinal))
                {
                    tab.Text = fixedTitle;
                }

                NormalizeControlTree(tab);
            }
        }

        if (control is DataGridView dataGridView)
        {
            NormalizeDataGridAppearance(dataGridView);
            AttachDataGridWatcher(dataGridView);
        }
    }

    private static void NormalizeListItems(IList items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i] is not string text)
            {
                continue;
            }

            var fixedText = NormalizeText(text);
            if (!string.Equals(fixedText, text, StringComparison.Ordinal))
            {
                items[i] = fixedText;
            }
        }
    }

    private static void NormalizeToolStripItems(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            AttachToolStripWatcher(item);

            var fixedText = NormalizeText(item.Text);
            if (!string.Equals(fixedText, item.Text, StringComparison.Ordinal))
            {
                item.Text = fixedText;
            }

            if (item is ToolStripDropDownItem dropDownItem && dropDownItem.HasDropDownItems)
            {
                NormalizeToolStripItems(dropDownItem.DropDownItems);
            }
        }
    }

    private static void AttachControlWatcher(Control control)
    {
        if (HookedControls.TryGetValue(control, out _))
        {
            return;
        }

        HookedControls.Add(control, new object());
        control.TextChanged += HandleControlTextChanged;
        control.ControlAdded += HandleControlAdded;
    }

    private static void AttachToolStripWatcher(ToolStripItem item)
    {
        if (HookedToolStripItems.TryGetValue(item, out _))
        {
            return;
        }

        HookedToolStripItems.Add(item, new object());
        item.TextChanged += HandleToolStripItemTextChanged;

        if (item is ToolStripDropDownItem dropDownItem)
        {
            dropDownItem.DropDownOpened += HandleDropDownOpened;
        }
    }

    private static void AttachDataGridWatcher(DataGridView grid)
    {
        if (HookedDataGrids.TryGetValue(grid, out _))
        {
            return;
        }

        HookedDataGrids.Add(grid, new object());
        grid.CellFormatting += HandleDataGridCellFormatting;
        grid.DataBindingComplete += HandleDataGridDataBindingComplete;
        grid.ColumnAdded += HandleDataGridColumnAdded;
    }

    private static void AttachListControlWatcher(ListControl control)
    {
        if (HookedListControls.TryGetValue(control, out _))
        {
            return;
        }

        HookedListControls.Add(control, new object());
        control.Format += HandleListControlFormat;
    }

    private static void HandleControlTextChanged(object? sender, EventArgs e)
    {
        if (sender is not Control control || control.IsDisposed)
        {
            return;
        }

        var fixedText = NormalizeText(control.Text);
        if (!string.Equals(fixedText, control.Text, StringComparison.Ordinal))
        {
            control.Text = fixedText;
        }
    }

    private static void HandleControlAdded(object? sender, ControlEventArgs e)
    {
        if (e.Control is not null && !e.Control.IsDisposed)
        {
            NormalizeControlTree(e.Control);
        }
    }

    private static void HandleToolStripItemTextChanged(object? sender, EventArgs e)
    {
        if (sender is not ToolStripItem item)
        {
            return;
        }

        var fixedText = NormalizeText(item.Text);
        if (!string.Equals(fixedText, item.Text, StringComparison.Ordinal))
        {
            item.Text = fixedText;
        }
    }

    private static void HandleDropDownOpened(object? sender, EventArgs e)
    {
        if (sender is ToolStripDropDownItem dropDownItem)
        {
            NormalizeToolStripItems(dropDownItem.DropDownItems);
        }
    }

    private static void HandleListControlFormat(object? sender, ListControlConvertEventArgs e)
    {
        if (e.Value is not string text)
        {
            return;
        }

        var fixedText = NormalizeText(text);
        if (!string.Equals(fixedText, text, StringComparison.Ordinal))
        {
            e.Value = fixedText;
        }
    }

    private static void HandleDataGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.CellStyle is not null && !string.IsNullOrWhiteSpace(e.CellStyle.Format))
        {
            var normalizedFormat = NormalizeText(e.CellStyle.Format);
            if (!string.Equals(normalizedFormat, e.CellStyle.Format, StringComparison.Ordinal))
            {
                e.CellStyle.Format = normalizedFormat;
            }
        }

        if (e.Value is not string textValue)
        {
            return;
        }

        var fixedText = NormalizeText(textValue);
        if (!string.Equals(fixedText, textValue, StringComparison.Ordinal))
        {
            e.Value = fixedText;
            e.FormattingApplied = true;
        }
    }

    private static void HandleDataGridDataBindingComplete(object? sender, DataGridViewBindingCompleteEventArgs e)
    {
        if (sender is DataGridView grid)
        {
            NormalizeDataGridAppearance(grid);
        }
    }

    private static void HandleDataGridColumnAdded(object? sender, DataGridViewColumnEventArgs e)
    {
        if (sender is DataGridView grid)
        {
            NormalizeDataGridAppearance(grid);
        }
    }

    private static void NormalizeDataGridAppearance(DataGridView grid)
    {
        NormalizeCellStyle(grid.DefaultCellStyle);
        NormalizeCellStyle(grid.AlternatingRowsDefaultCellStyle);
        NormalizeCellStyle(grid.ColumnHeadersDefaultCellStyle);
        NormalizeCellStyle(grid.RowHeadersDefaultCellStyle);

        foreach (DataGridViewColumn column in grid.Columns)
        {
            var fixedHeader = NormalizeText(column.HeaderText);
            if (!string.Equals(fixedHeader, column.HeaderText, StringComparison.Ordinal))
            {
                column.HeaderText = fixedHeader;
            }

            NormalizeCellStyle(column.DefaultCellStyle);
        }
    }

    private static void NormalizeCellStyle(DataGridViewCellStyle? style)
    {
        if (style is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(style.Format))
        {
            var fixedFormat = NormalizeText(style.Format);
            if (!string.Equals(fixedFormat, style.Format, StringComparison.Ordinal))
            {
                style.Format = fixedFormat;
            }
        }

        if (style.NullValue is string nullText)
        {
            var fixedNullText = NormalizeText(nullText);
            if (!string.Equals(fixedNullText, nullText, StringComparison.Ordinal))
            {
                style.NullValue = fixedNullText;
            }
        }
    }

    private static string NormalizeCandidate(string original)
    {
        var originalScore = GetQualityScore(original);
        if (originalScore <= 0)
        {
            return original;
        }

        var best = original;
        var bestScore = originalScore;
        var current = original;

        for (var i = 0; i < 4; i++)
        {
            current = TransformOnce(current);
            if (IntroducesReplacementGlyphs(original, current))
            {
                break;
            }

            var score = GetQualityScore(current);
            if (score < bestScore)
            {
                best = current;
                bestScore = score;
            }
        }

        return best;
    }

    private static string TransformOnce(string text)
    {
        return Utf8.GetString(Cp1251.GetBytes(text));
    }

    private static bool IntroducesReplacementGlyphs(string original, string candidate)
    {
        return candidate.Contains('\uFFFD', StringComparison.Ordinal) ||
               CountChar(candidate, '?') > CountChar(original, '?');
    }

    private static int CountChar(string value, char target)
    {
        var count = 0;
        foreach (var ch in value)
        {
            if (ch == target)
            {
                count++;
            }
        }

        return count;
    }

    private static int GetQualityScore(string text)
    {
        var score = 0;
        var chars = text.AsSpan();
        for (var i = 0; i < chars.Length; i++)
        {
            var ch = chars[i];
            var code = (int)ch;
            if (code == 0xFFFD)
            {
                score += 1000;
            }

            if ((code >= 0x0080 && code <= 0x009F) ||
                (code >= 0x00A0 && code <= 0x00BF) ||
                (code >= 0x201A && code <= 0x203A) ||
                code is 0x20AC or 0x2122)
            {
                score += 20;
            }

            if (i < chars.Length - 1 && IsMojibakeLead(ch) && IsLikelyMojibakeTrail(chars[i + 1]))
            {
                score += 35;
            }
        }

        return score;
    }

    private static bool IsMojibakeLead(char ch)
    {
        return ch is '\u0420' or '\u0421';
    }

    private static bool IsLikelyMojibakeTrail(char ch)
    {
        var code = (int)ch;
        return IsMojibakeCyrillicTrail(ch) ||
               (code >= 0x00A0 && code <= 0x00BF) ||
               (code >= 0x201A && code <= 0x203A) ||
               code is 0x20AC or 0x2122;
    }

    private static bool IsMojibakeCyrillicTrail(char ch)
    {
        return ch is '\u0402' or '\u0403' or '\u0405' or '\u0406' or '\u0408' or '\u0409' or '\u040A' or '\u040B' or '\u040C' or '\u040E' or '\u040F'
            or '\u0452' or '\u0453' or '\u0455' or '\u0456' or '\u0458' or '\u0459' or '\u045A' or '\u045B' or '\u045C' or '\u045E' or '\u045F';
    }
}
