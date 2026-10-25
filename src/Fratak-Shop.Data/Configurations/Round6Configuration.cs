using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fratak_Shop.Data.Configurations;

// Admin panel: staff roles & permissions, audit / error / security logs, internal order notes,
// supplier purchase invoices and suppliers.

public class AdminRoleConfiguration : IEntityTypeConfiguration<AdminRole>
{
    public void Configure(EntityTypeBuilder<AdminRole> builder)
    {
        builder.HasIndex(r => r.Name).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public class AdminRolePermissionConfiguration : IEntityTypeConfiguration<AdminRolePermission>
{
    public void Configure(EntityTypeBuilder<AdminRolePermission> builder)
    {
        builder.HasOne(p => p.Role).WithMany(r => r.Permissions).HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => new { p.RoleId, p.Permission }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public class UserAdminRoleConfiguration : IEntityTypeConfiguration<UserAdminRole>
{
    public void Configure(EntityTypeBuilder<UserAdminRole> builder)
    {
        builder.HasOne(m => m.User).WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(m => m.Role).WithMany(r => r.Users).HasForeignKey(m => m.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => new { m.UserId, m.RoleId }).IsUnique().HasFilter("[IsDeleted] = 0");
    }
}

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.HasIndex(a => a.CreatedAt);
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => new { a.UserId, a.CreatedAt });
    }
}

public class ErrorLogConfiguration : IEntityTypeConfiguration<ErrorLog>
{
    public void Configure(EntityTypeBuilder<ErrorLog> builder)
    {
        builder.HasIndex(e => e.CreatedAt);
        builder.HasIndex(e => new { e.Fingerprint, e.CreatedAt });
        builder.HasIndex(e => new { e.IsResolved, e.CreatedAt });
    }
}

public class SecurityLogConfiguration : IEntityTypeConfiguration<SecurityLog>
{
    public void Configure(EntityTypeBuilder<SecurityLog> builder)
    {
        builder.HasIndex(s => s.CreatedAt);
        builder.HasIndex(s => new { s.Type, s.CreatedAt });
        builder.HasIndex(s => new { s.UserId, s.CreatedAt });
    }
}

public class OrderNoteConfiguration : IEntityTypeConfiguration<OrderNote>
{
    public void Configure(EntityTypeBuilder<OrderNote> builder)
    {
        builder.HasOne(n => n.Order).WithMany().HasForeignKey(n => n.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(n => new { n.OrderId, n.CreatedAt });
    }
}

public class PurchaseInvoiceConfiguration : IEntityTypeConfiguration<PurchaseInvoice>
{
    public void Configure(EntityTypeBuilder<PurchaseInvoice> builder)
    {
        builder.HasOne(i => i.Order).WithMany().HasForeignKey(i => i.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(i => i.Supplier).WithMany().HasForeignKey(i => i.SupplierId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(i => i.Amount).HasPrecision(18, 0);
        builder.HasIndex(i => i.OrderId);
        builder.HasIndex(i => new { i.SupplierId, i.PurchasedAt });
        builder.ToTable(t => t.HasCheckConstraint("CK_PurchaseInvoices_Amount", "[Amount] >= 0 AND [SizeBytes] >= 0"));
    }
}

public class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.HasIndex(s => s.Name).IsUnique().HasFilter("[IsDeleted] = 0");
        builder.HasMany(s => s.Products).WithOne(p => p.Supplier).HasForeignKey(p => p.SupplierId).OnDelete(DeleteBehavior.Restrict);
    }
}
