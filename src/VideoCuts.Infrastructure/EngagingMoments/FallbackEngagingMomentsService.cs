using Microsoft.Extensions.Logging;
using VideoCuts.Core.Interfaces;
using VideoCuts.Core.Models.CutSuggestion;
using VideoCuts.Core.Models.Transcription;

namespace VideoCuts.Infrastructure.EngagingMoments;

/// <summary>
/// Decorator que tenta primeiro o serviço primário e, em caso de falha, usa o fallback.
/// Mantém as duas implementações intercambiáveis via <see cref="IEngagingMomentsService"/>.
/// </summary>
public class FallbackEngagingMomentsService : IEngagingMomentsService
{
    private readonly IEngagingMomentsService _primary;
    private readonly IEngagingMomentsService _fallback;
    private readonly ILogger<FallbackEngagingMomentsService> _logger;

    public FallbackEngagingMomentsService(
        IEngagingMomentsService primary,
        IEngagingMomentsService fallback,
        ILogger<FallbackEngagingMomentsService> logger)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<VideoCut>> GetEngagingMomentsAsync(
        IReadOnlyList<TranscriptSegment> segments,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithFallbackAsync(
            (svc, ct) => svc.GetEngagingMomentsAsync(segments, ct),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<VideoCut>> GetEngagingMomentsAsync(
        string transcriptText,
        CancellationToken cancellationToken = default)
    {
        return ExecuteWithFallbackAsync(
            (svc, ct) => svc.GetEngagingMomentsAsync(transcriptText, ct),
            cancellationToken);
    }

    private async Task<IReadOnlyList<VideoCut>> ExecuteWithFallbackAsync(
        Func<IEngagingMomentsService, CancellationToken, Task<IReadOnlyList<VideoCut>>> invoke,
        CancellationToken cancellationToken)
    {
        try
        {
            return await invoke(_primary, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Primary engaging moments service failed; falling back to secondary.");
            return await invoke(_fallback, cancellationToken).ConfigureAwait(false);
        }
    }
}
