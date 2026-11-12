using Fratak_Shop.Core.Services.Design;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Design;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست یکپارچهٔ دور ۲۴ (بستهٔ C – بنر هوشمند) روی SQL Server واقعی.
/// <list type="bullet">
/// <item>پیش‌نویس کمپین: کوپن غیرفعال با هدف دسته؛ لغو پیش‌نویس ← کوپن حذف؛ لغو دوباره ← خطا.</item>
/// <item>انتشار بنرِ گروه ← قاعده و کوپن در همان ذخیره فعال، پایان بنر = پایان کمپین؛ انتشار نسخهٔ دوم دوباره فعال نمی‌کند.</item>
/// <item>سربرگ دسته بدون دسته منتشر نمی‌شود؛ با دسته، CategoryId بنر پر می‌شود.</item>
/// <item>ضریب امتیاز: فقط بخش مشمول سفارش (با زیرشاخه‌های دسته)؛ کمپین لغوشده/خارج از زمان اثری ندارد.</item>
/// <item>نگه‌داری: کمپین گذشته ← تمام‌شده، پیش‌نویس رهاشده ← لغو؛ توقف کمپین فعال ← کوپن غیرفعال.</item>
/// </list>
/// هر آزمون فقط دادهٔ خودش را می‌سازد و در finally پاک می‌کند.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Area", "Campaign")]
public class CampaignRound24IntegrationTests(SqlServerFixture db)
{
    private static string Group() => "brief-" + Guid.NewGuid().ToString("N")[..8];

    private static async Task CleanupAsync(FratakShopDbContext context, IEnumerable<string> groups, IEnumerable<Guid>? designIds = null)
    {
        context.ChangeTracker.Clear();
        var keys = groups.ToList();
        var couponIds = await context.CampaignOffers.IgnoreQueryFilters().Where(o => keys.Contains(o.VariantGroup) && o.CouponId != null)
            .Select(o => o.CouponId!.Value).ToListAsync();
        await context.CouponTargets.IgnoreQueryFilters().Where(t => couponIds.Contains(t.CouponId)).ExecuteDeleteAsync();
        await context.Coupons.IgnoreQueryFilters().Where(c => couponIds.Contains(c.Id)).ExecuteDeleteAsync();
        await context.CampaignOffers.IgnoreQueryFilters().Where(o => keys.Contains(o.VariantGroup)).ExecuteDeleteAsync();
        var ids = (designIds ?? []).ToList();
        if (ids.Count > 0)
        {
            var bannerIds = await context.DesignDocs.IgnoreQueryFilters().Where(d => ids.Contains(d.Id) && d.BannerId != null).Select(d => d.BannerId!.Value).ToListAsync();
            await context.DesignDocs.IgnoreQueryFilters().Where(d => ids.Contains(d.Id)).ExecuteDeleteAsync();
            await context.Banners.IgnoreQueryFilters().Where(b => bannerIds.Contains(b.Id)).ExecuteDeleteAsync();
        }
    }

    private static DesignDoc BannerDoc(string group, int variant, Guid? categoryId = null, string preset = DesignPresets.Wide) => new()
    {
        Title = "کمپین آزمون " + variant,
        Kind = DesignKind.Banner,
        Status = DesignStatus.Suggested,
        IsSmart = true,
        PresetKey = preset,
        Width = 1920,
        Height = 320,
        Json = DesignTemplates.Blank(1920, 320),
        ImageName = "itest-" + Guid.NewGuid().ToString("N")[..8] + ".webp",
        IdeaKey = $"brief:-:{group["brief-".Length..]}:{variant}",
        Headline = "۲۰٪ تخفیف",
        LinkUrl = "/products",
        CategoryId = categoryId,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(30),
    };

    [SqlFact]
    public async Task Draft_creates_an_inactive_targeted_coupon_and_cancel_removes_it()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var campaigns = scope.ServiceProvider.GetRequiredService<ICampaignOfferService>();
        var group = Group();
        var categoryId = Guid.NewGuid();
        try
        {
            var code = await campaigns.NewCodeAsync();
            Assert.Matches("^CMP[A-Z2-9]{5}$", code);
            var offer = await campaigns.CreateDraftAsync(new BriefOffer(30, 2, CampaignOfferScope.Category, categoryId, "هدفون", code), group, "حراج هدفون", 7, "آزمون");

            context.ChangeTracker.Clear();
            var row = await context.CampaignOffers.AsNoTracking().SingleAsync(o => o.Id == offer.Id);
            Assert.Equal(CampaignOfferStatus.Draft, row.Status);
            Assert.Equal(30, row.DiscountPercent);
            Assert.Equal(2m, row.PointsMultiplier);
            Assert.Equal(code, row.CouponCode);
            Assert.Null(row.StartsAt);

            var coupon = await context.Coupons.AsNoTracking().Include(c => c.Targets).SingleAsync(c => c.Id == row.CouponId);
            Assert.False(coupon.IsActive);
            Assert.Equal(CouponType.Percentage, coupon.Type);
            Assert.Equal(30m, coupon.Value);
            Assert.True(coupon.ExcludeDiscountedProducts);
            Assert.Equal(1, coupon.UsageLimitPerUser);
            var target = Assert.Single(coupon.Targets);
            Assert.Equal(CouponTargetType.Category, target.TargetType);
            Assert.Equal(categoryId, target.TargetId);

            // کد غیرفعال قابل استفاده نیست (تا انتشار بنر).
            var groups = await campaigns.ForGroupsAsync(new[] { group, "", "brief-nothing" });
            Assert.Equal(offer.Id, Assert.Single(groups).Value.Id);

            Assert.True((await campaigns.CancelAsync(offer.Id, "آزمون")).IsSuccess());
            context.ChangeTracker.Clear();
            Assert.Equal(CampaignOfferStatus.Cancelled, (await context.CampaignOffers.AsNoTracking().SingleAsync(o => o.Id == offer.Id)).Status);
            Assert.False(await context.Coupons.AnyAsync(c => c.Id == row.CouponId));
            Assert.False((await campaigns.CancelAsync(offer.Id, "آزمون")).IsSuccess());
            Assert.False((await campaigns.CancelAsync(Guid.NewGuid(), "آزمون")).IsSuccess());
        }
        finally
        {
            await CleanupAsync(context, new[] { group });
        }
    }

    [SqlFact]
    public async Task Points_only_draft_has_no_coupon_and_values_are_clamped()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var campaigns = scope.ServiceProvider.GetRequiredService<ICampaignOfferService>();
        var group = Group();
        try
        {
            var offer = await campaigns.CreateDraftAsync(new BriefOffer(null, 9, CampaignOfferScope.All, Guid.NewGuid(), null), group, "امتیاز ویژه", 500, null);
            context.ChangeTracker.Clear();
            var row = await context.CampaignOffers.AsNoTracking().SingleAsync(o => o.Id == offer.Id);
            Assert.Null(row.CouponId);
            Assert.Null(row.DiscountPercent);
            Assert.Equal(5m, row.PointsMultiplier);
            Assert.Equal(60, row.DurationDays);
            // دامنهٔ «همه» شناسه ندارد.
            Assert.Null(row.ScopeId);
        }
        finally
        {
            await CleanupAsync(context, new[] { group });
        }
    }

    [SqlFact]
    public async Task Publishing_the_banner_activates_the_draft_atomically_once()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var campaigns = scope.ServiceProvider.GetRequiredService<ICampaignOfferService>();
        var designs = scope.ServiceProvider.GetRequiredService<IDesignService>();
        var group = Group();
        var docs = new List<Guid>();
        try
        {
            var offer = await campaigns.CreateDraftAsync(new BriefOffer(25, null, CampaignOfferScope.All, null, null), group, "حراج آزمون", 7, "آزمون");
            docs.Add(await designs.AddAsync(BannerDoc(group, 1)));
            docs.Add(await designs.AddAsync(BannerDoc(group, 2)));

            var before = DateTimeOffset.UtcNow.AddSeconds(-2);
            var result = await designs.PublishBannerAsync(docs[0], BannerPosition.None, null, null, "مدیر آزمون");
            Assert.True(result.IsSuccess(), result.Message);
            Assert.Contains("فعال شد", result.Message);
            Assert.Contains(offer.CouponCode!, result.Message);

            context.ChangeTracker.Clear();
            var row = await context.CampaignOffers.AsNoTracking().SingleAsync(o => o.Id == offer.Id);
            Assert.Equal(CampaignOfferStatus.Active, row.Status);
            Assert.NotNull(row.StartsAt);
            Assert.True(row.StartsAt >= before);
            Assert.Equal("مدیر آزمون", row.ActivatedByName);
            var coupon = await context.Coupons.AsNoTracking().SingleAsync(c => c.Id == row.CouponId);
            Assert.True(coupon.IsActive);
            Assert.Equal(row.EndsAt, coupon.EndsAt);
            var banner = await context.Banners.AsNoTracking().SingleAsync(b => b.Id == result.Data);
            Assert.Equal(row.EndsAt, banner.EndAt);
            Assert.Equal(group, banner.VariantGroup);

            // نسخهٔ دوم (آزمون A/B) کمپین را دوباره فعال/تمدید نمی‌کند.
            var second = await designs.PublishBannerAsync(docs[1], BannerPosition.None, null, null, "مدیر آزمون");
            Assert.True(second.IsSuccess());
            Assert.DoesNotContain("فعال شد", second.Message);
            context.ChangeTracker.Clear();
            Assert.Equal(row.StartsAt, (await context.CampaignOffers.AsNoTracking().SingleAsync(o => o.Id == offer.Id)).StartsAt);
        }
        finally
        {
            await CleanupAsync(context, new[] { group }, docs);
        }
    }

    [SqlFact]
    public async Task Category_header_needs_a_category_and_carries_it_to_the_banner()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var designs = scope.ServiceProvider.GetRequiredService<IDesignService>();
        var group = Group();
        var docs = new List<Guid>();
        var categoryId = Guid.NewGuid();
        try
        {
            docs.Add(await designs.AddAsync(BannerDoc(group, 1, null, DesignPresets.CategoryHeader)));
            var refused = await designs.PublishBannerAsync(docs[0], BannerPosition.None, null, null, "آزمون");
            Assert.False(refused.IsSuccess());
            Assert.Contains("دسته", refused.Message);

            docs.Add(await designs.AddAsync(BannerDoc(group, 2, categoryId, DesignPresets.CategoryHeader)));
            var published = await designs.PublishBannerAsync(docs[1], BannerPosition.None, null, null, "آزمون");
            Assert.True(published.IsSuccess(), published.Message);
            context.ChangeTracker.Clear();
            var banner = await context.Banners.AsNoTracking().SingleAsync(b => b.Id == published.Data);
            Assert.Equal(BannerPosition.CategoryHeader, banner.Position);
            Assert.Equal(categoryId, banner.CategoryId);
        }
        finally
        {
            await CleanupAsync(context, new[] { group }, docs);
        }
    }

    [SqlFact]
    public async Task Points_boost_applies_only_to_in_scope_items_including_subcategories()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var group = Group();
        var key = Guid.NewGuid().ToString("N")[..8];
        var parent = new Category { Name = "والد کمپین " + key, Slug = "camp-p-" + key, IsActive = true };
        var child = new Category { Name = "فرزند کمپین " + key, Slug = "camp-c-" + key, IsActive = true };
        Guid? orderId = null;
        var inScope = await db.CreateProductAsync(100_000, 5);
        var outScope = await db.CreateProductAsync(100_000, 5);
        try
        {
            context.Categories.Add(parent);
            await context.SaveChangesAsync();
            child.ParentId = parent.Id;
            context.Categories.Add(child);
            await context.SaveChangesAsync();
            await context.Products.Where(p => p.Id == inScope).ExecuteUpdateAsync(s => s.SetProperty(p => p.CategoryId, child.Id));

            var order = new Order
            {
                OrderNumber = "CP" + Random.Shared.NextInt64(100_000_000, 999_999_999),
                Status = OrderStatus.Delivered,
                PaymentMethod = PaymentMethod.OnlineGateway,
                PaymentStatus = PaymentStatus.Paid,
                SubtotalAmount = 200_000,
                PayableAmount = 200_000,
                CustomerName = "مشتری آزمون",
                CustomerPhone = "09120000000",
                ShippingProvince = "تهران",
                ShippingCity = "تهران",
                ShippingAddress = "خیابان آزمون",
                ShippingPostalCode = "1234567890",
                ShippingPhone = "09120000000",
            };
            order.Items.Add(new OrderItem { ProductId = inScope, ProductName = "مشمول", SKU = "C-1", UnitPrice = 100_000, Quantity = 1, TotalAmount = 100_000 });
            order.Items.Add(new OrderItem { ProductId = outScope, ProductName = "نامشمول", SKU = "C-2", UnitPrice = 100_000, Quantity = 1, TotalAmount = 100_000 });
            context.Orders.Add(order);
            await context.SaveChangesAsync();
            orderId = order.Id;

            // بدون کمپین ← همان امتیاز.
            Assert.Equal(100, await CampaignOffers.BoostPurchasePointsAsync(context, order.Id, 100));

            var now = DateTimeOffset.UtcNow;
            var offer = new CampaignOffer
            {
                Title = "امتیاز دوبرابر والد", VariantGroup = group, Scope = CampaignOfferScope.Category, ScopeId = parent.Id, ScopeName = parent.Name,
                PointsMultiplier = 2, Status = CampaignOfferStatus.Active, StartsAt = now.AddHours(-1), EndsAt = now.AddDays(1),
            };
            context.CampaignOffers.Add(offer);
            await context.SaveChangesAsync();

            // نیمی از مبلغ مشمول (زیرشاخهٔ دستهٔ کمپین) ×۲ ← ۱۵۰.
            Assert.Equal(150, await CampaignOffers.BoostPurchasePointsAsync(context, order.Id, 100));
            Assert.Equal(0, await CampaignOffers.BoostPurchasePointsAsync(context, order.Id, 0));
            Assert.Equal(100, await CampaignOffers.BoostPurchasePointsAsync(context, Guid.NewGuid(), 100));

            // لغوشده ← بی‌اثر.
            await context.CampaignOffers.Where(o => o.Id == offer.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, CampaignOfferStatus.Cancelled));
            Assert.Equal(100, await CampaignOffers.BoostPurchasePointsAsync(context, order.Id, 100));

            // کمپین بعد از ثبت سفارش شروع شده ← بی‌اثر؛ کمپین تمام‌شده‌ای که هنگام سفارش فعال بود ← حق مشتری محفوظ.
            await context.CampaignOffers.Where(o => o.Id == offer.Id).ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, CampaignOfferStatus.Active).SetProperty(o => o.StartsAt, now.AddHours(1)));
            Assert.Equal(100, await CampaignOffers.BoostPurchasePointsAsync(context, order.Id, 100));
            await context.CampaignOffers.Where(o => o.Id == offer.Id).ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, CampaignOfferStatus.Ended).SetProperty(o => o.StartsAt, now.AddHours(-1)));
            Assert.Equal(150, await CampaignOffers.BoostPurchasePointsAsync(context, order.Id, 100));

            // کمپین «همه» ×۳ کنار کمپین دسته ← بیشترین ضریب هر قلم (نه حاصل‌ضرب): هر دو قلم ×۳ ← ۳۰۰.
            context.CampaignOffers.Add(new CampaignOffer
            {
                Title = "امتیاز سه‌برابر همه", VariantGroup = group, Scope = CampaignOfferScope.All, PointsMultiplier = 3,
                Status = CampaignOfferStatus.Active, StartsAt = now.AddHours(-1), EndsAt = now.AddDays(1),
            });
            await context.SaveChangesAsync();
            Assert.Equal(300, await CampaignOffers.BoostPurchasePointsAsync(context, order.Id, 100));
        }
        finally
        {
            context.ChangeTracker.Clear();
            if (orderId is { } id)
            {
                await context.OrderItems.IgnoreQueryFilters().Where(i => i.OrderId == id).ExecuteDeleteAsync();
                await context.Orders.IgnoreQueryFilters().Where(o => o.Id == id).ExecuteDeleteAsync();
            }
            await CleanupAsync(context, new[] { group });
            await context.Products.IgnoreQueryFilters().Where(p => p.Id == inScope || p.Id == outScope).ExecuteDeleteAsync();
            await context.Categories.IgnoreQueryFilters().Where(c => c.Id == child.Id).ExecuteDeleteAsync();
            await context.Categories.IgnoreQueryFilters().Where(c => c.Id == parent.Id).ExecuteDeleteAsync();
        }
    }

    [SqlFact]
    public async Task Maintenance_ends_past_campaigns_and_cancels_stale_drafts_and_stop_deactivates_coupon()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var campaigns = scope.ServiceProvider.GetRequiredService<ICampaignOfferService>();
        var designs = scope.ServiceProvider.GetRequiredService<IDesignService>();
        var past = Group();
        var stale = Group();
        var live = Group();
        var docs = new List<Guid>();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var ended = await campaigns.CreateDraftAsync(new BriefOffer(null, 2, CampaignOfferScope.All, null, null), past, "گذشته", 7, null);
            await context.CampaignOffers.Where(o => o.Id == ended.Id).ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, CampaignOfferStatus.Active).SetProperty(o => o.StartsAt, now.AddDays(-8)).SetProperty(o => o.EndsAt, now.AddDays(-1)));
            var old = await campaigns.CreateDraftAsync(new BriefOffer(20, null, CampaignOfferScope.All, null, null), stale, "رهاشده", 7, null);
            await context.CampaignOffers.Where(o => o.Id == old.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.CreatedAt, now.AddDays(-31)));

            Assert.True(await campaigns.MaintainAsync(now) >= 2);
            context.ChangeTracker.Clear();
            Assert.Equal(CampaignOfferStatus.Ended, (await context.CampaignOffers.AsNoTracking().SingleAsync(o => o.Id == ended.Id)).Status);
            Assert.Equal(CampaignOfferStatus.Cancelled, (await context.CampaignOffers.AsNoTracking().SingleAsync(o => o.Id == old.Id)).Status);
            Assert.False(await context.Coupons.AnyAsync(c => c.Id == old.CouponId));

            // توقف کمپین فعال ← تمام‌شده و کوپن غیرفعال.
            var running = await campaigns.CreateDraftAsync(new BriefOffer(15, null, CampaignOfferScope.All, null, null), live, "فعال", 7, null);
            docs.Add(await designs.AddAsync(BannerDoc(live, 1)));
            Assert.True((await designs.PublishBannerAsync(docs[0], BannerPosition.None, null, null, "آزمون")).IsSuccess());
            var stop = await campaigns.CancelAsync(running.Id, "آزمون");
            Assert.True(stop.IsSuccess());
            context.ChangeTracker.Clear();
            var row = await context.CampaignOffers.AsNoTracking().SingleAsync(o => o.Id == running.Id);
            Assert.Equal(CampaignOfferStatus.Ended, row.Status);
            Assert.True(row.EndsAt <= DateTimeOffset.UtcNow);
            Assert.False((await context.Coupons.AsNoTracking().SingleAsync(c => c.Id == running.CouponId)).IsActive);

            var list = await campaigns.ListAsync(200);
            Assert.Contains(list, o => o.Id == running.Id);
        }
        finally
        {
            await CleanupAsync(context, new[] { past, stale, live }, docs);
        }
    }
}
