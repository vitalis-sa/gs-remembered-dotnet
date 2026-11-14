using System.Security.Claims;
using CuidarDePerto.Api.Comum;
using CuidarDePerto.Api.Infra.Data;
using Microsoft.EntityFrameworkCore;

namespace CuidarDePerto.Api.Features.Auth;

public sealed record CadastroRequest(string? Nome, string? Email, string? Senha) : IValidavel
{
    public IDictionary<string, string[]> Validar() => new Validador()
        .Obrigatorio("nome", Nome, 120)
        .Email("email", Email)
        .Regra("senha", Senha is { Length: >= 8 and <= 100 }, "A senha precisa ter de 8 a 100 caracteres.")
        .Erros;
}

public sealed record LoginRequest(string? Email, string? Senha) : IValidavel
{
    public IDictionary<string, string[]> Validar() => new Validador()
        .Obrigatorio("email", Email, 150)
        .Obrigatorio("senha", Senha, 100)
        .Erros;
}

public sealed record PushTokenRequest(string? PushToken) : IValidavel
{
    public IDictionary<string, string[]> Validar() => new Validador().Obrigatorio("pushToken", PushToken, 255).Erros;
}

public sealed record CuidadorResponse(long Id, string Nome, string Email, bool TemPushToken, DateTime CriadoEm)
{
    public static CuidadorResponse De(Cuidador c) => new(c.Id, c.Nome, c.Email, !string.IsNullOrEmpty(c.PushToken), c.CriadoEm);
}

public sealed record AuthResponse(string Token, DateTime ExpiraEm, CuidadorResponse Cuidador, IReadOnlyList<long> Idosos);

public static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuth(this RouteGroupBuilder api)
    {
        var auth = api.MapGroup("/auth").WithTags("Autenticação");
        auth.MapPost("/cadastro", Cadastrar).Validar<CadastroRequest>().AllowAnonymous()
            .Produces<AuthResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);
        auth.MapPost("/login", Entrar).Validar<LoginRequest>().AllowAnonymous()
            .Produces<AuthResponse>().ProducesProblem(StatusCodes.Status401Unauthorized);
        auth.MapPost("/renovar", Renovar).RequireAuthorization(Politicas.Cuidador)
            .WithSummary("Emite um token novo com a lista atual de idosos vinculados.")
            .Produces<AuthResponse>();

        var me = api.MapGroup("/me").WithTags("Autenticação").RequireAuthorization(Politicas.Cuidador);
        me.MapGet("", Perfil).Produces<CuidadorResponse>();
        me.MapPut("/push-token", AtualizarPushToken).Validar<PushTokenRequest>().Produces(StatusCodes.Status204NoContent);
        return api;
    }

    private static async Task<IResult> Cadastrar(CadastroRequest req, AppDbContext db, SenhaService senhas, TokenService tokens, TimeProvider tempo, CancellationToken ct)
    {
        var email = req.Email!.Trim().ToLowerInvariant();
        if (await db.Cuidadores.AnyAsync(c => c.Email == email, ct))
            throw new ConflitoException("Este e-mail já tem uma conta de cuidador.");

        var cuidador = new Cuidador
        {
            Nome = req.Nome!.Trim(),
            Email = email,
            SenhaHash = senhas.Hash(req.Senha!),
            CriadoEm = tempo.GetUtcNow().UtcDateTime,
        };
        db.Cuidadores.Add(cuidador);
        await db.SaveChangesAsync(ct);

        var token = tokens.Gerar(cuidador, []);
        return Results.Created("/api/v1/me", new AuthResponse(token.Token, token.ExpiraEm, CuidadorResponse.De(cuidador), []));
    }

    private static async Task<IResult> Entrar(LoginRequest req, AppDbContext db, SenhaService senhas, TokenService tokens, CancellationToken ct)
    {
        var email = req.Email!.Trim().ToLowerInvariant();
        var cuidador = await db.Cuidadores.FirstOrDefaultAsync(c => c.Email == email, ct);
        if (cuidador is null)
        {
            senhas.VerificarFalso(req.Senha!);
            return CredencialInvalida();
        }
        if (!senhas.Verificar(cuidador.SenhaHash, req.Senha!)) return CredencialInvalida();

        return Results.Ok(await MontarRespostaAsync(cuidador, db, tokens, ct));
    }

    private static async Task<IResult> Renovar(ClaimsPrincipal user, AppDbContext db, TokenService tokens, CancellationToken ct)
    {
        var cuidador = await db.Cuidadores.FindAsync([user.UsuarioId()], ct)
            ?? throw new NaoEncontradoException("Cuidador não encontrado.");
        return Results.Ok(await MontarRespostaAsync(cuidador, db, tokens, ct));
    }

    private static async Task<IResult> Perfil(ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var cuidador = await db.Cuidadores.FindAsync([user.UsuarioId()], ct)
            ?? throw new NaoEncontradoException("Cuidador não encontrado.");
        return Results.Ok(CuidadorResponse.De(cuidador));
    }

    private static async Task<IResult> AtualizarPushToken(PushTokenRequest req, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var cuidador = await db.Cuidadores.FindAsync([user.UsuarioId()], ct)
            ?? throw new NaoEncontradoException("Cuidador não encontrado.");
        cuidador.PushToken = req.PushToken!.Trim();
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    public static async Task<AuthResponse> MontarRespostaAsync(Cuidador cuidador, AppDbContext db, TokenService tokens, CancellationToken ct)
    {
        var idosos = await db.Vinculos.Where(v => v.CuidadorId == cuidador.Id).Select(v => v.IdosoId).ToListAsync(ct);
        var token = tokens.Gerar(cuidador, idosos);
        return new AuthResponse(token.Token, token.ExpiraEm, CuidadorResponse.De(cuidador), TokenService.ClaimIdosos(idosos));
    }

    /// <summary>Mesma mensagem para e-mail inexistente e senha errada.</summary>
    private static IResult CredencialInvalida() =>
        Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Não autorizado", detail: "E-mail ou senha inválidos.");
}
