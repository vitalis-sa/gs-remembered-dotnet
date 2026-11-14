using CuidarDePerto.Api.Features.Alertas;
using CuidarDePerto.Api.Infra.Data;
using CuidarDePerto.Api.Infra.Mensageria;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CuidarDePerto.UnitTests;

public sealed class DosePuladaHandlerTests : IDisposable
{
    private readonly BancoTeste _banco = new();
    private readonly PushFalso _push = new();
    private readonly RelogioFixo _relogio = new(new DateTimeOffset(2026, 11, 14, 12, 0, 0, TimeSpan.Zero));

    public DosePuladaHandlerTests()
    {
        using var db = _banco.NovoContexto();
        var ana = new Cuidador { Nome = "Ana", Email = "ana@x.com", SenhaHash = "h", PushToken = "ExponentPushToken[ana]" };
        var bia = new Cuidador { Nome = "Bia", Email = "bia@x.com", SenhaHash = "h" };
        var caio = new Cuidador { Nome = "Caio", Email = "caio@x.com", SenhaHash = "h" };
        db.Cuidadores.AddRange(ana, bia, caio);
        db.SaveChanges();
        db.Vinculos.AddRange(
            new Vinculo { CuidadorId = ana.Id, IdosoId = 10, IdosoNome = "Maria" },
            new Vinculo { CuidadorId = bia.Id, IdosoId = 10, IdosoNome = "Maria" },
            new Vinculo { CuidadorId = caio.Id, IdosoId = 20, IdosoNome = "José" });
        db.SaveChanges();
    }

    private static DosePuladaEvento Evento(string eventId = "e1", long doseId = 500, string severidade = "ALTA") =>
        new(eventId, doseId, 10, "Maria", "Losartana", severidade,
            new DateTimeOffset(2026, 11, 14, 8, 0, 0, TimeSpan.FromHours(-3)), DateTimeOffset.UtcNow);

    private async Task<int> ProcessarAsync(DosePuladaEvento e)
    {
        await using var db = _banco.NovoContexto();
        return await new DosePuladaHandler(db, _push, _relogio, NullLogger<DosePuladaHandler>.Instance).ProcessarAsync(e, default);
    }

    private int ContarAlertas()
    {
        using var db = _banco.NovoContexto();
        return db.Alertas.Count();
    }

    [Fact(DisplayName = "ALE-01: cria um alerta por cuidador vinculado ao idoso")]
    public async Task Um_alerta_por_cuidador()
    {
        (await ProcessarAsync(Evento())).ShouldBe(2);

        await using var db = _banco.NovoContexto();
        var alertas = await db.Alertas.ToListAsync();
        alertas.Count.ShouldBe(2);
        alertas.ShouldAllBe(a => a.IdosoId == 10 && a.Severidade == "ALTA" && a.DoseId == 500 && a.LidoEm == null);
    }

    [Fact(DisplayName = "ALE-02: evento repetido (mesmo eventId) não duplica alerta")]
    public async Task Evento_repetido()
    {
        await ProcessarAsync(Evento());
        (await ProcessarAsync(Evento())).ShouldBe(0);
        ContarAlertas().ShouldBe(2);
    }

    [Fact(DisplayName = "ALE-03: outro eventId para a mesma dose não cria segundo alerta")]
    public async Task Mesma_dose_outro_evento()
    {
        await ProcessarAsync(Evento("e1"));
        (await ProcessarAsync(Evento("e2"))).ShouldBe(0);
        ContarAlertas().ShouldBe(2);
    }

    [Fact(DisplayName = "ALE-04: push só para cuidador com token, com texto da tela de bloqueio")]
    public async Task Push_com_texto()
    {
        await ProcessarAsync(Evento());
        var push = _push.Enviados.ShouldHaveSingleItem();
        push.Token.ShouldBe("ExponentPushToken[ana]");
        push.Titulo.ShouldBe("Dose pulada: Losartana (importante)");
        push.Corpo.ShouldBe("Maria não confirmou a dose das 08:00");
    }

    [Fact(DisplayName = "ALE-05: severidade NENHUM não cria alerta")]
    public async Task Severidade_nenhum()
    {
        (await ProcessarAsync(Evento(severidade: "NENHUM"))).ShouldBe(0);
        ContarAlertas().ShouldBe(0);
    }

    [Fact(DisplayName = "ALE-06: severidade MEDIA não leva '(importante)' no título")]
    public void Titulo_media() => DosePuladaHandler.Titulo(Evento(severidade: "MEDIA")).ShouldBe("Dose pulada: Losartana");

    public void Dispose() => _banco.Dispose();
}
