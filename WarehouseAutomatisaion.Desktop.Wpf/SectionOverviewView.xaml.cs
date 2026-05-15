using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace WarehouseAutomatisaion.Desktop.Wpf;

public partial class SectionOverviewView : UserControl
{
    private readonly NavigationSection _section;
    private readonly List<NavigationGroup> _allGroups;

    public SectionOverviewView(NavigationSection section)
    {
        _section = section;
        _allGroups = section.Groups.ToList();
        InitializeComponent();
        GroupsItemsControl.ItemsSource = _allGroups;
    }

    public string SectionKey => _section.Key;

    private void HandleCommandClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: NavigationCommand command })
        {
            return;
        }

        var mainWindow = System.Windows.Application.Current?.MainWindow as MainWindow;
        if (mainWindow is null || string.IsNullOrWhiteSpace(command.TargetSectionKey))
        {
            return;
        }

        mainWindow.OpenSection(command.TargetSectionKey);
        if (!string.IsNullOrEmpty(command.SubSection))
        {
            mainWindow.ActivateSubSection(command.TargetSectionKey, command.SubSection);
        }
    }

    private void HandleSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchBox.Text?.Trim() ?? string.Empty;
        SearchPlaceholderText.Visibility = string.IsNullOrEmpty(query) ? Visibility.Visible : Visibility.Collapsed;

        if (string.IsNullOrEmpty(query))
        {
            GroupsItemsControl.ItemsSource = _allGroups;
            return;
        }

        var filtered = _allGroups
            .Select(group => new NavigationGroup(
                group.Title,
                group.Items
                    .Where(item => item.Caption.Contains(query, System.StringComparison.OrdinalIgnoreCase))
                    .ToArray()))
            .Where(group => group.Items.Count > 0)
            .ToArray();

        GroupsItemsControl.ItemsSource = filtered;
    }

    private void HandleSettingsClick(object sender, RoutedEventArgs e)
    {
        var mainWindow = System.Windows.Application.Current?.MainWindow as MainWindow;
        mainWindow?.OpenSection("settings");
    }

    private void HandleCloseClick(object sender, RoutedEventArgs e)
    {
        var mainWindow = System.Windows.Application.Current?.MainWindow as MainWindow;
        mainWindow?.CloseWorkspaceTab(_section.Key);
    }
}
