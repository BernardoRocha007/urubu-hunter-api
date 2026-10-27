using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UrubuApi.Data;
using UrubuApi.DTOs;
using UrubuApi.Models;
using UrubuApi.Services;

namespace UrubuApi.Endpoints;

public static class FeedEndpoints
{
    private static HuntStatusDto _huntStatus = new(false, "fashion", DateTime.UtcNow.ToString("o"), 0, 0, null);

    public static RouteGroupBuilder MapFeedEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api");

        // 1. GET /api/domains
        group.MapGet("/domains", () =>
        {
            var domains = new Dictionary<string, object>
            {
                ["fashion"] = new
                {
                    id = "fashion",
                    name = "Moda Streetwear",
                    description = "Curadoria de streetwear, marcas de nicho do Instagram, cortes oversized e achados com desconto.",
                    icon = "STREET",
                    defaultKeywords = new[] { "streetwear", "oversized", "corte boxy", "vintage 90s", "tons terrosos", "100% algodÃ£o", "minimalista urbano" },
                    sizeOptions = new[] { "PP", "P", "M", "G", "GG", "XG", "38", "40", "42", "44", "46", "39", "40", "41", "42", "43" },
                    defaultStores = new[] { "https://pacecompany.com.br", "https://sufgang.com.br", "https://kuehlofficial.com", "https://piet.com.br" }
                },
                ["pc-hardware"] = new
                {
                    id = "pc-hardware",
                    name = "Hardware & Tech",
                    description = "Monitoramento de placas de vÃ­deo, processadores e promoÃ§Ãµes de componentes custo-benefÃ­cio.",
                    icon = "TECH",
                    defaultKeywords = new[] { "RTX 4070", "RTX 4060 Ti", "Ryzen 7 7800X3D", "DDR5 6000MHz", "NVMe Gen4 2TB" },
                    sizeOptions = new[] { "Mini-ITX", "Micro-ATX", "ATX", "E-ATX", "16GB", "32GB", "64GB", "1TB", "2TB" },
                    defaultStores = new[] { "https://kabum.com.br", "https://terabyteshop.com.br", "https://pichau.com.br" }
                }
            };
            return Results.Ok(domains);
        })
        .WithName("GetDomains");

        // 2. GET /api/profile
        group.MapGet("/profile", async ([FromQuery] string? domain, [FromServices] IDbContextFactory<UrubuDbContext> dbFactory, CancellationToken ct) =>
        {
            using var db = await dbFactory.CreateDbContextAsync(ct);
            var d = domain ?? "fashion";
            var profile = await db.StyleProfiles.FirstOrDefaultAsync(x => x.Domain == d, ct);
            if (profile == null)
            {
                profile = new StyleProfile
                {
                    Domain = d,
                    AestheticKeywords = new List<string> { "streetwear", "boxy", "heavyweight", "workwear", "baggy" },
                    PreferredSizes = new List<string> { "M", "G", "GG" },
                    MaxPrice = 450m,
                    MinDiscountPct = 10,
                    ForbiddenTerms = new List<string> { "skinny", "poliÃ©ster" },
                    TargetStores = new List<string> { "https://classofficial.com.br", "https://sufgang.com.br", "https://pacecompany.com.br" }
                };
            }
            return Results.Ok(profile);
        })
        .WithName("GetProfile");

        // 3. POST /api/profile
        group.MapPost("/profile", async ([FromBody] StyleProfile updated, [FromServices] IDbContextFactory<UrubuDbContext> dbFactory, CancellationToken ct) =>
        {
            using var db = await dbFactory.CreateDbContextAsync(ct);
            var existing = await db.StyleProfiles.FirstOrDefaultAsync(x => x.Domain == updated.Domain, ct);
            if (existing != null)
            {
                existing.AestheticKeywords = updated.AestheticKeywords;
                existing.PreferredSizes = updated.PreferredSizes;
                existing.MaxPrice = updated.MaxPrice;
                existing.MinDiscountPct = updated.MinDiscountPct;
                existing.ForbiddenTerms = updated.ForbiddenTerms;
                existing.TargetStores = updated.TargetStores;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                db.StyleProfiles.Add(updated);
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(updated);
        })
        .WithName("SaveProfile");

        // 4. GET /api/items (Exato contrato do Hunter)
        group.MapGet("/items", async (
            [FromServices] IFeedService feedService,
            [FromQuery] string? domain,
            [FromQuery] int? minScore,
            [FromQuery] bool? saleOnly,
            [FromQuery] string? category,
            [FromQuery] string? search,
            [FromQuery] int? page,
            [FromQuery] int? limit,
            CancellationToken ct) =>
        {
            var p = page ?? 1;
            var l = limit ?? 20;
            var result = await feedService.GetFilteredFeedAsync(domain ?? "fashion", minScore, saleOnly ?? false, category, search, p, l, ct);

            var paginated = new PaginatedItemsResponse(
                items: result.Items,
                total: result.TotalItems,
                page: result.Page,
                limit: l,
                totalPages: result.TotalPages,
                hasMore: result.Page < result.TotalPages
            );

            return Results.Ok(paginated);
        })
        .WithName("GetItems");

        // 5. GET /api/feed/static (Snapshot estÃ¡tico de alta velocidade)
        group.MapGet("/feed/static", async ([FromServices] IFeedService feedService, [FromQuery] string? domain, CancellationToken ct) =>
        {
            var result = await feedService.GetStaticFeedAsync(domain ?? "fashion", ct);
            return Results.Ok(result);
        })
        .WithName("GetStaticFeed");

        // 6. GET /api/categories
        group.MapGet("/categories", async ([FromServices] IFeedService feedService, [FromQuery] string? domain, CancellationToken ct) =>
        {
            var categories = await feedService.GetCategoriesAsync(domain ?? "fashion", ct);
            return Results.Ok(categories);
        })
        .WithName("GetCategories");

        // 7. POST /api/items/{id}/feedback
        group.MapPost("/items/{id}/feedback", async (string id, [FromBody] FeedbackRequest request, [FromServices] IFeedService feedService, CancellationToken ct) =>
        {
            var updated = await feedService.SetFeedbackAsync(id, request.Feedback, ct);
            if (updated == null) return Results.NotFound(new { error = "Item nÃ£o encontrado." });
            return Results.Ok(updated);
        })
        .WithName("SetItemFeedback");

        // 8. POST /api/items/clear
        group.MapPost("/items/clear", async ([FromServices] IFeedService feedService, [FromQuery] string? domain, CancellationToken ct) =>
        {
            var (cleared, preserved) = await feedService.ClearFeedAsync(domain ?? "fashion", ct);
            return Results.Ok(new ClearFeedResponse(
                true,
                cleared,
                preserved,
                $"{cleared} itens removidos do feed. {preserved} favoritos foram preservados!"
            ));
        })
        .WithName("ClearFeedItems");

        // 9. POST /api/hunt
        group.MapPost("/hunt", ([FromBody] object? body) =>
        {
            _huntStatus = new HuntStatusDto(true, "fashion", DateTime.UtcNow.ToString("o"), 3, 3, null);

            // Simula conclusÃ£o assÃ­ncrona para o polling
            Task.Run(async () =>
            {
                await Task.Delay(3000);
                _huntStatus = new HuntStatusDto(false, "fashion", DateTime.UtcNow.ToString("o"), 3, 3, null);
            });

            return Results.Ok(new { message = "Garimpo iniciado com sucesso!", status = _huntStatus });
        })
        .WithName("TriggerHunt");

        // 10. GET /api/hunt/status
        group.MapGet("/hunt/status", () => Results.Ok(_huntStatus))
        .WithName("GetHuntStatus");

        // 11. GET /api/watchlist & POST /api/watchlist
        group.MapGet("/watchlist", () => Results.Ok(Array.Empty<object>()))
        .WithName("GetWatchlist");

        group.MapPost("/watchlist", ([FromBody] object body) => Results.Created("/api/watchlist", body))
        .WithName("AddWatchlist");

        // 12. GET & POST /api/notifications/config
        group.MapGet("/notifications/config", () => Results.Ok(new
        {
            email = "",
            phone = "",
            notifyOnPriceDrop = true,
            notifyOnTargetMet = true,
            channels = new { email = true, whatsapp = true, telegram = false, discord = false }
        }))
        .WithName("GetNotificationConfig");

        group.MapPost("/notifications/config", ([FromBody] object config) => Results.Ok(config))
        .WithName("SaveNotificationConfig");

        return group;
    }
}
