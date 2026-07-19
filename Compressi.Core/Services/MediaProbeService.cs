using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Compressi.Core.Models;

namespace Compressi.Core.Services;

public sealed class MediaProbeService : IMediaProbeService
{
    private readonly string _ffprobePath;
    private readonly ConcurrentDictionary<string, ProbeCacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);

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
                "Compressi couldn't find its video engine. Reinstall the app to restore it.",
                _ffprobePath);
        }

        if (!VideoFileFormats.IsSupportedExtension(Path.GetExtension(filePath)))
        {
            throw new InvalidOperationException("The selected file is not a supported video format.");
        }

        var info = new FileInfo(filePath);
        var stamp = new ProbeStamp(info.Length, info.LastWriteTimeUtc.Ticks);
        if (_cache.TryGetValue(filePath, out var cached) && cached.Stamp == stamp)
        {
            return cached.Video;
        }

        var output = await RunProcessAsync(_ffprobePath, filePath, cancellationToken).ConfigureAwait(false);
        var video = FfprobeJsonParser.Parse(filePath, output);
        _cache[filePath] = new ProbeCacheEntry(stamp, video);
        return video;
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
            throw new InvalidOperationException(
                "Couldn't analyze this video. Try another file or a different format.");
        }

        return await outputTask.ConfigureAwait(false);
    }

    private readonly record struct ProbeStamp(long Length, long LastWriteTicks);

    private sealed record ProbeCacheEntry(ProbeStamp Stamp, VideoFile Video);
}
