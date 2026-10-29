using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using UrubuApi.Data;
using UrubuApi.Endpoints;
using UrubuApi.Services;
using UrubuApi.Services.Background;
using UrubuApi.Services.Crawlers;
using UrubuApi.Services.Crawlers.Extractors;
using UrubuApi.Services.Crawlers.Http;
using UrubuApi.Services.Evaluator;
using UrubuApi.Services.Orchestrator;
using UrubuApi.Services.Security;

var builder = WebApplication.CreateBuilder(args);

// 1. Configuração do SQLite com EF Core Factory (Thread-safe para BackgroundServices e HTTP)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=urubu.db";
builder.Services.AddDbContextFactory<DBContext>(options =>
    options.UseSqlite(connectionString));

// 2. Cache em memória para o Snapshot Estático
builder.Services.AddMemoryCache();

// 3. HttpClientFactory resiliente com suporte a descompressão (GZip, Deflate, Brotli)
builder.Services.AddHttpClient("", client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
    client.Timeout = TimeSpan.FromSeconds(10);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = DecompressionMethods.All,
    AllowAutoRedirect = true
});

// 4. Segurança & Autenticação JWT
var chaveJwt = builder.Configuration["Jwt:SecretKey"] ?? "UrubuHunterSuperSecretKey2026_UltraSecureJwtTokenSigner_32chars!";
var emissor = builder.Configuration["Jwt:Issuer"] ?? "UrubuHunterApi";
var audiencia = builder.Configuration["Jwt:Audience"] ?? "UrubuHunterWeb";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = emissor,
            ValidateAudience = true,
            ValidAudience = audiencia,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chaveJwt))
        };
    });

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("GerenciamentoUsuarios", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == "Admin") ||
            ctx.User.HasClaim(c => c.Type == "role" && c.Value == "Admin") ||
            ctx.User.HasClaim(c => c.Type == "politica" && c.Value == "GerenciamentoUsuarios") ||
            ctx.User.HasClaim(c => c.Type == "politicas" && c.Value.Contains("GerenciamentoUsuarios"))
        ));
});
builder.Services.AddSingleton<IServicoSeguranca, ServicoSeguranca>();

// 5. Injeção de Dependências - Camada de Coletores e Extração (SOLID / Clean Architecture)
builder.Services.AddSingleton<IClienteHttpColetor, ClienteHttpColetor>();
builder.Services.AddSingleton<ExtratorShopify>();
builder.Services.AddSingleton<ExtratorSitemap>();
builder.Services.AddSingleton<ExtratorJsonLd>();
builder.Services.AddSingleton<ExtratorOpenGraph>();
builder.Services.AddSingleton<ExtratorCardsHtml>();

builder.Services.AddScoped<IServicoFeed, ServicoFeed>();
builder.Services.AddSingleton<ColetorUniversal>();
builder.Services.AddSingleton<ColetorShopify>();
builder.Services.AddSingleton<ColetorMercadoLivre>();
builder.Services.AddSingleton<IColetorSite>(sp => sp.GetRequiredService<ColetorMercadoLivre>());
builder.Services.AddSingleton<IColetorSite>(sp => sp.GetRequiredService<ColetorUniversal>());
builder.Services.AddSingleton<CatalogoColetores>();
builder.Services.AddSingleton<IAvaliadorEstilo, AvaliadorEstilo>();
builder.Services.AddSingleton<IOrquestradorGarimpo, OrquestradorGarimpo>();

// 6. Monitoramento de Preços 24/7 em Background
builder.Services.AddHostedService<ServicoMonitoramentoBackground>();

// 7. OpenAPI / Swagger
builder.Services.AddOpenApi();

// 8. CORS amigável para o Next.js (localhost:3000) e produção na Vercel
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
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<DBContext>>();
    using var db = factory.CreateDbContext();
    db.Database.EnsureCreated();

    // Garantir criação de tabelas novas em bancos já existentes sem recriar o arquivo
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS ""Usuarios"" (
            ""Id"" TEXT NOT NULL CONSTRAINT ""PK_Usuarios"" PRIMARY KEY,
            ""Nome"" TEXT NOT NULL,
            ""Email"" TEXT NOT NULL,
            ""Whatsapp"" TEXT NOT NULL,
            ""SenhaHash"" TEXT NOT NULL,
            ""CriadoEm"" TEXT NOT NULL,
            ""Ativo"" INTEGER NOT NULL DEFAULT 0,
            ""Role"" TEXT NOT NULL DEFAULT 'Usuario',
            ""Politicas"" TEXT NOT NULL DEFAULT ''
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Usuarios_Email"" ON ""Usuarios"" (""Email"");
    ");

    // Adiciona colunas caso a tabela ja existisse em versões anteriores
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Usuarios"" ADD COLUMN ""Ativo"" INTEGER NOT NULL DEFAULT 0;"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Usuarios"" ADD COLUMN ""Role"" TEXT NOT NULL DEFAULT 'Usuario';"); } catch { }
    try { db.Database.ExecuteSqlRaw(@"ALTER TABLE ""Usuarios"" ADD COLUMN ""Politicas"" TEXT NOT NULL DEFAULT '';"); } catch { }

    // Garante que os administradores iniciais estejam Ativos e com Role = Admin
    db.Database.ExecuteSqlRaw(@"
        UPDATE ""Usuarios"" 
        SET ""Ativo"" = 1, ""Role"" = 'Admin', ""Politicas"" = 'GerenciamentoUsuarios'
        WHERE ""Email"" LIKE '%bernardo%' OR ""Id"" IN (SELECT ""Id"" FROM ""Usuarios"" ORDER BY ""CriadoEm"" ASC LIMIT 1);
    ");
}

// Pipeline HTTP
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("NextJsPolicy");
app.UseMiddleware<ApiKeyMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

// Mapeamento dos endpoints Minimal APIs
app.MapearEndpointsFeed();
app.MapearEndpointsAutenticacao();

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
