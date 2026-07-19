using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Compressi.Core.Models;
using Compressi.Core.Services;

namespace Compressi_App.ViewModels;

public sealed class HistoryViewModel : INotifyPropertyChanged
{
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(200);

    private readonly HistoryStore _historyStore;
    private string _searchQuery = string.Empty;
    private IReadOnlyList<HistoryEntry> _allEntries = [];
    private IReadOnlyList<HistoryEntry> _entries = [];
    private CancellationTokenSource? _searchCts;
    private bool _isLoaded;
    private bool _isDirty = true;

    public HistoryViewModel()
        : this(new HistoryStore())
    {
    }

    public HistoryViewModel(HistoryStore historyStore)
    {
        _historyStore = historyStore;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler<HistoryEntry>? RerunRequested;

    public IReadOnlyList<HistoryEntry> Entries
    {
        get => _entries;
        private set
        {
            if (ReferenceEquals(_entries, value))
            {
                return;
            }

            _entries = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasEntries));
        }
    }

    public bool HasEntries => Entries.Count > 0;

    public bool NeedsRefresh => !_isLoaded || _isDirty;

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
            if (string.IsNullOrWhiteSpace(_searchQuery))
            {
                CancelScheduledFilter();
                ApplyFilter();
                return;
            }

            ScheduleFilter();
        }
    }

    public void MarkDirty() => _isDirty = true;

    public void Refresh()
    {
        CancelScheduledFilter();
        _allEntries = _historyStore.GetAll();
        _isLoaded = true;
        _isDirty = false;
        ApplyFilter();
    }

    public async Task RefreshAsync()
    {
        CancelScheduledFilter();
        var entries = await Task.Run(_historyStore.GetAll).ConfigureAwait(true);
        _allEntries = entries;
        _isLoaded = true;
        _isDirty = false;
        ApplyFilter();
    }

    public void DeleteEntry(HistoryEntry entry)
    {
        _historyStore.Delete(entry.Id);
        Refresh();
    }

    public bool TryOpenOutputFolder(HistoryEntry entry, out string? errorMessage)
    {
        errorMessage = null;
        if (string.IsNullOrWhiteSpace(entry.OutputPath))
        {
            errorMessage = "This history entry has no output file.";
            return false;
        }

        if (!File.Exists(entry.OutputPath))
        {
            errorMessage = "The output file is no longer available. You can Re-run the compression.";
            return false;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{entry.OutputPath}\"",
            UseShellExecute = true,
        });
        return true;
    }

    public void Rerun(HistoryEntry entry)
    {
        RerunRequested?.Invoke(this, entry);
    }

    private void CancelScheduledFilter()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = null;
    }

    private void ScheduleFilter()
    {
        CancelScheduledFilter();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;
        _ = DebouncedFilterAsync(token);
    }

    private async Task DebouncedFilterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(SearchDebounce, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!token.IsCancellationRequested)
        {
            ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        if (string.IsNullOrWhiteSpace(_searchQuery))
        {
            Entries = _allEntries;
            return;
        }

        var query = _searchQuery.Trim();
        Entries = _allEntries
            .Where(entry => entry.SourceName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
