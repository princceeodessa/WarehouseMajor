using System.Windows;
using System.Windows.Controls;
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
    }

    private static string Normalize(string? value)
    {
        return TextMojibakeFixer.NormalizeText(value);
    }
}
