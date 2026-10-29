using UrubuApi.Models;

namespace UrubuApi.Services.Crawlers;

public interface IColetorSite
{
    string Id { get; }
    bool PodeProcessar(string url);
    Task<IReadOnlyList<Product>> VarrerLojaAsync(string urlLoja, CancellationToken ct = default);
    Task<Product?> VarrerProdutoAsync(string urlProduto, CancellationToken ct = default);
}
