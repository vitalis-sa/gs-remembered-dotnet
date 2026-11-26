using System.Text;
using Remembered.Api.Comum;
using Remembered.Api.Features.Consultas;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Remembered.UnitTests;

public sealed class IdempotenciaFilterTests : IDisposable
{
    private readonly BancoTeste _banco = new();
    private readonly IServiceProvider _servicos = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
    private readonly RelogioFixo _relogio = new(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));
    private int _execucoes;

    private static ConsultaRequest Corpo(string especialidade) =>
        new(especialidade, 1, new DateTimeOffset(2026, 12, 1, 14, 0, 0, TimeSpan.Zero), null, 60);

    private async Task<(object? Resultado, DefaultHttpContext Http)> ChamarAsync(string chave, ConsultaRequest corpo)
    {
        var http = new DefaultHttpContext { RequestServices = _servicos, User = Usuarios.Cuidador(1, 10) };
        http.Request.Method = "POST";
        http.Request.Path = "/api/v1/idosos/10/consultas";
        http.Request.Headers[IdempotenciaFilter.Cabecalho] = chave;
        http.Response.Body = new MemoryStream();

        await using var db = _banco.NovoContexto();
        var filtro = new IdempotenciaFilter(db, _relogio, NullLogger<IdempotenciaFilter>.Instance);
        var ctx = EndpointFilterInvocationContext.Create(http, 10L, corpo);
        var resultado = await filtro.InvokeAsync(ctx, _ =>
        {
            _execucoes++;
            return ValueTask.FromResult<object?>(Results.Created("/api/v1/consultas/1", new { id = 1, execucao = _execucoes }));
        });
        return (resultado, http);
    }

    private static string Corpo(DefaultHttpContext http) =>
        Encoding.UTF8.GetString(((MemoryStream)http.Response.Body).ToArray());

    [Fact(DisplayName = "IDE-01: mesma chave e mesmo corpo devolvem a resposta salva sem executar de novo")]
    public async Task Mesma_chave_mesmo_corpo()
    {
        var (_, primeira) = await ChamarAsync("k1", Corpo("Cardiologia"));
        primeira.Response.StatusCode.ShouldBe(201);
        var corpoOriginal = Corpo(primeira);

        var (resultado, _) = await ChamarAsync("k1", Corpo("Cardiologia"));

        _execucoes.ShouldBe(1);
        var replay = resultado.ShouldBeOfType<ContentHttpResult>();
        replay.StatusCode.ShouldBe(201);
        replay.ResponseContent.ShouldBe(corpoOriginal);
    }

    [Fact(DisplayName = "IDE-02: mesma chave com corpo diferente devolve 422")]
    public async Task Mesma_chave_corpo_diferente()
    {
        await ChamarAsync("k2", Corpo("Cardiologia"));
        var (resultado, _) = await ChamarAsync("k2", Corpo("Ortopedia"));

        _execucoes.ShouldBe(1);
        resultado.ShouldBeOfType<ProblemHttpResult>().StatusCode.ShouldBe(422);
    }

    [Fact(DisplayName = "IDE-03: sem Idempotency-Key devolve 400")]
    public async Task Sem_chave()
    {
        var (resultado, _) = await ChamarAsync("", Corpo("Cardiologia"));
        _execucoes.ShouldBe(0);
        resultado.ShouldBeOfType<ProblemHttpResult>().StatusCode.ShouldBe(400);
    }

    [Fact(DisplayName = "IDE-04: chaves diferentes executam as duas requisições")]
    public async Task Chaves_diferentes()
    {
        await ChamarAsync("k3", Corpo("Cardiologia"));
        await ChamarAsync("k4", Corpo("Cardiologia"));
        _execucoes.ShouldBe(2);
    }

    public void Dispose() => _banco.Dispose();
}
