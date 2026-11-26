using System.Text.Json;
using Remembered.Api.Comum;
using Remembered.Api.Infra.Mensageria;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Remembered.Api.Infra.Saude;

/// <summary>GET /actuator/health da Java. Falha deixa a API Degraded, não Unhealthy.</summary>
public sealed class JavaHealthCheck(IHttpClientFactory clientes) : IHealthCheck
{
    public const string NomeCliente = "java-health";

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            using var resp = await clientes.CreateClient(NomeCliente).GetAsync("actuator/health", ct);
            return resp.IsSuccessStatusCode
                ? HealthCheckResult.Healthy("API Java respondendo")
                : new HealthCheckResult(context.Registration.FailureStatus, $"API Java respondeu {(int)resp.StatusCode}");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "API Java fora do ar", ex);
        }
    }
}

public sealed class RabbitMqHealthCheck(RabbitMqConexao conexao) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (!conexao.Habilitado) return Task.FromResult(HealthCheckResult.Healthy("RabbitMQ desabilitado nesta configuração"));
        try
        {
            return Task.FromResult(conexao.Obter().IsOpen
                ? HealthCheckResult.Healthy("Broker conectado")
                : new HealthCheckResult(context.Registration.FailureStatus, "Conexão com o broker fechada"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new HealthCheckResult(context.Registration.FailureStatus, "Broker fora do ar", ex));
        }
    }
}

public static class Saude
{
    public static readonly HealthCheckOptions Opcoes = new() { ResponseWriter = EscreverAsync };

    private static Task EscreverAsync(HttpContext http, HealthReport relatorio)
    {
        http.Response.ContentType = "application/json";
        var corpo = new
        {
            status = relatorio.Status.ToString(),
            duracaoMs = (int)relatorio.TotalDuration.TotalMilliseconds,
            verificacoes = relatorio.Entries.ToDictionary(
                e => e.Key,
                e => new { status = e.Value.Status.ToString(), descricao = e.Value.Description, duracaoMs = (int)e.Value.Duration.TotalMilliseconds }),
        };
        return http.Response.WriteAsync(JsonSerializer.Serialize(corpo, JsonPadrao.Opcoes));
    }
}
