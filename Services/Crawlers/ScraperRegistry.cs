namespace UrubuApi.Services.Crawlers;

public class ScraperRegistry
{
    private readonly IEnumerable<ISiteScraper> _scrapers;
    private readonly UniversalScraper _universalScraper;

    public ScraperRegistry(IEnumerable<ISiteScraper> scrapers, UniversalScraper universalScraper)
    {
        _scrapers = scrapers;
        _universalScraper = universalScraper;
    }

    public ISiteScraper GetScraperForUrl(string url)
    {
        // Se for Mercado Livre ou outro scraper específico com CanHandle restritivo
        var matched = _scrapers.FirstOrDefault(s => s is not UniversalScraper && s.CanHandle(url));
        return matched ?? _universalScraper;
    }
}
