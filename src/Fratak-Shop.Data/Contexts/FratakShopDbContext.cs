using System.Linq.Expressions;
using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Faqs;
using Fratak_Shop.Data.Entities.Favorites;
using Fratak_Shop.Data.Entities.Locations;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Messages;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.SeoRedirects;
using Fratak_Shop.Data.Entities.Shopping;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Data.Contexts;

public class FratakShopDbContext(DbContextOptions<FratakShopDbContext> options) : DbContext(options)
{
    public DbSet<User> Users { get; set; } = null!;

    public DbSet<UserAddress> Addresses { get; set; } = null!;

    public DbSet<Location> Locations { get; set; } = null!;

    public DbSet<Category> Categories { get; set; } = null!;

    public DbSet<Product> Products { get; set; } = null!;

    public DbSet<ProductImage> ProductImages { get; set; } = null!;

    public DbSet<ProductFeature> ProductFeatures { get; set; } = null!;

    public DbSet<Banner> Banners { get; set; } = null!;

    public DbSet<Cart> Carts { get; set; } = null!;

    public DbSet<CartItem> CartItems { get; set; } = null!;

    public DbSet<Order> Orders { get; set; } = null!;

    public DbSet<OrderItem> OrderItems { get; set; } = null!;

    public DbSet<Comment> Comments { get; set; } = null!;

    public DbSet<BlogPost> BlogPosts { get; set; } = null!;

    public DbSet<BlogCategory> BlogCategories { get; set; } = null!;

    public DbSet<FaqItem> FaqItems { get; set; } = null!;

    public DbSet<Message> Messages { get; set; } = null!;

    public DbSet<Favorite> Favorites { get; set; } = null!;

    public DbSet<PageSeo> PageSeos { get; set; } = null!;

    public DbSet<PageAlert> PageAlerts { get; set; } = null!;

    public DbSet<SeoRedirect> SeoRedirects { get; set; } = null!;

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        base.OnConfiguring(optionsBuilder);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Entity-specific rules (relationships, indexes, check constraints, ...)
        // live in the Configuration classes and stay there.
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FratakShopDbContext).Assembly);

        // Cross-cutting soft-delete rules are applied once here so no
        // configuration has to repeat them.
        ApplySoftDeleteRules(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditDates();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyAuditDates();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Fills CreatedAt/UpdatedAt automatically and turns hard deletes into
    /// soft deletes (IsDeleted/DeletedAt) so nothing is ever physically removed
    /// through SaveChanges.
    /// </summary>
    private void ApplyAuditDates()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    // شناسه‌های از پیش تعیین‌شده (مثل داده‌های Seed با DeterministicGuid) بازنویسی نمی‌شوند
                    // تا ارجاع‌های والد/فرزند در داده‌های مرجع معتبر بمانند.
                    if (entry.Entity.Id == Guid.Empty)
                        entry.Entity.Id = Guid.NewGuid();

                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;

                case EntityState.Deleted:
                    entry.Entity.IsDeleted = true;
                    entry.Entity.DeletedAt ??= now;
                    entry.Entity.UpdatedAt = now;
                    entry.State = EntityState.Modified;
                    break;
            }
        }
    }

    /// <summary>
    /// Central soft-delete behavior for every entity inheriting <see cref="BaseEntity"/>:
    /// 1) deleted rows are hidden from queries (global query filter),
    /// 2) unique indexes ignore deleted rows, so a value can be reused after a soft delete.
    /// </summary>
    private static void ApplySoftDeleteRules(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (!typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                continue;
            }

            var parameter = Expression.Parameter(entityType.ClrType, "e");
            var body = Expression.Equal(
                Expression.Property(parameter, nameof(BaseEntity.IsDeleted)),
                Expression.Constant(false));
            entityType.SetQueryFilter(Expression.Lambda(body, parameter));

            foreach (var index in entityType.GetIndexes().Where(i => i.IsUnique))
            {
                index.SetFilter("[IsDeleted] = 0");
            }
        }
    }
}
