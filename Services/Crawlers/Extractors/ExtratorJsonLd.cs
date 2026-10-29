using System.Globalization;
using System.Text.Json;
using HtmlAgilityPack;
using UrubuApi.Models;

namespace UrubuApi.Services.Crawlers.Extractors;

public class ExtratorJsonLd
{
    public Product? ExtrairProduto(HtmlDocument doc, string url, string marcaPadrao)
    {
        var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scripts == null) return null;

        var origem = UrlUtils.ObterOrigemLoja(url);
        foreach (var s in scripts)
        {
            var json = s.InnerText?.Trim();
            if (string.IsNullOrEmpty(json)) continue;

            try
            {
                using var jdoc = JsonDocument.Parse(json);
                if (jdoc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    var prod = ConverterProdutoJsonLd(jdoc.RootElement, origem, marcaPadrao);
                    if (prod != null) return prod;
                }
            }
            catch { }
        }

        return null;
    }

    public IReadOnlyList<Product> ExtrairCatalogo(HtmlDocument doc, string origem, string marcaPadrao)
    {
        var produtos = new List<Product>();
        var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scripts == null) return produtos;

        foreach (var script in scripts)
        {
            var conteudo = script.InnerText?.Trim();
            if (string.IsNullOrEmpty(conteudo)) continue;

            try
            {
                using var jdoc = JsonDocument.Parse(conteudo);
                if (jdoc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    ExtrairDeElemento(jdoc.RootElement, produtos, origem, marcaPadrao);
                }
                else if (jdoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in jdoc.RootElement.EnumerateArray())
                    {
                        ExtrairDeElemento(el, produtos, origem, marcaPadrao);
                    }
                }
            }
            catch { }
        }

        return produtos;
    }

    private void ExtrairDeElemento(JsonElement el, List<Product> produtos, string origem, string marcaPadrao)
    {
        if (!el.TryGetProperty("@type", out var typeProp)) return;
        var tipo = typeProp.GetString() ?? string.Empty;

        if (tipo.Equals("Product", StringComparison.OrdinalIgnoreCase))
        {
            var prod = ConverterProdutoJsonLd(el, origem, marcaPadrao);
            if (prod != null) produtos.Add(prod);
        }
        else if (tipo.Equals("ItemList", StringComparison.OrdinalIgnoreCase) &&
                 el.TryGetProperty("itemListElement", out var itens) &&
                 itens.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in itens.EnumerateArray())
            {
                if (item.TryGetProperty("item", out var nestedProd))
                {
                    var prod = ConverterProdutoJsonLd(nestedProd, origem, marcaPadrao);
                    if (prod != null) produtos.Add(prod);
                }
                else
                {
                    var prod = ConverterProdutoJsonLd(item, origem, marcaPadrao);
                    if (prod != null) produtos.Add(prod);
                }
            }
        }
    }

    private static Product? ConverterProdutoJsonLd(JsonElement el, string origem, string marcaPadrao)
    {
        try
        {
            var titulo = el.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(titulo)) return null;

            var url = el.TryGetProperty("url", out var u) ? u.GetString() : null;
            if (string.IsNullOrWhiteSpace(url)) return null;

            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                url = $"{origem.TrimEnd('/')}/{url.TrimStart('/')}";
            }

            var urlLimpa = UrlUtils.LimparUrlLoja(url);

            decimal preco = 0m;
            if (el.TryGetProperty("offers", out var ofertas))
            {
                if (ofertas.ValueKind == JsonValueKind.Object && ofertas.TryGetProperty("price", out var p))
                {
                    if (p.ValueKind == JsonValueKind.Number) preco = p.GetDecimal();
                    else if (p.ValueKind == JsonValueKind.String) decimal.TryParse(p.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out preco);
                }
                else if (ofertas.ValueKind == JsonValueKind.Array)
                {
                    foreach (var o in ofertas.EnumerateArray())
                    {
                        if (o.TryGetProperty("price", out var op))
                        {
                            if (op.ValueKind == JsonValueKind.Number) { preco = op.GetDecimal(); break; }
                            else if (op.ValueKind == JsonValueKind.String && decimal.TryParse(op.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out preco)) break;
                        }
                    }
                }
            }

            if (preco <= 0m) preco = 199.90m;

            var imagens = new List<string>();
            if (el.TryGetProperty("image", out var img))
            {
                if (img.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(img.GetString()))
                {
                    imagens.Add(img.GetString()!);
                }
                else if (img.ValueKind == JsonValueKind.Array)
                {
                    foreach (var i in img.EnumerateArray())
                    {
                        if (i.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(i.GetString()))
                        {
                            imagens.Add(i.GetString()!);
                        }
                    }
                }
            }

            var marca = marcaPadrao;
            if (el.TryGetProperty("brand", out var b))
            {
                if (b.ValueKind == JsonValueKind.Object && b.TryGetProperty("name", out var bn))
                {
                    marca = bn.GetString() ?? marcaPadrao;
                }
                else if (b.ValueKind == JsonValueKind.String)
                {
                    marca = b.GetString() ?? marcaPadrao;
                }
            }

            return new Product
            {
                Id = UrlUtils.GerarIdEntidade("ext", urlLimpa),
                Domain = "fashion",
                Title = UrlUtils.LimparTexto(titulo),
                Brand = marca,
                Price = preco,
                Url = urlLimpa,
                Images = imagens.Count > 0 ? imagens : new List<string> { "https://images.unsplash.com/photo-1523381210434-271e8be1f52b?auto=format&fit=crop&w=800&q=80" },
                Category = CategoriaUtils.InferirCategoria(titulo),
                AvailableSizes = new List<string> { "Consultar Loja" },
                Tags = new List<string> { "streetwear", marca.ToLowerInvariant() },
                Source = new Uri(origem).Host,
                CreatedAt = DateTime.UtcNow
            };
        }
        catch
        {
            return null;
        }
    }
}
