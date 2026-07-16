using System.Diagnostics;
using System.Text;
using Compressi.Core.Models;

namespace Compressi.Core.Services;

public sealed class MediaProbeService : IMediaProbeService
{
    private readonly string _ffprobePath;

    public MediaProbeService()
        : this(FfmpegToolPaths.GetFfprobePath())
    {
    }

    public MediaProbeService(string ffprobePath)
    {
        _ffprobePath = ffprobePath;
    }

    public async Task<VideoFile> ProbeAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("The selected video file could not be found.", filePath);
        }

        if (!File.Exists(_ffprobePath))
        {
            throw new FileNotFoundException(
                "ffprobe was not found. Ensure ffmpeg binaries are placed in Assets/ffmpeg.",
                _ffprobePath);
        }

        if (!VideoFileFormats.IsSupportedExtension(Path.GetExtension(filePath)))
        {
            throw new InvalidOperationException("The selected file is not a supported video format.");
        }

        var output = await RunProcessAsync(_ffprobePath, filePath, cancellationToken).ConfigureAwait(false);
        return FfprobeJsonParser.Parse(filePath, output);
    }

    private static async Task<string> RunProcessAsync(
        string executable,
        string filePath,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add("quiet");
        startInfo.ArgumentList.Add("-print_format");
        startInfo.ArgumentList.Add("json");
        startInfo.ArgumentList.Add("-show_format");
        startInfo.ArgumentList.Add("-show_streams");
        startInfo.ArgumentList.Add(filePath);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start ffprobe.");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync(cancellationToken)).ConfigureAwait(false);

        if (process.ExitCode != 0)
        {
            var error = await errorTask.ConfigureAwait(false);
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                ? "ffprobe failed to analyze the selected file."
                : error.Trim());
        }

        return await outputTask.ConfigureAwait(false);
    }
}
