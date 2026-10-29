using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using UrubuApi.Data;
using UrubuApi.Models;
using UrubuApi.Services.Security;

namespace UrubuApi.Endpoints;

public record RegistroUsuarioRequest(string Nome, string Email, string Whatsapp, string Senha);
public record LoginUsuarioRequest(string Email, string Senha);
public record CriarUsuarioAdminRequest(string Nome, string Email, string Whatsapp, string Senha, string? Role, string? Politicas);
public record AlternarStatusUsuarioRequest(bool Ativo);
public record UsuarioResposta(string Id, string Nome, string Email, string Whatsapp, bool Ativo, string Role, string Politicas, DateTime CriadoEm);
public record AuthResposta(string Token, UsuarioResposta Usuario);
public record AtualizarPerfilRequest(string? Nome, string? Whatsapp);

public static class EndpointsAutenticacao
{
    public static RouteGroupBuilder MapearEndpointsAutenticacao(this WebApplication app)
    {
        var grupo = app.MapGroup("/api/auth");

        // 1. POST /api/auth/register (Cadastro público com status pendente)
        grupo.MapPost("/register", async (
            [FromBody] RegistroUsuarioRequest req,
            [FromServices] IDbContextFactory<DBContext> fabricaDb,
            [FromServices] IServicoSeguranca servicoSeguranca,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Nome) || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Senha))
            {
                return Results.BadRequest(new { error = "Nome, e-mail e senha são obrigatórios." });
            }

            if (req.Senha.Length < 6)
            {
                return Results.BadRequest(new { error = "A senha deve conter no mínimo 6 caracteres." });
            }

            var emailNormalizado = req.Email.Trim().ToLowerInvariant();

            using var db = await fabricaDb.CreateDbContextAsync(ct);
            var jaExiste = await db.Usuarios.AnyAsync(u => u.Email == emailNormalizado, ct);
            if (jaExiste)
            {
                return Results.Conflict(new { error = "Já existe uma conta cadastrada com este e-mail." });
            }

            var novoUsuario = new Usuario
            {
                Nome = req.Nome.Trim(),
                Email = emailNormalizado,
                Whatsapp = req.Whatsapp?.Trim() ?? string.Empty,
                SenhaHash = servicoSeguranca.CriarHashSenha(req.Senha),
                Ativo = false, // Pendente de liberação pelo Administrador
                Role = "Usuario",
                Politicas = string.Empty,
                CriadoEm = DateTime.UtcNow
            };

            db.Usuarios.Add(novoUsuario);
            await db.SaveChangesAsync(ct);

            var resposta = new AuthResposta(
                string.Empty,
                new UsuarioResposta(
                    novoUsuario.Id,
                    novoUsuario.Nome,
                    novoUsuario.Email,
                    novoUsuario.Whatsapp,
                    novoUsuario.Ativo,
                    novoUsuario.Role,
                    novoUsuario.Politicas,
                    novoUsuario.CriadoEm
                )
            );

            return Results.Created($"/api/auth/me", resposta);
        })
        .WithName("RegistrarUsuario")
        .WithDescription("Cadastra um novo usuário com senha protegida por PBKDF2 e status pendente de liberação.");

        // 2. POST /api/auth/login
        grupo.MapPost("/login", async (
            [FromBody] LoginUsuarioRequest req,
            [FromServices] IDbContextFactory<DBContext> fabricaDb,
            [FromServices] IServicoSeguranca servicoSeguranca,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Senha))
            {
                return Results.BadRequest(new { error = "E-mail e senha são obrigatórios." });
            }

            var emailNormalizado = req.Email.Trim().ToLowerInvariant();

            using var db = await fabricaDb.CreateDbContextAsync(ct);
            var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Email == emailNormalizado, ct);
            if (usuario == null)
            {
                return Results.Unauthorized();
            }

            var senhaValida = servicoSeguranca.VerificarSenha(req.Senha, usuario.SenhaHash);
            if (!senhaValida)
            {
                return Results.Unauthorized();
            }

            // Se o usuário ainda não foi ativado/aprovado pelo administrador, bloqueia a entrada com mensagem clara
            if (!usuario.Ativo)
            {
                return Results.Json(new
                {
                    error = "Sua conta foi criada, mas ainda está pendente de liberação pelo Administrador.",
                    code = "USUARIO_PENDENTE"
                }, statusCode: StatusCodes.Status403Forbidden);
            }

            var token = servicoSeguranca.GerarTokenJwt(usuario);
            var resposta = new AuthResposta(
                token,
                new UsuarioResposta(
                    usuario.Id,
                    usuario.Nome,
                    usuario.Email,
                    usuario.Whatsapp,
                    usuario.Ativo,
                    usuario.Role,
                    usuario.Politicas,
                    usuario.CriadoEm
                )
            );

            return Results.Ok(resposta);
        })
        .WithName("LoginUsuario")
        .WithDescription("Autentica o usuário e retorna o token JWT caso esteja ativo.");

        // 3. GET /api/auth/me
        grupo.MapGet("/me", async (
            ClaimsPrincipal principal,
            [FromServices] IDbContextFactory<DBContext> fabricaDb,
            CancellationToken ct) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            using var db = await fabricaDb.CreateDbContextAsync(ct);
            var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (usuario == null)
            {
                return Results.NotFound(new { error = "Usuário não encontrado." });
            }

            return Results.Ok(new UsuarioResposta(
                usuario.Id,
                usuario.Nome,
                usuario.Email,
                usuario.Whatsapp,
                usuario.Ativo,
                usuario.Role,
                usuario.Politicas,
                usuario.CriadoEm
            ));
        })
        .RequireAuthorization()
        .WithName("ObterUsuarioAutenticado")
        .WithDescription("Retorna as informações do usuário associado ao token JWT enviado.");

        // 4. POST /api/auth/test-notification
        grupo.MapPost("/test-notification", async (
            ClaimsPrincipal principal,
            [FromServices] IDbContextFactory<DBContext> fabricaDb,
            [FromServices] ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            using var db = await fabricaDb.CreateDbContextAsync(ct);
            var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (usuario == null)
            {
                return Results.NotFound(new { error = "Usuário não encontrado." });
            }

            var logger = loggerFactory.CreateLogger("Notificacoes");
            var mensagem = $"🦅 [URABU HUNTER] Olá {usuario.Nome}! Este é um teste de alerta de promoção. Quando um item que você curtiu baixar de preço, você receberá um alerta automático!";
            var telefoneNumeros = new string((usuario.Whatsapp ?? string.Empty).Where(char.IsDigit).ToArray());
            var whatsappUrl = !string.IsNullOrEmpty(telefoneNumeros)
                ? $"https://api.whatsapp.com/send?phone=55{telefoneNumeros.TrimStart('0')}&text={Uri.EscapeDataString(mensagem)}"
                : string.Empty;

            logger.LogInformation("🔔 [TESTE DE NOTIFICAÇÃO] Disparado para {Nome} ({Email}) | WhatsApp: {Whatsapp}",
                usuario.Nome, usuario.Email, usuario.Whatsapp);

            return Results.Ok(new
            {
                sucesso = true,
                mensagem = "Notificação de teste gerada com sucesso!",
                detalhe = mensagem,
                destinatario = string.IsNullOrEmpty(usuario.Whatsapp) ? usuario.Email : usuario.Whatsapp,
                whatsappUrl,
                enviadoEm = DateTime.UtcNow
            });
        })
        .RequireAuthorization()
        .WithName("TestarNotificacao")
        .WithDescription("Dispara um teste de notificação de alerta para o usuário autenticado.");

        // 5. PUT /api/auth/profile
        grupo.MapPut("/profile", async (
            [FromBody] AtualizarPerfilRequest req,
            ClaimsPrincipal principal,
            [FromServices] IDbContextFactory<DBContext> fabricaDb,
            CancellationToken ct) =>
        {
            var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userId))
            {
                return Results.Unauthorized();
            }

            using var db = await fabricaDb.CreateDbContextAsync(ct);
            var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == userId, ct);
            if (usuario == null)
            {
                return Results.NotFound(new { error = "Usuário não encontrado." });
            }

            if (!string.IsNullOrWhiteSpace(req.Nome))
                usuario.Nome = req.Nome.Trim();

            if (req.Whatsapp != null)
                usuario.Whatsapp = req.Whatsapp.Trim();

            await db.SaveChangesAsync(ct);

            return Results.Ok(new UsuarioResposta(
                usuario.Id,
                usuario.Nome,
                usuario.Email,
                usuario.Whatsapp,
                usuario.Ativo,
                usuario.Role,
                usuario.Politicas,
                usuario.CriadoEm
            ));
        })
        .RequireAuthorization()
        .WithName("AtualizarPerfil")
        .WithDescription("Atualiza informações cadastrais do usuário autenticado.");

        // ==========================================
        // ROTAS ADMINISTRATIVAS - GESTÃO DE USUÁRIOS
        // Protegidas pela política "GerenciamentoUsuarios"
        // ==========================================
        var adminGrupo = app.MapGroup("/api/admin/usuarios").RequireAuthorization("GerenciamentoUsuarios");

        // 6. GET /api/admin/usuarios - Lista todos os usuários
        adminGrupo.MapGet("", async (
            [FromServices] IDbContextFactory<DBContext> fabricaDb,
            CancellationToken ct) =>
        {
            using var db = await fabricaDb.CreateDbContextAsync(ct);
            var usuarios = await db.Usuarios
                .AsNoTracking()
                .OrderByDescending(u => u.CriadoEm)
                .Select(u => new UsuarioResposta(
                    u.Id,
                    u.Nome,
                    u.Email,
                    u.Whatsapp,
                    u.Ativo,
                    u.Role,
                    u.Politicas,
                    u.CriadoEm
                ))
                .ToListAsync(ct);

            return Results.Ok(usuarios);
        })
        .WithName("AdminListarUsuarios")
        .WithDescription("Lista todos os usuários cadastrados no sistema (Requer política GerenciamentoUsuarios).");

        // 7. POST /api/admin/usuarios - ADM cria usuário (nasce inativo / pendente)
        adminGrupo.MapPost("", async (
            [FromBody] CriarUsuarioAdminRequest req,
            [FromServices] IDbContextFactory<DBContext> fabricaDb,
            [FromServices] IServicoSeguranca servicoSeguranca,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Nome) || string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Senha))
            {
                return Results.BadRequest(new { error = "Nome, e-mail e senha são obrigatórios." });
            }

            if (req.Senha.Length < 6)
            {
                return Results.BadRequest(new { error = "A senha deve conter no mínimo 6 caracteres." });
            }

            var emailNormalizado = req.Email.Trim().ToLowerInvariant();

            using var db = await fabricaDb.CreateDbContextAsync(ct);
            var jaExiste = await db.Usuarios.AnyAsync(u => u.Email == emailNormalizado, ct);
            if (jaExiste)
            {
                return Results.Conflict(new { error = "Já existe uma conta cadastrada com este e-mail." });
            }

            var novoUsuario = new Usuario
            {
                Nome = req.Nome.Trim(),
                Email = emailNormalizado,
                Whatsapp = req.Whatsapp?.Trim() ?? string.Empty,
                SenhaHash = servicoSeguranca.CriarHashSenha(req.Senha),
                Ativo = false, // Conforme exigido: ao criar pelo ADM, NÃO colocar no sistema com permissão para logar
                Role = string.IsNullOrWhiteSpace(req.Role) ? "Usuario" : req.Role.Trim(),
                Politicas = req.Politicas?.Trim() ?? string.Empty,
                CriadoEm = DateTime.UtcNow
            };

            db.Usuarios.Add(novoUsuario);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/admin/usuarios/{novoUsuario.Id}", new UsuarioResposta(
                novoUsuario.Id,
                novoUsuario.Nome,
                novoUsuario.Email,
                novoUsuario.Whatsapp,
                novoUsuario.Ativo,
                novoUsuario.Role,
                novoUsuario.Politicas,
                novoUsuario.CriadoEm
            ));
        })
        .WithName("AdminCriarUsuario")
        .WithDescription("Cria um novo usuário pelo Admin com status pendente (sem permissão inicial de login). Requer política GerenciamentoUsuarios.");

        // 8. PATCH /api/admin/usuarios/{id}/status - Alterna status (Adiciona ao sistema / Desativa)
        adminGrupo.MapPatch("/{id}/status", async (
            string id,
            [FromBody] AlternarStatusUsuarioRequest req,
            [FromServices] IDbContextFactory<DBContext> fabricaDb,
            CancellationToken ct) =>
        {
            using var db = await fabricaDb.CreateDbContextAsync(ct);
            var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (usuario == null)
            {
                return Results.NotFound(new { error = "Usuário não encontrado." });
            }

            usuario.Ativo = req.Ativo;
            await db.SaveChangesAsync(ct);

            return Results.Ok(new UsuarioResposta(
                usuario.Id,
                usuario.Nome,
                usuario.Email,
                usuario.Whatsapp,
                usuario.Ativo,
                usuario.Role,
                usuario.Politicas,
                usuario.CriadoEm
            ));
        })
        .WithName("AdminAlternarStatusUsuario")
        .WithDescription("Adiciona ou remove o usuário do sistema (permite ou revoga permissão de login). Requer política GerenciamentoUsuarios.");

        // 9. DELETE /api/admin/usuarios/{id} - Exclui usuário
        adminGrupo.MapDelete("/{id}", async (
            string id,
            ClaimsPrincipal principal,
            [FromServices] IDbContextFactory<DBContext> fabricaDb,
            CancellationToken ct) =>
        {
            var adminLogadoId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
            if (adminLogadoId == id)
            {
                return Results.BadRequest(new { error = "Você não pode excluir sua própria conta de administrador." });
            }

            using var db = await fabricaDb.CreateDbContextAsync(ct);
            var usuario = await db.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct);
            if (usuario == null)
            {
                return Results.NotFound(new { error = "Usuário não encontrado." });
            }

            db.Usuarios.Remove(usuario);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { sucesso = true, mensagem = "Usuário removido com sucesso." });
        })
        .WithName("AdminExcluirUsuario")
        .WithDescription("Exclui um usuário do sistema. Requer política GerenciamentoUsuarios.");

        return grupo;
    }
}
