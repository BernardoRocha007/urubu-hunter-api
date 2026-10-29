using System.Text.Json;

namespace UrubuApi.Services.Crawlers.Http;

public interface IClienteHttpColetor
{
    Task<string?> ObterHtmlAsync(string url, CancellationToken ct = default);
    Task<string?> ObterXmlAsync(string url, CancellationToken ct = default);
    Task<JsonDocument?> ObterJsonAsync(string url, CancellationToken ct = default);
}
