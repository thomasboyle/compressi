using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Compressi_App.Services;

/// <summary>
/// Env-gated launch/runtime markers. Enable with COMPRESSI_PERF=1.
/// Writes JSONL lines to %TEMP%\compressi-perf\run-&lt;pid&gt;.jsonl
/// </summary>
internal static class PerfProbe
{
    private static readonly bool Enabled =
        string.Equals(Environment.GetEnvironmentVariable("COMPRESSI_PERF"), "1", StringComparison.Ordinal);

    private static readonly long StartTimestamp = Stopwatch.GetTimestamp();
    private static readonly object Gate = new();
    private static StreamWriter? _writer;
    private static string? _path;

    // Anchor StartTimestamp as early as the runtime allows (before App static field inits when possible).
    [ModuleInitializer]
    internal static void Initialize()
    {
        _ = StartTimestamp;
        if (Enabled)
        {
            Mark("module_init");
        }
    }

    public static bool IsEnabled => Enabled;

    public static void Mark(string name, string? detail = null)
    {
        if (!Enabled)
        {
            return;
        }

        var ms = ElapsedMs();
        var line = detail is null
            ? $"{{\"t_ms\":{ms.ToString("F3", CultureInfo.InvariantCulture)},\"name\":{Escape(name)}}}"
            : $"{{\"t_ms\":{ms.ToString("F3", CultureInfo.InvariantCulture)},\"name\":{Escape(name)},\"detail\":{Escape(detail)}}}";

        lock (Gate)
        {
            EnsureWriter();
            _writer!.WriteLine(line);
            _writer.Flush();
        }
    }

    public static void MarkDuration(string name, long startTimestamp, string? detail = null)
    {
        if (!Enabled)
        {
            return;
        }

        var durationMs = (Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / Stopwatch.Frequency;
        var line = detail is null
            ? $"{{\"t_ms\":{ElapsedMs().ToString("F3", CultureInfo.InvariantCulture)},\"name\":{Escape(name)},\"duration_ms\":{durationMs.ToString("F3", CultureInfo.InvariantCulture)}}}"
            : $"{{\"t_ms\":{ElapsedMs().ToString("F3", CultureInfo.InvariantCulture)},\"name\":{Escape(name)},\"duration_ms\":{durationMs.ToString("F3", CultureInfo.InvariantCulture)},\"detail\":{Escape(detail)}}}";

        lock (Gate)
        {
            EnsureWriter();
            _writer!.WriteLine(line);
            _writer.Flush();
        }
    }

    public static string? LogPath
    {
        get
        {
            if (!Enabled)
            {
                return null;
            }

            lock (Gate)
            {
                EnsureWriter();
                return _path;
            }
        }
    }

    private static double ElapsedMs() =>
        (Stopwatch.GetTimestamp() - StartTimestamp) * 1000.0 / Stopwatch.Frequency;

    private static void EnsureWriter()
    {
        if (_writer is not null)
        {
            return;
        }

        var dir = Path.Combine(Path.GetTempPath(), "compressi-perf");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, $"run-{Environment.ProcessId}.jsonl");
        _writer = new StreamWriter(new FileStream(_path, FileMode.Create, FileAccess.Write, FileShare.Read), Encoding.UTF8)
        {
            AutoFlush = true,
        };
        _writer.WriteLine($"{{\"t_ms\":0,\"name\":\"process_start\",\"detail\":{Escape(DateTimeOffset.Now.ToString("O"))}}}");
    }

    private static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\':
                    sb.Append("\\\\");
                    break;
                case '"':
                    sb.Append("\\\"");
                    break;
                case '\n':
                    sb.Append("\\n");
                    break;
                case '\r':
                    sb.Append("\\r");
                    break;
                default:
                    sb.Append(ch);
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }
}
