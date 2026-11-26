using System.Text.Json;
using Remembered.Api.Comum;
using RabbitMQ.Client;

namespace Remembered.Api.Infra.Mensageria;

/// <summary>Mensagem de consulta.criada. CriadoPorTipo diz à Java se deve avisar o idoso.</summary>
public sealed record ConsultaCriadaEvento(
    string EventId,
    long ConsultaId,
    long IdosoId,
    string CriadoPorNome,
    string CriadoPorTipo,
    string Especialidade,
    DateTime DataHora,
    string LocalNome);

/// <summary>Mensagem de dose.pulada publicada pela Java. A Java manda LocalDateTime sem fuso (hora de Brasília).</summary>
public sealed record DosePuladaEvento(
    string EventId,
    long DoseId,
    long IdosoId,
    string IdosoNome,
    string MedicamentoNome,
    string Severidade,
    DateTime HorarioPrevisto,
    DateTime OcorridoEm);

public interface IPublicadorEventos
{
    /// <summary>Publica no exchange remembered.eventos. Chamado depois do commit no Oracle.</summary>
    Task PublicarAsync(string routingKey, string eventId, object evento, CancellationToken ct);
}

/// <summary>
/// Publica com confirmação do broker. Se o broker estiver fora, registra erro e segue:
/// a consulta já está gravada e o idoso continua vendo-a pelo app (sem outbox, limitação registrada no README).
/// </summary>
public sealed class RabbitMqPublicador(RabbitMqConexao conexao, ILogger<RabbitMqPublicador> log) : IPublicadorEventos
{
    public Task PublicarAsync(string routingKey, string eventId, object evento, CancellationToken ct)
    {
        if (!conexao.Habilitado)
        {
            log.LogInformation("RabbitMQ desabilitado; evento {Rota} {EventId} não publicado", routingKey, eventId);
            return Task.CompletedTask;
        }

        try
        {
            using var canal = conexao.Obter().CreateModel();
            canal.ConfirmSelect();
            Topologia.DeclararExchange(canal);

            var props = canal.CreateBasicProperties();
            props.Persistent = true;
            props.ContentType = "application/json";
            props.MessageId = eventId;
            props.Type = routingKey;
            props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

            var corpo = JsonSerializer.SerializeToUtf8Bytes(evento, evento.GetType(), JsonPadrao.Opcoes);
            canal.BasicPublish(Topologia.Exchange, routingKey, props, corpo);
            canal.WaitForConfirmsOrDie(TimeSpan.FromSeconds(5));
            log.LogInformation("Evento {Rota} {EventId} publicado", routingKey, eventId);
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Falha ao publicar {Rota} {EventId}", routingKey, eventId);
        }
        return Task.CompletedTask;
    }
}
