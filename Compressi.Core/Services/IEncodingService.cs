using Compressi.Core.Models;

namespace Compressi.Core.Services;

public interface IEncodingService
{
    Task<CompressionResult> EncodeAsync(
        CompressionJob job,
        IProgress<EncodingProgressState>? progress = null,
        CancellationToken cancellationToken = default);
}
