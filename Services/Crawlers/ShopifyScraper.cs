using UrubuApi.Models;
using UrubuApi.Services.Crawlers.Extractors;

namespace UrubuApi.Services.Crawlers;

public class ShopifyScraper : ISiteScraper
{
    private readonly ShopifyExtractor _extractor;

    public string Id => "shopify";

    public ShopifyScraper(ShopifyExtractor extractor)
    {
        _extractor = extractor;
    }

    public bool CanHandle(string url)
    {
        return url.Contains("myshopify.com", StringComparison.OrdinalIgnoreCase);
    }

    public Task<IReadOnlyList<Product>> ScrapeStoreAsync(string storeUrl, CancellationToken ct = default)
    {
        var origin = UrlUtils.GetStoreOrigin(storeUrl);
        var brand = UrlUtils.ExtractBrandFromUrl(storeUrl);
        return _extractor.ExtractStoreCatalogAsync(origin, brand, ct);
    }

    public Task<Product?> ScrapeProductAsync(string productUrl, CancellationToken ct = default)
    {
        var cleanUrl = UrlUtils.CleanStoreUrl(productUrl);
        var brand = UrlUtils.ExtractBrandFromUrl(cleanUrl);
        return _extractor.ExtractProductJsonAsync(cleanUrl, brand, ct);
    }
}
