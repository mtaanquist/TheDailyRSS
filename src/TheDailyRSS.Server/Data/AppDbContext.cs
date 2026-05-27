using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace TheDailyRSS.Server.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<FeedSource> FeedSources => Set<FeedSource>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<UserArticleState> UserArticleStates => Set<UserArticleState>();
    public DbSet<KeywordFilter> KeywordFilters => Set<KeywordFilter>();
    public DbSet<UserSession> Sessions => Set<UserSession>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Category>(e =>
        {
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.SortOrder);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.Slug).HasMaxLength(60);
            e.Property(x => x.Color).HasMaxLength(16);
            e.HasData(CategorySeed.All.Select(s => new Category
            {
                Id = s.Id,
                Name = s.Name,
                Slug = s.Slug,
                Color = s.Color,
                SortOrder = s.SortOrder,
            }));
        });

        b.Entity<FeedSource>(e =>
        {
            // The dedup key: one source per (normalized) feed URL.
            e.HasIndex(x => x.FeedUrl).IsUnique();
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.FeedUrl).HasMaxLength(2000);
            e.Property(x => x.SiteUrl).HasMaxLength(2000);
            e.Property(x => x.IconText).HasMaxLength(4);
            e.HasMany(x => x.Articles)
                .WithOne(a => a.Source!)
                .HasForeignKey(a => a.SourceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<Subscription>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.SourceId }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.CategoryId, x.SortOrder });
            e.Property(x => x.CustomTitle).HasMaxLength(300);

            e.HasOne(x => x.User)
                .WithMany(u => u.Subscriptions)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Source)
                .WithMany(s => s.Subscriptions)
                .HasForeignKey(x => x.SourceId)
                .OnDelete(DeleteBehavior.Cascade);

            // Deleting a seeded category must not silently drop user subscriptions.
            e.HasOne(x => x.Category)
                .WithMany(c => c.Subscriptions)
                .HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Article>(e =>
        {
            // De-dupe articles within a source by their external guid.
            e.HasIndex(x => new { x.SourceId, x.ExternalId }).IsUnique();
            e.HasIndex(x => new { x.SourceId, x.EditionDate });
            e.HasIndex(x => new { x.EditionDate, x.PublishedAt });
            e.Property(x => x.Title).HasMaxLength(1000);
            e.Property(x => x.ExternalId).HasMaxLength(1000);
            e.Property(x => x.Url).HasMaxLength(2000);
            e.Property(x => x.ImageUrl).HasMaxLength(2000);
            e.Property(x => x.Author).HasMaxLength(300);
        });

        b.Entity<UserArticleState>(e =>
        {
            e.HasKey(x => new { x.UserId, x.ArticleId });
            e.HasIndex(x => new { x.UserId, x.IsSaved });
            e.HasIndex(x => new { x.UserId, x.IsRead });
            e.HasIndex(x => new { x.UserId, x.IsHidden });

            e.HasOne(x => x.User)
                .WithMany(u => u.ArticleStates)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Article)
                .WithMany(a => a.States)
                .HasForeignKey(x => x.ArticleId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<KeywordFilter>(e =>
        {
            e.HasIndex(x => new { x.UserId, x.Term }).IsUnique();
            e.Property(x => x.Term).HasMaxLength(120);
            e.HasOne(x => x.User)
                .WithMany(u => u.KeywordFilters)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
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
