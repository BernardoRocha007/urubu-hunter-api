using Microsoft.EntityFrameworkCore;
using UrubuApi.Data;
using UrubuApi.Endpoints;
using UrubuApi.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. ConfiguraÃ§Ã£o do SQLite com EF Core Factory (Thread-safe para BackgroundServices e HTTP)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=urubu.db";
builder.Services.AddDbContextFactory<UrubuDbContext>(options =>
    options.UseSqlite(connectionString));

// 2. Cache em memÃ³ria para o Snapshot EstÃ¡tico
builder.Services.AddMemoryCache();

// 3. InjeÃ§Ã£o de DependÃªncias
builder.Services.AddScoped<IFeedService, FeedService>();

// 4. OpenAPI / Swagger
builder.Services.AddOpenApi();

// 5. CORS amigÃ¡vel para o Next.js (localhost:3000) e desenvolvimento
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

// Garantir que o banco SQLite seja criado e populado na inicializaÃ§Ã£o
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
.WithSummary("Verifica a saÃºde da API.");

app.Run();
