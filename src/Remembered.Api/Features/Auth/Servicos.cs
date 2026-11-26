using System.Globalization;
using Remembered.Api.Comum;
using Remembered.Api.Infra.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Remembered.Api.Features.Auth;

public sealed record TokenGerado(string Token, DateTime ExpiraEm);

/// <summary>
/// Emite o JWT HS256 do cuidador: sub, role=CUIDADOR, nome, email e idosos (sempre array JSON,
/// mesmo com 0 ou 1 idoso, para a Java ler a claim do mesmo jeito).
/// </summary>
public sealed class TokenService(IOptions<JwtOptions> opcoes, TimeProvider tempo)
{
    public TokenGerado Gerar(Cuidador cuidador, IEnumerable<long> idosos)
    {
        var o = opcoes.Value;
        var agora = tempo.GetUtcNow().UtcDateTime;
        var expira = agora.AddHours(o.ExpiracaoHoras);

        var descritor = new SecurityTokenDescriptor
        {
            Issuer = o.Issuer,
            Audience = o.Audience,
            IssuedAt = agora,
            NotBefore = agora,
            Expires = expira,
            SigningCredentials = new SigningCredentials(o.ChaveSimetrica(), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [TiposClaim.Sub] = cuidador.Id.ToString(CultureInfo.InvariantCulture),
                [TiposClaim.Papel] = Papeis.Cuidador,
                [TiposClaim.Nome] = cuidador.Nome,
                [TiposClaim.Email] = cuidador.Email,
                [TiposClaim.Idosos] = ClaimIdosos(idosos),
            },
        };
        return new TokenGerado(new JsonWebTokenHandler().CreateToken(descritor), expira);
    }

    /// <summary>Ids únicos e ordenados.</summary>
    public static long[] ClaimIdosos(IEnumerable<long> idosos) => idosos.Where(i => i > 0).Distinct().Order().ToArray();
}

/// <summary>Hash PBKDF2 do ASP.NET Core Identity (PasswordHasher).</summary>
public sealed class SenhaService
{
    private static readonly PasswordHasher<Cuidador> Hasher = new();
    private static readonly string HashFalso = Hasher.HashPassword(new Cuidador(), "senha-que-ninguem-usa");

    public string Hash(string senha) => Hasher.HashPassword(new Cuidador(), senha);

    public bool Verificar(string hash, string senha) =>
        Hasher.VerifyHashedPassword(new Cuidador(), hash, senha) is PasswordVerificationResult.Success
            or PasswordVerificationResult.SuccessRehashNeeded;

    /// <summary>Gasta o mesmo tempo de uma verificação real quando o e-mail não existe.</summary>
    public void VerificarFalso(string senha) => Verificar(HashFalso, senha);
}
