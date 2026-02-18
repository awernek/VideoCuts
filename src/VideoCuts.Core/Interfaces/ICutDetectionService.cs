using VideoCuts.Core.Models.CutDetection;

namespace VideoCuts.Core.Interfaces;

/// <summary>
/// Contrato para detecção de cortes, mudanças de cena ou segmentos em vídeo.
/// </summary>
public interface ICutDetectionService
{
    /// <summary>
    /// Analisa o vídeo e detecta cortes/cenas conforme as opções.
    /// </summary>
    /// <param name="videoPath">Caminho local do arquivo de vídeo.</param>
    /// <param name="options">Opções de detecção (sensibilidade, tipo).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Resultado com lista de segmentos detectados.</returns>
    Task<CutDetectionResult> DetectCutsAsync(
        string videoPath,
        CutDetectionOptions options,
        CancellationToken cancellationToken = default);
}
