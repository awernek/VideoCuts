using VideoCuts.Core.Interfaces;
using VideoCuts.Core.Models.Transcription;
using FFMpegCore;
using Whisper.net;
using Whisper.net.Ggml;

namespace VideoCuts.Infrastructure.Transcription;

/// <summary>
/// Implementação de <see cref="ITranscriptionService"/> usando Whisper (local via Whisper.net).
/// Entrada: caminho do vídeo. Saída: transcrição com segmentos (Start, End, Text).
/// </summary>
public class WhisperTranscriptionService : ITranscriptionService, IAsyncDisposable
{
    private readonly string _modelPath;
    private readonly string? _language;
    private WhisperProcessor? _processor;
    private WhisperFactory? _factory;

    /// <summary>
    /// Cria o serviço com o caminho do modelo Whisper (arquivo .bin).
    /// O áudio do vídeo é extraído para 16 kHz mono WAV antes da transcrição.
    /// </summary>
    /// <param name="modelPath">Caminho para o arquivo do modelo (ex.: ggml-base.bin). Se não existir, pode ser baixado via <see cref="WhisperGgmlDownloader"/>.</param>
    /// <param name="language">Idioma fixo (ex.: "pt", "en") ou null para detecção automática ("auto").</param>
    public WhisperTranscriptionService(string modelPath, string? language = null)
    {
        _modelPath = modelPath ?? throw new ArgumentNullException(nameof(modelPath));
        _language = language;
    }

    private async Task<WhisperProcessor> GetOrCreateProcessorAsync(CancellationToken cancellationToken)
    {
        if (_processor != null)
            return _processor;

        if (!File.Exists(_modelPath))
            throw new InvalidOperationException($"Modelo Whisper não encontrado: {_modelPath}. Baixe o modelo (ex.: WhisperGgmlDownloader.GetGgmlModelAsync(GgmlType.Base)) e salve em {_modelPath}.");

        _factory = WhisperFactory.FromPath(_modelPath);
        var builder = _factory.CreateBuilder().WithLanguage(_language ?? "auto");
        _processor = builder.Build();
        await Task.CompletedTask;
        return _processor;
    }

    /// <inheritdoc />
    public async Task<TranscriptionResult> TranscribeAsync(
        string videoPath,
        TranscriptionOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(videoPath) || !File.Exists(videoPath))
            return new TranscriptionResult { Success = false, ErrorMessage = "Arquivo de vídeo não encontrado." };

        string? tempWavPath = null;
        try
        {
            tempWavPath = await ExtractAudioToWav16kAsync(videoPath, cancellationToken);
            var processor = await GetOrCreateProcessorAsync(cancellationToken);

            var segments = new List<TranscriptSegment>();
            string? fullText = null;
            var fullTextParts = new List<string>();

            await foreach (var segment in processor.ProcessAsync(File.OpenRead(tempWavPath), cancellationToken))
            {
                double start = segment.Start.TotalSeconds;
                double end = segment.End.TotalSeconds;
                string text = (segment.Text ?? "").Trim();
                if (string.IsNullOrEmpty(text))
                    continue;

                segments.Add(new TranscriptSegment
                {
                    StartTimeSeconds = start,
                    EndTimeSeconds = end,
                    Text = text
                });
                fullTextParts.Add(text);
            }

            if (options.IncludeTimestamps == false && segments.Count > 0)
                fullText = string.Join(" ", fullTextParts);

            return new TranscriptionResult
            {
                Success = true,
                Segments = segments,
                FullText = fullText ?? (segments.Count > 0 ? string.Join(" ", fullTextParts) : null),
                DetectedLanguage = _language
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
            if (tempWavPath != null && File.Exists(tempWavPath))
            {
                try { File.Delete(tempWavPath); }
                catch { /* ignore */ }
            }
        }
    }

    /// <summary>
    /// Extrai o áudio do vídeo para um WAV 16 kHz mono (requisito do Whisper).
    /// </summary>
    private static async Task<string> ExtractAudioToWav16kAsync(string videoPath, CancellationToken cancellationToken)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "VideoCuts", "Transcription");
        Directory.CreateDirectory(tempDir);
        string wavPath = Path.Combine(tempDir, $"{Guid.NewGuid():N}.wav");

        // -vn = sem vídeo, -acodec pcm_s16le = áudio 16-bit PCM, -ar 16000 = 16kHz, -ac 1 = mono
        await FFMpegArguments
            .FromFileInput(videoPath, true, opts => opts.WithCustomArgument("-vn"))
            .OutputToFile(wavPath, true, opts => opts
                .WithCustomArgument("-acodec pcm_s16le -ar 16000 -ac 1")
                .ForceFormat("wav"))
            .ProcessAsynchronously(true);

        return wavPath;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_processor != null)
        {
            _processor.Dispose();
            _processor = null;
        }
        if (_factory != null)
        {
            _factory.Dispose();
            _factory = null;
        }
        await Task.CompletedTask;
    }
}
