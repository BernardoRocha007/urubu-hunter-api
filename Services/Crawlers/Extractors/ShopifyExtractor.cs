using System.Globalization;
using System.Text.Json;
using UrubuApi.Models;
using UrubuApi.Services.Crawlers.Http;

namespace UrubuApi.Services.Crawlers.Extractors;

public class ShopifyExtractor
{
    private readonly ICrawlerHttpClient _httpClient;

    public ShopifyExtractor(ICrawlerHttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<Product>> ExtractStoreCatalogAsync(string origin, string brandDefault, CancellationToken ct = default)
    {
        var products = new List<Product>();

        for (var page = 1; page <= 5; page++)
        {
            var jsonUrl = $"{origin}/products.json?limit=250&page={page}";
            using var doc = await _httpClient.GetJsonAsync(jsonUrl, ct);
            if (doc == null) break;

            if (!doc.RootElement.TryGetProperty("products", out var productsArray) ||
                productsArray.ValueKind != JsonValueKind.Array ||
                productsArray.GetArrayLength() == 0)
            {
                break;
            }

            foreach (var item in productsArray.EnumerateArray())
            {
                var prod = ParseShopifyJsonProduct(item, origin, brandDefault);
                if (prod != null) products.Add(prod);
            }

            if (productsArray.GetArrayLength() < 250) break;
        }

        return products;
    }

    public async Task<Product?> ExtractProductJsonAsync(string cleanUrl, string brandDefault, CancellationToken ct = default)
    {
        var jsonUrl = $"{cleanUrl}.json";
        using var doc = await _httpClient.GetJsonAsync(jsonUrl, ct);
        if (doc == null) return null;

        if (doc.RootElement.TryGetProperty("product", out var p))
        {
            var uri = new Uri(cleanUrl);
            var origin = $"{uri.Scheme}://{uri.Authority}";
            return ParseShopifyJsonProduct(p, origin, brandDefault);
        }

        return null;
    }

    private static Product? ParseShopifyJsonProduct(JsonElement p, string origin, string brandDefault)
    {
        try
        {
            var title = p.GetProperty("title").GetString() ?? string.Empty;
            var handle = p.GetProperty("handle").GetString() ?? string.Empty;
            var productType = p.TryGetProperty("product_type", out var pt) ? pt.GetString() : string.Empty;
            var vendor = p.TryGetProperty("vendor", out var v) && !string.IsNullOrWhiteSpace(v.GetString())
                ? v.GetString()!
                : brandDefault;

            var tags = new List<string>();
            if (p.TryGetProperty("tags", out var tg))
            {
                if (tg.ValueKind == JsonValueKind.String)
                {
                    tags = tg.GetString()?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList() ?? new();
                }
                else if (tg.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tag in tg.EnumerateArray())
                    {
                        var t = tag.GetString();
                        if (!string.IsNullOrEmpty(t)) tags.Add(t);
                    }
                }
            }

            var images = new List<string>();
            if (p.TryGetProperty("images", out var imgs) && imgs.ValueKind == JsonValueKind.Array)
            {
                foreach (var img in imgs.EnumerateArray())
                {
                    var src = img.TryGetProperty("src", out var s) ? s.GetString() : null;
                    if (!string.IsNullOrEmpty(src)) images.Add(src);
                }
            }

            decimal price = 0m;
            decimal? compareAtPrice = null;
            var availableSizes = new HashSet<string>();

            if (p.TryGetProperty("variants", out var variants) && variants.ValueKind == JsonValueKind.Array)
            {
                foreach (var variant in variants.EnumerateArray())
                {
                    if (price == 0m && variant.TryGetProperty("price", out var pr) && decimal.TryParse(pr.GetString(), CultureInfo.InvariantCulture, out var parsedPrice))
                    {
                        price = parsedPrice;
                    }

                    if (!compareAtPrice.HasValue && variant.TryGetProperty("compare_at_price", out var cmp) && cmp.ValueKind == JsonValueKind.String && decimal.TryParse(cmp.GetString(), CultureInfo.InvariantCulture, out var parsedCompare))
                    {
                        compareAtPrice = parsedCompare;
                    }

                    var isAvailable = !variant.TryGetProperty("available", out var av) || av.GetBoolean();
                    var titleSize = variant.TryGetProperty("title", out var vt) ? vt.GetString() : null;
                    if (isAvailable && !string.IsNullOrEmpty(titleSize) && !titleSize.Equals("Default Title", StringComparison.OrdinalIgnoreCase))
                    {
                        availableSizes.Add(titleSize.Trim());
                    }
                }
            }

            if (price <= 0m) return null;

            int? discountPct = null;
            if (compareAtPrice.HasValue && compareAtPrice.Value > price)
            {
                discountPct = (int)Math.Round((1m - (price / compareAtPrice.Value)) * 100m);
            }

            var cleanUrl = $"{origin}/products/{handle}";
            var category = CategoryUtils.InferCategory(title, tags, productType);

            return new Product
            {
                Id = UrlUtils.GenerateEntityId("ext", cleanUrl),
                Domain = "fashion",
                Title = UrlUtils.CleanText(title),
                Brand = vendor,
                Price = price,
                OriginalPrice = compareAtPrice,
                DiscountPct = discountPct,
                Url = cleanUrl,
                Images = images.Count > 0 ? images : new List<string> { "https://images.unsplash.com/photo-1523381210434-271e8be1f52b?auto=format&fit=crop&w=800&q=80" },
                Category = category,
                AvailableSizes = availableSizes.ToList(),
                Tags = tags,
                Source = new Uri(origin).Host,
                CreatedAt = DateTime.UtcNow
            };
        }
        catch
        {
            return null;
        }
    }
}
