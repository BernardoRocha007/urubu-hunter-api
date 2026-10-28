using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace UrubuApi.Services.Crawlers;

public static class UrlUtils
{
    private static readonly HashSet<string> TrackingParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content",
        "srsltid", "gclid", "fbclid", "igshid", "_ga", "mc_cid", "mc_eid"
    };

    public static string CleanStoreUrl(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var trimmed = raw.Trim();
        var withProtocol = trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                           trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"https://{trimmed}";

        try
        {
            var uri = new Uri(withProtocol);
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

            foreach (var key in query.AllKeys.Where(k => k != null && TrackingParams.Contains(k)).ToList())
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

    public static string GetStoreOrigin(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var trimmed = raw.Trim();
        var withProtocol = trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                           trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"https://{trimmed}";

        try
        {
            var uri = new Uri(withProtocol);
            return $"{uri.Scheme}://{uri.Authority}";
        }
        catch
        {
            return CleanStoreUrl(raw);
        }
    }

    public static string ExtractBrandFromUrl(string url)
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

    public static string CleanText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return WebUtility.HtmlDecode(raw).Trim();
    }

    public static string GenerateEntityId(string prefix, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value.Trim());
        var hash = SHA256.HashData(bytes);
        var hex = Convert.ToHexString(hash).ToLowerInvariant()[..16];
        return $"{prefix}-{hex}";
    }

    public static bool IsSpecificProductUrl(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return false;
        try
        {
            var withProtocol = raw.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? raw : $"https://{raw}";
            var uri = new Uri(withProtocol);
            var path = uri.AbsolutePath.TrimEnd('/');

            if (string.IsNullOrEmpty(path)) return false;
            if (Regex.IsMatch(path, @"^/(produtos?|products?|shop|loja|catalog|colecoes|collections?)$", RegexOptions.IgnoreCase)) return false;

            // 1. Padrões padrão de produto em lojas modernas: /products/slug, /produtos/slug, /item/..., /p/...
            if (Regex.IsMatch(path, @"/(produtos?|products?|produto|item|p|dp)/[^/]+", RegexOptions.IgnoreCase)) return true;

            // 2. Mercado Livre
            if (Regex.IsMatch(path, @"(MLB-?\d+)", RegexOptions.IgnoreCase)) return true;

            // 3. URLs terminadas em id numérico ou html
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
