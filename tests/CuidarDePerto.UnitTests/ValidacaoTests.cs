using CuidarDePerto.Api.Features.Auth;
using CuidarDePerto.Api.Features.Consultas;
using CuidarDePerto.Api.Features.Relatorios;
using CuidarDePerto.Api.Infra.Data;

namespace CuidarDePerto.UnitTests;

public class ValidacaoTests
{
    [Fact(DisplayName = "VAL-01: cadastro recusa senha curta e e-mail inválido")]
    public void Cadastro_invalido()
    {
        var erros = new CadastroRequest("Ana", "nao-e-email", "123").Validar();
        erros.Keys.ShouldBe(["email", "senha"], ignoreOrder: true);
    }

    [Fact(DisplayName = "VAL-02: consulta exige especialidade, local e data futura")]
    public void Consulta_invalida()
    {
        var erros = new ConsultaRequest("", null, DateTimeOffset.UtcNow.AddDays(-1), null, null).Validar();
        erros.Keys.ShouldBe(["especialidade", "localId", "dataHora"], ignoreOrder: true);
    }

    [Theory(DisplayName = "VAL-03: relatório recusa período fora de 1 a 90 dias, como a Java")]
    [InlineData(0)]
    [InlineData(91)]
    public void Relatorio_fora_do_limite(int dias) =>
        new RelatorioRequest(dias).Validar().ShouldContainKey("dias");

    [Theory(DisplayName = "VAL-04: relatório de 7 dias ou sem período (padrão da Java) é válido")]
    [InlineData(7)]
    [InlineData(null)]
    public void Relatorio_valido(int? dias) =>
        new RelatorioRequest(dias).Validar().ShouldBeEmpty();

    [Theory(DisplayName = "VAL-05: colunas seguem o padrão do DDL Oracle")]
    [InlineData("CuidadorId", "CUIDADOR_ID")]
    [InlineData("LembrarAntesMin", "LEMBRAR_ANTES_MIN")]
    [InlineData("Id", "ID")]
    public void Nome_coluna(string propriedade, string coluna) => AppDbContext.NomeColuna(propriedade).ShouldBe(coluna);
}
