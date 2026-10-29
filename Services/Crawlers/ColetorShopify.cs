using UrubuApi.Models;
using UrubuApi.Services.Crawlers.Extractors;

namespace UrubuApi.Services.Crawlers;

public class ColetorShopify : IColetorSite
{
    private readonly ExtratorShopify _extrator;

    public string Id => "shopify";

    public ColetorShopify(ExtratorShopify extrator)
    {
        _extrator = extrator;
    }

    public bool PodeProcessar(string url)
    {
        return url.Contains("myshopify.com", StringComparison.OrdinalIgnoreCase);
    }

    public Task<IReadOnlyList<Product>> VarrerLojaAsync(string urlLoja, CancellationToken ct = default)
    {
        var origem = UrlUtils.ObterOrigemLoja(urlLoja);
        var marca = UrlUtils.ExtrairMarcaDaUrl(urlLoja);
        return _extrator.ExtrairCatalogoLojaAsync(origem, marca, ct);
    }

    public Task<Product?> VarrerProdutoAsync(string urlProduto, CancellationToken ct = default)
    {
        var cleanUrl = UrlUtils.LimparUrlLoja(urlProduto);
        var marca = UrlUtils.ExtrairMarcaDaUrl(cleanUrl);
        return _extrator.ExtrairProdutoJsonAsync(cleanUrl, marca, ct);
    }
}
