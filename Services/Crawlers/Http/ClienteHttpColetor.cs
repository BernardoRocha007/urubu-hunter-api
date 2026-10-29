using System.Text.Json;

namespace UrubuApi.Services.Crawlers.Http;

public class ClienteHttpColetor : IClienteHttpColetor
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ClienteHttpColetor> _logger;

    public ClienteHttpColetor(IHttpClientFactory httpClientFactory, ILogger<ClienteHttpColetor> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<string?> ObterHtmlAsync(string url, CancellationToken ct = default)
    {
        try
        {
            var client = CriarCliente();
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

    public async Task<string?> ObterXmlAsync(string url, CancellationToken ct = default)
    {
        try
        {
            var client = CriarCliente();
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

    public async Task<JsonDocument?> ObterJsonAsync(string url, CancellationToken ct = default)
    {
        try
        {
            var client = CriarCliente();
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
            _logger.LogDebug(ex, "Falha ao obter ou converter JSON de {Url}", url);
            return null;
        }
    }

    private HttpClient CriarCliente()
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml,application/xml;q=0.9,application/json,*/*;q=0.8");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("pt-BR,pt;q=0.9,en-US;q=0.8,en;q=0.7");
        client.Timeout = TimeSpan.FromSeconds(8);
        return client;
    }
}
