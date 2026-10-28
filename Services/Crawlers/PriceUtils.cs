using System.Globalization;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace UrubuApi.Services.Crawlers;

public static class PriceUtils
{
    public static decimal ParsePrice(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0m;

        // 1. Padrão brasileiro: R$ 199,90 ou R$ 1.299,00
        var brMatch = Regex.Match(text, @"R?\$?\s*(\d{1,3}(?:\.\d{3})*,\d{2})");
        if (brMatch.Success)
        {
            var raw = brMatch.Groups[1].Value.Replace(".", "").Replace(",", ".");
            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            {
                return val;
            }
        }

        // 2. Padrão internacional com ponto: 199.90 ou 1,299.90
        var usMatch = Regex.Match(text, @"\b(\d+(?:,\d{3})*\.\d{2})\b");
        if (usMatch.Success)
        {
            var raw = usMatch.Groups[1].Value.Replace(",", "");
            if (decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var val))
            {
                return val;
            }
        }

        // 3. Fallback dígitos simples
        var digitsMatch = Regex.Match(text, @"\b(\d{2,6})\b");
        if (digitsMatch.Success && decimal.TryParse(digitsMatch.Groups[1].Value, NumberStyles.Any, CultureInfo.InvariantCulture, out var intVal))
        {
            return intVal;
        }

        return 0m;
    }

    public static decimal ExtractPriceFromDocument(HtmlDocument doc)
    {
        var priceNodes = doc.DocumentNode.SelectNodes("//*[contains(text(), 'R$')]");
        if (priceNodes != null)
        {
            foreach (var node in priceNodes)
            {
                var price = ParsePrice(node.InnerText);
                if (price > 0m) return price;
            }
        }
        return 0m;
    }
}
