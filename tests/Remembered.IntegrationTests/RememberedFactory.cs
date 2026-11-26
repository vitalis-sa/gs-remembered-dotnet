using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Remembered.Api.Infra.Data;
using Remembered.Api.Infra.Java;
using Remembered.Api.Infra.Mensageria;
using Remembered.Api.Infra.Push;
using Remembered.Api.Infra.Saude;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Remembered.IntegrationTests;

/// <summary>API real com SQLite em memória, RabbitMQ desligado e a API Java trocada por um handler falso.</summary>
public sealed class RememberedFactory : WebApplicationFactory<Program>
{
    public const string ChaveJwt = "chave-de-teste-com-pelo-menos-32-bytes!!";
    public const string IssuerJava = "remembered-java";

    private readonly SqliteConnection _conexao = new("DataSource=:memory:");

    public JavaFalsa Java { get; } = new();
    public PublicadorFalso Publicador { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _conexao.Open();
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:Key", ChaveJwt);
        builder.UseSetting("Jwt:Issuer", "remembered-dotnet");
        builder.UseSetting("Jwt:Audience", "remembered");
        builder.UseSetting("Jwt:IssuersAceitos:0", IssuerJava);
        builder.UseSetting("Java:BaseUrl", "http://java.test");
        builder.UseSetting("RabbitMq:Enabled", "false");

        builder.ConfigureTestServices(s =>
        {
            s.RemoveAll<DbContextOptions<AppDbContext>>();
            s.RemoveAll<AppDbContext>();
            s.AddDbContext<AppDbContext>(o => o.UseSqlite(_conexao));

            s.RemoveAll<IPublicadorEventos>();
            s.AddSingleton<IPublicadorEventos>(Publicador);
            s.RemoveAll<IPushSender>();
            s.AddSingleton<IPushSender, PushNulo>();

            s.AddHttpClient<JavaApiClient>().ConfigurePrimaryHttpMessageHandler(() => Java);
            s.AddHttpClient(JavaHealthCheck.NomeCliente).ConfigurePrimaryHttpMessageHandler(() => Java);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        using var escopo = host.Services.CreateScope();
        escopo.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();
        return host;
    }

    /// <summary>Token como a API Java emitiria para um idoso.</summary>
    public static string TokenIdoso(long idosoId) => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
    {
        Issuer = IssuerJava,
        Audience = "remembered",
        Expires = DateTime.UtcNow.AddHours(1),
        SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(ChaveJwt)), SecurityAlgorithms.HmacSha256),
        Claims = new Dictionary<string, object> { ["sub"] = idosoId.ToString(), ["role"] = "IDOSO", ["nome"] = "Maria" },
    });

    public HttpClient Cliente(string? token = null)
    {
        var c = CreateClient();
        if (token is not null) c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return c;
    }

    /// <summary>Cadastra um cuidador novo e devolve o token dele.</summary>
    public async Task<string> NovoCuidadorAsync()
    {
        var resp = await CreateClient().PostAsJsonAsync("/api/v1/auth/cadastro",
            new { nome = "Ana Teste", email = $"ana-{Guid.NewGuid():N}@teste.com", senha = "senhaForte1" });
        resp.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    /// <summary>Vincula o cuidador ao idoso pela Java falsa e devolve o token novo (com o idoso na claim).</summary>
    public async Task<string> VincularAsync(string tokenCuidador, long idosoId)
    {
        Java.Responder = _ => JavaFalsa.Json(HttpStatusCode.OK, new { idosoId, nome = "Maria" });
        var resp = await Cliente(tokenCuidador).PostAsJsonAsync("/api/v1/vinculos", new { codigo = "ABC123", parentesco = "Filha" });
        resp.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString()!;
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        await _conexao.DisposeAsync();
    }
}

public sealed class JavaFalsa : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Responder { get; set; } = _ => Json(HttpStatusCode.OK, new { status = "UP" });
    public ConcurrentQueue<(string Metodo, string Caminho, string? Authorization)> Chamadas { get; } = new();

    public static HttpResponseMessage Json(HttpStatusCode status, object corpo) =>
        new(status) { Content = JsonContent.Create(corpo) };

    public int ContarChamadas(string trecho) => Chamadas.Count(c => c.Caminho.Contains(trecho, StringComparison.Ordinal));

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Chamadas.Enqueue((request.Method.Method, request.RequestUri!.PathAndQuery, request.Headers.Authorization?.ToString()));
        return Task.FromResult(Responder(request));
    }
}

public sealed class PublicadorFalso : IPublicadorEventos
{
    public ConcurrentQueue<(string Rota, object Evento)> Publicados { get; } = new();

    public Task PublicarAsync(string routingKey, string eventId, object evento, CancellationToken ct)
    {
        Publicados.Enqueue((routingKey, evento));
        return Task.CompletedTask;
    }
}

public sealed class PushNulo : IPushSender
{
    public Task EnviarAsync(string pushToken, string titulo, string corpo, object? dados, CancellationToken ct) => Task.CompletedTask;
}
