using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CuidarDePerto.Api.Infra.Data;
using Microsoft.Extensions.DependencyInjection;

namespace CuidarDePerto.IntegrationTests;

public class RelatoriosTests(CuidarFactory f) : IClassFixture<CuidarFactory>
{
    private static readonly object Periodo = new { dias = 7 };

    private static HttpRequestMessage Post(long idosoId, string chave) =>
        new(HttpMethod.Post, $"/api/v1/idosos/{idosoId}/relatorios-adesao")
        {
            Content = JsonContent.Create(Periodo),
            Headers = { { "Idempotency-Key", chave } },
        };

    private async Task<HttpClient> CuidadorVinculadoAsync(long idosoId) =>
        f.Cliente(await f.VincularAsync(await f.NovoCuidadorAsync(), idosoId));

    [Fact(DisplayName = "INT-REL-01: repassa à Java com o token do cuidador e devolve o corpo como veio")]
    public async Task Proxy_para_java()
    {
        var c = await CuidadorVinculadoAsync(600);
        f.Java.Chamadas.Clear();
        f.Java.Responder = _ => JavaFalsa.Json(HttpStatusCode.Created, new { id = 9, percentualAdesao = 85.7, textoLlm = "Semana boa." });

        var resp = await c.SendAsync(Post(600, Guid.NewGuid().ToString()));

        resp.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("textoLlm").GetString().ShouldBe("Semana boa.");
        var chamada = f.Java.Chamadas.ShouldHaveSingleItem();
        chamada.Caminho.ShouldBe("/api/v1/idosos/600/relatorios-adesao");
        chamada.Authorization.ShouldNotBeNull().ShouldStartWith("Bearer ");
    }

    [Fact(DisplayName = "INT-REL-02: mesma Idempotency-Key não chama a Java de novo")]
    public async Task Idempotencia_nao_chama_java()
    {
        var c = await CuidadorVinculadoAsync(601);
        f.Java.Chamadas.Clear();
        f.Java.Responder = _ => JavaFalsa.Json(HttpStatusCode.Created, new { id = 10 });
        var chave = Guid.NewGuid().ToString();

        (await c.SendAsync(Post(601, chave))).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await c.SendAsync(Post(601, chave))).StatusCode.ShouldBe(HttpStatusCode.Created);
        f.Java.ContarChamadas("relatorios-adesao").ShouldBe(1);
    }

    [Fact(DisplayName = "INT-RL-01: a 4ª chamada de relatório no mesmo minuto dá 429 com Retry-After")]
    public async Task Limite_de_relatorios()
    {
        var c = await CuidadorVinculadoAsync(602);
        f.Java.Responder = _ => JavaFalsa.Json(HttpStatusCode.Created, new { id = 11 });

        for (var i = 0; i < 3; i++)
            (await c.SendAsync(Post(602, Guid.NewGuid().ToString()))).StatusCode.ShouldBe(HttpStatusCode.Created);

        var quarta = await c.SendAsync(Post(602, Guid.NewGuid().ToString()));
        quarta.StatusCode.ShouldBe(HttpStatusCode.TooManyRequests);
        quarta.Headers.RetryAfter.ShouldNotBeNull();
    }

    [Fact(DisplayName = "INT-JAV-01: Java com erro 500 vira 502 em ProblemDetails")]
    public async Task Java_500_vira_502()
    {
        var c = await CuidadorVinculadoAsync(603);
        f.Java.Responder = _ => JavaFalsa.Json(HttpStatusCode.InternalServerError, new { erro = "boom" });

        var resp = await c.SendAsync(Post(603, Guid.NewGuid().ToString()));
        resp.StatusCode.ShouldBe(HttpStatusCode.BadGateway);
        resp.Content.Headers.ContentType!.MediaType.ShouldBe("application/problem+json");
    }

    [Fact(DisplayName = "INT-JAV-02: Java fora do ar vira 503; falha não fica gravada na idempotência")]
    public async Task Java_fora_vira_503()
    {
        var c = await CuidadorVinculadoAsync(604);
        var chave = Guid.NewGuid().ToString();
        f.Java.Responder = _ => throw new HttpRequestException("conexão recusada");
        (await c.SendAsync(Post(604, chave))).StatusCode.ShouldBe(HttpStatusCode.ServiceUnavailable);

        f.Java.Responder = _ => JavaFalsa.Json(HttpStatusCode.Created, new { id = 12 });
        (await c.SendAsync(Post(604, chave))).StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact(DisplayName = "INT-REL-03: cuidador sem vínculo não gera relatório (403)")]
    public async Task Sem_vinculo()
    {
        var c = f.Cliente(await f.NovoCuidadorAsync());
        (await c.SendAsync(Post(605, Guid.NewGuid().ToString()))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }
}

public class SaudeEAlertasTests(CuidarFactory f) : IClassFixture<CuidarFactory>
{
    [Fact(DisplayName = "INT-SAU-01: /health fica Healthy com Oracle e Java no ar")]
    public async Task Saudavel()
    {
        f.Java.Responder = _ => JavaFalsa.Json(HttpStatusCode.OK, new { status = "UP" });
        var resp = await f.Cliente().GetAsync("/health");
        resp.StatusCode.ShouldBe(HttpStatusCode.OK);
        var corpo = await resp.Content.ReadFromJsonAsync<JsonElement>();
        corpo.GetProperty("status").GetString().ShouldBe("Healthy");
        corpo.GetProperty("verificacoes").GetProperty("oracle").GetProperty("status").GetString().ShouldBe("Healthy");
    }

    [Fact(DisplayName = "INT-SAU-02: /health fica Degraded quando a Java cai")]
    public async Task Degradado()
    {
        f.Java.Responder = _ => JavaFalsa.Json(HttpStatusCode.ServiceUnavailable, new { status = "DOWN" });
        var corpo = await f.Cliente().GetFromJsonAsync<JsonElement>("/health");
        corpo.GetProperty("status").GetString().ShouldBe("Degraded");
        corpo.GetProperty("verificacoes").GetProperty("api-java").GetProperty("status").GetString().ShouldBe("Degraded");
    }

    [Fact(DisplayName = "INT-ALE-01: cuidador vê só os próprios alertas e marca como lido")]
    public async Task Alertas_do_cuidador()
    {
        var token = await f.VincularAsync(await f.NovoCuidadorAsync(), 700);
        var cuidadorId = long.Parse((await f.Cliente(token).GetFromJsonAsync<JsonElement>("/api/v1/me")).GetProperty("id").ToString());

        using (var escopo = f.Services.CreateScope())
        {
            var db = escopo.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Alertas.AddRange(
                new Alerta { CuidadorId = cuidadorId, IdosoId = 700, IdosoNome = "Maria", MedicamentoNome = "Losartana", DoseId = 1, Severidade = "ALTA", Motivo = "m", CriadoEm = DateTime.UtcNow },
                new Alerta { CuidadorId = cuidadorId, IdosoId = 999, IdosoNome = "Outro", MedicamentoNome = "X", DoseId = 2, Severidade = "MEDIA", Motivo = "m", CriadoEm = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var c = f.Cliente(token);
        var lista = await c.GetFromJsonAsync<JsonElement>("/api/v1/alertas?lido=false");
        var alerta = lista.GetProperty("itens").EnumerateArray().ShouldHaveSingleItem();
        alerta.GetProperty("medicamentoNome").GetString().ShouldBe("Losartana");

        var lido = await c.PatchAsync($"/api/v1/alertas/{alerta.GetProperty("id").GetInt64()}/lido", null);
        lido.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await c.GetFromJsonAsync<JsonElement>("/api/v1/alertas?lido=false")).GetProperty("total").GetInt32().ShouldBe(0);
    }
}
