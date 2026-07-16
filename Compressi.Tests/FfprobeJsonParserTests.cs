using Compressi.Core.Services;

namespace Compressi.Tests;

public class FfprobeJsonParserTests
{
    private const string SampleJson = """
        {
          "format": {
            "format_name": "mov,mp4,m4a,3gp,3g2,mj2",
            "duration": "154.500000",
            "size": "47456789"
          },
          "streams": [
            {
              "codec_type": "video",
              "codec_name": "h264",
              "width": 1920,
              "height": 1080,
              "avg_frame_rate": "30000/1001",
              "r_frame_rate": "30/1",
              "duration": "154.500000"
            },
            {
              "codec_type": "audio",
              "codec_name": "aac"
            }
          ]
        }
        """;

    [Fact]
    public void Parse_ReturnsExpectedVideoMetadata()
    {
        var result = FfprobeJsonParser.Parse(@"C:\videos\sample_video.mp4", SampleJson);

        Assert.Equal("sample_video.mp4", result.FileName);
        Assert.Equal(1920, result.Width);
        Assert.Equal(1080, result.Height);
        Assert.Equal("h264", result.VideoCodec);
        Assert.Equal("mov", result.ContainerFormat);
        Assert.Equal(47456789, result.FileSizeBytes);
        Assert.Equal(TimeSpan.FromSeconds(154.5), result.Duration);
        Assert.Equal(30000d / 1001d, result.FrameRate, precision: 5);
        Assert.Equal("1920 × 1080 • 2:34 • 45.3 MB", result.MetadataLine);
    }

    [Fact]
    public void Parse_FallsBackToRFrameRateWhenAvgMissing()
    {
        const string json = """
            {
              "format": { "format_name": "mp4", "duration": "1.0", "size": "1000" },
              "streams": [{
                "codec_type": "video",
                "codec_name": "h264",
                "width": 1280,
                "height": 720,
                "r_frame_rate": "60/1"
              }]
            }
            """;

        var result = FfprobeJsonParser.Parse(@"C:\videos\sixty.mp4", json, fallbackFileSizeBytes: 1000);

        Assert.Equal(60, result.FrameRate);
    }

    [Fact]
    public void Parse_ThrowsWhenVideoStreamMissing()
    {
        const string json = """
            {
              "format": { "format_name": "mp4", "duration": "1.0", "size": "1000" },
              "streams": [{ "codec_type": "audio", "codec_name": "aac" }]
            }
            """;

        Assert.Throws<InvalidOperationException>(() =>
            FfprobeJsonParser.Parse(@"C:\videos\audio_only.mp4", json));
    }
}
