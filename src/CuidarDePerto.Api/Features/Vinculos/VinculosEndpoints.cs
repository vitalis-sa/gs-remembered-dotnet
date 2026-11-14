using System.Security.Claims;
using CuidarDePerto.Api.Comum;
using CuidarDePerto.Api.Features.Auth;
using CuidarDePerto.Api.Infra.Data;
using CuidarDePerto.Api.Infra.Java;
using Microsoft.EntityFrameworkCore;

namespace CuidarDePerto.Api.Features.Vinculos;

public sealed record VinculoRequest(string? Codigo, string? Parentesco) : IValidavel
{
    public IDictionary<string, string[]> Validar() => new Validador()
        .Obrigatorio("codigo", Codigo, 20)
        .Opcional("parentesco", Parentesco, 60)
        .Erros;
}

public sealed record VinculoResponse(long Id, long CuidadorId, string CuidadorNome, long IdosoId, string IdosoNome, string? Parentesco, DateTime CriadoEm)
{
    public static VinculoResponse De(Vinculo v, string cuidadorNome) =>
        new(v.Id, v.CuidadorId, cuidadorNome, v.IdosoId, v.IdosoNome, v.Parentesco, v.CriadoEm);
}

/// <summary>Devolve o token novo: o antigo não tem o idoso recém-vinculado na claim idosos.</summary>
public sealed record VinculoCriadoResponse(VinculoResponse Vinculo, string Token, DateTime ExpiraEm);

public static class VinculosEndpoints
{
    public static RouteGroupBuilder MapVinculos(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/vinculos").WithTags("Vínculos");
        g.MapPost("", Criar).Validar<VinculoRequest>().RequireAuthorization(Politicas.Cuidador)
            .WithSummary("Vincula o cuidador a um idoso pelo código de pareamento gerado na API Java.")
            .Produces<VinculoCriadoResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status409Conflict).ProducesProblem(StatusCodes.Status422UnprocessableEntity);
        g.MapGet("", Listar).RequireAuthorization()
            .WithSummary("Cuidador: seus idosos. Idoso (token da Java): seus cuidadores.")
            .Produces<List<VinculoResponse>>();
        g.MapDelete("/{id:long}", Excluir).RequireAuthorization()
            .WithSummary("Remove o vínculo. Cuidador ou o próprio idoso podem remover.")
            .Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status404NotFound);
        return api;
    }

    private static async Task<IResult> Criar(VinculoRequest req, ClaimsPrincipal user, HttpContext http, AppDbContext db,
        JavaApiClient java, TokenService tokens, TimeProvider tempo, CancellationToken ct)
    {
        var cuidadorId = user.UsuarioId();
        var cuidador = await db.Cuidadores.FindAsync([cuidadorId], ct)
            ?? throw new NaoEncontradoException("Cuidador não encontrado.");

        var idoso = await java.ValidarPareamentoAsync(req.Codigo!.Trim(), http.Request.Headers.Authorization.ToString(), ct);

        if (await db.Vinculos.AnyAsync(v => v.CuidadorId == cuidadorId && v.IdosoId == idoso.IdosoId, ct))
            throw new ConflitoException("Você já está vinculado a este idoso.");

        var vinculo = new Vinculo
        {
            CuidadorId = cuidadorId,
            IdosoId = idoso.IdosoId,
            IdosoNome = idoso.Nome,
            Parentesco = string.IsNullOrWhiteSpace(req.Parentesco) ? null : req.Parentesco.Trim(),
            CriadoEm = tempo.GetUtcNow().UtcDateTime,
        };
        db.Vinculos.Add(vinculo);
        await db.SaveChangesAsync(ct);

        var resposta = await AuthEndpoints.MontarRespostaAsync(cuidador, db, tokens, ct);
        return Results.Created($"/api/v1/vinculos/{vinculo.Id}",
            new VinculoCriadoResponse(VinculoResponse.De(vinculo, cuidador.Nome), resposta.Token, resposta.ExpiraEm));
    }

    private static async Task<IResult> Listar(ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var id = user.UsuarioId();
        var q = db.Vinculos.AsNoTracking();
        q = user.Papel() switch
        {
            Papeis.Cuidador => q.Where(v => v.CuidadorId == id),
            Papeis.Idoso => q.Where(v => v.IdosoId == id),
            _ => q.Where(_ => false),
        };
        var itens = await q.OrderBy(v => v.CriadoEm)
            .Select(v => new VinculoResponse(v.Id, v.CuidadorId, v.Cuidador!.Nome, v.IdosoId, v.IdosoNome, v.Parentesco, v.CriadoEm))
            .ToListAsync(ct);
        return Results.Ok(itens);
    }

    private static async Task<IResult> Excluir(long id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var usuarioId = user.UsuarioId();
        var vinculo = await db.Vinculos.FindAsync([id], ct);
        var dono = vinculo is not null && user.Papel() switch
        {
            Papeis.Cuidador => vinculo.CuidadorId == usuarioId,
            Papeis.Idoso => vinculo.IdosoId == usuarioId,
            _ => false,
        };
        if (!dono) throw new NaoEncontradoException("Vínculo não encontrado.");

        db.Vinculos.Remove(vinculo!);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }
}
