using VideoCuts.Core.Models.VideoDownload;

namespace VideoCuts.Core.Interfaces;

/// <summary>
/// Contrato para download de vídeos a partir de uma URL ou fonte externa.
/// </summary>
public interface IVideoDownloader
{
    /// <summary>
    /// Baixa o vídeo da fonte especificada conforme as opções.
    /// </summary>
    /// <param name="source">Origem do vídeo (URL, etc.).</param>
    /// <param name="options">Opções de download (pasta, formato, qualidade).</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Resultado com caminho local ou mensagem de erro.</returns>
    Task<DownloadResult> DownloadAsync(
        VideoSource source,
        DownloadOptions options,
        CancellationToken cancellationToken = default);
}
