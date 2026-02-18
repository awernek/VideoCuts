namespace VideoCuts.Core.Models.VideoDownload;

/// <summary>
/// Opções para o download de vídeo.
/// </summary>
public record DownloadOptions
{
    /// <summary>Pasta de destino do arquivo baixado. Se null, usa diretório padrão.</summary>
    public string? OutputDirectory { get; init; }

    /// <summary>Formato desejado (ex.: mp4, webm). Se null, usa formato padrão da origem.</summary>
    public string? Format { get; init; }

    /// <summary>Qualidade ou resolução preferida (ex.: 1080p, best).</summary>
    public string? Quality { get; init; }
}
