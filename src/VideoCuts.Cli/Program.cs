using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VideoCuts.Core.Configuration;
using VideoCuts.Core.Interfaces;
using VideoCuts.Core.Models.CutDetection;
using VideoCuts.Core.Models.Pipeline;
using VideoCuts.Core.Models.Transcription;
using VideoCuts.Core.Models.VideoDownload;
using VideoCuts.Core.Models.VideoEditing;
using VideoCuts.Infrastructure.EngagingMoments;
using VideoCuts.Infrastructure.Pipeline;
using VideoCuts.Infrastructure.Transcription;
using VideoCuts.Infrastructure.VideoDownload;
using VideoCuts.Infrastructure.VideoEditing;
using FFMpegCore;

// Composition root
static IVideoEditor CreateVideoEditor() => new FfmpegVideoEditor();

static IConfiguration BuildConfiguration()
{
    // Pasta do executável (onde appsettings.json é copiado ao publicar)
    var basePath = AppContext.BaseDirectory;
    return new ConfigurationBuilder()
        .SetBasePath(basePath)
        .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
        .Build();
}

/// <summary>Configura o FFMpegCore para usar a pasta do FFmpeg informada no appsettings (FFmpeg:BinaryFolder), se definida.</summary>
static void EnsureFFmpegConfigured(IConfiguration configuration)
{
    var folder = configuration["FFmpeg:BinaryFolder"];
    if (string.IsNullOrWhiteSpace(folder)) return;
    folder = folder.Trim();
    if (!Directory.Exists(folder))
        throw new InvalidOperationException($"Pasta do FFmpeg não encontrada: {folder}. Defina FFmpeg:BinaryFolder no appsettings.json (ex.: C:\\ffmpeg\\bin) ou adicione ffmpeg ao PATH.");
    GlobalFFOptions.Configure(new FFOptions { BinaryFolder = folder });
}

static IOptions<VideoCutsSettings> BuildVideoCutsOptions(IConfiguration configuration)
{
    var services = new ServiceCollection();
    services.Configure<VideoCutsSettings>(configuration.GetSection(VideoCutsSettings.SectionName));
    return services.BuildServiceProvider().GetRequiredService<IOptions<VideoCutsSettings>>();
}

static IOptions<OllamaEngagingMomentsOptions> BuildOllamaOptions(IConfiguration configuration)
{
    var services = new ServiceCollection();
    services.Configure<OllamaEngagingMomentsOptions>(configuration.GetSection(OllamaEngagingMomentsOptions.SectionName));
    return services.BuildServiceProvider().GetRequiredService<IOptions<OllamaEngagingMomentsOptions>>();
}

static ILoggerFactory CreateLoggerFactory()
{
    return LoggerFactory.Create(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Information);
    });
}

static bool IsYouTubeUrl(string? url)
{
    if (string.IsNullOrWhiteSpace(url)) return false;
    try
    {
        var uri = new Uri(url);
        return uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase);
    }
    catch { return false; }
}

static IVideoClipPipeline CreatePipeline(bool useUrl, string? sourceUrl, EngagingMomentsMode engagingMode, bool useLocalWhisper, string? whisperModelPath, ILoggerFactory loggerFactory, IConfiguration configuration, IOptions<VideoCutsSettings>? videoCutsOptions = null)
{
    string? apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
    bool needApiKey = !useLocalWhisper || engagingMode == EngagingMomentsMode.OpenAi || engagingMode == EngagingMomentsMode.OllamaThenOpenAi;
    if (needApiKey && string.IsNullOrWhiteSpace(apiKey))
        throw new InvalidOperationException("Defina a variável de ambiente OPENAI_API_KEY (transcrição Whisper API ou cortes OpenAI). Use --whisper-local + --ollama para rodar 100% local.");

    ITranscriptionService transcription = useLocalWhisper
        ? new WhisperTranscriptionService(whisperModelPath!)
        : new OpenAiWhisperTranscriptionService(apiKey!);
    IEngagingMomentsService engagingMoments = engagingMode switch
    {
        EngagingMomentsMode.OpenAi => new LlmEngagingMomentsService(apiKey!),
        EngagingMomentsMode.Ollama => new OllamaEngagingMomentsService(new HttpClient(), BuildOllamaOptions(configuration), loggerFactory.CreateLogger<OllamaEngagingMomentsService>()),
        EngagingMomentsMode.OllamaThenOpenAi => new FallbackEngagingMomentsService(
            new OllamaEngagingMomentsService(new HttpClient(), BuildOllamaOptions(configuration), loggerFactory.CreateLogger<OllamaEngagingMomentsService>()),
            new LlmEngagingMomentsService(apiKey!),
            loggerFactory.CreateLogger<FallbackEngagingMomentsService>()),
        _ => new LlmEngagingMomentsService(apiKey!)
    };
    var editor = CreateVideoEditor();
    IVideoDownloader? downloader = useUrl
        ? (IsYouTubeUrl(sourceUrl) ? new YoutubeExplodeVideoDownloader() : new HttpVideoDownloader())
        : null;
    var logger = loggerFactory.CreateLogger<VideoClipPipeline>();
    return new VideoClipPipeline(transcription, engagingMoments, editor, logger, downloader, videoCutsOptions);
}

// ----- Modo: pipeline (transcrever → LLM cortes → gerar clipes) -----
if (args.Length > 0 && args[0].Equals("pipeline", StringComparison.OrdinalIgnoreCase))
{
    string? inputPath = null;
    string? url = null;
    string? outputDir = null;
    int? maxClips = null;
    bool convertToVertical = true;
    bool useOllama = false;
    bool useOllamaFallback = false;
    bool useWhisperLocal = false;
    string? whisperModelPath = null;

    for (int i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--input":
            case "-i":
                if (i + 1 < args.Length) inputPath = args[++i];
                break;
            case "--url":
            case "-u":
                if (i + 1 < args.Length) url = args[++i];
                break;
            case "--output-dir":
            case "-o":
                if (i + 1 < args.Length) outputDir = args[++i];
                break;
            case "--max-clips":
                if (i + 1 < args.Length && int.TryParse(args[++i], out int n)) maxClips = n;
                break;
            case "--no-vertical":
                convertToVertical = false;
                break;
            case "--ollama":
                useOllama = true;
                break;
            case "--ollama-fallback":
                useOllamaFallback = true;
                break;
            case "--whisper-local":
                useWhisperLocal = true;
                break;
            case "--whisper-model":
                if (i + 1 < args.Length) whisperModelPath = args[++i];
                break;
        }
    }

    EngagingMomentsMode engagingMode = useOllamaFallback ? EngagingMomentsMode.OllamaThenOpenAi : (useOllama ? EngagingMomentsMode.Ollama : EngagingMomentsMode.OpenAi);

    if (string.IsNullOrWhiteSpace(inputPath) && string.IsNullOrWhiteSpace(url))
    {
        Console.WriteLine("Uso (pipeline): VideoCuts.Cli pipeline --input <arquivo> | --url <url> [--output-dir <pasta>] [--max-clips N] [--no-vertical]");
        Console.WriteLine("                [--whisper-local] [--whisper-model <caminho>] [--ollama] [--ollama-fallback]");
        Console.WriteLine();
        Console.WriteLine("  --input, -i    Caminho do vídeo no disco.");
        Console.WriteLine("  --url, -u      URL do vídeo (YouTube ou HTTP). O vídeo será baixado antes.");
        Console.WriteLine("  --output-dir   Pasta dos clipes gerados (padrão: mesma do vídeo).");
        Console.WriteLine("  --max-clips    Número máximo de clipes (opcional).");
        Console.WriteLine("  --no-vertical  Não converter clipes para 9:16.");
        Console.WriteLine("  --whisper-local     Transcrição local (Whisper.net). Requer --whisper-model ou Whisper:ModelPath no appsettings.");
        Console.WriteLine("  --whisper-model     Caminho do modelo Whisper (ex.: ggml-base.bin). Use com --whisper-local.");
        Console.WriteLine("  --ollama            Cortes via Ollama (localhost). Com --whisper-local = 100% local, sem OPENAI_API_KEY.");
        Console.WriteLine("  --ollama-fallback   Ollama primeiro; em falha, OpenAI (exige OPENAI_API_KEY).");
        return 1;
    }

    var configuration = BuildConfiguration();
    EnsureFFmpegConfigured(configuration);
    if (useWhisperLocal)
    {
        whisperModelPath = string.IsNullOrWhiteSpace(whisperModelPath) ? configuration["Whisper:ModelPath"] : whisperModelPath;
        if (string.IsNullOrWhiteSpace(whisperModelPath) || !File.Exists(whisperModelPath))
        {
            Console.WriteLine("Erro: com --whisper-local informe --whisper-model <caminho> ou defina Whisper:ModelPath no appsettings.json. O arquivo do modelo deve existir (ex.: ggml-base.bin).");
            return 1;
        }
    }
    string? resolvedWhisperPath = useWhisperLocal ? whisperModelPath : null;

    if (!string.IsNullOrWhiteSpace(inputPath) && !File.Exists(inputPath))
    {
        Console.WriteLine($"Erro: arquivo não encontrado: {inputPath}");
        return 1;
    }

    var videoCutsOptions = BuildVideoCutsOptions(configuration);
    var settings = videoCutsOptions.Value;

    var request = new PipelineRequest
    {
        LocalVideoPath = string.IsNullOrWhiteSpace(url) ? Path.GetFullPath(inputPath!) : null,
        Source = !string.IsNullOrWhiteSpace(url) ? new VideoSource { Url = url } : null,
        DownloadIfUrl = !string.IsNullOrWhiteSpace(url),
        OutputDirectory = outputDir ?? settings.OutputDirectory,
        MaxClips = maxClips ?? settings.MaxClips,
        ConvertClipsToVertical = convertToVertical,
        TranscriptionOptions = new TranscriptionOptions { IncludeTimestamps = true }
    };

    try
    {
        using var loggerFactory = CreateLoggerFactory();
        var pipeline = CreatePipeline(useUrl: !string.IsNullOrWhiteSpace(url), sourceUrl: url, engagingMode, useLocalWhisper: useWhisperLocal, whisperModelPath: resolvedWhisperPath, loggerFactory, configuration, videoCutsOptions);
        Console.WriteLine(useWhisperLocal
            ? "Pipeline: transcrição local (Whisper) → " + (engagingMode == EngagingMomentsMode.Ollama ? "Ollama (cortes)" : engagingMode == EngagingMomentsMode.OllamaThenOpenAi ? "Ollama (cortes, fallback OpenAI)" : "LLM (cortes)") + " → geração de clipes..."
            : engagingMode == EngagingMomentsMode.OllamaThenOpenAi
            ? "Pipeline: transcrição → Ollama (cortes, fallback OpenAI) → geração de clipes..."
            : engagingMode == EngagingMomentsMode.Ollama
                ? "Pipeline: transcrição → Ollama (cortes) → geração de clipes..."
                : "Pipeline: transcrição → LLM (cortes) → geração de clipes...");
        var result = await pipeline.RunAsync(request);

        if (!result.Success)
        {
            Console.WriteLine($"Erro: {result.ErrorMessage}");
            return 1;
        }

        Console.WriteLine($"Vídeo: {result.LocalVideoPath}");
        Console.WriteLine($"Cortes identificados: {result.Cuts?.Count ?? 0}");
        Console.WriteLine($"Clipes gerados: {result.GeneratedClips?.Count ?? 0}");
        foreach (var clip in result.GeneratedClips ?? [])
            Console.WriteLine($"  {clip.Index}. {clip.OutputPath} ({clip.Cut.StartSeconds:F0}s - {clip.Cut.EndSeconds:F0}s)");
        return 0;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Erro: {ex.Message}");
        return 1;
    }
}

// ----- Modo: batch (pasta com vários vídeos) -----
if (args.Length > 0 && args[0].Equals("batch", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.WriteLine("Uso: VideoCuts.Cli batch <pasta> [--output-dir <pasta>] [--max-clips N] [--no-vertical] [--whisper-local] [--whisper-model <caminho>] [--ollama] [--ollama-fallback] [--json]");
        Console.WriteLine();
        Console.WriteLine("Processa todos os vídeos da pasta (transcrição → cortes → clipes). Falhas por arquivo não interrompem o lote.");
        return 1;
    }

    string folderPath = args[1];
    if (!Directory.Exists(folderPath))
    {
        Console.WriteLine($"Erro: pasta não encontrada: {folderPath}");
        return 1;
    }

    string? outputDir = null;
    int? maxClips = null;
    bool convertToVertical = true;
    bool useOllama = false;
    bool useOllamaFallback = false;
    bool useWhisperLocal = false;
    string? whisperModelPath = null;
    bool outputJson = false;

    for (int i = 2; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--output-dir":
            case "-o":
                if (i + 1 < args.Length) outputDir = args[++i];
                break;
            case "--max-clips":
                if (i + 1 < args.Length && int.TryParse(args[++i], out int n)) maxClips = n;
                break;
            case "--no-vertical":
                convertToVertical = false;
                break;
            case "--ollama":
                useOllama = true;
                break;
            case "--ollama-fallback":
                useOllamaFallback = true;
                break;
            case "--whisper-local":
                useWhisperLocal = true;
                break;
            case "--whisper-model":
                if (i + 1 < args.Length) whisperModelPath = args[++i];
                break;
            case "--json":
                outputJson = true;
                break;
        }
    }

    var configuration = BuildConfiguration();
    EnsureFFmpegConfigured(configuration);
    string? batchWhisperPath = null;
    if (useWhisperLocal)
    {
        whisperModelPath = string.IsNullOrWhiteSpace(whisperModelPath) ? configuration["Whisper:ModelPath"] : whisperModelPath;
        if (string.IsNullOrWhiteSpace(whisperModelPath) || !File.Exists(whisperModelPath))
        {
            Console.WriteLine("Erro: com --whisper-local informe --whisper-model <caminho> ou defina Whisper:ModelPath no appsettings.json.");
            return 1;
        }
        batchWhisperPath = whisperModelPath;
    }

    var videoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".avi", ".mov", ".webm", ".m4v", ".wmv", ".flv" };
    var files = Directory.EnumerateFiles(folderPath, "*.*")
        .Where(f => videoExtensions.Contains(Path.GetExtension(f)))
        .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
        .ToList();

    if (files.Count == 0)
    {
        Console.WriteLine($"Nenhum vídeo encontrado em: {folderPath}");
        return 0;
    }

    var videoCutsOptions = BuildVideoCutsOptions(configuration);
    var settings = videoCutsOptions.Value;
    EngagingMomentsMode engagingMode = useOllamaFallback ? EngagingMomentsMode.OllamaThenOpenAi : (useOllama ? EngagingMomentsMode.Ollama : EngagingMomentsMode.OpenAi);

    var results = new List<BatchFileResult>();

    using (var loggerFactory = CreateLoggerFactory())
    {
        var pipeline = CreatePipeline(useUrl: false, sourceUrl: null, engagingMode, useLocalWhisper: useWhisperLocal, whisperModelPath: batchWhisperPath, loggerFactory, configuration, videoCutsOptions);

        for (int i = 0; i < files.Count; i++)
        {
            string fullPath = Path.GetFullPath(files[i]);
            string fileName = Path.GetFileName(fullPath);
            Console.WriteLine($"[{i + 1}/{files.Count}] {fileName}");

            try
            {
                var request = new PipelineRequest
                {
                    LocalVideoPath = fullPath,
                    OutputDirectory = outputDir ?? settings.OutputDirectory,
                    MaxClips = maxClips ?? settings.MaxClips,
                    ConvertClipsToVertical = convertToVertical,
                    TranscriptionOptions = new TranscriptionOptions { IncludeTimestamps = true }
                };

                var result = await pipeline.RunAsync(request);

                if (result.Success)
                {
                    int count = result.GeneratedClips?.Count ?? 0;
                    results.Add(new BatchFileResult(fullPath, true, count, null));
                }
                else
                {
                    results.Add(new BatchFileResult(fullPath, false, 0, result.ErrorMessage));
                    Console.WriteLine($"   Falha: {result.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                results.Add(new BatchFileResult(fullPath, false, 0, ex.Message));
                Console.WriteLine($"   Falha: {ex.Message}");
            }
        }
    }

    int ok = results.Count(r => r.Success);
    int fail = results.Count - ok;
    Console.WriteLine();
    Console.WriteLine($"Concluído: {ok} ok, {fail} falha(s).");

    if (outputJson)
    {
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(results.Select(r => new { r.FilePath, r.Success, Clips = r.ClipsGenerated, r.Error }).ToList(), new System.Text.Json.JsonSerializerOptions { WriteIndented = false }));
    }
    else
    {
        foreach (var r in results)
            Console.WriteLine(r.Success ? $"  OK   {Path.GetFileName(r.FilePath)} ({r.ClipsGenerated} clipes)" : $"  FAIL {Path.GetFileName(r.FilePath)}: {r.Error}");
    }

    return fail > 0 ? 1 : 0;
}

// ----- Modo: corte manual (um trecho) -----
if (args.Length < 3)
{
    Console.WriteLine("Uso: VideoCuts.Cli <caminho-do-video> <inicio_segundos> <fim_segundos> [caminho-saida] [--no-vertical]");
    Console.WriteLine("     VideoCuts.Cli pipeline --input <arquivo> | --url <url> [opções]");
    Console.WriteLine("     VideoCuts.Cli batch <pasta> [opções]");
    Console.WriteLine();
    Console.WriteLine("Exemplos:");
    Console.WriteLine("  Corte manual:  VideoCuts.Cli video.mp4 10 60");
    Console.WriteLine("  Pipeline:      VideoCuts.Cli pipeline --input video.mp4 --output-dir ./clips");
    Console.WriteLine("  Batch:         VideoCuts.Cli batch ./videos --output-dir ./clips --max-clips 3");
    return 1;
}

string path = args[0];
if (!double.TryParse(args[1], NumberStyles.Any, CultureInfo.InvariantCulture, out double startSeconds))
{
    Console.WriteLine("Erro: início deve ser um número (segundos).");
    return 1;
}
if (!double.TryParse(args[2], NumberStyles.Any, CultureInfo.InvariantCulture, out double endSeconds))
{
    Console.WriteLine("Erro: fim deve ser um número (segundos).");
    return 1;
}

string? outPath = args.Length > 3 && !args[3].StartsWith("--") ? args[3] : null;
bool vertical = true;
for (int i = 3; i < args.Length; i++)
{
    if (args[i] == "--no-vertical") { vertical = false; break; }
}

if (!File.Exists(path))
{
    Console.WriteLine($"Erro: arquivo não encontrado: {path}");
    return 1;
}
if (startSeconds < 0 || endSeconds <= startSeconds)
{
    Console.WriteLine("Erro: início >= 0 e fim > início.");
    return 1;
}

IVideoEditor editor = CreateVideoEditor();
var editRequest = new EditRequest
{
    InputVideoPath = Path.GetFullPath(path),
    SegmentsToKeep = new[] { new CutSegment { StartTimeSeconds = startSeconds, EndTimeSeconds = endSeconds } },
    OutputPath = outPath != null ? Path.GetFullPath(outPath) : null,
    ConvertToVertical = vertical
};

Console.WriteLine($"Entrada: {editRequest.InputVideoPath}");
Console.WriteLine($"Trecho: {startSeconds}s - {endSeconds}s | Vertical: {vertical}");
Console.WriteLine("Processando...");

var editResult = await editor.EditAsync(editRequest);

if (editResult.Success)
{
    Console.WriteLine($"Concluído: {editResult.OutputPath}");
    if (editResult.OutputDurationSeconds.HasValue)
        Console.WriteLine($"Duração: {editResult.OutputDurationSeconds.Value:F1}s");
    return 0;
}
Console.WriteLine($"Erro: {editResult.ErrorMessage}");
return 1;

record BatchFileResult(string FilePath, bool Success, int ClipsGenerated, string? Error);

enum EngagingMomentsMode { OpenAi, Ollama, OllamaThenOpenAi }
