using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Remembered.Api.Comum;

public sealed record Pagina<T>(IReadOnlyList<T> Itens, int Numero, int Tamanho, int Total);

public static class Paginacao
{
    public const int TamanhoPadrao = 20;
    public const int TamanhoMaximo = 100;

    /// <summary>Página começa em 0. A query precisa estar ordenada antes.</summary>
    public static async Task<Pagina<T>> PaginarAsync<T>(this IQueryable<T> q, int? pagina, int? tamanho, CancellationToken ct)
    {
        var p = Math.Max(pagina ?? 0, 0);
        var t = Math.Clamp(tamanho ?? TamanhoPadrao, 1, TamanhoMaximo);
        var total = await q.CountAsync(ct);
        var itens = await q.Skip(p * t).Take(t).ToListAsync(ct);
        return new Pagina<T>(itens, p, t, total);
    }
}

public static class JsonPadrao
{
    public static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web);
}
