using HtmlAgilityPack;
using UrubuApi.Models;

namespace UrubuApi.Services.Crawlers.Extractors;

public class OpenGraphExtractor
{
    public Product? ExtractProduct(HtmlDocument doc, string url, string brandDefault)
    {
        var ogTitle = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title' or @name='twitter:title']")?.GetAttributeValue("content", null);
        if (string.IsNullOrWhiteSpace(ogTitle)) return null;

        var ogImage = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image' or @name='twitter:image']")?.GetAttributeValue("content", null);
        var ogPrice = doc.DocumentNode.SelectSingleNode("//meta[@property='product:price:amount' or @property='og:price:amount']")?.GetAttributeValue("content", null);
        var ogSiteName = doc.DocumentNode.SelectSingleNode("//meta[@property='og:site_name']")?.GetAttributeValue("content", null);

        var price = PriceUtils.ParsePrice(ogPrice);
        if (price <= 0m) price = PriceUtils.ExtractPriceFromDocument(doc);
        if (price <= 0m) price = 199.90m;

        return new Product
        {
            Id = UrlUtils.GenerateEntityId("ext", url),
            Domain = "fashion",
            Title = UrlUtils.CleanText(ogTitle),
            Brand = !string.IsNullOrWhiteSpace(ogSiteName) ? ogSiteName : brandDefault,
            Price = price,
            Url = url,
            Images = !string.IsNullOrEmpty(ogImage)
                ? new List<string> { ogImage }
                : new List<string> { "https://images.unsplash.com/photo-1523381210434-271e8be1f52b?auto=format&fit=crop&w=800&q=80" },
            Category = CategoryUtils.InferCategory(ogTitle),
            AvailableSizes = new List<string> { "Consultar Loja" },
            Tags = new List<string> { "streetwear", brandDefault.ToLowerInvariant() },
            Source = new Uri(url).Host,
            CreatedAt = DateTime.UtcNow
        };
    }
}
