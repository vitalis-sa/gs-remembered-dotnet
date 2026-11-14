using System.Security.Claims;
using CuidarDePerto.Api.Comum;
using CuidarDePerto.Api.Infra.Data;
using CuidarDePerto.Api.Infra.Mensageria;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CuidarDePerto.Api.Features.Consultas;

public sealed record ConsultaRequest(string? Especialidade, long? LocalId, DateTimeOffset? DataHora, string? Observacoes, int? LembrarAntesMin) : IValidavel
{
    public IDictionary<string, string[]> Validar() => new Validador()
        .Obrigatorio("especialidade", Especialidade, 100)
        .Regra("localId", LocalId is > 0, "Escolha um local de atendimento.")
        .Regra("dataHora", DataHora is not null, "Informe data e hora.")
        .Regra("dataHora", DataHora is null || DataHora > DateTimeOffset.UtcNow, "A consulta precisa ser numa data futura.")
        .Opcional("observacoes", Observacoes, 500)
        .Regra("lembrarAntesMin", LembrarAntesMin is null or (>= 0 and <= 10080), "Lembrete entre 0 e 10080 minutos (7 dias).")
        .Erros;
}

public sealed record AtualizarConsultaRequest(string? Especialidade, long? LocalId, DateTimeOffset? DataHora, string? Status, string? Observacoes, int? LembrarAntesMin) : IValidavel
{
    public IDictionary<string, string[]> Validar() => new Validador()
        .Obrigatorio("especialidade", Especialidade, 100)
        .Regra("localId", LocalId is > 0, "Escolha um local de atendimento.")
        .Regra("dataHora", DataHora is not null, "Informe data e hora.")
        .Regra("status", Status is null || StatusConsulta.Todos.Contains(Status.ToUpperInvariant()), "Use AGENDADA, REALIZADA ou CANCELADA.")
        .Opcional("observacoes", Observacoes, 500)
        .Regra("lembrarAntesMin", LembrarAntesMin is null or (>= 0 and <= 10080), "Lembrete entre 0 e 10080 minutos (7 dias).")
        .Erros;
}

public sealed record ConsultaResponse(long Id, long IdosoId, long LocalId, string LocalNome, string LocalEndereco,
    string Especialidade, DateTime DataHora, string Status, string? Observacoes, int LembrarAntesMin,
    string CriadoPorTipo, string CriadoPorNome)
{
    public static ConsultaResponse De(Consulta c, LocalAtendimento l) => new(c.Id, c.IdosoId, c.LocalId, l.Nome, l.Endereco,
        c.Especialidade, c.DataHora, c.Status, c.Observacoes, c.LembrarAntesMin, c.CriadoPorTipo, c.CriadoPorNome);
}

public static class ConsultasEndpoints
{
    /// <summary>Consultas do mesmo idoso precisam de pelo menos 60 min de diferença.</summary>
    public static readonly TimeSpan IntervaloMinimo = TimeSpan.FromMinutes(60);

    public static RouteGroupBuilder MapConsultas(this RouteGroupBuilder api)
    {
        var porIdoso = api.MapGroup("/idosos/{idosoId:long}/consultas").WithTags("Consultas")
            .RequireAuthorization(Politicas.CuidadorVinculado);
        porIdoso.MapGet("", Listar).Produces<Pagina<ConsultaResponse>>();
        porIdoso.MapPost("", Criar).Validar<ConsultaRequest>().Idempotente()
            .WithSummary("Cria a consulta e publica consulta.criada. Exige Idempotency-Key.")
            .Produces<ConsultaResponse>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status409Conflict);

        var g = api.MapGroup("/consultas").WithTags("Consultas").RequireAuthorization();
        g.MapGet("/{id:long}", Obter).Produces<ConsultaResponse>();
        g.MapPut("/{id:long}", Atualizar).Validar<AtualizarConsultaRequest>()
            .Produces<ConsultaResponse>().ProducesProblem(StatusCodes.Status409Conflict);
        g.MapDelete("/{id:long}", Excluir).Produces(StatusCodes.Status204NoContent);
        return api;
    }

    private static async Task<IResult> Listar(long idosoId, string? status, DateTimeOffset? de, DateTimeOffset? ate,
        int? pagina, int? tamanho, AppDbContext db, CancellationToken ct)
    {
        var q = db.Consultas.AsNoTracking().Where(c => c.IdosoId == idosoId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var s = status.Trim().ToUpperInvariant();
            q = q.Where(c => c.Status == s);
        }
        if (de is not null)
        {
            var inicio = de.Value.UtcDateTime;
            q = q.Where(c => c.DataHora >= inicio);
        }
        if (ate is not null)
        {
            var fim = ate.Value.UtcDateTime;
            q = q.Where(c => c.DataHora <= fim);
        }

        var resultado = await q.OrderBy(c => c.DataHora).ThenBy(c => c.Id)
            .Select(c => new ConsultaResponse(c.Id, c.IdosoId, c.LocalId, c.Local!.Nome, c.Local.Endereco, c.Especialidade,
                c.DataHora, c.Status, c.Observacoes, c.LembrarAntesMin, c.CriadoPorTipo, c.CriadoPorNome))
            .PaginarAsync(pagina, tamanho, ct);
        return Results.Ok(resultado);
    }

    private static async Task<IResult> Criar(long idosoId, ConsultaRequest req,
        [FromHeader(Name = IdempotenciaFilter.Cabecalho)] string? idempotencyKey,
        ClaimsPrincipal user, AppDbContext db, IPublicadorEventos publicador, CancellationToken ct)
    {
        var local = await db.Locais.FindAsync([req.LocalId!.Value], ct)
            ?? throw new RegraNegocioException("Local de atendimento não encontrado.");
        var dataHora = req.DataHora!.Value.UtcDateTime;
        await GarantirSemConflitoAsync(db, idosoId, dataHora, null, ct);

        var consulta = new Consulta
        {
            IdosoId = idosoId,
            LocalId = local.Id,
            Especialidade = req.Especialidade!.Trim(),
            DataHora = dataHora,
            Status = StatusConsulta.Agendada,
            Observacoes = Limpar(req.Observacoes),
            LembrarAntesMin = req.LembrarAntesMin ?? 60,
            CriadoPorTipo = user.Papel(),
            CriadoPorNome = user.NomeUsuario(),
        };
        db.Consultas.Add(consulta);
        await db.SaveChangesAsync(ct);

        // Publicado depois do commit: a Java só recebe consultas que existem no banco.
        var eventId = Guid.NewGuid().ToString();
        await publicador.PublicarAsync(RotasEventos.ConsultaCriada, eventId, new ConsultaCriadaEvento(
            eventId, consulta.Id, idosoId, consulta.CriadoPorNome, consulta.CriadoPorTipo,
            consulta.Especialidade, HoraBrasil.DeUtc(consulta.DataHora), local.Nome), ct);

        return Results.Created($"/api/v1/consultas/{consulta.Id}", ConsultaResponse.De(consulta, local));
    }

    private static async Task<IResult> Obter(long id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var c = await CarregarComAcessoAsync(id, user, db, ct);
        return c is null ? SemAcesso() : Results.Ok(ConsultaResponse.De(c, c.Local!));
    }

    private static async Task<IResult> Atualizar(long id, AtualizarConsultaRequest req, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var c = await CarregarComAcessoAsync(id, user, db, ct);
        if (c is null) return SemAcesso();

        var novoStatus = req.Status?.Trim().ToUpperInvariant() ?? c.Status;
        if (c.Status != StatusConsulta.Agendada && novoStatus != c.Status)
            throw new RegraNegocioException($"Consulta {c.Status} não muda de status.");

        var local = c.LocalId == req.LocalId ? c.Local! : await db.Locais.FindAsync([req.LocalId!.Value], ct)
            ?? throw new RegraNegocioException("Local de atendimento não encontrado.");
        var dataHora = req.DataHora!.Value.UtcDateTime;

        if (novoStatus == StatusConsulta.Agendada)
        {
            if (dataHora != c.DataHora && req.DataHora <= DateTimeOffset.UtcNow)
                throw new RegraNegocioException("A consulta precisa ser numa data futura.");
            await GarantirSemConflitoAsync(db, c.IdosoId, dataHora, c.Id, ct);
        }

        c.Especialidade = req.Especialidade!.Trim();
        c.LocalId = local.Id;
        c.Local = local;
        c.DataHora = dataHora;
        c.Status = novoStatus;
        c.Observacoes = Limpar(req.Observacoes);
        c.LembrarAntesMin = req.LembrarAntesMin ?? c.LembrarAntesMin;
        await db.SaveChangesAsync(ct);
        return Results.Ok(ConsultaResponse.De(c, local));
    }

    private static async Task<IResult> Excluir(long id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var c = await CarregarComAcessoAsync(id, user, db, ct);
        if (c is null) return SemAcesso();
        db.Consultas.Remove(c);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    /// <summary>Null quando a consulta não existe ou é de idoso fora do token: o cliente recebe 404 nos dois casos.</summary>
    private static async Task<Consulta?> CarregarComAcessoAsync(long id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct)
    {
        var c = await db.Consultas.Include(x => x.Local).FirstOrDefaultAsync(x => x.Id == id, ct);
        return c is not null && AcessoIdoso.PodeAcessar(user, c.IdosoId) ? c : null;
    }

    public static async Task GarantirSemConflitoAsync(AppDbContext db, long idosoId, DateTime dataHora, long? ignorarId, CancellationToken ct)
    {
        var inicio = dataHora - IntervaloMinimo;
        var fim = dataHora + IntervaloMinimo;
        var conflito = await db.Consultas.AsNoTracking().Include(c => c.Local)
            .Where(c => c.IdosoId == idosoId && c.Status == StatusConsulta.Agendada
                && c.DataHora > inicio && c.DataHora < fim
                && (ignorarId == null || c.Id != ignorarId))
            .OrderBy(c => c.DataHora)
            .FirstOrDefaultAsync(ct);
        if (conflito is not null)
            throw new ConflitoException("Já existe uma consulta a menos de 60 minutos deste horário.",
                new Dictionary<string, object?> { ["consultaConflitante"] = ConsultaResponse.De(conflito, conflito.Local!) });
    }

    private static string? Limpar(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static IResult SemAcesso() =>
        Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Não encontrado", detail: "Consulta não encontrada.");
}
