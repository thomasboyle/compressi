namespace Compressi.Core.Models;

public sealed class AdvancedEncodingOptions
{
    public string? ResolutionOverride { get; init; }

    /// <summary>Parsed width from <see cref="ResolutionOverride"/>; prefer over re-parsing the string.</summary>
    public int? ResolutionWidth { get; init; }

    /// <summary>Parsed height from <see cref="ResolutionOverride"/>; prefer over re-parsing the string.</summary>
    public int? ResolutionHeight { get; init; }

    public int? FrameRateOverride { get; init; }

    public int? AudioBitrateKbps { get; init; }

    public bool KeepOriginalAudio { get; init; }

    public string? OutputFilenamePattern { get; init; }

    public string? OutputDirectory { get; init; }
}
