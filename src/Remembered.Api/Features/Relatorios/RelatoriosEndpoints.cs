using Remembered.Api.Comum;
using Remembered.Api.Infra.Java;
using Microsoft.AspNetCore.Mvc;

namespace Remembered.Api.Features.Relatorios;

/// <summary>Mesmo corpo do RelatorioAdesaoRequest da Java: dias até hoje, de 1 a 90; vazio vale 7.</summary>
public sealed record RelatorioRequest(int? Dias) : IValidavel
{
    public const int MaximoDias = 90;

    public IDictionary<string, string[]> Validar() => new Validador()
        .Regra("dias", Dias is null || Dias is >= 1 and <= MaximoDias, $"Período de 1 a {MaximoDias} dias.")
        .Erros;
}

/// <summary>Proxy para a API Java, onde está o LLM. Repassa o token do cuidador e o corpo da resposta como veio.</summary>
public static class RelatoriosEndpoints
{
    public const string PoliticaLimite = "relatorios";

    public static RouteGroupBuilder MapRelatorios(this RouteGroupBuilder api)
    {
        var porIdoso = api.MapGroup("/idosos/{idosoId:long}/relatorios-adesao").WithTags("Relatórios")
            .RequireAuthorization(Politicas.Cuidador, Politicas.CuidadorVinculado);
        porIdoso.MapPost("", Gerar).Validar<RelatorioRequest>().Idempotente().RequireRateLimiting(PoliticaLimite)
            .WithSummary("Pede à API Java um relatório de adesão. Exige Idempotency-Key; 3 por minuto por cuidador.")
            .ProducesProblem(StatusCodes.Status429TooManyRequests)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
        porIdoso.MapGet("", Listar).WithSummary("Histórico de relatórios do idoso (repasse da API Java).");

        api.MapGet("/relatorios-adesao/{id:long}", Obter).WithTags("Relatórios")
            .RequireAuthorization(Politicas.Cuidador)
            .WithSummary("Um relatório pelo id. A API Java confere o vínculo pela claim idosos.");
        return api;
    }

    private static async Task<IResult> Gerar(long idosoId, RelatorioRequest req,
        [FromHeader(Name = IdempotenciaFilter.Cabecalho)] string? idempotencyKey,
        HttpContext http, JavaApiClient java, CancellationToken ct)
    {
        var r = await java.EnviarAsync(HttpMethod.Post, $"api/v1/idosos/{idosoId}/relatorios-adesao",
            http.Request.Headers.Authorization.ToString(), req, ct);
        return r.ComoResultado();
    }

    private static async Task<IResult> Listar(long idosoId, HttpContext http, JavaApiClient java, CancellationToken ct)
    {
        var r = await java.EnviarAsync(HttpMethod.Get, $"api/v1/idosos/{idosoId}/relatorios-adesao{http.Request.QueryString}",
            http.Request.Headers.Authorization.ToString(), null, ct);
        return r.ComoResultado();
    }

    private static async Task<IResult> Obter(long id, HttpContext http, JavaApiClient java, CancellationToken ct)
    {
        var r = await java.EnviarAsync(HttpMethod.Get, $"api/v1/relatorios-adesao/{id}",
            http.Request.Headers.Authorization.ToString(), null, ct);
        return r.ComoResultado();
    }
}
