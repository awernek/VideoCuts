using VideoCuts.Core.Interfaces;
using VideoCuts.Core.Models.VideoDownload;

namespace VideoCuts.Infrastructure.VideoDownload;

/// <summary>
/// Baixa vídeo de URLs HTTP/HTTPS diretas (ex.: link para arquivo .mp4).
/// Não suporta YouTube/Vimeo etc.; para isso use yt-dlp e depois LocalVideoPath no pipeline.
/// </summary>
public class HttpVideoDownloader : IVideoDownloader
{
    private readonly HttpClient _httpClient;

    public HttpVideoDownloader(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "VideoCuts/1.0");
    }

    /// <inheritdoc />
    public async Task<DownloadResult> DownloadAsync(
        VideoSource source,
        DownloadOptions options,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(source?.Url))
            return new DownloadResult { Success = false, ErrorMessage = "URL não informada." };

        var uri = new Uri(source.Url);
        if (!uri.Scheme.Equals("http", StringComparison.OrdinalIgnoreCase) &&
            !uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase))
            return new DownloadResult { Success = false, ErrorMessage = "Apenas URLs HTTP/HTTPS são suportadas." };

        string dir = options.OutputDirectory ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);
        string fileName = string.IsNullOrWhiteSpace(source.Title)
            ? Path.GetFileName(uri.LocalPath)
            : SanitizeFileName(source.Title);
        if (string.IsNullOrEmpty(Path.GetExtension(fileName)))
            fileName += ".mp4";
        string localPath = Path.Combine(dir, fileName);

        try
        {
            using var response = await _httpClient.GetAsync(source.Url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var file = File.Create(localPath);
            await stream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            return new DownloadResult { Success = true, LocalPath = localPath };
        }
        catch (Exception ex)
        {
            if (File.Exists(localPath)) try { File.Delete(localPath); } catch { }
            return new DownloadResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var c in invalid)
            name = name.Replace(c, '_');
        return name.Trim();
    }
}
