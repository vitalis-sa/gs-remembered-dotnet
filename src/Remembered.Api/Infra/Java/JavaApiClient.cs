using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Remembered.Api.Comum;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace Remembered.Api.Infra.Java;

public sealed class JavaOptions
{
    public const string Secao = "Java";
    /// <summary>Raiz da API Java, por exemplo https://cuidar-java.azurewebsites.net.</summary>
    public string BaseUrl { get; set; } = "";
    public int TimeoutSegundos { get; set; } = 20;
}

public sealed record JavaResposta(int Status, string Corpo, string ContentType)
{
    /// <summary>Repassa ao cliente o corpo exatamente como veio da Java.</summary>
    public IResult ComoResultado() => Results.Content(Corpo, ContentType, statusCode: Status);
}

public sealed record IdosoPareado(long IdosoId, string Nome);

/// <summary>
/// Cliente tipado da API Java. Repassa o Authorization recebido (sem token de serviço).
/// Timeout, nova tentativa e circuit breaker ficam no pipeline Polly registrado no Program.
/// </summary>
public sealed class JavaApiClient(HttpClient http, ILogger<JavaApiClient> log)
{
    public async Task<JavaResposta> EnviarAsync(HttpMethod metodo, string caminho, string? authorization, object? corpo, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(metodo, caminho.TrimStart('/'));
        if (!string.IsNullOrWhiteSpace(authorization))
            req.Headers.TryAddWithoutValidation("Authorization", authorization);
        if (corpo is not null)
            req.Content = JsonContent.Create(corpo, corpo.GetType(), options: JsonPadrao.Opcoes);

        HttpResponseMessage resp;
        try
        {
            resp = await http.SendAsync(req, ct);
        }
        catch (BrokenCircuitException ex)
        {
            throw new JavaIndisponivelException("A API Java falhou várias vezes seguidas. Tente de novo em alguns segundos.", ex);
        }
        catch (TimeoutRejectedException ex)
        {
            throw new JavaIndisponivelException("A API Java demorou demais para responder.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new JavaIndisponivelException("Não foi possível conectar na API Java.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new JavaIndisponivelException("A API Java demorou demais para responder.", ex);
        }

        using (resp)
        {
            var texto = await resp.Content.ReadAsStringAsync(ct);
            var status = (int)resp.StatusCode;
            if (status >= 500)
            {
                log.LogWarning("API Java respondeu {Status} em {Metodo} {Caminho}", status, metodo, caminho);
                throw new JavaErroException($"A API Java respondeu com erro {status}.");
            }
            var contentType = resp.Content.Headers.ContentType?.ToString() ?? "application/json";
            return new JavaResposta(status, texto, contentType);
        }
    }

    /// <summary>Confere e consome o código de pareamento na Java (POST /api/v1/pareamento/validar).</summary>
    public async Task<IdosoPareado> ValidarPareamentoAsync(string codigo, string authorization, CancellationToken ct)
    {
        var r = await EnviarAsync(HttpMethod.Post, "api/v1/pareamento/validar", authorization, new { codigo }, ct);
        switch (r.Status)
        {
            case (int)HttpStatusCode.Unauthorized or (int)HttpStatusCode.Forbidden:
                throw new JavaErroException("A API Java recusou o token do cuidador. Confira Jwt__Key, Issuer e Audience nas duas APIs.");
            case >= 400:
                throw new RegraNegocioException("Código de pareamento inválido, expirado ou já usado.");
        }

        try
        {
            var idoso = JsonSerializer.Deserialize<IdosoPareado>(r.Corpo, JsonPadrao.Opcoes);
            if (idoso is null || idoso.IdosoId <= 0)
                throw new JavaErroException("A API Java devolveu um pareamento sem idosoId.");
            return idoso;
        }
        catch (JsonException)
        {
            throw new JavaErroException("A API Java devolveu um pareamento em formato inesperado.");
        }
    }
}
