using System.Globalization;
using System.Xml.Linq;
using UrubuApi.Models;
using UrubuApi.Services.Crawlers.Http;

namespace UrubuApi.Services.Crawlers.Extractors;

public class ExtratorSitemap
{
    private readonly IClienteHttpColetor _clienteHttp;

    public ExtratorSitemap(IClienteHttpColetor clienteHttp)
    {
        _clienteHttp = clienteHttp;
    }

    public async Task<IReadOnlyList<Product>> ExtrairDoSitemapAsync(string origem, string marcaPadrao, CancellationToken ct = default)
    {
        var produtos = new List<Product>();
        string[] caminhos = { "/sitemap_products_1.xml", "/sitemap.xml", "/sitemap_index.xml" };

        foreach (var caminho in caminhos)
        {
            try
            {
                var xml = await _clienteHttp.ObterXmlAsync($"{origem}{caminho}", ct);
                if (string.IsNullOrWhiteSpace(xml)) continue;

                var xdoc = XDocument.Parse(xml);
                XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
                XNamespace imgNs = "http://www.google.com/schemas/sitemap-image/1.1";

                var urls = xdoc.Descendants(ns + "url").ToList();
                if (urls.Count == 0) continue;

                foreach (var urlEl in urls.Take(120))
                {
                    var loc = urlEl.Element(ns + "loc")?.Value;
                    if (string.IsNullOrEmpty(loc) || !UrlUtils.EhUrlDeProdutoEspecifico(loc)) continue;

                    var imageEl = urlEl.Element(imgNs + "image");
                    var imgLoc = imageEl?.Element(imgNs + "loc")?.Value;
                    var imgTitle = imageEl?.Element(imgNs + "title")?.Value;

                    var slug = loc.Split('/').LastOrDefault()?.Replace("-", " ") ?? "Produto";
                    var titulo = !string.IsNullOrWhiteSpace(imgTitle)
                        ? imgTitle
                        : CultureInfo.CurrentCulture.TextInfo.ToTitleCase(slug);

                    var cleanProdUrl = UrlUtils.LimparUrlLoja(loc);
                    produtos.Add(new Product
                    {
                        Id = UrlUtils.GerarIdEntidade("ext", cleanProdUrl),
                        Domain = "fashion",
                        Title = UrlUtils.LimparTexto(titulo),
                        Brand = marcaPadrao,
                        Price = 249.90m,
                        Url = cleanProdUrl,
                        Images = !string.IsNullOrEmpty(imgLoc)
                            ? new List<string> { imgLoc }
                            : new List<string> { "https://images.unsplash.com/photo-1523381210434-271e8be1f52b?auto=format&fit=crop&w=800&q=80" },
                        Category = CategoriaUtils.InferirCategoria(titulo),
                        AvailableSizes = new List<string> { "M", "G" },
                        Tags = new List<string> { "streetwear", marcaPadrao.ToLowerInvariant() },
                        Source = new Uri(origem).Host,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                if (produtos.Count > 0) return produtos;
            }
            catch
            {
                // Tenta próximo caminho
            }
        }

        return produtos;
    }
}
