using System.Globalization;
using System.Text.Json;
using UrubuApi.Models;
using UrubuApi.Services.Crawlers.Http;

namespace UrubuApi.Services.Crawlers.Extractors;

public class ExtratorShopify
{
    private readonly IClienteHttpColetor _clienteHttp;

    public ExtratorShopify(IClienteHttpColetor clienteHttp)
    {
        _clienteHttp = clienteHttp;
    }

    public async Task<IReadOnlyList<Product>> ExtrairCatalogoLojaAsync(string origem, string marcaPadrao, CancellationToken ct = default)
    {
        var produtos = new List<Product>();

        for (var pagina = 1; pagina <= 5; pagina++)
        {
            var urlJson = $"{origem}/products.json?limit=250&page={pagina}";
            using var doc = await _clienteHttp.ObterJsonAsync(urlJson, ct);
            if (doc == null) break;

            if (!doc.RootElement.TryGetProperty("products", out var arrayProdutos) ||
                arrayProdutos.ValueKind != JsonValueKind.Array ||
                arrayProdutos.GetArrayLength() == 0)
            {
                break;
            }

            foreach (var item in arrayProdutos.EnumerateArray())
            {
                var prod = ConverterProdutoJsonShopify(item, origem, marcaPadrao);
                if (prod != null) produtos.Add(prod);
            }

            if (arrayProdutos.GetArrayLength() < 250) break;
        }

        return produtos;
    }

    public async Task<Product?> ExtrairProdutoJsonAsync(string urlLimpa, string marcaPadrao, CancellationToken ct = default)
    {
        var urlJson = $"{urlLimpa}.json";
        using var doc = await _clienteHttp.ObterJsonAsync(urlJson, ct);
        if (doc == null) return null;

        if (doc.RootElement.TryGetProperty("product", out var p))
        {
            var uri = new Uri(urlLimpa);
            var origem = $"{uri.Scheme}://{uri.Authority}";
            return ConverterProdutoJsonShopify(p, origem, marcaPadrao);
        }

        return null;
    }

    private static Product? ConverterProdutoJsonShopify(JsonElement p, string origem, string marcaPadrao)
    {
        try
        {
            var titulo = p.GetProperty("title").GetString() ?? string.Empty;
            var handle = p.GetProperty("handle").GetString() ?? string.Empty;
            var tipoProduto = p.TryGetProperty("product_type", out var pt) ? pt.GetString() : string.Empty;
            var vendedor = p.TryGetProperty("vendor", out var v) && !string.IsNullOrWhiteSpace(v.GetString())
                ? v.GetString()!
                : marcaPadrao;

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

            var imagens = new List<string>();
            if (p.TryGetProperty("images", out var imgs) && imgs.ValueKind == JsonValueKind.Array)
            {
                foreach (var img in imgs.EnumerateArray())
                {
                    var src = img.TryGetProperty("src", out var s) ? s.GetString() : null;
                    if (!string.IsNullOrEmpty(src)) imagens.Add(src);
                }
            }

            decimal preco = 0m;
            decimal? precoOriginal = null;
            var tamanhosDisponiveis = new HashSet<string>();

            if (p.TryGetProperty("variants", out var variantes) && variantes.ValueKind == JsonValueKind.Array)
            {
                foreach (var variante in variantes.EnumerateArray())
                {
                    if (preco == 0m && variante.TryGetProperty("price", out var pr) && decimal.TryParse(pr.GetString(), CultureInfo.InvariantCulture, out var precoConvertido))
                    {
                        preco = precoConvertido;
                    }

                    if (!precoOriginal.HasValue && variante.TryGetProperty("compare_at_price", out var cmp) && cmp.ValueKind == JsonValueKind.String && decimal.TryParse(cmp.GetString(), CultureInfo.InvariantCulture, out var comparativoConvertido))
                    {
                        precoOriginal = comparativoConvertido;
                    }

                    var disponivel = !variante.TryGetProperty("available", out var av) || av.GetBoolean();
                    var nomeTamanho = variante.TryGetProperty("title", out var vt) ? vt.GetString() : null;
                    if (disponivel && !string.IsNullOrEmpty(nomeTamanho) && !nomeTamanho.Equals("Default Title", StringComparison.OrdinalIgnoreCase))
                    {
                        tamanhosDisponiveis.Add(nomeTamanho.Trim());
                    }
                }
            }

            if (preco <= 0m) return null;

            int? percentualDesconto = null;
            if (precoOriginal.HasValue && precoOriginal.Value > preco)
            {
                percentualDesconto = (int)Math.Round((1m - (preco / precoOriginal.Value)) * 100m);
            }

            var urlProdutoLimpa = $"{origem}/products/{handle}";
            var categoria = CategoriaUtils.InferirCategoria(titulo, tags, tipoProduto);

            return new Product
            {
                Id = UrlUtils.GerarIdEntidade("ext", urlProdutoLimpa),
                Domain = "fashion",
                Title = UrlUtils.LimparTexto(titulo),
                Brand = vendedor,
                Price = preco,
                OriginalPrice = precoOriginal,
                DiscountPct = percentualDesconto,
                Url = urlProdutoLimpa,
                Images = imagens.Count > 0 ? imagens : new List<string> { "https://images.unsplash.com/photo-1523381210434-271e8be1f52b?auto=format&fit=crop&w=800&q=80" },
                Category = categoria,
                AvailableSizes = tamanhosDisponiveis.ToList(),
                Tags = tags,
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
