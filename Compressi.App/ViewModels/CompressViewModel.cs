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
    private string? _errorActionLabel;
    private CompressionPreset _preset;
    private OutputFormat? _outputFormat = Compressi.Core.Models.OutputFormat.Mp4;
    private VideoCodec _videoCodec = VideoCodec.Av1;
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
    private int _propertyChangedBatchDepth;
    private bool _hasBatchedPropertyChanged;

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
    public event EventHandler? HistoryChanged;

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
            NotifyUiStateChanged(
                nameof(Result),
                nameof(HasResult),
                nameof(ShowEmptyCompleteState));
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
            using (BatchPropertyChanged())
            {
                OnPropertyChanged();
                OnPropertyChanged(nameof(PresetHelperText));
                UpdateInfoMessage();
            }
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
            using (BatchPropertyChanged())
            {
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanStartCompression));
                if (_outputFormat == Compressi.Core.Models.OutputFormat.WebM && _videoCodec == VideoCodec.H264)
                {
                    VideoCodec = VideoCodec.Av1;
                }
            }
        }
    }

    public VideoCodec VideoCodec
    {
        get => _videoCodec;
        set
        {
            if (_videoCodec == value)
            {
                return;
            }

            _videoCodec = value;
            using (BatchPropertyChanged())
            {
                OnPropertyChanged();
                if (_videoCodec == VideoCodec.H264
                    && _outputFormat == Compressi.Core.Models.OutputFormat.WebM)
                {
                    OutputFormat = Compressi.Core.Models.OutputFormat.Mp4;
                }
            }
        }
    }

    public string PresetHelperText => Preset switch
    {
        CompressionPreset.Ultra =>
            "Smallest file size, video quality reduced. Best for archiving or slow connections.",
        CompressionPreset.EightMB =>
            "Targets an 8MB file while keeping audio and frame rate as high as possible. Great for Discord/chat sharing.",
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
            NotifyUiStateChanged(
                nameof(IsProbing),
                nameof(CanStartCompression),
                nameof(IsDropZoneEnabled),
                nameof(IsInputLocked),
                nameof(DropZoneMessage));
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
            NotifyUiStateChanged(
                nameof(IsEncoding),
                nameof(CanStartCompression),
                nameof(CanCancelCompression),
                nameof(IsDropZoneEnabled),
                nameof(IsInputLocked),
                nameof(ShowEncodingProgress),
                nameof(ShowEmptyCompleteState));
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
            NotifyUiStateChanged(nameof(ErrorMessage), nameof(HasError), nameof(HasErrorAction));
        }
    }

    public string? ErrorActionLabel
    {
        get => _errorActionLabel;
        private set
        {
            if (_errorActionLabel == value)
            {
                return;
            }

            _errorActionLabel = value;
            NotifyUiStateChanged(nameof(ErrorActionLabel), nameof(HasErrorAction));
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
            NotifyUiStateChanged(nameof(InfoMessage), nameof(HasInfo));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasErrorAction => HasError && !string.IsNullOrWhiteSpace(ErrorActionLabel);
    public bool HasInfo => !string.IsNullOrWhiteSpace(InfoMessage);

    public string OutputDirectoryDisplay
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_outputDirectoryOverride))
            {
                return _outputDirectoryOverride;
            }

            if (!string.IsNullOrWhiteSpace(_settings.DefaultOutputFolder))
            {
                return _settings.DefaultOutputFolder;
            }

            return "Same folder as the source file";
        }
    }
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
        set
        {
            if (_outputDirectoryOverride == value)
            {
                return;
            }

            _outputDirectoryOverride = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OutputDirectoryDisplay));
        }
    }

    public void ReloadSettings()
    {
        _settings = _settingsStore.Load();
        Preset = _settings.DefaultPreset;
        OutputDirectoryOverride = _settings.DefaultOutputFolder;
        UpdateInfoMessage();
        OnPropertyChanged(nameof(OutputDirectoryDisplay));
    }

    public async Task LoadFileFromPathAsync(string filePath)
    {
        using (BatchPropertyChanged())
        {
            ClearStatusMessages();
            Result = null;
            IsProbing = true;
        }

        try
        {
            var probed = await _probeService.ProbeAsync(filePath).ConfigureAwait(true);
            using (BatchPropertyChanged())
            {
                SourceFile = probed;
                UpdateInfoMessage();
            }
        }
        catch (Exception ex)
        {
            SourceFile = null;
            SetError(UserFacingErrors.FromException(ex));
        }
        finally
        {
            IsProbing = false;
        }
    }

    public string? GetPlannedOutputPath()
    {
        if (SourceFile is null || OutputFormat is null)
        {
            return null;
        }

        return CompressionPresetResolver.ResolveOutputPath(BuildJob(), ensureDirectoryExists: false);
    }

    public string? ValidateAdvancedOverrides()
    {
        if (!string.IsNullOrWhiteSpace(_resolutionOverride)
            && !ResolutionParser.TryParse(_resolutionOverride, out _, out _))
        {
            return "Resolution must look like 1920x1080.";
        }

        if (!string.IsNullOrWhiteSpace(_frameRateOverride)
            && (!int.TryParse(_frameRateOverride, out var fps) || fps <= 0))
        {
            return "Frame rate must be a positive whole number (e.g. 30).";
        }

        if (!string.IsNullOrWhiteSpace(_audioBitrateOverride)
            && (!int.TryParse(_audioBitrateOverride, out var kbps) || kbps <= 0))
        {
            return "Audio bitrate must be a positive whole number in kbps (e.g. 128).";
        }

        return null;
    }

    public async Task StartCompressionAsync()
    {
        if (SourceFile is null || IsEncoding || OutputFormat is null)
        {
            return;
        }

        var validationError = ValidateAdvancedOverrides();
        if (validationError is not null)
        {
            SetError(new UserFacingError(validationError, null));
            return;
        }

        using (BatchPropertyChanged())
        {
            ClearStatusMessages();
            Result = null;
            IsEncoding = true;
            ResetProgress();
        }

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
            using (BatchPropertyChanged())
            {
                Result = result;
                if (!string.IsNullOrWhiteSpace(result.InfoNote))
                {
                    InfoMessage = result.InfoNote;
                }
            }

            SaveHistory(result, CompressionJobStatus.Completed);

            if (_settings.NotifyOnCompletion && CompletionNotificationService.IsAvailable)
            {
                CompletionNotificationService.ShowCompressionComplete(result.Output.FileName);
            }
        }
        catch (OperationCanceledException)
        {
            SaveCancelledHistory();
            using (BatchPropertyChanged())
            {
                ErrorMessage = null;
                ErrorActionLabel = null;
                InfoMessage = "Compression was cancelled.";
            }
        }
        catch (Exception ex)
        {
            var facing = UserFacingErrors.FromException(ex);
            SaveFailedHistory(facing.Message);
            SetError(facing);
        }
        finally
        {
            _encodeCts?.Dispose();
            _encodeCts = null;
            IsEncoding = false;
        }
    }

    public void ClearStatusMessages()
    {
        ErrorMessage = null;
        ErrorActionLabel = null;
        InfoMessage = null;
    }

    private void SetError(UserFacingError error)
    {
        using (BatchPropertyChanged())
        {
            InfoMessage = null;
            ErrorMessage = error.Message;
            ErrorActionLabel = error.ActionLabel;
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
        using (BatchPropertyChanged())
        {
            SourceFile = null;
            Result = null;
            ClearStatusMessages();
        }
    }

    public void CompressAnother()
    {
        using (BatchPropertyChanged())
        {
            Result = null;
            SourceFile = null;
            ClearStatusMessages();
            ResetProgress();
            Preset = _settings.DefaultPreset;
            OutputFormat = Compressi.Core.Models.OutputFormat.Mp4;
            VideoCodec = VideoCodec.Av1;
        }
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
        // Use in-memory settings; ReloadSettings() refreshes after Settings save / encoder warmup.
        var useHardware = _settings.HardwareAcceleration
            && Preset != CompressionPreset.EightMB
            && VideoCodec == VideoCodec.Av1;
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
        else if (_settings.HardwareAcceleration && VideoCodec == VideoCodec.H264)
        {
            note = "Using CPU encoding for H.264.";
        }

        return new CompressionJob
        {
            Source = SourceFile!,
            Preset = Preset,
            Format = OutputFormat!.Value,
            VideoCodec = VideoCodec,
            ThreadCount = ResolveEncodeThreadCount(_settings.EffectiveThreadCount, hardwareEnabled),
            HardwareAccelerationEnabled = hardwareEnabled,
            GpuEncoder = gpuEncoder,
            EncodingInfoNote = note,
            Advanced = BuildAdvancedOptions(),
        };
    }

    private static string? ResolveGpuEncoder(string? detectedGpuEncoder)
    {
        if (string.IsNullOrWhiteSpace(detectedGpuEncoder))
        {
            // Not persisted yet — fall back to catalog (may probe once).
            return FfmpegEncoderCatalog.GetPreferredGpuEncoder();
        }

        if (string.Equals(detectedGpuEncoder, "None detected", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return detectedGpuEncoder;
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
        HistoryChanged?.Invoke(this, EventArgs.Empty);
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
        HistoryChanged?.Invoke(this, EventArgs.Empty);
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
        HistoryChanged?.Invoke(this, EventArgs.Empty);
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
        _progressPercent = 0;
        _elapsedDisplay = "0:00";
        _remainingDisplay = "--:--";
        _outputSizeDisplay = "0 MB";
        _speedDisplay = string.Empty;
        OnPropertyChanged(nameof(ProgressPercent));
        OnPropertyChanged(nameof(ElapsedDisplay));
        OnPropertyChanged(nameof(RemainingDisplay));
        OnPropertyChanged(nameof(OutputSizeDisplay));
        OnPropertyChanged(nameof(SpeedDisplay));
    }

    private void NotifySourceChanged()
    {
        NotifyUiStateChanged(
            nameof(SourceFile),
            nameof(HasSourceFile),
            nameof(CanStartCompression),
            nameof(SourceFileName),
            nameof(SourceMetadataLine));
    }

    private void NotifyUiStateChanged(params string[] propertyNames)
    {
        using (BatchPropertyChanged())
        {
            foreach (var propertyName in propertyNames)
            {
                OnPropertyChanged(propertyName);
            }
        }
    }

    private PropertyChangedBatch BatchPropertyChanged() => new(this);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        if (_propertyChangedBatchDepth > 0)
        {
            _hasBatchedPropertyChanged = true;
            return;
        }

        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private void BeginPropertyChangedBatch()
    {
        if (_propertyChangedBatchDepth == 0)
        {
            _hasBatchedPropertyChanged = false;
        }

        _propertyChangedBatchDepth++;
    }

    private void EndPropertyChangedBatch()
    {
        _propertyChangedBatchDepth--;
        if (_propertyChangedBatchDepth != 0 || !_hasBatchedPropertyChanged)
        {
            return;
        }

        _hasBatchedPropertyChanged = false;
        // Null property name means "many properties changed" — listeners refresh UI once.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private readonly struct PropertyChangedBatch : IDisposable
    {
        private readonly CompressViewModel _owner;

        public PropertyChangedBatch(CompressViewModel owner)
        {
            _owner = owner;
            _owner.BeginPropertyChangedBatch();
        }

        public void Dispose() => _owner.EndPropertyChangedBatch();
    }
}
