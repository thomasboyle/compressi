using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Compressi.Core.Models;
using Compressi.Core.Services;

namespace Compressi_App.ViewModels;

public sealed class HistoryViewModel : INotifyPropertyChanged
{
    private readonly HistoryStore _historyStore;
    private string _searchQuery = string.Empty;
    private IReadOnlyList<HistoryEntry> _entries = [];

    public HistoryViewModel()
        : this(new HistoryStore())
    {
    }

    public HistoryViewModel(HistoryStore historyStore)
    {
        _historyStore = historyStore;
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<HistoryEntry>? RerunRequested;

    public IReadOnlyList<HistoryEntry> Entries
    {
        get => _entries;
        private set
        {
            _entries = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasEntries));
        }
    }

    public bool HasEntries => Entries.Count > 0;

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (_searchQuery == value)
            {
                return;
            }

            _searchQuery = value;
            OnPropertyChanged();
            Refresh();
        }
    }

    public void Refresh()
    {
        Entries = _historyStore.Search(SearchQuery);
    }

    public void DeleteEntry(HistoryEntry entry)
    {
        _historyStore.Delete(entry.Id);
        Refresh();
    }

    public void OpenOutputFolder(HistoryEntry entry)
    {
        if (string.IsNullOrWhiteSpace(entry.OutputPath))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{entry.OutputPath}\"",
            UseShellExecute = true,
        });
    }

    public void Rerun(HistoryEntry entry)
    {
        RerunRequested?.Invoke(this, entry);
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
