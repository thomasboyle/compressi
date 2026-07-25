using System.Text.Json;
using Compressi.Core.Models;

namespace Compressi_App.Services;

/// <summary>
/// Kicks off settings.json I/O on a thread-pool thread from the earliest managed hook so it
/// overlaps WASDK/App type bring-up. <see cref="SettingsStore.Load"/> joins the result.
/// </summary>
internal static class SettingsPreload
{
    private static Task<PreloadResult>? _task;
    private static string? _path;

    public static void Start()
    {
        if (_task is not null)
        {
            return;
        }

        _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Compressi",
            "settings.json");

        _task = Task.Run(LoadCore);
    }

    public static bool TryTake(out AppSettings settings, out bool loadedFromDefaultsAfterError)
    {
        settings = null!;
        loadedFromDefaultsAfterError = false;
        var task = _task;
        if (task is null)
        {
            return false;
        }

        // Only consume once so SettingsStore owns the snapshot thereafter.
        _task = null;
        try
        {
            var result = task.GetAwaiter().GetResult();
            settings = result.Settings;
            loadedFromDefaultsAfterError = result.LoadedFromDefaultsAfterError;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static PreloadResult LoadCore()
    {
        var path = _path ?? throw new InvalidOperationException("Settings preload path was not set.");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (!File.Exists(path))
        {
            return new PreloadResult(new AppSettings(), false);
        }

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
            return new PreloadResult(settings, false);
        }
        catch
        {
            return new PreloadResult(new AppSettings(), true);
        }
    }

    private readonly record struct PreloadResult(AppSettings Settings, bool LoadedFromDefaultsAfterError);
}
