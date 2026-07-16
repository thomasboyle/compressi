using Compressi.Core.Models;
using Compressi.Core.Services;

namespace Compressi.Tests;

public class EightMbBitrateResolverTests
{
    [Fact]
    public void Resolve_ComputesBitrateForTypicalClip()
    {
        var source = CreateSource(width: 1920, height: 1080, duration: TimeSpan.FromSeconds(60), frameRate: 30);

        var plan = EightMbBitrateResolver.Resolve(source, null);

        Assert.True(plan.IsViable);
        Assert.True(plan.VideoBitrateKbps > 0);
        Assert.Equal(EncodingConstants.EightMbAudioBitrateKbps, plan.AudioBitrateKbps);
        Assert.Equal(30, plan.OutputFrameRate);
    }

    [Fact]
    public void Resolve_FailsWhenAudioAloneExceedsBudget()
    {
        var source = CreateSource(width: 1280, height: 720, duration: TimeSpan.FromHours(2), frameRate: 30);

        var plan = EightMbBitrateResolver.Resolve(source, null);

        Assert.False(plan.IsViable);
        Assert.NotNull(plan.FailureReason);
    }

    [Fact]
    public void Resolve_UsesProbedFrameRateForDownscaleDecisions()
    {
        // ~25s keeps 1080p30 viable but forces 1080p60 to drop resolution under the bpp floor.
        var at30 = EightMbBitrateResolver.Resolve(
            CreateSource(width: 1920, height: 1080, duration: TimeSpan.FromSeconds(25), frameRate: 30),
            null);
        var at60 = EightMbBitrateResolver.Resolve(
            CreateSource(width: 1920, height: 1080, duration: TimeSpan.FromSeconds(25), frameRate: 60),
            null);

        Assert.True(at30.IsViable);
        Assert.True(at60.IsViable);
        Assert.Equal(1080, at30.OutputHeight);
        Assert.True(at60.OutputHeight < at30.OutputHeight);
    }

    [Fact]
    public void Resolve_PrefersTypedResolutionOverride()
    {
        var source = CreateSource(width: 1920, height: 1080, duration: TimeSpan.FromSeconds(45), frameRate: 30);
        var advanced = new AdvancedEncodingOptions
        {
            ResolutionOverride = "garbage",
            ResolutionWidth = 1280,
            ResolutionHeight = 720,
        };

        var plan = EightMbBitrateResolver.Resolve(source, advanced);

        Assert.True(plan.IsViable);
        Assert.Equal(1280, plan.OutputWidth);
        Assert.Equal(720, plan.OutputHeight);
    }

    private static VideoFile CreateSource(int width, int height, TimeSpan duration, double frameRate) => new()
    {
        FilePath = @"C:\clip.mp4",
        FileName = "clip.mp4",
        Width = width,
        Height = height,
        FrameRate = frameRate,
        Duration = duration,
        VideoCodec = "h264",
        AudioCodec = "aac",
        ContainerFormat = "mp4",
        FileSizeBytes = 50_000_000,
    };
}
