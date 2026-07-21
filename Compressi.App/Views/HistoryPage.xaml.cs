using System.ComponentModel;
using Compressi.Core.Models;
using Compressi_App.Services.UiSounds;
using Compressi_App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Compressi_App.Views;

public sealed partial class HistoryPage : Page, IAppPage
{
    private bool _isActive;

    public HistoryPage()
    {
        InitializeComponent();
        App.HistoryViewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    public void Activate()
    {
        _isActive = true;
        var vm = App.HistoryViewModel;
        SearchBox.Text = vm.SearchQuery;
        RefreshList();
        if (vm.NeedsRefresh)
        {
            _ = RefreshIfNeededAsync();
        }
    }

    public void Deactivate()
    {
        _isActive = false;
    }

    private async Task RefreshIfNeededAsync()
    {
        var vm = App.HistoryViewModel;
        if (!vm.NeedsRefresh)
        {
            return;
        }

        await vm.RefreshAsync();
        if (_isActive)
        {
            RefreshList();
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isActive)
        {
            return;
        }

        if (e.PropertyName is null
            or nameof(HistoryViewModel.Entries)
            or nameof(HistoryViewModel.HasEntries))
        {
            RefreshList();
        }
    }

    private void RefreshList()
    {
        var vm = App.HistoryViewModel;
        if (!ReferenceEquals(HistoryList.ItemsSource, vm.Entries))
        {
            HistoryList.ItemsSource = vm.Entries;
        }

        EmptyPanel.Visibility = vm.HasEntries ? Visibility.Collapsed : Visibility.Visible;
        HistoryScrollViewer.Visibility = vm.HasEntries ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        App.HistoryViewModel.SearchQuery = SearchBox.Text;
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: HistoryEntry entry })
        {
            return;
        }

        UiSoundService.Play(UiSoundName.Release);
        if (!App.HistoryViewModel.TryOpenOutputFolder(entry, out var errorMessage))
        {
            ShowHistoryMessage(errorMessage ?? "Couldn't open the output folder.", InfoBarSeverity.Warning);
        }
    }

    private void RerunButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: HistoryEntry entry })
        {
            UiSoundService.Play(UiSoundName.Press);
            App.HistoryViewModel.Rerun(entry);
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: HistoryEntry entry })
        {
            return;
        }

        var dialog = new ContentDialog
        {
            Title = "Delete history entry?",
            Content = $"Remove “{entry.SourceName}” from history? This does not delete the video files.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        UiSoundService.Play(UiSoundName.Droplet);
        App.HistoryViewModel.DeleteEntry(entry);
    }

    private void GoToCompressButton_Click(object sender, RoutedEventArgs e)
    {
        UiSoundService.Play(UiSoundName.Press);
        App.MainWindow?.NavigateToCompress();
    }

    private void ShowHistoryMessage(string message, InfoBarSeverity severity)
    {
        HistoryInfoBar.Severity = severity;
        HistoryInfoBar.Title = severity == InfoBarSeverity.Warning ? "File missing" : string.Empty;
        HistoryInfoBar.Message = message;
        HistoryInfoBar.IsOpen = true;
    }
}
