using CuidarDePerto.Api.Features.Alertas;
using CuidarDePerto.Api.Features.Auth;
using CuidarDePerto.Api.Features.Consultas;
using CuidarDePerto.Api.Features.Locais;
using CuidarDePerto.Api.Features.Relatorios;
using CuidarDePerto.Api.Features.Vinculos;
using CuidarDePerto.Api.Infra;
using CuidarDePerto.Api.Infra.Saude;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCuidarDePerto();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseSwagger();
app.UseSwaggerUI(o => o.DocumentTitle = "Cuidar de Perto · API .NET");

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

var api = app.MapGroup("/api/v1");
api.MapAuth();
api.MapVinculos();
api.MapAlertas();
api.MapConsultas();
api.MapLocais();
api.MapRelatorios();

app.MapHealthChecks("/health", Saude.Opcoes).DisableRateLimiting();
app.MapHealthChecks("/api/v1/health", Saude.Opcoes).DisableRateLimiting();
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();

app.Run();

public partial class Program;
