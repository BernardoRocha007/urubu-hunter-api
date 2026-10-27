using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UrubuApi.Data;
using UrubuApi.DTOs;
using UrubuApi.Models;

namespace UrubuApi.Services;

public class FeedService : IFeedService
{
    private readonly IDbContextFactory<UrubuDbContext> _dbFactory;
    private readonly IMemoryCache _cache;
    private const string StaticFeedCacheKey = "STATIC_FEED_FASHION";

    public FeedService(IDbContextFactory<UrubuDbContext> dbFactory, IMemoryCache cache)
    {
        _dbFactory = dbFactory;
        _cache = cache;
    }

    public async Task<FeedResponse> GetStaticFeedAsync(string domain = "fashion", CancellationToken ct = default)
    {
        if (_cache.TryGetValue(StaticFeedCacheKey, out FeedResponse? cached) && cached != null)
        {
            return cached;
        }

        using var db = await _dbFactory.CreateDbContextAsync(ct);
        await EnsureProfileDataAsync(db, ct);

        var query = db.CuratedItems
            .Include(x => x.Evaluation)
            .Where(x => x.Domain == domain)
            .OrderByDescending(x => x.Evaluation != null ? x.Evaluation.Score : 0)
            .ThenByDescending(x => x.CreatedAt);

        var totalItems = await query.CountAsync(ct);
        var items = await query.Take(24).ToListAsync(ct);
        var categories = await db.CuratedItems
            .Where(x => x.Domain == domain && !string.IsNullOrEmpty(x.Category))
            .Select(x => x.Category)
            .Distinct()
            .ToListAsync(ct);

        var response = new FeedResponse(
            Items: items,
            TotalItems: totalItems,
            Page: 1,
            TotalPages: Math.Max(1, (int)Math.Ceiling(totalItems / 24.0)),
            AvailableCategories: categories,
            GeneratedAt: DateTime.UtcNow
        );

        _cache.Set(StaticFeedCacheKey, response, TimeSpan.FromMinutes(5));
        return response;
    }

    public async Task<FeedResponse> GetFilteredFeedAsync(
        string domain,
        int? minScore,
        bool saleOnly,
        string? category,
        string? search,
        int page,
        int limit,
        CancellationToken ct = default)
    {
        using var db = await _dbFactory.CreateDbContextAsync(ct);
        await EnsureProfileDataAsync(db, ct);

        var query = db.CuratedItems
            .Include(x => x.Evaluation)
            .Where(x => x.Domain == domain);

        if (minScore.HasValue)
        {
            query = query.Where(x => x.Evaluation != null && x.Evaluation.Score >= minScore.Value);
        }

        if (saleOnly)
        {
            query = query.Where(x => x.DiscountPct.HasValue && x.DiscountPct > 0);
        }

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(x => x.Category == category);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.ToLower();
            query = query.Where(x => x.Title.ToLower().Contains(s) || x.Brand.ToLower().Contains(s));
        }

        var totalItems = await query.CountAsync(ct);
        var safeLimit = Math.Clamp(limit, 1, 100);
        var safePage = Math.Max(1, page);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)safeLimit));

        var items = await query
            .OrderByDescending(x => x.Evaluation != null ? x.Evaluation.Score : 0)
            .ThenByDescending(x => x.CreatedAt)
            .Skip((safePage - 1) * safeLimit)
            .Take(safeLimit)
            .ToListAsync(ct);

        var categories = await db.CuratedItems
            .Where(x => x.Domain == domain && !string.IsNullOrEmpty(x.Category))
            .Select(x => x.Category)
            .Distinct()
            .ToListAsync(ct);

        return new FeedResponse(
            Items: items,
            TotalItems: totalItems,
            Page: safePage,
            TotalPages: totalPages,
            AvailableCategories: categories,
            GeneratedAt: DateTime.UtcNow
        );
    }

    public async Task<CuratedItem?> SetFeedbackAsync(string id, string? feedback, CancellationToken ct = default)
    {
        using var db = await _dbFactory.CreateDbContextAsync(ct);
        var item = await db.CuratedItems.Include(x => x.Evaluation).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item == null) return null;

        item.Feedback = feedback;
        await db.SaveChangesAsync(ct);
        InvalidateStaticCache();
        return item;
    }

    public async Task<(int Cleared, int Preserved)> ClearFeedAsync(string domain, CancellationToken ct = default)
    {
        using var db = await _dbFactory.CreateDbContextAsync(ct);
        var toDelete = await db.CuratedItems
            .Where(x => x.Domain == domain && (x.Feedback == null || (x.Feedback != "liked" && x.Feedback != "saved")))
            .ToListAsync(ct);

        var preserved = await db.CuratedItems
            .Where(x => x.Domain == domain && (x.Feedback == "liked" || x.Feedback == "saved"))
            .CountAsync(ct);

        db.CuratedItems.RemoveRange(toDelete);
        await db.SaveChangesAsync(ct);

        InvalidateStaticCache();
        return (toDelete.Count, preserved);
    }

    public async Task<IReadOnlyList<string>> GetCategoriesAsync(string domain, CancellationToken ct = default)
    {
        using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.CuratedItems
            .Where(x => x.Domain == domain && !string.IsNullOrEmpty(x.Category))
            .Select(x => x.Category)
            .Distinct()
            .ToListAsync(ct);
    }

    public void InvalidateStaticCache()
    {
        _cache.Remove(StaticFeedCacheKey);
    }

    private async Task EnsureProfileDataAsync(UrubuDbContext db, CancellationToken ct)
    {
        if (await db.StyleProfiles.AnyAsync(ct)) return;

        var profile = new StyleProfile
        {
            Domain = "fashion",
            AestheticKeywords = new List<string> { "streetwear", "boxy", "heavyweight", "workwear", "baggy", "minimalist" },
            PreferredSizes = new List<string> { "M", "G", "GG" },
            MaxPrice = 450m,
            MinDiscountPct = 10,
            ForbiddenTerms = new List<string> { "skinny", "poliéster", "slim fit", "estampa exagerada" },
            TargetStores = new List<string> { "https://pacecompany.com.br", "https://sufgang.com.br", "https://kuehlofficial.com" }
        };
        db.StyleProfiles.Add(profile);

        await db.SaveChangesAsync(ct);
    }
}
