using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UrubuApi.Data;
using UrubuApi.DTOs;
using UrubuApi.Models;
using UrubuApi.Services.Crawlers;
using UrubuApi.Services.Evaluator;

namespace UrubuApi.Services.Orchestrator;

public class HunterOrchestrator : IHunterOrchestrator
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HunterOrchestrator> _logger;
    private readonly object _lock = new();

    private HuntStatusDto _status = new(
        isRunning: false,
        domain: null,
        lastRun: null,
        itemsFound: 0,
        itemsCurated: 0,
        error: null
    );

    public HunterOrchestrator(IServiceScopeFactory scopeFactory, ILogger<HunterOrchestrator> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public HuntStatusDto GetStatus()
    {
        lock (_lock)
        {
            return _status;
        }
    }

    public Task<HuntStatusDto> TriggerHuntAsync(string domain, IReadOnlyList<string>? targetStores = null, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_status.isRunning)
            {
                return Task.FromResult(_status);
            }

            _status = new HuntStatusDto(
                isRunning: true,
                domain: domain,
                lastRun: DateTime.UtcNow.ToString("o"),
                itemsFound: 0,
                itemsCurated: 0,
                error: null
            );
        }

        // Executa a varredura assíncrona em background usando Task.Run para não bloquear a requisição HTTP
        _ = Task.Run(async () =>
        {
            try
            {
                await ExecuteHuntCycleAsync(domain, targetStores, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro durante o ciclo de garimpo do Urubu Hunter.");
                lock (_lock)
                {
                    _status = _status with { isRunning = false, error = ex.Message };
                }
            }
        }, CancellationToken.None);

        return Task.FromResult(GetStatus());
    }

    private async Task ExecuteHuntCycleAsync(string domain, IReadOnlyList<string>? targetStores, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<UrubuDbContext>>();
        var scraperRegistry = scope.ServiceProvider.GetRequiredService<ScraperRegistry>();
        var evaluator = scope.ServiceProvider.GetRequiredService<IStyleEvaluator>();
        var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();

        using var db = await dbFactory.CreateDbContextAsync(ct);

        // 1. Obtém o perfil de estilo ativo
        var profile = await db.StyleProfiles.FirstOrDefaultAsync(x => x.Domain == domain, ct);
        if (profile == null)
        {
            profile = new StyleProfile
            {
                Domain = domain,
                AestheticKeywords = new List<string> { "streetwear", "boxy", "heavyweight", "workwear", "baggy" },
                PreferredSizes = new List<string> { "M", "G", "GG" },
                MaxPrice = 500m,
                MinDiscountPct = 0,
                ForbiddenTerms = new List<string> { "skinny", "poliéster" },
                TargetStores = new List<string> { "https://classofficial.com.br", "https://sufgang.com.br", "https://pacecompany.com.br" }
            };
            db.StyleProfiles.Add(profile);
            await db.SaveChangesAsync(ct);
        }

        var storesToCrawl = targetStores != null && targetStores.Count > 0
            ? targetStores
            : profile.TargetStores;

        // 2. Se for varredura geral (todas as lojas), renova o feed não curtido.
        // Se for garimpo direcionado de lojas/URLs específicas, preserva o feed e adiciona as novas peças
        if (targetStores == null || targetStores.Count == 0)
        {
            var unlikedItems = await db.CuratedItems
                .Where(x => x.Domain == domain && x.Feedback == null)
                .ToListAsync(ct);

            if (unlikedItems.Count > 0)
            {
                db.CuratedItems.RemoveRange(unlikedItems);
                await db.SaveChangesAsync(ct);
            }
        }
        else
        {
            // Salva as novas lojas no perfil para conveniência futura
            var hasNewStore = false;
            foreach (var store in targetStores)
            {
                if (!profile.TargetStores.Contains(store, StringComparer.OrdinalIgnoreCase))
                {
                    profile.TargetStores.Add(store);
                    hasNewStore = true;
                }
            }
            if (hasNewStore)
            {
                await db.SaveChangesAsync(ct);
            }
        }

        var totalFound = 0;
        var totalCurated = 0;
        var currentTotalCount = await db.CuratedItems.CountAsync(x => x.Domain == domain, ct);

        // 3. Varredura das URLs informadas (qualquer loja ou produto da web)
        foreach (var storeUrl in storesToCrawl.Distinct())
        {
            if (string.IsNullOrWhiteSpace(storeUrl)) continue;

            try
            {
                var scraper = scraperRegistry.GetScraperForUrl(storeUrl);
                IReadOnlyList<Product> crawledProducts;

                // Suporte transparente para URL de produto individual vs Loja inteira
                if (UrlUtils.IsSpecificProductUrl(storeUrl))
                {
                    _logger.LogInformation("Detectada URL de produto individual: {Url}", storeUrl);
                    var singleProduct = await scraper.ScrapeProductAsync(storeUrl, ct);
                    crawledProducts = singleProduct != null ? new List<Product> { singleProduct } : Array.Empty<Product>();
                }
                else
                {
                    _logger.LogInformation("Iniciando varredura da loja/catálogo: {Url}", storeUrl);
                    crawledProducts = await scraper.ScrapeStoreAsync(storeUrl, ct);
                }

                totalFound += crawledProducts.Count;

                foreach (var product in crawledProducts)
                {
                    if (!evaluator.PassesPreFilter(product, profile))
                    {
                        continue;
                    }

                    var evaluation = evaluator.Evaluate(product, profile);

                    var curatedItem = new CuratedItem
                    {
                        Id = product.Id,
                        Domain = domain,
                        Title = product.Title,
                        Brand = product.Brand,
                        Price = product.Price,
                        OriginalPrice = product.OriginalPrice,
                        DiscountPct = product.DiscountPct,
                        Url = product.Url,
                        Images = product.Images,
                        Category = product.Category,
                        AvailableSizes = product.AvailableSizes,
                        Tags = product.Tags,
                        Source = product.Source,
                        Evaluation = evaluation,
                        CuratedAt = DateTime.UtcNow
                    };

                    var existing = await db.CuratedItems
                        .Include(x => x.Evaluation)
                        .FirstOrDefaultAsync(x => x.Id == curatedItem.Id, ct);

                    if (existing != null)
                    {
                        existing.Price = curatedItem.Price;
                        existing.OriginalPrice = curatedItem.OriginalPrice;
                        existing.DiscountPct = curatedItem.DiscountPct;
                        existing.AvailableSizes = curatedItem.AvailableSizes;
                        if (existing.Evaluation != null)
                        {
                            existing.Evaluation.Score = evaluation.Score;
                            existing.Evaluation.Verdict = evaluation.Verdict;
                            existing.Evaluation.MatchReasons = evaluation.MatchReasons;
                            existing.Evaluation.Concerns = evaluation.Concerns;
                            existing.Evaluation.DetectedTags = evaluation.DetectedTags;
                            existing.Evaluation.IsRecommended = evaluation.IsRecommended;
                        }
                    }
                    else
                    {
                        // Limite rígido de 500 itens para poupar memória e garantir máxima fluidez
                        if (currentTotalCount >= 500)
                        {
                            _logger.LogInformation("Limite máximo de 500 itens atingido para o feed {Domain}. Interrompendo adições.", domain);
                            break;
                        }

                        db.CuratedItems.Add(curatedItem);
                        totalCurated++;
                        currentTotalCount++;
                    }
                }

                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao varrer loja {StoreUrl}", storeUrl);
            }
        }

        // 4. Invalida cache de feed estático
        cache.Remove($"STATIC_FEED_{domain.ToUpperInvariant()}");

        // 5. Finaliza o status do garimpo
        lock (_lock)
        {
            _status = new HuntStatusDto(
                isRunning: false,
                domain: domain,
                lastRun: DateTime.UtcNow.ToString("o"),
                itemsFound: totalFound,
                itemsCurated: totalCurated,
                error: null
            );
        }
    }
}
