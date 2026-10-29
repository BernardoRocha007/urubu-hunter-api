using Microsoft.EntityFrameworkCore;
using UrubuApi.Data;
using UrubuApi.Services.Crawlers;

namespace UrubuApi.Services.Background;

/// <summary>
/// BackgroundService que executa 24/7 no servidor C# monitorando periodicamente
/// os itens favoritados e monitorados na Watchlist para detectar quedas de preço e promoções.
/// </summary>
public class ServicoMonitoramentoBackground : BackgroundService
{
    private readonly IDbContextFactory<DBContext> _fabricaDb;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ServicoMonitoramentoBackground> _logger;
    private readonly TimeSpan _intervalo = TimeSpan.FromMinutes(30);

    public ServicoMonitoramentoBackground(
        IDbContextFactory<DBContext> fabricaDb,
        IServiceProvider serviceProvider,
        ILogger<ServicoMonitoramentoBackground> logger)
    {
        _fabricaDb = fabricaDb;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Serviço de Monitoramento 24/7 em Background iniciado.");

        // Aguarda 1 minuto após o start da API antes da primeira verificação
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ChecarPrecosAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Erro durante ciclo de monitoramento em background.");
            }

            await Task.Delay(_intervalo, stoppingToken);
        }

        _logger.LogInformation("Serviço de Monitoramento em Background encerrado.");
    }

    private async Task ChecarPrecosAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var coletorUniversal = scope.ServiceProvider.GetRequiredService<ColetorUniversal>();

        using var db = await _fabricaDb.CreateDbContextAsync(ct);

        // 1. Obtém itens marcados como 'liked' (favoritos)
        var itensFavoritados = await db.CuratedItems
            .Where(x => x.Feedback == "liked")
            .Take(100)
            .ToListAsync(ct);

        _logger.LogInformation("Monitorando {Count} peças curtidas pelos usuários...", itensFavoritados.Count);

        var quedasDetectadas = 0;

        foreach (var item in itensFavoritados)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                var produtoAtualizado = await coletorUniversal.VarrerProdutoAsync(item.Url, ct);
                if (produtoAtualizado != null && produtoAtualizado.Price > 0)
                {
                    if (produtoAtualizado.Price < item.Price)
                    {
                        var economia = item.Price - produtoAtualizado.Price;
                        _logger.LogInformation(
                            "🎉 QUEDA DE PREÇO DETECTADA! Item '{Titulo}': de R$ {Antes} por R$ {Agora} (Economia de R$ {Eco})",
                            item.Title, item.Price, produtoAtualizado.Price, economia
                        );

                        item.OriginalPrice = item.Price;
                        item.Price = produtoAtualizado.Price;
                        item.DiscountPct = produtoAtualizado.DiscountPct ?? (int)Math.Round(((item.OriginalPrice.Value - item.Price) / item.OriginalPrice.Value) * 100m);
                        quedasDetectadas++;
                    }
                }

                // Pequeno espaçamento de 500ms entre checagens para não sobrecarregar as lojas
                await Task.Delay(500, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Falha ao checar preço da peça {Id}: {Msg}", item.Id, ex.Message);
            }
        }

        if (quedasDetectadas > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("Ciclo concluído: {Count} promoções/quedas registradas no banco.", quedasDetectadas);
        }
    }
}
