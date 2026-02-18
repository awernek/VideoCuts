using VideoCuts.Core.Models.Pipeline;

namespace VideoCuts.Core.Interfaces;

/// <summary>
/// Pipeline modular: download (opcional) → transcrição → detecção de cortes → geração de clipes.
/// Cada etapa é assíncrona e pode ser injetada/ substituída.
/// </summary>
public interface IVideoClipPipeline
{
    /// <summary>
    /// Executa o pipeline completo de forma assíncrona.
    /// </summary>
    /// <param name="request">Entrada (URL e/ou path local, opções).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Resultado com transcrição, cortes e clipes gerados.</returns>
    Task<PipelineResult> RunAsync(
        PipelineRequest request,
        CancellationToken cancellationToken = default);
}
