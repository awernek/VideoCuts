using VideoCuts.Core.Interfaces;
using VideoCuts.Core.Models.CutDetection;
using VideoCuts.Core.Models.VideoEditing;
using VideoCuts.Infrastructure.VideoEditing;

// Exemplo de uso do FfmpegVideoEditor:
// - Recebe caminho do vídeo de entrada, tempo inicial e final
// - Corta o trecho e opcionalmente converte para vertical (9:16)
// - Salva o arquivo de saída

if (args.Length < 3)
{
    Console.WriteLine("Uso: VideoCuts.Cli <caminho-do-video> <inicio_segundos> <fim_segundos> [caminho-saida] [--no-vertical]");
    Console.WriteLine();
    Console.WriteLine("Exemplo:");
    Console.WriteLine("  VideoCuts.Cli video.mp4 10 60");
    Console.WriteLine("    -> Corta de 10s a 60s e converte para 9:16 (vertical)");
    Console.WriteLine();
    Console.WriteLine("  VideoCuts.Cli video.mp4 0 30 output_cortado.mp4 --no-vertical");
    Console.WriteLine("    -> Corta de 0s a 30s, mantém proporção original, salva em output_cortado.mp4");
    return 1;
}

string inputPath = args[0];
if (!double.TryParse(args[1], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double startSeconds))
{
    Console.WriteLine("Erro: início deve ser um número (segundos).");
    return 1;
}
if (!double.TryParse(args[2], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double endSeconds))
{
    Console.WriteLine("Erro: fim deve ser um número (segundos).");
    return 1;
}

string? outputPath = args.Length > 3 && !args[3].StartsWith("--") ? args[3] : null;
bool convertToVertical = true;
for (int i = 3; i < args.Length; i++)
{
    if (args[i] == "--no-vertical")
    {
        convertToVertical = false;
        break;
    }
}

if (!File.Exists(inputPath))
{
    Console.WriteLine($"Erro: arquivo não encontrado: {inputPath}");
    return 1;
}

if (startSeconds < 0 || endSeconds <= startSeconds)
{
    Console.WriteLine("Erro: início deve ser >= 0 e fim deve ser maior que início.");
    return 1;
}

IVideoEditor editor = new FfmpegVideoEditor();

var request = new EditRequest
{
    InputVideoPath = Path.GetFullPath(inputPath),
    SegmentsToKeep = new[] { new CutSegment { StartTimeSeconds = startSeconds, EndTimeSeconds = endSeconds } },
    OutputPath = outputPath != null ? Path.GetFullPath(outputPath) : null,
    ConvertToVertical = convertToVertical
};

Console.WriteLine($"Entrada: {request.InputVideoPath}");
Console.WriteLine($"Trecho: {startSeconds}s - {endSeconds}s");
Console.WriteLine($"Vertical (9:16): {convertToVertical}");
Console.WriteLine("Processando...");

var result = await editor.EditAsync(request);

if (result.Success)
{
    Console.WriteLine($"Concluído: {result.OutputPath}");
    if (result.OutputDurationSeconds.HasValue)
        Console.WriteLine($"Duração: {result.OutputDurationSeconds.Value:F1}s");
    return 0;
}
else
{
    Console.WriteLine($"Erro: {result.ErrorMessage}");
    return 1;
}
