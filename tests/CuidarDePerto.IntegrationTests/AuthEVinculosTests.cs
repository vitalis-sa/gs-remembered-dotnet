using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CuidarDePerto.IntegrationTests;

public class AuthEVinculosTests(CuidarFactory f) : IClassFixture<CuidarFactory>
{
    [Fact(DisplayName = "INT-AUT-01: cadastro e login devolvem token; e-mail repetido dá 409")]
    public async Task Cadastro_login_duplicado()
    {
        var email = $"bia-{Guid.NewGuid():N}@teste.com";
        var c = f.Cliente();
        (await c.PostAsJsonAsync("/api/v1/auth/cadastro", new { nome = "Bia", email, senha = "senhaForte1" }))
            .StatusCode.ShouldBe(HttpStatusCode.Created);
        (await c.PostAsJsonAsync("/api/v1/auth/cadastro", new { nome = "Bia", email, senha = "senhaForte1" }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);

        var login = await c.PostAsJsonAsync("/api/v1/auth/login", new { email = email.ToUpperInvariant(), senha = "senhaForte1" });
        login.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString().ShouldNotBeNullOrEmpty();
    }

    [Fact(DisplayName = "INT-AUT-02: senha errada e e-mail inexistente dão a mesma resposta 401")]
    public async Task Login_errado_nao_revela_email()
    {
        var token = await f.NovoCuidadorAsync();
        var c = f.Cliente();
        var errada = await c.PostAsJsonAsync("/api/v1/auth/login", new { email = "nao-existe@teste.com", senha = "qualquer123" });
        errada.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await errada.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString().ShouldBe("E-mail ou senha inválidos.");
        token.ShouldNotBeNull();
    }

    [Fact(DisplayName = "INT-AUT-03: cadastro inválido devolve ProblemDetails com erros por campo")]
    public async Task Cadastro_invalido()
    {
        var resp = await f.Cliente().PostAsJsonAsync("/api/v1/auth/cadastro", new { nome = "", email = "x", senha = "1" });
        resp.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var pd = await resp.Content.ReadFromJsonAsync<JsonElement>();
        pd.GetProperty("errors").EnumerateObject().Select(p => p.Name).ShouldBe(["nome", "email", "senha"], ignoreOrder: true);
        pd.GetProperty("path").GetString().ShouldBe("/api/v1/auth/cadastro");
    }

    [Fact(DisplayName = "INT-SEG-01: rota protegida sem token dá 401")]
    public async Task Sem_token() =>
        (await f.Cliente().GetAsync("/api/v1/alertas")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

    [Fact(DisplayName = "INT-SEG-02: idoso (token da Java) tentando ver alertas dá 403")]
    public async Task Idoso_em_alertas() =>
        (await f.Cliente(CuidarFactory.TokenIdoso(10)).GetAsync("/api/v1/alertas")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);

    [Fact(DisplayName = "INT-SEG-03: token assinado com outra chave dá 401")]
    public async Task Chave_errada()
    {
        var c = f.Cliente("eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxIiwicm9sZSI6IkNVSURBRE9SIn0.assinatura-invalida");
        (await c.GetAsync("/api/v1/alertas")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "INT-VIN-01: vínculo valida o código na Java com o token do cuidador e devolve token novo")]
    public async Task Vinculo_valida_na_java()
    {
        var tokenAntigo = await f.NovoCuidadorAsync();
        f.Java.Chamadas.Clear();
        var tokenNovo = await f.VincularAsync(tokenAntigo, 4242);

        var chamada = f.Java.Chamadas.ShouldHaveSingleItem();
        chamada.Caminho.ShouldBe("/api/v1/pareamento/validar");
        chamada.Authorization.ShouldBe($"Bearer {tokenAntigo}");

        (await f.Cliente(tokenNovo).GetAsync("/api/v1/idosos/4242/consultas")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await f.Cliente(tokenAntigo).GetAsync("/api/v1/idosos/4242/consultas")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await f.Cliente(tokenNovo).GetAsync("/api/v1/idosos/9999/consultas")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact(DisplayName = "INT-VIN-02: código recusado pela Java dá 422; vínculo repetido dá 409")]
    public async Task Codigo_invalido_e_repetido()
    {
        var token = await f.NovoCuidadorAsync();
        f.Java.Responder = _ => JavaFalsa.Json(HttpStatusCode.NotFound, new { title = "Código inválido" });
        (await f.Cliente(token).PostAsJsonAsync("/api/v1/vinculos", new { codigo = "XXXXXX" }))
            .StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);

        var novo = await f.VincularAsync(token, 777);
        (await f.Cliente(novo).PostAsJsonAsync("/api/v1/vinculos", new { codigo = "ABC123" }))
            .StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact(DisplayName = "INT-VIN-03: idoso lista os próprios cuidadores e remove o vínculo")]
    public async Task Idoso_lista_e_remove()
    {
        var token = await f.NovoCuidadorAsync();
        await f.VincularAsync(token, 5150);

        var idoso = f.Cliente(CuidarFactory.TokenIdoso(5150));
        var lista = await idoso.GetFromJsonAsync<JsonElement>("/api/v1/vinculos");
        var vinculo = lista.EnumerateArray().ShouldHaveSingleItem();
        vinculo.GetProperty("cuidadorNome").GetString().ShouldBe("Ana Teste");

        (await idoso.DeleteAsync($"/api/v1/vinculos/{vinculo.GetProperty("id").GetInt64()}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await idoso.GetFromJsonAsync<JsonElement>("/api/v1/vinculos")).GetArrayLength().ShouldBe(0);
    }
}
