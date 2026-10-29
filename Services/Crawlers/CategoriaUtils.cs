using System.Text.RegularExpressions;

namespace UrubuApi.Services.Crawlers;

public static class CategoriaUtils
{
    private static readonly (string Categoria, string Padrao)[] RegrasCategoria = new[]
    {
        ("Calças", @"\b(cal[çc]as?|pants?|tailored\s*pants|alfaiataria|cargo\s*pants|sweatpants|trousers?|jeans)\b"),
        ("Jaquetas & Casacos", @"\b(jaquetas?|jackets?|casacos?|coats?|coach|bomber|puffer|fleece|windbreaker|corta\s*vento|biker|blazer|anorak|parka|sobretudo)\b"),
        ("Moletons & Blusas", @"\b(moletons?|moletom|hoodies?|sweaters?|crewneck|sueter|suéter|tric[oôó]|tricot|cardigan|pullover)\b"),
        ("Bermudas & Shorts", @"\b(bermudas?|shorts?|swim\s*shorts|boardshorts?)\b"),
        ("Bonés & Gorros", @"\b(bon[eé]s?|hats?|caps?|strapback|snapback|beanie|gorros?|bucket|5\s*panel|6\s*panel|dad\s*hat)\b"),
        ("Acessórios", @"\b(bags?|bolsas?|shoulder\s*bag|tote|mala|cordura\s*bag|cintos?|belts?|meias?|socks?|carteiras?|wallets?|chaveiro|backpack|mochila)\b"),
        ("Tênis", @"\b(t[eê]nis|sneakers?|shoes?|sapatos?|botas?|boots?|dunk|jordan|slip\s*on)\b"),
        ("Camisetas & Polos", @"\b(camisetas?|tees?|t-shirts?|t\s*shirt|polos?|camisas?|regatas?|tanks?|baby\s*look|long\s*sleeve|manga\s*longa)\b")
    };

    public static string InferirCategoria(string titulo, IEnumerable<string>? tags = null, string? categoriaOriginal = null)
    {
        if (!string.IsNullOrWhiteSpace(categoriaOriginal) &&
            !categoriaOriginal.Equals("Geral", StringComparison.OrdinalIgnoreCase) &&
            !categoriaOriginal.Equals("Streetwear", StringComparison.OrdinalIgnoreCase) &&
            !categoriaOriginal.Equals("Vestuário", StringComparison.OrdinalIgnoreCase))
        {
            return categoriaOriginal.Trim();
        }

        var texto = $"{titulo} {(tags != null ? string.Join(" ", tags) : string.Empty)}".ToLowerInvariant();

        foreach (var (categoria, padrao) in RegrasCategoria)
        {
            if (Regex.IsMatch(texto, padrao, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return categoria;
            }
        }

        return "Vestuário";
    }
}
