namespace Compressi.Core.Models;

public static class CompressionPresetLabels
{
    public const string Ultra = "Ultra";
    public const string EightMB = "8 MB Target (Recommended)";
    public const string Balanced = "Balanced";

    public const string UltraTooltip =
        "Smallest file size with reduced video quality. Best for archiving or slow connections.";

    public const string EightMBTooltip =
        "Targets an 8 MB file while keeping audio and frame rate as high as possible. Great for Discord and chat sharing.";

    public const string BalancedTooltip =
        "Solid quality-to-size tradeoff for everyday sharing.";

    public static string GetDisplayName(CompressionPreset preset) => preset switch
    {
        CompressionPreset.Ultra => Ultra,
        CompressionPreset.EightMB => EightMB,
        CompressionPreset.Balanced => Balanced,
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown compression preset."),
    };

    public static string GetTooltip(CompressionPreset preset) => preset switch
    {
        CompressionPreset.Ultra => UltraTooltip,
        CompressionPreset.EightMB => EightMBTooltip,
        CompressionPreset.Balanced => BalancedTooltip,
        _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Unknown compression preset."),
    };
}
