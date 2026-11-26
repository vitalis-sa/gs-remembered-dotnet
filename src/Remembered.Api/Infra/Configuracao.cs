using System.Text;
using System.Threading.RateLimiting;
using Remembered.Api.Comum;
using Remembered.Api.Features.Alertas;
using Remembered.Api.Features.Auth;
using Remembered.Api.Features.Relatorios;
using Remembered.Api.Infra.Data;
using Remembered.Api.Infra.Java;
using Remembered.Api.Infra.Mensageria;
using Remembered.Api.Infra.Push;
using Remembered.Api.Infra.Saude;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Oracle.EntityFrameworkCore.Infrastructure;
using Polly;

namespace Remembered.Api.Infra;

public sealed class LimitesOptions
{
    public const string Secao = "Limites";
    public int RelatoriosPorMinuto { get; set; } = 3;
    public int GeralPorMinuto { get; set; } = 60;
}

/// <summary>
/// Registro de serviços. Toda configuração é lida por IOptions na hora do uso,
/// para os testes conseguirem trocar valores pelo WebApplicationFactory.
/// </summary>
public static class Configuracao
{
    public static IServiceCollection AddRemembered(this IServiceCollection s)
    {
        s.AddOptions<JwtOptions>().BindConfiguration(JwtOptions.Secao)
            .Validate(o => Encoding.UTF8.GetByteCount(o.Key) >= 32, "Jwt:Key precisa ter pelo menos 32 bytes (HS256).")
            .ValidateOnStart();
        s.AddOptions<JavaOptions>().BindConfiguration(JavaOptions.Secao)
            .Validate(o => Uri.TryCreate(o.BaseUrl, UriKind.Absolute, out _), "Java:BaseUrl precisa ser uma URL absoluta.")
            .ValidateOnStart();
        s.AddOptions<RabbitMqOptions>().BindConfiguration(RabbitMqOptions.Secao);
        s.AddOptions<LimitesOptions>().BindConfiguration(LimitesOptions.Secao);
        s.AddOptions<PushOptions>().BindConfiguration(PushOptions.Secao);

        s.AddSingleton(TimeProvider.System);
        s.AddDbContext<AppDbContext>((sp, o) =>
            // O Oracle da FIAP é 19c: sem isto o provider 8.23 gera TRUE/FALSE (sintaxe do 23ai) e dá ORA-00904.
            o.UseOracle(sp.GetRequiredService<IConfiguration>().GetConnectionString("Oracle") ?? "",
                ora => ora.UseOracleSQLCompatibility(OracleSQLCompatibility.DatabaseVersion19)));

        s.AddProblemDetails(o => o.CustomizeProblemDetails = ProblemDetailsConfig.Customizar);
        s.AddExceptionHandler<ExcecaoHandler>();

        s.AddSeguranca();
        s.AddLimites();
        s.AddIntegracoes();
        s.AddSwagger();

        s.AddSingleton<SenhaService>();
        s.AddScoped<TokenService>();
        s.AddScoped<DosePuladaHandler>();

        s.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("oracle", failureStatus: HealthStatus.Unhealthy)
            .AddCheck<JavaHealthCheck>("api-java", failureStatus: HealthStatus.Degraded)
            .AddCheck<RabbitMqHealthCheck>("rabbitmq", failureStatus: HealthStatus.Degraded);
        return s;
    }

    private static void AddSeguranca(this IServiceCollection s)
    {
        s.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        s.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((o, jwt) =>
            {
                var cfg = jwt.Value;
                o.MapInboundClaims = false;
                o.TokenValidationParameters = new()
                {
                    ValidateIssuer = true,
                    ValidIssuers = cfg.TodosIssuers().ToArray(),
                    ValidateAudience = true,
                    ValidAudience = cfg.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = cfg.ChaveSimetrica(),
                    ValidAlgorithms = ["HS256"],
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromMinutes(1),
                    NameClaimType = TiposClaim.Nome,
                    RoleClaimType = TiposClaim.Papel,
                };
            });

        s.AddSingleton<IAuthorizationHandler, CuidadorVinculadoHandler>();
        s.AddAuthorizationBuilder()
            .AddPolicy(Politicas.Cuidador, p => p.RequireAuthenticatedUser().RequireRole(Papeis.Cuidador))
            .AddPolicy(Politicas.CuidadorVinculado, p => p.RequireAuthenticatedUser().AddRequirements(new CuidadorVinculadoRequirement()));
    }

    private static void AddLimites(this IServiceCollection s)
    {
        s.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.OnRejected = async (ctx, ct) =>
            {
                var segundos = 60;
                if (ctx.Lease.TryGetMetadata(MetadataName.RetryAfter, out var espera))
                    segundos = Math.Max(1, (int)Math.Ceiling(espera.TotalSeconds));
                ctx.HttpContext.Response.Headers.RetryAfter = segundos.ToString(System.Globalization.CultureInfo.InvariantCulture);

                var pds = ctx.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
                await pds.WriteAsync(new ProblemDetailsContext
                {
                    HttpContext = ctx.HttpContext,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Muitas tentativas",
                        Detail = $"Aguarde {segundos} segundos e tente de novo.",
                    },
                });
            };

            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                var limite = http.RequestServices.GetRequiredService<IOptions<LimitesOptions>>().Value.GeralPorMinuto;
                return RateLimitPartition.GetFixedWindowLimiter("geral:" + Particao(http), _ => Janela(limite));
            });

            o.AddPolicy(RelatoriosEndpoints.PoliticaLimite, http =>
            {
                var limite = http.RequestServices.GetRequiredService<IOptions<LimitesOptions>>().Value.RelatoriosPorMinuto;
                return RateLimitPartition.GetFixedWindowLimiter("relatorios:" + Particao(http), _ => Janela(limite));
            });
        });
    }

    /// <summary>Por usuário autenticado; sem login, por IP.</summary>
    private static string Particao(HttpContext http) =>
        http.User.Identity?.IsAuthenticated == true
            ? http.User.ChaveUsuario()
            : http.Connection.RemoteIpAddress?.ToString() ?? "anonimo";

    private static FixedWindowRateLimiterOptions Janela(int limite) => new()
    {
        PermitLimit = limite,
        Window = TimeSpan.FromMinutes(1),
        QueueLimit = 0,
        AutoReplenishment = true,
    };

    private static void AddIntegracoes(this IServiceCollection s)
    {
        s.AddHttpClient<JavaApiClient>((sp, c) =>
            {
                c.BaseAddress = BaseJava(sp);
                c.Timeout = Timeout.InfiniteTimeSpan; // o timeout fica no pipeline Polly
            })
            .AddResilienceHandler("java", (pipeline, ctx) =>
            {
                var java = ctx.ServiceProvider.GetRequiredService<IOptions<JavaOptions>>().Value;
                pipeline.AddTimeout(TimeSpan.FromSeconds(java.TimeoutSegundos));

                var retry = new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = 1,
                    Delay = TimeSpan.FromMilliseconds(500),
                    BackoffType = DelayBackoffType.Constant,
                };
                // POST não é repetido: pareamento consome o código e relatório chama o LLM.
                var padrao = retry.ShouldHandle;
                retry.ShouldHandle = args =>
                    args.Context.GetRequestMessage()?.Method == HttpMethod.Get ? padrao(args) : ValueTask.FromResult(false);
                pipeline.AddRetry(retry);

                pipeline.AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 0.5,
                    MinimumThroughput = 5,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = TimeSpan.FromSeconds(15),
                });
            });

        s.AddHttpClient(JavaHealthCheck.NomeCliente, (sp, c) =>
        {
            c.BaseAddress = BaseJava(sp);
            c.Timeout = TimeSpan.FromSeconds(5);
        });

        s.AddHttpClient<IPushSender, ExpoPushSender>(c => c.Timeout = TimeSpan.FromSeconds(10));

        s.AddSingleton<RabbitMqConexao>();
        s.AddSingleton<IPublicadorEventos, RabbitMqPublicador>();
        s.AddHostedService<DosePuladaConsumidor>();
    }

    private static Uri BaseJava(IServiceProvider sp) =>
        new(sp.GetRequiredService<IOptions<JavaOptions>>().Value.BaseUrl.TrimEnd('/') + "/");

    private static void AddSwagger(this IServiceCollection s)
    {
        s.AddEndpointsApiExplorer();
        s.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Remembered · API .NET (cuidador)",
                Version = "v1",
                Description = "Cadastro e login do cuidador, vínculos, alertas de dose pulada, consultas, locais e relatórios (repasse à API Java).",
            });
            var esquema = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Token de POST /api/v1/auth/login (cuidador) ou da API Java (idoso).",
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" },
            };
            o.AddSecurityDefinition("Bearer", esquema);
            o.AddSecurityRequirement(new OpenApiSecurityRequirement { [esquema] = Array.Empty<string>() });
            o.CustomSchemaIds(t => t.FullName?.Replace("Remembered.Api.", "").Replace('+', '.'));
        });
    }
}
