using UrubuApi.DTOs;
using UrubuApi.Models;

namespace UrubuApi.Services;

public interface IFeedService
{
    Task<FeedResponse> GetStaticFeedAsync(string domain = "fashion", CancellationToken ct = default);
    Task<FeedResponse> GetFilteredFeedAsync(string domain, int? minScore, bool saleOnly, string? category, string? search, int page, int limit, CancellationToken ct = default);
    Task<CuratedItem?> SetFeedbackAsync(string id, string? feedback, CancellationToken ct = default);
    Task<(int Cleared, int Preserved)> ClearFeedAsync(string domain, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetCategoriesAsync(string domain, CancellationToken ct = default);
    void InvalidateStaticCache();
}
