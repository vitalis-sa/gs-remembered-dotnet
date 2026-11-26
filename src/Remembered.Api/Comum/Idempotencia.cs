using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Remembered.Api.Infra.Data;
using Microsoft.EntityFrameworkCore;

namespace Remembered.Api.Comum;

/// <summary>
/// Exige Idempotency-Key e guarda em NET_IDEMPOTENCIA a chave, o hash do corpo e a resposta.
/// Mesma chave e mesmo corpo: devolve a resposta salva sem executar o endpoint.
/// Mesma chave e corpo diferente: 422. Chaves valem 24 h. Respostas 5xx não são guardadas,
/// para o cliente poder tentar de novo depois de uma falha da API Java.
/// </summary>
public sealed class IdempotenciaFilter(AppDbContext db, TimeProvider tempo, ILogger<IdempotenciaFilter> log) : IEndpointFilter
{
    public const string Cabecalho = "Idempotency-Key";
    public const string CabecalhoReplay = "Idempotent-Replayed";
    public static readonly TimeSpan Validade = TimeSpan.FromHours(24);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var http = ctx.HttpContext;
        var ct = http.RequestAborted;
        var chave = http.Request.Headers[Cabecalho].ToString().Trim();
        if (chave.Length is 0 or > 100)
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Idempotency-Key obrigatória",
                detail: $"Envie o cabeçalho {Cabecalho} com até 100 caracteres (um UUID por formulário).");

        var usuario = http.User.ChaveUsuario();
        var hash = CalcularHash(http.Request.Method, http.Request.Path, ctx.Arguments);
        var agora = tempo.GetUtcNow().UtcDateTime;

        var salvo = await db.Idempotencias.FirstOrDefaultAsync(i => i.UsuarioId == usuario && i.Chave == chave, ct);
        if (salvo is not null && salvo.CriadoEm < agora - Validade)
        {
            db.Idempotencias.Remove(salvo);
            await db.SaveChangesAsync(ct);
            salvo = null;
        }

        if (salvo is not null)
        {
            if (salvo.HashCorpo != hash)
                return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "Idempotency-Key reutilizada",
                    detail: "Esta chave já foi usada com outro corpo. Gere uma chave nova para uma requisição nova.");

            http.Response.Headers[CabecalhoReplay] = "true";
            return Results.Content(salvo.Resposta, "application/json", Encoding.UTF8, salvo.StatusCode);
        }

        var resultado = await next(ctx);

        // Executa o resultado num buffer para capturar status e corpo.
        var original = http.Response.Body;
        using var buffer = new MemoryStream();
        http.Response.Body = buffer;
        try
        {
            await ExecutarAsync(resultado, http);
        }
        finally
        {
            http.Response.Body = original;
        }

        var bytes = buffer.ToArray();
        if (http.Response.StatusCode < 500)
        {
            db.Idempotencias.Add(new RegistroIdempotencia
            {
                UsuarioId = usuario,
                Chave = chave,
                HashCorpo = hash,
                StatusCode = http.Response.StatusCode,
                Resposta = Encoding.UTF8.GetString(bytes),
                CriadoEm = agora,
            });
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException ex)
            {
                // Duas requisições simultâneas com a mesma chave: a primeira gravou.
                log.LogWarning(ex, "Chave de idempotência {Chave} já gravada por requisição concorrente", chave);
            }
        }

        await original.WriteAsync(bytes, ct);
        return Results.Empty;
    }

    public static string CalcularHash(string metodo, string caminho, IEnumerable<object?> argumentos)
    {
        var sb = new StringBuilder().Append(metodo).Append(' ').Append(caminho);
        foreach (var arg in argumentos.OfType<IValidavel>())
            sb.Append('\n').Append(JsonSerializer.Serialize(arg, arg.GetType(), JsonPadrao.Opcoes));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    private static Task ExecutarAsync(object? resultado, HttpContext http) => resultado switch
    {
        null => Task.CompletedTask,
        IResult r => r.ExecuteAsync(http),
        _ => Results.Json(resultado).ExecuteAsync(http),
    };
}

public static class IdempotenciaExtensions
{
    public static RouteHandlerBuilder Idempotente(this RouteHandlerBuilder b) =>
        b.AddEndpointFilter<IdempotenciaFilter>()
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
}
