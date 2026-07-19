using System.Globalization;

namespace Compressi.Core.Services;

internal static class VideoEncoderArgumentBuilder
{
    public static void AppendCpuAv1Args(
        IList<string> args,
        int threadCount,
        int crf,
        int preset,
        int? tileRows = null,
        int? tileColumns = null)
    {
        var encoder = FfmpegEncoderCatalog.GetCpuAv1Encoder();
        args.Add("-c:v");
        args.Add(encoder);
        args.Add("-crf");
        args.Add(crf.ToString(CultureInfo.InvariantCulture));

        if (encoder == "libsvtav1")
        {
            args.Add("-preset");
            args.Add(preset.ToString(CultureInfo.InvariantCulture));
            var svtParams = $"lp={threadCount}";
            if (tileRows is > 0 && tileColumns is > 0)
            {
                svtParams += $":tile-rows={tileRows}:tile-columns={tileColumns}";
            }

            args.Add("-svtav1-params");
            args.Add(svtParams);
        }
        else
        {
            args.Add("-cpu-used");
            args.Add(preset.ToString(CultureInfo.InvariantCulture));
        }

        args.Add("-threads");
        args.Add(threadCount.ToString(CultureInfo.InvariantCulture));
    }

    public static void AppendCpuAv1BitrateArgs(
        IList<string> args,
        int threadCount,
        int bitrateKbps,
        int preset,
        int passNumber,
        string passLogFile)
    {
        AppendCpuAv1BitrateCore(args, threadCount, bitrateKbps, preset, constrainRate: true);
        args.Add("-pass");
        args.Add(passNumber.ToString(CultureInfo.InvariantCulture));
        args.Add("-passlogfile");
        args.Add(passLogFile);
    }

    /// <summary>
    /// Single-pass bitrate encode for EightMB corrective re-encodes.
    /// </summary>
    public static void AppendCpuAv1SinglePassBitrateArgs(
        IList<string> args,
        int threadCount,
        int bitrateKbps,
        int preset)
    {
        AppendCpuAv1BitrateCore(args, threadCount, bitrateKbps, preset, constrainRate: true);
    }

    private static void AppendCpuAv1BitrateCore(
        IList<string> args,
        int threadCount,
        int bitrateKbps,
        int preset,
        bool constrainRate)
    {
        var encoder = FfmpegEncoderCatalog.GetCpuAv1Encoder();
        args.Add("-c:v");
        args.Add(encoder);
        args.Add("-b:v");
        args.Add($"{bitrateKbps}k");

        // libsvtav1: equal -b:v/-maxrate forces CBR (rc=2), which SVT rejects for
        // RANDOM_ACCESS. Max bitrate is also CRF-only. Use plain -b:v (VBR) instead.
        if (constrainRate && encoder != "libsvtav1")
        {
            args.Add("-maxrate");
            args.Add($"{bitrateKbps}k");
            args.Add("-bufsize");
            args.Add($"{Math.Max(bitrateKbps * 2, bitrateKbps)}k");
        }

        if (encoder == "libsvtav1")
        {
            args.Add("-preset");
            args.Add(preset.ToString(CultureInfo.InvariantCulture));
            args.Add("-svtav1-params");
            args.Add($"lp={threadCount}:rc=1");
        }
        else
        {
            args.Add("-cpu-used");
            args.Add(preset.ToString(CultureInfo.InvariantCulture));
        }

        args.Add("-threads");
        args.Add(threadCount.ToString(CultureInfo.InvariantCulture));
    }

    public static void AppendGpuAv1Args(IList<string> args, string gpuEncoder, int quality)
    {
        args.Add("-c:v");
        args.Add(gpuEncoder);

        switch (gpuEncoder)
        {
            case "av1_nvenc":
                args.Add("-preset");
                args.Add("p7");
                args.Add("-cq");
                args.Add(quality.ToString(CultureInfo.InvariantCulture));
                break;
            case "av1_qsv":
                args.Add("-global_quality");
                args.Add(quality.ToString(CultureInfo.InvariantCulture));
                break;
            case "av1_amf":
                args.Add("-quality");
                args.Add("quality");
                args.Add("-rc");
                args.Add("cqp");
                args.Add("-qp_i");
                args.Add(quality.ToString(CultureInfo.InvariantCulture));
                args.Add("-qp_p");
                args.Add(quality.ToString(CultureInfo.InvariantCulture));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(gpuEncoder), gpuEncoder, "Unsupported GPU encoder.");
        }
    }

    public static (int tileRows, int tileColumns)? GetTileConfig(int width, int height)
    {
        if (height < 1080)
        {
            return null;
        }

        return height >= 2160 ? (2, 2) : (1, 2);
    }
}
