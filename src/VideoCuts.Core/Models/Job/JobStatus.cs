namespace VideoCuts.Core.Models.Job;

/// <summary>
/// Estado do job de processamento de vídeo.
/// Transições válidas: Pending → Processing → Completed | Failed.
/// </summary>
public enum JobStatus
{
    /// <summary>Aguardando processamento.</summary>
    Pending = 0,

    /// <summary>Em execução (download, transcrição, cortes, geração de clipes).</summary>
    Processing = 1,

    /// <summary>Concluído com sucesso.</summary>
    Completed = 2,

    /// <summary>Falhou (ver <see cref="VideoProcessingJob.ErrorMessage"/>).</summary>
    Failed = 3
}
