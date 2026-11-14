using System.Text;
using System.Text.Json;
using CuidarDePerto.Api.Comum;
using CuidarDePerto.Api.Features.Alertas;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CuidarDePerto.Api.Infra.Mensageria;

/// <summary>
/// Consome dose.pulada com confirmação manual. Falha: até MaxTentativas, com espera de 5 s e 25 s
/// na fila de retry; depois disso a mensagem vai para a DLQ. A idempotência por eventId fica no handler.
/// </summary>
public sealed class DosePuladaConsumidor(
    RabbitMqConexao conexao,
    IServiceScopeFactory escopos,
    IOptions<RabbitMqOptions> opcoes,
    ILogger<DosePuladaConsumidor> log) : BackgroundService
{
    private const string CabecalhoTentativas = "x-tentativas";
    private IModel? _canal;

    protected override async Task ExecuteAsync(CancellationToken parar)
    {
        if (!opcoes.Value.Enabled)
        {
            log.LogInformation("RabbitMQ desabilitado; consumidor de dose.pulada não iniciado");
            return;
        }

        while (!parar.IsCancellationRequested)
        {
            try
            {
                var canal = conexao.Obter().CreateModel();
                Topologia.DeclararFilasDoConsumidor(canal);
                canal.BasicQos(0, 10, false);

                var consumidor = new AsyncEventingBasicConsumer(canal);
                consumidor.Received += (_, ea) => TratarAsync(canal, ea, parar);
                canal.BasicConsume(Topologia.FilaAlertas, autoAck: false, consumer: consumidor);
                _canal = canal;
                log.LogInformation("Consumindo {Fila}", Topologia.FilaAlertas);

                // A recuperação automática do cliente reabre canal e consumidor se o broker cair.
                await Task.Delay(Timeout.Infinite, parar);
            }
            catch (OperationCanceledException) when (parar.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "RabbitMQ indisponível; nova tentativa em 15 s");
                try { await Task.Delay(TimeSpan.FromSeconds(15), parar); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private async Task TratarAsync(IModel canal, BasicDeliverEventArgs ea, CancellationToken ct)
    {
        DosePuladaEvento? evento;
        try
        {
            evento = JsonSerializer.Deserialize<DosePuladaEvento>(ea.Body.Span, JsonPadrao.Opcoes);
        }
        catch (JsonException ex)
        {
            log.LogError(ex, "Mensagem dose.pulada ilegível; enviada à DLQ");
            canal.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        if (evento is null || string.IsNullOrWhiteSpace(evento.EventId))
        {
            log.LogError("Mensagem dose.pulada sem eventId; enviada à DLQ");
            canal.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
            return;
        }

        try
        {
            using var escopo = escopos.CreateScope();
            var handler = escopo.ServiceProvider.GetRequiredService<DosePuladaHandler>();
            await handler.ProcessarAsync(evento, ct);
            canal.BasicAck(ea.DeliveryTag, multiple: false);
        }
        catch (Exception ex)
        {
            var tentativa = LerTentativas(ea.BasicProperties) + 1;
            if (tentativa >= opcoes.Value.MaxTentativas)
            {
                log.LogError(ex, "dose.pulada {EventId} falhou {Tentativa} vezes; enviada à DLQ", evento.EventId, tentativa);
                canal.BasicNack(ea.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            var espera = TimeSpan.FromSeconds(5 * Math.Pow(5, tentativa - 1));
            log.LogWarning(ex, "dose.pulada {EventId} falhou (tentativa {Tentativa}); nova tentativa em {Espera}", evento.EventId, tentativa, espera);

            var props = canal.CreateBasicProperties();
            props.Persistent = true;
            props.ContentType = "application/json";
            props.MessageId = evento.EventId;
            props.Expiration = ((long)espera.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            props.Headers = new Dictionary<string, object> { [CabecalhoTentativas] = tentativa };
            canal.BasicPublish("", Topologia.FilaAlertasRetry, props, ea.Body);
            canal.BasicAck(ea.DeliveryTag, multiple: false);
        }
    }

    private static int LerTentativas(IBasicProperties? props)
    {
        if (props?.Headers is null || !props.Headers.TryGetValue(CabecalhoTentativas, out var valor)) return 0;
        return valor switch
        {
            int i => i,
            long l => (int)l,
            byte[] b when int.TryParse(Encoding.UTF8.GetString(b), out var p) => p,
            _ => 0,
        };
    }

    public override void Dispose()
    {
        _canal?.Dispose();
        base.Dispose();
    }
}
