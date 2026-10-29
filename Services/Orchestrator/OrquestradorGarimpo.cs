using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using UrubuApi.Data;
using UrubuApi.DTOs;
using UrubuApi.Models;
using UrubuApi.Services.Crawlers;
using UrubuApi.Services.Evaluator;

namespace UrubuApi.Services.Orchestrator;

public class OrquestradorGarimpo : IOrquestradorGarimpo
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OrquestradorGarimpo> _logger;
    private readonly object _lock = new();

    private HuntStatusDto _status = new(
        isRunning: false,
        domain: null,
        lastRun: null,
        itemsFound: 0,
        itemsCurated: 0,
        error: null
    );

    public OrquestradorGarimpo(IServiceScopeFactory scopeFactory, ILogger<OrquestradorGarimpo> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public HuntStatusDto ObterStatus()
    {
        lock (_lock)
        {
            return _status;
        }
    }

    public Task<HuntStatusDto> IniciarGarimpoAsync(string dominio, IReadOnlyList<string>? lojasAlvo = null, CancellationToken ct = default)
    {
        lock (_lock)
        {
            if (_status.isRunning)
            {
                return Task.FromResult(_status);
            }

            _status = new HuntStatusDto(
                isRunning: true,
                domain: dominio,
                lastRun: DateTime.UtcNow.ToString("o"),
                itemsFound: 0,
                itemsCurated: 0,
                error: null
            );
        }

        // Executa a varredura assíncrona em background para não travar a requisição HTTP
        _ = Task.Run(async () =>
        {
            try
            {
                await ExecutarCicloGarimpoAsync(dominio, lojasAlvo, CancellationToken.None);
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

        return Task.FromResult(ObterStatus());
    }

    private async Task ExecutarCicloGarimpoAsync(string dominio, IReadOnlyList<string>? lojasAlvo, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<DBContext>>();
        var catalogoColetores = scope.ServiceProvider.GetRequiredService<CatalogoColetores>();
        var avaliador = scope.ServiceProvider.GetRequiredService<IAvaliadorEstilo>();
        var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();

        using var db = await dbFactory.CreateDbContextAsync(ct);

        // 1. Obtém o perfil de estilo ativo
        var perfil = await db.StyleProfiles.FirstOrDefaultAsync(x => x.Domain == dominio, ct);
        if (perfil == null)
        {
            perfil = new StyleProfile
            {
                Domain = dominio,
                AestheticKeywords = new List<string> { "streetwear", "boxy", "heavyweight", "workwear", "baggy" },
                PreferredSizes = new List<string> { "M", "G", "GG" },
                MaxPrice = 500m,
                MinDiscountPct = 0,
                ForbiddenTerms = new List<string> { "skinny", "poliéster" },
                TargetStores = new List<string> { "https://classofficial.com.br", "https://sufgang.com.br", "https://pacecompany.com.br" }
            };
            db.StyleProfiles.Add(perfil);
            await db.SaveChangesAsync(ct);
        }

        var lojasParaVarrer = lojasAlvo != null && lojasAlvo.Count > 0
            ? lojasAlvo
            : perfil.TargetStores;

        // 2. Se for varredura geral (todas as lojas), renova o feed não curtido.
        // Se for garimpo direcionado de lojas/URLs específicas, preserva o feed e adiciona as novas peças
        if (lojasAlvo == null || lojasAlvo.Count == 0)
        {
            var itensSemFeedback = await db.CuratedItems
                .Where(x => x.Domain == dominio && x.Feedback == null)
                .ToListAsync(ct);

            if (itensSemFeedback.Count > 0)
            {
                db.CuratedItems.RemoveRange(itensSemFeedback);
                await db.SaveChangesAsync(ct);
            }
        }
        else
        {
            // Salva as novas lojas no perfil para conveniência futura
            var temNovaLoja = false;
            foreach (var loja in lojasAlvo)
            {
                if (!perfil.TargetStores.Contains(loja, StringComparer.OrdinalIgnoreCase))
                {
                    perfil.TargetStores.Add(loja);
                    temNovaLoja = true;
                }
            }
            if (temNovaLoja)
            {
                await db.SaveChangesAsync(ct);
            }
        }

        var totalEncontrados = 0;
        var totalCurados = 0;
        var contagemTotalAtual = await db.CuratedItems.CountAsync(x => x.Domain == dominio, ct);

        // 3. Varredura das URLs informadas
        foreach (var urlLoja in lojasParaVarrer.Distinct())
        {
            if (string.IsNullOrWhiteSpace(urlLoja)) continue;

            try
            {
                var coletor = catalogoColetores.ObterColetorParaUrl(urlLoja);
                IReadOnlyList<Product> produtosColetados;

                if (UrlUtils.EhUrlDeProdutoEspecifico(urlLoja))
                {
                    _logger.LogInformation("Detectada URL de produto individual: {Url}", urlLoja);
                    var produtoIndividual = await coletor.VarrerProdutoAsync(urlLoja, ct);
                    produtosColetados = produtoIndividual != null ? new List<Product> { produtoIndividual } : Array.Empty<Product>();
                }
                else
                {
                    _logger.LogInformation("Iniciando varredura da loja/catálogo: {Url}", urlLoja);
                    produtosColetados = await coletor.VarrerLojaAsync(urlLoja, ct);
                }

                totalEncontrados += produtosColetados.Count;

                foreach (var produto in produtosColetados)
                {
                    if (!avaliador.PassaNoPreFiltro(produto, perfil))
                    {
                        continue;
                    }

                    var avaliacao = avaliador.Avaliar(produto, perfil);

                    var itemCurado = new CuratedItem
                    {
                        Id = produto.Id,
                        Domain = dominio,
                        Title = produto.Title,
                        Brand = produto.Brand,
                        Price = produto.Price,
                        OriginalPrice = produto.OriginalPrice,
                        DiscountPct = produto.DiscountPct,
                        Url = produto.Url,
                        Images = produto.Images,
                        Category = produto.Category,
                        AvailableSizes = produto.AvailableSizes,
                        Tags = produto.Tags,
                        Source = produto.Source,
                        Evaluation = avaliacao,
                        CuratedAt = DateTime.UtcNow
                    };

                    var existente = await db.CuratedItems
                        .Include(x => x.Evaluation)
                        .FirstOrDefaultAsync(x => x.Id == itemCurado.Id, ct);

                    if (existente != null)
                    {
                        existente.Price = itemCurado.Price;
                        existente.OriginalPrice = itemCurado.OriginalPrice;
                        existente.DiscountPct = itemCurado.DiscountPct;
                        existente.AvailableSizes = itemCurado.AvailableSizes;
                        if (existente.Evaluation != null)
                        {
                            existente.Evaluation.Score = avaliacao.Score;
                            existente.Evaluation.Verdict = avaliacao.Verdict;
                            existente.Evaluation.MatchReasons = avaliacao.MatchReasons;
                            existente.Evaluation.Concerns = avaliacao.Concerns;
                            existente.Evaluation.DetectedTags = avaliacao.DetectedTags;
                            existente.Evaluation.IsRecommended = avaliacao.IsRecommended;
                        }
                    }
                    else
                    {
                        // Limite rígido de 500 itens para poupar memória e garantir máxima fluidez
                        if (contagemTotalAtual >= 500)
                        {
                            _logger.LogInformation("Limite máximo de 500 itens atingido para o feed {Dominio}. Interrompendo adições.", dominio);
                            break;
                        }

                        db.CuratedItems.Add(itemCurado);
                        totalCurados++;
                        contagemTotalAtual++;
                    }
                }

                await db.SaveChangesAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao varrer loja {StoreUrl}", urlLoja);
            }
        }

        // 4. Invalida cache de feed estático
        cache.Remove($"STATIC_FEED_{dominio.ToUpperInvariant()}");

        // 5. Finaliza o status do garimpo
        lock (_lock)
        {
            _status = new HuntStatusDto(
                isRunning: false,
                domain: dominio,
                lastRun: DateTime.UtcNow.ToString("o"),
                itemsFound: totalEncontrados,
                itemsCurated: totalCurados,
                error: null
            );
        }
    }
}
