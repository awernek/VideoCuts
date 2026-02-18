using VideoCuts.Core.Interfaces;
using VideoCuts.Core.Models.CutDetection;
using VideoCuts.Core.Models.CutSuggestion;
using VideoCuts.Core.Models.Pipeline;
using VideoCuts.Core.Models.VideoEditing;
using VideoCuts.Core.Models.VideoDownload;

namespace VideoCuts.Infrastructure.Pipeline;

/// <summary>
/// Pipeline modular assíncrono: download (opcional) → transcrição → detecção de cortes → geração de clipes.
/// Serviços injetados; etapas podem ser ignoradas quando o serviço correspondente for null (exceto transcrição e edição).
/// </summary>
public class VideoClipPipeline : IVideoClipPipeline
{
    private readonly IVideoDownloader? _downloader;
    private readonly ITranscriptionService _transcription;
    private readonly IEngagingMomentsService _engagingMoments;
    private readonly IVideoEditor _editor;

    /// <summary>
    /// Cria o pipeline com os serviços necessários. Download é opcional (passe null para pular quando usar apenas path local).
    /// </summary>
    public VideoClipPipeline(
        ITranscriptionService transcription,
        IEngagingMomentsService engagingMoments,
        IVideoEditor editor,
        IVideoDownloader? downloader = null)
    {
        _transcription = transcription ?? throw new ArgumentNullException(nameof(transcription));
        _engagingMoments = engagingMoments ?? throw new ArgumentNullException(nameof(engagingMoments));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        _downloader = downloader;
    }

    /// <inheritdoc />
    public async Task<PipelineResult> RunAsync(
        PipelineRequest request,
        CancellationToken cancellationToken = default)
    {
        string? localPath = await ResolveLocalPathAsync(request, cancellationToken).ConfigureAwait(false);
        if (localPath == null)
            return new PipelineResult { Success = false, ErrorMessage = "Não foi possível obter o caminho local do vídeo. Informe LocalVideoPath ou Source com DownloadIfUrl e um IVideoDownloader." };

        var transcript = await _transcription.TranscribeAsync(
            localPath,
            request.TranscriptionOptions,
            cancellationToken).ConfigureAwait(false);

        if (!transcript.Success)
            return new PipelineResult
            {
                Success = false,
                LocalVideoPath = localPath,
                ErrorMessage = $"Transcrição falhou: {transcript.ErrorMessage}"
            };

        IReadOnlyList<VideoCut> cuts;
        if (transcript.Segments.Count > 0)
            cuts = await _engagingMoments.GetEngagingMomentsAsync(transcript.Segments, cancellationToken).ConfigureAwait(false);
        else
            cuts = await _engagingMoments.GetEngagingMomentsAsync(transcript.FullText ?? "", cancellationToken).ConfigureAwait(false);

        if (cuts.Count == 0)
            return new PipelineResult
            {
                Success = true,
                LocalVideoPath = localPath,
                Transcript = transcript,
                Cuts = cuts,
                GeneratedClips = Array.Empty<GeneratedClip>()
            };

        int maxClips = request.MaxClips ?? cuts.Count;
        var clipsToGenerate = cuts.Take(maxClips).ToList();
        string outputDir = request.OutputDirectory ?? Path.GetDirectoryName(localPath) ?? ".";

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
                ConvertToVertical = request.ConvertClipsToVertical
            };

            var editResult = await _editor.EditAsync(editRequest, cancellationToken).ConfigureAwait(false);

            if (editResult.Success && !string.IsNullOrEmpty(editResult.OutputPath))
                generatedClips.Add(new GeneratedClip
                {
                    Cut = cut,
                    OutputPath = editResult.OutputPath,
                    Index = i + 1
                });
        }

        return new PipelineResult
        {
            Success = true,
            LocalVideoPath = localPath,
            Transcript = transcript,
            Cuts = cuts,
            GeneratedClips = generatedClips
        };
    }

    private async Task<string?> ResolveLocalPathAsync(PipelineRequest request, CancellationToken cancellationToken)
    {
        if (request.Source != null && request.DownloadIfUrl && _downloader != null)
        {
            var downloadResult = await _downloader.DownloadAsync(
                request.Source,
                new DownloadOptions { OutputDirectory = request.OutputDirectory },
                cancellationToken).ConfigureAwait(false);

            return downloadResult.Success ? downloadResult.LocalPath : null;
        }

        if (!string.IsNullOrWhiteSpace(request.LocalVideoPath) && File.Exists(request.LocalVideoPath))
            return request.LocalVideoPath;

        return null;
    }
}
