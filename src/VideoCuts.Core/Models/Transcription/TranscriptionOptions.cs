namespace VideoCuts.Core.Models.Transcription;

/// <summary>
/// Opções para o serviço de transcrição.
/// </summary>
public record TranscriptionOptions
{
    /// <summary>Idioma esperado do áudio (código ISO, ex.: pt-BR). Se null, tenta detecção automática.</summary>
    public string? Language { get; init; }

    /// <summary>Se true, retorna segmentos com timestamps; se false, apenas texto contínuo.</summary>
    public bool IncludeTimestamps { get; init; } = true;
}
