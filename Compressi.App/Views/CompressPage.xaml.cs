using System.Diagnostics;
using Compressi.Core.Models;
using Compressi.Core.Services;
using Compressi_App.Services;
using Compressi_App.Services.UiSounds;
using Compressi_App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace Compressi_App.Views;

public sealed partial class CompressPage : Page, IAppPage
{
    private static readonly SolidColorBrush DropZoneDefaultFill = new(Color.FromArgb(0x14, 0xA7, 0xB1, 0x8F));
    private static readonly SolidColorBrush DropZoneHoverFill = new(Color.FromArgb(0x28, 0xA7, 0xB1, 0x8F));
    private static readonly SolidColorBrush DropZoneDefaultStroke = new(Color.FromArgb(0xFF, 0x4F, 0x58, 0x38));
    private static readonly SolidColorBrush DropZoneHoverStroke = new(Color.FromArgb(0xFF, 0x2A, 0x32, 0x20));

    private bool _suppressFormatToggle;
    private bool _suppressCodecToggle;
    private bool _suppressPresetSync;
    private bool _suppressKeepAudioSound;
    private bool _uiStateUpdateQueued;
    private bool _isActive;
    private bool _uiDirty;
    private string? _loadedPreviewPath;
    private string? _lastHeardError;
    private string? _lastHeardSourcePath;
    private string? _lastHeardResultPath;
    private MediaPlayerElement? _previewPlayer;

    public CompressViewModel ViewModel => App.CompressViewModel;

    public CompressPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        SyncAdvancedFieldsFromViewModel();
        SyncFormatFromViewModel();
        SyncCodecFromViewModel();
        UpdateUiState();
    }

    public void Activate()
    {
        _isActive = true;
        if (!_uiDirty)
        {
            return;
        }

        _uiDirty = false;
        SyncPresetFromViewModel();
        SyncFormatFromViewModel();
        SyncCodecFromViewModel();
        UpdateUiState();
    }

    public void Deactivate()
    {
        _isActive = false;
    }

    private void LeftScrollViewer_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Fill the viewport when the window grows; allow scroll when advanced options overflow.
        if (e.NewSize.Height > 0)
        {
            LeftContentRoot.MinHeight = e.NewSize.Height;
        }
    }

    private void ResultPanel_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (ResultContentRoot is not null && e.NewSize.Height > 0)
        {
            ResultContentRoot.MinHeight = e.NewSize.Height;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        PlayOutcomeSound(e.PropertyName);

        if (!_isActive)
        {
            _uiDirty = true;
            return;
        }

        // Progress ticks are frequent during CPU multi-pass encodes; only refresh progress controls.
        switch (e.PropertyName)
        {
            case nameof(CompressViewModel.ProgressPercent):
            case nameof(CompressViewModel.ElapsedDisplay):
            case nameof(CompressViewModel.RemainingDisplay):
            case nameof(CompressViewModel.OutputSizeDisplay):
            case nameof(CompressViewModel.SpeedDisplay):
                UpdateProgressUi();
                return;
            default:
                QueueUiStateUpdate();
                return;
        }
    }

    private void PlayOutcomeSound(string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(CompressViewModel.Result):
                if (ViewModel.Result is { } result
                    && !string.Equals(_lastHeardResultPath, result.OutputPath, StringComparison.OrdinalIgnoreCase))
                {
                    _lastHeardResultPath = result.OutputPath;
                    UiSoundService.Play(UiSoundName.Success);
                }
                else if (ViewModel.Result is null)
                {
                    _lastHeardResultPath = null;
                }

                break;

            case nameof(CompressViewModel.ErrorMessage):
                if (ViewModel.HasError
                    && !string.Equals(_lastHeardError, ViewModel.ErrorMessage, StringComparison.Ordinal))
                {
                    _lastHeardError = ViewModel.ErrorMessage;
                    UiSoundService.Play(UiSoundName.Error);
                }
                else if (!ViewModel.HasError)
                {
                    _lastHeardError = null;
                }

                break;

            case nameof(CompressViewModel.SourceFile):
                if (ViewModel.HasSourceFile
                    && !string.Equals(_lastHeardSourcePath, ViewModel.SourceFile?.FilePath, StringComparison.OrdinalIgnoreCase))
                {
                    _lastHeardSourcePath = ViewModel.SourceFile?.FilePath;
                    UiSoundService.Play(UiSoundName.Ready);
                }
                else if (!ViewModel.HasSourceFile)
                {
                    _lastHeardSourcePath = null;
                }

                break;
        }
    }

    private void QueueUiStateUpdate()
    {
        if (!_isActive || _uiStateUpdateQueued)
        {
            return;
        }

        _uiStateUpdateQueued = true;
        DispatcherQueue.TryEnqueue(() =>
        {
            _uiStateUpdateQueued = false;
            if (_isActive)
            {
                UpdateUiState();
            }
        });
    }

    private void UpdateProgressUi()
    {
        EncodingProgressBar.Value = ViewModel.ProgressPercent;
        ElapsedText.Text = $"Elapsed: {ViewModel.ElapsedDisplay}";
        RemainingText.Text = $"Remaining: {ViewModel.RemainingDisplay}";
        OutputSizeText.Text = $"Output: {ViewModel.OutputSizeDisplay}";
        SpeedText.Text = string.IsNullOrWhiteSpace(ViewModel.SpeedDisplay)
            ? string.Empty
            : $"Speed: {ViewModel.SpeedDisplay}";
    }

    private void SyncPresetFromViewModel()
    {
        PresetComboBox.SelectedIndex = ViewModel.Preset switch
        {
            CompressionPreset.Ultra => 0,
            CompressionPreset.EightMB => 1,
            _ => 2,
        };
    }

    private void SyncAdvancedFieldsFromViewModel()
    {
        ResolutionOverrideBox.Text = ViewModel.ResolutionOverride ?? string.Empty;
        FrameRateOverrideBox.Text = ViewModel.FrameRateOverride ?? string.Empty;
        AudioBitrateOverrideBox.Text = ViewModel.AudioBitrateOverride ?? string.Empty;
        _suppressKeepAudioSound = true;
        KeepAudioToggle.IsOn = ViewModel.KeepOriginalAudio;
        _suppressKeepAudioSound = false;
        OutputPatternBox.Text = ViewModel.OutputFilenamePattern ?? string.Empty;
    }

    private void SyncAdvancedFieldsToViewModel()
    {
        ViewModel.ResolutionOverride = ResolutionOverrideBox.Text;
        ViewModel.FrameRateOverride = FrameRateOverrideBox.Text;
        ViewModel.AudioBitrateOverride = AudioBitrateOverrideBox.Text;
        ViewModel.KeepOriginalAudio = KeepAudioToggle.IsOn;
        ViewModel.OutputFilenamePattern = OutputPatternBox.Text;
    }

    private void UpdateUiState()
    {
        ProbeProgressRing.IsActive = ViewModel.IsProbing;
        ProbeProgressRing.Visibility = ViewModel.IsProbing ? Visibility.Visible : Visibility.Collapsed;
        DropZoneIcon.Visibility = ViewModel.IsProbing ? Visibility.Collapsed : Visibility.Visible;
        DropZoneText.Text = ViewModel.DropZoneMessage;
        RemoveFileButton.IsEnabled = !ViewModel.IsInputLocked;
        DropZoneHitTarget.IsHitTestVisible = ViewModel.IsDropZoneEnabled;
        BrowseFilesButton.IsEnabled = ViewModel.IsDropZoneEnabled;

        if (!ViewModel.IsDropZoneEnabled)
        {
            ApplyDropZoneVisualState(isHovered: false);
        }
        StartCompressionButton.IsEnabled = ViewModel.CanStartCompression;
        StartCompressionButton.Visibility = ViewModel.ShowEncodingProgress ? Visibility.Collapsed : Visibility.Visible;
        CancelCompressionButton.Visibility = ViewModel.ShowEncodingProgress ? Visibility.Visible : Visibility.Collapsed;
        EncodingProgressPanel.Visibility = ViewModel.ShowEncodingProgress ? Visibility.Visible : Visibility.Collapsed;
        UpdateProgressUi();

        _suppressPresetSync = true;
        PresetComboBox.SelectedIndex = ViewModel.Preset switch
        {
            CompressionPreset.Ultra => 0,
            CompressionPreset.EightMB => 1,
            _ => 2,
        };
        _suppressPresetSync = false;

        SyncFormatFromViewModel();
        SyncCodecFromViewModel();

        UpdateStatusInfoBar();
        OutputFolderPathText.Text = $"Saving to: {ViewModel.OutputDirectoryDisplay}";

        if (ViewModel.HasSourceFile)
        {
            FileChip.Visibility = Visibility.Visible;
            FileNameText.Text = ViewModel.SourceFileName;
            FileMetadataText.Text = ViewModel.SourceMetadataLine;
        }
        else
        {
            FileChip.Visibility = Visibility.Collapsed;
        }

        EmptyCompletePanel.Visibility = ViewModel.ShowEmptyCompleteState ? Visibility.Visible : Visibility.Collapsed;

        if (ViewModel.HasResult)
        {
            var resultPanel = EnsureResultPanel();
            resultPanel.Visibility = Visibility.Visible;
        }
        else if (ResultPanel is not null)
        {
            ResultPanel.Visibility = Visibility.Collapsed;
        }

        if (ViewModel.Result is { } result)
        {
            BindResultPanel(result);
            if (!string.Equals(_loadedPreviewPath, result.OutputPath, StringComparison.OrdinalIgnoreCase))
            {
                _loadedPreviewPath = result.OutputPath;
                _ = LoadPreviewAsync(result.OutputPath);
            }
        }
        else if (_loadedPreviewPath is not null)
        {
            _loadedPreviewPath = null;
            if (_previewPlayer is not null)
            {
                _previewPlayer.Source = null;
            }
        }
    }

    private ScrollViewer EnsureResultPanel()
    {
        if (ResultPanel is not null)
        {
            return ResultPanel;
        }

        // Realizes the x:Load="False" ResultPanel subtree on first use.
        return (ScrollViewer)FindName(nameof(ResultPanel))!;
    }

    private MediaPlayerElement EnsurePreviewPlayer()
    {
        if (_previewPlayer is not null)
        {
            return _previewPlayer;
        }

        _previewPlayer = new MediaPlayerElement
        {
            AreTransportControlsEnabled = true,
            AutoPlay = false,
        };
        PreviewHost.Child = _previewPlayer;
        return _previewPlayer;
    }

    private async Task LoadPreviewAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            if (!_isActive || !string.Equals(_loadedPreviewPath, path, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            EnsurePreviewPlayer().Source = Windows.Media.Core.MediaSource.CreateFromStorageFile(file);
        }
        catch
        {
            if (_isActive && string.Equals(_loadedPreviewPath, path, StringComparison.OrdinalIgnoreCase) && _previewPlayer is not null)
            {
                _previewPlayer.Source = null;
            }
        }
    }

    private void BindResultPanel(CompressionResult result)
    {
        OriginalNameText.Text = result.Source.FileName;
        CompressedNameText.Text = result.Output.FileName;
        OriginalFormatText.Text = result.Source.CodecFormatDisplay;
        CompressedFormatText.Text = result.Output.CodecFormatDisplay;
        OriginalResolutionText.Text = result.Source.ResolutionDisplay;
        CompressedResolutionText.Text = result.Output.ResolutionDisplay;
        OriginalDurationText.Text = result.Source.DurationClockDisplay;
        CompressedDurationText.Text = result.Output.DurationClockDisplay;
        OriginalSizeText.Text = result.Source.FileSizeDisplay;
        CompressedSizeText.Text = result.Output.FileSizeDisplay;

        RatioStatText.Text = result.CompressionRatioDisplay;
        SavedStatText.Text = $"{result.BytesSavedDisplay} smaller";
        TimeStatText.Text = VideoFile.FormatDurationClock(result.Elapsed);
        SpeedStatText.Text = result.AverageSpeedDisplay;
    }

    private async void StartCompressionButton_Click(object sender, RoutedEventArgs e)
    {
        SyncAdvancedFieldsToViewModel();

        if (ViewModel.ValidateAdvancedOverrides() is not null)
        {
            await ViewModel.StartCompressionAsync();
            return;
        }

        var outputPath = ViewModel.GetPlannedOutputPath();
        if (outputPath is not null && File.Exists(outputPath))
        {
            var overwrite = new ContentDialog
            {
                Title = "Replace existing file?",
                Content = $"A file already exists at:\n{outputPath}\n\nReplace it?",
                PrimaryButtonText = "Replace",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot,
            };

            if (await overwrite.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        UiSoundService.Play(UiSoundName.Loading);
        await ViewModel.StartCompressionAsync();
    }

    private void UpdateStatusInfoBar()
    {
        if (ViewModel.HasError)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Error;
            StatusInfoBar.Title = "Something went wrong";
            StatusInfoBar.Message = ViewModel.ErrorMessage ?? string.Empty;
            StatusInfoBar.IsOpen = true;
            StatusInfoBarActionButton.Content = ViewModel.ErrorActionLabel ?? string.Empty;
            StatusInfoBarActionButton.Visibility = ViewModel.HasErrorAction
                ? Visibility.Visible
                : Visibility.Collapsed;
            return;
        }

        if (ViewModel.HasInfo)
        {
            StatusInfoBar.Severity = InfoBarSeverity.Informational;
            StatusInfoBar.Title = string.Empty;
            StatusInfoBar.Message = ViewModel.InfoMessage ?? string.Empty;
            StatusInfoBar.IsOpen = true;
            StatusInfoBarActionButton.Visibility = Visibility.Collapsed;
            return;
        }

        StatusInfoBar.IsOpen = false;
        StatusInfoBarActionButton.Visibility = Visibility.Collapsed;
    }

    private void StatusInfoBar_CloseButtonClick(InfoBar sender, object args)
    {
        ViewModel.ClearStatusMessages();
    }

    private async void StatusInfoBarActionButton_Click(object sender, RoutedEventArgs e)
    {
        var action = ViewModel.ErrorActionLabel;
        ViewModel.ClearStatusMessages();

        if (string.Equals(action, UserFacingErrors.ActionBrowse, StringComparison.Ordinal))
        {
            await PickSourceFileAsync();
            return;
        }

        if (string.Equals(action, UserFacingErrors.ActionRetry, StringComparison.Ordinal)
            && ViewModel.CanStartCompression)
        {
            UiSoundService.Play(UiSoundName.Loading);
            await ViewModel.StartCompressionAsync();
        }
    }

    private void CancelCompressionButton_Click(object sender, RoutedEventArgs e)
    {
        UiSoundService.Play(UiSoundName.Droplet);
        ViewModel.CancelCompression();
    }

    private void CompressAnotherButton_Click(object sender, RoutedEventArgs e)
    {
        UiSoundService.Play(UiSoundName.Droplet);
        ViewModel.CompressAnother();
        SyncPresetFromViewModel();
        SyncAdvancedFieldsFromViewModel();
        SyncFormatFromViewModel();
    }

    private void OpenOutputFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Result is null)
        {
            return;
        }

        UiSoundService.Play(UiSoundName.Release);
        Process.Start(new ProcessStartInfo
        {
            FileName = ViewModel.Result.OutputPath,
            UseShellExecute = true,
        });
    }

    private void OpenOutputFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Result is null)
        {
            return;
        }

        UiSoundService.Play(UiSoundName.Release);
        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{ViewModel.Result.OutputPath}\"",
            UseShellExecute = true,
        });
    }

    private async void ChooseOutputFolderButton_Click(object sender, RoutedEventArgs e)
    {
        UiSoundService.Play(UiSoundName.Release);
        var picker = new FolderPicker();
        InitializePicker(picker);
        picker.FileTypeFilter.Add("*");
        var folder = await picker.PickSingleFolderAsync();
        if (folder is not null)
        {
            ViewModel.OutputDirectoryOverride = folder.Path;
        }
    }

    private void SyncFormatFromViewModel()
    {
        _suppressFormatToggle = true;
        Mp4FormatButton.IsChecked = ViewModel.OutputFormat == OutputFormat.Mp4;
        MkvFormatButton.IsChecked = ViewModel.OutputFormat == OutputFormat.Mkv;
        WebmFormatButton.IsChecked = ViewModel.OutputFormat == OutputFormat.WebM;
        WebmFormatButton.IsEnabled = ViewModel.VideoCodec != VideoCodec.H264;
        _suppressFormatToggle = false;
    }

    private void SyncCodecFromViewModel()
    {
        _suppressCodecToggle = true;
        Av1CodecButton.IsChecked = ViewModel.VideoCodec == VideoCodec.Av1;
        H264CodecButton.IsChecked = ViewModel.VideoCodec == VideoCodec.H264;
        H264CodecButton.IsEnabled = ViewModel.OutputFormat != OutputFormat.WebM;
        _suppressCodecToggle = false;
    }

    private void FormatRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressFormatToggle || sender is not RadioButton checkedButton)
        {
            return;
        }

        UiSoundService.Play(UiSoundName.Press);
        ViewModel.OutputFormat = checkedButton.Tag?.ToString() switch
        {
            "Mp4" => OutputFormat.Mp4,
            "Mkv" => OutputFormat.Mkv,
            "WebM" => OutputFormat.WebM,
            _ => throw new ArgumentOutOfRangeException(nameof(checkedButton), checkedButton.Tag, "Unknown output format."),
        };
        SyncCodecFromViewModel();
        SyncFormatFromViewModel();
    }

    private void CodecRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressCodecToggle || sender is not RadioButton checkedButton)
        {
            return;
        }

        UiSoundService.Play(UiSoundName.Press);
        ViewModel.VideoCodec = checkedButton.Tag?.ToString() switch
        {
            "Av1" => VideoCodec.Av1,
            "H264" => VideoCodec.H264,
            _ => throw new ArgumentOutOfRangeException(nameof(checkedButton), checkedButton.Tag, "Unknown video codec."),
        };
        SyncFormatFromViewModel();
        SyncCodecFromViewModel();
    }

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPresetSync || PresetComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string tag)
        {
            return;
        }

        UiSoundService.Play(UiSoundName.Press);
        ViewModel.Preset = tag switch
        {
            "Ultra" => CompressionPreset.Ultra,
            "EightMB" => CompressionPreset.EightMB,
            "Balanced" => CompressionPreset.Balanced,
            _ => throw new ArgumentOutOfRangeException(nameof(tag), tag, "Unknown compression preset."),
        };
    }

    private void AdvancedExpander_Expanding(Expander sender, ExpanderExpandingEventArgs args) =>
        UiSoundService.Play(UiSoundName.Bloom);

    private void AdvancedExpander_Collapsed(Expander sender, ExpanderCollapsedEventArgs args) =>
        UiSoundService.Play(UiSoundName.Droplet);

    private void KeepAudioToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressKeepAudioSound)
        {
            return;
        }

        UiSoundService.Play(UiSoundName.Toggle);
    }

    private void DropZone_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = DataPackageOperation.None;

        if (!ViewModel.IsDropZoneEnabled)
        {
            return;
        }

        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
        }
    }

    private async void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (!ViewModel.IsDropZoneEnabled || !e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        var items = await e.DataView.GetStorageItemsAsync();
        if (items.Count == 0 || items[0] is not StorageFile file)
        {
            return;
        }

        await ViewModel.LoadFileFromPathAsync(file.Path);
    }

    private void DropZone_PointerEntered(object sender, PointerRoutedEventArgs e)
    {
        if (!ViewModel.IsDropZoneEnabled)
        {
            return;
        }

        ApplyDropZoneVisualState(isHovered: true);
    }

    private void DropZone_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        ApplyDropZoneVisualState(isHovered: false);
    }

    private void ApplyDropZoneVisualState(bool isHovered)
    {
        DropZoneOutline.Stroke = isHovered ? DropZoneHoverStroke : DropZoneDefaultStroke;
        DropZoneOutline.Fill = isHovered ? DropZoneHoverFill : DropZoneDefaultFill;
    }

    private async void BrowseFilesButton_Click(object sender, RoutedEventArgs e)
    {
        UiSoundService.Play(UiSoundName.Press);
        await PickSourceFileAsync();
    }

    private async void DropZone_Tapped(object sender, TappedRoutedEventArgs e) => await PickSourceFileAsync();

    private async Task PickSourceFileAsync()
    {
        if (!ViewModel.IsDropZoneEnabled)
        {
            return;
        }

        var picker = new FileOpenPicker();
        InitializePicker(picker);

        foreach (var extension in VideoFileFormats.Extensions)
        {
            picker.FileTypeFilter.Add(extension);
        }

        var file = await picker.PickSingleFileAsync();
        if (file is not null)
        {
            await ViewModel.LoadFileFromPathAsync(file.Path);
        }
    }

    private void RemoveFileButton_Click(object sender, RoutedEventArgs e)
    {
        UiSoundService.Play(UiSoundName.Droplet);
        ViewModel.ClearSourceFile();
    }

    private static void InitializePicker(object picker)
    {
        if (App.MainWindow is null)
        {
            throw new InvalidOperationException("Main window is not available.");
        }

        var hwnd = WindowNative.GetWindowHandle(App.MainWindow);
        InitializeWithWindow.Initialize(picker, hwnd);
    }
}
