using System.Text;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Infrastructure.Seeders;

/// <summary>
/// Demo-only data of round 6 (admin panel), runs with <c>Shop:SeedDemoData</c>: a sample supplier with
/// source links on every product, the sample purchase-invoice PDF and one demo account per staff role
/// (password <see cref="DemoDataSeeder.DemoPassword"/>) so every part of the panel can be tried.
/// </summary>
public static class Round6DemoSeeder
{
    public const string DemoInvoiceFileName = "demo-purchase-invoice.pdf";

    /// <summary>Demo staff accounts: phone, first name, last name, role (null = store owner).</summary>
    public static readonly (string Phone, string FirstName, string LastName, string? Role)[] DemoStaff =
    [
        ("09120000009", "مالک", "فروشگاه", null),
        ("09120000002", "مدیر", "سفارش‌ها", "مدیر فروش و سفارش‌ها"),
        ("09120000003", "کارشناس", "سئو", "کارشناس سئو"),
        ("09120000004", "نویسنده", "محتوا", "نویسنده محتوا"),
        ("09120000005", "پشتیبان", "فنی", "پشتیبانی فنی"),
        ("09120000006", "حسابدار", "فروشگاه", "حسابداری و مالی"),
    ];

    public static readonly byte[] DemoInvoicePdf = BuildPdf("Fratak demo purchase invoice - DEMO-INV-0001");

    public static async Task SeedAsync(FratakShopDbContext context, IWebHostEnvironment environment, ILogger logger, CancellationToken cancellationToken = default)
    {
        try
        {
            var folder = Path.Combine(environment.ContentRootPath, "App_Data", "purchase-invoices");
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, DemoInvoiceFileName);
            if (!File.Exists(path))
                await File.WriteAllBytesAsync(path, DemoInvoicePdf, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Writing the demo purchase invoice failed");
        }

        await SeedSupplierAsync(context, cancellationToken);
        await SeedStaffAsync(context, cancellationToken);
    }

    private static async Task SeedSupplierAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        var supplier = await context.Suppliers.AsTracking().FirstOrDefaultAsync(s => s.Name == "دیجی‌کالا", cancellationToken);
        if (supplier is null)
        {
            supplier = new Supplier
            {
                Name = "دیجی‌کالا",
                Website = "https://www.digikala.com",
                Notes = "تأمین‌کننده نمونه (داده آزمایشی)",
                DisplayOrder = 1,
            };
            context.Suppliers.Add(supplier);
            context.Suppliers.Add(new Supplier { Name = "ترب", Website = "https://torob.com", Notes = "مقایسه قیمت و خرید از فروشندگان", DisplayOrder = 2 });
            await context.SaveChangesAsync(cancellationToken);
        }

        var products = await context.Products.AsTracking().Where(p => p.SupplierUrl == null).ToListAsync(cancellationToken);
        foreach (var product in products)
        {
            product.SupplierId = supplier.Id;
            product.SupplierUrl = "https://www.digikala.com/search/?q=" + Uri.EscapeDataString(product.Name);
            product.SupplierSku ??= product.SKU;
        }

        if (products.Count > 0)
            await context.SaveChangesAsync(cancellationToken);

        var invoices = await context.PurchaseInvoices.AsTracking().Where(i => i.SupplierId == null && i.SupplierName == "تأمین‌کننده نمونه").ToListAsync(cancellationToken);
        foreach (var invoice in invoices)
        {
            invoice.SupplierId = supplier.Id;
            invoice.SupplierName = supplier.Name;
        }

        if (invoices.Count > 0)
            await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedStaffAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        var roles = await context.AdminRoles.ToDictionaryAsync(r => r.Name, r => r.Id, cancellationToken);
        var changed = false;

        foreach (var (phone, firstName, lastName, role) in DemoStaff)
        {
            if (await context.Users.IgnoreQueryFilters().AnyAsync(u => u.PhoneNumber == phone, cancellationToken))
                continue;

            var user = new User
            {
                PhoneNumber = phone,
                FirstName = firstName,
                LastName = lastName,
                PasswordHash = PasswordHasher.Hash(DemoDataSeeder.DemoPassword),
                SecurityStamp = PasswordHasher.NewSecurityStamp(),
                IsPhoneVerified = true,
                Role = role is null ? UserRole.SuperAdmin : UserRole.Customer,
            };
            context.Users.Add(user);

            if (role is not null && roles.TryGetValue(role, out var roleId))
                context.UserAdminRoles.Add(new UserAdminRole { User = user, RoleId = roleId });

            changed = true;
        }

        if (changed)
        {
            await context.SaveChangesAsync(cancellationToken);
            AdminAccessService.InvalidateAll();
        }
    }

    /// <summary>A minimal, valid one-page PDF (correct xref offsets) with one line of text.</summary>
    private static byte[] BuildPdf(string text)
    {
        var content = $"BT /F1 18 Tf 60 760 Td ({text}) Tj ET";
        string[] objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Contents 4 0 R /Resources << /Font << /F1 5 0 R >> >> >>",
            $"<< /Length {content.Length} >>\nstream\n{content}\nendstream",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
        ];

        var builder = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var i = 0; i < objects.Length; i++)
        {
            offsets.Add(builder.Length);
            builder.Append($"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
        }

        var xref = builder.Length;
        builder.Append($"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            builder.Append($"{offset:D10} 00000 n \n");
        builder.Append($"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
