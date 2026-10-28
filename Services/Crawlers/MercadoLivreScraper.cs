using System.Text.RegularExpressions;
using HtmlAgilityPack;
using UrubuApi.Models;
using UrubuApi.Services.Crawlers.Http;

namespace UrubuApi.Services.Crawlers;

public class MercadoLivreScraper : ISiteScraper
{
    private readonly ICrawlerHttpClient _httpClient;

    public string Id => "mercadolivre";

    public MercadoLivreScraper(ICrawlerHttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public bool CanHandle(string url)
    {
        return url.Contains("mercadolivre.com.br", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<Product>> ScrapeStoreAsync(string storeUrl, CancellationToken ct = default)
    {
        var cleanUrl = UrlUtils.CleanStoreUrl(storeUrl);
        var origin = UrlUtils.GetStoreOrigin(storeUrl);
        var products = new List<Product>();

        var searchUrl = cleanUrl;
        var isRoot = cleanUrl == origin || cleanUrl == $"{origin}/" || cleanUrl.Contains("www.mercadolivre.com.br");
        if (isRoot && !cleanUrl.Contains("lista.mercadolivre") && !cleanUrl.Contains("loja.mercadolivre"))
        {
            searchUrl = "https://lista.mercadolivre.com.br/calcados-roupas-bolsas/streetwear_NoIndex_True";
        }

        var html = await _httpClient.GetHtmlAsync(searchUrl, ct);
        if (string.IsNullOrWhiteSpace(html)) return products;

        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        var nodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'ui-search-result') or contains(@class, 'ui-search-layout__item') or contains(@class, 'poly-card')]");
        if (nodes != null)
        {
            foreach (var node in nodes.Take(30))
            {
                var titleNode = node.SelectSingleNode(".//h2") ??
                                node.SelectSingleNode(".//*[contains(@class, 'ui-search-item__title') or contains(@class, 'poly-component__title')]");
                var linkNode = node.SelectSingleNode(".//a[contains(@class, 'ui-search-link') or contains(@class, 'poly-component__title')]") ??
                               node.SelectSingleNode(".//a[@href]");
                var priceNode = node.SelectSingleNode(".//*[contains(@class, 'andes-money-amount__fraction')]");
                var imgNode = node.SelectSingleNode(".//img");

                var title = titleNode?.InnerText?.Trim() ?? string.Empty;
                var link = linkNode?.GetAttributeValue("href", string.Empty);
                var priceText = priceNode?.InnerText?.Trim();
                var imgSrc = imgNode?.GetAttributeValue("src", string.Empty);
                if (string.IsNullOrEmpty(imgSrc) || imgSrc.StartsWith("data:"))
                {
                    imgSrc = imgNode?.GetAttributeValue("data-src", string.Empty);
                }

                if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(link))
                {
                    var cleanLink = UrlUtils.CleanStoreUrl(link);
                    var price = PriceUtils.ParsePrice(priceText);
                    if (price <= 0m) price = 149.90m;

                    products.Add(new Product
                    {
                        Id = UrlUtils.GenerateEntityId("ml", cleanLink),
                        Domain = "fashion",
                        Title = title.Length > 120 ? title[..120] : title,
                        Brand = "Mercado Livre",
                        Price = price,
                        Url = cleanLink,
                        Images = !string.IsNullOrEmpty(imgSrc) ? new List<string> { imgSrc } : new List<string> { "https://images.unsplash.com/photo-1523381210434-271e8be1f52b?auto=format&fit=crop&w=800&q=80" },
                        Category = CategoryUtils.InferCategory(title),
                        AvailableSizes = new List<string> { "Consultar Loja" },
                        Tags = new List<string> { "mercado-livre", "marketplace", "streetwear" },
                        Source = "mercadolivre.com.br",
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }
        }

        return products;
    }

    public async Task<Product?> ScrapeProductAsync(string productUrl, CancellationToken ct = default)
    {
        var cleanUrl = UrlUtils.CleanStoreUrl(productUrl);
        var match = Regex.Match(cleanUrl, @"(MLB-?\d+)", RegexOptions.IgnoreCase);
        var mlbId = match.Success ? match.Groups[1].Value.Replace("-", "") : null;

        return new Product
        {
            Id = UrlUtils.GenerateEntityId("ml", mlbId ?? cleanUrl),
            Domain = "fashion",
            Title = $"Produto Mercado Livre {mlbId}",
            Brand = "Mercado Livre",
            Price = 189.90m,
            Url = cleanUrl,
            Images = new List<string> { "https://images.unsplash.com/photo-1523381210434-271e8be1f52b?auto=format&fit=crop&w=800&q=80" },
            Category = "Marketplace",
            AvailableSizes = new List<string> { "Consultar Loja" },
            Tags = new List<string> { "mercado-livre", "marketplace" },
            Source = "mercadolivre.com.br",
            CreatedAt = DateTime.UtcNow
        };
    }
}
