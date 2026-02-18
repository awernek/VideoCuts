using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VideoCuts.Core.Interfaces;
using VideoCuts.Core.Models.CutSuggestion;
using VideoCuts.Core.Models.Transcription;

namespace VideoCuts.Infrastructure.EngagingMoments;

/// <summary>
/// Identifica momentos mais engajantes para short-form enviando a transcrição a um LLM (API OpenAI-compatível).
/// Retorna timestamps para cortes mapeados em <see cref="VideoCut"/>.
/// </summary>
public class LlmEngagingMomentsService : IEngagingMomentsService
{
    private const string DefaultPrompt = "Identify the most engaging moments for short-form content.";
    private readonly HttpClient _httpClient;
    private readonly string _model;
    private readonly string _apiBase;

    /// <summary>
    /// Cria o serviço contra uma API de chat compatível com OpenAI.
    /// </summary>
    /// <param name="apiKey">Chave da API (Bearer).</param>
    /// <param name="model">Modelo a usar (ex.: gpt-4o-mini, gpt-4o).</param>
    /// <param name="httpClient">Cliente HTTP opcional.</param>
    /// <param name="apiBase">Base URL (padrão: https://api.openai.com/v1).</param>
    public LlmEngagingMomentsService(
        string apiKey,
        string model = "gpt-4o-mini",
        HttpClient? httpClient = null,
        string? apiBase = null)
    {
        _model = model ?? "gpt-4o-mini";
        _apiBase = apiBase ?? "https://api.openai.com/v1";
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        var baseUrl = _apiBase.TrimEnd('/') + "/";
        _httpClient.BaseAddress = new Uri(baseUrl);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<VideoCut>> GetEngagingMomentsAsync(
        IReadOnlyList<TranscriptSegment> segments,
        CancellationToken cancellationToken = default)
    {
        if (segments == null || segments.Count == 0)
            return Task.FromResult<IReadOnlyList<VideoCut>>(Array.Empty<VideoCut>());

        string text = string.Join("\n", segments.Select(s =>
            $"[{s.StartTimeSeconds:F1}s - {s.EndTimeSeconds:F1}s] {s.Text}"));
        return GetEngagingMomentsAsync(text, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<VideoCut>> GetEngagingMomentsAsync(
        string transcriptText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(transcriptText))
            return Array.Empty<VideoCut>();

        string systemPrompt = """
            You are an expert at identifying the most engaging moments in long-form content for short-form clips (e.g. TikTok, Reels, Shorts).
            Given a transcript (optionally with timestamps), return a JSON object with exactly one property "cuts" that is an array of objects.
            Each object must have "start" (number, seconds), "end" (number, seconds), and "description" (string, brief reason why this moment is engaging).
            Use the timestamps from the transcript when available; otherwise estimate. Return only valid JSON, no markdown or extra text.
            """;

        string userContent = $"{DefaultPrompt}\n\nTranscript:\n{transcriptText}";

        var request = new ChatRequest
        {
            Model = _model,
            Messages = new[]
            {
                new ChatMessage { Role = "system", Content = systemPrompt },
                new ChatMessage { Role = "user", Content = userContent }
            },
            ResponseFormat = new ResponseFormat { Type = "json_object" }
        };

        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var body = new StringContent(json, Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("chat/completions", body, cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"LLM API error ({(int)response.StatusCode}): {error}");
        }

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var chatResponse = JsonSerializer.Deserialize<ChatResponse>(responseJson, JsonOptions);

        string? content = chatResponse?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(content))
            return Array.Empty<VideoCut>();

        return EngagingMomentsJsonParser.Parse(content);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private sealed class ChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("messages")]
        public ChatMessage[]? Messages { get; set; }

        [JsonPropertyName("response_format")]
        public ResponseFormat? ResponseFormat { get; set; }
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; } = "";

        [JsonPropertyName("content")]
        public string Content { get; set; } = "";
    }

    private sealed class ResponseFormat
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "json_object";
    }

    private sealed class ChatResponse
    {
        [JsonPropertyName("choices")]
        public ChatChoice[]? Choices { get; set; }
    }

    private sealed class ChatChoice
    {
        [JsonPropertyName("message")]
        public ChatMessage? Message { get; set; }
    }
}
