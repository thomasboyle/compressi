using System.Globalization;
using Compressi.Core.Models;

namespace Compressi.Core.Services;

public static class FfmpegProgressParser
{
    public static EncodingProgress ApplyLine(string line, TimeSpan totalDuration, EncodingProgress current)
    {
        var state = EncodingProgressState.From(current);
        ApplyLine(line, totalDuration, state);
        return state.ToProgress();
    }

    public static bool ApplyLine(string line, TimeSpan totalDuration, EncodingProgressState state)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var separatorIndex = line.IndexOf('=');
        if (separatorIndex <= 0)
        {
            return false;
        }

        var key = line.AsSpan(0, separatorIndex).Trim();
        var value = line.AsSpan(separatorIndex + 1).Trim();

        if (key.Equals("out_time_us", StringComparison.Ordinal)
            && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var micros))
        {
            return UpdateOutTime(state, TimeSpan.FromMicroseconds(micros), totalDuration);
        }

        if (key.Equals("out_time_ms", StringComparison.Ordinal)
            && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out micros))
        {
            return UpdateOutTime(state, TimeSpan.FromMicroseconds(micros), totalDuration);
        }

        if (key.Equals("out_time", StringComparison.Ordinal))
        {
            return UpdateOutTime(state, ParseOutTime(value), totalDuration);
        }

        if (key.Equals("total_size", StringComparison.Ordinal)
            && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
        {
            if (state.OutputSizeBytes == size)
            {
                return false;
            }

            state.OutputSizeBytes = size;
            return true;
        }

        if (key.Equals("speed", StringComparison.Ordinal))
        {
            var speed = ParseSpeed(value);
            if (state.SpeedMultiplier == speed)
            {
                return false;
            }

            state.SpeedMultiplier = speed;
            return true;
        }

        if (key.Equals("progress", StringComparison.Ordinal) && value.Equals("end", StringComparison.Ordinal))
        {
            state.IsFinished = true;
            state.ProgressPercent = 100;
            return true;
        }

        return false;
    }

    private static bool UpdateOutTime(EncodingProgressState state, TimeSpan outTime, TimeSpan totalDuration)
    {
        double? percent = null;
        if (totalDuration > TimeSpan.Zero)
        {
            percent = Math.Clamp(outTime.TotalSeconds / totalDuration.TotalSeconds * 100, 0, 100);
        }

        if (state.OutTime == outTime && state.ProgressPercent == percent)
        {
            return false;
        }

        state.OutTime = outTime;
        state.ProgressPercent = percent;
        return true;
    }

    private static TimeSpan ParseOutTime(ReadOnlySpan<char> value)
    {
        if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return TimeSpan.Zero;
    }

    private static double? ParseSpeed(ReadOnlySpan<char> value)
    {
        if (value.EndsWith("x", StringComparison.Ordinal))
        {
            value = value[..^1];
        }

        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var speed)
            ? speed
            : null;
    }
}

public sealed class EncodingProgressState
{
    public double? ProgressPercent { get; set; }

    public TimeSpan? OutTime { get; set; }

    public long? OutputSizeBytes { get; set; }

    public double? SpeedMultiplier { get; set; }

    public bool IsFinished { get; set; }

    public static EncodingProgressState From(EncodingProgress current) => new()
    {
        ProgressPercent = current.ProgressPercent,
        OutTime = current.OutTime,
        OutputSizeBytes = current.OutputSizeBytes,
        SpeedMultiplier = current.SpeedMultiplier,
        IsFinished = current.IsFinished,
    };

    public EncodingProgress ToProgress() => new()
    {
        ProgressPercent = ProgressPercent,
        OutTime = OutTime,
        OutputSizeBytes = OutputSizeBytes,
        SpeedMultiplier = SpeedMultiplier,
        IsFinished = IsFinished,
    };
}
