namespace Compressi.Core.Services;

public static class EncodingConstants
{
    public const long EightMbTargetBytes = 8L * 1024 * 1024;
    // Leave headroom for container overhead and AV1 rate control overshoot.
    public const double EightMbSafetyMargin = 0.92;

    public const int BalancedCrf = 31;
    public const int BalancedPreset = 8;

    public const int UltraCrf = 47;
    public const int UltraPreset = 6;
    public const int UltraMaxHeight = 1080;
    public const int UltraAudioBitrateKbps = 96;

    public const int EightMbAudioBitrateKbps = 80;
    public const int EightMbPreset = 6;

    public const int DefaultAudioBitrateKbps = 128;
}
