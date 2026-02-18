namespace VideoCuts.Core.Models.Transcription;

/// <summary>
/// Um segmento de transcrição com intervalo de tempo e texto.
/// </summary>
public record TranscriptSegment
{
    /// <summary>Início do segmento em segundos.</summary>
    public double StartTimeSeconds { get; init; }

    /// <summary>Fim do segmento em segundos.</summary>
    public double EndTimeSeconds { get; init; }

    /// <summary>Início do segmento em segundos (alias de StartTimeSeconds).</summary>
    public double Start => StartTimeSeconds;

    /// <summary>Fim do segmento em segundos (alias de EndTimeSeconds).</summary>
    public double End => EndTimeSeconds;

    /// <summary>Texto transcrito do segmento.</summary>
    public required string Text { get; init; }
}
