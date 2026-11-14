using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;

namespace CuidarDePerto.Api.Comum;

public static class Papeis
{
    public const string Cuidador = "CUIDADOR";
    public const string Idoso = "IDOSO";
}

public static class Politicas
{
    public const string Cuidador = "Cuidador";
    public const string CuidadorVinculado = "CuidadorVinculado";
}

public static class TiposClaim
{
    public const string Sub = "sub";
    public const string Papel = "role";
    public const string Nome = "nome";
    public const string Email = "email";
    public const string Idosos = "idosos";
}

/// <summary>Chave, Issuer e Audience comuns às APIs Java e .NET.</summary>
public sealed class JwtOptions
{
    public const string Secao = "Jwt";
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "cuidar-de-perto-dotnet";
    public string Audience { get; set; } = "cuidar-de-perto";
    /// <summary>Emissores aceitos além do próprio (o da API Java).</summary>
    public string[] IssuersAceitos { get; set; } = [];
    public int ExpiracaoHoras { get; set; } = 24;

    public SymmetricSecurityKey ChaveSimetrica() => new(Encoding.UTF8.GetBytes(Key));

    public IEnumerable<string> TodosIssuers() =>
        IssuersAceitos.Append(Issuer).Where(i => !string.IsNullOrWhiteSpace(i)).Distinct();
}

public static class UsuarioExtensions
{
    public static long UsuarioId(this ClaimsPrincipal u) =>
        long.TryParse(u.FindFirstValue(TiposClaim.Sub), out var id)
            ? id
            : throw new InvalidOperationException("Token sem claim sub numérica.");

    public static string Papel(this ClaimsPrincipal u) => u.FindFirstValue(TiposClaim.Papel) ?? "";

    public static string NomeUsuario(this ClaimsPrincipal u) => u.FindFirstValue(TiposClaim.Nome) ?? "";

    /// <summary>Identifica o usuário entre os dois emissores, por exemplo "CUIDADOR:7".</summary>
    public static string ChaveUsuario(this ClaimsPrincipal u) => $"{u.Papel()}:{u.FindFirstValue(TiposClaim.Sub)}";

    public static IReadOnlySet<long> IdososVinculados(this ClaimsPrincipal u) =>
        u.FindAll(TiposClaim.Idosos)
            .Select(c => long.TryParse(c.Value, out var v) ? v : 0)
            .Where(v => v > 0)
            .ToHashSet();
}

/// <summary>Regra de vínculo: CUIDADOR acessa os idosos da claim idosos; IDOSO acessa só o próprio id.</summary>
public static class AcessoIdoso
{
    public static bool PodeAcessar(ClaimsPrincipal usuario, long idosoId)
    {
        if (idosoId <= 0) return false;
        return usuario.Papel() switch
        {
            Papeis.Cuidador => usuario.IdososVinculados().Contains(idosoId),
            Papeis.Idoso => long.TryParse(usuario.FindFirstValue(TiposClaim.Sub), out var id) && id == idosoId,
            _ => false,
        };
    }
}

public sealed class CuidadorVinculadoRequirement : IAuthorizationRequirement;

/// <summary>Lê o {idosoId} da rota e aplica <see cref="AcessoIdoso"/>.</summary>
public sealed class CuidadorVinculadoHandler : AuthorizationHandler<CuidadorVinculadoRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, CuidadorVinculadoRequirement requirement)
    {
        if (context.Resource is HttpContext http
            && long.TryParse(http.GetRouteValue("idosoId")?.ToString(), out var idosoId)
            && AcessoIdoso.PodeAcessar(context.User, idosoId))
        {
            context.Succeed(requirement);
        }
        return Task.CompletedTask;
    }
}
