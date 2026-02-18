namespace VideoCuts.Core.Models.CutDetection;

/// <summary>
/// Representa um segmento (intervalo) identificado para corte ou preservação.
/// </summary>
public record CutSegment
{
    /// <summary>Início do segmento em segundos.</summary>
    public double StartTimeSeconds { get; init; }

    /// <summary>Fim do segmento em segundos.</summary>
    public double EndTimeSeconds { get; init; }

    /// <summary>Tipo do segmento (ex.: corte, silêncio, cena).</summary>
    public string? SegmentType { get; init; }
}
