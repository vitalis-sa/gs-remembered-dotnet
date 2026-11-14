using System.Security.Claims;
using CuidarDePerto.Api.Comum;
using CuidarDePerto.Api.Infra.Data;
using CuidarDePerto.Api.Infra.Push;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CuidarDePerto.UnitTests;

/// <summary>Banco SQLite em memória por teste.</summary>
public sealed class BancoTeste : IDisposable
{
    private readonly SqliteConnection _conexao = new("DataSource=:memory:");

    public BancoTeste()
    {
        _conexao.Open();
        using var db = NovoContexto();
        db.Database.EnsureCreated();
    }

    public AppDbContext NovoContexto() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conexao).Options);

    public void Dispose() => _conexao.Dispose();
}

public sealed class PushFalso : IPushSender
{
    public List<(string Token, string Titulo, string Corpo)> Enviados { get; } = [];

    public Task EnviarAsync(string pushToken, string titulo, string corpo, object? dados, CancellationToken ct)
    {
        Enviados.Add((pushToken, titulo, corpo));
        return Task.CompletedTask;
    }
}

public sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
}

public static class Usuarios
{
    public static ClaimsPrincipal Cuidador(long id, params long[] idosos)
    {
        var claims = new List<Claim>
        {
            new(TiposClaim.Sub, id.ToString()),
            new(TiposClaim.Papel, Papeis.Cuidador),
            new(TiposClaim.Nome, "Ana"),
        };
        claims.AddRange(idosos.Select(i => new Claim(TiposClaim.Idosos, i.ToString())));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "teste", TiposClaim.Nome, TiposClaim.Papel));
    }

    public static ClaimsPrincipal Idoso(long id) => new(new ClaimsIdentity(
    [
        new Claim(TiposClaim.Sub, id.ToString()),
        new Claim(TiposClaim.Papel, Papeis.Idoso),
        new Claim(TiposClaim.Nome, "Maria"),
    ], "teste", TiposClaim.Nome, TiposClaim.Papel));
}
