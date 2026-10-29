using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using UrubuApi.Models;

namespace UrubuApi.Services.Security;

public interface IServicoSeguranca
{
    string CriarHashSenha(string senha);
    bool VerificarSenha(string senha, string hashArmazenado);
    string GerarTokenJwt(Usuario usuario);
}

public class ServicoSeguranca : IServicoSeguranca
{
    private const int TamanhoSalt = 16;
    private const int TamanhoHash = 32;
    private const int Iteracoes = 100_000;
    private static readonly HashAlgorithmName Algoritmo = HashAlgorithmName.SHA512;

    private readonly string _chaveJwt;
    private readonly string _emissor;
    private readonly string _audiencia;

    public ServicoSeguranca(IConfiguration config)
    {
        // Chave secreta de no mínimo 256 bits (32 caracteres)
        _chaveJwt = config["Jwt:SecretKey"] ?? "UrubuHunterSuperSecretKey2026_UltraSecureJwtTokenSigner_32chars!";
        _emissor = config["Jwt:Issuer"] ?? "UrubuHunterApi";
        _audiencia = config["Jwt:Audience"] ?? "UrubuHunterWeb";
    }

    public string CriarHashSenha(string senha)
    {
        var salt = RandomNumberGenerator.GetBytes(TamanhoSalt);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            senha,
            salt,
            Iteracoes,
            Algoritmo,
            TamanhoHash
        );

        return $"{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }

    public bool VerificarSenha(string senha, string hashArmazenado)
    {
        if (string.IsNullOrWhiteSpace(hashArmazenado) || !hashArmazenado.Contains(':'))
            return false;

        var partes = hashArmazenado.Split(':');
        if (partes.Length != 2)
            return false;

        try
        {
            var salt = Convert.FromBase64String(partes[0]);
            var hashOriginal = Convert.FromBase64String(partes[1]);

            var hashTentativa = Rfc2898DeriveBytes.Pbkdf2(
                senha,
                salt,
                Iteracoes,
                Algoritmo,
                TamanhoHash
            );

            return CryptographicOperations.FixedTimeEquals(hashOriginal, hashTentativa);
        }
        catch
        {
            return false;
        }
    }

    public string GerarTokenJwt(Usuario usuario)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_chaveJwt));
        var credenciais = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, usuario.Id),
            new Claim(JwtRegisteredClaimNames.Email, usuario.Email),
            new Claim("nome", usuario.Nome),
            new Claim("whatsapp", usuario.Whatsapp ?? string.Empty),
            new Claim(ClaimTypes.Role, usuario.Role ?? "Usuario"),
            new Claim("role", usuario.Role ?? "Usuario"),
            new Claim("ativo", usuario.Ativo.ToString()),
            new Claim("politicas", usuario.Politicas ?? string.Empty),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (string.Equals(usuario.Role, "Admin", StringComparison.OrdinalIgnoreCase) ||
            (usuario.Politicas?.Contains("GerenciamentoUsuarios") == true))
        {
            claims.Add(new Claim("politica", "GerenciamentoUsuarios"));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddDays(7),
            Issuer = _emissor,
            Audience = _audiencia,
            SigningCredentials = credenciais
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(tokenDescriptor);
        return handler.WriteToken(token);
    }
}
