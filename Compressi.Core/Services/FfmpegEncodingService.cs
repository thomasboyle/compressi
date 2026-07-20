using System.Diagnostics;
using System.Text;
using Compressi.Core.Models;

namespace Compressi.Core.Services;

public sealed class FfmpegEncodingService : IEncodingService
{
    private const int StderrCaptureMaxChars = 65_536;

    private static readonly string[] PassLogSuffixes = ["-0.log", "-0.log.mbtree", ".log", ".log.mbtree"];
    // Coalesce progress so UI/threadpool aren't flooded by per-key ffmpeg lines.
    private static readonly long ProgressReportIntervalTicks = Stopwatch.Frequency / 10;

    private readonly string _ffmpegPath;

    public FfmpegEncodingService()
        : this(FfmpegToolPaths.GetFfmpegPath())
    {
    }

    public FfmpegEncodingService(string ffmpegPath)
    {
        _ffmpegPath = ffmpegPath;
    }

    public async Task<CompressionResult> EncodeAsync(
        CompressionJob job,
        IProgress<EncodingProgressState>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(_ffmpegPath))
        {
            throw new FileNotFoundException(
                "ffmpeg was not found. Ensure ffmpeg binaries are placed in Assets/ffmpeg.",
                _ffmpegPath);
        }

        try
        {
            return await EncodeCoreAsync(job, progress, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ShouldFallbackToCpu(job, ex))
        {
            var cpuJob = CreateCpuFallbackJob(job);
            return await EncodeCoreAsync(cpuJob, progress, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<CompressionResult> EncodeCoreAsync(
        CompressionJob job,
        IProgress<EncodingProgressState>? progress,
        CancellationToken cancellationToken)
    {
        // Resolve the encode plan off the UI thread (CPU-only 8 MB plans are non-trivial).
        var plan = await Task.Run(() => CompressionPresetResolver.Resolve(job), cancellationToken)
            .ConfigureAwait(false);
        var startedAt = DateTimeOffset.UtcNow;
        var passCount = plan.Passes.Count;
        var passIndex = 0;

        try
        {
            foreach (var pass in plan.Passes)
            {
                passIndex++;
                var passProgress = new PassProgressAdapter(progress, passIndex, passCount);
                await RunPassAsync(pass, job.Source.Duration, passProgress, cancellationToken).ConfigureAwait(false);
            }

            if (!File.Exists(plan.OutputPath))
            {
                throw new InvalidOperationException("Encoding finished but the output file was not created.");
            }

            var outputLengthBytes = new FileInfo(plan.OutputPath).Length;

            if (job.Preset == CompressionPreset.EightMB
                && plan.TargetVideoBitrateKbps is int targetBitrate
                && outputLengthBytes > EncodingConstants.EightMbTargetBytes)
            {
                var adjusted = EightMbBitrateResolver.AdjustBitrateForOvershoot(targetBitrate, outputLengthBytes);
                await RunEightMbCorrectivePassAsync(job, plan, adjusted, progress, cancellationToken)
                    .ConfigureAwait(false);

                if (!File.Exists(plan.OutputPath))
                {
                    throw new InvalidOperationException("Encoding finished but the output file was not created.");
                }

                outputLengthBytes = new FileInfo(plan.OutputPath).Length;
            }

            var output = EncodeOutputMetadata.Create(job, plan, outputLengthBytes);
            var elapsed = DateTimeOffset.UtcNow - startedAt;
            var bytesSaved = Math.Max(0, job.Source.FileSizeBytes - output.FileSizeBytes);
            var ratio = job.Source.FileSizeBytes > 0
                ? (1 - (double)output.FileSizeBytes / job.Source.FileSizeBytes) * 100
                : 0;
            var avgSpeed = elapsed.TotalSeconds > 0
                ? job.Source.FileSizeBytes / (1024d * 1024d) / elapsed.TotalSeconds
                : 0;

            progress?.Report(new EncodingProgressState
            {
                ProgressPercent = 100,
                IsFinished = true,
                OutputSizeBytes = output.FileSizeBytes,
            });

            return new CompressionResult
            {
                Source = job.Source,
                Output = output,
                OutputPath = plan.OutputPath,
                Elapsed = elapsed,
                AverageSpeedMegabytesPerSecond = avgSpeed,
                CompressionRatioPercent = ratio,
                BytesSaved = bytesSaved,
                InfoNote = job.EncodingInfoNote,
            };
        }
        finally
        {
            foreach (var pass in plan.Passes)
            {
                CleanupPassLogs(pass.PassLogFilePrefix);
            }
        }
    }

    private static bool ShouldFallbackToCpu(CompressionJob job, Exception ex)
    {
        if (!job.HardwareAccelerationEnabled || string.IsNullOrWhiteSpace(job.GpuEncoder))
        {
            return false;
        }

        return GpuEncoderOpenFailure.IsGpuEncoderOpenFailure(ex.Message);
    }

    private static CompressionJob CreateCpuFallbackJob(CompressionJob job)
    {
        var note = string.IsNullOrWhiteSpace(job.EncodingInfoNote)
            ? "GPU encoder unavailable; using CPU encoding."
            : $"{job.EncodingInfoNote} GPU encoder unavailable; using CPU encoding.";

        return new CompressionJob
        {
            Source = job.Source,
            Preset = job.Preset,
            Format = job.Format,
            Advanced = job.Advanced,
            ThreadCount = job.ThreadCount,
            HardwareAccelerationEnabled = false,
            GpuEncoder = null,
            EncodingInfoNote = note,
        };
    }

    private async Task RunEightMbCorrectivePassAsync(
        CompressionJob job,
        FfmpegEncodePlan originalPlan,
        int adjustedBitrateKbps,
        IProgress<EncodingProgressState>? progress,
        CancellationToken cancellationToken)
    {
        // Single constrained pass from frozen plan dims/fps/audio — avoid a second full 2-pass.
        var corrective = CompressionPresetResolver.BuildEightMbCorrectivePass(job, originalPlan, adjustedBitrateKbps);
        await RunPassAsync(corrective, job.Source.Duration, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task RunPassAsync(
        FfmpegEncodePass pass,
        TimeSpan totalDuration,
        IProgress<EncodingProgressState>? progress,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _ffmpegPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in pass.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true,
        };

        ProcessPriorityClass? previousPriority = null;
        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start ffmpeg.");
        }

        using var cancellationRegistration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best effort cancellation.
            }
        });

        // Bound stderr so long encodes cannot retain an unbounded log in the heap.
        var errorTask = ReadStderrBoundedAsync(process.StandardError, StderrCaptureMaxChars);

        try
        {
            try
            {
                previousPriority = process.PriorityClass;
                // BelowNormal keeps the WinUI thread responsive during CPU-only multi-pass encodes.
                process.PriorityClass = ProcessPriorityClass.BelowNormal;
            }
            catch
            {
                // Best-effort priority adjustment.
            }

            var state = new EncodingProgressState();
            var stdout = process.StandardOutput;
            var lastReportTimestamp = 0L;
            var hasPendingReport = false;

            while (!stdout.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var line = await stdout.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (!FfmpegProgressParser.ApplyLine(line, totalDuration, state) || progress is null)
                {
                    continue;
                }

                if (state.IsFinished || ShouldReportProgress(ref lastReportTimestamp))
                {
                    progress.Report(state);
                    hasPendingReport = false;
                }
                else
                {
                    hasPendingReport = true;
                }
            }

            if (hasPendingReport && progress is not null)
            {
                progress.Report(state);
            }

            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }

            if (process.ExitCode != 0)
            {
                var error = await errorTask.ConfigureAwait(false);
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(error)
                    ? "ffmpeg failed during encoding."
                    : error.Trim());
            }
        }
        finally
        {
            if (previousPriority.HasValue && !process.HasExited)
            {
                try
                {
                    process.PriorityClass = previousPriority.Value;
                }
                catch
                {
                    // Process may already be gone.
                }
            }

            try
            {
                await errorTask.ConfigureAwait(false);
            }
            catch
            {
                // Best effort drain.
            }
        }
    }

    private static async Task<string> ReadStderrBoundedAsync(StreamReader reader, int maxChars)
    {
        var buffer = new char[4096];
        var builder = new StringBuilder(Math.Min(maxChars, 4096));

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory()).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            builder.Append(buffer, 0, read);
            if (builder.Length > maxChars)
            {
                builder.Remove(0, builder.Length - maxChars);
            }
        }

        return builder.ToString();
    }

    private static bool ShouldReportProgress(ref long lastReportTimestamp)
    {
        var now = Stopwatch.GetTimestamp();
        if (lastReportTimestamp != 0 && now - lastReportTimestamp < ProgressReportIntervalTicks)
        {
            return false;
        }

        lastReportTimestamp = now;
        return true;
    }

    private static void CleanupPassLogs(string? passLogFilePrefix)
    {
        if (string.IsNullOrWhiteSpace(passLogFilePrefix))
        {
            return;
        }

        foreach (var suffix in PassLogSuffixes)
        {
            TryDelete(passLogFilePrefix + suffix);
        }

        TryDelete(passLogFilePrefix);
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

    private sealed class PassProgressAdapter : IProgress<EncodingProgressState>
    {
        private readonly IProgress<EncodingProgressState>? _inner;
        private readonly EncodingProgressState _scratch = new();
        private readonly int _passCount;
        private readonly double _passStart;
        private readonly double _passShare;

        public PassProgressAdapter(IProgress<EncodingProgressState>? inner, int passIndex, int passCount)
        {
            _inner = inner;
            _passCount = passCount;
            _passStart = passCount > 0 ? (passIndex - 1) * 100d / passCount : 0;
            _passShare = passCount > 0 ? 100d / passCount : 100;
        }

        public void Report(EncodingProgressState value)
        {
            if (_inner is null)
            {
                return;
            }

            if (_passCount <= 1 || value.ProgressPercent is null)
            {
                _inner.Report(value);
                return;
            }

            _scratch.OutTime = value.OutTime;
            _scratch.OutputSizeBytes = value.OutputSizeBytes;
            _scratch.SpeedMultiplier = value.SpeedMultiplier;
            _scratch.IsFinished = value.IsFinished;
            _scratch.ProgressPercent = Math.Min(99, _passStart + value.ProgressPercent.Value * _passShare / 100d);
            _inner.Report(_scratch);
        }
    }
}
