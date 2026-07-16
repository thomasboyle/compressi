using System.Globalization;
using System.Text.Json;
using Compressi.Core.Models;

namespace Compressi.Core.Services;

public static class FfprobeJsonParser
{
    public static VideoFile Parse(string filePath, string json, long? fallbackFileSizeBytes = null)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (!root.TryGetProperty("streams", out var streams))
        {
            throw new InvalidOperationException("ffprobe output did not contain stream information.");
        }

        JsonElement? videoStream = null;
        JsonElement? audioStream = null;
        foreach (var stream in streams.EnumerateArray())
        {
            if (!stream.TryGetProperty("codec_type", out var codecType))
            {
                continue;
            }

            var codecTypeValue = codecType.GetString();
            if (videoStream is null && codecTypeValue == "video")
            {
                videoStream = stream;
            }
            else if (audioStream is null && codecTypeValue == "audio")
            {
                audioStream = stream;
            }

            if (videoStream is not null && audioStream is not null)
            {
                break;
            }
        }

        if (videoStream is null)
        {
            throw new InvalidOperationException("No video stream found in the selected file.");
        }

        var width = videoStream.Value.GetProperty("width").GetInt32();
        var height = videoStream.Value.GetProperty("height").GetInt32();
        var frameRate = ParseFrameRate(videoStream.Value);
        var videoCodec = videoStream.Value.TryGetProperty("codec_name", out var codecName)
            ? codecName.GetString() ?? "unknown"
            : "unknown";
        string? audioCodec = audioStream?.TryGetProperty("codec_name", out var audioCodecName) == true
            ? audioCodecName.GetString()
            : null;

        var duration = ParseDuration(root, videoStream.Value);
        var container = ParseContainer(root, filePath);
        var fileSize = ParseFileSize(root, ResolveFallbackFileSize(filePath, fallbackFileSizeBytes));

        return new VideoFile
        {
            FilePath = filePath,
            FileName = Path.GetFileName(filePath),
            Width = width,
            Height = height,
            FrameRate = frameRate,
            Duration = duration,
            VideoCodec = videoCodec,
            AudioCodec = audioCodec,
            ContainerFormat = container,
            FileSizeBytes = fileSize,
        };
    }

    internal static double ParseFrameRate(JsonElement videoStream)
    {
        if (TryParseRate(videoStream, "avg_frame_rate", out var rate) && rate > 0)
        {
            return rate;
        }

        if (TryParseRate(videoStream, "r_frame_rate", out rate) && rate > 0)
        {
            return rate;
        }

        return 0;
    }

    private static bool TryParseRate(JsonElement videoStream, string propertyName, out double rate)
    {
        rate = 0;
        if (!videoStream.TryGetProperty(propertyName, out var rateElement))
        {
            return false;
        }

        var raw = rateElement.GetString();
        if (string.IsNullOrWhiteSpace(raw) || raw is "0/0")
        {
            return false;
        }

        var parts = raw.Split('/', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2
            && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator)
            && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator)
            && denominator > 0)
        {
            rate = numerator / denominator;
            return rate > 0 && !double.IsNaN(rate) && !double.IsInfinity(rate);
        }

        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out rate)
            && rate > 0
            && !double.IsNaN(rate)
            && !double.IsInfinity(rate);
    }

    private static TimeSpan ParseDuration(JsonElement root, JsonElement videoStream)
    {
        if (root.TryGetProperty("format", out var format)
            && format.TryGetProperty("duration", out var formatDuration)
            && TryParseSeconds(formatDuration.GetString(), out var seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        if (videoStream.TryGetProperty("duration", out var streamDuration)
            && TryParseSeconds(streamDuration.GetString(), out seconds))
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return TimeSpan.Zero;
    }

    private static string ParseContainer(JsonElement root, string filePath)
    {
        if (!root.TryGetProperty("format", out var format)
            || !format.TryGetProperty("format_name", out var formatName))
        {
            return Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        }

        var raw = formatName.GetString() ?? "unknown";
        return raw.Split(',')[0];
    }

    private static long ParseFileSize(JsonElement root, long fallback)
    {
        if (root.TryGetProperty("format", out var format)
            && format.TryGetProperty("size", out var sizeElement)
            && long.TryParse(sizeElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
        {
            return size;
        }

        return fallback;
    }

    private static long ResolveFallbackFileSize(string filePath, long? fallbackFileSizeBytes)
    {
        if (fallbackFileSizeBytes.HasValue)
        {
            return fallbackFileSizeBytes.Value;
        }

        return File.Exists(filePath) ? new FileInfo(filePath).Length : 0;
    }

    private static bool TryParseSeconds(string? value, out double seconds)
    {
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds);
    }
}
