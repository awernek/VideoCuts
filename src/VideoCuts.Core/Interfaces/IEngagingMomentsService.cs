using VideoCuts.Core.Models.CutSuggestion;
using VideoCuts.Core.Models.Transcription;

namespace VideoCuts.Core.Interfaces;

/// <summary>
/// Contrato para identificar momentos mais engajantes em um texto de transcrição via LLM.
/// Retorna cortes sugeridos com timestamps.
/// </summary>
public interface IEngagingMomentsService
{
    /// <summary>
    /// Envia o texto da transcrição para um LLM e obtém timestamps dos cortes sugeridos para short-form.
    /// </summary>
    /// <param name="transcriptText">Texto completo da transcrição (com ou sem timestamps no texto).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Lista de <see cref="VideoCut"/> com início, fim e descrição opcional.</returns>
    Task<IReadOnlyList<VideoCut>> GetEngagingMomentsAsync(
        string transcriptText,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Envia os segmentos da transcrição (com timestamps) para o LLM e obtém cortes sugeridos.
    /// O texto é formatado com timestamps para maior precisão na resposta.
    /// </summary>
    Task<IReadOnlyList<VideoCut>> GetEngagingMomentsAsync(
        IReadOnlyList<TranscriptSegment> segments,
        CancellationToken cancellationToken = default);
}
