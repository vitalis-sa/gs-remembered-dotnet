using System.Net.Http.Json;

namespace CuidarDePerto.Api.Infra.Push;

public interface IPushSender
{
    /// <summary>Envia um push. Falha de envio é registrada em log e não interrompe o fluxo.</summary>
    Task EnviarAsync(string pushToken, string titulo, string corpo, object? dados, CancellationToken ct);
}

public sealed class PushOptions
{
    public const string Secao = "Push";
    public string ExpoUrl { get; set; } = "https://exp.host/--/api/v2/push/send";
    /// <summary>Opcional: token de acesso do Expo, quando o projeto exige push seguro.</summary>
    public string? ExpoAccessToken { get; set; }
}

public sealed class ExpoPushSender(HttpClient http, Microsoft.Extensions.Options.IOptions<PushOptions> opcoes, ILogger<ExpoPushSender> log) : IPushSender
{
    public async Task EnviarAsync(string pushToken, string titulo, string corpo, object? dados, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(pushToken)) return;
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, opcoes.Value.ExpoUrl)
            {
                Content = JsonContent.Create(new
                {
                    to = pushToken,
                    title = titulo,
                    body = corpo,
                    data = dados,
                    sound = "default",
                    priority = "high",
                    channelId = "alertas",
                }),
            };
            if (!string.IsNullOrWhiteSpace(opcoes.Value.ExpoAccessToken))
                req.Headers.Authorization = new("Bearer", opcoes.Value.ExpoAccessToken);

            using var resp = await http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
                log.LogWarning("Expo recusou o push: {Status} {Corpo}", (int)resp.StatusCode, await resp.Content.ReadAsStringAsync(ct));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "Falha ao enviar push pelo Expo");
        }
    }
}
