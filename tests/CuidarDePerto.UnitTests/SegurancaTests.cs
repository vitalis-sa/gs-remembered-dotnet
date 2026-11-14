using System.Text;
using System.Text.Json;
using CuidarDePerto.Api.Comum;
using CuidarDePerto.Api.Features.Auth;
using CuidarDePerto.Api.Infra.Data;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CuidarDePerto.UnitTests;

public class AcessoIdosoTests
{
    [Fact(DisplayName = "VIN-01: cuidador acessa idoso que está na claim idosos")]
    public void Cuidador_com_vinculo() => AcessoIdoso.PodeAcessar(Usuarios.Cuidador(1, 10, 20), 20).ShouldBeTrue();

    [Fact(DisplayName = "VIN-02: cuidador não acessa idoso fora da claim idosos")]
    public void Cuidador_sem_vinculo() => AcessoIdoso.PodeAcessar(Usuarios.Cuidador(1, 10), 99).ShouldBeFalse();

    [Fact(DisplayName = "VIN-03: cuidador sem nenhum vínculo não acessa ninguém")]
    public void Cuidador_sem_idosos() => AcessoIdoso.PodeAcessar(Usuarios.Cuidador(1), 10).ShouldBeFalse();

    [Fact(DisplayName = "VIN-04: idoso acessa só o próprio id")]
    public void Idoso_proprio()
    {
        AcessoIdoso.PodeAcessar(Usuarios.Idoso(10), 10).ShouldBeTrue();
        AcessoIdoso.PodeAcessar(Usuarios.Idoso(10), 11).ShouldBeFalse();
    }

    [Fact(DisplayName = "VIN-05: id do cuidador igual ao do idoso não dá acesso")]
    public void Sub_do_cuidador_nao_vale_como_idoso() => AcessoIdoso.PodeAcessar(Usuarios.Cuidador(10), 10).ShouldBeFalse();
}

public class TokenServiceTests
{
    private const string Chave = "chave-de-teste-com-pelo-menos-32-bytes!!";

    private static TokenService Servico() => new(Options.Create(new JwtOptions
    {
        Key = Chave,
        Issuer = "cuidar-de-perto-dotnet",
        Audience = "cuidar-de-perto",
    }), new RelogioFixo(DateTimeOffset.UtcNow));

    private static JsonElement Payload(string token)
    {
        var parte = token.Split('.')[1];
        return JsonDocument.Parse(Base64UrlEncoder.Decode(parte)).RootElement;
    }

    [Theory(DisplayName = "JWT-01: claim idosos sai sempre como array JSON")]
    [InlineData(new long[0])]
    [InlineData(new long[] { 7 })]
    [InlineData(new long[] { 9, 7, 7 })]
    public void Idosos_e_array(long[] idosos)
    {
        var token = Servico().Gerar(new Cuidador { Id = 3, Nome = "Ana", Email = "ana@x.com" }, idosos).Token;
        var claim = Payload(token).GetProperty("idosos");
        claim.ValueKind.ShouldBe(JsonValueKind.Array);
        claim.EnumerateArray().Select(e => e.GetInt64()).ShouldBe(idosos.Distinct().Order());
    }

    [Fact(DisplayName = "JWT-02: token traz sub, role CUIDADOR, nome e valida com a chave comum")]
    public async Task Claims_e_assinatura()
    {
        var token = Servico().Gerar(new Cuidador { Id = 3, Nome = "Ana", Email = "ana@x.com" }, [10]).Token;
        var p = Payload(token);
        p.GetProperty("sub").GetString().ShouldBe("3");
        p.GetProperty("role").GetString().ShouldBe("CUIDADOR");
        p.GetProperty("nome").GetString().ShouldBe("Ana");

        var resultado = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = "cuidar-de-perto-dotnet",
            ValidAudience = "cuidar-de-perto",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Chave)),
        });
        resultado.IsValid.ShouldBeTrue();
        resultado.SecurityToken.ShouldBeOfType<JsonWebToken>().Alg.ShouldBe("HS256");
    }
}

public class SenhaServiceTests
{
    private readonly SenhaService _senhas = new();

    [Fact(DisplayName = "SEN-01: hash não contém a senha e confere com a senha certa")]
    public void Hash_e_conferencia()
    {
        var hash = _senhas.Hash("minhaSenha123");
        hash.ShouldNotContain("minhaSenha123");
        _senhas.Verificar(hash, "minhaSenha123").ShouldBeTrue();
    }

    [Fact(DisplayName = "SEN-02: senha errada não confere")]
    public void Senha_errada() => _senhas.Verificar(_senhas.Hash("minhaSenha123"), "outraSenha").ShouldBeFalse();

    [Fact(DisplayName = "SEN-03: mesma senha gera hashes diferentes (salt)")]
    public void Salt() => _senhas.Hash("abcdefgh").ShouldNotBe(_senhas.Hash("abcdefgh"));
}
