using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Remembered.Api.Comum;

public class ErroDeNegocioException(string mensagem, IDictionary<string, object?>? extras = null) : Exception(mensagem)
{
    public IDictionary<string, object?>? Extras { get; } = extras;
}

public sealed class NaoEncontradoException(string mensagem) : ErroDeNegocioException(mensagem);

public sealed class ConflitoException(string mensagem, IDictionary<string, object?>? extras = null)
    : ErroDeNegocioException(mensagem, extras);

/// <summary>Dado válido no formato, mas recusado pela regra (422).</summary>
public sealed class RegraNegocioException(string mensagem) : ErroDeNegocioException(mensagem);

/// <summary>Java fora do ar, timeout ou circuito aberto (503).</summary>
public sealed class JavaIndisponivelException(string mensagem, Exception? interna = null) : Exception(mensagem, interna);

/// <summary>Java respondeu 5xx ou algo que não dá para usar (502).</summary>
public sealed class JavaErroException(string mensagem) : Exception(mensagem);

/// <summary>Converte toda exceção em ProblemDetails. Nunca devolve stack trace.</summary>
public sealed class ExcecaoHandler(IProblemDetailsService problemDetails, ILogger<ExcecaoHandler> log) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, titulo) = ex switch
        {
            NaoEncontradoException => (StatusCodes.Status404NotFound, "Não encontrado"),
            ConflitoException => (StatusCodes.Status409Conflict, "Conflito"),
            RegraNegocioException => (StatusCodes.Status422UnprocessableEntity, "Regra de negócio"),
            JavaIndisponivelException => (StatusCodes.Status503ServiceUnavailable, "API Java indisponível"),
            JavaErroException => (StatusCodes.Status502BadGateway, "Falha na API Java"),
            BadHttpRequestException b => (b.StatusCode, "Requisição inválida"),
            _ => (StatusCodes.Status500InternalServerError, "Erro inesperado"),
        };

        if (status >= 500) log.LogError(ex, "Erro tratado como {Status}", status);

        var pd = new ProblemDetails
        {
            Status = status,
            Title = titulo,
            Detail = status == StatusCodes.Status500InternalServerError ? "Tente de novo em instantes." : ex.Message,
        };
        if (ex is ErroDeNegocioException { Extras: not null } negocio)
            foreach (var (k, v) in negocio.Extras) pd.Extensions[k] = v;

        http.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext { HttpContext = http, ProblemDetails = pd, Exception = ex });
    }
}

public static class ProblemDetailsConfig
{
    /// <summary>Acrescenta path e traceId a todo ProblemDetails.</summary>
    public static void Customizar(ProblemDetailsContext ctx)
    {
        ctx.ProblemDetails.Extensions["path"] = ctx.HttpContext.Request.Path.Value;
        ctx.ProblemDetails.Extensions["traceId"] = ctx.HttpContext.TraceIdentifier;
    }
}
