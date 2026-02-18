using VideoCuts.Core.Models.CutDetection;

namespace VideoCuts.Core.Models.VideoEditing;

/// <summary>
/// Requisição para edição de vídeo (cortar/concatenar segmentos).
/// </summary>
public record EditRequest
{
    /// <summary>Caminho do vídeo de entrada.</summary>
    public required string InputVideoPath { get; init; }

    /// <summary>Segmentos a manter (intervalos em segundos). A ordem define a ordem no vídeo final.</summary>
    public required IReadOnlyList<CutSegment> SegmentsToKeep { get; init; }

    /// <summary>Caminho do vídeo de saída. Se null, gera em mesmo diretório com sufixo.</summary>
    public string? OutputPath { get; init; }

    /// <summary>Formato de saída (ex.: mp4). Se null, usa o mesmo do input.</summary>
    public string? OutputFormat { get; init; }

    /// <summary>Se true, converte a saída para formato vertical 9:16 (com scale e pad).</summary>
    public bool ConvertToVertical { get; init; }
}
