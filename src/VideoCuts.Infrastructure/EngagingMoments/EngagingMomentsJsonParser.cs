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
    /// Aceita conteúdo com ou sem wrapper markdown (```json ... ```) e extrai JSON mesmo com texto em volta.
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
        content = ExtractJsonObject(content);

        try
        {
            var cutsResponse = JsonSerializer.Deserialize<CutsResponse>(content, JsonOptions);
            if (cutsResponse?.Cuts == null || cutsResponse.Cuts.Length == 0)
                return Array.Empty<VideoCut>();

            return cutsResponse.Cuts
                .Select(c =>
                {
                    double start = c.Start >= 0 ? c.Start : c.StartSeconds;
                    double end = c.End >= 0 ? c.End : c.EndSeconds;
                    if (start < 0 || end <= start) return null;
                    return new VideoCut { StartSeconds = start, EndSeconds = end, Description = c.Description };
                })
                .Where(c => c != null)
                .Cast<VideoCut>()
                .ToList();
        }
        catch (JsonException)
        {
            return Array.Empty<VideoCut>();
        }
    }

    /// <summary>Extrai o primeiro objeto JSON da string (entre { e } balanceados) para tolerar texto em volta.</summary>
    private static string ExtractJsonObject(string text)
    {
        int start = text.IndexOf('{');
        if (start < 0) return text;
        int depth = 0;
        for (int i = start; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0) return text.Substring(start, i - start + 1);
            }
        }
        return text;
    }

    private sealed class CutsResponse
    {
        [JsonPropertyName("cuts")]
        public CutItem[]? Cuts { get; set; }
    }

    private sealed class CutItem
    {
        [JsonPropertyName("start")]
        public double Start { get; set; } = -1;

        [JsonPropertyName("end")]
        public double End { get; set; } = -1;

        [JsonPropertyName("start_seconds")]
        public double StartSeconds { get; set; } = -1;

        [JsonPropertyName("end_seconds")]
        public double EndSeconds { get; set; } = -1;

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }
}
