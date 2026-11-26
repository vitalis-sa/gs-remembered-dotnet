namespace Remembered.Api.Comum;

/// <summary>
/// A API Java troca datas como LocalDateTime (sem fuso) na hora de Brasília.
/// Esta API grava em UTC, então converte na entrada e na saída das mensagens.
/// </summary>
public static class HoraBrasil
{
    public static readonly TimeZoneInfo Fuso = CarregarFuso();

    /// <summary>UTC do banco para a hora de parede de Brasília (Kind Unspecified, serializa sem fuso).</summary>
    public static DateTime DeUtc(DateTime utc) =>
        DateTime.SpecifyKind(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), Fuso), DateTimeKind.Unspecified);

    /// <summary>Sem fuso já é hora de Brasília; com fuso (Utc ou Local) é convertida.</summary>
    public static DateTime ParaBrasil(DateTime valor) =>
        valor.Kind == DateTimeKind.Unspecified ? valor : TimeZoneInfo.ConvertTime(valor, Fuso);

    private static TimeZoneInfo CarregarFuso()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "Brasília", "Brasília");
        }
    }
}
