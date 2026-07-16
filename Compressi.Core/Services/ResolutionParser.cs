using Compressi.Core.Models;

namespace Compressi.Core.Services;

public static class ResolutionParser
{
    public static bool TryGetOverride(AdvancedEncodingOptions? advanced, out int width, out int height)
    {
        if (advanced?.ResolutionWidth is int w
            && advanced.ResolutionHeight is int h
            && w > 0
            && h > 0)
        {
            width = w;
            height = h;
            return true;
        }

        return TryParse(advanced?.ResolutionOverride, out width, out height);
    }

    public static bool TryParse(string? value, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('x', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2
            && int.TryParse(parts[0], out width)
            && int.TryParse(parts[1], out height)
            && width > 0
            && height > 0;
    }
}
