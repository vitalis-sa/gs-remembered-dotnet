using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Remembered.Api.Infra.Mensageria;

namespace Remembered.IntegrationTests;

public class ConsultasTests(RememberedFactory f) : IClassFixture<RememberedFactory>
{
    private static readonly DateTimeOffset Futuro = DateTimeOffset.UtcNow.AddDays(10).Date.AddHours(17);

    private async Task<(HttpClient Cliente, long LocalId, long IdosoId)> PrepararAsync()
    {
        var idosoId = Random.Shared.NextInt64(10_000, 1_000_000);
        var token = await f.VincularAsync(await f.NovoCuidadorAsync(), idosoId);
        var c = f.Cliente(token);
        var local = await c.PostAsJsonAsync("/api/v1/locais", new { nome = "Clínica Centro", endereco = "Rua A, 10", telefone = "(11) 3333-0000" });
        local.StatusCode.ShouldBe(HttpStatusCode.Created);
        var localId = (await local.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();
        return (c, localId, idosoId);
    }

    private static HttpRequestMessage Post(long idosoId, object corpo, string? chave)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/idosos/{idosoId}/consultas") { Content = JsonContent.Create(corpo) };
        if (chave is not null) req.Headers.Add("Idempotency-Key", chave);
        return req;
    }

    [Fact(DisplayName = "INT-CON-01: criar consulta grava, publica consulta.criada e lista")]
    public async Task Criar_publica_e_lista()
    {
        var (c, localId, idosoId) = await PrepararAsync();
        var resp = await c.SendAsync(Post(idosoId, new { especialidade = "Cardiologia", localId, dataHora = Futuro, lembrarAntesMin = 60 }, Guid.NewGuid().ToString()));
        resp.StatusCode.ShouldBe(HttpStatusCode.Created);

        var evento = f.Publicador.Publicados.Select(p => p.Evento).OfType<ConsultaCriadaEvento>().Single(e => e.IdosoId == idosoId);
        evento.CriadoPorTipo.ShouldBe("CUIDADOR");
        evento.CriadoPorNome.ShouldBe("Ana Teste");
        // Nomes que o record ConsultaCriada da Java lê.
        var json = JsonSerializer.SerializeToElement(evento, Remembered.Api.Comum.JsonPadrao.Opcoes);
        foreach (var campo in new[] { "eventId", "consultaId", "idosoId", "especialidade", "dataHora", "criadoPorTipo", "criadoPorNome" })
            json.TryGetProperty(campo, out _).ShouldBeTrue(campo);
        // LocalDateTime da Java: hora de Brasília, sem "Z" nem offset.
        json.GetProperty("dataHora").GetString().ShouldBe(Futuro.ToOffset(TimeSpan.FromHours(-3)).ToString("yyyy-MM-ddTHH:mm:ss"));
        evento.Especialidade.ShouldBe("Cardiologia");

        var lista = await c.GetFromJsonAsync<JsonElement>($"/api/v1/idosos/{idosoId}/consultas");
        lista.GetProperty("total").GetInt32().ShouldBe(1);
        lista.GetProperty("itens")[0].GetProperty("localNome").GetString().ShouldBe("Clínica Centro");
    }

    [Fact(DisplayName = "INT-IDE-01: reenvio com a mesma chave devolve a mesma resposta e não duplica")]
    public async Task Idempotencia_ponta_a_ponta()
    {
        var (c, localId, idosoId) = await PrepararAsync();
        var chave = Guid.NewGuid().ToString();
        var corpo = new { especialidade = "Geriatria", localId, dataHora = Futuro.AddDays(1) };

        var r1 = await c.SendAsync(Post(idosoId, corpo, chave));
        var r2 = await c.SendAsync(Post(idosoId, corpo, chave));

        r1.StatusCode.ShouldBe(HttpStatusCode.Created);
        r2.StatusCode.ShouldBe(HttpStatusCode.Created);
        r2.Headers.GetValues("Idempotent-Replayed").ShouldContain("true");
        (await r2.Content.ReadAsStringAsync()).ShouldBe(await r1.Content.ReadAsStringAsync());

        (await c.GetFromJsonAsync<JsonElement>($"/api/v1/idosos/{idosoId}/consultas")).GetProperty("total").GetInt32().ShouldBe(1);
        f.Publicador.Publicados.Select(p => p.Evento).OfType<ConsultaCriadaEvento>().Count(e => e.IdosoId == idosoId).ShouldBe(1);
    }

    [Fact(DisplayName = "INT-IDE-02: mesma chave com outro corpo dá 422; sem chave dá 400")]
    public async Task Idempotencia_corpo_diferente_e_sem_chave()
    {
        var (c, localId, idosoId) = await PrepararAsync();
        var chave = Guid.NewGuid().ToString();
        (await c.SendAsync(Post(idosoId, new { especialidade = "Geriatria", localId, dataHora = Futuro }, chave)))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await c.SendAsync(Post(idosoId, new { especialidade = "Ortopedia", localId, dataHora = Futuro.AddDays(2) }, chave)))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await c.SendAsync(Post(idosoId, new { especialidade = "Ortopedia", localId, dataHora = Futuro.AddDays(2) }, null)))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "INT-CON-02: consulta a menos de 60 min de outra dá 409 com a consulta conflitante")]
    public async Task Conflito_de_horario()
    {
        var (c, localId, idosoId) = await PrepararAsync();
        (await c.SendAsync(Post(idosoId, new { especialidade = "Cardiologia", localId, dataHora = Futuro }, Guid.NewGuid().ToString())))
            .StatusCode.ShouldBe(HttpStatusCode.Created);

        var resp = await c.SendAsync(Post(idosoId, new { especialidade = "Dermatologia", localId, dataHora = Futuro.AddMinutes(30) }, Guid.NewGuid().ToString()));
        resp.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("consultaConflitante").GetProperty("especialidade").GetString().ShouldBe("Cardiologia");
    }

    [Fact(DisplayName = "INT-CON-03: data passada dá 400")]
    public async Task Data_passada()
    {
        var (c, localId, idosoId) = await PrepararAsync();
        (await c.SendAsync(Post(idosoId, new { especialidade = "Cardiologia", localId, dataHora = DateTimeOffset.UtcNow.AddDays(-1) }, Guid.NewGuid().ToString())))
            .StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact(DisplayName = "INT-CON-04: editar, cancelar e excluir; cuidador sem vínculo recebe 404")]
    public async Task Editar_cancelar_excluir()
    {
        var (c, localId, idosoId) = await PrepararAsync();
        var criada = await c.SendAsync(Post(idosoId, new { especialidade = "Cardiologia", localId, dataHora = Futuro }, Guid.NewGuid().ToString()));
        var id = (await criada.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt64();

        var outro = f.Cliente(await f.NovoCuidadorAsync());
        (await outro.GetAsync($"/api/v1/consultas/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        var cancelada = await c.PutAsJsonAsync($"/api/v1/consultas/{id}",
            new { especialidade = "Cardiologia", localId, dataHora = Futuro, status = "CANCELADA" });
        cancelada.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await cancelada.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString().ShouldBe("CANCELADA");

        (await c.PutAsJsonAsync($"/api/v1/consultas/{id}", new { especialidade = "Cardiologia", localId, dataHora = Futuro, status = "AGENDADA" }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        (await c.DeleteAsync($"/api/v1/consultas/{id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await c.GetAsync($"/api/v1/consultas/{id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact(DisplayName = "INT-CON-05: idoso com token da Java cria e lista as próprias consultas")]
    public async Task Idoso_cria_consulta()
    {
        var (c, localId, _) = await PrepararAsync();
        var idoso = f.Cliente(RememberedFactory.TokenIdoso(31337));
        var resp = await idoso.SendAsync(Post(31337, new { especialidade = "Oftalmologia", localId, dataHora = Futuro }, Guid.NewGuid().ToString()));
        resp.StatusCode.ShouldBe(HttpStatusCode.Created);
        f.Publicador.Publicados.Select(p => p.Evento).OfType<ConsultaCriadaEvento>()
            .Single(e => e.IdosoId == 31337).CriadoPorTipo.ShouldBe("IDOSO");
        (await idoso.SendAsync(Post(31338, new { especialidade = "X", localId, dataHora = Futuro }, Guid.NewGuid().ToString())))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        c.ShouldNotBeNull();
    }

    [Fact(DisplayName = "INT-LOC-01: só quem criou o local altera; local com consulta não é excluído")]
    public async Task Locais_dono_e_exclusao()
    {
        var (c, localId, idosoId) = await PrepararAsync();
        var outro = f.Cliente(await f.NovoCuidadorAsync());
        (await outro.PutAsJsonAsync($"/api/v1/locais/{localId}", new { nome = "Outro", endereco = "Rua B" }))
            .StatusCode.ShouldBe(HttpStatusCode.Forbidden);

        await c.SendAsync(Post(idosoId, new { especialidade = "Cardiologia", localId, dataHora = Futuro }, Guid.NewGuid().ToString()));
        (await c.DeleteAsync($"/api/v1/locais/{localId}")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }
}
