using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VideoCuts.Core.Configuration;
using VideoCuts.Core.Interfaces;
using VideoCuts.Core.Models.CutDetection;
using VideoCuts.Core.Models.CutSuggestion;
using VideoCuts.Core.Models.Job;
using VideoCuts.Core.Models.Pipeline;
using VideoCuts.Core.Models.Transcription;
using VideoCuts.Core.Models.VideoEditing;
using VideoCuts.Core.Models.VideoDownload;

namespace VideoCuts.Infrastructure.Pipeline;

/// <summary>
/// Pipeline modular assíncrono: download (opcional) → transcrição → detecção de cortes → geração de clipes.
/// Serviços injetados; etapas podem ser ignoradas quando o serviço correspondente for null (exceto transcrição e edição).
/// </summary>
public class VideoClipPipeline : IVideoClipPipeline
{
    private const string StageTranscription = "Transcription";
    private const string StageEngagingMoments = "EngagingMoments";
    private const string StageClipGeneration = "ClipGeneration";

    private static readonly Func<ILogger, string, string?, IDisposable?> BeginPipelineScope =
        LoggerMessage.DefineScope<string, string?>("Pipeline run: LocalVideoPath={LocalVideoPath}, SourceUrl={SourceUrl}");

    private readonly ILogger<VideoClipPipeline> _logger;
    private readonly IVideoDownloader? _downloader;
    private readonly ITranscriptionService _transcription;
    private readonly IEngagingMomentsService _engagingMoments;
    private readonly IVideoEditor _editor;
    private readonly VideoCutsSettings? _settings;

    /// <summary>
    /// Cria o pipeline com os serviços necessários. Download é opcional (passe null para pular quando usar apenas path local).
    /// Valores não informados em <see cref="PipelineRequest"/> são preenchidos a partir de <paramref name="options"/> quando disponível.
    /// </summary>
    /// <param name="transcription">Serviço de transcrição.</param>
    /// <param name="engagingMoments">Serviço de detecção de momentos engajantes (LLM).</param>
    /// <param name="editor">Editor de vídeo.</param>
    /// <param name="logger">Logger para telemetria (opcional; se null, usa <see cref="NullLogger{T}"/>).</param>
    /// <param name="downloader">Downloader opcional para URL.</param>
    /// <param name="options">Configuração centralizada (opcional). Quando informado, MaxClips e OutputDirectory do request são complementados pelos valores das settings quando null.</param>
    public VideoClipPipeline(
        ITranscriptionService transcription,
        IEngagingMomentsService engagingMoments,
        IVideoEditor editor,
        ILogger<VideoClipPipeline>? logger = null,
        IVideoDownloader? downloader = null,
        IOptions<VideoCutsSettings>? options = null)
    {
        _transcription = transcription ?? throw new ArgumentNullException(nameof(transcription));
        _engagingMoments = engagingMoments ?? throw new ArgumentNullException(nameof(engagingMoments));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _logger = logger ?? NullLogger<VideoClipPipeline>.Instance;
        _downloader = downloader;
        _settings = options?.Value;
    }

    /// <inheritdoc />
    public async Task<PipelineResult> RunAsync(
        PipelineRequest request,
        CancellationToken cancellationToken = default,
        VideoProcessingJob? job = null)
    {
        using (BeginPipelineScope(_logger, request.LocalVideoPath ?? "", request.Source?.Url))
        {
            _logger.LogInformation("Pipeline started. LocalVideoPath={LocalVideoPath}, SourceUrl={SourceUrl}, DownloadIfUrl={DownloadIfUrl}",
                request.LocalVideoPath, request.Source?.Url, request.DownloadIfUrl);

            job?.MarkProcessing();
            var pipelineStopwatch = Stopwatch.StartNew();

            try
            {
                string? localPath = await ResolveLocalPathWithLoggingAsync(request, cancellationToken).ConfigureAwait(false);
                if (localPath == null)
                {
                    const string message = "Não foi possível obter o caminho local do vídeo. Informe LocalVideoPath ou Source com DownloadIfUrl e um IVideoDownloader.";
                    _logger.LogWarning("Could not resolve local video path. Provide LocalVideoPath or Source with DownloadIfUrl and IVideoDownloader.");
                    job?.MarkFailed(message);
                    return new PipelineResult { Success = false, ErrorMessage = message };
                }

                var stageTimings = new List<PipelineStageTiming>();

                var (transcript, transcriptionMs) = await RunTranscriptionWithLoggingAsync(localPath, request, cancellationToken).ConfigureAwait(false);
                stageTimings.Add(new PipelineStageTiming(StageTranscription, transcriptionMs));

                if (!transcript.Success)
                {
                    var message = $"Transcrição falhou: {transcript.ErrorMessage}";
                    _logger.LogError("Transcription failed. LocalPath={LocalPath}, Error={Error}", localPath, transcript.ErrorMessage);
                    job?.MarkFailed(message);
                    return new PipelineResult
                    {
                        Success = false,
                        LocalVideoPath = localPath,
                        ErrorMessage = message,
                        StageTimings = stageTimings
                    };
                }

                var (cuts, engagingMs) = await RunEngagingMomentsWithLoggingAsync(transcript, cancellationToken).ConfigureAwait(false);
                stageTimings.Add(new PipelineStageTiming(StageEngagingMoments, engagingMs));

                if (cuts.Count == 0)
                {
                    _logger.LogInformation("No engaging moments detected. Returning success with zero clips. StageTimings: Transcription={TranscriptionMs}ms, EngagingMoments={EngagingMs}ms",
                        transcriptionMs, engagingMs);
                    pipelineStopwatch.Stop();
                    _logger.LogInformation("Pipeline completed. Success={Success}, TotalDurationMs={DurationMs}, ClipsGenerated={ClipsGenerated}",
                        true, pipelineStopwatch.ElapsedMilliseconds, 0);
                    job?.MarkCompleted();
                    return new PipelineResult
                    {
                        Success = true,
                        LocalVideoPath = localPath,
                        Transcript = transcript,
                        Cuts = cuts,
                        GeneratedClips = Array.Empty<GeneratedClip>(),
                        StageTimings = stageTimings
                    };
                }

                int maxClips = request.MaxClips ?? _settings?.MaxClips ?? cuts.Count;
                var clipsToGenerate = cuts.Take(maxClips).ToList();
                string outputDir = request.OutputDirectory ?? _settings?.OutputDirectory ?? Path.GetDirectoryName(localPath) ?? ".";

                var (generatedClips, clipGenMs) = await RunClipGenerationWithLoggingAsync(
                    localPath, clipsToGenerate, outputDir, request.ConvertClipsToVertical, cancellationToken).ConfigureAwait(false);
                stageTimings.Add(new PipelineStageTiming(StageClipGeneration, clipGenMs));

                pipelineStopwatch.Stop();
                _logger.LogInformation("Pipeline completed. Success={Success}, TotalDurationMs={DurationMs}, ClipsGenerated={ClipsGenerated}, CutsDetected={CutsDetected}. StageTimings: Transcription={TranscriptionMs}ms, EngagingMoments={EngagingMs}ms, ClipGeneration={ClipGenMs}ms",
                    true, pipelineStopwatch.ElapsedMilliseconds, generatedClips.Count, cuts.Count, transcriptionMs, engagingMs, clipGenMs);
                job?.MarkCompleted();

                return new PipelineResult
                {
                    Success = true,
                    LocalVideoPath = localPath,
                    Transcript = transcript,
                    Cuts = cuts,
                    GeneratedClips = generatedClips,
                    StageTimings = stageTimings
                };
            }
            catch (Exception ex)
            {
                pipelineStopwatch.Stop();
                _logger.LogError(ex, "Pipeline failed. DurationMs={DurationMs}, Message={Message}",
                    pipelineStopwatch.ElapsedMilliseconds, ex.Message);
                job?.MarkFailed(ex.Message);
                throw;
            }
        }
    }

    private async Task<string?> ResolveLocalPathWithLoggingAsync(PipelineRequest request, CancellationToken cancellationToken)
    {
        if (request.Source != null && request.DownloadIfUrl && _downloader != null)
        {
            var sw = Stopwatch.StartNew();
            _logger.LogInformation("Step Download started. SourceUrl={SourceUrl}", request.Source.Url);
            try
            {
                var downloadResult = await _downloader.DownloadAsync(
                    request.Source,
                    new DownloadOptions { OutputDirectory = request.OutputDirectory ?? _settings?.OutputDirectory },
                    cancellationToken).ConfigureAwait(false);
                sw.Stop();
                if (downloadResult.Success)
                    _logger.LogInformation("Step Download completed. DurationMs={DurationMs}, LocalPath={LocalPath}",
                        sw.ElapsedMilliseconds, downloadResult.LocalPath);
                else
                    _logger.LogWarning("Step Download failed. DurationMs={DurationMs}, Error={Error}",
                        sw.ElapsedMilliseconds, downloadResult.ErrorMessage);
                return downloadResult.Success ? downloadResult.LocalPath : null;
            }
            catch (Exception ex)
            {
                sw.Stop();
                _logger.LogError(ex, "Step Download threw. DurationMs={DurationMs}", sw.ElapsedMilliseconds);
                throw;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.LocalVideoPath) && File.Exists(request.LocalVideoPath))
        {
            _logger.LogInformation("Using local video path (no download). LocalPath={LocalPath}", request.LocalVideoPath);
            return request.LocalVideoPath;
        }

        return null;
    }

    private async Task<(TranscriptionResult Result, long ElapsedMilliseconds)> RunTranscriptionWithLoggingAsync(
        string localPath,
        PipelineRequest request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Step Transcription started. LocalPath={LocalPath}", localPath);
        try
        {
            var result = await _transcription.TranscribeAsync(
                localPath,
                request.TranscriptionOptions,
                cancellationToken).ConfigureAwait(false);
            sw.Stop();
            long elapsed = sw.ElapsedMilliseconds;
            _logger.LogInformation("Step Transcription completed. DurationMs={DurationMs}, Success={Success}, SegmentCount={SegmentCount}",
                elapsed, result.Success, result.Segments?.Count ?? 0);
            return (result, elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Step Transcription threw. DurationMs={DurationMs}", sw.ElapsedMilliseconds);
            throw;
        }
    }

    private async Task<(IReadOnlyList<VideoCut> Cuts, long ElapsedMilliseconds)> RunEngagingMomentsWithLoggingAsync(
        TranscriptionResult transcript,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Step EngagingMoments (LLM) started. SegmentCount={SegmentCount}", transcript.Segments?.Count ?? 0);
        try
        {
            IReadOnlyList<VideoCut> cuts;
            if (transcript.Segments?.Count > 0)
                cuts = await _engagingMoments.GetEngagingMomentsAsync(transcript.Segments, cancellationToken).ConfigureAwait(false);
            else
                cuts = await _engagingMoments.GetEngagingMomentsAsync(transcript.FullText ?? "", cancellationToken).ConfigureAwait(false);
            sw.Stop();
            long elapsed = sw.ElapsedMilliseconds;
            _logger.LogInformation("Step EngagingMoments (LLM) completed. DurationMs={DurationMs}, CutsCount={CutsCount}",
                elapsed, cuts.Count);
            return (cuts, elapsed);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(ex, "Step EngagingMoments threw. DurationMs={DurationMs}", sw.ElapsedMilliseconds);
            throw;
        }
    }

    private async Task<(List<GeneratedClip> Clips, long ElapsedMilliseconds)> RunClipGenerationWithLoggingAsync(
        string localPath,
        List<VideoCut> clipsToGenerate,
        string outputDir,
        bool convertToVertical,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        _logger.LogInformation("Step ClipGeneration started. ClipCount={ClipCount}, OutputDir={OutputDir}, ConvertToVertical={ConvertToVertical}",
            clipsToGenerate.Count, outputDir, convertToVertical);

        var generatedClips = new List<GeneratedClip>();
        for (int i = 0; i < clipsToGenerate.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var cut = clipsToGenerate[i];
            var segment = new CutSegment
            {
                StartTimeSeconds = cut.StartSeconds,
                EndTimeSeconds = cut.EndSeconds
            };

            string clipFileName = $"clip_{i + 1:D3}_{cut.StartSeconds:F0}s_{cut.EndSeconds:F0}s.mp4";
            string outputPath = Path.Combine(outputDir, clipFileName);

            var editRequest = new EditRequest
            {
                InputVideoPath = localPath,
                SegmentsToKeep = new[] { segment },
                OutputPath = outputPath,
                ConvertToVertical = convertToVertical
            };

            var clipSw = Stopwatch.StartNew();
            try
            {
                var editResult = await _editor.EditAsync(editRequest, cancellationToken).ConfigureAwait(false);
                clipSw.Stop();
                if (editResult.Success && !string.IsNullOrEmpty(editResult.OutputPath))
                {
                    generatedClips.Add(new GeneratedClip
                    {
                        Cut = cut,
                        OutputPath = editResult.OutputPath,
                        Index = i + 1
                    });
                    _logger.LogDebug("Clip {Index} generated. DurationMs={DurationMs}, OutputPath={OutputPath}", i + 1, clipSw.ElapsedMilliseconds, editResult.OutputPath);
                }
                else
                    _logger.LogWarning("Clip {Index} edit failed. DurationMs={DurationMs}, Error={Error}", i + 1, clipSw.ElapsedMilliseconds, editResult.ErrorMessage);
            }
            catch (Exception ex)
            {
                clipSw.Stop();
                _logger.LogError(ex, "Clip {Index} generation threw. DurationMs={DurationMs}, Start={Start}s, End={End}s",
                    i + 1, clipSw.ElapsedMilliseconds, cut.StartSeconds, cut.EndSeconds);
                throw;
            }
        }

        sw.Stop();
        long elapsed = sw.ElapsedMilliseconds;
        _logger.LogInformation("Step ClipGeneration completed. DurationMs={DurationMs}, GeneratedCount={GeneratedCount}",
            elapsed, generatedClips.Count);
        return (generatedClips, elapsed);
    }
}
