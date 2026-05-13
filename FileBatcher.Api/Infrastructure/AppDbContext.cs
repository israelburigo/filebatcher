using FileBatcher.Domain;
using Microsoft.EntityFrameworkCore;

namespace FileBatcher.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<FileBatch> FileBatches => Set<FileBatch>();
    public DbSet<FileBatchItem> FileBatchItems => Set<FileBatchItem>();
    public DbSet<Partner> Partners => Set<Partner>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<FileBatch>(e =>
        {
            e.ToTable("filebatch");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(255).IsRequired();
            e.Property(x => x.Action).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.CreatedAt).HasPrecision(0);
            e.Property(x => x.UpdatedAt).HasPrecision(0);
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.UpdatedAt);
        });

        modelBuilder.Entity<FileBatchItem>(e =>
        {
            e.ToTable("filebatch_item");
            e.HasKey(x => x.Id);
            e.Property(x => x.Data).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.CreatedAt).HasPrecision(0);
            e.Property(x => x.UpdatedAt).HasPrecision(0);
            e.HasOne(x => x.FileBatch)
                .WithMany(x => x.Items)
                .HasForeignKey(x => x.FileBatchId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.FileBatchId, x.Status });
        });

        modelBuilder.Entity<Partner>(e =>
        {
            e.ToTable("partner");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.Name).HasMaxLength(255).IsRequired();
            e.Property(x => x.Document).HasMaxLength(11).IsRequired();
            e.Property(x => x.Email).HasMaxLength(255).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(20).IsRequired();
            e.Property(x => x.CreatedAt).HasPrecision(0);
            e.Property(x => x.UpdatedAt).HasPrecision(0);
            e.HasIndex(x => x.Document).IsUnique();
            e.HasIndex(x => x.Name);
        });
    }
}
