using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Compressi.Core.Models;
using Compressi.Core.Services;
using Compressi_App.Services;

namespace Compressi_App.ViewModels;

public sealed class CompressViewModel : INotifyPropertyChanged
{
    private static readonly long UiProgressIntervalTicks = Stopwatch.Frequency / 10;

    private readonly IMediaProbeService _probeService;
    private readonly IEncodingService _encodingService;
    private readonly HistoryStore _historyStore;
    private readonly SettingsStore _settingsStore;
    private CancellationTokenSource? _encodeCts;
    private long _lastUiProgressTimestamp;
    private VideoFile? _sourceFile;
    private CompressionResult? _result;
    private AppSettings _settings;
    private bool _isProbing;
    private bool _isEncoding;
    private string? _errorMessage;
    private string? _infoMessage;
    private CompressionPreset _preset;
    private OutputFormat? _outputFormat = Compressi.Core.Models.OutputFormat.Mp4;
    private double _progressPercent;
    private string _elapsedDisplay = "0:00";
    private string _remainingDisplay = "--:--";
    private string _outputSizeDisplay = "0 MB";
    private string _speedDisplay = string.Empty;
    private string? _resolutionOverride;
    private string? _frameRateOverride;
    private string? _audioBitrateOverride;
    private bool _keepOriginalAudio;
    private string? _outputFilenamePattern;
    private string? _outputDirectoryOverride;

    public CompressViewModel()
        : this(new MediaProbeService(), new FfmpegEncodingService(), new HistoryStore(), new SettingsStore())
    {
    }

    public CompressViewModel(
        IMediaProbeService probeService,
        IEncodingService encodingService,
        HistoryStore historyStore,
        SettingsStore settingsStore)
    {
        _probeService = probeService;
        _encodingService = encodingService;
        _historyStore = historyStore;
        _settingsStore = settingsStore;
        _settings = settingsStore.Load();
        _preset = _settings.DefaultPreset;
        _outputDirectoryOverride = _settings.DefaultOutputFolder;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? RerunRequested;

    public AppSettings Settings => _settings;

    public VideoFile? SourceFile
    {
        get => _sourceFile;
        private set
        {
            if (_sourceFile == value)
            {
                return;
            }

            _sourceFile = value;
            NotifySourceChanged();
        }
    }

    public CompressionResult? Result
    {
        get => _result;
        private set
        {
            if (_result == value)
            {
                return;
            }

            _result = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasResult));
            OnPropertyChanged(nameof(ShowEmptyCompleteState));
        }
    }

    public bool HasSourceFile => SourceFile is not null;
    public bool HasResult => Result is not null;
    public bool ShowEmptyCompleteState => !HasResult && !IsEncoding;

    public CompressionPreset Preset
    {
        get => _preset;
        set
        {
            if (_preset == value)
            {
                return;
            }

            _preset = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PresetHelperText));
            UpdateInfoMessage();
        }
    }

    public OutputFormat? OutputFormat
    {
        get => _outputFormat;
        set
        {
            if (_outputFormat == value)
            {
                return;
            }

            _outputFormat = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStartCompression));
        }
    }

    public string PresetHelperText => Preset switch
    {
        CompressionPreset.Ultra =>
            "Smallest file size, video quality reduced. Best for archiving or slow connections.",
        CompressionPreset.EightMB =>
            "Targets an 8 MB file while keeping audio and frame rate as high as possible. Great for Discord/chat sharing.",
        CompressionPreset.Balanced =>
            "Solid quality-to-size tradeoff for everyday sharing. Recommended.",
        _ => throw new ArgumentOutOfRangeException(nameof(Preset), Preset, "Unknown compression preset."),
    };

    public bool IsProbing
    {
        get => _isProbing;
        private set
        {
            if (_isProbing == value)
            {
                return;
            }

            _isProbing = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStartCompression));
            OnPropertyChanged(nameof(IsDropZoneEnabled));
            OnPropertyChanged(nameof(IsInputLocked));
            OnPropertyChanged(nameof(DropZoneMessage));
        }
    }

    public bool IsEncoding
    {
        get => _isEncoding;
        private set
        {
            if (_isEncoding == value)
            {
                return;
            }

            _isEncoding = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanStartCompression));
            OnPropertyChanged(nameof(CanCancelCompression));
            OnPropertyChanged(nameof(IsDropZoneEnabled));
            OnPropertyChanged(nameof(IsInputLocked));
            OnPropertyChanged(nameof(ShowEncodingProgress));
            OnPropertyChanged(nameof(ShowEmptyCompleteState));
        }
    }

    public bool ShowEncodingProgress => IsEncoding;
    public bool IsDropZoneEnabled => !IsProbing && !IsEncoding;
    public bool IsInputLocked => IsProbing || IsEncoding;
    public bool CanStartCompression => HasSourceFile && OutputFormat.HasValue && !IsProbing && !IsEncoding;
    public bool CanCancelCompression => IsEncoding;

    public double ProgressPercent
    {
        get => _progressPercent;
        private set
        {
            if (Math.Abs(_progressPercent - value) < 0.01)
            {
                return;
            }

            _progressPercent = value;
            OnPropertyChanged();
        }
    }

    public string ElapsedDisplay
    {
        get => _elapsedDisplay;
        private set
        {
            if (_elapsedDisplay == value)
            {
                return;
            }

            _elapsedDisplay = value;
            OnPropertyChanged();
        }
    }

    public string RemainingDisplay
    {
        get => _remainingDisplay;
        private set
        {
            if (_remainingDisplay == value)
            {
                return;
            }

            _remainingDisplay = value;
            OnPropertyChanged();
        }
    }

    public string OutputSizeDisplay
    {
        get => _outputSizeDisplay;
        private set
        {
            if (_outputSizeDisplay == value)
            {
                return;
            }

            _outputSizeDisplay = value;
            OnPropertyChanged();
        }
    }

    public string SpeedDisplay
    {
        get => _speedDisplay;
        private set
        {
            if (_speedDisplay == value)
            {
                return;
            }

            _speedDisplay = value;
            OnPropertyChanged();
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (_errorMessage == value)
            {
                return;
            }

            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public string? InfoMessage
    {
        get => _infoMessage;
        private set
        {
            if (_infoMessage == value)
            {
                return;
            }

            _infoMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasInfo));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasInfo => !string.IsNullOrWhiteSpace(InfoMessage);
    public string SourceFileName => SourceFile?.FileName ?? string.Empty;
    public string SourceMetadataLine => SourceFile?.MetadataLine ?? string.Empty;
    public string DropZoneMessage => IsProbing ? "Analyzing video..." : "Drag & drop a video file here";

    public string? ResolutionOverride
    {
        get => _resolutionOverride;
        set { _resolutionOverride = value; OnPropertyChanged(); }
    }

    public string? FrameRateOverride
    {
        get => _frameRateOverride;
        set { _frameRateOverride = value; OnPropertyChanged(); }
    }

    public string? AudioBitrateOverride
    {
        get => _audioBitrateOverride;
        set { _audioBitrateOverride = value; OnPropertyChanged(); }
    }

    public bool KeepOriginalAudio
    {
        get => _keepOriginalAudio;
        set { _keepOriginalAudio = value; OnPropertyChanged(); }
    }

    public string? OutputFilenamePattern
    {
        get => _outputFilenamePattern;
        set { _outputFilenamePattern = value; OnPropertyChanged(); }
    }

    public string? OutputDirectoryOverride
    {
        get => _outputDirectoryOverride;
        set { _outputDirectoryOverride = value; OnPropertyChanged(); }
    }

    public void ReloadSettings()
    {
        _settings = _settingsStore.Load();
        Preset = _settings.DefaultPreset;
        OutputDirectoryOverride = _settings.DefaultOutputFolder;
        UpdateInfoMessage();
    }

    public async Task LoadFileFromPathAsync(string filePath)
    {
        ErrorMessage = null;
        Result = null;
        IsProbing = true;

        try
        {
            SourceFile = await _probeService.ProbeAsync(filePath).ConfigureAwait(true);
            UpdateInfoMessage();
        }
        catch (Exception ex)
        {
            SourceFile = null;
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsProbing = false;
        }
    }

    public async Task StartCompressionAsync()
    {
        if (SourceFile is null || IsEncoding || OutputFormat is null)
        {
            return;
        }

        ErrorMessage = null;
        Result = null;
        IsEncoding = true;
        ResetProgress();
        var encodeStarted = DateTimeOffset.UtcNow;
        _lastUiProgressTimestamp = 0;
        _encodeCts = new CancellationTokenSource();

        try
        {
            var job = BuildJob();
            // Progress<T> marshals to the UI SynchronizationContext captured here.
            var progress = new Progress<EncodingProgress>(update =>
            {
                if (!ShouldApplyUiProgress(update.IsFinished))
                {
                    return;
                }

                ApplyProgress(update, encodeStarted);
            });

            var result = await _encodingService
                .EncodeAsync(job, progress, _encodeCts.Token)
                .ConfigureAwait(true);
            Result = result;
            if (!string.IsNullOrWhiteSpace(result.InfoNote))
            {
                InfoMessage = result.InfoNote;
            }

            SaveHistory(result, CompressionJobStatus.Completed);

            if (_settings.NotifyOnCompletion && App.MainWindow is not null)
            {
                // Toast notification deferred; completion is visible in-app.
            }
        }
        catch (OperationCanceledException)
        {
            SaveCancelledHistory();
            ErrorMessage = "Compression was cancelled.";
        }
        catch (Exception ex)
        {
            SaveFailedHistory(ex.Message);
            ErrorMessage = ex.Message;
        }
        finally
        {
            _encodeCts?.Dispose();
            _encodeCts = null;
            IsEncoding = false;
        }
    }

    private bool ShouldApplyUiProgress(bool force)
    {
        if (force)
        {
            _lastUiProgressTimestamp = Stopwatch.GetTimestamp();
            return true;
        }

        var now = Stopwatch.GetTimestamp();
        if (_lastUiProgressTimestamp != 0 && now - _lastUiProgressTimestamp < UiProgressIntervalTicks)
        {
            return false;
        }

        _lastUiProgressTimestamp = now;
        return true;
    }

    public void CancelCompression()
    {
        _encodeCts?.Cancel();
    }

    public void ClearSourceFile()
    {
        SourceFile = null;
        ErrorMessage = null;
        Result = null;
        InfoMessage = null;
    }

    public void CompressAnother()
    {
        Result = null;
        SourceFile = null;
        ErrorMessage = null;
        InfoMessage = null;
        ResetProgress();
        Preset = _settings.DefaultPreset;
        OutputFormat = Compressi.Core.Models.OutputFormat.Mp4;
    }

    public void RequestRerun(HistoryEntry entry)
    {
        RerunRequested?.Invoke(this, EventArgs.Empty);
        Preset = entry.Preset;
        OutputFormat = entry.Format;
        _ = LoadFileFromPathAsync(entry.SourcePath);
    }

    private CompressionJob BuildJob()
    {
        _settings = _settingsStore.Load();

        var useHardware = _settings.HardwareAcceleration && Preset != CompressionPreset.EightMB;
        // Prefer cached detection; fall back to catalog without forcing a blocking refresh on the UI path.
        var gpuEncoder = useHardware
            ? ResolveGpuEncoder(_settings.DetectedGpuEncoder)
            : null;
        var hardwareEnabled = useHardware && gpuEncoder is not null;
        string? note = null;

        if (_settings.HardwareAcceleration && Preset == CompressionPreset.EightMB)
        {
            note = "Using CPU encoding for precise size targeting.";
        }

        return new CompressionJob
        {
            Source = SourceFile!,
            Preset = Preset,
            Format = OutputFormat!.Value,
            ThreadCount = ResolveEncodeThreadCount(_settings.EffectiveThreadCount, hardwareEnabled),
            HardwareAccelerationEnabled = hardwareEnabled,
            GpuEncoder = gpuEncoder,
            EncodingInfoNote = note,
            Advanced = BuildAdvancedOptions(),
        };
    }

    private static string? ResolveGpuEncoder(string? detectedGpuEncoder)
    {
        if (!string.IsNullOrWhiteSpace(detectedGpuEncoder)
            && !string.Equals(detectedGpuEncoder, "None detected", StringComparison.OrdinalIgnoreCase))
        {
            return detectedGpuEncoder;
        }

        return FfmpegEncoderCatalog.GetPreferredGpuEncoder();
    }

    private static int ResolveEncodeThreadCount(int requestedThreads, bool hardwareEnabled)
    {
        if (hardwareEnabled)
        {
            return requestedThreads;
        }

        // Leave one logical core free so WinUI stays responsive during CPU-only encodes.
        var maxCpuThreads = Math.Max(1, Environment.ProcessorCount - 1);
        return Math.Max(1, Math.Min(requestedThreads, maxCpuThreads));
    }

    private AdvancedEncodingOptions BuildAdvancedOptions()
    {
        int? audioBitrate = int.TryParse(_audioBitrateOverride, out var parsedAudio) ? parsedAudio : null;
        int? frameRate = int.TryParse(_frameRateOverride, out var parsedFps) ? parsedFps : null;
        var resolution = string.IsNullOrWhiteSpace(_resolutionOverride) ? null : _resolutionOverride.Trim();
        int? resolutionWidth = null;
        int? resolutionHeight = null;
        if (resolution is not null
            && ResolutionParser.TryParse(resolution, out var parsedWidth, out var parsedHeight))
        {
            resolutionWidth = parsedWidth;
            resolutionHeight = parsedHeight;
        }

        return new AdvancedEncodingOptions
        {
            ResolutionOverride = resolution,
            ResolutionWidth = resolutionWidth,
            ResolutionHeight = resolutionHeight,
            FrameRateOverride = frameRate,
            AudioBitrateKbps = audioBitrate,
            KeepOriginalAudio = _keepOriginalAudio,
            OutputFilenamePattern = string.IsNullOrWhiteSpace(_outputFilenamePattern) ? null : _outputFilenamePattern,
            OutputDirectory = string.IsNullOrWhiteSpace(_outputDirectoryOverride)
                ? _settings.DefaultOutputFolder
                : _outputDirectoryOverride,
        };
    }

    private void SaveHistory(CompressionResult result, CompressionJobStatus status)
    {
        _historyStore.Add(new HistoryEntry
        {
            Id = 0,
            SourceName = result.Source.FileName,
            SourcePath = result.Source.FilePath,
            OutputPath = result.OutputPath,
            Preset = Preset,
            Format = OutputFormat!.Value,
            Status = status,
            OriginalSizeBytes = result.Source.FileSizeBytes,
            CompressedSizeBytes = result.Output.FileSizeBytes,
            CompressionRatioPercent = result.CompressionRatioPercent,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private void SaveFailedHistory(string? _)
    {
        if (SourceFile is null)
        {
            return;
        }

        _historyStore.Add(new HistoryEntry
        {
            Id = 0,
            SourceName = SourceFile.FileName,
            SourcePath = SourceFile.FilePath,
            OutputPath = null,
            Preset = Preset,
            Format = OutputFormat!.Value,
            Status = CompressionJobStatus.Failed,
            OriginalSizeBytes = SourceFile.FileSizeBytes,
            CompressedSizeBytes = 0,
            CompressionRatioPercent = 0,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private void SaveCancelledHistory()
    {
        if (SourceFile is null)
        {
            return;
        }

        _historyStore.Add(new HistoryEntry
        {
            Id = 0,
            SourceName = SourceFile.FileName,
            SourcePath = SourceFile.FilePath,
            OutputPath = null,
            Preset = Preset,
            Format = OutputFormat!.Value,
            Status = CompressionJobStatus.Cancelled,
            OriginalSizeBytes = SourceFile.FileSizeBytes,
            CompressedSizeBytes = 0,
            CompressionRatioPercent = 0,
            CreatedAt = DateTimeOffset.UtcNow,
        });
    }

    private void UpdateInfoMessage()
    {
        InfoMessage = _settings.HardwareAcceleration && Preset == CompressionPreset.EightMB
            ? "Using CPU encoding for precise size targeting."
            : null;
    }

    private void ApplyProgress(EncodingProgress update, DateTimeOffset startedAt)
    {
        var progressChanged = false;

        if (update.ProgressPercent is not null
            && Math.Abs(_progressPercent - update.ProgressPercent.Value) >= 0.01)
        {
            _progressPercent = update.ProgressPercent.Value;
            progressChanged = true;
        }

        if (update.OutputSizeBytes is not null)
        {
            var sizeDisplay = VideoFile.FormatFileSize(update.OutputSizeBytes.Value);
            if (!string.Equals(_outputSizeDisplay, sizeDisplay, StringComparison.Ordinal))
            {
                _outputSizeDisplay = sizeDisplay;
                progressChanged = true;
            }
        }

        if (update.SpeedMultiplier is not null)
        {
            var speedDisplay = $"{update.SpeedMultiplier.Value:0.##}x";
            if (!string.Equals(_speedDisplay, speedDisplay, StringComparison.Ordinal))
            {
                _speedDisplay = speedDisplay;
                progressChanged = true;
            }
        }

        var elapsed = DateTimeOffset.UtcNow - startedAt;
        var elapsedDisplay = VideoFile.FormatDuration(elapsed);
        if (!string.Equals(_elapsedDisplay, elapsedDisplay, StringComparison.Ordinal))
        {
            _elapsedDisplay = elapsedDisplay;
            progressChanged = true;
        }

        if (update.ProgressPercent is > 0 and < 100)
        {
            var remainingSeconds = elapsed.TotalSeconds / (update.ProgressPercent.Value / 100) - elapsed.TotalSeconds;
            var remainingDisplay = VideoFile.FormatDuration(TimeSpan.FromSeconds(Math.Max(0, remainingSeconds)));
            if (!string.Equals(_remainingDisplay, remainingDisplay, StringComparison.Ordinal))
            {
                _remainingDisplay = remainingDisplay;
                progressChanged = true;
            }
        }
        else if (update.IsFinished)
        {
            if (Math.Abs(_progressPercent - 100) >= 0.01)
            {
                _progressPercent = 100;
                progressChanged = true;
            }

            if (!string.Equals(_remainingDisplay, "0:00", StringComparison.Ordinal))
            {
                _remainingDisplay = "0:00";
                progressChanged = true;
            }
        }

        // One notification so the page refreshes progress controls once per tick.
        if (progressChanged)
        {
            OnPropertyChanged(nameof(ProgressPercent));
        }
    }

    private void ResetProgress()
    {
        ProgressPercent = 0;
        ElapsedDisplay = "0:00";
        RemainingDisplay = "--:--";
        OutputSizeDisplay = "0 MB";
        SpeedDisplay = string.Empty;
    }

    private void NotifySourceChanged()
    {
        OnPropertyChanged(nameof(SourceFile));
        OnPropertyChanged(nameof(HasSourceFile));
        OnPropertyChanged(nameof(CanStartCompression));
        OnPropertyChanged(nameof(SourceFileName));
        OnPropertyChanged(nameof(SourceMetadataLine));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
