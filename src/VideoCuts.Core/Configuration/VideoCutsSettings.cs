namespace VideoCuts.Core.Configuration;

/// <summary>
/// Configuração centralizada do pipeline de clipes.
/// Carregada de appsettings.json (seção "VideoCuts") e injetada via IOptions&lt;VideoCutsSettings&gt;.
/// </summary>
public class VideoCutsSettings
{
    /// <summary>Nome da seção no appsettings.json.</summary>
    public const string SectionName = "VideoCuts";

    /// <summary>Número máximo de clipes a gerar por execução. Null = sem limite (todos os cortes sugeridos).</summary>
    public int? MaxClips { get; set; }

    /// <summary>Se true, converte cada clipe para formato vertical (9:16).</summary>
    public bool ConvertToVertical { get; set; } = true;

    /// <summary>Pasta padrão para salvar os clipes gerados. Null = usar o diretório do vídeo de entrada.</summary>
    public string? OutputDirectory { get; set; }
}
