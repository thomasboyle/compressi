using Compressi.Core.Models;
using Compressi.Core.Services;

namespace Compressi.Tests;

public class EncodeOutputMetadataTests
{
    [Fact]
    public void Create_UsesPlanDimensionsFileSizeAndAv1()
    {
        var job = new CompressionJob
        {
            Source = new VideoFile
            {
                FilePath = @"C:\videos\clip.mp4",
                FileName = "clip.mp4",
                Width = 3840,
                Height = 2160,
                Duration = TimeSpan.FromSeconds(90),
                VideoCodec = "h264",
                AudioCodec = "aac",
                ContainerFormat = "mp4",
                FileSizeBytes = 100_000_000,
            },
            Preset = CompressionPreset.Ultra,
            Format = OutputFormat.Mp4,
        };

        var plan = new FfmpegEncodePlan
        {
            OutputPath = @"C:\videos\clip_compressed.mp4",
            Passes = Array.Empty<FfmpegEncodePass>(),
            OutputWidth = 1920,
            OutputHeight = 1080,
        };

        var output = EncodeOutputMetadata.Create(job, plan, fileSizeBytes: 12_345_678);

        Assert.Equal("clip_compressed.mp4", output.FileName);
        Assert.Equal(1920, output.Width);
        Assert.Equal(1080, output.Height);
        Assert.Equal(TimeSpan.FromSeconds(90), output.Duration);
        Assert.Equal("av1", output.VideoCodec);
        Assert.Equal("mp4", output.ContainerFormat);
        Assert.Equal(12_345_678, output.FileSizeBytes);
    }

    [Fact]
    public void Create_UsesH264WhenJobRequestsIt()
    {
        var job = new CompressionJob
        {
            Source = new VideoFile
            {
                FilePath = @"C:\videos\clip.mp4",
                FileName = "clip.mp4",
                Width = 1280,
                Height = 720,
                Duration = TimeSpan.FromSeconds(30),
                VideoCodec = "h264",
                AudioCodec = "aac",
                ContainerFormat = "mp4",
                FileSizeBytes = 20_000_000,
            },
            Preset = CompressionPreset.EightMB,
            Format = OutputFormat.Mp4,
            VideoCodec = VideoCodec.H264,
        };

        var plan = new FfmpegEncodePlan
        {
            OutputPath = @"C:\videos\clip_compressed.mp4",
            Passes = Array.Empty<FfmpegEncodePass>(),
            OutputWidth = 1280,
            OutputHeight = 720,
        };

        var output = EncodeOutputMetadata.Create(job, plan, fileSizeBytes: 7_000_000);
        Assert.Equal("h264", output.VideoCodec);
    }
}
