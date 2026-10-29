using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using UrubuApi.Models;

namespace UrubuApi.Data;

public class DBContext : DbContext
{
    public DBContext(DbContextOptions<DBContext> options) : base(options) { }

    public DbSet<CuratedItem> CuratedItems => Set<CuratedItem>();
    public DbSet<EvaluationResult> Evaluations => Set<EvaluationResult>();
    public DbSet<StyleProfile> StyleProfiles => Set<StyleProfile>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var stringListConverter = new ValueConverter<List<string>, string>(
            v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
            v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>()
        );

        var stringListComparer = new ValueComparer<List<string>>(
            (c1, c2) => JsonSerializer.Serialize(c1, (JsonSerializerOptions?)null) == JsonSerializer.Serialize(c2, (JsonSerializerOptions?)null),
            c => c == null ? 0 : JsonSerializer.Serialize(c, (JsonSerializerOptions?)null).GetHashCode(),
            c => c == null ? new List<string>() : JsonSerializer.Deserialize<List<string>>(JsonSerializer.Serialize(c, (JsonSerializerOptions?)null), (JsonSerializerOptions?)null)!
        );

        modelBuilder.Entity<Usuario>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.Email).IsRequired();
            entity.Property(e => e.SenhaHash).IsRequired();
        });

        modelBuilder.Entity<CuratedItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Images).HasConversion(stringListConverter, stringListComparer);
            entity.Property(e => e.AvailableSizes).HasConversion(stringListConverter, stringListComparer);
            entity.Property(e => e.Tags).HasConversion(stringListConverter, stringListComparer);

            entity.HasOne(e => e.Evaluation)
                  .WithOne()
                  .HasForeignKey<EvaluationResult>(e => e.ItemId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EvaluationResult>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.MatchReasons).HasConversion(stringListConverter, stringListComparer);
            entity.Property(e => e.Concerns).HasConversion(stringListConverter, stringListComparer);
            entity.Property(e => e.DetectedTags).HasConversion(stringListConverter, stringListComparer);
        });

        modelBuilder.Entity<StyleProfile>(entity =>
        {
            entity.HasKey(e => e.Domain);
            entity.Property(e => e.AestheticKeywords).HasConversion(stringListConverter, stringListComparer);
            entity.Property(e => e.PreferredSizes).HasConversion(stringListConverter, stringListComparer);
            entity.Property(e => e.ForbiddenTerms).HasConversion(stringListConverter, stringListComparer);
            entity.Property(e => e.TargetStores).HasConversion(stringListConverter, stringListComparer);
        });
    }
}
