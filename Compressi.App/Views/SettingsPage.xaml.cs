using Compressi.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Compressi_App.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadFromViewModel();
    }

    private void LoadFromViewModel()
    {
        var vm = App.SettingsViewModel;
        vm.RefreshEncoderDetection();

        OutputFolderBox.Text = vm.DefaultOutputFolder ?? string.Empty;
        DefaultPresetBox.SelectedIndex = vm.DefaultPreset switch
        {
            CompressionPreset.Ultra => 0,
            CompressionPreset.EightMB => 1,
            CompressionPreset.Balanced => 2,
            _ => 2,
        };
        HardwareToggle.IsOn = vm.HardwareAcceleration;
        ThreadSlider.Value = vm.ThreadCount;
        ThreadCountLabel.Text = vm.ThreadCountDisplay;
        ThemeBox.SelectedIndex = vm.Theme switch
        {
            "Light" => 1,
            "Dark" => 2,
            _ => 0,
        };
        NotifyToggle.IsOn = vm.NotifyOnCompletion;
        CpuEncoderText.Text = $"CPU: {vm.DetectedCpuEncoder}";
        GpuEncoderText.Text = $"GPU: {vm.DetectedGpuEncoder}";
    }

    private void ThreadSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        App.SettingsViewModel.ThreadCount = (int)e.NewValue;
        ThreadCountLabel.Text = App.SettingsViewModel.ThreadCountDisplay;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
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
        vm.NotifyOnCompletion = NotifyToggle.IsOn;
        vm.Save();

        App.CompressViewModel.ReloadSettings();
    }
}
