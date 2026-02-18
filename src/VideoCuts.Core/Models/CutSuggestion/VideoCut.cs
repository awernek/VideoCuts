namespace VideoCuts.Core.Models.CutSuggestion;

/// <summary>
/// Representa um corte sugerido para conteúdo short-form, com timestamps de início e fim.
/// </summary>
public record VideoCut
{
    /// <summary>Início do corte em segundos.</summary>
    public double StartSeconds { get; init; }

    /// <summary>Fim do corte em segundos.</summary>
    public double EndSeconds { get; init; }

    /// <summary>Descrição opcional do motivo (ex.: momento de maior engajamento).</summary>
    public string? Description { get; init; }
}
