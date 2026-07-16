using Compressi.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Compressi_App.Views;

public sealed partial class HistoryPage : Page
{
    public HistoryPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        RefreshList();
    }

    private void RefreshList()
    {
        var vm = App.HistoryViewModel;
        HistoryList.ItemsSource = vm.Entries;
        EmptyText.Visibility = vm.HasEntries ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        App.HistoryViewModel.SearchQuery = SearchBox.Text;
        RefreshList();
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HistoryEntry entry })
        {
            App.HistoryViewModel.OpenOutputFolder(entry);
        }
    }

    private void RerunButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HistoryEntry entry })
        {
            App.HistoryViewModel.Rerun(entry);
        }
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HistoryEntry entry })
        {
            App.HistoryViewModel.DeleteEntry(entry);
            RefreshList();
        }
    }
}
