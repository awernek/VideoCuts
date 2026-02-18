using VideoCuts.Core.Models.Transcription;

namespace VideoCuts.Core.Interfaces;

/// <summary>
/// Contrato para transcrição de áudio/vídeo em texto.
/// </summary>
public interface ITranscriptionService
{
    /// <summary>
    /// Transcreve o áudio do arquivo de vídeo em texto, opcionalmente com timestamps.
    /// </summary>
    /// <param name="videoPath">Caminho local do arquivo de vídeo.</param>
    /// <param name="options">Opções de transcrição (idioma, timestamps).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Resultado com segmentos ou texto completo.</returns>
    Task<TranscriptionResult> TranscribeAsync(
        string videoPath,
        TranscriptionOptions options,
        CancellationToken cancellationToken = default);
}
