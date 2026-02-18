using Microsoft.Extensions.Options;
using VideoCuts.Core.Configuration;
using VideoCuts.Core.Interfaces;
using VideoCuts.Core.Models.Pipeline;
using VideoCuts.Core.Models.Transcription;
using VideoCuts.Core.Models.VideoDownload;
using VideoCuts.Infrastructure.EngagingMoments;
using VideoCuts.Infrastructure.Pipeline;
using VideoCuts.Infrastructure.Transcription;
using VideoCuts.Infrastructure.VideoDownload;
using VideoCuts.Infrastructure.VideoEditing;

namespace VideoCuts.Web.Services;

public sealed class PipelineJobHost
{
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private PipelineJobStatus _status = new() { State = JobState.Idle };
    private readonly object _lock = new();

    public PipelineJobHost(IConfiguration configuration, ILoggerFactory loggerFactory)
    {
        _configuration = configuration;
        _loggerFactory = loggerFactory;
    }

    public PipelineJobStatus GetStatus()
    {
        lock (_lock) return _status with { };
    }

    public async Task StartAsync(RunRequest request, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_status.State == JobState.Running)
            {
                return;
            }
            _status = new PipelineJobStatus { State = JobState.Running, Message = "Iniciando..." };
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var pipeline = CreatePipeline(request);
                var pipelineRequest = BuildPipelineRequest(request);
                var result = await pipeline.RunAsync(pipelineRequest, cancellationToken);
                var clipDetails = BuildClipDetails(result);
                lock (_lock)
                {
                    _status = new PipelineJobStatus
                    {
                        State = result.Success ? JobState.Completed : JobState.Failed,
                        Message = result.Success ? "Concluído." : (result.ErrorMessage ?? "Falha."),
                        Success = result.Success,
                        ErrorMessage = result.ErrorMessage,
                        ClipsCount = result.GeneratedClips?.Count ?? 0,
                        ClipPaths = result.GeneratedClips?.Select(c => c.OutputPath).ToList() ?? new List<string>(),
                        ClipDetails = clipDetails
                    };
                }
            }
            catch (Exception ex)
            {
                lock (_lock)
                {
                    _status = new PipelineJobStatus
                    {
                        State = JobState.Failed,
                        Message = ex.Message,
                        Success = false,
                        ErrorMessage = ex.Message
                    };
                }
            }
        }, cancellationToken);

        await Task.CompletedTask;
    }

    private IVideoClipPipeline CreatePipeline(RunRequest request)
    {
        bool hasValidLocal = !string.IsNullOrWhiteSpace(request.LocalPath) && File.Exists(request.LocalPath);
        var useUrl = !hasValidLocal && !string.IsNullOrWhiteSpace(request.VideoUrl);
        var sourceUrl = request.VideoUrl;
        var engagingMode = request.UseOllamaFallback ? EngagingMomentsMode.OllamaThenOpenAi : (request.UseOllama ? EngagingMomentsMode.Ollama : EngagingMomentsMode.OpenAi);
        var useLocalWhisper = request.UseLocalWhisper;
        var whisperPath = useLocalWhisper
            ? (string.IsNullOrWhiteSpace(request.WhisperPathOverride) ? _configuration["Whisper:ModelPath"] : request.WhisperPathOverride)?.Trim()
            : null;

        if (useLocalWhisper && (string.IsNullOrWhiteSpace(whisperPath) || !File.Exists(whisperPath)))
            throw new InvalidOperationException("Transcrição local ativa: informe WhisperPathOverride ou defina Whisper:ModelPath no appsettings e garanta que o arquivo existe.");

        string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        bool needApiKey = !useLocalWhisper || engagingMode == EngagingMomentsMode.OpenAi || engagingMode == EngagingMomentsMode.OllamaThenOpenAi;
        if (needApiKey && string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Defina OPENAI_API_KEY no ambiente ou use transcrição local + Ollama para cortes.");

        ITranscriptionService transcription = useLocalWhisper
            ? new WhisperTranscriptionService(whisperPath!)
            : new OpenAiWhisperTranscriptionService(apiKey!);

        var ollamaOptions = new ServiceCollection()
            .Configure<OllamaEngagingMomentsOptions>(_configuration.GetSection(OllamaEngagingMomentsOptions.SectionName))
            .BuildServiceProvider()
            .GetRequiredService<IOptions<OllamaEngagingMomentsOptions>>();

        IEngagingMomentsService engagingMoments = engagingMode switch
        {
            EngagingMomentsMode.OpenAi => new LlmEngagingMomentsService(apiKey!),
            EngagingMomentsMode.Ollama => new OllamaEngagingMomentsService(new HttpClient(), ollamaOptions, _loggerFactory.CreateLogger<OllamaEngagingMomentsService>()),
            EngagingMomentsMode.OllamaThenOpenAi => new FallbackEngagingMomentsService(
                new OllamaEngagingMomentsService(new HttpClient(), ollamaOptions, _loggerFactory.CreateLogger<OllamaEngagingMomentsService>()),
                new LlmEngagingMomentsService(apiKey!),
                _loggerFactory.CreateLogger<FallbackEngagingMomentsService>()),
            _ => new LlmEngagingMomentsService(apiKey!)
        };

        IVideoEditor editor = new FfmpegVideoEditor();
        IVideoDownloader? downloader = useUrl
            ? (IsYouTubeUrl(sourceUrl) ? new YoutubeExplodeVideoDownloader() : new HttpVideoDownloader())
            : null;
        var videoCutsOptions = new ServiceCollection()
            .Configure<VideoCutsSettings>(_configuration.GetSection(VideoCutsSettings.SectionName))
            .BuildServiceProvider()
            .GetRequiredService<IOptions<VideoCutsSettings>>();
        var logger = _loggerFactory.CreateLogger<VideoClipPipeline>();
        return new VideoClipPipeline(transcription, engagingMoments, editor, logger, downloader, videoCutsOptions);
    }

    private static bool IsYouTubeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        try
        {
            var uri = new Uri(url);
            return uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static IReadOnlyList<ClipDetail> BuildClipDetails(PipelineResult result)
    {
        if (result.GeneratedClips == null || result.GeneratedClips.Count == 0)
            return Array.Empty<ClipDetail>();
        var segments = result.Transcript?.Segments;
        if (segments == null || segments.Count == 0)
            return result.GeneratedClips.Select(c => new ClipDetail(c.OutputPath, "")).ToList();

        var list = new List<ClipDetail>();
        foreach (var clip in result.GeneratedClips)
        {
            double start = clip.Cut.StartSeconds;
            double end = clip.Cut.EndSeconds;
            var overlap = segments
                .Where(s => s.StartTimeSeconds < end && s.EndTimeSeconds > start)
                .OrderBy(s => s.StartTimeSeconds)
                .Select(s => s.Text?.Trim() ?? "")
                .Where(t => t.Length > 0);
            string transcript = string.Join(" ", overlap);
            list.Add(new ClipDetail(clip.OutputPath, transcript));
        }
        return list;
    }

    private static PipelineRequest BuildPipelineRequest(RunRequest request)
    {
        string? localPath = null;
        if (!string.IsNullOrWhiteSpace(request.LocalPath) && File.Exists(request.LocalPath))
            localPath = Path.GetFullPath(request.LocalPath);
        // Se tiver arquivo local válido, usa só ele; senão usa URL (download).
        bool useUrl = string.IsNullOrEmpty(localPath) && !string.IsNullOrWhiteSpace(request.VideoUrl);
        return new PipelineRequest
        {
            LocalVideoPath = localPath,
            Source = useUrl ? new VideoSource { Url = request.VideoUrl! } : null,
            DownloadIfUrl = useUrl,
            OutputDirectory = string.IsNullOrWhiteSpace(request.OutputDirectory) ? null : request.OutputDirectory,
            MaxClips = request.MaxClips,
            ConvertClipsToVertical = request.ConvertToVertical,
            TranscriptionOptions = new TranscriptionOptions { IncludeTimestamps = true }
        };
    }
}

public record RunRequest
{
    public string? VideoUrl { get; init; }
    public string? LocalPath { get; init; }
    public bool UseLocalWhisper { get; init; }
    public string? WhisperPathOverride { get; init; }
    public bool UseOllama { get; init; }
    public bool UseOllamaFallback { get; init; }
    public int? MaxClips { get; init; }
    public bool ConvertToVertical { get; init; } = true;
    public string? OutputDirectory { get; init; }
}

public record PipelineJobStatus
{
    public JobState State { get; init; }
    public string Message { get; init; } = "";
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public int ClipsCount { get; init; }
    public IReadOnlyList<string> ClipPaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<ClipDetail> ClipDetails { get; init; } = Array.Empty<ClipDetail>();
}

public record ClipDetail(string Path, string Transcript);

public enum JobState { Idle, Running, Completed, Failed }

internal enum EngagingMomentsMode { OpenAi, Ollama, OllamaThenOpenAi }
