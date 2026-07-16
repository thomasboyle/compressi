using Compressi.Core.Models;

namespace Compressi.Core.Services;

public interface IMediaProbeService
{
    Task<VideoFile> ProbeAsync(string filePath, CancellationToken cancellationToken = default);
}
