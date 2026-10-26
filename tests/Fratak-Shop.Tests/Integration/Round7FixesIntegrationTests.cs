using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Brands;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست‌های اصلاحیهٔ مرحلهٔ هفتم روی SQL Server واقعی:
/// <list type="bullet">
/// <item>توضیح برند دیگر سقف ۱۰۰۰ نویسه ندارد (مهاجرت Round7Fixes) – علت خطای «تأیید پیشنهاد سئو».</item>
/// <item>محافظ طول ستون (<see cref="DbLengthGuard"/>) متن بلندتر از سقف را قبل از SaveChanges با پیام فارسی گزارش می‌کند.</item>
/// </list>
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class Round7FixesIntegrationTests(SqlServerFixture db)
{
    [Fact]
    public async Task Brand_description_accepts_long_html()
    {
        var slug = "brand-" + Guid.NewGuid().ToString("N")[..10];
        var html = string.Concat(Enumerable.Repeat("<p>معرفی کامل برند با جزئیات محصولات و تاریخچه.</p>", 80)); // بیش از ۳۰۰۰ نویسه
        Assert.True(html.Length > 3000);

        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            context.Brands.Add(new Brand { Name = "برند آزمایشی " + slug, Slug = slug, Description = html });
            Assert.Null(DbLengthGuard.FindOverflow(context));
            await context.SaveChangesAsync();
        }

        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var saved = await context.Brands.AsNoTracking().SingleAsync(b => b.Slug == slug);
            Assert.Equal(html, saved.Description);
        }
    }

    [Fact]
    public async Task Length_guard_reports_overflow_without_saving()
    {
        var id = await db.CreateProductAsync();
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var product = await context.Products.AsTracking().SingleAsync(p => p.Id == id);
        var original = product.Name;

        product.Name = new string('ک', 400); // سقف نام محصول ۲۵۰ است
        var message = DbLengthGuard.FindOverflow(context);
        Assert.NotNull(message);
        Assert.Contains("Name", message);
        Assert.Contains("Product", message);

        // ویژگی‌های تغییرنکرده بررسی نمی‌شوند؛ با برگرداندن نام، خطایی نیست.
        product.Name = original;
        Assert.Null(DbLengthGuard.FindOverflow(context));
    }
}
