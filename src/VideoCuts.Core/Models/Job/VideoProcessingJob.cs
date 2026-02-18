namespace VideoCuts.Core.Models.Job;

/// <summary>
/// Job de processamento de vídeo no pipeline.
/// Controle de status e transições centralizado; o pipeline apenas invoca os métodos de transição.
/// </summary>
public class VideoProcessingJob
{
    /// <summary>Identificador único do job.</summary>
    public Guid Id { get; }

    /// <summary>Estado atual. Transições: Pending → Processing → Completed | Failed.</summary>
    public JobStatus Status { get; private set; }

    /// <summary>Data/hora de criação (UTC).</summary>
    public DateTimeOffset CreatedAt { get; }

    /// <summary>Última atualização de status (UTC). Null enquanto Status for Pending.</summary>
    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>Mensagem de erro quando Status é Failed.</summary>
    public string? ErrorMessage { get; private set; }

    /// <summary>
    /// Cria um job em estado Pending.
    /// </summary>
    /// <param name="id">Id do job; se não informado, gera um novo Guid.</param>
    public VideoProcessingJob(Guid? id = null)
    {
        Id = id ?? Guid.NewGuid();
        Status = JobStatus.Pending;
        CreatedAt = DateTimeOffset.UtcNow;
        UpdatedAt = null;
        ErrorMessage = null;
    }

    /// <summary>
    /// Marca o job como em processamento. Válido apenas a partir de Pending.
    /// </summary>
    public void MarkProcessing()
    {
        if (Status != JobStatus.Pending)
            return;
        Status = JobStatus.Processing;
        UpdatedAt = DateTimeOffset.UtcNow;
        ErrorMessage = null;
    }

    /// <summary>
    /// Marca o job como concluído com sucesso. Válido apenas a partir de Processing.
    /// </summary>
    public void MarkCompleted()
    {
        if (Status != JobStatus.Processing)
            return;
        Status = JobStatus.Completed;
        UpdatedAt = DateTimeOffset.UtcNow;
        ErrorMessage = null;
    }

    /// <summary>
    /// Marca o job como falho. Válido apenas a partir de Processing (ou Pending, para falha antes de iniciar).
    /// </summary>
    /// <param name="message">Mensagem de erro (opcional).</param>
    public void MarkFailed(string? message = null)
    {
        if (Status != JobStatus.Processing && Status != JobStatus.Pending)
            return;
        Status = JobStatus.Failed;
        UpdatedAt = DateTimeOffset.UtcNow;
        ErrorMessage = message;
    }
}
