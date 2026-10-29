using System.Text.RegularExpressions;
using HtmlAgilityPack;
using UrubuApi.Models;

namespace UrubuApi.Services.Crawlers.Extractors;

public class ExtratorCardsHtml
{
    public IReadOnlyList<Product> ExtrairCards(HtmlDocument doc, string origem, string marcaPadrao)
    {
        var produtos = new List<Product>();
        var baseUri = new Uri(origem);

        var cardNodes = doc.DocumentNode.SelectNodes(
            "//div[contains(@class, 'product-card') or contains(@class, 'product-item') or contains(@class, 'item-product') or contains(@class, 'product') or contains(@class, 'card-product') or contains(@data-product, '')] | " +
            "//li[contains(@class, 'product') or contains(@class, 'item')] | " +
            "//article"
        );

        if (cardNodes == null) return produtos;

        var urlsVistas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var card in cardNodes.Take(80))
        {
            var linkNode = card.SelectSingleNode(".//a[@href and not(contains(@href, 'javascript:')) and not(contains(@href, '#'))]");
            var href = linkNode?.GetAttributeValue("href", string.Empty);
            if (string.IsNullOrWhiteSpace(href)) continue;

            string urlCompleta;
            try
            {
                urlCompleta = new Uri(baseUri, href).AbsoluteUri;
            }
            catch
            {
                continue;
            }

            var cleanProdUrl = UrlUtils.LimparUrlLoja(urlCompleta);
            if (urlsVistas.Contains(cleanProdUrl)) continue;

            // Ignora links que não são produtos
            if (Regex.IsMatch(cleanProdUrl, @"/(carrinho|cart|checkout|login|conta|account|search)", RegexOptions.IgnoreCase))
            {
                continue;
            }

            var titleNode = card.SelectSingleNode(".//h2 | .//h3 | .//h4 | .//*[contains(@class, 'title') or contains(@class, 'name')] | .//a[@title]");
            var titulo = titleNode?.GetAttributeValue("title", null) ?? titleNode?.InnerText?.Trim();
            if (string.IsNullOrWhiteSpace(titulo) || titulo.Length < 3) continue;

            var imgNode = card.SelectSingleNode(".//img[@src or @data-src or @data-lazy or @srcset]");
            var imgSrc = imgNode?.GetAttributeValue("src", null) ??
                         imgNode?.GetAttributeValue("data-src", null) ??
                         imgNode?.GetAttributeValue("data-lazy", null);

            if (!string.IsNullOrEmpty(imgSrc) && !imgSrc.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    imgSrc = new Uri(baseUri, imgSrc).AbsoluteUri;
                }
                catch { }
            }

            var precoTexto = card.SelectSingleNode(".//*[contains(@class, 'price') or contains(@class, 'preco') or contains(text(), 'R$')]")?.InnerText;
            var preco = PrecoUtils.ConverterPreco(precoTexto);
            if (preco <= 0m) preco = 189.90m;

            urlsVistas.Add(cleanProdUrl);
            produtos.Add(new Product
            {
                Id = UrlUtils.GerarIdEntidade("ext", cleanProdUrl),
                Domain = "fashion",
                Title = UrlUtils.LimparTexto(titulo),
                Brand = marcaPadrao,
                Price = preco,
                Url = cleanProdUrl,
                Images = !string.IsNullOrEmpty(imgSrc)
                    ? new List<string> { imgSrc }
                    : new List<string> { "https://images.unsplash.com/photo-1523381210434-271e8be1f52b?auto=format&fit=crop&w=800&q=80" },
                Category = CategoriaUtils.InferirCategoria(titulo),
                AvailableSizes = new List<string> { "Consultar Loja" },
                Tags = new List<string> { "streetwear", marcaPadrao.ToLowerInvariant() },
                Source = baseUri.Host,
                CreatedAt = DateTime.UtcNow
            });
        }

        return produtos;
    }
}
