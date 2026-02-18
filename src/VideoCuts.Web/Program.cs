using FFMpegCore;
using Microsoft.Extensions.Options;
using VideoCuts.Core.Configuration;
using VideoCuts.Web.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorPages();
builder.Services.AddSingleton<PipelineJobHost>();

var app = builder.Build();

// Configurar FFmpeg a partir do appsettings (igual à CLI)
var ffmpegFolder = app.Configuration["FFmpeg:BinaryFolder"];
if (!string.IsNullOrWhiteSpace(ffmpegFolder) && Directory.Exists(ffmpegFolder.Trim()))
    GlobalFFOptions.Configure(new FFOptions { BinaryFolder = ffmpegFolder.Trim() });

app.UseStaticFiles();
app.MapRazorPages();
app.MapGet("/", () => Results.Redirect("/Index"));
app.MapGet("/api/job/status", (PipelineJobHost host) =>
{
    var s = host.GetStatus();
    return Results.Ok(new
    {
        state = s.State.ToString(),
        message = s.Message,
        success = s.Success,
        errorMessage = s.ErrorMessage,
        clipPaths = s.ClipPaths,
        clipDetails = s.ClipDetails?.Select(c => new { path = c.Path, transcript = c.Transcript }).ToList()
    });
});
app.MapPost("/api/job/run", async (RunRequest request, PipelineJobHost host, HttpContext ctx) =>
{
    if (request == null)
        return Results.BadRequest("Corpo da requisição inválido.");
    await host.StartAsync(request, ctx.RequestAborted);
    return Results.Ok(new { started = true });
});

app.Run();
