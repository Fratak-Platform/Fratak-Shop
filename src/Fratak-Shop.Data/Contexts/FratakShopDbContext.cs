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

public class FratakShopDbContext : DbContext
{
    public FratakShopDbContext(DbContextOptions<FratakShopDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<UserAddress> Addresses => Set<UserAddress>();

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<ProductImage> ProductImages => Set<ProductImage>();

    public DbSet<ProductFeature> ProductFeatures => Set<ProductFeature>();

    public DbSet<Banner> Banners => Set<Banner>();

    public DbSet<Cart> Carts => Set<Cart>();

    public DbSet<CartItem> CartItems => Set<CartItem>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<BlogPost> BlogPosts => Set<BlogPost>();

    public DbSet<BlogCategory> BlogCategories => Set<BlogCategory>();

    public DbSet<FaqItem> FaqItems => Set<FaqItem>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<Favorite> Favorites => Set<Favorite>();

    public DbSet<PageSeo> PageSeos => Set<PageSeo>();

    public DbSet<PageAlert> PageAlerts => Set<PageAlert>();

    public DbSet<SeoRedirect> SeoRedirects => Set<SeoRedirect>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(FratakShopDbContext).Assembly);

        ApplySoftDeleteQueryFilters(modelBuilder);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyAuditRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Fills CreatedAt/UpdatedAt automatically so no caller has to remember them.
    /// </summary>
    private void ApplyAuditRules()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            switch (entry.State)
            {
                case EntityState.Added:
                    entry.Entity.CreatedAt = now;
                    entry.Entity.UpdatedAt = now;
                    break;

                case EntityState.Modified:
                    entry.Entity.UpdatedAt = now;
                    break;
            }
        }
    }

    /// <summary>
    /// Soft-deleted rows (IsDeleted = 1) are hidden from every query unless the
    /// caller explicitly uses IgnoreQueryFilters().
    /// </summary>
    private static void ApplySoftDeleteQueryFilters(ModelBuilder modelBuilder)
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
        }
    }
}
