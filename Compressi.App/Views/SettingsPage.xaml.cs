using Compressi.Core.Models;
using Compressi_App.Services;
using Compressi_App.Services.UiSounds;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Compressi_App.Views;

public sealed partial class SettingsPage : Page, IAppPage
{
    private bool _isActive;
    private bool _formLoaded;
    private bool _suppressToggleSound;
    private bool _suppressVolumePreview;
    private bool _suppressDirty;
    private bool _isDirty;

    public SettingsPage()
    {
        InitializeComponent();
    }

    public void Activate()
    {
        _isActive = true;
        if (!_formLoaded)
        {
            _formLoaded = true;
            LoadFromViewModel();
            if (App.SettingsViewModel.LoadedFromDefaultsAfterError)
            {
                ShowInfo(
                    "Settings file was unreadable, so defaults were loaded. Save to create a new settings file.",
                    InfoBarSeverity.Warning);
            }
        }
        else
        {
            BindEncoderLabels();
            BindNotificationAvailability();
        }

        if (App.SettingsViewModel.NeedsEncoderDetection)
        {
            _ = EnsureEncodersAsync();
        }
    }

    public void Deactivate()
    {
        _isActive = false;
    }

    public async Task<bool> ConfirmLeaveAsync()
    {
        if (!_isDirty)
        {
            return true;
        }

        var dialog = new ContentDialog
        {
            Title = "Unsaved settings",
            Content = "You have unsaved changes. Save them before leaving?",
            PrimaryButtonText = "Save",
            SecondaryButtonText = "Discard",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        switch (result)
        {
            case ContentDialogResult.Primary:
                return SaveSettings();
            case ContentDialogResult.Secondary:
                DiscardChanges();
                return true;
            default:
                return false;
        }
    }

    private async Task EnsureEncodersAsync()
    {
        await App.SettingsViewModel.EnsureEncoderDetectionAsync();
        if (_isActive)
        {
            BindEncoderLabels();
        }
    }

    private void LoadFromViewModel()
    {
        var vm = App.SettingsViewModel;
        _suppressDirty = true;

        OutputFolderBox.Text = vm.DefaultOutputFolder ?? string.Empty;
        DefaultPresetBox.SelectedIndex = vm.DefaultPreset switch
        {
            CompressionPreset.Ultra => 0,
            CompressionPreset.EightMB => 1,
            CompressionPreset.Balanced => 2,
            _ => 2,
        };

        _suppressToggleSound = true;
        HardwareToggle.IsOn = vm.HardwareAcceleration;
        NotifyToggle.IsOn = vm.NotifyOnCompletion;
        UiSoundsToggle.IsOn = vm.UiSoundsEnabled;
        _suppressToggleSound = false;

        ThreadSlider.Value = vm.ThreadCount;
        ThreadCountLabel.Text = vm.ThreadCountDisplay;
        ThemeBox.SelectedIndex = vm.Theme switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0,
        };

        _suppressVolumePreview = true;
        UiSoundVolumeSlider.Value = vm.UiSoundVolume;
        UiSoundVolumeLabel.Text = vm.UiSoundVolumeDisplay;
        _suppressVolumePreview = false;

        BindEncoderLabels();
        BindNotificationAvailability();
        _suppressDirty = false;
        _isDirty = false;
    }

    private void BindEncoderLabels()
    {
        var vm = App.SettingsViewModel;
        CpuEncoderText.Text = $"CPU: {vm.DetectedCpuEncoder}";
        GpuEncoderText.Text = $"GPU: {vm.DetectedGpuEncoder}";
    }

    private void BindNotificationAvailability()
    {
        var available = CompletionNotificationService.IsAvailable;
        NotifyToggle.IsEnabled = available;
        NotifyUnavailableText.Visibility = available ? Visibility.Collapsed : Visibility.Visible;
        if (!available)
        {
            _suppressToggleSound = true;
            NotifyToggle.IsOn = false;
            _suppressToggleSound = false;
        }
    }

    private void MarkDirty()
    {
        if (!_suppressDirty)
        {
            _isDirty = true;
        }
    }

    private void OutputFolderBox_TextChanged(object sender, TextChangedEventArgs e) => MarkDirty();

    private void DefaultPresetBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => MarkDirty();

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => MarkDirty();

    private void ThreadSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        App.SettingsViewModel.ThreadCount = (int)e.NewValue;
        ThreadCountLabel.Text = App.SettingsViewModel.ThreadCountDisplay;
        MarkDirty();
    }

    private void UiSoundVolumeSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        var volume = (int)e.NewValue;
        App.SettingsViewModel.UiSoundVolume = volume;
        UiSoundVolumeLabel.Text = App.SettingsViewModel.UiSoundVolumeDisplay;
        UiSoundService.VolumePercent = volume;
        MarkDirty();

        if (_suppressVolumePreview || !_formLoaded)
        {
            return;
        }

        var wasEnabled = UiSoundService.IsEnabled;
        UiSoundService.IsEnabled = true;
        UiSoundService.Play(UiSoundName.Tick);
        UiSoundService.IsEnabled = wasEnabled;
    }

    private void HardwareToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleSound)
        {
            return;
        }

        UiSoundService.Play(UiSoundName.Toggle);
        MarkDirty();
    }

    private void NotifyToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleSound)
        {
            return;
        }

        UiSoundService.Play(UiSoundName.Toggle);
        MarkDirty();
    }

    private void UiSoundsToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_suppressToggleSound)
        {
            return;
        }

        var enabled = UiSoundsToggle.IsOn;
        UiSoundService.IsEnabled = true;
        UiSoundService.Play(UiSoundName.Toggle);
        UiSoundService.IsEnabled = enabled;
        MarkDirty();
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (SaveSettings())
        {
            UiSoundService.Play(UiSoundName.Success);
        }
    }

    private bool SaveSettings()
    {
        var vm = App.SettingsViewModel;
        vm.DefaultOutputFolder = string.IsNullOrWhiteSpace(OutputFolderBox.Text) ? null : OutputFolderBox.Text.Trim();
        vm.DefaultPreset = DefaultPresetBox.SelectedIndex switch
        {
            0 => CompressionPreset.Ultra,
            1 => CompressionPreset.EightMB,
            _ => CompressionPreset.Balanced,
        };
        vm.HardwareAcceleration = HardwareToggle.IsOn;
        vm.ThreadCount = (int)ThreadSlider.Value;
        vm.Theme = ThemeBox.SelectedIndex switch
        {
            1 => "Light",
            2 => "Dark",
            _ => "System",
        };
        vm.NotifyOnCompletion = NotifyToggle.IsEnabled && NotifyToggle.IsOn;
        vm.UiSoundsEnabled = UiSoundsToggle.IsOn;
        vm.UiSoundVolume = (int)UiSoundVolumeSlider.Value;

        if (!vm.TrySave(out var errorMessage))
        {
            ShowInfo(errorMessage ?? "Couldn't save settings.", InfoBarSeverity.Error);
            return false;
        }

        App.CompressViewModel.ReloadSettings();
        _isDirty = false;
        ShowInfo("Settings saved.", InfoBarSeverity.Success);
        return true;
    }

    private void DiscardChanges()
    {
        App.SettingsViewModel.Reload();
        UiSoundService.IsEnabled = App.SettingsViewModel.UiSoundsEnabled;
        UiSoundService.VolumePercent = App.SettingsViewModel.UiSoundVolume;
        ThemeService.ApplyTheme(App.SettingsViewModel.Theme);
        LoadFromViewModel();
        SettingsInfoBar.IsOpen = false;
    }

    private void ShowInfo(string message, InfoBarSeverity severity)
    {
        SettingsInfoBar.Severity = severity;
        SettingsInfoBar.Title = severity switch
        {
            InfoBarSeverity.Success => "Saved",
            InfoBarSeverity.Error => "Couldn't save",
            InfoBarSeverity.Warning => "Settings reset",
            _ => string.Empty,
        };
        SettingsInfoBar.Message = message;
        SettingsInfoBar.IsOpen = true;
    }
}
