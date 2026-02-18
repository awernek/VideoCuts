namespace VideoCuts.Core.Models.Pipeline;

/// <summary>
/// Tempo de execução de uma etapa do pipeline.
/// </summary>
/// <param name="StageName">Nome da etapa (ex.: Transcription, EngagingMoments, ClipGeneration).</param>
/// <param name="ElapsedMilliseconds">Duração em milissegundos.</param>
public record PipelineStageTiming(string StageName, long ElapsedMilliseconds);
