using System.Globalization;
using System.Xml.Linq;
using UrubuApi.Models;
using UrubuApi.Services.Crawlers.Http;

namespace UrubuApi.Services.Crawlers.Extractors;

public class SitemapExtractor
{
    private readonly ICrawlerHttpClient _httpClient;

    public SitemapExtractor(ICrawlerHttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<Product>> ExtractFromSitemapAsync(string origin, string brandDefault, CancellationToken ct = default)
    {
        var products = new List<Product>();
        string[] paths = { "/sitemap_products_1.xml", "/sitemap.xml", "/sitemap_index.xml" };

        foreach (var path in paths)
        {
            try
            {
                var xml = await _httpClient.GetXmlAsync($"{origin}{path}", ct);
                if (string.IsNullOrWhiteSpace(xml)) continue;

                var xdoc = XDocument.Parse(xml);
                XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
                XNamespace imgNs = "http://www.google.com/schemas/sitemap-image/1.1";

                var urls = xdoc.Descendants(ns + "url").ToList();
                if (urls.Count == 0) continue;

                foreach (var urlEl in urls.Take(120))
                {
                    var loc = urlEl.Element(ns + "loc")?.Value;
                    if (string.IsNullOrEmpty(loc) || !UrlUtils.IsSpecificProductUrl(loc)) continue;

                    var imageEl = urlEl.Element(imgNs + "image");
                    var imgLoc = imageEl?.Element(imgNs + "loc")?.Value;
                    var imgTitle = imageEl?.Element(imgNs + "title")?.Value;

                    var slug = loc.Split('/').LastOrDefault()?.Replace("-", " ") ?? "Produto";
                    var title = !string.IsNullOrWhiteSpace(imgTitle)
                        ? imgTitle
                        : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(slug);

                    var cleanProdUrl = UrlUtils.CleanStoreUrl(loc);
                    products.Add(new Product
                    {
                        Id = UrlUtils.GenerateEntityId("ext", cleanProdUrl),
                        Domain = "fashion",
                        Title = UrlUtils.CleanText(title),
                        Brand = brandDefault,
                        Price = 249.90m,
                        Url = cleanProdUrl,
                        Images = !string.IsNullOrEmpty(imgLoc)
                            ? new List<string> { imgLoc }
                            : new List<string> { "https://images.unsplash.com/photo-1523381210434-271e8be1f52b?auto=format&fit=crop&w=800&q=80" },
                        Category = CategoryUtils.InferCategory(title),
                        AvailableSizes = new List<string> { "M", "G" },
                        Tags = new List<string> { "streetwear", brandDefault.ToLowerInvariant() },
                        Source = new Uri(origin).Host,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                if (products.Count > 0) return products;
            }
            catch
            {
                // Tenta próximo path
            }
        }

        return products;
    }
}
