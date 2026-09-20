using CdsHelper.Api.Entities;
using Microsoft.EntityFrameworkCore;

namespace CdsHelper.Api.Data;

public class AppDbContext : DbContext
{
    public DbSet<CityEntity> Cities { get; set; } = null!;
    public DbSet<BookEntity> Books { get; set; } = null!;
    public DbSet<BookCityEntity> BookCities { get; set; } = null!;
    public DbSet<HintEntity> Hints { get; set; } = null!;
    public DbSet<BookHintEntity> BookHints { get; set; } = null!;
    public DbSet<DiscoveryEntity> Discoveries { get; set; } = null!;
    public DbSet<DiscoveryParentEntity> DiscoveryParents { get; set; } = null!;

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<CityEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever(); // JSON에서 직접 ID 지정
            entity.Property(e => e.Name).IsRequired().HasMaxLength(50);
            entity.Property(e => e.CulturalSphere).HasMaxLength(50);
        });

        modelBuilder.Entity<BookEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<BookCityEntity>(entity =>
        {
            entity.HasKey(e => new { e.BookId, e.CityId });

            entity.HasOne(e => e.Book)
                .WithMany(b => b.BookCities)
                .HasForeignKey(e => e.BookId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.City)
                .WithMany(c => c.BookCities)
                .HasForeignKey(e => e.CityId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<HintEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);
        });

        modelBuilder.Entity<BookHintEntity>(entity =>
        {
            entity.HasKey(e => new { e.BookId, e.HintId });

            entity.HasOne(e => e.Book)
                .WithMany(b => b.BookHints)
                .HasForeignKey(e => e.BookId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Hint)
                .WithMany(h => h.BookHints)
                .HasForeignKey(e => e.HintId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<DiscoveryEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).ValueGeneratedNever();
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);

            entity.HasOne(e => e.Hint)
                .WithMany(h => h.Discoveries)
                .HasForeignKey(e => e.HintId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<DiscoveryParentEntity>(entity =>
        {
            entity.HasKey(e => new { e.DiscoveryId, e.ParentDiscoveryId });

            entity.HasOne(e => e.Discovery)
                .WithMany(d => d.Parents)
                .HasForeignKey(e => e.DiscoveryId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.ParentDiscovery)
                .WithMany(d => d.Children)
                .HasForeignKey(e => e.ParentDiscoveryId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
