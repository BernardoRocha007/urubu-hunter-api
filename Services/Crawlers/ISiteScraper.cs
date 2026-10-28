using UrubuApi.Models;

namespace UrubuApi.Services.Crawlers;

public interface ISiteScraper
{
    string Id { get; }
    bool CanHandle(string url);
    Task<IReadOnlyList<Product>> ScrapeStoreAsync(string storeUrl, CancellationToken ct = default);
    Task<Product?> ScrapeProductAsync(string productUrl, CancellationToken ct = default);
}
