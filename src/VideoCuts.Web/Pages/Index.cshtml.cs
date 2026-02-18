using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;

namespace VideoCuts.Web.Pages;

public class IndexModel : PageModel
{
    private readonly IConfiguration _configuration;

    public IndexModel(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    /// <summary>Caminho do modelo Whisper do appsettings (para exibir na combo).</summary>
    public string? WhisperModelPathFromSettings => _configuration["Whisper:ModelPath"]?.Trim();
}
