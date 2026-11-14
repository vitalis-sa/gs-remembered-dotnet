using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace CuidarDePerto.Api.Infra.Mensageria;

public sealed class RabbitMqOptions
{
    public const string Secao = "RabbitMq";
    public bool Enabled { get; set; } = true;
    /// <summary>Se preenchida (ex.: amqps://... do CloudAMQP), tem prioridade sobre Host/Port/Usuario/Senha.</summary>
    public string? Uri { get; set; }
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Usuario { get; set; } = "guest";
    public string Senha { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public int MaxTentativas { get; set; } = 3;
}

/// <summary>Nomes combinados com a API Java.</summary>
public static class Topologia
{
    public const string Exchange = "cuidar.eventos";
    public const string ExchangeMortas = "cuidar.eventos.dlx";

    public const string FilaAlertas = "cuidador.alerta-dose";
    public const string FilaAlertasRetry = "cuidador.alerta-dose.retry";
    public const string FilaAlertasMortas = "cuidador.alerta-dose.dlq";

    public static void DeclararExchange(IModel canal) =>
        canal.ExchangeDeclare(Exchange, ExchangeType.Topic, durable: true, autoDelete: false);

    /// <summary>
    /// Fila principal ligada a dose.pulada; falha definitiva vai para a DLQ pela DLX;
    /// a fila de retry segura a mensagem pelo TTL e devolve à principal.
    /// </summary>
    public static void DeclararFilasDoConsumidor(IModel canal)
    {
        DeclararExchange(canal);
        canal.ExchangeDeclare(ExchangeMortas, ExchangeType.Direct, durable: true, autoDelete: false);

        canal.QueueDeclare(FilaAlertasMortas, durable: true, exclusive: false, autoDelete: false);
        canal.QueueBind(FilaAlertasMortas, ExchangeMortas, FilaAlertas);

        canal.QueueDeclare(FilaAlertas, durable: true, exclusive: false, autoDelete: false, arguments: new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = ExchangeMortas,
            ["x-dead-letter-routing-key"] = FilaAlertas,
        });
        canal.QueueBind(FilaAlertas, Exchange, RotasEventos.DosePulada);

        canal.QueueDeclare(FilaAlertasRetry, durable: true, exclusive: false, autoDelete: false, arguments: new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = "",
            ["x-dead-letter-routing-key"] = FilaAlertas,
        });
    }
}

public static class RotasEventos
{
    public const string DosePulada = "dose.pulada";
    public const string ConsultaCriada = "consulta.criada";
}

/// <summary>Conexão única e preguiçosa: a API sobe mesmo com o broker fora do ar.</summary>
public sealed class RabbitMqConexao(IOptions<RabbitMqOptions> opcoes) : IDisposable
{
    private readonly object _trava = new();
    private IConnection? _conexao;

    public bool Habilitado => opcoes.Value.Enabled;

    public IConnection Obter()
    {
        lock (_trava)
        {
            if (_conexao is { IsOpen: true }) return _conexao;
            _conexao?.Dispose();
            _conexao = CriarFactory().CreateConnection("cuidar-de-perto-dotnet");
            return _conexao;
        }
    }

    private ConnectionFactory CriarFactory()
    {
        var o = opcoes.Value;
        var f = new ConnectionFactory
        {
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10),
            RequestedConnectionTimeout = TimeSpan.FromSeconds(10),
        };
        if (!string.IsNullOrWhiteSpace(o.Uri))
        {
            f.Uri = new Uri(o.Uri);
        }
        else
        {
            f.HostName = o.Host;
            f.Port = o.Port;
            f.UserName = o.Usuario;
            f.Password = o.Senha;
            f.VirtualHost = o.VirtualHost;
        }
        return f;
    }

    public void Dispose()
    {
        lock (_trava) _conexao?.Dispose();
    }
}
