using System.Globalization;
using System.Text.Json;
using HtmlAgilityPack;
using UrubuApi.Models;

namespace UrubuApi.Services.Crawlers.Extractors;

public class JsonLdExtractor
{
    public Product? ExtractProduct(HtmlDocument doc, string url, string brandDefault)
    {
        var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scripts == null) return null;

        var origin = UrlUtils.GetStoreOrigin(url);
        foreach (var s in scripts)
        {
            var json = s.InnerText?.Trim();
            if (string.IsNullOrEmpty(json)) continue;

            try
            {
                using var jdoc = JsonDocument.Parse(json);
                if (jdoc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var prod = ParseJsonLdProduct(jdoc.RootElement, origin, brandDefault);
                    if (prod != null) return prod;
                }
            }
            catch { }
        }

        return null;
    }

    public IReadOnlyList<Product> ExtractCatalog(HtmlDocument doc, string origin, string brandDefault)
    {
        var products = new List<Product>();
        var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scripts == null) return products;

        foreach (var script in scripts)
        {
            var content = script.InnerText?.Trim();
            if (string.IsNullOrEmpty(content)) continue;

            try
            {
                using var jdoc = JsonDocument.Parse(content);
                if (jdoc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    ExtractFromElement(jdoc.RootElement, products, origin, brandDefault);
                }
                else if (jdoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in jdoc.RootElement.EnumerateArray())
                    {
                        ExtractFromElement(el, products, origin, brandDefault);
                    }
                }
            }
            catch { }
        }

        return products;
    }

    private void ExtractFromElement(JsonElement el, List<Product> products, string origin, string brandDefault)
    {
        if (!el.TryGetProperty("@type", out var typeProp)) return;
        var type = typeProp.GetString() ?? string.Empty;

        if (type.Equals("Product", StringComparison.OrdinalIgnoreCase))
        {
            var prod = ParseJsonLdProduct(el, origin, brandDefault);
            if (prod != null) products.Add(prod);
        }
        else if (type.Equals("ItemList", StringComparison.OrdinalIgnoreCase) &&
                 el.TryGetProperty("itemListElement", out var items) &&
                 items.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in items.EnumerateArray())
            {
                if (item.TryGetProperty("item", out var nestedProd))
                {
                    var prod = ParseJsonLdProduct(nestedProd, origin, brandDefault);
                    if (prod != null) products.Add(prod);
                }
                else
                {
                    var prod = ParseJsonLdProduct(item, origin, brandDefault);
                    if (prod != null) products.Add(prod);
                }
            }
        }
    }

    private static Product? ParseJsonLdProduct(JsonElement el, string origin, string brandDefault)
    {
        try
        {
            var title = el.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(title)) return null;

            var url = el.TryGetProperty("url", out var u) ? u.GetString() : null;
            if (string.IsNullOrWhiteSpace(url)) return null;

            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                url = $"{origin.TrimEnd('/')}/{url.TrimStart('/')}";
            }

            var cleanUrl = UrlUtils.CleanStoreUrl(url);

            decimal price = 0m;
            if (el.TryGetProperty("offers", out var offers))
            {
                if (offers.ValueKind == JsonValueKind.Object && offers.TryGetProperty("price", out var p))
                {
                    if (p.ValueKind == JsonValueKind.Number) price = p.GetDecimal();
                    else if (p.ValueKind == JsonValueKind.String) decimal.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out price);
                }
                else if (offers.ValueKind == JsonValueKind.Array)
                {
                    foreach (var o in offers.EnumerateArray())
                    {
                        if (o.TryGetProperty("price", out var op))
                        {
                            if (op.ValueKind == JsonValueKind.Number) { price = op.GetDecimal(); break; }
                            else if (op.ValueKind == JsonValueKind.String && decimal.TryParse(op.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out price)) break;
                        }
                    }
                }
            }

            if (price <= 0m) price = 199.90m;

            var images = new List<string>();
            if (el.TryGetProperty("image", out var img))
            {
                if (img.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(img.GetString()))
                {
                    images.Add(img.GetString()!);
                }
                else if (img.ValueKind == JsonValueKind.Array)
                {
                    foreach (var i in img.EnumerateArray())
                    {
                        if (i.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(i.GetString()))
                        {
                            images.Add(i.GetString()!);
                        }
                    }
                }
            }

            var brand = brandDefault;
            if (el.TryGetProperty("brand", out var b))
            {
                if (b.ValueKind == JsonValueKind.Object && b.TryGetProperty("name", out var bn))
                {
                    brand = bn.GetString() ?? brandDefault;
                }
                else if (b.ValueKind == JsonValueKind.String)
                {
                    brand = b.GetString() ?? brandDefault;
                }
            }

            return new Product
            {
                Id = UrlUtils.GenerateEntityId("ext", cleanUrl),
                Domain = "fashion",
                Title = UrlUtils.CleanText(title),
                Brand = brand,
                Price = price,
                Url = cleanUrl,
                Images = images.Count > 0 ? images : new List<string> { "https://images.unsplash.com/photo-1523381210434-271e8be1f52b?auto=format&fit=crop&w=800&q=80" },
                Category = CategoryUtils.InferCategory(title),
                AvailableSizes = new List<string> { "Consultar Loja" },
                Tags = new List<string> { "streetwear", brand.ToLowerInvariant() },
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
