using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace UrubuApi.Services.Crawlers;

public static class UrlUtils
{
    private static readonly HashSet<string> ParametrosRastreamento = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content",
        "srsltid", "gclid", "fbclid", "igshid", "_ga", "mc_cid", "mc_eid"
    };

    public static string LimparUrlLoja(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var trimmed = raw.Trim();
        var comProtocolo = trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                           trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"https://{trimmed}";

        try
        {
            var uri = new Uri(comProtocolo);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

            foreach (var key in query.AllKeys.Where(k => k != null && ParametrosRastreamento.Contains(k)).ToList())
            {
                query.Remove(key);
            }

            var cleanQuery = query.ToString();
            var queryString = string.IsNullOrEmpty(cleanQuery) ? string.Empty : $"?{cleanQuery}";

            var origin = $"{uri.Scheme}://{uri.Authority}";
            var path = uri.AbsolutePath.TrimEnd('/');

            if (string.IsNullOrEmpty(path) && string.IsNullOrEmpty(queryString))
            {
                return origin;
            }

            return $"{origin}{path}{queryString}";
        }
        catch
        {
            return trimmed.Split('?')[0].TrimEnd('/');
        }
    }

    public static string ObterOrigemLoja(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var trimmed = raw.Trim();
        var comProtocolo = trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                           trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"https://{trimmed}";

        try
        {
            var uri = new Uri(comProtocolo);
            return $"{uri.Scheme}://{uri.Authority}";
        }
        catch
        {
            return LimparUrlLoja(raw);
        }
    }

    public static string ExtrairMarcaDaUrl(string url)
    {
        try
        {
            var uri = new Uri(url.StartsWith("http") ? url : $"https://{url}");
            var host = uri.Host.Replace("www.", "");
            var parts = host.Split('.');
            var brand = parts[0];
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(brand);
        }
        catch
        {
            return "Marca";
        }
    }

    public static string LimparTexto(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return WebUtility.HtmlDecode(raw).Trim();
    }

    public static string GerarIdEntidade(string prefixo, string valor)
    {
        var bytes = Encoding.UTF8.GetBytes(valor.Trim());
        var hash = SHA256.HashData(bytes);
        var hex = Convert.ToHexString(hash).ToLowerInvariant()[..16];
        return $"{prefixo}-{hex}";
    }

    public static bool EhUrlDeProdutoEspecifico(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        try
        {
            var comProtocolo = raw.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? raw : $"https://{raw}";
            var uri = new Uri(comProtocolo);
            var path = uri.AbsolutePath.TrimEnd('/');

            if (string.IsNullOrEmpty(path)) return false;
            if (Regex.IsMatch(path, @"^/(produtos?|products?|shop|loja|catalog|colecoes|collections?)$", RegexOptions.IgnoreCase)) return false;

            // 1. Padrões de produto: /products/slug, /produtos/slug, /item/..., /p/...
            if (Regex.IsMatch(path, @"/(produtos?|products?|produto|item|p|dp)/[^/]+", RegexOptions.IgnoreCase)) return true;

            // 2. Mercado Livre
            if (Regex.IsMatch(path, @"(MLB-?\d+)", RegexOptions.IgnoreCase)) return true;

            // 3. URLs com slug de id ou html
            if (Regex.IsMatch(path, @"-[pP]-\d+$", RegexOptions.IgnoreCase)) return true;
            if (Regex.IsMatch(path, @"\.(html?|php)$", RegexOptions.IgnoreCase) && path.Count(c => c == '/') >= 2) return true;

            return false;
        }
        catch
        {
            return false;
        }
    }
}
