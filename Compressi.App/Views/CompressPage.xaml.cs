using System.Diagnostics;
using Compressi.Core.Models;
using Compressi.Core.Services;
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

public sealed partial class CompressPage : Page
{
    private static readonly SolidColorBrush DropZoneDefaultFill = new(Color.FromArgb(0x0A, 0x00, 0x78, 0xD4));
    private static readonly SolidColorBrush DropZoneHoverFill = new(Color.FromArgb(0x18, 0x00, 0x78, 0xD4));
    private static readonly SolidColorBrush DropZoneDefaultStroke = new(Color.FromArgb(0x99, 0x00, 0x78, 0xD4));
    private static readonly SolidColorBrush DropZoneHoverStroke = new(Color.FromArgb(0xFF, 0x00, 0x78, 0xD4));

    private bool _suppressFormatToggle;
    private bool _suppressPresetSync;

    public CompressViewModel ViewModel => App.CompressViewModel;

    public CompressPage()
    {
        InitializeComponent();
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;
        SyncAdvancedFieldsFromViewModel();
        SyncFormatFromViewModel();
        UpdateUiState();
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
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
                UpdateUiState();
                return;
        }
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

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.ReloadSettings();
        SyncPresetFromViewModel();
        UpdateUiState();
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
        KeepAudioToggle.IsOn = ViewModel.KeepOriginalAudio;
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

        if (ViewModel.HasError)
        {
            ErrorText.Text = ViewModel.ErrorMessage;
            ErrorText.Visibility = Visibility.Visible;
        }
        else
        {
            ErrorText.Visibility = Visibility.Collapsed;
        }

        if (ViewModel.HasInfo)
        {
            InfoText.Text = ViewModel.InfoMessage;
            InfoText.Visibility = Visibility.Visible;
        }
        else
        {
            InfoText.Visibility = Visibility.Collapsed;
        }

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
        ResultPanel.Visibility = ViewModel.HasResult ? Visibility.Visible : Visibility.Collapsed;

        if (ViewModel.Result is { } result)
        {
            _ = LoadPreviewAsync(result.OutputPath);
            BindResultPanel(result);
        }
    }

    private async Task LoadPreviewAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            PreviewPlayer.Source = Windows.Media.Core.MediaSource.CreateFromStorageFile(file);
        }
        catch
        {
            PreviewPlayer.Source = null;
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
        await ViewModel.StartCompressionAsync();
    }

    private void CancelCompressionButton_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.CancelCompression();
    }

    private void CompressAnotherButton_Click(object sender, RoutedEventArgs e)
    {
        PreviewPlayer.Source = null;
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

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{ViewModel.Result.OutputPath}\"",
            UseShellExecute = true,
        });
    }

    private async void ChooseOutputFolderButton_Click(object sender, RoutedEventArgs e)
    {
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
        _suppressFormatToggle = false;
    }

    private void FormatRadioButton_Checked(object sender, RoutedEventArgs e)
    {
        if (_suppressFormatToggle || sender is not RadioButton checkedButton)
        {
            return;
        }

        ViewModel.OutputFormat = checkedButton.Tag?.ToString() switch
        {
            "Mp4" => OutputFormat.Mp4,
            "Mkv" => OutputFormat.Mkv,
            "WebM" => OutputFormat.WebM,
            _ => throw new ArgumentOutOfRangeException(nameof(checkedButton), checkedButton.Tag, "Unknown output format."),
        };
    }

    private void PresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressPresetSync || PresetComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string tag)
        {
            return;
        }

        ViewModel.Preset = tag switch
        {
            "Ultra" => CompressionPreset.Ultra,
            "EightMB" => CompressionPreset.EightMB,
            "Balanced" => CompressionPreset.Balanced,
            _ => throw new ArgumentOutOfRangeException(nameof(tag), tag, "Unknown compression preset."),
        };
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
