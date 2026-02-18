using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using VideoCuts.Core.Interfaces;
using VideoCuts.Core.Models.Transcription;
using FFMpegCore;

namespace VideoCuts.Infrastructure.Transcription;

/// <summary>
/// Implementação de <see cref="ITranscriptionService"/> usando a API Whisper da OpenAI.
/// Entrada: caminho do vídeo. Saída: transcrição com segmentos (Start, End, Text).
/// Requer extração prévia do áudio do vídeo (FFmpeg) e envio do áudio para a API.
/// </summary>
public class OpenAiWhisperTranscriptionService : ITranscriptionService
{
    private readonly HttpClient _httpClient;
    private const string DefaultApiBase = "https://api.openai.com/v1";
    private const string TranscriptionsEndpoint = "audio/transcriptions";

    /// <summary>
    /// Cria o serviço usando a API OpenAI.
    /// </summary>
    /// <param name="apiKey">Chave da API OpenAI (Bearer).</param>
    /// <param name="httpClient">Cliente HTTP opcional. Se null, usa um novo HttpClient.</param>
    /// <param name="apiBase">Base URL da API (padrão: https://api.openai.com/v1).</param>
    public OpenAiWhisperTranscriptionService(
        string apiKey,
        HttpClient? httpClient = null,
        string? apiBase = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        var baseUrl = apiBase ?? DefaultApiBase;
        _httpClient.BaseAddress = new Uri(baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : baseUrl + "/");
    }

    /// <inheritdoc />
    public async Task<TranscriptionResult> TranscribeAsync(
        string videoPath,
        TranscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            return new TranscriptionResult { Success = false, ErrorMessage = "Arquivo de vídeo não encontrado." };

        string? tempPath = null;
        try
        {
            tempPath = await ExtractAudioToMp3Async(videoPath, cancellationToken);

            using var content = new MultipartFormDataContent();
            using var fileContent = new StreamContent(File.OpenRead(tempPath));
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/mpeg");
            content.Add(fileContent, "file", Path.GetFileName(tempPath) ?? "audio.mp3");
            content.Add(new StringContent("whisper-1"), "model");
            content.Add(new StringContent("verbose_json"), "response_format");
            if (!string.IsNullOrWhiteSpace(options.Language))
                content.Add(new StringContent(options.Language), "language");

            var response = await _httpClient.PostAsync(TranscriptionsEndpoint, content, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                return new TranscriptionResult
                {
                    Success = false,
                    ErrorMessage = $"API error ({(int)response.StatusCode}): {errorBody}"
                };
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var verbose = JsonSerializer.Deserialize<VerboseTranscriptionResponse>(json);

            if (verbose == null)
                return new TranscriptionResult { Success = false, ErrorMessage = "Resposta da API inválida." };

            var segments = (verbose.Segments ?? Array.Empty<VerboseSegment>())
                .Select(s => new TranscriptSegment
                {
                    StartTimeSeconds = s.Start,
                    EndTimeSeconds = s.End,
                    Text = s.Text ?? ""
                })
                .ToList();

            return new TranscriptionResult
            {
                Success = true,
                Segments = segments,
                FullText = verbose.Text,
                DetectedLanguage = verbose.Language
            };
        }
        catch (Exception ex)
        {
            return new TranscriptionResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            if (tempPath != null && File.Exists(tempPath))
            {
                try { File.Delete(tempPath); }
                catch { /* ignore */ }
            }
        }
    }

    private static async Task<string> ExtractAudioToMp3Async(string videoPath, CancellationToken cancellationToken)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "VideoCuts", "Transcription");
        Directory.CreateDirectory(tempDir);
        string mp3Path = Path.Combine(tempDir, $"{Guid.NewGuid():N}.mp3");

        await FFMpegArguments
            .FromFileInput(videoPath, true, opts => opts.WithCustomArgument("-vn"))
            .OutputToFile(mp3Path, true, opts => opts.WithCustomArgument("-acodec libmp3lame"))
            .ProcessAsynchronously(true);

        return mp3Path;
    }

    private sealed class VerboseTranscriptionResponse
    {
        [JsonPropertyName("duration")]
        public double? Duration { get; set; }

        [JsonPropertyName("language")]
        public string? Language { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("segments")]
        public VerboseSegment[]? Segments { get; set; }
    }

    private sealed class VerboseSegment
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("start")]
        public double Start { get; set; }

        [JsonPropertyName("end")]
        public double End { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }
}
