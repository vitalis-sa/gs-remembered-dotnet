using Remembered.Api.Features.Alertas;
using Remembered.Api.Features.Auth;
using Remembered.Api.Features.Consultas;
using Remembered.Api.Features.Locais;
using Remembered.Api.Features.Relatorios;
using Remembered.Api.Features.Vinculos;
using Remembered.Api.Infra;
using Remembered.Api.Infra.Saude;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRemembered();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseSwagger();
app.UseSwaggerUI(o => o.DocumentTitle = "Remembered · API .NET");

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
