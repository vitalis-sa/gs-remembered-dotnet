using CuidarDePerto.Api.Comum;
using CuidarDePerto.Api.Infra.Data;
using CuidarDePerto.Api.Infra.Mensageria;
using CuidarDePerto.Api.Infra.Push;
using Microsoft.EntityFrameworkCore;

namespace CuidarDePerto.Api.Features.Alertas;

/// <summary>
/// Cria um alerta por cuidador vinculado ao idoso, no máximo um por dose (único cuidador_id + dose_id),
/// registra o eventId como processado na mesma transação e depois envia o push.
/// </summary>
public sealed class DosePuladaHandler(AppDbContext db, IPushSender push, TimeProvider tempo, ILogger<DosePuladaHandler> log)
{
    /// <returns>Quantidade de alertas criados.</returns>
    public async Task<int> ProcessarAsync(DosePuladaEvento evento, CancellationToken ct)
    {
        if (await db.EventosProcessados.AnyAsync(e => e.EventId == evento.EventId, ct))
        {
            log.LogInformation("Evento {EventId} já processado; ignorado", evento.EventId);
            return 0;
        }

        var agora = tempo.GetUtcNow().UtcDateTime;
        var novos = new List<(Alerta Alerta, string? PushToken)>();

        if (Severidades.Valida(evento.Severidade))
        {
            var vinculos = await db.Vinculos.Include(v => v.Cuidador)
                .Where(v => v.IdosoId == evento.IdosoId).ToListAsync(ct);
            var jaAlertados = await db.Alertas.Where(a => a.DoseId == evento.DoseId)
                .Select(a => a.CuidadorId).ToListAsync(ct);

            foreach (var v in vinculos.Where(v => !jaAlertados.Contains(v.CuidadorId)))
            {
                var alerta = new Alerta
                {
                    CuidadorId = v.CuidadorId,
                    IdosoId = evento.IdosoId,
                    IdosoNome = evento.IdosoNome,
                    MedicamentoNome = evento.MedicamentoNome,
                    DoseId = evento.DoseId,
                    Severidade = evento.Severidade,
                    Motivo = Motivo(evento.Severidade),
                    CriadoEm = agora,
                };
                db.Alertas.Add(alerta);
                novos.Add((alerta, v.Cuidador?.PushToken));
            }
        }
        else
        {
            log.LogWarning("Evento {EventId} com severidade {Severidade}; nenhum alerta criado", evento.EventId, evento.Severidade);
        }

        db.EventosProcessados.Add(new EventoProcessado { EventId = evento.EventId, ProcessadoEm = agora });
        await db.SaveChangesAsync(ct);

        foreach (var (alerta, token) in novos.Where(n => !string.IsNullOrWhiteSpace(n.PushToken)))
        {
            await push.EnviarAsync(token!, Titulo(evento), Corpo(evento),
                new { tipo = "ALERTA", alertaId = alerta.Id, idosoId = alerta.IdosoId }, ct);
        }
        return novos.Count;
    }

    public static string Motivo(string severidade) => severidade == Severidades.Alta
        ? "Remédio importante não foi tomado."
        : "Doses puladas chegaram ao limite nos últimos 7 dias.";

    public static string Titulo(DosePuladaEvento e) =>
        $"Dose pulada: {e.MedicamentoNome}{(e.Severidade == Severidades.Alta ? " (importante)" : "")}";

    public static string Corpo(DosePuladaEvento e)
    {
        var hora = HoraBrasil.ParaBrasil(e.HorarioPrevisto);
        return $"{e.IdosoNome} não confirmou a dose das {hora:HH\\:mm}";
    }
}
