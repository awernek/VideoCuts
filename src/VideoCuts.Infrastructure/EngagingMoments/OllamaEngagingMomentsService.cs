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
    public async Task<IReadOnlyList<VideoCut>> GetEngagingMomentsAsync(
        IReadOnlyList<TranscriptSegment> segments,
        CancellationToken cancellationToken = default)
    {
        if (segments == null || segments.Count == 0)
            return Array.Empty<VideoCut>();

        int chunkMax = _options.ChunkMaxCharacters;
        if (chunkMax <= 0)
        {
            string text = string.Join("\n", segments.Select(s =>
                $"[{s.StartTimeSeconds:F1}s - {s.EndTimeSeconds:F1}s] {s.Text}"));
            return await GetEngagingMomentsAsync(text, cancellationToken).ConfigureAwait(false);
        }

        var chunks = BuildChunks(segments, chunkMax);
        if (chunks.Count <= 1)
        {
            string text = string.Join("\n", segments.Select(s =>
                $"[{s.StartTimeSeconds:F1}s - {s.EndTimeSeconds:F1}s] {s.Text}"));
            return await GetEngagingMomentsAsync(text, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation("Transcript split into {ChunkCount} chunks (max {ChunkMax} chars each) for Ollama", chunks.Count, chunkMax);
        var allCuts = new List<VideoCut>();
        for (int i = 0; i < chunks.Count; i++)
        {
            var chunkSegments = chunks[i];
            string chunkText = string.Join("\n", chunkSegments.Select(s =>
                $"[{s.StartTimeSeconds:F1}s - {s.EndTimeSeconds:F1}s] {s.Text}"));
            var cuts = await GetEngagingMomentsAsync(chunkText, cancellationToken).ConfigureAwait(false);
            if (cuts.Count > 0)
            {
                allCuts.AddRange(cuts);
                _logger.LogInformation("Chunk {ChunkIndex}/{Total} returned {CutCount} cuts", i + 1, chunks.Count, cuts.Count);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        return allCuts.OrderBy(c => c.StartSeconds).ToList();
    }

    /// <summary>Agrupa segmentos em chunks de até chunkMaxCharacters (por tamanho de texto).</summary>
    private static List<List<TranscriptSegment>> BuildChunks(IReadOnlyList<TranscriptSegment> segments, int chunkMaxCharacters)
    {
        var chunks = new List<List<TranscriptSegment>>();
        var current = new List<TranscriptSegment>();
        int currentChars = 0;
        const int overheadPerSegment = 50;

        foreach (var seg in segments)
        {
            int segLen = seg.Text.Length + overheadPerSegment;
            if (currentChars + segLen > chunkMaxCharacters && current.Count > 0)
            {
                chunks.Add(current);
                current = new List<TranscriptSegment>();
                currentChars = 0;
            }
            current.Add(seg);
            currentChars += segLen;
        }
        if (current.Count > 0)
            chunks.Add(current);
        return chunks;
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

    private const int MaxTranscriptLength = 28_000;

    private static string BuildPrompt(string transcriptText)
    {
        if (transcriptText.Length > MaxTranscriptLength)
            transcriptText = transcriptText[..MaxTranscriptLength] + "\n\n[Transcript truncated for length. Use timestamps above to suggest engaging moments.]";
        return """
            You are an expert editor for viral short-form videos (YouTube Shorts, TikTok, Instagram Reels).

            Your task is to extract 2 to 5 HIGH-QUALITY, SELF-CONTAINED clips from a long transcript.

            DURATION IS MANDATORY: Every clip MUST be at least 30 seconds long (end - start >= 30). Prefer 45–90 seconds. Clips under 30 seconds will be rejected. Expand the time range to include full context (setup + idea + conclusion).

            CONTENT CONTEXT:
            The content is about intelligence, personal finance, productivity, mindset, prosperity, and happiness.

            STRICT RULES:

            1. EACH CLIP MUST BE SELF-CONTAINED
            - The viewer must fully understand the idea without watching the full video
            - Include a clear BEGINNING (setup) and END (payoff)

            2. CLIP STRUCTURE (MANDATORY)
            - Start at the beginning of a COMPLETE sentence or thought. NEVER start a clip with a word that continues from the previous sentence (e.g. "Porque", "Então", "E", "Mas", "Que", "Por isso" at the very start).
            - End after the conclusion, insight, or punchline (end of a complete sentence).
            - One complete idea per clip. The viewer must not feel that the clip "starts in the middle of something".

            3. PRIORITIZE HIGH-ENGAGEMENT MOMENTS
            Select segments with:
            - strong or controversial opinions
            - emotional intensity
            - practical advice or clear value
            - relatable personal stories
            - surprising or counterintuitive insights

            4. AVOID:
            - incomplete thoughts or clips that start with continuation words (Porque, Então, E, Mas, Que)
            - overlapping time ranges: each cut must not overlap the previous one (next start >= previous end)
            - filler content
            - generic or low-value speech
            - segments that depend on previous context

            5. DURATION (CRITICAL — clips were coming out too short):
            - MINIMUM: 30 seconds. NEVER suggest a clip shorter than 30 seconds.
            - IDEAL: 45 to 90 seconds per clip. A complete idea (setup + development + conclusion) usually takes 45–90 seconds of speech.
            - MAXIMUM: 120 seconds when the story or lesson needs more context.
            - If a good moment seems to span only 10–20 seconds in the transcript, EXPAND the range to include the full setup and payoff (previous and following sentences) so the clip is at least 30 seconds and makes sense alone.

            6. TIMESTAMPS:
            - Use timestamps exactly as provided in the transcript
            - Ensure cuts align with natural sentence boundaries (start = beginning of a sentence, end = end of a sentence)
            - Do not suggest overlapping ranges: if cut A ends at 417s, cut B must start at 417s or later

            OUTPUT FORMAT (STRICT):
            - Return ONLY valid JSON
            - No markdown, no explanations, no extra text
            - Use this exact structure: {"cuts":[{"start":number,"end":number,"description":"string in Portuguese describing the value of the clip"}]}

            QUALITY OVER QUANTITY:
            - It is better to return 2 excellent clips than 5 weak ones

            Transcript:
            """ + transcriptText;
    }

    private IReadOnlyList<VideoCut> ParseCutsFromResponse(string? responseText)
    {
        var cuts = EngagingMomentsJsonParser.Parse(responseText);
        if (cuts.Count == 0 && !string.IsNullOrWhiteSpace(responseText))
        {
            var preview = responseText.Length > 600 ? responseText.AsSpan(0, 600).ToString() + "..." : responseText;
            _logger.LogWarning("Ollama returned no valid cuts (empty or invalid JSON). Preview: {Preview}", preview);
        }
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
