using HtmlAgilityPack;
using UrubuApi.Models;

namespace UrubuApi.Services.Crawlers.Extractors;

public class ExtratorOpenGraph
{
    public Product? ExtrairProduto(HtmlDocument doc, string url, string marcaPadrao)
    {
        var ogTitle = doc.DocumentNode.SelectSingleNode("//meta[@property='og:title' or @name='twitter:title']")?.GetAttributeValue("content", null);
        if (string.IsNullOrWhiteSpace(ogTitle)) return null;

        var ogImage = doc.DocumentNode.SelectSingleNode("//meta[@property='og:image' or @name='twitter:image']")?.GetAttributeValue("content", null);
        var ogPrice = doc.DocumentNode.SelectSingleNode("//meta[@property='product:price:amount' or @property='og:price:amount']")?.GetAttributeValue("content", null);
        var ogSiteName = doc.DocumentNode.SelectSingleNode("//meta[@property='og:site_name']")?.GetAttributeValue("content", null);

        var preco = PrecoUtils.ConverterPreco(ogPrice);
        if (preco <= 0m) preco = PrecoUtils.ExtrairPrecoDoDocumento(doc);
        if (preco <= 0m) preco = 199.90m;

        return new Product
        {
            Id = UrlUtils.GerarIdEntidade("ext", url),
            Domain = "fashion",
            Title = UrlUtils.LimparTexto(ogTitle),
            Brand = !string.IsNullOrWhiteSpace(ogSiteName) ? ogSiteName : marcaPadrao,
            Price = preco,
            Url = url,
            Images = !string.IsNullOrEmpty(ogImage)
                ? new List<string> { ogImage }
                : new List<string> { "https://images.unsplash.com/photo-1523381210434-271e8be1f52b?auto=format&fit=crop&w=800&q=80" },
            Category = CategoriaUtils.InferirCategoria(ogTitle),
            AvailableSizes = new List<string> { "Consultar Loja" },
            Tags = new List<string> { "streetwear", marcaPadrao.ToLowerInvariant() },
            Source = new Uri(url).Host,
            CreatedAt = DateTime.UtcNow
        };
    }
}
