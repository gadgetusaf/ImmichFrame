using ImmichFrame.WebApi.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace ImmichFrame.WebApi.Persistence;

/// <summary>
/// Application database. Holds the runtime-editable configuration that used to live in
/// Settings.json / environment variables. Backed by SQLite.
/// </summary>
public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<GeneralSettingsEntity> GeneralSettings => Set<GeneralSettingsEntity>();
    public DbSet<AccountEntity> Accounts => Set<AccountEntity>();
    public DbSet<UserEntity> Users => Set<UserEntity>();
    public DbSet<SlideshowLinkEntity> SlideshowLinks => Set<SlideshowLinkEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GeneralSettingsEntity>(entity =>
        {
            entity.ToTable("GeneralSettings");
            entity.HasKey(e => e.Id);
            // EF Core 8 maps List<string> as a JSON primitive collection by convention.
            entity.PrimitiveCollection(e => e.Webcalendars);
        });

        modelBuilder.Entity<AccountEntity>(entity =>
        {
            entity.ToTable("Accounts");
            entity.HasKey(e => e.Id);
            // List<Guid> / List<string> stored as JSON primitive collections.
            entity.PrimitiveCollection(e => e.Albums);
            entity.PrimitiveCollection(e => e.ExcludedAlbums);
            entity.PrimitiveCollection(e => e.People);
            entity.PrimitiveCollection(e => e.Tags);
        });

        modelBuilder.Entity<UserEntity>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Username).IsUnique();
        });

        modelBuilder.Entity<SlideshowLinkEntity>(entity =>
        {
            entity.ToTable("SlideshowLinks");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Slug).IsUnique();
            entity.PrimitiveCollection(e => e.Albums);
            entity.PrimitiveCollection(e => e.ExcludedAlbums);
            entity.PrimitiveCollection(e => e.People);
            entity.PrimitiveCollection(e => e.Tags);
        });
    }
}
