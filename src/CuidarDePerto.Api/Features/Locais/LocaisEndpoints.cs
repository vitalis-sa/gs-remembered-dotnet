using System.Security.Claims;
using CuidarDePerto.Api.Comum;
using CuidarDePerto.Api.Infra.Data;
using Microsoft.EntityFrameworkCore;

namespace CuidarDePerto.Api.Features.Locais;

public sealed record LocalRequest(string? Nome, string? Endereco, string? Telefone) : IValidavel
{
    public IDictionary<string, string[]> Validar() => new Validador()
        .Obrigatorio("nome", Nome, 120)
        .Obrigatorio("endereco", Endereco, 255)
        .Opcional("telefone", Telefone, 20)
        .Regra("telefone", Telefone is null || Telefone.All(c => char.IsDigit(c) || c is ' ' or '(' or ')' or '-' or '+'),
            "Use só números, espaço, parênteses, hífen e +.")
        .Erros;
}

public sealed record LocalResponse(long Id, string Nome, string Endereco, string? Telefone, string CriadoPorTipo, long CriadoPorId)
{
    public static LocalResponse De(LocalAtendimento l) => new(l.Id, l.Nome, l.Endereco, l.Telefone, l.CriadoPorTipo, l.CriadoPorId);
}

/// <summary>Catálogo compartilhado de locais. Só quem criou edita ou exclui.</summary>
public static class LocaisEndpoints
{
    public static RouteGroupBuilder MapLocais(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/locais").WithTags("Locais").RequireAuthorization();
        g.MapGet("", Listar).Produces<Pagina<LocalResponse>>();
        g.MapGet("/{id:long}", Obter).Produces<LocalResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        g.MapPost("", Criar).Validar<LocalRequest>().Produces<LocalResponse>(StatusCodes.Status201Created);
        g.MapPut("/{id:long}", Atualizar).Validar<LocalRequest>().Produces<LocalResponse>().ProducesProblem(StatusCodes.Status403Forbidden);
        g.MapDelete("/{id:long}", Excluir).Produces(StatusCodes.Status204NoContent).ProducesProblem(StatusCodes.Status409Conflict);
        return api;
    }

    private static async Task<IResult> Listar(string? nome, int? pagina, int? tamanho, AppDbContext db, CancellationToken ct)
    {
        var q = db.Locais.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(nome))
        {
            var termo = nome.Trim().ToUpper();
            q = q.Where(l => l.Nome.ToUpper().Contains(termo));
        }
        var resultado = await q.OrderBy(l => l.Nome).ThenBy(l => l.Id)
            .Select(l => new LocalResponse(l.Id, l.Nome, l.Endereco, l.Telefone, l.CriadoPorTipo, l.CriadoPorId))
            .PaginarAsync(pagina, tamanho, ct);
        return Results.Ok(resultado);
    }

    private static async Task<IResult> Obter(long id, AppDbContext db, CancellationToken ct)
    {
        var l = await db.Locais.FindAsync([id], ct) ?? throw new NaoEncontradoException("Local não encontrado.");
        return Results.Ok(LocalResponse.De(l));
    }

    private static async Task<IResult> Criar(LocalRequest req, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var local = new LocalAtendimento
        {
            Nome = req.Nome!.Trim(),
            Endereco = req.Endereco!.Trim(),
            Telefone = string.IsNullOrWhiteSpace(req.Telefone) ? null : req.Telefone.Trim(),
            CriadoPorTipo = user.Papel(),
            CriadoPorId = user.UsuarioId(),
        };
        db.Locais.Add(local);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/locais/{local.Id}", LocalResponse.De(local));
    }

    private static async Task<IResult> Atualizar(long id, LocalRequest req, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var local = await db.Locais.FindAsync([id], ct) ?? throw new NaoEncontradoException("Local não encontrado.");
        if (!EhDono(local, user)) return NaoEhDono();

        local.Nome = req.Nome!.Trim();
        local.Endereco = req.Endereco!.Trim();
        local.Telefone = string.IsNullOrWhiteSpace(req.Telefone) ? null : req.Telefone.Trim();
        await db.SaveChangesAsync(ct);
        return Results.Ok(LocalResponse.De(local));
    }

    private static async Task<IResult> Excluir(long id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var local = await db.Locais.FindAsync([id], ct) ?? throw new NaoEncontradoException("Local não encontrado.");
        if (!EhDono(local, user)) return NaoEhDono();
        if (await db.Consultas.AnyAsync(c => c.LocalId == id, ct))
            throw new ConflitoException("Este local tem consultas. Mude as consultas de local antes de excluir.");

        db.Locais.Remove(local);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static bool EhDono(LocalAtendimento l, ClaimsPrincipal user) =>
        l.CriadoPorTipo == user.Papel() && l.CriadoPorId == user.UsuarioId();

    private static IResult NaoEhDono() =>
        Results.Problem(statusCode: StatusCodes.Status403Forbidden, title: "Sem permissão", detail: "Só quem cadastrou o local pode alterá-lo.");
}
