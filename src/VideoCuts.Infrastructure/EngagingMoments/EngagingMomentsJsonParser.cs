using System.Text.Json;
using System.Text.Json.Serialization;
using VideoCuts.Core.Models.CutSuggestion;

namespace VideoCuts.Infrastructure.EngagingMoments;

/// <summary>
/// Parseia respostas JSON de LLM (formato "cuts": [{ "start", "end", "description" }]) em <see cref="VideoCut"/>.
/// Lógica de negócio compartilhada e testável sem I/O.
/// </summary>
public static class EngagingMomentsJsonParser
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Parseia o texto de resposta do LLM (JSON com propriedade "cuts") em lista de <see cref="VideoCut"/>.
    /// Aceita conteúdo com ou sem wrapper markdown (```json ... ```).
    /// Cortes inválidos (start &lt; 0 ou end &lt;= start) são filtrados.
    /// Em falha de parse ou conteúdo vazio, retorna lista vazia (nunca lança).
    /// </summary>
    public static IReadOnlyList<VideoCut> Parse(string? responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return Array.Empty<VideoCut>();

        string content = responseText.Trim();
        if (content.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            content = content.Replace("```json", "").Replace("```", "").Trim();
        if (content.StartsWith("```", StringComparison.OrdinalIgnoreCase))
            content = content.Replace("```", "").Trim();

        try
        {
            var cutsResponse = JsonSerializer.Deserialize<CutsResponse>(content, JsonOptions);
            if (cutsResponse?.Cuts == null || cutsResponse.Cuts.Length == 0)
                return Array.Empty<VideoCut>();

            return cutsResponse.Cuts
                .Where(c => c.Start >= 0 && c.End > c.Start)
                .Select(c => new VideoCut
                {
                    StartSeconds = c.Start,
                    EndSeconds = c.End,
                    Description = c.Description
                })
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<VideoCut>();
        }
    }

    private sealed class CutsResponse
    {
        [JsonPropertyName("cuts")]
        public CutItem[]? Cuts { get; set; }
    }

    private sealed class CutItem
    {
        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}
