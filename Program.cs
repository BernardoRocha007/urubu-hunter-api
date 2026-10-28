using System.Net;
using Microsoft.EntityFrameworkCore;
using UrubuApi.Data;
using UrubuApi.Endpoints;
using UrubuApi.Services;
using UrubuApi.Services.Crawlers;
using UrubuApi.Services.Crawlers.Extractors;
using UrubuApi.Services.Crawlers.Http;
using UrubuApi.Services.Evaluator;
using UrubuApi.Services.Orchestrator;

var builder = WebApplication.CreateBuilder(args);

// 1. Configuração do SQLite com EF Core Factory (Thread-safe para BackgroundServices e HTTP)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=urubu.db";
builder.Services.AddDbContextFactory<UrubuDbContext>(options =>
    options.UseSqlite(connectionString));

// 2. Cache em memória para o Snapshot Estático
builder.Services.AddMemoryCache();

// 3. HttpClientFactory resiliente com suporte transparente a descompressão (GZip, Deflate, Brotli)
builder.Services.AddHttpClient("", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
    client.Timeout = TimeSpan.FromSeconds(10);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = DecompressionMethods.All,
    AllowAutoRedirect = true
});

// 4. Injeção de Dependências - Camada de Crawlers e Extração (SOLID / Clean Architecture)
builder.Services.AddSingleton<ICrawlerHttpClient, CrawlerHttpClient>();
builder.Services.AddSingleton<ShopifyExtractor>();
builder.Services.AddSingleton<SitemapExtractor>();
builder.Services.AddSingleton<JsonLdExtractor>();
builder.Services.AddSingleton<OpenGraphExtractor>();
builder.Services.AddSingleton<HtmlCardExtractor>();

builder.Services.AddScoped<IFeedService, FeedService>();
builder.Services.AddSingleton<UniversalScraper>();
builder.Services.AddSingleton<ShopifyScraper>();
builder.Services.AddSingleton<MercadoLivreScraper>();
builder.Services.AddSingleton<ISiteScraper>(sp => sp.GetRequiredService<MercadoLivreScraper>());
builder.Services.AddSingleton<ISiteScraper>(sp => sp.GetRequiredService<UniversalScraper>());
builder.Services.AddSingleton<ScraperRegistry>();
builder.Services.AddSingleton<IStyleEvaluator, StyleEvaluator>();
builder.Services.AddSingleton<IHunterOrchestrator, HunterOrchestrator>();

// 5. OpenAPI / Swagger
builder.Services.AddOpenApi();

// 6. CORS amigável para o Next.js (localhost:3000) e desenvolvimento
builder.Services.AddCors(options =>
{
    options.AddPolicy("NextJsPolicy", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Garantir que o banco SQLite seja criado e populado na inicialização
using (var scope = app.Services.CreateScope())
{
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<UrubuDbContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();
}

// Pipeline HTTP
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("NextJsPolicy");

// Mapeamento dos endpoints Minimal APIs
app.MapFeedEndpoints();

// Health check endpoint
app.MapGet("/api/health", () => Results.Ok(new
{
    status = "healthy",
    framework = ".NET 10 ASP.NET Core",
    timestamp = DateTime.UtcNow
}))
.WithName("HealthCheck")
.WithSummary("Verifica a saúde da API.");

app.Run();
