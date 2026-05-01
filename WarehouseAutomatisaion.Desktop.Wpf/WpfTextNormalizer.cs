using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WarehouseAutomatisaion.Desktop.Text;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public static class WpfTextNormalizer
{
    public static void NormalizeTree(DependencyObject? root)
    {
        if (root is null)
        {
            return;
        }

        var visited = new HashSet<DependencyObject>();
        NormalizeNode(root, visited);
    }

    private static void NormalizeNode(DependencyObject node, ISet<DependencyObject> visited)
    {
        if (!visited.Add(node))
        {
            return;
        }

        NormalizeSelf(node);

        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
        {
            NormalizeNode(child, visited);
        }

        var visualChildren = 0;
        try
        {
            visualChildren = VisualTreeHelper.GetChildrenCount(node);
        }
        catch (InvalidOperationException)
        {
        }

        for (var i = 0; i < visualChildren; i++)
        {
            NormalizeNode(VisualTreeHelper.GetChild(node, i), visited);
        }
    }

    private static void NormalizeSelf(DependencyObject node)
    {
        switch (node)
        {
            case TextBlock textBlock:
                textBlock.Text = Normalize(textBlock.Text);
                break;
            case TextBox textBox when !textBox.IsKeyboardFocusWithin:
                textBox.Text = Normalize(textBox.Text);
                break;
            case HeaderedContentControl { Header: string header } headeredContentControl:
                headeredContentControl.Header = Normalize(header);
                break;
            case ContentControl { Content: string content } contentControl:
                contentControl.Content = Normalize(content);
                break;
            case DataGrid dataGrid:
                foreach (var column in dataGrid.Columns)
                {
                    if (column.Header is string columnHeader)
                    {
                        column.Header = Normalize(columnHeader);
                    }
                }
                break;
        }

        if (node is ItemsControl itemsControl && itemsControl.ItemsSource is null)
        {
            for (var i = 0; i < itemsControl.Items.Count; i++)
            {
                if (itemsControl.Items[i] is string item)
                {
                    itemsControl.Items[i] = Normalize(item);
                }
            }
        }

        if (node is FrameworkElement { ToolTip: string tooltip } frameworkElement)
        {
            frameworkElement.ToolTip = Normalize(tooltip);
        }

        if (node is ButtonBase button)
        {
            EnsureButtonAutomationName(button);
        }
    }

    private static string Normalize(string? value)
    {
        return TextMojibakeFixer.NormalizeText(value);
    }

    private static void EnsureButtonAutomationName(ButtonBase button)
    {
        var currentName = Normalize(AutomationProperties.GetName(button));
        if (!string.IsNullOrWhiteSpace(currentName))
        {
            AutomationProperties.SetName(button, currentName);
            return;
        }

        var candidate = ExtractText(button.Content);
        if (string.IsNullOrWhiteSpace(candidate) && button.ToolTip is string tooltip)
        {
            candidate = Normalize(tooltip);
        }

        if (string.IsNullOrWhiteSpace(candidate) && button.Content is not null)
        {
            candidate = MapIconButtonName(Normalize(button.Content.ToString()));
        }

        if (string.IsNullOrWhiteSpace(candidate) && FindAncestor<ComboBox>(button) is not null)
        {
            candidate = "Открыть список";
        }

        if (string.IsNullOrWhiteSpace(candidate)
            && button is ToggleButton
            && (string.Equals(button.Name, "Expander", StringComparison.OrdinalIgnoreCase)
                || FindAncestor<TreeViewItem>(button) is not null))
        {
            candidate = "Развернуть группу";
        }

        if (!string.IsNullOrWhiteSpace(candidate))
        {
            AutomationProperties.SetName(button, candidate);
        }
    }

    private static string ExtractText(object? value)
    {
        switch (value)
        {
            case null:
                return string.Empty;
            case string text:
                return IsIconOnly(text) ? string.Empty : Normalize(text).Trim();
            case TextBlock textBlock:
                return IsIconOnly(textBlock.Text) ? string.Empty : Normalize(textBlock.Text).Trim();
            case DependencyObject dependencyObject:
                return ExtractTextFromTree(dependencyObject);
            default:
                return string.Empty;
        }
    }

    private static string ExtractTextFromTree(DependencyObject root)
    {
        var values = new List<string>();
        CollectText(root, values, new HashSet<DependencyObject>());
        return string.Join(" ", values.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
    }

    private static void CollectText(DependencyObject node, ICollection<string> values, ISet<DependencyObject> visited)
    {
        if (!visited.Add(node))
        {
            return;
        }

        if (node is TextBlock textBlock)
        {
            var text = Normalize(textBlock.Text).Trim();
            if (!string.IsNullOrWhiteSpace(text) && !IsIconOnly(text))
            {
                values.Add(text);
            }
        }

        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
        {
            CollectText(child, values, visited);
        }

        var visualChildren = 0;
        try
        {
            visualChildren = VisualTreeHelper.GetChildrenCount(node);
        }
        catch (InvalidOperationException)
        {
        }

        for (var i = 0; i < visualChildren; i++)
        {
            CollectText(VisualTreeHelper.GetChild(node, i), values, visited);
        }
    }

    private static string MapIconButtonName(string? value)
    {
        var text = Normalize(value).Trim();
        return text switch
        {
            "\uE711" or "×" or "Г—" => "Закрыть",
            "\uE712" or "\uE70D" => "Действия",
            "\uE76B" or "<" or "‹" => "Назад",
            "\uE76C" or ">" or "›" => "Вперед",
            _ => string.Empty
        };
    }

    private static bool IsIconOnly(string? value)
    {
        var text = Normalize(value).Trim();
        return text.Length > 0
               && text.All(character =>
                   char.IsWhiteSpace(character)
                   || character is '×' or 'Г' or '—' or '<' or '>' or '‹' or '›'
                   || character is >= '\uE000' and <= '\uF8FF');
    }

    private static T? FindAncestor<T>(DependencyObject node)
        where T : DependencyObject
    {
        var parent = GetParent(node);
        while (parent is not null)
        {
            if (parent is T match)
            {
                return match;
            }

            parent = GetParent(parent);
        }

        return null;
    }

    private static DependencyObject? GetParent(DependencyObject node)
    {
        try
        {
            return VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node);
        }
        catch (InvalidOperationException)
        {
            return LogicalTreeHelper.GetParent(node);
        }
    }
}
