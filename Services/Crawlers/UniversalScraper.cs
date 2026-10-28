using HtmlAgilityPack;
using UrubuApi.Models;
using UrubuApi.Services.Crawlers.Extractors;
using UrubuApi.Services.Crawlers.Http;

namespace UrubuApi.Services.Crawlers;

/// <summary>
/// Orquestrador de scraping universal (SRP / SOLID):
/// Não possui acoplamento direto com HttpClient ou detalhes de parsing de formatos específicos.
/// Delega responsabilidades para extratores especializados (Shopify, Sitemap, JsonLd, OpenGraph, HtmlCards).
/// </summary>
public class UniversalScraper : ISiteScraper
{
    private readonly ICrawlerHttpClient _httpClient;
    private readonly ShopifyExtractor _shopifyExtractor;
    private readonly SitemapExtractor _sitemapExtractor;
    private readonly JsonLdExtractor _jsonLdExtractor;
    private readonly OpenGraphExtractor _openGraphExtractor;
    private readonly HtmlCardExtractor _htmlCardExtractor;
    private readonly ILogger<UniversalScraper> _logger;

    public string Id => "universal";

    public UniversalScraper(
        ICrawlerHttpClient httpClient,
        ShopifyExtractor shopifyExtractor,
        SitemapExtractor sitemapExtractor,
        JsonLdExtractor jsonLdExtractor,
        OpenGraphExtractor openGraphExtractor,
        HtmlCardExtractor htmlCardExtractor,
        ILogger<UniversalScraper> logger)
    {
        _httpClient = httpClient;
        _shopifyExtractor = shopifyExtractor;
        _sitemapExtractor = sitemapExtractor;
        _jsonLdExtractor = jsonLdExtractor;
        _openGraphExtractor = openGraphExtractor;
        _htmlCardExtractor = htmlCardExtractor;
        _logger = logger;
    }

    public bool CanHandle(string url)
    {
        return !string.IsNullOrWhiteSpace(url) &&
               (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                url.Contains('.'));
    }

    public async Task<IReadOnlyList<Product>> ScrapeStoreAsync(string storeUrl, CancellationToken ct = default)
    {
        var cleanUrl = UrlUtils.CleanStoreUrl(storeUrl);
        var origin = UrlUtils.GetStoreOrigin(storeUrl);
        var brand = UrlUtils.ExtractBrandFromUrl(storeUrl);

        // 1. Se a URL for diretamente uma página de produto
        if (UrlUtils.IsSpecificProductUrl(storeUrl))
        {
            var single = await ScrapeProductAsync(storeUrl, ct);
            return single != null ? new List<Product> { single } : Array.Empty<Product>();
        }

        // 2. Estratégia Shopify API (/products.json)
        try
        {
            var shopifyProducts = await _shopifyExtractor.ExtractStoreCatalogAsync(origin, brand, ct);
            if (shopifyProducts.Count > 0)
            {
                _logger.LogInformation("Shopify API: {Count} produtos obtidos de {Origin}", shopifyProducts.Count, origin);
                return shopifyProducts;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Estratégia Shopify falhou para {Origin}", origin);
        }

        // 3. Estratégia Sitemap XML
        try
        {
            var sitemapProducts = await _sitemapExtractor.ExtractFromSitemapAsync(origin, brand, ct);
            if (sitemapProducts.Count > 0)
            {
                _logger.LogInformation("Sitemap XML: {Count} produtos obtidos de {Origin}", sitemapProducts.Count, origin);
                return sitemapProducts;
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Estratégia Sitemap falhou para {Origin}", origin);
        }

        // 4. Estratégia HTML (Schema.org JSON-LD + DOM Cards)
        try
        {
            var html = await _httpClient.GetHtmlAsync(cleanUrl, ct);
            if (!string.IsNullOrWhiteSpace(html))
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var jsonLdProducts = _jsonLdExtractor.ExtractCatalog(doc, origin, brand);
                if (jsonLdProducts.Count >= 6) return jsonLdProducts;

                var cardProducts = _htmlCardExtractor.ExtractCards(doc, origin, brand);
                if (cardProducts.Count > 0) return cardProducts;

                if (jsonLdProducts.Count > 0) return jsonLdProducts;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Estratégia HTML falhou para {Url}", cleanUrl);
        }

        return Array.Empty<Product>();
    }

    public async Task<Product?> ScrapeProductAsync(string productUrl, CancellationToken ct = default)
    {
        var cleanUrl = UrlUtils.CleanStoreUrl(productUrl);
        var brand = UrlUtils.ExtractBrandFromUrl(cleanUrl);

        // 1. Tenta Shopify JSON
        if (cleanUrl.Contains("/products/"))
        {
            var shopifyProd = await _shopifyExtractor.ExtractProductJsonAsync(cleanUrl, brand, ct);
            if (shopifyProd != null) return shopifyProd;
        }

        // 2. Extração via HTML da página
        var html = await _httpClient.GetHtmlAsync(cleanUrl, ct);
        if (string.IsNullOrWhiteSpace(html)) return null;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // A) Schema.org JSON-LD
        var jsonLdProd = _jsonLdExtractor.ExtractProduct(doc, cleanUrl, brand);
        if (jsonLdProd != null) return jsonLdProd;

        // B) OpenGraph Meta Tags
        var ogProd = _openGraphExtractor.ExtractProduct(doc, cleanUrl, brand);
        if (ogProd != null) return ogProd;

        return null;
    }
}
