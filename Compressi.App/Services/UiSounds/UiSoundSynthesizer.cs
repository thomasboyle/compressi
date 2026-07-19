using System.Buffers.Binary;

namespace Compressi_App.Services.UiSounds;

/// <summary>
/// Offline PCM renderer for Cuelume-style recipes (MIT).
/// https://github.com/Danilaa1/cuelume
/// </summary>
internal static class UiSoundSynthesizer
{
    private const int SampleRate = 44100;
    private const double SourceStopPadding = 0.05;
    private const double CleanupMargin = 0.05;
    private const double InaudibleGain = 0.0001;
    private const double FloorGain = 0.0001;

    public static byte[] RenderWav(UiSoundName name)
    {
        var recipe = UiSoundRecipes.Get(name);
        var durationSeconds = SourceEnd(recipe) + ShimmerTail(recipe.Shimmer) + CleanupMargin;
        var sampleCount = Math.Max(1, (int)Math.Ceiling(durationSeconds * SampleRate));
        var dry = new float[sampleCount];

        foreach (var layer in recipe.Layers)
        {
            MixLayer(dry, layer);
        }

        Scale(dry, (float)recipe.MasterGain);

        var output = recipe.Shimmer is null
            ? dry
            : ApplyShimmer(dry, recipe.Shimmer);

        // Bake 3× headroom so MediaPlayer.Volume can reach 3× the original level at 100%.
        Scale(output, UiSoundService.MaxVolumeMultiplier);

        return EncodeWav(output);
    }

    private static void MixLayer(float[] buffer, UiSoundRecipes.Layer layer)
    {
        var start = (int)Math.Round(layer.Offset * SampleRate);
        var length = Math.Max(1, (int)Math.Ceiling((layer.Attack + layer.Decay + SourceStopPadding) * SampleRate));

        if (layer is UiSoundRecipes.ToneLayer tone)
        {
            MixTone(buffer, tone, start, length);
            return;
        }

        if (layer is UiSoundRecipes.NoiseLayer noise)
        {
            MixNoise(buffer, noise, start, length);
        }
    }

    private static void MixTone(float[] buffer, UiSoundRecipes.ToneLayer layer, int start, int length)
    {
        var frequency = layer.Frequency;
        if (layer.DetuneCents is { } cents)
        {
            frequency *= Math.Pow(2.0, cents / 1200.0);
        }

        var glideTo = layer.GlideTo ?? frequency;
        var glideTime = layer.GlideTime ?? (layer.Attack + layer.Decay);
        var glideSamples = Math.Max(1, (int)Math.Round(glideTime * SampleRate));
        var phase = 0.0;

        for (var i = 0; i < length; i++)
        {
            var index = start + i;
            if ((uint)index >= (uint)buffer.Length)
            {
                break;
            }

            var t = i / (double)SampleRate;
            var freq = frequency;
            if (layer.GlideTo is not null)
            {
                var progress = Math.Min(1.0, i / (double)glideSamples);
                // Approximate Web Audio exponentialRamp for positive frequencies.
                freq = frequency * Math.Pow(glideTo / frequency, progress);
            }

            var sample = layer.Waveform switch
            {
                UiSoundRecipes.Waveform.Triangle => Triangle(phase),
                _ => Math.Sin(phase * Math.PI * 2.0),
            };

            buffer[index] += (float)(sample * Envelope(t, layer.Attack, layer.Decay, layer.Peak));
            phase += freq / SampleRate;
            if (phase >= 1.0)
            {
                phase -= Math.Floor(phase);
            }
        }
    }

    private static void MixNoise(float[] buffer, UiSoundRecipes.NoiseLayer layer, int start, int length)
    {
        var filter = Biquad.Create(layer.FilterType, layer.FilterFrequency, layer.FilterQ, SampleRate);
        var random = new Random(unchecked((int)(layer.FilterFrequency * 1000 + layer.Peak * 10000)));

        for (var i = 0; i < length; i++)
        {
            var index = start + i;
            if ((uint)index >= (uint)buffer.Length)
            {
                break;
            }

            var t = i / (double)SampleRate;
            var white = (random.NextDouble() * 2.0) - 1.0;
            var filtered = filter.Process(white);
            buffer[index] += (float)(filtered * Envelope(t, layer.Attack, layer.Decay, layer.Peak));
        }
    }

    private static float[] ApplyShimmer(float[] dry, UiSoundRecipes.Shimmer shimmer)
    {
        var delaySamples = Math.Max(1, (int)Math.Round(shimmer.Delay * SampleRate));
        var output = new float[dry.Length];
        Array.Copy(dry, output, dry.Length);

        var filter = Biquad.Create(UiSoundRecipes.FilterKind.Lowpass, shimmer.Lowpass, 0.707, SampleRate);
        var delayLine = new float[delaySamples];
        var write = 0;

        for (var i = 0; i < dry.Length; i++)
        {
            var delayed = delayLine[write];
            var filtered = filter.Process(delayed);
            output[i] += (float)(filtered * shimmer.Wet);

            var input = dry[i] + (filtered * shimmer.Feedback);
            delayLine[write] = (float)input;
            write++;
            if (write >= delaySamples)
            {
                write = 0;
            }
        }

        return output;
    }

    private static double Envelope(double t, double attack, double decay, double peak)
    {
        if (t <= 0)
        {
            return FloorGain;
        }

        if (t < attack)
        {
            return ExpRamp(FloorGain, peak, t / Math.Max(attack, 1e-6));
        }

        var decayT = t - attack;
        if (decayT >= decay)
        {
            return FloorGain;
        }

        return ExpRamp(peak, FloorGain, decayT / Math.Max(decay, 1e-6));
    }

    private static double ExpRamp(double from, double to, double progress)
    {
        progress = Math.Clamp(progress, 0.0, 1.0);
        from = Math.Max(from, FloorGain);
        to = Math.Max(to, FloorGain);
        return from * Math.Pow(to / from, progress);
    }

    private static double Triangle(double phase)
    {
        var p = phase - Math.Floor(phase);
        if (p < 0.25)
        {
            return 4.0 * p;
        }

        if (p < 0.75)
        {
            return 2.0 - (4.0 * p);
        }

        return (4.0 * p) - 4.0;
    }

    private static void Scale(float[] buffer, float gain)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] *= gain;
        }
    }

    private static double SourceEnd(UiSoundRecipes.Recipe recipe)
    {
        var max = 0.0;
        foreach (var layer in recipe.Layers)
        {
            max = Math.Max(max, layer.Offset + layer.Attack + layer.Decay + SourceStopPadding);
        }

        return max;
    }

    private static double ShimmerTail(UiSoundRecipes.Shimmer? shimmer)
    {
        if (shimmer is null || shimmer.Feedback <= 0)
        {
            return 0;
        }

        if (shimmer.Feedback >= 1)
        {
            return shimmer.Delay;
        }

        return shimmer.Delay * (1 + Math.Ceiling(Math.Log(InaudibleGain) / Math.Log(shimmer.Feedback)));
    }

    private static byte[] EncodeWav(float[] samples)
    {
        const int channels = 1;
        const int bitsPerSample = 16;
        var blockAlign = channels * bitsPerSample / 8;
        var dataSize = samples.Length * blockAlign;
        var fileSize = 44 + dataSize;
        var bytes = new byte[fileSize];

        WriteAscii(bytes, 0, "RIFF");
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), fileSize - 8);
        WriteAscii(bytes, 8, "WAVE");
        WriteAscii(bytes, 12, "fmt ");
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(20), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(22), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(24), SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(28), SampleRate * blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(32), (short)blockAlign);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(34), bitsPerSample);
        WriteAscii(bytes, 36, "data");
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(40), dataSize);

        var offset = 44;
        for (var i = 0; i < samples.Length; i++)
        {
            var clipped = Math.Clamp(samples[i], -1f, 1f);
            var value = (short)Math.Round(clipped * short.MaxValue);
            BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(offset), value);
            offset += 2;
        }

        return bytes;
    }

    private static void WriteAscii(byte[] buffer, int offset, string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            buffer[offset + i] = (byte)value[i];
        }
    }

    private sealed class Biquad
    {
        private readonly double _b0;
        private readonly double _b1;
        private readonly double _b2;
        private readonly double _a1;
        private readonly double _a2;
        private double _z1;
        private double _z2;

        private Biquad(double b0, double b1, double b2, double a1, double a2)
        {
            _b0 = b0;
            _b1 = b1;
            _b2 = b2;
            _a1 = a1;
            _a2 = a2;
        }

        public static Biquad Create(UiSoundRecipes.FilterKind kind, double frequency, double q, int sampleRate)
        {
            frequency = Math.Clamp(frequency, 20.0, sampleRate * 0.49);
            q = Math.Max(q, 0.01);

            var w0 = 2.0 * Math.PI * frequency / sampleRate;
            var cos = Math.Cos(w0);
            var sin = Math.Sin(w0);
            var alpha = sin / (2.0 * q);

            double b0;
            double b1;
            double b2;
            double a0;
            double a1;
            double a2;

            if (kind == UiSoundRecipes.FilterKind.Bandpass)
            {
                b0 = alpha;
                b1 = 0;
                b2 = -alpha;
                a0 = 1 + alpha;
                a1 = -2 * cos;
                a2 = 1 - alpha;
            }
            else
            {
                b0 = (1 - cos) / 2.0;
                b1 = 1 - cos;
                b2 = (1 - cos) / 2.0;
                a0 = 1 + alpha;
                a1 = -2 * cos;
                a2 = 1 - alpha;
            }

            return new Biquad(b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
        }

        public double Process(double input)
        {
            var output = (_b0 * input) + _z1;
            _z1 = (_b1 * input) - (_a1 * output) + _z2;
            _z2 = (_b2 * input) - (_a2 * output);
            return output;
        }
    }
}
