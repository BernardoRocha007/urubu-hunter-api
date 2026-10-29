using System.Security.Cryptography;
using System.Text;

namespace UrubuApi.Services.Security;

/// <summary>
/// Middleware de proteção perimetral por API Key (Zero Trust).
/// Valida a presença e integridade do cabeçalho X-Api-Key contra as chaves configuradas em ambiente (ALLOWED_API_KEYS).
/// </summary>
public class ApiKeyMiddleware
{
    private const string ApiKeyHeaderName = "X-Api-Key";
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyMiddleware> _logger;
    private readonly HashSet<string> _chavesPermitidas;

    public ApiKeyMiddleware(RequestDelegate next, IConfiguration config, ILogger<ApiKeyMiddleware> logger)
    {
        _next = next;
        _logger = logger;
        _chavesPermitidas = new HashSet<string>(StringComparer.Ordinal);

        // 1. Ler da variável de ambiente ALLOWED_API_KEYS (separada por vírgula)
        var envKeys = config["ALLOWED_API_KEYS"];
        if (!string.IsNullOrWhiteSpace(envKeys))
        {
            foreach (var key in envKeys.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                _chavesPermitidas.Add(key);
            }
        }

        // 2. Ler da seção estruturada ApiKeys caso exista em appsettings
        var configKeys = config.GetSection("ApiKeys").Get<string[]>();
        if (configKeys != null)
        {
            foreach (var key in configKeys)
            {
                if (!string.IsNullOrWhiteSpace(key))
                    _chavesPermitidas.Add(key.Trim());
            }
        }

        // 3. Fallback pontual para BACKEND_API_KEY
        var singleKey = config["BACKEND_API_KEY"] ?? config["ApiKey"];
        if (!string.IsNullOrWhiteSpace(singleKey))
        {
            _chavesPermitidas.Add(singleKey.Trim());
        }

        _logger.LogInformation("ApiKeyMiddleware inicializado com {Count} chave(s) autorizada(s).", _chavesPermitidas.Count);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Health check público para monitoramento de infraestrutura / balanceador
        if (path.Equals("/api/health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Se nenhuma chave foi configurada no ambiente (ex: dev local sem chave configurada), permite para não quebrar dev
        if (_chavesPermitidas.Count == 0)
        {
            await _next(context);
            return;
        }

        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyValue) || string.IsNullOrWhiteSpace(apiKeyValue))
        {
            _logger.LogWarning("Tentativa de acesso não autorizada a {Path} a partir de {Ip}: Header {Header} ausente.",
                path, context.Connection.RemoteIpAddress, ApiKeyHeaderName);

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Acesso negado: Chave de API ausente.",
                code = "API_KEY_MISSING"
            });
            return;
        }

        var incomingKey = apiKeyValue.ToString().Trim();
        var incomingBytes = Encoding.UTF8.GetBytes(incomingKey);
        var isValid = false;

        foreach (var allowedKey in _chavesPermitidas)
        {
            var allowedBytes = Encoding.UTF8.GetBytes(allowedKey);
            if (CryptographicOperations.FixedTimeEquals(incomingBytes, allowedBytes))
            {
                isValid = true;
                break;
            }
        }

        if (!isValid)
        {
            _logger.LogWarning("Tentativa de acesso com chave inválida a {Path} a partir de {Ip}.",
                path, context.Connection.RemoteIpAddress);

            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Acesso negado: Chave de API inválida.",
                code = "API_KEY_INVALID"
            });
            return;
        }

        await _next(context);
    }
}
