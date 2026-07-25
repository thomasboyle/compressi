using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Compressi.LaunchProbe;

/// <summary>
/// External ground-truth cold-launch probe: measures process create -> real pixels on screen.
/// Independent of in-app instrumentation. Detects DWM uncloak and first painted pixel.
/// </summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        // Physical-pixel coordinates are required for GetWindowRect/GetPixel to line up.
        _ = SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        var exe = GetArg(args, "--exe") ?? DefaultExe();
        var runs = int.Parse(GetArg(args, "--runs") ?? "5", CultureInfo.InvariantCulture);
        var gapSeconds = int.Parse(GetArg(args, "--gap") ?? "45", CultureInfo.InvariantCulture);
        var cold = !HasFlag(args, "--warm");
        var label = GetArg(args, "--label") ?? "run";
        var outPath = GetArg(args, "--out");
        var timeoutMs = int.Parse(GetArg(args, "--timeout") ?? "30000", CultureInfo.InvariantCulture);

        if (!File.Exists(exe))
        {
            Console.Error.WriteLine($"exe not found: {exe}");
            return 2;
        }

        if (HasFlag(args, "--diag"))
        {
            return Diagnose(exe);
        }

        Console.WriteLine($"exe   : {exe}");
        Console.WriteLine($"runs  : {runs} ({(cold ? "cold" : "warm")}) gap={gapSeconds}s");

        var results = new List<RunResult>();
        for (var i = 1; i <= runs; i++)
        {
            KillApp();
            if (cold)
            {
                FlushStandby();
                Thread.Sleep(TimeSpan.FromSeconds(i == 1 ? Math.Min(gapSeconds, 5) : gapSeconds));
            }
            else
            {
                Thread.Sleep(1200);
            }

            var r = Measure(exe, timeoutMs);
            r.Index = i;
            r.Kind = cold ? "cold" : "warm";
            results.Add(r);
            Console.WriteLine(
                $"  {r.Kind} {i,2}: hwnd={F(r.HwndMs)} uncloak={F(r.UncloakMs)} pixel={F(r.FirstPixelMs)} " +
                $"tti={F(r.MarkTti)} revealed={F(r.MarkWindowRevealed)} pid={r.Pid}");
            KillApp();
        }

        Report(label, exe, cold, results);

        if (outPath is not null)
        {
            var json = JsonSerializer.Serialize(
                new { label, exe, kind = cold ? "cold" : "warm", createdAt = DateTimeOffset.Now, runs = results },
                new JsonSerializerOptions { WriteIndented = true });
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
            File.WriteAllText(outPath, json, Encoding.UTF8);
            Console.WriteLine($"wrote {outPath}");
        }

        return 0;
    }

    private static int Diagnose(string exe)
    {
        KillApp();
        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false,
        };
        psi.Environment["COMPRESSI_PERF"] = "1";
        using var proc = Process.Start(psi)!;

        Thread.Sleep(4000);
        var hwnd = FindAppWindow(proc.Id);
        Console.WriteLine($"pid={proc.Id} hwnd=0x{hwnd:X} cloaked={IsCloaked(hwnd)} visible={IsWindowVisible(hwnd)}");
        _ = GetWindowRect(hwnd, out var rect);
        Console.WriteLine($"rect=({rect.Left},{rect.Top})-({rect.Right},{rect.Bottom})");
        Console.WriteLine($"foreground=0x{GetForegroundWindow():X}");

        var dc = GetDC(IntPtr.Zero);
        var points = BuildSamplePoints(hwnd);
        foreach (var p in points)
        {
            var c = GetPixel(dc, p.X, p.Y);
            Console.WriteLine(
                $"  ({p.X,5},{p.Y,5}) raw=0x{c:X8} r={c & 0xFF,3} g={(c >> 8) & 0xFF,3} b={(c >> 16) & 0xFF,3} paper={IsPaperColor(c)}");
        }

        ReleaseDC(IntPtr.Zero, dc);
        KillApp();
        return 0;
    }

    private static RunResult Measure(string exe, int timeoutMs)
    {
        var result = new RunResult();

        var psi = new ProcessStartInfo(exe)
        {
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            UseShellExecute = false,
        };
        if (!HasFlag(Environment.GetCommandLineArgs(), "--no-perf"))
        {
            psi.Environment["COMPRESSI_PERF"] = "1";
        }

        var sw = Stopwatch.StartNew();
        using var proc = Process.Start(psi)!;
        result.Pid = proc.Id;
        result.SpawnMs = sw.Elapsed.TotalMilliseconds;

        IntPtr hwnd = IntPtr.Zero;
        var baseline = Array.Empty<uint>();
        var samplePoints = Array.Empty<POINT>();
        var screenDc = GetDC(IntPtr.Zero);

        try
        {
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (hwnd == IntPtr.Zero)
                {
                    hwnd = FindAppWindow(proc.Id);
                    if (hwnd != IntPtr.Zero)
                    {
                        result.HwndMs = sw.Elapsed.TotalMilliseconds;
                        samplePoints = BuildSamplePoints(hwnd);
                        baseline = SamplePixels(screenDc, samplePoints);
                    }
                }

                if (hwnd != IntPtr.Zero)
                {
                    if (result.UncloakMs is null && !IsCloaked(hwnd) && IsWindowVisible(hwnd))
                    {
                        result.UncloakMs = sw.Elapsed.TotalMilliseconds;
                    }

                    if (result.FirstPixelMs is null && samplePoints.Length > 0)
                    {
                        var now = SamplePixels(screenDc, samplePoints);
                        var appLike = 0;
                        for (var i = 0; i < now.Length; i++)
                        {
                            if (now[i] != baseline[i] && IsPaperColor(now[i]))
                            {
                                appLike++;
                            }
                        }

                        // Compressi's UI is a cream paper surface; requiring most probes to match it
                        // rules out repaints of other windows losing foreground.
                        if (appLike >= (samplePoints.Length * 2 / 3) + 1)
                        {
                            result.FirstPixelMs = sw.Elapsed.TotalMilliseconds;
                        }
                    }

                    // Uncloak is the authoritative visibility signal: content is already rendered,
                    // so DWM composites it on the next vsync. The pixel probe is best-effort only
                    // (it fails when another window occludes the launched app).
                    if (result.UncloakMs is not null)
                    {
                        break;
                    }
                }

                if (proc.HasExited)
                {
                    result.Exited = true;
                    break;
                }

                Thread.SpinWait(2000);
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }

        // Let in-app marks flush before reading the perf log.
        Thread.Sleep(2600);
        ReadMarks(proc.Id, result);
        return result;
    }

    private static POINT[] BuildSamplePoints(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var rect))
        {
            return Array.Empty<POINT>();
        }

        var w = rect.Right - rect.Left;
        var h = rect.Bottom - rect.Top;
        if (w <= 40 || h <= 40)
        {
            return Array.Empty<POINT>();
        }

        var fractions = new (double X, double Y)[]
        {
            (0.20, 0.30), (0.50, 0.30), (0.80, 0.30),
            (0.20, 0.55), (0.50, 0.55), (0.80, 0.55),
            (0.20, 0.80), (0.50, 0.80), (0.80, 0.80),
        };

        var points = new POINT[fractions.Length];
        for (var i = 0; i < fractions.Length; i++)
        {
            points[i] = new POINT
            {
                X = rect.Left + (int)(w * fractions[i].X),
                Y = rect.Top + (int)(h * fractions[i].Y),
            };
        }

        return points;
    }

    // COLORREF is 0x00BBGGRR. Compressi's paper surface is around #E8DFD0.
    private static bool IsPaperColor(uint colorRef)
    {
        var r = (int)(colorRef & 0xFF);
        var g = (int)((colorRef >> 8) & 0xFF);
        var b = (int)((colorRef >> 16) & 0xFF);
        return r is >= 195 and <= 255
            && g is >= 180 and <= 252
            && b is >= 155 and <= 245
            && r >= g
            && g >= b
            && r - b >= 8;
    }

    private static uint[] SamplePixels(IntPtr dc, POINT[] points)
    {
        var values = new uint[points.Length];
        for (var i = 0; i < points.Length; i++)
        {
            values[i] = GetPixel(dc, points[i].X, points[i].Y);
        }

        return values;
    }

    private static IntPtr FindAppWindow(int pid)
    {
        var best = IntPtr.Zero;
        var bestArea = 0L;

        EnumWindows((hwnd, _) =>
        {
            _ = GetWindowThreadProcessId(hwnd, out var wpid);
            if (wpid != pid || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero)
            {
                return true;
            }

            var cls = GetClassNameOf(hwnd);
            if (!cls.Contains("WinUIDesktopWin32WindowClass", StringComparison.Ordinal))
            {
                return true;
            }

            if (!GetWindowRect(hwnd, out var rect))
            {
                return true;
            }

            var area = (long)(rect.Right - rect.Left) * (rect.Bottom - rect.Top);
            if (area > bestArea)
            {
                bestArea = area;
                best = hwnd;
            }

            return true;
        }, IntPtr.Zero);

        return bestArea > 100_000 ? best : IntPtr.Zero;
    }

    private static string GetClassNameOf(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        var len = GetClassName(hwnd, sb, sb.Capacity);
        return len > 0 ? sb.ToString(0, len) : string.Empty;
    }

    private static bool IsCloaked(IntPtr hwnd)
    {
        var cloaked = 0;
        var hr = DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out cloaked, sizeof(int));
        return hr == 0 && cloaked != 0;
    }

    private static void ReadMarks(int pid, RunResult result)
    {
        var path = Path.Combine(Path.GetTempPath(), "compressi-perf", $"run-{pid}.jsonl");
        if (!File.Exists(path))
        {
            return;
        }

        result.PerfLog = path;

        // The app still holds the log open for writing.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                using var doc = JsonDocument.Parse(line);
                var root = doc.RootElement;
                var name = root.GetProperty("name").GetString();
                var t = root.GetProperty("t_ms").GetDouble();
                result.Marks[name!] = t;
                if (root.TryGetProperty("duration_ms", out var d))
                {
                    result.Durations[name!] = d.GetDouble();
                }
            }
            catch (JsonException)
            {
            }
        }

        result.MarkModuleInit = result.Marks.TryGetValue("module_init", out var mi) ? mi : null;
        result.MarkOnLaunched = result.Marks.TryGetValue("on_launched_begin", out var ol) ? ol : null;
        result.MarkActivate = result.Marks.TryGetValue("main_window_activate", out var ac) ? ac : null;
        result.MarkTti = result.Marks.TryGetValue("tti", out var tti) ? tti : null;
        result.MarkWindowRevealed = result.Marks.TryGetValue("window_revealed", out var wr) ? wr : null;
    }

    private static void Report(string label, string exe, bool cold, List<RunResult> results)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {label} ({(cold ? "cold" : "warm")}) n={results.Count} ===");
        PrintStat("spawn (CreateProcess)     ", results.Select(r => (double?)r.SpawnMs));
        PrintStat("-> hwnd exists            ", results.Select(r => r.HwndMs));
        PrintStat("-> DWM uncloak (VISIBLE)  ", results.Select(r => r.UncloakMs));
        PrintStat("-> first pixels (if fg)   ", results.Select(r => r.FirstPixelMs));
        PrintStat("prelude spawn->module_init", results.Select(r => r.PreludeMs));
        Console.WriteLine("in-app marks (from module_init anchor):");
        PrintStat("   module_init            ", results.Select(r => r.MarkModuleInit));
        PrintStat("   on_launched_begin      ", results.Select(r => r.MarkOnLaunched));
        PrintStat("   tti                    ", results.Select(r => r.MarkTti));
        PrintStat("   main_window_activate   ", results.Select(r => r.MarkActivate));
        PrintStat("   window_revealed        ", results.Select(r => r.MarkWindowRevealed));

        var names = results.SelectMany(r => r.Durations.Keys).Distinct().ToList();
        if (names.Count > 0)
        {
            Console.WriteLine("in-app durations:");
            foreach (var n in names)
            {
                PrintStat($"   {n,-22}", results.Select(r => r.Durations.TryGetValue(n, out var v) ? v : (double?)null));
            }
        }
    }

    private static void PrintStat(string name, IEnumerable<double?> values)
    {
        var v = values.Where(x => x.HasValue).Select(x => x!.Value).OrderBy(x => x).ToArray();
        if (v.Length == 0)
        {
            Console.WriteLine($"  {name} n/a");
            return;
        }

        var mean = v.Average();
        var median = v[(v.Length - 1) / 2];
        Console.WriteLine(
            $"  {name} mean={mean,7:F1} median={median,7:F1} min={v[0],7:F1} max={v[^1],7:F1} n={v.Length}");
    }

    private static void KillApp()
    {
        foreach (var p in Process.GetProcessesByName("Compressi.App"))
        {
            try
            {
                p.Kill();
                p.WaitForExit(4000);
            }
            catch (Exception)
            {
            }
            finally
            {
                p.Dispose();
            }
        }

        Thread.Sleep(500);
    }

    private static void FlushStandby()
    {
        var candidates = new[]
        {
            Path.Combine(Path.GetTempPath(), "EmptyStandbyList.exe"),
            Path.Combine(AppContext.BaseDirectory, "EmptyStandbyList.exe"),
            @"D:\C++\Compressi\tools\EmptyStandbyList.exe",
        };

        foreach (var tool in candidates)
        {
            if (!File.Exists(tool))
            {
                continue;
            }

            foreach (var arg in new[] { "workingsets", "standbylist", "modifiedpagelist" })
            {
                try
                {
                    using var p = Process.Start(new ProcessStartInfo(tool, arg) { UseShellExecute = false });
                    p?.WaitForExit(10000);
                }
                catch (Exception)
                {
                }
            }

            return;
        }
    }

    private static string F(double? v) => v.HasValue ? v.Value.ToString("F1", CultureInfo.InvariantCulture) : "n/a";

    private static string? GetArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static bool HasFlag(string[] args, string name) =>
        args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));

    private static string DefaultExe() =>
        @"D:\C++\Compressi\Compressi.App\bin\Release\net8.0-windows10.0.26100.0\win-x64\setup-publish\Compressi.App.exe";

    private sealed class RunResult
    {
        public int Index { get; set; }
        public string Kind { get; set; } = "cold";
        public int Pid { get; set; }
        public bool Exited { get; set; }
        public double SpawnMs { get; set; }
        public double? HwndMs { get; set; }
        public double? UncloakMs { get; set; }
        public double? FirstPixelMs { get; set; }
        public double? MarkModuleInit { get; set; }
        public double? MarkOnLaunched { get; set; }
        public double? MarkActivate { get; set; }
        public double? MarkTti { get; set; }
        public double? MarkWindowRevealed { get; set; }

        /// <summary>Native host + CLR startup before the first managed mark.</summary>
        public double? PreludeMs =>
            UncloakMs is not null && MarkWindowRevealed is not null
                ? UncloakMs - MarkWindowRevealed
                : null;
        public string? PerfLog { get; set; }
        public Dictionary<string, double> Marks { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, double> Durations { get; } = new(StringComparer.Ordinal);
    }

    private const int GW_OWNER = 4;
    private const int DWMWA_CLOAKED = 14;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetWindowThreadProcessId(IntPtr hwnd, out int pid);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern uint GetPixel(IntPtr dc, int x, int y);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    [DllImport("user32.dll")]
    private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
}
