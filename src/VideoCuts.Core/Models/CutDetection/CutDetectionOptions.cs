namespace VideoCuts.Core.Models.CutDetection;

/// <summary>
/// Opções para detecção de cortes/cenas.
/// </summary>
public record CutDetectionOptions
{
    /// <summary>Limiar de sensibilidade (0–1). Valores maiores detectam mais cortes.</summary>
    public double Sensitivity { get; init; } = 0.5;

    /// <summary>Detectar cortes por mudança de cena (visual).</summary>
    public bool DetectSceneChanges { get; init; } = true;

    /// <summary>Detectar segmentos de silêncio para possível remoção.</summary>
    public bool DetectSilence { get; init; }
}
