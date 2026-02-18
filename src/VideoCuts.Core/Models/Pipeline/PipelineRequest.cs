using VideoCuts.Core.Models.Transcription;
using VideoCuts.Core.Models.VideoDownload;

namespace VideoCuts.Core.Models.Pipeline;

/// <summary>
/// Entrada do pipeline: vídeo por URL (com download opcional) ou por caminho local.
/// </summary>
public record PipelineRequest
{
    /// <summary>Origem por URL. Se preenchido e <see cref="DownloadIfUrl"/> for true, o vídeo será baixado.</summary>
    public VideoSource? Source { get; init; }

    /// <summary>Caminho local do vídeo. Use quando não for usar download (arquivo já no disco).</summary>
    public string? LocalVideoPath { get; init; }

    /// <summary>Se true e <see cref="Source"/> estiver definido, baixa o vídeo antes de transcrever.</summary>
    public bool DownloadIfUrl { get; init; }

    /// <summary>Pasta onde os clipes gerados serão salvos. Se null, usa o diretório do vídeo.</summary>
    public string? OutputDirectory { get; init; }

    /// <summary>Se true, cada clipe é convertido para vertical (9:16).</summary>
    public bool ConvertClipsToVertical { get; init; }

    /// <summary>Número máximo de clipes a gerar. Null = todos os cortes sugeridos.</summary>
    public int? MaxClips { get; init; }

    /// <summary>Opções de transcrição (idioma, timestamps).</summary>
    public TranscriptionOptions TranscriptionOptions { get; init; } = new();
}
