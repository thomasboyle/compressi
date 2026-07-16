using Compressi.Core.Models;



namespace Compressi.Core.Services;



public static class EightMbBitrateResolver

{

    private static readonly (int MaxHeight, double MinBitsPerPixel)[] ResolutionFloors =

    [

        (2160, 0.025),

        (1080, 0.030),

        (720, 0.040),

        (480, 0.050),

    ];



    public sealed record EightMbPlan(

        int VideoBitrateKbps,

        int AudioBitrateKbps,

        int OutputWidth,

        int OutputHeight,

        int OutputFrameRate,

        bool IsViable,

        string? FailureReason);



    public static EightMbPlan Resolve(VideoFile source, AdvancedEncodingOptions? advanced)

    {

        var durationSeconds = source.Duration.TotalSeconds;

        if (durationSeconds <= 0)

        {

            return new EightMbPlan(0, 0, source.Width, source.Height, EstimateFrameRate(source), false, "Video duration is zero.");

        }



        var audioKbps = advanced?.AudioBitrateKbps ?? EncodingConstants.EightMbAudioBitrateKbps;

        var targetBits = (long)(EncodingConstants.EightMbTargetBytes * 8 * EncodingConstants.EightMbSafetyMargin);

        var audioBits = (long)audioKbps * 1000L * (long)Math.Ceiling(durationSeconds);



        if (audioBits >= targetBits)

        {

            return new EightMbPlan(

                0,

                audioKbps,

                source.Width,

                source.Height,

                EstimateFrameRate(source),

                false,

                "Can't hit 8 MB with audio at this video length.");

        }



        var videoBits = targetBits - audioBits;

        var videoBitrateKbps = (int)Math.Max(1, videoBits / durationSeconds / 1000);



        var width = source.Width;

        var height = source.Height;

        var frameRate = advanced?.FrameRateOverride is int overrideFps && overrideFps > 0

            ? overrideFps

            : EstimateFrameRate(source);



        if (ResolutionParser.TryGetOverride(advanced, out var overrideWidth, out var overrideHeight))

        {

            width = overrideWidth;

            height = overrideHeight;

        }



        if (!MeetsFloor(width, height, frameRate, videoBitrateKbps))

        {

            foreach (var (maxHeight, _) in ResolutionFloors)

            {

                if (height <= maxHeight)

                {

                    continue;

                }



                var scaled = ScaleToHeight(width, height, maxHeight);

                width = scaled.width;

                height = scaled.height;

                if (MeetsFloor(width, height, frameRate, videoBitrateKbps))

                {

                    break;

                }

            }

        }



        if (!MeetsFloor(width, height, frameRate, videoBitrateKbps))

        {

            frameRate = frameRate > 30 ? 30 : frameRate;

        }



        if (!MeetsFloor(width, height, frameRate, videoBitrateKbps))

        {

            var scaled = ScaleToHeight(width, height, 480);

            width = scaled.width;

            height = scaled.height;

        }



        if (!MeetsFloor(width, height, frameRate, videoBitrateKbps))

        {

            return new EightMbPlan(

                videoBitrateKbps,

                audioKbps,

                width,

                height,

                frameRate,

                false,

                "Can't hit 8 MB with audio at this length, even at 480p.");

        }



        return new EightMbPlan(videoBitrateKbps, audioKbps, width, height, frameRate, true, null);

    }



    public static int AdjustBitrateForOvershoot(int currentBitrateKbps, long actualBytes)

    {

        if (actualBytes <= EncodingConstants.EightMbTargetBytes)

        {

            return currentBitrateKbps;

        }



        var ratio = EncodingConstants.EightMbTargetBytes / (double)actualBytes;

        return Math.Max(32, (int)(currentBitrateKbps * ratio * 0.95));

    }



    private static bool MeetsFloor(int width, int height, int frameRate, int videoBitrateKbps)

    {

        var minBpp = GetMinBitsPerPixel(height);

        var requiredKbps = width * height * frameRate * minBpp / 1000d;

        return videoBitrateKbps >= requiredKbps;

    }



    private static double GetMinBitsPerPixel(int height)

    {

        foreach (var (maxHeight, minBpp) in ResolutionFloors)

        {

            if (height <= maxHeight)

            {

                return minBpp;

            }

        }



        return ResolutionFloors[^1].MinBitsPerPixel;

    }



    private static (int width, int height) ScaleToHeight(int width, int height, int targetHeight)

    {

        if (height <= targetHeight)

        {

            return (width, height);

        }



        var scaledWidth = Math.Max(2, (int)Math.Round(width * (targetHeight / (double)height)));

        if (scaledWidth % 2 != 0)

        {

            scaledWidth--;

        }



        return (scaledWidth, targetHeight);

    }



    internal static int EstimateFrameRate(VideoFile source)

    {

        if (source.FrameRate > 0)

        {

            return Math.Max(1, (int)Math.Round(source.FrameRate));

        }



        return 30;

    }

}


