namespace VideoCuts.Core.Models.Transcription;

/// <summary>
/// Resultado de uma operação de transcrição.
/// </summary>
public record TranscriptionResult
{
    /// <summary>Indica se a transcrição foi concluída com sucesso.</summary>
    public bool Success { get; init; }

    /// <summary>Segmentos com texto e timestamps. Preenchido quando Success é true e IncludeTimestamps foi usado.</summary>
    public IReadOnlyList<TranscriptSegment> Segments { get; init; } = Array.Empty<TranscriptSegment>();

    /// <summary>Texto completo da transcrição (quando não segmentado).</summary>
    public string? FullText { get; init; }

    /// <summary>Idioma detectado ou utilizado (código ISO).</summary>
    public string? DetectedLanguage { get; init; }

    /// <summary>Mensagem de erro. Preenchido quando Success é false.</summary>
    public string? ErrorMessage { get; init; }
}
