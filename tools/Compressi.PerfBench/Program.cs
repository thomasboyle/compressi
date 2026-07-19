using System.Diagnostics;
using System.Text.Json;
using Compressi.Core.Models;
using Compressi.Core.Services;

// Core/runtime micro-benches used for Phase 1/4 baselines (no UI).
// Usage: dotnet run -c Release --project tools/Compressi.PerfBench -- [outdir]

var outDir = args.Length > 0
    ? args[0]
    : Path.Combine(Path.GetTempPath(), "compressi-perf");
Directory.CreateDirectory(outDir);

var ffmpegDir = FindFfmpegDir();
if (ffmpegDir is not null)
{
    // FfmpegToolPaths looks next to the entry assembly; copy tools beside this exe for probes.
    var dest = Path.Combine(AppContext.BaseDirectory, "Assets", "ffmpeg");
    Directory.CreateDirectory(dest);
    foreach (var name in new[] { "ffmpeg.exe", "ffprobe.exe" })
    {
        var src = Path.Combine(ffmpegDir, name);
        var dst = Path.Combine(dest, name);
        if (File.Exists(src) && !File.Exists(dst))
        {
            File.Copy(src, dst);
        }
    }
}

var results = new List<object>();
var workDir = Path.Combine(outDir, "bench-work");
Directory.CreateDirectory(workDir);

var sampleVideo = Path.Combine(workDir, "sample.mp4");
EnsureSampleVideo(sampleVideo, ffmpegDir);

var historyDb = Path.Combine(workDir, $"history-{Guid.NewGuid():N}.db");
var settingsPath = Path.Combine(workDir, $"settings-{Guid.NewGuid():N}.json");

var history = new HistoryStore(historyDb);
for (var i = 0; i < 50; i++)
{
    history.Add(new HistoryEntry
    {
        SourceName = $"clip-{i}.mp4",
        SourcePath = $@"C:\videos\clip-{i}.mp4",
        OutputPath = $@"C:\out\clip-{i}.mp4",
        Preset = CompressionPreset.Balanced,
        Format = OutputFormat.Mp4,
        Status = CompressionJobStatus.Completed,
        OriginalSizeBytes = 10_000_000 + i,
        CompressedSizeBytes = 4_000_000 + i,
        CompressionRatioPercent = 60,
        CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-i),
    });
}

results.Add(Bench("history_get_all", 20, () => _ = history.GetAll()));
results.Add(Bench("history_search", 20, () => _ = history.Search("clip-1")));
results.Add(Bench("history_add", 20, () =>
{
    history.Add(new HistoryEntry
    {
        SourceName = "bench.mp4",
        SourcePath = @"C:\videos\bench.mp4",
        OutputPath = @"C:\out\bench.mp4",
        Preset = CompressionPreset.Ultra,
        Format = OutputFormat.Mp4,
        Status = CompressionJobStatus.Completed,
        OriginalSizeBytes = 1,
        CompressedSizeBytes = 1,
        CompressionRatioPercent = 0,
        CreatedAt = DateTimeOffset.UtcNow,
    });
}));

var settingsJson = """
{"DefaultPreset":"Balanced","HardwareAcceleration":true,"ThreadCount":0,"Theme":"System","UiSoundsEnabled":true,"UiSoundVolume":50,"NotifyOnCompletion":true}
""";
File.WriteAllText(settingsPath, settingsJson);
results.Add(Bench("settings_read", 50, () => _ = File.ReadAllText(settingsPath)));
results.Add(Bench("settings_write", 50, () => File.WriteAllText(settingsPath, settingsJson)));

results.Add(Bench("encoder_catalog_cold", 1, () =>
{
    FfmpegEncoderCatalog.Refresh();
}));
results.Add(Bench("encoder_catalog_warm", 10, () =>
{
    _ = FfmpegEncoderCatalog.GetCpuAv1Encoder();
    _ = FfmpegEncoderCatalog.GetPreferredGpuEncoder();
}));

if (File.Exists(sampleVideo))
{
    var probe = new MediaProbeService();
    // Cold probe: new service each iteration (no cache).
    results.Add(Bench("media_probe_cold", 5, () =>
    {
        new MediaProbeService().ProbeAsync(sampleVideo).GetAwaiter().GetResult();
    }));

    // Warm probe: same service instance (path+mtime cache).
    results.Add(Bench("media_probe_warm", 20, () =>
    {
        probe.ProbeAsync(sampleVideo).GetAwaiter().GetResult();
    }));

    var video = probe.ProbeAsync(sampleVideo).GetAwaiter().GetResult();
    results.Add(Bench("preset_resolve_balanced", 50, () =>
        _ = CompressionPresetResolver.Resolve(MakeJob(video, CompressionPreset.Balanced))));
    results.Add(Bench("preset_resolve_ultra", 50, () =>
        _ = CompressionPresetResolver.Resolve(MakeJob(video, CompressionPreset.Ultra))));
    results.Add(Bench("preset_resolve_8mb", 50, () =>
        _ = CompressionPresetResolver.Resolve(MakeJob(video, CompressionPreset.EightMB))));
}
else
{
    results.Add(new { name = "media_probe", skipped = true, reason = "sample video unavailable" });
}

var reportPath = Path.Combine(outDir, $"core-bench-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.json");
var payload = new
{
    createdAt = DateTimeOffset.Now,
    ffmpegDir,
    sampleVideoExists = File.Exists(sampleVideo),
    results,
};
File.WriteAllText(reportPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(reportPath);
foreach (var r in results)
{
    Console.WriteLine(JsonSerializer.Serialize(r));
}

static CompressionJob MakeJob(VideoFile video, CompressionPreset preset) => new()
{
    Source = video,
    Preset = preset,
    Format = OutputFormat.Mp4,
    HardwareAccelerationEnabled = false,
    ThreadCount = Math.Max(1, Environment.ProcessorCount - 1),
    Advanced = new AdvancedEncodingOptions
    {
        OutputDirectory = Path.GetTempPath(),
    },
};

static object Bench(string name, int iterations, Action action)
{
    action();
    var times = new double[iterations];
    for (var i = 0; i < iterations; i++)
    {
        var sw = Stopwatch.StartNew();
        action();
        sw.Stop();
        times[i] = sw.Elapsed.TotalMilliseconds;
    }

    Array.Sort(times);
    var median = times[times.Length / 2];
    var mean = times.Average();
    var variance = times.Select(t => (t - mean) * (t - mean)).Average();
    return new
    {
        name,
        iterations,
        median_ms = Math.Round(median, 3),
        mean_ms = Math.Round(mean, 3),
        stdev_ms = Math.Round(Math.Sqrt(variance), 3),
        min_ms = Math.Round(times[0], 3),
        max_ms = Math.Round(times[^1], 3),
    };
}

static string? FindFfmpegDir()
{
    var root = FindRepoRoot();
    if (root is null)
    {
        return null;
    }

    var candidate = Path.Combine(root, "Compressi.App", "Assets", "ffmpeg");
    return File.Exists(Path.Combine(candidate, "ffmpeg.exe")) ? candidate : null;
}

static string? FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir.FullName, "Compressi.App", "Compressi.App.csproj")))
        {
            return dir.FullName;
        }

        dir = dir.Parent;
    }

    return null;
}

static void EnsureSampleVideo(string path, string? ffmpegDir)
{
    if (File.Exists(path))
    {
        return;
    }

    var ffmpeg = ffmpegDir is null ? null : Path.Combine(ffmpegDir, "ffmpeg.exe");
    if (ffmpeg is null || !File.Exists(ffmpeg))
    {
        return;
    }

    var psi = new ProcessStartInfo
    {
        FileName = ffmpeg,
        Arguments = $"-y -f lavfi -i color=c=black:s=320x240:d=1 -f lavfi -i sine=f=440:d=1 -shortest -c:v libx264 -pix_fmt yuv420p -c:a aac \"{path}\"",
        UseShellExecute = false,
        CreateNoWindow = true,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
    };
    using var p = Process.Start(psi);
    p?.WaitForExit(60_000);
}
