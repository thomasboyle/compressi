using System.Diagnostics;
using Compressi.Core.Services;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Compressi_App.Views;

public sealed partial class AboutPage : Page
{
    public AboutPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        VersionText.Text = "Version 1.0.0";
        FfmpegText.Text = QueryFfmpegVersion();
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
