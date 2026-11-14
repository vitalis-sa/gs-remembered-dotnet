using System.Net.Mail;

namespace CuidarDePerto.Api.Comum;

/// <summary>DTO de entrada que sabe se validar. Erros saem por campo, em camelCase.</summary>
public interface IValidavel
{
    IDictionary<string, string[]> Validar();
}

public sealed class Validador
{
    private readonly Dictionary<string, List<string>> _erros = new();

    public Validador Regra(string campo, bool valido, string mensagem)
    {
        if (valido) return this;
        if (!_erros.TryGetValue(campo, out var lista)) _erros[campo] = lista = [];
        lista.Add(mensagem);
        return this;
    }

    public Validador Obrigatorio(string campo, string? valor, int max = 200) =>
        string.IsNullOrWhiteSpace(valor)
            ? Regra(campo, false, "Campo obrigatório.")
            : Regra(campo, valor.Trim().Length <= max, $"Use no máximo {max} caracteres.");

    public Validador Opcional(string campo, string? valor, int max) =>
        Regra(campo, valor is null || valor.Trim().Length <= max, $"Use no máximo {max} caracteres.");

    public Validador Email(string campo, string? valor) =>
        string.IsNullOrWhiteSpace(valor)
            ? Regra(campo, false, "Campo obrigatório.")
            : Regra(campo, valor.Length <= 150 && MailAddress.TryCreate(valor.Trim(), out _), "E-mail inválido.");

    public IDictionary<string, string[]> Erros => _erros.ToDictionary(e => e.Key, e => e.Value.ToArray());
}

public sealed class ValidacaoFilter<T> : IEndpointFilter where T : class, IValidavel
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var dto = ctx.Arguments.OfType<T>().FirstOrDefault();
        if (dto is null)
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Dados inválidos", detail: "Corpo da requisição obrigatório.");

        var erros = dto.Validar();
        if (erros.Count > 0)
            return Results.ValidationProblem(erros, title: "Dados inválidos");

        return await next(ctx);
    }
}

public static class ValidacaoExtensions
{
    public static RouteHandlerBuilder Validar<T>(this RouteHandlerBuilder b) where T : class, IValidavel =>
        b.AddEndpointFilter<ValidacaoFilter<T>>().ProducesValidationProblem();
}
