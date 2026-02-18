namespace VideoCuts.Infrastructure.EngagingMoments;

/// <summary>
/// Configuração para <see cref="OllamaEngagingMomentsService"/>.
/// Carregada de appsettings (seção "Ollama") e injetada via IOptions&lt;OllamaEngagingMomentsOptions&gt;.
/// </summary>
public class OllamaEngagingMomentsOptions
{
    /// <summary>Nome da seção no appsettings.json.</summary>
    public const string SectionName = "Ollama";

    /// <summary>Base URL da API Ollama (ex.: http://localhost:11434).</summary>
    public string BaseUrl { get; set; } = "http://localhost:11434";

    /// <summary>Modelo a usar (ex.: llama3).</summary>
    public string Model { get; set; } = "llama3";

    /// <summary>Número de tentativas adicionais em caso de falha (0 = só uma chamada).</summary>
    public int RetryCount { get; set; } = 2;

    /// <summary>Delay em ms entre tentativas de retry.</summary>
    public int RetryDelayMs { get; set; } = 1000;
}
