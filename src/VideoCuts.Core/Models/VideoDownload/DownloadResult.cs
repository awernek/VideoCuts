namespace VideoCuts.Core.Models.VideoDownload;

/// <summary>
/// Resultado de uma operação de download de vídeo.
/// </summary>
public record DownloadResult
{
    /// <summary>Indica se o download foi concluído com sucesso.</summary>
    public bool Success { get; init; }

    /// <summary>Caminho local do arquivo baixado. Preenchido quando Success é true.</summary>
    public string? LocalPath { get; init; }

    /// <summary>Mensagem de erro ou detalhe. Preenchido quando Success é false.</summary>
    public string? ErrorMessage { get; init; }
}
