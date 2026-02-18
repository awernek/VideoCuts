using VideoCuts.Core.Models.CutSuggestion;

namespace VideoCuts.Core.Models.Pipeline;

/// <summary>
/// Um clipe gerado pelo pipeline, com o corte usado e o caminho do arquivo.
/// </summary>
public record GeneratedClip
{
    /// <summary>Corte aplicado (início, fim, descrição).</summary>
    public required VideoCut Cut { get; init; }

    /// <summary>Caminho do arquivo de vídeo gerado.</summary>
    public required string OutputPath { get; init; }

    /// <summary>Índice do clipe (1-based) na ordem gerada.</summary>
    public int Index { get; init; }
}
