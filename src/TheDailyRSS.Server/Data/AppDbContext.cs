using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

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
    public DbSet<FieldFilter> FieldFilters => Set<FieldFilter>();
    public DbSet<UserSession> Sessions => Set<UserSession>();
    public DbSet<AiSummary> AiSummaries => Set<AiSummary>();

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
            e.Property(x => x.Fields)
                .HasColumnType("jsonb")
                .HasConversion(FieldsConverter)
                .Metadata.SetValueComparer(FieldsComparer);
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
            // Including SourceId in the key lets the same term coexist as a global mute
            // and a per-feed mute. Postgres treats NULLs as distinct, so the dup-check for
            // the "global" case (SourceId = null) lives in endpoint code.
            e.HasIndex(x => new { x.UserId, x.Term, x.SourceId }).IsUnique();
            e.Property(x => x.Term).HasMaxLength(120);
            e.HasOne(x => x.User)
                .WithMany(u => u.KeywordFilters)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Source)
                .WithMany()
                .HasForeignKey(x => x.SourceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<FieldFilter>(e =>
        {
            // One rule per (user, field, operator, value, optional source).
            e.HasIndex(x => new { x.UserId, x.FieldKey, x.Operator, x.Value, x.SourceId }).IsUnique();
            e.Property(x => x.FieldKey).HasMaxLength(120);
            e.Property(x => x.Value).HasMaxLength(200);
            e.HasOne(x => x.User)
                .WithMany(u => u.FieldFilters)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
            // A user-scoped source rule should follow the source if it's removed,
            // rather than orphan a dangling SourceId.
            e.HasOne(x => x.Source)
                .WithMany()
                .HasForeignKey(x => x.SourceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<UserSession>(e =>
        {
            e.HasIndex(x => x.UserId);
            e.Property(x => x.DeviceLabel).HasMaxLength(120);
            e.Property(x => x.UserAgent).HasMaxLength(1000);
            e.Property(x => x.IpAddress).HasMaxLength(64);
        });

        b.Entity<AiSummary>(e =>
        {
            // One cached summary per user, kind and period; regenerating overwrites it.
            e.HasIndex(x => new { x.UserId, x.Kind, x.PeriodStart, x.PeriodEnd }).IsUnique();
            e.Property(x => x.Model).HasMaxLength(200);
            e.HasOne(x => x.User)
                .WithMany(u => u.AiSummaries)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AppUser>(e =>
        {
            e.Property(x => x.AiBaseUrl).HasMaxLength(2000);
            e.Property(x => x.AiModel).HasMaxLength(200);
        });
    }

    // ── JSONB plumbing for Article.Fields ────────────────────────────────
    // Two halves are needed: a ValueConverter (DB ↔ CLR) and a ValueComparer that knows the
    // dictionary is not the same reference after a round-trip, so EF tracks changes correctly.

    private static readonly JsonSerializerOptions FieldsJsonOptions = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly ValueConverter<Dictionary<string, List<string>>, string> FieldsConverter = new(
        v => JsonSerializer.Serialize(v ?? new(), FieldsJsonOptions),
        v => string.IsNullOrEmpty(v)
            ? new Dictionary<string, List<string>>()
            : (JsonSerializer.Deserialize<Dictionary<string, List<string>>>(v, FieldsJsonOptions)
               ?? new Dictionary<string, List<string>>()));

    private static readonly ValueComparer<Dictionary<string, List<string>>> FieldsComparer = new(
        (a, b) => DictionariesEqual(a, b),
        v => v == null ? 0 : v.Aggregate(0, (h, kv) => HashCode.Combine(h, kv.Key, kv.Value.Aggregate(0, (hh, s) => HashCode.Combine(hh, s)))),
        v => v == null
            ? new Dictionary<string, List<string>>()
            : v.ToDictionary(kv => kv.Key, kv => new List<string>(kv.Value)));

    private static bool DictionariesEqual(Dictionary<string, List<string>>? a, Dictionary<string, List<string>>? b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a is null || b is null) return a is null && b is null;
        if (a.Count != b.Count) return false;
        foreach (var (k, v) in a)
        {
            if (!b.TryGetValue(k, out var bv)) return false;
            if (!v.SequenceEqual(bv)) return false;
        }
        return true;
    }
}
