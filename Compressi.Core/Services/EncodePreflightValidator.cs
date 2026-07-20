using Compressi.Core.Models;

namespace Compressi.Core.Services;

public static class EncodePreflightValidator
{
    public static void Validate(CompressionJob job)
    {
        if (job.Source.Duration <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("This file has zero duration and cannot be compressed.");
        }

        if (string.IsNullOrWhiteSpace(job.Source.VideoCodec) || job.Source.VideoCodec == "unknown")
        {
            throw new InvalidOperationException("Could not detect a supported video codec in this file.");
        }

        if (job.Format == OutputFormat.WebM && job.VideoCodec == VideoCodec.H264)
        {
            throw new InvalidOperationException("WebM does not support H.264. Choose AV1, or use MP4/MKV.");
        }

        if (job.Preset == CompressionPreset.EightMB)
        {
            var plan = EightMbBitrateResolver.Resolve(job.Source, job.Advanced);
            if (!plan.IsViable)
            {
                throw new InvalidOperationException(plan.FailureReason
                    ?? "Can't hit 8 MB with audio at this length.");
            }
        }

        var outputDirectory = job.Advanced?.OutputDirectory
            ?? Path.GetDirectoryName(job.Source.FilePath)
            ?? Environment.CurrentDirectory;

        var drive = Path.GetPathRoot(Path.GetFullPath(outputDirectory));
        if (drive is not null)
        {
            try
            {
                var root = drive.TrimEnd('\\');
                var info = DriveInfo.GetDrives().FirstOrDefault(d =>
                    string.Equals(d.Name.TrimEnd('\\'), root, StringComparison.OrdinalIgnoreCase));
                if (info is { IsReady: true, AvailableFreeSpace: < 256 * 1024 * 1024 })
                {
                    throw new InvalidOperationException("Not enough free disk space in the output folder.");
                }
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch
            {
                // Ignore drive inspection failures.
            }
        }
    }
}
