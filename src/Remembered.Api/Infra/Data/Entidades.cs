namespace Remembered.Api.Infra.Data;

public class Cuidador
{
    public long Id { get; set; }
    public string Nome { get; set; } = "";
    public string Email { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public string? PushToken { get; set; }
    public DateTime CriadoEm { get; set; }
}

/// <summary>IdosoId aponta para TB_USUARIO da API Java, sem chave estrangeira.</summary>
public class Vinculo
{
    public long Id { get; set; }
    public long CuidadorId { get; set; }
    public long IdosoId { get; set; }
    public string IdosoNome { get; set; } = "";
    public string? Parentesco { get; set; }
    public DateTime CriadoEm { get; set; }
    public Cuidador? Cuidador { get; set; }
}

public static class Severidades
{
    public const string Alta = "ALTA";
    public const string Media = "MEDIA";
    public static bool Valida(string? s) => s is Alta or Media;
}

public class Alerta
{
    public long Id { get; set; }
    public long CuidadorId { get; set; }
    public long IdosoId { get; set; }
    public string IdosoNome { get; set; } = "";
    public string MedicamentoNome { get; set; } = "";
    public long DoseId { get; set; }
    public string Severidade { get; set; } = Severidades.Media;
    public string Motivo { get; set; } = "";
    public DateTime CriadoEm { get; set; }
    public DateTime? LidoEm { get; set; }
}

public class LocalAtendimento
{
    public long Id { get; set; }
    public string Nome { get; set; } = "";
    public string Endereco { get; set; } = "";
    public string? Telefone { get; set; }
    public string CriadoPorTipo { get; set; } = "";
    public long CriadoPorId { get; set; }
}

public static class StatusConsulta
{
    public const string Agendada = "AGENDADA";
    public const string Realizada = "REALIZADA";
    public const string Cancelada = "CANCELADA";
    public static readonly string[] Todos = [Agendada, Realizada, Cancelada];
}

public class Consulta
{
    public long Id { get; set; }
    public long IdosoId { get; set; }
    public long LocalId { get; set; }
    public string Especialidade { get; set; } = "";
    public DateTime DataHora { get; set; }
    public string Status { get; set; } = StatusConsulta.Agendada;
    public string? Observacoes { get; set; }
    public int LembrarAntesMin { get; set; } = 60;
    public string CriadoPorTipo { get; set; } = "";
    public string CriadoPorNome { get; set; } = "";
    public LocalAtendimento? Local { get; set; }
}

/// <summary>UsuarioId guarda "PAPEL:id" porque idoso e cuidador têm sequências de id diferentes.</summary>
public class RegistroIdempotencia
{
    public string UsuarioId { get; set; } = "";
    public string Chave { get; set; } = "";
    public string HashCorpo { get; set; } = "";
    public int StatusCode { get; set; }
    public string Resposta { get; set; } = "";
    public DateTime CriadoEm { get; set; }
}

/// <summary>Um registro por eventId consumido do RabbitMQ.</summary>
public class EventoProcessado
{
    public string EventId { get; set; } = "";
    public DateTime ProcessadoEm { get; set; }
}
