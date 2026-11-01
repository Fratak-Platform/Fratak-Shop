using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Pricing;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// مرحلهٔ سیزدهم روی دیتابیس واقعی: دادهٔ پیش‌فرض فقط روی دیتابیس تازه (و هرگز دوباره)، ساخت مدیران پیش‌فرض با رمز اولیه (هر تعداد) و
/// ساخته نشدن دوباره‌اش پس از حذف، و صف قیمت‌گذاری بازار محصولات درون‌ریزی‌شده (اعمال، دست نزدن به قیمت دستی، تلاش دوباره).
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class ProductionReadinessIntegrationTests(SqlServerFixture db)
{
    [SqlFact]
    public async Task DefaultData_IsSeededOnce_AndNeverAgain()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();

        // fixture با دیتابیس خالی شروع کرد و دادهٔ پیش‌فرض کامل ساخته شد.
        Assert.True(await context.StoreSettings.AnyAsync());
        foreach (var slug in new[] { "about-us", "return-policy", "how-to-buy", "club-rules", "privacy", "warranty" })
            Assert.True(await context.ContentPages.IgnoreQueryFilters().AnyAsync(p => p.Slug == slug), slug);
        var returnPolicy = await context.ContentPages.AsNoTracking().FirstAsync(p => p.Slug == "return-policy");
        Assert.Contains("۷ روز", returnPolicy.Content);

        // حالا دیتابیس «تازه» نیست؛ نه با true و نه با false دوباره ساخته نمی‌شود.
        Assert.False(await DefaultDataSeeder.IsFreshDatabaseAsync(context));
        var pages = await context.ContentPages.IgnoreQueryFilters().CountAsync();
        Assert.Equal(DefaultDataOutcome.AlreadyInitialized, await DefaultDataSeeder.EnsureAsync(context, enabled: true));
        Assert.Equal(DefaultDataOutcome.AlreadyInitialized, await DefaultDataSeeder.EnsureAsync(context, enabled: false));
        Assert.Equal(pages, await context.ContentPages.IgnoreQueryFilters().CountAsync());
    }

    [SqlFact]
    public async Task DefaultAdmin_IsCreatedWithPassword_OnlyOnce_AndNotRecreatedAfterDelete()
    {
        var phone = "0901" + Random.Shared.Next(1_000_000, 9_999_999);
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<IAdminAccessService>();
        try
        {
            var notes = await access.EnsureDefaultsAsync([new DefaultAdminAccount(" " + phone + " ", "محمدامین", "ناظری", "12345678")]);
            var owner = await context.Users.AsNoTracking().SingleAsync(u => u.PhoneNumber == phone);
            Assert.Equal(UserRole.SuperAdmin, owner.Role);
            Assert.True(PasswordHasher.Verify("12345678", owner.PasswordHash));
            Assert.True(owner.IsPhoneVerified);
            Assert.Equal("محمدامین", owner.FirstName);
            Assert.Equal("ناظری", owner.LastName);
            Assert.False(string.IsNullOrEmpty(owner.SecurityStamp));
            Assert.True((await access.GetAccessAsync(owner.Id)).IsOwner);
            // رمز هرگز در یادداشت‌های لاگ نمی‌آید؛ شماره هم پوشیده است.
            Assert.Single(notes);
            Assert.DoesNotContain("12345678", notes[0], StringComparison.Ordinal);
            Assert.DoesNotContain(phone, notes[0], StringComparison.Ordinal);

            // مالک رمزش را عوض کرد: اجرای دوباره نه حساب تکراری می‌سازد نه رمز را به 12345678 برمی‌گرداند.
            var tracked = await context.Users.AsTracking().SingleAsync(u => u.PhoneNumber == phone);
            tracked.PasswordHash = PasswordHasher.Hash("my-new-strong-pass");
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            await access.EnsureDefaultsAsync([new DefaultAdminAccount(phone, "محمدامین", "ناظری", "12345678")]);
            Assert.Equal(1, await context.Users.IgnoreQueryFilters().CountAsync(u => u.PhoneNumber == phone));
            var after = await context.Users.AsNoTracking().SingleAsync(u => u.PhoneNumber == phone);
            Assert.True(PasswordHasher.Verify("my-new-strong-pass", after.PasswordHash));
            Assert.False(PasswordHasher.Verify("12345678", after.PasswordHash));

            // مدیر حساب را حذف کرد: با ری‌استارت برنامه دوباره ساخته نمی‌شود.
            tracked = await context.Users.AsTracking().SingleAsync(u => u.PhoneNumber == phone);
            context.Users.Remove(tracked);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            await access.EnsureDefaultsAsync([new DefaultAdminAccount(phone, "محمدامین", "ناظری", "12345678")]);
            Assert.False(await context.Users.AnyAsync(u => u.PhoneNumber == phone));
            Assert.Equal(1, await context.Users.IgnoreQueryFilters().CountAsync(u => u.PhoneNumber == phone));

            // شمارهٔ نامعتبر یا رمز کوتاه هیچ حسابی نمی‌سازد.
            var before = await context.Users.IgnoreQueryFilters().CountAsync();
            await access.EnsureDefaultsAsync([new DefaultAdminAccount("12345", "x", "y", "12345678"), new DefaultAdminAccount(phone[..^1] + "0", "x", "y", "123")]);
            Assert.Equal(before, await context.Users.IgnoreQueryFilters().CountAsync());
        }
        finally
        {
            context.ChangeTracker.Clear();
            var leftovers = await context.Users.IgnoreQueryFilters().AsTracking().Where(u => u.PhoneNumber == phone).ToListAsync();
            context.Users.RemoveRange(leftovers);
            await context.SaveChangesAsync();
        }
    }

    [SqlFact]
    public async Task SeveralDefaultAdmins_GetTheirOwnAccess_AndPasswordlessAccountGetsInitialPasswordOnce()
    {
        var baseNumber = Random.Shared.Next(1_000_000, 9_999_997);
        var ownerPhone = "0902" + baseNumber;
        var supportPhone = "0902" + (baseNumber + 1);
        var existingPhone = "0902" + (baseNumber + 2);
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<IAdminAccessService>();
        try
        {
            // حسابی که قبلاً با کد پیامکی (بدون رمز) ثبت‌نام کرده بود.
            context.Users.Add(new User { PhoneNumber = existingPhone, IsPhoneVerified = true, SecurityStamp = PasswordHasher.NewSecurityStamp() });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            await access.EnsureDefaultsAsync(
            [
                new DefaultAdminAccount(ownerPhone, "مدیر", "یک", "first-pass-1"),
                new DefaultAdminAccount(supportPhone, "پشتیبان", "دو", "second-pass-2", FullAccess: false, Roles: ["پشتیبانی", "نقش-ناموجود"]),
                new DefaultAdminAccount(existingPhone, "قدیمی", "سه", "third-pass-3"),
            ]);

            var owner = await context.Users.AsNoTracking().SingleAsync(u => u.PhoneNumber == ownerPhone);
            var support = await context.Users.AsNoTracking().SingleAsync(u => u.PhoneNumber == supportPhone);
            var existing = await context.Users.AsNoTracking().SingleAsync(u => u.PhoneNumber == existingPhone);

            Assert.True((await access.GetAccessAsync(owner.Id)).IsOwner);
            Assert.True(PasswordHasher.Verify("first-pass-1", owner.PasswordHash));

            var supportAccess = await access.GetAccessAsync(support.Id);
            Assert.False(supportAccess.IsOwner);
            Assert.Equal(UserRole.Customer, support.Role);
            Assert.True(await context.UserAdminRoles.AnyAsync(m => m.UserId == support.Id && m.Role.Name == "پشتیبانی"));
            Assert.True(PasswordHasher.Verify("second-pass-2", support.PasswordHash));

            Assert.Equal(UserRole.SuperAdmin, existing.Role);
            Assert.True(PasswordHasher.Verify("third-pass-3", existing.PasswordHash));
            Assert.Null(existing.FirstName); // نام حساب موجود را تنظیمات عوض نمی‌کند

            // اجرای دوم با رمز دیگر: رمز موجود دست نمی‌خورد و نقش تکراری اضافه نمی‌شود.
            await access.EnsureDefaultsAsync(
            [
                new DefaultAdminAccount(supportPhone, "پشتیبان", "دو", "changed-pass-9", FullAccess: false, Roles: ["پشتیبانی"]),
            ]);
            support = await context.Users.AsNoTracking().SingleAsync(u => u.PhoneNumber == supportPhone);
            Assert.True(PasswordHasher.Verify("second-pass-2", support.PasswordHash));
            Assert.Equal(1, await context.UserAdminRoles.CountAsync(m => m.UserId == support.Id));
        }
        finally
        {
            context.ChangeTracker.Clear();
            var phones = new[] { ownerPhone, supportPhone, existingPhone };
            var ids = await context.Users.IgnoreQueryFilters().Where(u => phones.Contains(u.PhoneNumber)).Select(u => u.Id).ToListAsync();
            context.UserAdminRoles.RemoveRange(await context.UserAdminRoles.IgnoreQueryFilters().AsTracking().Where(m => ids.Contains(m.UserId)).ToListAsync());
            context.Users.RemoveRange(await context.Users.IgnoreQueryFilters().AsTracking().Where(u => ids.Contains(u.Id)).ToListAsync());
            await context.SaveChangesAsync();
        }
    }

    private sealed class FakeCompetitors(Func<string, CompetitorLookup> respond) : ICompetitorPriceService
    {
        public List<string> Queries { get; } = [];

        public Task<CompetitorLookup> SearchAsync(string query, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            return Task.FromResult(respond(query));
        }
    }

    /// <summary>محصول درون‌ریزی‌شدهٔ غیرفعال با قیمت خرید و قیمت اولیهٔ سود هدف.</summary>
    private async Task<(Guid Id, decimal Initial, PricingCosts Costs)> ImportedProductAsync(decimal purchase)
    {
        await using var scope = db.Scope();
        var costs = PricingCosts.From(await scope.ServiceProvider.GetRequiredService<IStoreSettingService>().GetAsync());
        var initial = PriceAdvisor.Suggest(purchase, 0, costs, []).TargetPrice;
        var id = await db.CreateProductAsync(initial, stock: 0);
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var product = await context.Products.AsTracking().SingleAsync(p => p.Id == id);
        product.PurchasePrice = purchase;
        product.IsActive = false;
        await context.SaveChangesAsync();
        return (id, initial, costs);
    }

    private static MarketPricingService Market(AsyncServiceScope scope, ICompetitorPriceService competitors)
        => new(scope.ServiceProvider.GetRequiredService<FratakShopDbContext>(), competitors,
            scope.ServiceProvider.GetRequiredService<IStoreSettingService>(), new ExternalSourceGate(0));

    /// <summary>صف را تا رسیدن به ردیف این محصول پردازش می‌کند (ردیف‌های آزمون‌های دیگر هم ممکن است در صف باشند).</summary>
    private static async Task<MarketPriceCheck> ProcessUntilAsync(AsyncServiceScope scope, MarketPricingService market, Guid productId)
    {
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        for (var i = 0; i < 20; i++)
        {
            var job = await context.MarketPriceChecks.AsNoTracking().OrderByDescending(c => c.CreatedAt).FirstAsync(c => c.ProductId == productId);
            if (job.Status != MarketPriceCheckStatus.Pending || job.NextAttemptAt > DateTimeOffset.UtcNow)
                return job;
            Assert.True(await market.ProcessNextAsync());
            context.ChangeTracker.Clear();
        }
        throw new InvalidOperationException("queue did not reach the product");
    }

    [SqlFact]
    public async Task MarketPricing_AppliesTheSuggestedPrice_FromCompetitors()
    {
        var (id, initial, costs) = await ImportedProductAsync(200_000);
        CompetitorOffer[] offers = [new("فروشگاه الف", 285_000, "ترب"), new("فروشگاه ب", 290_000, "ترب"), new("دیجی‌کالا", 310_000, "دیجی‌کالا")];
        var competitors = new FakeCompetitors(_ => new CompetitorLookup(offers, [], "هدفون آزمایشی"));

        await using var scope = db.Scope();
        var market = Market(scope, competitors);
        await market.EnqueueAsync(id, "هدفون آزمایشی T15", initial, 200_000);
        await market.EnqueueAsync(id, "هدفون آزمایشی T15", initial, 200_000); // تکراری در صف ساخته نمی‌شود
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        Assert.Equal(1, await context.MarketPriceChecks.CountAsync(c => c.ProductId == id));

        var job = await ProcessUntilAsync(scope, market, id);
        var expected = PriceAdvisor.Suggest(200_000, 0, costs, offers).Suggested;
        Assert.Equal(MarketPriceCheckStatus.Applied, job.Status);
        Assert.Equal(expected, job.SuggestedPrice);
        Assert.Equal(285_000, job.LowestPrice);
        Assert.Equal(3, job.OfferCount);
        Assert.NotNull(job.CompletedAt);
        Assert.Equal(expected, (await context.Products.AsNoTracking().SingleAsync(p => p.Id == id)).Price);
        Assert.Contains("هدفون آزمایشی T15", competitors.Queries);

        var summary = await market.SummaryAsync(50);
        Assert.Contains(summary.Recent, r => r.ProductId == id && r.Status == MarketPriceCheckStatus.Applied && r.ProductName.StartsWith("محصول آزمایشی"));
    }

    [SqlFact]
    public async Task MarketPricing_LeavesManuallyEditedPrices_AndRetriesWhenSourcesAreLimited()
    {
        var (edited, initial, _) = await ImportedProductAsync(150_000);
        var (limited, limitedInitial, _) = await ImportedProductAsync(120_000);

        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var market = Market(scope, new FakeCompetitors(_ => new CompetitorLookup([], ["ترب: موقتاً محدود"], null, Limited: true)));
        await market.EnqueueAsync(edited, "کالای ویرایش‌شده", initial, 150_000);
        await market.EnqueueAsync(limited, "کالای محدود", limitedInitial, 120_000);

        // کارمند قیمت را دستی عوض کرد ← صف به آن دست نمی‌زند.
        var product = await context.Products.AsTracking().SingleAsync(p => p.Id == edited);
        product.Price = initial + 7_000;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var editedJob = await ProcessUntilAsync(scope, market, edited);
        Assert.Equal(MarketPriceCheckStatus.Skipped, editedJob.Status);
        Assert.Equal(initial + 7_000, (await context.Products.AsNoTracking().SingleAsync(p => p.Id == edited)).Price);

        // منابع محدود ← در صف می‌ماند با زمان تلاش بعدی در آینده؛ قیمت اولیه دست نخورده.
        var limitedJob = await ProcessUntilAsync(scope, market, limited);
        Assert.Equal(MarketPriceCheckStatus.Pending, limitedJob.Status);
        Assert.Equal(1, limitedJob.Attempts);
        Assert.True(limitedJob.NextAttemptAt > DateTimeOffset.UtcNow.AddMinutes(10));
        Assert.Equal(limitedInitial, (await context.Products.AsNoTracking().SingleAsync(p => p.Id == limited)).Price);

        // پاک‌سازی: ردیف در صف آزمون‌های بعدی را معطل نکند.
        var row = await context.MarketPriceChecks.AsTracking().SingleAsync(c => c.ProductId == limited);
        context.MarketPriceChecks.Remove(row);
        await context.SaveChangesAsync();
    }
}
