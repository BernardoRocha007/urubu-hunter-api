using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UrubuApi.Data;
using UrubuApi.DTOs;
using UrubuApi.Models;

namespace UrubuApi.Services;

public class ServicoFeed : IServicoFeed
{
    private readonly IDbContextFactory<DBContext> _dbFactory;
    private readonly IMemoryCache _cache;
    private const string ChaveCacheFeedEstatico = "STATIC_FEED_FASHION";

    public ServicoFeed(IDbContextFactory<DBContext> dbFactory, IMemoryCache cache)
    {
        _dbFactory = dbFactory;
        _cache = cache;
    }

    public async Task<FeedResponse> ObterFeedEstaticoAsync(string dominio = "fashion", CancellationToken ct = default)
    {
        if (_cache.TryGetValue(ChaveCacheFeedEstatico, out FeedResponse? cached) && cached != null)
        {
            return cached;
        }

        using var db = await _dbFactory.CreateDbContextAsync(ct);
        await GarantirDadosPerfilAsync(db, ct);

        var query = db.CuratedItems
            .Include(x => x.Evaluation)
            .Where(x => x.Domain == dominio)
            .OrderByDescending(x => x.Evaluation != null ? x.Evaluation.Score : 0)
            .ThenByDescending(x => x.CreatedAt);

        var totalItens = await query.CountAsync(ct);
        var itens = await query.Take(24).ToListAsync(ct);
        var categorias = await db.CuratedItems
            .Where(x => x.Domain == dominio && !string.IsNullOrEmpty(x.Category))
            .Select(x => x.Category)
            .Distinct()
            .ToListAsync(ct);

        var response = new FeedResponse(
            Items: itens,
            TotalItems: totalItens,
            Page: 1,
            TotalPages: Math.Max(1, (int)Math.Ceiling(totalItens / 24.0)),
            AvailableCategories: categorias,
            GeneratedAt: DateTime.UtcNow
        );

        _cache.Set(ChaveCacheFeedEstatico, response, TimeSpan.FromMinutes(5));
        return response;
    }

    public async Task<FeedResponse> ObterFeedFiltradoAsync(
        string dominio,
        int? pontuacaoMinima,
        bool apenasPromocao,
        string? categoria,
        string? busca,
        int pagina,
        int limite,
        CancellationToken ct = default)
    {
        using var db = await _dbFactory.CreateDbContextAsync(ct);
        await GarantirDadosPerfilAsync(db, ct);

        var query = db.CuratedItems
            .Include(x => x.Evaluation)
            .Where(x => x.Domain == dominio);

        if (pontuacaoMinima.HasValue)
        {
            query = query.Where(x => x.Evaluation != null && x.Evaluation.Score >= pontuacaoMinima.Value);
        }

        if (apenasPromocao)
        {
            query = query.Where(x => x.DiscountPct.HasValue && x.DiscountPct > 0);
        }

        if (!string.IsNullOrWhiteSpace(categoria))
        {
            query = query.Where(x => x.Category == categoria);
        }

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var s = busca.ToLower();
            query = query.Where(x => x.Title.ToLower().Contains(s) || x.Brand.ToLower().Contains(s));
        }

        var totalItens = await query.CountAsync(ct);
        var limiteSeguro = Math.Clamp(limite, 1, 100);
        var paginaSegura = Math.Max(1, pagina);
        var totalPaginas = Math.Max(1, (int)Math.Ceiling(totalItens / (double)limiteSeguro));

        var itens = await query
            .OrderByDescending(x => x.Evaluation != null ? x.Evaluation.Score : 0)
            .ThenByDescending(x => x.CreatedAt)
            .Skip((paginaSegura - 1) * limiteSeguro)
            .Take(limiteSeguro)
            .ToListAsync(ct);

        var categorias = await db.CuratedItems
            .Where(x => x.Domain == dominio && !string.IsNullOrEmpty(x.Category))
            .Select(x => x.Category)
            .Distinct()
            .ToListAsync(ct);

        return new FeedResponse(
            Items: itens,
            TotalItems: totalItens,
            Page: paginaSegura,
            TotalPages: totalPaginas,
            AvailableCategories: categorias,
            GeneratedAt: DateTime.UtcNow
        );
    }

    public async Task<CuratedItem?> DefinirFeedbackAsync(string id, string? feedback, CancellationToken ct = default)
    {
        using var db = await _dbFactory.CreateDbContextAsync(ct);
        var item = await db.CuratedItems.Include(x => x.Evaluation).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (item == null) return null;

        item.Feedback = feedback;
        await db.SaveChangesAsync(ct);
        InvalidarCacheEstatico();
        return item;
    }

    public async Task<(int Limpos, int Preservados)> LimparFeedAsync(string dominio, CancellationToken ct = default)
    {
        using var db = await _dbFactory.CreateDbContextAsync(ct);
        var paraRemover = await db.CuratedItems
            .Where(x => x.Domain == dominio && (x.Feedback == null || (x.Feedback != "liked" && x.Feedback != "saved")))
            .ToListAsync(ct);

        var preservados = await db.CuratedItems
            .Where(x => x.Domain == dominio && (x.Feedback == "liked" || x.Feedback == "saved"))
            .CountAsync(ct);

        db.CuratedItems.RemoveRange(paraRemover);
        await db.SaveChangesAsync(ct);

        InvalidarCacheEstatico();
        return (paraRemover.Count, preservados);
    }

    public async Task<IReadOnlyList<string>> ObterCategoriasAsync(string dominio, CancellationToken ct = default)
    {
        using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.CuratedItems
            .Where(x => x.Domain == dominio && !string.IsNullOrEmpty(x.Category))
            .Select(x => x.Category)
            .Distinct()
            .ToListAsync(ct);
    }

    public void InvalidarCacheEstatico()
    {
        _cache.Remove(ChaveCacheFeedEstatico);
    }

    private async Task GarantirDadosPerfilAsync(DBContext db, CancellationToken ct)
    {
        if (await db.StyleProfiles.AnyAsync(ct)) return;

        var perfil = new StyleProfile
        {
            Domain = "fashion",
            AestheticKeywords = new List<string> { "streetwear", "boxy", "heavyweight", "workwear", "baggy", "minimalist" },
            PreferredSizes = new List<string> { "M", "G", "GG" },
            MaxPrice = 450m,
            MinDiscountPct = 10,
            ForbiddenTerms = new List<string> { "skinny", "poliéster", "slim fit", "estampa exagerada" },
            TargetStores = new List<string> { "https://pacecompany.com.br", "https://sufgang.com.br", "https://kuehlofficial.com" }
        };
        db.StyleProfiles.Add(perfil);

        await db.SaveChangesAsync(ct);
    }
}
