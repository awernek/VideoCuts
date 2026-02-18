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

    /// <summary>Modelo Ollama (ex.: llama3.1:8b; instale com ollama pull llama3.1:8b).</summary>
    public string Model { get; set; } = "llama3.1:8b";

    /// <summary>Número de tentativas adicionais em caso de falha (0 = só uma chamada).</summary>
    public int RetryCount { get; set; } = 2;

    /// <summary>Delay em ms entre tentativas de retry.</summary>
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>Tamanho máximo de transcrição por chunk em caracteres (0 = não dividir; padrão 12000 para vídeos longos).</summary>
    public int ChunkMaxCharacters { get; set; } = 12_000;
}
