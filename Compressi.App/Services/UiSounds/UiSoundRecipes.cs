namespace Compressi_App.Services.UiSounds;

/// <summary>
/// Sound recipes ported from Cuelume (MIT) — https://github.com/Danilaa1/cuelume
/// Copyright (c) 2026 Daniel Belyi
/// </summary>
internal static class UiSoundRecipes
{
    internal enum Waveform
    {
        Sine,
        Triangle,
    }

    internal enum FilterKind
    {
        Lowpass,
        Bandpass,
    }

    internal abstract record Layer(
        double Offset,
        double Attack,
        double Decay,
        double Peak);

    internal sealed record ToneLayer(
        double Offset,
        double Attack,
        double Decay,
        double Peak,
        Waveform Waveform,
        double Frequency,
        double? DetuneCents = null,
        double? GlideTo = null,
        double? GlideTime = null) : Layer(Offset, Attack, Decay, Peak);

    internal sealed record NoiseLayer(
        double Offset,
        double Attack,
        double Decay,
        double Peak,
        FilterKind FilterType,
        double FilterFrequency,
        double FilterQ = 1.0) : Layer(Offset, Attack, Decay, Peak);

    internal sealed record Shimmer(
        double Delay,
        double Feedback,
        double Wet,
        double Lowpass);

    internal sealed record Recipe(
        double MasterGain,
        Layer[] Layers,
        Shimmer? Shimmer = null);

    internal static Recipe Get(UiSoundName name) => name switch
    {
        UiSoundName.Chime => Chime,
        UiSoundName.Sparkle => Sparkle,
        UiSoundName.Droplet => Droplet,
        UiSoundName.Bloom => Bloom,
        UiSoundName.Whisper => Whisper,
        UiSoundName.Tick => Tick,
        UiSoundName.Press => Press,
        UiSoundName.Release => Release,
        UiSoundName.Toggle => Toggle,
        UiSoundName.Success => Success,
        UiSoundName.Error => Error,
        UiSoundName.Page => Page,
        UiSoundName.Loading => Loading,
        UiSoundName.Ready => Ready,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, null),
    };

    private static readonly Recipe Chime = new(
        0.5,
        [
            new ToneLayer(0, 0.006, 0.22, 0.09, Waveform.Sine, 1046.5),
            new ToneLayer(0.09, 0.006, 0.26, 0.08, Waveform.Sine, 1568),
        ],
        new Shimmer(0.12, 0.25, 0.18, 4000));

    private static readonly Recipe Sparkle = new(
        0.5,
        [
            new ToneLayer(0, 0.003, 0.09, 0.045, Waveform.Sine, 1760),
            new ToneLayer(0.045, 0.003, 0.09, 0.04, Waveform.Sine, 2217),
            new ToneLayer(0.09, 0.003, 0.1, 0.038, Waveform.Sine, 2637),
            new ToneLayer(0.135, 0.003, 0.12, 0.032, Waveform.Sine, 3520),
        ],
        new Shimmer(0.07, 0.35, 0.22, 6000));

    private static readonly Recipe Droplet = new(
        0.55,
        [
            new ToneLayer(0, 0.004, 0.2, 0.075, Waveform.Sine, 1200, GlideTo: 550, GlideTime: 0.14),
        ],
        new Shimmer(0.09, 0.2, 0.15, 3000));

    private static readonly Recipe Bloom = new(
        0.5,
        [
            new ToneLayer(0, 0.06, 0.32, 0.06, Waveform.Sine, 528),
            new ToneLayer(0, 0.06, 0.34, 0.05, Waveform.Sine, 528, DetuneCents: 12),
        ],
        new Shimmer(0.15, 0.2, 0.12, 2500));

    private static readonly Recipe Whisper = new(
        0.5,
        [
            new NoiseLayer(0, 0.04, 0.16, 0.05, FilterKind.Lowpass, 1200, 0.7),
        ]);

    private static readonly Recipe Tick = new(
        0.4,
        [
            new NoiseLayer(0, 0.001, 0.018, 0.14, FilterKind.Bandpass, 5400, 1.8),
            new ToneLayer(0, 0.001, 0.012, 0.018, Waveform.Sine, 2600),
        ]);

    private static readonly Recipe Press = new(
        0.4,
        [
            new NoiseLayer(0, 0.001, 0.02, 0.13, FilterKind.Bandpass, 1700, 1.4),
        ]);

    private static readonly Recipe Release = new(
        0.4,
        [
            new NoiseLayer(0, 0.001, 0.016, 0.12, FilterKind.Bandpass, 4600, 1.8),
            new ToneLayer(0.006, 0.001, 0.05, 0.02, Waveform.Sine, 3200),
        ]);

    private static readonly Recipe Toggle = new(
        0.4,
        [
            new NoiseLayer(0, 0.001, 0.016, 0.12, FilterKind.Bandpass, 2200, 1.6),
            new NoiseLayer(0.024, 0.001, 0.02, 0.1, FilterKind.Bandpass, 3800, 1.6),
        ]);

    private static readonly Recipe Success = new(
        0.5,
        [
            new ToneLayer(0, 0.004, 0.09, 0.06, Waveform.Sine, 880),
            new ToneLayer(0.06, 0.004, 0.1, 0.06, Waveform.Sine, 1108.73),
            new ToneLayer(0.12, 0.004, 0.18, 0.07, Waveform.Sine, 1318.51),
        ],
        new Shimmer(0.1, 0.22, 0.16, 4500));

    private static readonly Recipe Error = new(
        0.42,
        [
            new NoiseLayer(0, 0.001, 0.035, 0.13, FilterKind.Bandpass, 850, 1.1),
            new ToneLayer(0.025, 0.004, 0.09, 0.045, Waveform.Triangle, 440),
            new ToneLayer(0.1, 0.004, 0.14, 0.04, Waveform.Triangle, 349.23),
        ]);

    private static readonly Recipe Page = new(
        0.38,
        [
            new NoiseLayer(0, 0.006, 0.08, 0.11, FilterKind.Lowpass, 1800, 0.7),
            new NoiseLayer(0.04, 0.004, 0.065, 0.08, FilterKind.Bandpass, 4200, 1.2),
            new ToneLayer(0.075, 0.002, 0.045, 0.02, Waveform.Sine, 2400),
        ]);

    private static readonly Recipe Loading = new(
        0.42,
        [
            new NoiseLayer(0, 0.035, 0.14, 0.035, FilterKind.Lowpass, 1400, 0.6),
            new ToneLayer(0, 0.025, 0.18, 0.05, Waveform.Sine, 420, GlideTo: 630, GlideTime: 0.18),
        ],
        new Shimmer(0.11, 0.18, 0.12, 2800));

    private static readonly Recipe Ready = new(
        0.45,
        [
            new NoiseLayer(0, 0.001, 0.018, 0.1, FilterKind.Bandpass, 3200, 1.7),
            new ToneLayer(0.025, 0.012, 0.2, 0.05, Waveform.Sine, 659.25),
            new ToneLayer(0.025, 0.012, 0.22, 0.035, Waveform.Sine, 987.77),
        ],
        new Shimmer(0.13, 0.2, 0.13, 3600));
}
