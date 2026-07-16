using Compressi.Core.Models;
using Compressi.Core.Services;

namespace Compressi.Tests;

public class FfmpegEncodingServiceIntegrationTests
{
    [Fact]
    public async Task EncodeAsync_BalancedMp4_ProducesOutputFile()
    {
        var ffmpegPath = FfmpegToolPaths.GetFfmpegPath();
        if (!File.Exists(ffmpegPath))
        {
            return;
        }

        var sourcePath = await CreateTestVideoAsync(ffmpegPath);
        var source = await new MediaProbeService().ProbeAsync(sourcePath);
        var outputDirectory = Path.GetTempPath();
        var outputBaseName = $"compressi_out_{Guid.NewGuid():N}";

        try
        {
            var job = new CompressionJob
            {
                Source = source,
                Preset = CompressionPreset.Balanced,
                Format = OutputFormat.Mp4,
                ThreadCount = Math.Min(4, Environment.ProcessorCount),
                Advanced = new AdvancedEncodingOptions
                {
                    OutputDirectory = outputDirectory,
                    OutputFilenamePattern = outputBaseName,
                },
            };

            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            var service = new FfmpegEncodingService();
            var result = await service.EncodeAsync(job, cancellationToken: cts.Token);

            Assert.True(File.Exists(result.OutputPath));
            Assert.True(result.Output.FileSizeBytes > 0);
            Assert.True(result.Elapsed > TimeSpan.Zero);

            TryDelete(result.OutputPath);
        }
        finally
        {
            TryDelete(sourcePath);
        }
    }

    private static async Task<string> CreateTestVideoAsync(string ffmpegPath)
    {
        var path = Path.Combine(Path.GetTempPath(), $"compressi_src_{Guid.NewGuid():N}.mp4");
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = $"-y -hide_banner -f lavfi -i testsrc=duration=2:size=640x360:rate=30 -c:v libx264 \"{path}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to create test video.");
        await process.WaitForExitAsync();
        if (process.ExitCode != 0 || !File.Exists(path))
        {
            throw new InvalidOperationException("Failed to create test video.");
        }

        return path;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
