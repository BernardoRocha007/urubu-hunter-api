using UrubuApi.DTOs;
using UrubuApi.Models;

namespace UrubuApi.Services;

public interface IServicoFeed
{
    Task<FeedResponse> ObterFeedEstaticoAsync(string dominio = "fashion", CancellationToken ct = default);
    Task<FeedResponse> ObterFeedFiltradoAsync(string dominio, int? pontuacaoMinima, bool apenasPromocao, string? categoria, string? busca, int pagina, int limite, CancellationToken ct = default);
    Task<CuratedItem?> DefinirFeedbackAsync(string id, string? feedback, CancellationToken ct = default);
    Task<(int Limpos, int Preservados)> LimparFeedAsync(string dominio, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ObterCategoriasAsync(string dominio, CancellationToken ct = default);
    void InvalidarCacheEstatico();
}
