namespace VideoCuts.Core.Models.VideoEditing;

/// <summary>
/// Resultado de uma operação de edição de vídeo.
/// </summary>
public record EditResult
{
    /// <summary>Indica se a edição foi concluída com sucesso.</summary>
    public bool Success { get; init; }

    /// <summary>Caminho do arquivo de vídeo gerado. Preenchido quando Success é true.</summary>
    public string? OutputPath { get; init; }

    /// <summary>Duração do vídeo de saída em segundos.</summary>
    public double? OutputDurationSeconds { get; init; }

    /// <summary>Mensagem de erro. Preenchido quando Success é false.</summary>
    public string? ErrorMessage { get; init; }
}
