using Compressi.Core.Models;

namespace Compressi.Core.Services;

public interface IEncodingService
{
    Task<CompressionResult> EncodeAsync(
        CompressionJob job,
        IProgress<EncodingProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
