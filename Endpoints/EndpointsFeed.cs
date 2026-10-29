using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UrubuApi.Data;
using UrubuApi.DTOs;
using UrubuApi.Models;
using UrubuApi.Services;
using UrubuApi.Services.Orchestrator;

namespace UrubuApi.Endpoints;

public static class EndpointsFeed
{
    public static RouteGroupBuilder MapearEndpointsFeed(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api");

        // 1. GET /api/domains
        grupo.MapGet("/domains", () =>
        {
            var dominios = new Dictionary<string, object>
            {
                ["fashion"] = new
                {
                    id = "fashion",
                    name = "Moda Streetwear",
                    description = "Curadoria de streetwear, marcas de nicho do Instagram, cortes oversized e achados com desconto.",
                    icon = "STREET",
                    defaultKeywords = new[] { "streetwear", "oversized", "corte boxy", "vintage 90s", "tons terrosos", "100% algodão", "minimalista urbano" },
                    sizeOptions = new[] { "PP", "P", "M", "G", "GG", "XG", "38", "39", "40", "41", "42", "43", "44", "46" },
                    defaultStores = new[] { "https://classofficial.com.br", "https://pacecompany.com.br", "https://sufgang.com.br", "https://kuehlofficial.com", "https://piet.com.br" }
                },
                ["pc-hardware"] = new
                {
                    id = "pc-hardware",
                    name = "Hardware & Tech",
                    description = "Monitoramento de placas de vídeo, processadores e promoções de componentes custo-benefício.",
                    icon = "TECH",
                    defaultKeywords = new[] { "RTX 4070", "RTX 4060 Ti", "Ryzen 7 7800X3D", "DDR5 6000MHz", "NVMe Gen4 2TB" },
                    sizeOptions = new[] { "Mini-ITX", "Micro-ATX", "ATX", "E-ATX", "16GB", "32GB", "64GB", "1TB", "2TB" },
                    defaultStores = new[] { "https://kabum.com.br", "https://terabyteshop.com.br", "https://pichau.com.br" }
                }
            };
            return Results.Ok(dominios);
        })
        .WithName("ObterDominios");

        // 2. GET /api/profile
        grupo.MapGet("/profile", async ([FromQuery] string? domain, [FromServices] IDbContextFactory<DBContext> fabricaDb, CancellationToken ct) =>
        {
            using var db = await fabricaDb.CreateDbContextAsync(ct);
            var d = domain ?? "fashion";
            var perfil = await db.StyleProfiles.FirstOrDefaultAsync(x => x.Domain == d, ct);
            if (perfil == null)
            {
                perfil = new StyleProfile
                {
                    Domain = d,
                    AestheticKeywords = new List<string> { "streetwear", "boxy", "heavyweight", "workwear", "baggy" },
                    PreferredSizes = new List<string> { "M", "G", "GG" },
                    MaxPrice = 500m,
                    MinDiscountPct = 0,
                    ForbiddenTerms = new List<string> { "skinny", "poliéster" },
                    TargetStores = new List<string> { "https://classofficial.com.br", "https://sufgang.com.br", "https://pacecompany.com.br" }
                };
            }
            return Results.Ok(perfil);
        })
        .WithName("ObterPerfil");

        // 3. POST /api/profile
        grupo.MapPost("/profile", async ([FromBody] StyleProfile atualizado, [FromServices] IDbContextFactory<DBContext> fabricaDb, CancellationToken ct) =>
        {
            using var db = await fabricaDb.CreateDbContextAsync(ct);
            var existente = await db.StyleProfiles.FirstOrDefaultAsync(x => x.Domain == atualizado.Domain, ct);
            if (existente != null)
            {
                existente.AestheticKeywords = atualizado.AestheticKeywords;
                existente.PreferredSizes = atualizado.PreferredSizes;
                existente.MaxPrice = atualizado.MaxPrice;
                existente.MinDiscountPct = atualizado.MinDiscountPct;
                existente.ForbiddenTerms = atualizado.ForbiddenTerms;
                existente.TargetStores = atualizado.TargetStores;
                existente.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                db.StyleProfiles.Add(atualizado);
            }
            await db.SaveChangesAsync(ct);
            return Results.Ok(atualizado);
        })
        .WithName("SalvarPerfil");

        // 4. GET /api/items (Exato contrato do Hunter)
        grupo.MapGet("/items", async (
            [FromServices] IServicoFeed servicoFeed,
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
            var resultado = await servicoFeed.ObterFeedFiltradoAsync(domain ?? "fashion", minScore, saleOnly ?? false, category, search, p, l, ct);

            var paginado = new PaginatedItemsResponse(
                items: resultado.Items,
                total: resultado.TotalItems,
                page: resultado.Page,
                limit: l,
                totalPages: resultado.TotalPages,
                hasMore: resultado.Page < resultado.TotalPages
            );

            return Results.Ok(paginado);
        })
        .WithName("ObterItens");

        // 5. GET /api/feed/static (Snapshot estático de alta velocidade)
        grupo.MapGet("/feed/static", async ([FromServices] IServicoFeed servicoFeed, [FromQuery] string? domain, CancellationToken ct) =>
        {
            var resultado = await servicoFeed.ObterFeedEstaticoAsync(domain ?? "fashion", ct);
            return Results.Ok(resultado);
        })
        .WithName("ObterFeedEstatico");

        // 6. GET /api/categories
        grupo.MapGet("/categories", async ([FromServices] IServicoFeed servicoFeed, [FromQuery] string? domain, CancellationToken ct) =>
        {
            var categorias = await servicoFeed.ObterCategoriasAsync(domain ?? "fashion", ct);
            return Results.Ok(categorias);
        })
        .WithName("ObterCategorias");

        // 7. POST /api/items/{id}/feedback
        grupo.MapPost("/items/{id}/feedback", async (string id, [FromBody] FeedbackRequest requisicao, [FromServices] IServicoFeed servicoFeed, CancellationToken ct) =>
        {
            var atualizado = await servicoFeed.DefinirFeedbackAsync(id, requisicao.Feedback, ct);
            if (atualizado == null) return Results.NotFound(new { error = "Item não encontrado." });
            return Results.Ok(atualizado);
        })
        .WithName("DefinirFeedbackItem");

        // 8. POST /api/items/clear
        grupo.MapPost("/items/clear", async ([FromServices] IServicoFeed servicoFeed, [FromQuery] string? domain, CancellationToken ct) =>
        {
            var (limpos, preservados) = await servicoFeed.LimparFeedAsync(domain ?? "fashion", ct);
            return Results.Ok(new ClearFeedResponse(
                true,
                limpos,
                preservados,
                $"{limpos} itens removidos do feed. {preservados} favoritos foram preservados!"
            ));
        })
        .WithName("LimparItensFeed");

        // 9. POST /api/hunt (Disparo de garimpo com coletor real em segundo plano)
        grupo.MapPost("/hunt", async ([FromBody] HuntRequest? requisicao, [FromServices] IOrquestradorGarimpo orquestrador, CancellationToken ct) =>
        {
            var status = orquestrador.ObterStatus();
            if (status.isRunning)
            {
                return Results.Conflict(new { error = "Um garimpo já está em andamento." });
            }

            var dominio = requisicao?.domain ?? "fashion";
            var statusAtualizado = await orquestrador.IniciarGarimpoAsync(dominio, requisicao?.targetStores, ct);

            return Results.Ok(new { message = "Garimpo iniciado com sucesso!", status = statusAtualizado });
        })
        .WithName("DispararGarimpo");

        // 10. GET /api/hunt/status (Consulta de status do garimpo)
        grupo.MapGet("/hunt/status", ([FromServices] IOrquestradorGarimpo orquestrador) =>
        {
            return Results.Ok(orquestrador.ObterStatus());
        })
        .WithName("ObterStatusGarimpo");

        // 11. GET /api/watchlist & POST /api/watchlist
        grupo.MapGet("/watchlist", () => Results.Ok(Array.Empty<object>()))
        .WithName("ObterListaObservacao");

        grupo.MapPost("/watchlist", ([FromBody] object corpo) => Results.Created("/api/watchlist", corpo))
        .WithName("AdicionarListaObservacao");

        // 12. GET & POST /api/notifications/config
        grupo.MapGet("/notifications/config", () => Results.Ok(new
        {
            email = "",
            phone = "",
            notifyOnPriceDrop = true,
            notifyOnTargetMet = true,
            channels = new { email = true, whatsapp = true, telegram = false, discord = false }
        }))
        .WithName("ObterConfiguracaoNotificacoes");

        grupo.MapPost("/notifications/config", ([FromBody] object config) => Results.Ok(config))
        .WithName("SalvarConfiguracaoNotificacoes");

        return grupo;
    }
}
