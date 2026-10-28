using System.Text.Json;

namespace UrubuApi.Services.Crawlers.Http;

public class CrawlerHttpClient : ICrawlerHttpClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<CrawlerHttpClient> _logger;

    public CrawlerHttpClient(IHttpClientFactory httpClientFactory, ILogger<CrawlerHttpClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string?> GetHtmlAsync(string url, CancellationToken ct = default)
    {
        try
        {
            var client = CreateClient();
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao obter HTML de {Url}", url);
            return null;
        }
    }

    public async Task<string?> GetXmlAsync(string url, CancellationToken ct = default)
    {
        try
        {
            var client = CreateClient();
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            return await response.Content.ReadAsStringAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao obter XML de {Url}", url);
            return null;
        }
    }

    public async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct = default)
    {
        try
        {
            var client = CreateClient();
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
            if (!contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Falha ao obter ou parsear JSON de {Url}", url);
            return null;
        }
    }

    private HttpClient CreateClient()
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,application/json,*/*;q=0.8");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("pt-BR,pt;q=0.9,en-US;q=0.8,en;q=0.7");
        client.Timeout = TimeSpan.FromSeconds(8);
        return client;
    }
}
