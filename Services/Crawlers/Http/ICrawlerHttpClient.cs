using System.Text.Json;

namespace UrubuApi.Services.Crawlers.Http;

public interface ICrawlerHttpClient
{
    Task<string?> GetHtmlAsync(string url, CancellationToken ct = default);
    Task<string?> GetXmlAsync(string url, CancellationToken ct = default);
    Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct = default);
}
