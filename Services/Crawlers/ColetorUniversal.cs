using HtmlAgilityPack;
using UrubuApi.Models;
using UrubuApi.Services.Crawlers.Extractors;
using UrubuApi.Services.Crawlers.Http;

namespace UrubuApi.Services.Crawlers;

public class ColetorUniversal : IColetorSite
{
    private readonly IClienteHttpColetor _clienteHttp;
    private readonly ExtratorShopify _extratorShopify;
    private readonly ExtratorSitemap _extratorSitemap;
    private readonly ExtratorJsonLd _extratorJsonLd;
    private readonly ExtratorOpenGraph _extratorOpenGraph;
    private readonly ExtratorCardsHtml _extratorCardsHtml;
    private readonly ILogger<ColetorUniversal> _logger;

    public string Id => "universal";

    public ColetorUniversal(
        IClienteHttpColetor clienteHttp,
        ExtratorShopify extratorShopify,
        ExtratorSitemap extratorSitemap,
        ExtratorJsonLd extratorJsonLd,
        ExtratorOpenGraph extratorOpenGraph,
        ExtratorCardsHtml extratorCardsHtml,
        ILogger<ColetorUniversal> logger)
    {
        _clienteHttp = clienteHttp;
        _extratorShopify = extratorShopify;
        _extratorSitemap = extratorSitemap;
        _extratorJsonLd = extratorJsonLd;
        _extratorOpenGraph = extratorOpenGraph;
        _extratorCardsHtml = extratorCardsHtml;
        _logger = logger;
    }

    public bool PodeProcessar(string url)
    {
        return !string.IsNullOrWhiteSpace(url) &&
               (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                url.Contains('.'));
    }

    public async Task<IReadOnlyList<Product>> VarrerLojaAsync(string urlLoja, CancellationToken ct = default)
    {
        var urlLimpa = UrlUtils.LimparUrlLoja(urlLoja);
        var origem = UrlUtils.ObterOrigemLoja(urlLoja);
        var marca = UrlUtils.ExtrairMarcaDaUrl(urlLoja);

        // 1. Se a URL for diretamente uma página de produto
        if (UrlUtils.EhUrlDeProdutoEspecifico(urlLoja))
        {
            var individual = await VarrerProdutoAsync(urlLoja, ct);
            return individual != null ? new List<Product> { individual } : Array.Empty<Product>();
        }

        // 2. Estratégia Shopify API (/products.json)
        try
        {
            var produtosShopify = await _extratorShopify.ExtrairCatalogoLojaAsync(origem, marca, ct);
            if (produtosShopify.Count > 0)
            {
                _logger.LogInformation("Shopify API: {Count} produtos obtidos de {Origem}", produtosShopify.Count, origem);
                return produtosShopify;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Estratégia Shopify falhou para {Origem}", origem);
        }

        // 3. Estratégia Sitemap XML
        try
        {
            var produtosSitemap = await _extratorSitemap.ExtrairDoSitemapAsync(origem, marca, ct);
            if (produtosSitemap.Count > 0)
            {
                _logger.LogInformation("Sitemap XML: {Count} produtos obtidos de {Origem}", produtosSitemap.Count, origem);
                return produtosSitemap;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Estratégia Sitemap falhou para {Origem}", origem);
        }

        // 4. Estratégia HTML (Schema.org JSON-LD + DOM Cards)
        try
        {
            var html = await _clienteHttp.ObterHtmlAsync(urlLimpa, ct);
            if (!string.IsNullOrWhiteSpace(html))
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var produtosJsonLd = _extratorJsonLd.ExtrairCatalogo(doc, origem, marca);
                if (produtosJsonLd.Count >= 6) return produtosJsonLd;

                var produtosCards = _extratorCardsHtml.ExtrairCards(doc, origem, marca);
                if (produtosCards.Count > 0) return produtosCards;

                if (produtosJsonLd.Count > 0) return produtosJsonLd;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Estratégia HTML falhou para {Url}", urlLimpa);
        }

        return Array.Empty<Product>();
    }

    public async Task<Product?> VarrerProdutoAsync(string urlProduto, CancellationToken ct = default)
    {
        var urlLimpa = UrlUtils.LimparUrlLoja(urlProduto);
        var marca = UrlUtils.ExtrairMarcaDaUrl(urlLimpa);

        // 1. Tenta Shopify JSON
        if (urlLimpa.Contains("/products/"))
        {
            var prodShopify = await _extratorShopify.ExtrairProdutoJsonAsync(urlLimpa, marca, ct);
            if (prodShopify != null) return prodShopify;
        }

        // 2. Extração via HTML da página
        var html = await _clienteHttp.ObterHtmlAsync(urlLimpa, ct);
        if (string.IsNullOrWhiteSpace(html)) return null;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // A) Schema.org JSON-LD
        var prodJsonLd = _extratorJsonLd.ExtrairProduto(doc, urlLimpa, marca);
        if (prodJsonLd != null) return prodJsonLd;

        // B) OpenGraph Meta Tags
        var prodOg = _extratorOpenGraph.ExtrairProduto(doc, urlLimpa, marca);
        if (prodOg != null) return prodOg;

        return null;
    }
}
