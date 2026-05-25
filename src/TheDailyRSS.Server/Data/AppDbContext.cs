using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace TheDailyRSS.Server.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Feed> Feeds => Set<Feed>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<UserSession> Sessions => Set<UserSession>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Category>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.SortOrder });
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Color).HasMaxLength(16);
            e.HasMany(x => x.Feeds)
                .WithOne(f => f.Category!)
                .HasForeignKey(f => f.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Feed>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.CategoryId, x.SortOrder });
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.FeedUrl).HasMaxLength(2000);
            e.Property(x => x.SiteUrl).HasMaxLength(2000);
            e.Property(x => x.IconText).HasMaxLength(4);
            e.HasMany(x => x.Articles)
                .WithOne(a => a.Feed!)
                .HasForeignKey(a => a.FeedId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Article>(e =>
        {
            // De-dupe articles within a feed by their external guid.
            e.HasIndex(x => new { x.FeedId, x.ExternalId }).IsUnique();
            e.HasIndex(x => new { x.FeedId, x.EditionDate });
            e.HasIndex(x => x.IsSaved);
            e.Property(x => x.Title).HasMaxLength(1000);
            e.Property(x => x.ExternalId).HasMaxLength(1000);
            e.Property(x => x.Url).HasMaxLength(2000);
            e.Property(x => x.ImageUrl).HasMaxLength(2000);
            e.Property(x => x.Author).HasMaxLength(300);
        });

        b.Entity<UserSession>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.Property(x => x.DeviceLabel).HasMaxLength(120);
            e.Property(x => x.UserAgent).HasMaxLength(1000);
            e.Property(x => x.IpAddress).HasMaxLength(64);
        });
    }
}
