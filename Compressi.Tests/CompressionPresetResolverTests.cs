using Compressi.Core.Models;
using Compressi.Core.Services;

namespace Compressi.Tests;

public class CompressionPresetResolverTests
{
    [Fact]
    public void Resolve_Balanced_IncludesSvtAv1CrfAndOutputPath()
    {
        var source = CreateSource();
        var job = new CompressionJob
        {
            Source = source,
            Preset = CompressionPreset.Balanced,
            Format = OutputFormat.Mp4,
            ThreadCount = 8,
        };

        var plan = CompressionPresetResolver.Resolve(job);

        Assert.EndsWith("_compressed.mp4", plan.OutputPath, StringComparison.OrdinalIgnoreCase);
        Assert.Single(plan.Passes);
        var args = plan.Passes[0].Arguments;
        Assert.Contains("-c:v", args);
        Assert.Contains("-crf", args);
        Assert.Contains(EncodingConstants.BalancedCrf.ToString(), args);
        Assert.Contains("-progress", args);
        Assert.Contains("pipe:1", args);
    }

    [Fact]
    public void Resolve_Ultra_ReturnsCrfPlan()
    {
        var job = new CompressionJob
        {
            Source = CreateSource(),
            Preset = CompressionPreset.Ultra,
            Format = OutputFormat.Mp4,
        };

        var plan = CompressionPresetResolver.Resolve(job);
        Assert.Contains("-crf", plan.Passes[0].Arguments);
        Assert.Contains(EncodingConstants.UltraCrf.ToString(), plan.Passes[0].Arguments);
    }

    [Fact]
    public void Resolve_Ultra_TallSource_RecordsDownscaledOutputDimensions()
    {
        var source = CreateSource();
        source = new VideoFile
        {
            FilePath = source.FilePath,
            FileName = source.FileName,
            Width = 3840,
            Height = 2160,
            FrameRate = source.FrameRate,
            Duration = source.Duration,
            VideoCodec = source.VideoCodec,
            AudioCodec = source.AudioCodec,
            ContainerFormat = source.ContainerFormat,
            FileSizeBytes = source.FileSizeBytes,
        };

        var plan = CompressionPresetResolver.Resolve(new CompressionJob
        {
            Source = source,
            Preset = CompressionPreset.Ultra,
            Format = OutputFormat.Mp4,
        });

        Assert.Equal(1920, plan.OutputWidth);
        Assert.Equal(1080, plan.OutputHeight);
    }

    [Fact]
    public void Resolve_Balanced_RecordsSourceOutputDimensions()
    {
        var plan = CompressionPresetResolver.Resolve(new CompressionJob
        {
            Source = CreateSource(),
            Preset = CompressionPreset.Balanced,
            Format = OutputFormat.Mp4,
        });

        Assert.Equal(1920, plan.OutputWidth);
        Assert.Equal(1080, plan.OutputHeight);
    }

    [Fact]
    public void Resolve_EightMb_FirstPassUsesSeparateNullOutputArgs()
    {
        var job = new CompressionJob
        {
            Source = CreateSource(),
            Preset = CompressionPreset.EightMB,
            Format = OutputFormat.Mp4,
            ThreadCount = 8,
        };

        var plan = CompressionPresetResolver.Resolve(job);

        Assert.Equal(2, plan.Passes.Count);
        var passOne = plan.Passes[0].Arguments;
        Assert.Contains("-f", passOne);
        Assert.Contains("mp4", passOne);
        Assert.Contains("NUL", passOne);
        Assert.Contains("-maxrate", passOne);
        Assert.DoesNotContain("-f mp4 NUL", passOne);
        Assert.True(plan.AudioBitrateKbps > 0);
        Assert.True(plan.OutputFrameRate > 0);
        Assert.False(plan.UseHardwareAcceleration);
    }

    [Fact]
    public void BuildEightMbCorrectivePass_IsSinglePassUsingFrozenPlan()
    {
        var job = new CompressionJob
        {
            Source = CreateSource(frameRate: 60),
            Preset = CompressionPreset.EightMB,
            Format = OutputFormat.Mp4,
            ThreadCount = 4,
        };

        var plan = CompressionPresetResolver.Resolve(job);
        var corrective = CompressionPresetResolver.BuildEightMbCorrectivePass(job, plan, adjustedBitrateKbps: 400);

        Assert.Contains("-b:v", corrective.Arguments);
        Assert.Contains("400k", corrective.Arguments);
        Assert.Contains("-maxrate", corrective.Arguments);
        Assert.DoesNotContain("-pass", corrective.Arguments);
        Assert.Null(corrective.PassLogFilePrefix);
        Assert.Contains(plan.OutputPath, corrective.Arguments);

        var vfIndex = corrective.Arguments.ToList().IndexOf("-vf");
        if (plan.OutputWidth != job.Source.Width || plan.OutputHeight != job.Source.Height)
        {
            Assert.True(vfIndex >= 0);
            Assert.Contains($"scale={plan.OutputWidth}:{plan.OutputHeight}", corrective.Arguments[vfIndex + 1]);
        }
    }

    private static VideoFile CreateSource(double frameRate = 30)
    {
        return new VideoFile
        {
            FilePath = @"C:\videos\clip.mp4",
            FileName = "clip.mp4",
            Width = 1920,
            Height = 1080,
            FrameRate = frameRate,
            Duration = TimeSpan.FromMinutes(2),
            VideoCodec = "h264",
            AudioCodec = "aac",
            ContainerFormat = "mp4",
            FileSizeBytes = 50_000_000,
        };
    }
}
