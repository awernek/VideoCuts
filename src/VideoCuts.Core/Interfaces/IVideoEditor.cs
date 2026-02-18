using VideoCuts.Core.Models.VideoEditing;

namespace VideoCuts.Core.Interfaces;

/// <summary>
/// Contrato para edição de vídeo (cortar, concatenar segmentos).
/// </summary>
public interface IVideoEditor
{
    /// <summary>
    /// Edita o vídeo conforme a requisição (mantém apenas os segmentos indicados, na ordem dada).
    /// </summary>
    /// <param name="request">Requisição com input, segmentos a manter e opções de saída.</param>
    /// <param name="cancellationToken">Token de cancelamento.</param>
    /// <returns>Resultado com caminho do vídeo gerado ou mensagem de erro.</returns>
    Task<EditResult> EditAsync(
        EditRequest request,
        CancellationToken cancellationToken = default);
}
