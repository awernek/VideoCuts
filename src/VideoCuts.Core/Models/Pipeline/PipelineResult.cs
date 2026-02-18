using VideoCuts.Core.Models.CutSuggestion;
using VideoCuts.Core.Models.Transcription;

namespace VideoCuts.Core.Models.Pipeline;

/// <summary>
/// Resultado da execução do pipeline.
/// </summary>
public record PipelineResult
{
    /// <summary>Indica se o pipeline concluiu com sucesso.</summary>
    public bool Success { get; init; }

    /// <summary>Caminho do vídeo usado (local após download ou path informado).</summary>
    public string? LocalVideoPath { get; init; }

    /// <summary>Resultado da transcrição (quando executada).</summary>
    public TranscriptionResult? Transcript { get; init; }

    /// <summary>Cortes identificados (momentos engajantes ou detecção).</summary>
    public IReadOnlyList<VideoCut> Cuts { get; init; } = Array.Empty<VideoCut>();

    /// <summary>Clipes gerados (arquivos de vídeo).</summary>
    public IReadOnlyList<GeneratedClip> GeneratedClips { get; init; } = Array.Empty<GeneratedClip>();

    /// <summary>Mensagem de erro quando <see cref="Success"/> é false.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Tempos de execução por etapa (Transcription, EngagingMoments, ClipGeneration), quando disponíveis.</summary>
    public IReadOnlyList<PipelineStageTiming>? StageTimings { get; init; }
}
