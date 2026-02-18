namespace VideoCuts.Core.Models.CutDetection;

/// <summary>
/// Resultado da detecção de cortes em um vídeo.
/// </summary>
public record CutDetectionResult
{
    /// <summary>Indica se a detecção foi concluída com sucesso.</summary>
    public bool Success { get; init; }

    /// <summary>Segmentos detectados (cortes ou cenas a manter).</summary>
    public IReadOnlyList<CutSegment> Segments { get; init; } = Array.Empty<CutSegment>();

    /// <summary>Mensagem de erro. Preenchido quando Success é false.</summary>
    public string? ErrorMessage { get; init; }
}
