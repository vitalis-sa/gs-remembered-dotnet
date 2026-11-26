using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Remembered.Api.Infra.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Cuidador> Cuidadores => Set<Cuidador>();
    public DbSet<Vinculo> Vinculos => Set<Vinculo>();
    public DbSet<Alerta> Alertas => Set<Alerta>();
    public DbSet<LocalAtendimento> Locais => Set<LocalAtendimento>();
    public DbSet<Consulta> Consultas => Set<Consulta>();
    public DbSet<RegistroIdempotencia> Idempotencias => Set<RegistroIdempotencia>();
    public DbSet<EventoProcessado> EventosProcessados => Set<EventoProcessado>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Cuidador>(e =>
        {
            e.ToTable("NET_CUIDADOR");
            e.HasKey(x => x.Id);
            e.Property(x => x.Nome).HasMaxLength(120).IsRequired();
            e.Property(x => x.Email).HasMaxLength(150).IsRequired();
            e.Property(x => x.SenhaHash).HasMaxLength(255).IsRequired();
            e.Property(x => x.PushToken).HasMaxLength(255);
            e.HasIndex(x => x.Email).IsUnique();
        });

        b.Entity<Vinculo>(e =>
        {
            e.ToTable("NET_VINCULO");
            e.HasKey(x => x.Id);
            e.Property(x => x.IdosoNome).HasMaxLength(120).IsRequired();
            e.Property(x => x.Parentesco).HasMaxLength(60);
            e.HasIndex(x => new { x.CuidadorId, x.IdosoId }).IsUnique();
            e.HasIndex(x => x.IdosoId);
            e.HasOne(x => x.Cuidador).WithMany().HasForeignKey(x => x.CuidadorId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Alerta>(e =>
        {
            e.ToTable("NET_ALERTA");
            e.HasKey(x => x.Id);
            e.Property(x => x.IdosoNome).HasMaxLength(120).IsRequired();
            e.Property(x => x.MedicamentoNome).HasMaxLength(120).IsRequired();
            e.Property(x => x.Severidade).HasMaxLength(10).IsRequired();
            e.Property(x => x.Motivo).HasMaxLength(255).IsRequired();
            e.HasIndex(x => new { x.CuidadorId, x.DoseId }).IsUnique();
            e.HasOne<Cuidador>().WithMany().HasForeignKey(x => x.CuidadorId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<LocalAtendimento>(e =>
        {
            e.ToTable("NET_LOCAL_ATENDIMENTO");
            e.HasKey(x => x.Id);
            e.Property(x => x.Nome).HasMaxLength(120).IsRequired();
            e.Property(x => x.Endereco).HasMaxLength(255).IsRequired();
            e.Property(x => x.Telefone).HasMaxLength(20);
            e.Property(x => x.CriadoPorTipo).HasMaxLength(10).IsRequired();
        });

        b.Entity<Consulta>(e =>
        {
            e.ToTable("NET_CONSULTA");
            e.HasKey(x => x.Id);
            e.Property(x => x.Especialidade).HasMaxLength(100).IsRequired();
            e.Property(x => x.Status).HasMaxLength(10).IsRequired();
            e.Property(x => x.Observacoes).HasMaxLength(500);
            e.Property(x => x.CriadoPorTipo).HasMaxLength(10).IsRequired();
            e.Property(x => x.CriadoPorNome).HasMaxLength(120).IsRequired();
            e.HasIndex(x => new { x.IdosoId, x.DataHora });
            e.HasOne(x => x.Local).WithMany().HasForeignKey(x => x.LocalId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<RegistroIdempotencia>(e =>
        {
            e.ToTable("NET_IDEMPOTENCIA");
            e.HasKey(x => new { x.UsuarioId, x.Chave });
            e.Property(x => x.UsuarioId).HasMaxLength(40);
            e.Property(x => x.Chave).HasMaxLength(100);
            e.Property(x => x.HashCorpo).HasMaxLength(64).IsRequired();
            e.Property(x => x.Resposta).IsRequired();
        });

        b.Entity<EventoProcessado>(e =>
        {
            e.ToTable("NET_EVENTO_PROCESSADO");
            e.HasKey(x => x.EventId);
            e.Property(x => x.EventId).HasMaxLength(64);
        });

        AplicarConvencoes(b);
    }

    /// <summary>
    /// Colunas em MAIUSCULAS_COM_SUBLINHADO, igual ao DDL do Oracle (identificadores sem aspas).
    /// Toda data é gravada em UTC e lida com Kind=Utc, para o JSON sair com "Z".
    /// </summary>
    private static void AplicarConvencoes(ModelBuilder b)
    {
        var utc = new ValueConverter<DateTime, DateTime>(
            v => v.Kind == DateTimeKind.Unspecified ? v : v.ToUniversalTime(),
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

        foreach (var entidade in b.Model.GetEntityTypes())
        {
            foreach (var prop in entidade.GetProperties())
            {
                prop.SetColumnName(NomeColuna(prop.Name));
                if (prop.ClrType == typeof(DateTime) || prop.ClrType == typeof(DateTime?))
                    prop.SetValueConverter(utc);
            }
        }
    }

    public static string NomeColuna(string propriedade)
    {
        var sb = new StringBuilder(propriedade.Length + 5);
        for (var i = 0; i < propriedade.Length; i++)
        {
            var c = propriedade[i];
            if (i > 0 && char.IsUpper(c)) sb.Append('_');
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }
}
