namespace Compressi.Core.Services;

public static class FfmpegToolPaths
{
    public static string GetFfprobePath()
    {
        return ResolveToolPath("ffprobe");
    }

    public static string GetFfmpegPath()
    {
        return ResolveToolPath("ffmpeg");
    }

    public static bool IsFfprobeAvailable()
    {
        return File.Exists(GetFfprobePath());
    }

    private static string ResolveToolPath(string toolName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "ffmpeg", $"{toolName}.exe"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Assets", "ffmpeg", $"{toolName}.exe")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Compressi.App", "Assets", "ffmpeg", $"{toolName}.exe")),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return candidates[0];
    }
}
