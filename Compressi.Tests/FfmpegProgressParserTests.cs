using Compressi.Core.Services;

namespace Compressi.Tests;

public class FfmpegProgressParserTests
{
    [Fact]
    public void ApplyLine_ParsesOutTimeAndSize()
    {
        var total = TimeSpan.FromSeconds(100);
        var state = new Compressi.Core.Models.EncodingProgress();

        state = FfmpegProgressParser.ApplyLine("out_time_ms=50000000", total, state);
        state = FfmpegProgressParser.ApplyLine("total_size=1048576", total, state);

        Assert.NotNull(state.OutTime);
        Assert.InRange(state.OutTime.Value.TotalSeconds, 49, 51);
        Assert.NotNull(state.ProgressPercent);
        Assert.InRange(state.ProgressPercent.Value, 49, 51);
        Assert.Equal(1048576, state.OutputSizeBytes);
    }

    [Fact]
    public void ApplyLine_EndMarksFinished()
    {
        var state = FfmpegProgressParser.ApplyLine(
            "progress=end",
            TimeSpan.FromSeconds(10),
            new Compressi.Core.Models.EncodingProgress());

        Assert.True(state.IsFinished);
        Assert.Equal(100, state.ProgressPercent);
    }

    [Fact]
    public void ApplyLine_MutableState_AvoidsRecordAllocations()
    {
        var total = TimeSpan.FromSeconds(100);
        var state = new EncodingProgressState();

        Assert.True(FfmpegProgressParser.ApplyLine("out_time_us=25000000", total, state));
        Assert.False(FfmpegProgressParser.ApplyLine("out_time_us=25000000", total, state));
        Assert.Equal(25, state.ProgressPercent);

        Assert.True(FfmpegProgressParser.ApplyLine("total_size=2048", total, state));
        Assert.False(FfmpegProgressParser.ApplyLine("total_size=2048", total, state));
        Assert.Equal(2048, state.OutputSizeBytes);

        Assert.True(FfmpegProgressParser.ApplyLine("progress=end", total, state));
        Assert.True(state.IsFinished);
        Assert.Equal(100, state.ProgressPercent);
    }
}
