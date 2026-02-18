using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VideoCuts.Core.Interfaces;
using VideoCuts.Core.Models.CutSuggestion;
using VideoCuts.Core.Models.Transcription;

namespace VideoCuts.Infrastructure.EngagingMoments;

/// <summary>
/// Implementação de <see cref="IEngagingMomentsService"/> usando Ollama (LLM local).
/// Chama http://localhost:11434/api/generate com transcrição e interpreta a resposta como JSON de cortes.
/// </summary>
public class OllamaEngagingMomentsService : IEngagingMomentsService
{
    private readonly HttpClient _httpClient;
    private readonly OllamaEngagingMomentsOptions _options;
    private readonly ILogger<OllamaEngagingMomentsService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public OllamaEngagingMomentsService(
        HttpClient httpClient,
        IOptions<OllamaEngagingMomentsOptions> options,
        ILogger<OllamaEngagingMomentsService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private string GenerateEndpoint => _options.BaseUrl.TrimEnd('/') + "/api/generate";

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

        string prompt = BuildPrompt(transcriptText);
        var request = new OllamaGenerateRequest
        {
            Model = _options.Model,
            Prompt = prompt,
            Stream = false,
            Format = "json"
        };

        int maxAttempts = _options.RetryCount + 1;
        Exception? lastException = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var response = await _httpClient.PostAsJsonAsync(
                    GenerateEndpoint,
                    request,
                    JsonOptions,
                    cancellationToken).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    throw new InvalidOperationException(
                        $"Ollama API returned {(int)response.StatusCode}: {errorBody}");
                }

                var ollamaResponse = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(
                    JsonOptions, cancellationToken).ConfigureAwait(false);

                return ParseCutsFromResponse(ollamaResponse?.Response);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogWarning(ex, "Ollama attempt {Attempt}/{Max} failed", attempt, maxAttempts);
                if (attempt < maxAttempts)
                    await Task.Delay(_options.RetryDelayMs, cancellationToken).ConfigureAwait(false);
            }
        }

        _logger.LogError(lastException, "All Ollama attempts failed; returning no cuts");
        return Array.Empty<VideoCut>();
    }

    private static string BuildPrompt(string transcriptText)
    {
        return """
            You are an expert at identifying the most engaging moments in long-form content for short-form clips (e.g. TikTok, Reels, Shorts).
            Given a transcript (optionally with timestamps), return a JSON object with exactly one property "cuts" that is an array of objects.
            Each object must have "start" (number, seconds), "end" (number, seconds), and "description" (string, brief reason why this moment is engaging).
            Use the timestamps from the transcript when available; otherwise estimate. Return only valid JSON, no markdown or extra text.

            Transcript:
            """ + transcriptText;
    }

    private IReadOnlyList<VideoCut> ParseCutsFromResponse(string? responseText)
    {
        var cuts = EngagingMomentsJsonParser.Parse(responseText);
        if (cuts.Count == 0 && !string.IsNullOrWhiteSpace(responseText))
            _logger.LogWarning("Ollama returned no valid cuts (empty or invalid JSON).");
        return cuts;
    }

    private sealed class OllamaGenerateRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = "";

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("format")]
        public string? Format { get; set; }
    }

    private sealed class OllamaGenerateResponse
    {
        [JsonPropertyName("response")]
        public string? Response { get; set; }
    }
}
