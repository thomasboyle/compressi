namespace Compressi.Core.Services;

public static class VideoFileFormats
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".mov", ".mkv", ".avi", ".webm", ".m4v",
    };

    public static IReadOnlyCollection<string> Extensions => SupportedExtensions;

    public static bool IsSupportedExtension(string? extension)
    {
        return !string.IsNullOrWhiteSpace(extension) && SupportedExtensions.Contains(extension);
    }

    public static bool IsSupportedPath(string path)
    {
        return IsSupportedExtension(Path.GetExtension(path));
    }
}
