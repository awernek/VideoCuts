namespace VideoCuts.Core.Models.VideoDownload;

/// <summary>
/// Representa a origem de um vídeo a ser baixado.
/// </summary>
public record VideoSource
{
    /// <summary>URL do vídeo (ex.: YouTube, link direto).</summary>
    public required string Url { get; init; }

    /// <summary>Título ou identificador opcional do vídeo.</summary>
    public string? Title { get; init; }
}
