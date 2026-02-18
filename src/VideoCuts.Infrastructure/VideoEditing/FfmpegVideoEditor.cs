using VideoCuts.Core.Interfaces;
using VideoCuts.Core.Models.VideoEditing;
using FFMpegCore;

namespace VideoCuts.Infrastructure.VideoEditing;

/// <summary>
/// Implementação de <see cref="IVideoEditor"/> usando FFMpeg: corta segmentos e opcionalmente converte para 9:16.
/// </summary>
public class FfmpegVideoEditor : IVideoEditor
{
    private const int VerticalWidth = 1080;
    private const int VerticalHeight = 1920;

    /// <inheritdoc />
    public async Task<EditResult> EditAsync(
        EditRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.InputVideoPath) || !File.Exists(request.InputVideoPath))
            return new EditResult { Success = false, ErrorMessage = "Arquivo de entrada não encontrado." };

        if (request.SegmentsToKeep.Count == 0)
            return new EditResult { Success = false, ErrorMessage = "Nenhum segmento informado." };

        string outputPath = ResolveOutputPath(request);

        try
        {
            if (request.SegmentsToKeep.Count == 1)
            {
                await ProcessSingleSegmentAsync(request, outputPath, cancellationToken);
            }
            else
            {
                await ProcessMultipleSegmentsAsync(request, outputPath, cancellationToken);
            }

            double? durationSeconds = null;
            try
            {
                var probe = await FFProbe.AnalyseAsync(outputPath);
                durationSeconds = probe.Duration.TotalSeconds;
            }
            catch
            {
                // ignorar falha ao obter duração
            }

            return new EditResult
            {
                Success = true,
                OutputPath = outputPath,
                OutputDurationSeconds = durationSeconds
            };
        }
        catch (Exception ex)
        {
            if (File.Exists(outputPath))
            {
                try { File.Delete(outputPath); } catch { /* ignore */ }
            }
            return new EditResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private static string ResolveOutputPath(EditRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.OutputPath))
            return request.OutputPath;

        string dir = Path.GetDirectoryName(request.InputVideoPath) ?? ".";
        string name = Path.GetFileNameWithoutExtension(request.InputVideoPath);
        string ext = string.IsNullOrEmpty(request.OutputFormat) ? Path.GetExtension(request.InputVideoPath) : $".{request.OutputFormat.TrimStart('.')}";
        string suffix = request.ConvertToVertical ? "_vertical" : "_cut";
        return Path.Combine(dir, $"{name}{suffix}{ext}");
    }

    private async Task ProcessSingleSegmentAsync(EditRequest request, string outputPath, CancellationToken cancellationToken)
    {
        var seg = request.SegmentsToKeep[0];
        TimeSpan start = TimeSpan.FromSeconds(seg.StartTimeSeconds);
        TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0, seg.EndTimeSeconds - seg.StartTimeSeconds));

        if (request.ConvertToVertical)
        {
            await ProcessTrimAndVerticalAsync(request.InputVideoPath, outputPath, start, duration, cancellationToken);
        }
        else
        {
            await FFMpegArguments
                .FromFileInput(request.InputVideoPath, true, opts => opts.Seek(start))
                .OutputToFile(outputPath, true, opts => opts.WithDuration(duration))
                .ProcessAsynchronously(true);
        }
    }

    private async Task ProcessTrimAndVerticalAsync(string inputPath, string outputPath, TimeSpan start, TimeSpan duration, CancellationToken cancellationToken)
    {
        const string verticalFilter = "scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2";

        await FFMpegArguments
            .FromFileInput(inputPath, true, opts => opts.Seek(start))
            .OutputToFile(outputPath, true, opts => opts
                .WithDuration(duration)
                .WithCustomArgument($"-vf \"{verticalFilter}\""))
            .ProcessAsynchronously(true);
    }

    private async Task ProcessMultipleSegmentsAsync(EditRequest request, string outputPath, CancellationToken cancellationToken)
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "VideoCuts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempFiles = new List<string>();
            for (int i = 0; i < request.SegmentsToKeep.Count; i++)
            {
                var seg = request.SegmentsToKeep[i];
                TimeSpan start = TimeSpan.FromSeconds(seg.StartTimeSeconds);
                TimeSpan duration = TimeSpan.FromSeconds(Math.Max(0, seg.EndTimeSeconds - seg.StartTimeSeconds));
                string partPath = Path.Combine(tempDir, $"part_{i:D4}.mp4");

                await FFMpegArguments
                    .FromFileInput(request.InputVideoPath, true, opts => opts.Seek(start))
                    .OutputToFile(partPath, true, opts => opts.WithDuration(duration))
                    .ProcessAsynchronously(true);

                tempFiles.Add(partPath);
            }

            if (request.ConvertToVertical)
            {
                string concatPath = Path.Combine(tempDir, "concat.mp4");
                await Task.Run(() => FFMpeg.Join(concatPath, tempFiles.ToArray()), cancellationToken);
                const string verticalFilter = "scale=1080:1920:force_original_aspect_ratio=decrease,pad=1080:1920:(ow-iw)/2:(oh-ih)/2";
                await FFMpegArguments
                    .FromFileInput(concatPath, true)
                    .OutputToFile(outputPath, true, opts => opts.WithCustomArgument($"-vf \"{verticalFilter}\""))
                    .ProcessAsynchronously(true);
            }
            else
            {
                await Task.Run(() => FFMpeg.Join(outputPath, tempFiles.ToArray()), cancellationToken);
            }
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
            catch
            {
                // ignorar falha ao limpar temp
            }
        }
    }
}
