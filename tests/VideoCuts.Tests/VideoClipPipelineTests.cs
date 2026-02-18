using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using VideoCuts.Core.Configuration;
using VideoCuts.Core.Interfaces;
using VideoCuts.Core.Models.CutSuggestion;
using VideoCuts.Core.Models.Pipeline;
using VideoCuts.Core.Models.Transcription;
using VideoCuts.Core.Models.VideoEditing;
using VideoCuts.Infrastructure.Pipeline;
using Xunit;

namespace VideoCuts.Tests;

public class VideoClipPipelineTests : IDisposable
{
    private readonly string _tempVideoPath;

    public VideoClipPipelineTests()
    {
        _tempVideoPath = Path.Combine(Path.GetTempPath(), $"VideoCuts_test_{Guid.NewGuid():N}.mp4");
        File.WriteAllText(_tempVideoPath, "fake video content for test");
    }

    public void Dispose() => File.Delete(_tempVideoPath);

    [Fact]
    public async Task RunAsync_WithLocalPath_CallsTranscriptionThenEngagingMomentsThenEditor()
    {
        var transcript = new TranscriptionResult
        {
            Success = true,
            FullText = "Hello world.",
            Segments = new List<TranscriptSegment>
            {
                new() { StartTimeSeconds = 0, EndTimeSeconds = 1, Text = "Hello world." }
            }
        };

        var cuts = new List<VideoCut>
        {
            new() { StartSeconds = 0, EndSeconds = 5, Description = "Intro" },
            new() { StartSeconds = 10, EndSeconds = 15, Description = "Highlight" }
        };

        var transcriptionMock = new Mock<ITranscriptionService>();
        transcriptionMock
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TranscriptionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);

        var engagingMock = new Mock<IEngagingMomentsService>();
        engagingMock
            .Setup(e => e.GetEngagingMomentsAsync(It.IsAny<IReadOnlyList<TranscriptSegment>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cuts);

        var editorMock = new Mock<IVideoEditor>();
        editorMock
            .Setup(e => e.EditAsync(It.IsAny<EditRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EditRequest req, CancellationToken _) => new EditResult
            {
                Success = true,
                OutputPath = req.OutputPath ?? "out.mp4"
            });

        var pipeline = new VideoClipPipeline(
            transcriptionMock.Object,
            engagingMock.Object,
            editorMock.Object,
            NullLogger<VideoClipPipeline>.Instance);

        var request = new PipelineRequest
        {
            LocalVideoPath = _tempVideoPath,
            OutputDirectory = Path.GetTempPath(),
            MaxClips = 2,
            ConvertClipsToVertical = false,
            TranscriptionOptions = new TranscriptionOptions { IncludeTimestamps = true }
        };

        var result = await pipeline.RunAsync(request);

        Assert.True(result.Success);
        Assert.Equal(2, result.Cuts.Count);
        Assert.Equal(2, result.GeneratedClips?.Count ?? 0);

        transcriptionMock.Verify(
            t => t.TranscribeAsync(_tempVideoPath, request.TranscriptionOptions, It.IsAny<CancellationToken>()),
            Times.Once);
        engagingMock.Verify(
            e => e.GetEngagingMomentsAsync(It.IsAny<IReadOnlyList<TranscriptSegment>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        editorMock.Verify(
            e => e.EditAsync(It.IsAny<EditRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task RunAsync_TranscriptionFails_ReturnsFailureResult()
    {
        var transcriptionMock = new Mock<ITranscriptionService>();
        transcriptionMock
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TranscriptionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionResult { Success = false, ErrorMessage = "Transcription failed" });

        var engagingMock = new Mock<IEngagingMomentsService>();
        var editorMock = new Mock<IVideoEditor>();

        var pipeline = new VideoClipPipeline(
            transcriptionMock.Object,
            engagingMock.Object,
            editorMock.Object,
            NullLogger<VideoClipPipeline>.Instance);

        var result = await pipeline.RunAsync(new PipelineRequest
        {
            LocalVideoPath = _tempVideoPath,
            TranscriptionOptions = new TranscriptionOptions()
        });

        Assert.False(result.Success);
        Assert.Contains("Transcrição", result.ErrorMessage ?? "");
        engagingMock.Verify(e => e.GetEngagingMomentsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        editorMock.Verify(e => e.EditAsync(It.IsAny<EditRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_ZeroCuts_ReturnsSuccessWithNoClips()
    {
        var transcript = new TranscriptionResult
        {
            Success = true,
            FullText = "Short.",
            Segments = new List<TranscriptSegment> { new() { StartTimeSeconds = 0, EndTimeSeconds = 1, Text = "Short." } }
        };

        var transcriptionMock = new Mock<ITranscriptionService>();
        transcriptionMock
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TranscriptionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);

        var engagingMock = new Mock<IEngagingMomentsService>();
        engagingMock
            .Setup(e => e.GetEngagingMomentsAsync(It.IsAny<IReadOnlyList<TranscriptSegment>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<VideoCut>());

        var editorMock = new Mock<IVideoEditor>();

        var pipeline = new VideoClipPipeline(
            transcriptionMock.Object,
            engagingMock.Object,
            editorMock.Object,
            NullLogger<VideoClipPipeline>.Instance);

        var result = await pipeline.RunAsync(new PipelineRequest
        {
            LocalVideoPath = _tempVideoPath,
            TranscriptionOptions = new TranscriptionOptions { IncludeTimestamps = true }
        });

        Assert.True(result.Success);
        Assert.Empty(result.Cuts);
        Assert.Empty(result.GeneratedClips ?? Array.Empty<GeneratedClip>());
        editorMock.Verify(e => e.EditAsync(It.IsAny<EditRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_MaxClipsLimitsGeneratedClips()
    {
        var transcript = new TranscriptionResult
        {
            Success = true,
            FullText = "Text",
            Segments = new List<TranscriptSegment> { new() { StartTimeSeconds = 0, EndTimeSeconds = 1, Text = "Text" } }
        };

        var cuts = new List<VideoCut>
        {
            new() { StartSeconds = 0, EndSeconds = 5, Description = "A" },
            new() { StartSeconds = 5, EndSeconds = 10, Description = "B" },
            new() { StartSeconds = 10, EndSeconds = 15, Description = "C" }
        };

        var transcriptionMock = new Mock<ITranscriptionService>();
        transcriptionMock
            .Setup(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TranscriptionOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(transcript);

        var engagingMock = new Mock<IEngagingMomentsService>();
        engagingMock
            .Setup(e => e.GetEngagingMomentsAsync(It.IsAny<IReadOnlyList<TranscriptSegment>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cuts);

        var editorMock = new Mock<IVideoEditor>();
        editorMock
            .Setup(e => e.EditAsync(It.IsAny<EditRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((EditRequest req, CancellationToken _) => new EditResult { Success = true, OutputPath = req.OutputPath ?? "out.mp4" });

        var pipeline = new VideoClipPipeline(
            transcriptionMock.Object,
            engagingMock.Object,
            editorMock.Object,
            NullLogger<VideoClipPipeline>.Instance);

        var result = await pipeline.RunAsync(new PipelineRequest
        {
            LocalVideoPath = _tempVideoPath,
            OutputDirectory = Path.GetTempPath(),
            MaxClips = 2,
            ConvertClipsToVertical = false,
            TranscriptionOptions = new TranscriptionOptions { IncludeTimestamps = true }
        });

        Assert.True(result.Success);
        Assert.Equal(3, result.Cuts.Count);
        Assert.Equal(2, result.GeneratedClips?.Count ?? 0);
        editorMock.Verify(e => e.EditAsync(It.IsAny<EditRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RunAsync_InvalidLocalPath_ReturnsFailureWithoutCallingTranscription()
    {
        var transcriptionMock = new Mock<ITranscriptionService>();
        var engagingMock = new Mock<IEngagingMomentsService>();
        var editorMock = new Mock<IVideoEditor>();

        var pipeline = new VideoClipPipeline(
            transcriptionMock.Object,
            engagingMock.Object,
            editorMock.Object,
            NullLogger<VideoClipPipeline>.Instance);

        var result = await pipeline.RunAsync(new PipelineRequest
        {
            LocalVideoPath = Path.Combine(Path.GetTempPath(), "nonexistent_video_12345.mp4"),
            TranscriptionOptions = new TranscriptionOptions()
        });

        Assert.False(result.Success);
        transcriptionMock.Verify(t => t.TranscribeAsync(It.IsAny<string>(), It.IsAny<TranscriptionOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
