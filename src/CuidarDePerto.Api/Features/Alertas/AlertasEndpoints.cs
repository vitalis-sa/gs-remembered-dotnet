using System.Security.Claims;
using CuidarDePerto.Api.Comum;
using CuidarDePerto.Api.Infra.Data;
using Microsoft.EntityFrameworkCore;

namespace CuidarDePerto.Api.Features.Alertas;

public sealed record AlertaResponse(long Id, long IdosoId, string IdosoNome, string MedicamentoNome, long DoseId,
    string Severidade, string Motivo, DateTime CriadoEm, DateTime? LidoEm);

public static class AlertasEndpoints
{
    public static RouteGroupBuilder MapAlertas(this RouteGroupBuilder api)
    {
        var g = api.MapGroup("/alertas").WithTags("Alertas").RequireAuthorization(Politicas.Cuidador);
        g.MapGet("", Listar).WithSummary("Alertas do cuidador, só de idosos ainda vinculados. Não lidos primeiro.")
            .Produces<Pagina<AlertaResponse>>();
        g.MapPatch("/{id:long}/lido", MarcarLido).Produces<AlertaResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        return api;
    }

    private static async Task<IResult> Listar(bool? lido, long? idosoId, int? pagina, int? tamanho,
        ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var cuidadorId = user.UsuarioId();
        var q = db.Alertas.AsNoTracking()
            .Where(a => a.CuidadorId == cuidadorId
                && db.Vinculos.Any(v => v.CuidadorId == cuidadorId && v.IdosoId == a.IdosoId));
        if (lido is true) q = q.Where(a => a.LidoEm != null);
        if (lido is false) q = q.Where(a => a.LidoEm == null);
        if (idosoId is not null) q = q.Where(a => a.IdosoId == idosoId);

        var resultado = await q
            .OrderBy(a => a.LidoEm == null ? 0 : 1).ThenByDescending(a => a.CriadoEm).ThenByDescending(a => a.Id)
            .Select(a => new AlertaResponse(a.Id, a.IdosoId, a.IdosoNome, a.MedicamentoNome, a.DoseId, a.Severidade, a.Motivo, a.CriadoEm, a.LidoEm))
            .PaginarAsync(pagina, tamanho, ct);
        return Results.Ok(resultado);
    }

    private static async Task<IResult> MarcarLido(long id, ClaimsPrincipal user, AppDbContext db, TimeProvider tempo, CancellationToken ct)
    {
        var cuidadorId = user.UsuarioId();
        var a = await db.Alertas.FirstOrDefaultAsync(x => x.Id == id && x.CuidadorId == cuidadorId, ct)
            ?? throw new NaoEncontradoException("Alerta não encontrado.");
        a.LidoEm ??= tempo.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync(ct);
        return Results.Ok(new AlertaResponse(a.Id, a.IdosoId, a.IdosoNome, a.MedicamentoNome, a.DoseId, a.Severidade, a.Motivo, a.CriadoEm, a.LidoEm));
    }
}
