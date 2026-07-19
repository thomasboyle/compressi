using System.Diagnostics;
using Compressi.Core.Services;
using Microsoft.UI.Xaml.Controls;

namespace Compressi_App.Views;

public sealed partial class AboutPage : Page, IAppPage
{
    private static string? _cachedFfmpegBlurb;
    private bool _isActive;
    private bool _versionBound;

    public AboutPage()
    {
        InitializeComponent();
    }

    public void Activate()
    {
        _isActive = true;
        if (!_versionBound)
        {
            _versionBound = true;
            VersionText.Text = "Version 1.0.9";
        }

        if (_cachedFfmpegBlurb is not null)
        {
            FfmpegText.Text = _cachedFfmpegBlurb;
            return;
        }

        FfmpegText.Text = "Detecting FFmpeg...";
        _ = LoadFfmpegBlurbAsync();
    }

    public void Deactivate()
    {
        _isActive = false;
    }

    private async Task LoadFfmpegBlurbAsync()
    {
        var blurb = await Task.Run(QueryFfmpegVersion).ConfigureAwait(true);
        _cachedFfmpegBlurb = blurb;
        if (_isActive)
        {
            FfmpegText.Text = blurb;
        }
    }

    private static string QueryFfmpegVersion()
    {
        try
        {
            var path = FfmpegToolPaths.GetFfmpegPath();
            if (!File.Exists(path))
            {
                return "FFmpeg: not bundled";
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = "-version",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return "FFmpeg: unavailable";
            }

            var firstLine = process.StandardOutput.ReadLine();
            process.WaitForExit();
            var cpu = FfmpegEncoderCatalog.GetCpuAv1Encoder();
            var gpu = FfmpegEncoderCatalog.GetPreferredGpuEncoder() ?? "none";
            return $"{firstLine}\nCPU encoder: {cpu}\nGPU encoder: {gpu}";
        }
        catch
        {
            return "FFmpeg: unavailable";
        }
    }
}
