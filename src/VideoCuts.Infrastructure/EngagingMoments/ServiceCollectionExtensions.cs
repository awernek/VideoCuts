using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VideoCuts.Core.Interfaces;

namespace VideoCuts.Infrastructure.EngagingMoments;

/// <summary>
/// Extensões de DI para registrar implementações de <see cref="IEngagingMomentsService"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Regista opções Ollama (seção "Ollama"), HttpClient e <see cref="OllamaEngagingMomentsService"/> como <see cref="IEngagingMomentsService"/>.
    /// Requer que logging esteja configurado (ex.: AddLogging()) para obter ILogger.
    /// </summary>
    public static IServiceCollection AddOllamaEngagingMoments(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<OllamaEngagingMomentsOptions>(
            configuration.GetSection(OllamaEngagingMomentsOptions.SectionName));
        services.AddHttpClient<IEngagingMomentsService, OllamaEngagingMomentsService>();
        return services;
    }

    /// <summary>
    /// Regista estratégia de fallback: primeiro Ollama (local), em falha usa OpenAI.
    /// Requer OPENAI_API_KEY ou configuração "OpenAI:ApiKey" para o fallback.
    /// Usa keyed services para manter as implementações intercambiáveis.
    /// </summary>
    public static IServiceCollection AddEngagingMomentsWithOllamaFallback(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<OllamaEngagingMomentsOptions>(
            configuration.GetSection(OllamaEngagingMomentsOptions.SectionName));
        services.AddHttpClient();

        services.AddSingleton<OllamaEngagingMomentsService>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            return new OllamaEngagingMomentsService(
                factory.CreateClient(),
                sp.GetRequiredService<IOptions<OllamaEngagingMomentsOptions>>(),
                sp.GetRequiredService<ILogger<OllamaEngagingMomentsService>>());
        });
        services.AddKeyedSingleton<IEngagingMomentsService>("primary", (sp, _) => sp.GetRequiredService<OllamaEngagingMomentsService>());

        services.AddSingleton<LlmEngagingMomentsService>(sp =>
        {
            var apiKey = configuration["OpenAI:ApiKey"] ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";
            return new LlmEngagingMomentsService(apiKey);
        });
        services.AddKeyedSingleton<IEngagingMomentsService>("fallback", (sp, _) => sp.GetRequiredService<LlmEngagingMomentsService>());

        services.AddSingleton<IEngagingMomentsService>(sp =>
        {
            var primary = sp.GetKeyedService<IEngagingMomentsService>("primary") ?? throw new InvalidOperationException("Keyed service 'primary' not registered.");
            var fallback = sp.GetKeyedService<IEngagingMomentsService>("fallback") ?? throw new InvalidOperationException("Keyed service 'fallback' not registered.");
            return new FallbackEngagingMomentsService(primary, fallback, sp.GetRequiredService<ILogger<FallbackEngagingMomentsService>>());
        });

        return services;
    }
}
