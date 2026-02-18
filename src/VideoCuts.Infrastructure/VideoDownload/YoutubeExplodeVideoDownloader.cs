using VideoCuts.Core.Interfaces;
using VideoCuts.Core.Models.VideoDownload;
using YoutubeExplode;
using YoutubeExplode.Videos.Streams;

namespace VideoCuts.Infrastructure.VideoDownload;

/// <summary>
/// Implementação de <see cref="IVideoDownloader"/> usando YoutubeExplode para URLs do YouTube.
/// Baixa o vídeo para um arquivo local e retorna o caminho; erros e URLs inválidas retornam <see cref="DownloadResult"/> com falha.
/// </summary>
public class YoutubeExplodeVideoDownloader : IVideoDownloader
{
    private readonly YoutubeClient _client;

    public YoutubeExplodeVideoDownloader(YoutubeClient? client = null)
    {
        _client = client ?? new YoutubeClient();
    }

    /// <inheritdoc />
    public async Task<DownloadResult> DownloadAsync(
        VideoSource source,
        DownloadOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source?.Url))
            return new DownloadResult { Success = false, ErrorMessage = "URL não informada." };

        if (!IsYouTubeUrl(source.Url))
            return new DownloadResult { Success = false, ErrorMessage = "URL não é do YouTube. Use um link youtube.com ou youtu.be." };

        string outputDir = options.OutputDirectory ?? Path.GetTempPath();
        Directory.CreateDirectory(outputDir);

        try
        {
            var video = await _client.Videos.GetAsync(source.Url, cancellationToken).ConfigureAwait(false);
            var manifest = await _client.Videos.Streams.GetManifestAsync(source.Url, cancellationToken).ConfigureAwait(false);
            var muxed = manifest.GetMuxedStreams().GetWithHighestVideoQuality();
            if (muxed == null)
                return new DownloadResult { Success = false, ErrorMessage = "Nenhum stream de vídeo disponível para este vídeo." };

            string extension = muxed.Container.Name;
            string baseName = !string.IsNullOrWhiteSpace(source.Title)
                ? SanitizeFileName(source.Title)
                : SanitizeFileName(video.Title);
            if (string.IsNullOrEmpty(baseName))
                baseName = video.Id;
            if (!baseName.EndsWith($".{extension}", StringComparison.OrdinalIgnoreCase))
                baseName += $".{extension}";
            string localPath = Path.Combine(outputDir, baseName);

            await _client.Videos.Streams.DownloadAsync(muxed, localPath, null, cancellationToken).ConfigureAwait(false);
            return new DownloadResult { Success = true, LocalPath = localPath };
        }
        catch (Exception ex)
        {
            string message = ex.Message;
            if (ex.InnerException != null)
                message += " " + ex.InnerException.Message;
            return new DownloadResult { Success = false, ErrorMessage = message.Trim() };
        }
    }

    private static bool IsYouTubeUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        try
        {
            var uri = new Uri(url);
            return uri.Host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                   uri.Host.Equals("youtu.be", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
            name = name.Replace(c, '_');
        return name.Trim().TrimEnd('.');
    }
}
