using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Searches;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Shopping;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست یکپارچهٔ دور ۲۴ (بستهٔ B – تجربهٔ خرید) روی SQL Server واقعی.
/// <list type="bullet">
/// <item>تضمین قیمت: ثبت، یک درخواست باز برای هر کالا، سقف روزانه، کالای ناموجود، تأیید ← کوپن شخصی یک‌بارمصرف فقط برای همان کالا،
/// تأیید دوباره ممنوع، رد با دلیل، قیمت ما پایین آمده ← تأیید ممنوع.</item>
/// <item>سفارش دوباره: فقط سفارش خود مشتری؛ کالای ناموجود رد می‌شود؛ تعداد به موجودی محدود می‌شود.</item>
/// <item>رتبه‌بندی یادگیرنده: کلیک تکراری همان روز شمرده نمی‌شود؛ کالای غیرفعال/مرورگر بدون شناسه نادیده؛ مرتب‌سازی «مرتبط‌ترین» کالای پرکلیک را بالا می‌آورد.</item>
/// <item>آمار واقعی خرید: زیر آستانه چیزی نشان نمی‌دهد؛ با سفارش‌های پرداخت‌شده جمله می‌سازد.</item>
/// </list>
/// هر آزمون فقط دادهٔ خودش را می‌سازد و در finally پاک می‌کند.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Area", "Commerce")]
public class CommerceRound24IntegrationTests(SqlServerFixture db)
{
    private static async Task CleanupPriceMatchAsync(FratakShopDbContext context, Guid userId)
    {
        context.ChangeTracker.Clear();
        var codes = await context.PriceMatchRequests.IgnoreQueryFilters().Where(r => r.UserId == userId && r.CouponCode != null).Select(r => r.CouponCode!).ToListAsync();
        var couponIds = await context.Coupons.IgnoreQueryFilters().Where(c => codes.Contains(c.Code)).Select(c => c.Id).ToListAsync();
        await context.CouponTargets.IgnoreQueryFilters().Where(t => couponIds.Contains(t.CouponId)).ExecuteDeleteAsync();
        await context.Coupons.IgnoreQueryFilters().Where(c => couponIds.Contains(c.Id)).ExecuteDeleteAsync();
        await context.PriceMatchRequests.IgnoreQueryFilters().Where(r => r.UserId == userId).ExecuteDeleteAsync();
        await context.Notifications.IgnoreQueryFilters().Where(n => n.UserId == userId).ExecuteDeleteAsync();
        await context.StaffNotifications.IgnoreQueryFilters().Where(n => n.Url == "/admin/marketing/price-match").ExecuteDeleteAsync();
    }

    // ── تضمین قیمت ──────────────────────────────────────────────

    [SqlFact]
    public async Task PriceMatch_submit_then_approve_creates_personal_product_coupon()
    {
        var userId = await db.CreateUserAsync();
        var productId = await db.CreateProductAsync(price: 1_000_000, stock: 5);
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IPriceMatchService>();
        try
        {
            var submitted = await service.SubmitAsync(userId, new PriceMatchInput(productId, "https://www.example-shop.ir/p/1", 955_500m), "shop.fratak.ir");
            Assert.True(submitted.IsSuccess(), submitted.Message);

            // یک درخواست باز برای هر کالا.
            var duplicate = await service.SubmitAsync(userId, new PriceMatchInput(productId, "https://other.ir/p", 960_000m), "shop.fratak.ir");
            Assert.False(duplicate.IsSuccess());

            var request = await context.PriceMatchRequests.SingleAsync(r => r.UserId == userId);
            Assert.Equal(PriceMatchStatus.Pending, request.Status);
            Assert.Equal(1_000_000m, request.OurPrice);
            Assert.Equal(955_500m, request.CompetitorPrice);
            Assert.True(await service.PendingCountAsync() >= 1);
            Assert.Contains(await service.ListAsync(PriceMatchStatus.Pending), r => r.Id == request.Id && r.CompetitorHost == "example-shop.ir");

            var approved = await service.ApproveAsync(request.Id, "مدیر آزمون", "بررسی شد");
            Assert.True(approved.IsSuccess(), approved.Message);

            context.ChangeTracker.Clear();
            request = await context.PriceMatchRequests.SingleAsync(r => r.Id == request.Id);
            Assert.Equal(PriceMatchStatus.Approved, request.Status);
            Assert.StartsWith("PM-", request.CouponCode);
            Assert.Equal("مدیر آزمون", request.ReviewerName);

            var coupon = await context.Coupons.Include(c => c.Targets).SingleAsync(c => c.Code == request.CouponCode);
            Assert.Equal(CouponType.FixedAmount, coupon.Type);
            Assert.Equal(44_000m, coupon.Value);                   // ۴۴٬۵۰۰ ← گرد به پایین تا هزار
            Assert.Equal(userId, coupon.UserId);
            Assert.False(coupon.IsPublic);
            Assert.Equal(1, coupon.UsageLimit);
            Assert.True(coupon.EndsAt <= DateTimeOffset.UtcNow.AddHours(IPriceMatchService.CouponHours + 1));
            var target = Assert.Single(coupon.Targets);
            Assert.Equal(CouponTargetType.Product, target.TargetType);
            Assert.Equal(productId, target.TargetId);

            Assert.True(await context.Notifications.AnyAsync(n => n.UserId == userId));

            // دوباره تأیید یا رد نمی‌شود.
            Assert.False((await service.ApproveAsync(request.Id, "مدیر", null)).IsSuccess());
            Assert.False((await service.RejectAsync(request.Id, "مدیر", null)).IsSuccess());
            Assert.Equal(1, await context.Coupons.CountAsync(c => c.UserId == userId && c.Code.StartsWith("PM-")));
        }
        finally
        {
            await CleanupPriceMatchAsync(context, userId);
        }
    }

    [SqlFact]
    public async Task PriceMatch_daily_limit_and_validation_attacks()
    {
        var userId = await db.CreateUserAsync();
        var products = new List<Guid>();
        for (var i = 0; i < 4; i++)
            products.Add(await db.CreateProductAsync(price: 500_000, stock: 3));
        var outOfStock = await db.CreateProductAsync(price: 500_000, stock: 0);
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IPriceMatchService>();
        try
        {
            // حمله‌ها و ورودی بد: هیچ ردیفی ساخته نمی‌شود.
            Assert.False((await service.SubmitAsync(Guid.Empty, new PriceMatchInput(products[0], "https://a.ir/x", 450_000m), null)).IsSuccess());
            Assert.False((await service.SubmitAsync(userId, new PriceMatchInput(products[0], "javascript:alert(1)", 450_000m), null)).IsSuccess());
            Assert.False((await service.SubmitAsync(userId, new PriceMatchInput(products[0], "http://127.0.0.1/admin", 450_000m), null)).IsSuccess());
            Assert.False((await service.SubmitAsync(userId, new PriceMatchInput(products[0], "https://shop.fratak.ir/p", 450_000m), "shop.fratak.ir")).IsSuccess());
            Assert.False((await service.SubmitAsync(userId, new PriceMatchInput(products[0], "https://a.ir/x", 100_000m), null)).IsSuccess()); // ۸۰٪ پایین‌تر
            Assert.False((await service.SubmitAsync(userId, new PriceMatchInput(products[0], "https://a.ir/x", 600_000m), null)).IsSuccess()); // گران‌تر
            Assert.False((await service.SubmitAsync(userId, new PriceMatchInput(outOfStock, "https://a.ir/x", 450_000m), null)).IsSuccess());
            Assert.False((await service.SubmitAsync(userId, new PriceMatchInput(Guid.NewGuid(), "https://a.ir/x", 450_000m), null)).IsSuccess());
            Assert.Equal(0, await context.PriceMatchRequests.CountAsync(r => r.UserId == userId));

            // سقف روزانه: ۳ درخواست، چهارمی رد.
            for (var i = 0; i < IPriceMatchService.DailyLimit; i++)
                Assert.True((await service.SubmitAsync(userId, new PriceMatchInput(products[i], "https://a.ir/x" + i, 480_000m), null)).IsSuccess());
            var fourth = await service.SubmitAsync(userId, new PriceMatchInput(products[3], "https://a.ir/x3", 480_000m), null);
            Assert.False(fourth.IsSuccess());
            Assert.Contains("فردا", fourth.Message);
            Assert.Equal(IPriceMatchService.DailyLimit, await context.PriceMatchRequests.CountAsync(r => r.UserId == userId));
        }
        finally
        {
            await CleanupPriceMatchAsync(context, userId);
        }
    }

    [SqlFact]
    public async Task PriceMatch_reject_notifies_and_price_drop_blocks_approval()
    {
        var userId = await db.CreateUserAsync();
        var first = await db.CreateProductAsync(price: 800_000, stock: 2);
        var second = await db.CreateProductAsync(price: 800_000, stock: 2);
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IPriceMatchService>();
        try
        {
            Assert.True((await service.SubmitAsync(userId, new PriceMatchInput(first, "https://a.ir/1", 760_000m), null)).IsSuccess());
            Assert.True((await service.SubmitAsync(userId, new PriceMatchInput(second, "https://a.ir/2", 760_000m), null)).IsSuccess());
            var ids = await context.PriceMatchRequests.Where(r => r.UserId == userId).ToDictionaryAsync(r => r.ProductId, r => r.Id);

            var rejected = await service.RejectAsync(ids[first], "مدیر", "  گارانتی متفاوت  ");
            Assert.True(rejected.IsSuccess());
            context.ChangeTracker.Clear();
            var row = await context.PriceMatchRequests.SingleAsync(r => r.Id == ids[first]);
            Assert.Equal(PriceMatchStatus.Rejected, row.Status);
            Assert.Equal("گارانتی متفاوت", row.AdminNote);
            Assert.Null(row.CouponCode);

            // قیمت ما تا لحظهٔ تأیید به زیر قیمت رقیب رسیده ← کوپنی ساخته نمی‌شود و درخواست باز می‌ماند.
            await context.Products.Where(p => p.Id == second).ExecuteUpdateAsync(s => s.SetProperty(p => p.Price, 750_000m));
            context.ChangeTracker.Clear();
            var blocked = await service.ApproveAsync(ids[second], "مدیر", null);
            Assert.False(blocked.IsSuccess());
            Assert.Equal(PriceMatchStatus.Pending, (await context.PriceMatchRequests.SingleAsync(r => r.Id == ids[second])).Status);
            Assert.False(await context.Coupons.AnyAsync(c => c.UserId == userId && c.Code.StartsWith("PM-")));

            Assert.False((await service.ApproveAsync(Guid.NewGuid(), "مدیر", null)).IsSuccess());
        }
        finally
        {
            await CleanupPriceMatchAsync(context, userId);
        }
    }

    // ── سفارش دوباره ────────────────────────────────────────────

    private static Order NewOrder(Guid? userId) => new()
    {
        OrderNumber = "R" + Random.Shared.NextInt64(1_000_000_000, 9_999_999_999),
        UserId = userId,
        Status = OrderStatus.Delivered,
        PaymentMethod = PaymentMethod.OnlineGateway,
        PaymentStatus = PaymentStatus.Paid,
        SubtotalAmount = 1_000_000,
        PayableAmount = 1_000_000,
        CustomerName = "مشتری آزمایشی",
        CustomerPhone = "09120000000",
        ShippingProvince = "تهران",
        ShippingCity = "تهران",
        ShippingAddress = "خیابان آزمایش",
        ShippingPostalCode = "1234567890",
        ShippingPhone = "09120000000",
    };

    private static OrderItem Line(Guid? productId, string name, int quantity) => new()
    {
        ProductId = productId,
        ProductName = name,
        SKU = "SKU-" + Guid.NewGuid().ToString("N")[..6],
        UnitPrice = 100_000,
        Quantity = quantity,
        TotalAmount = 100_000 * quantity,
    };

    [SqlFact]
    public async Task Reorder_adds_available_items_skips_out_of_stock_and_caps_quantity()
    {
        var userId = await db.CreateUserAsync();
        var otherUser = await db.CreateUserAsync();
        var plenty = await db.CreateProductAsync(stock: 10);
        var scarce = await db.CreateProductAsync(stock: 1);
        var empty = await db.CreateProductAsync(stock: 0);
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<IReorderService>();
        var order = NewOrder(userId);
        order.Items.Add(Line(plenty, "کالای فراوان", 2));
        order.Items.Add(Line(scarce, "کالای کم", 3));
        order.Items.Add(Line(empty, "کالای ناموجود", 1));
        order.Items.Add(Line(null, "کالای حذف‌شده", 1));
        context.Orders.Add(order);
        var cart = new Cart { UserId = userId };
        context.Carts.Add(cart);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        try
        {
            // سفارش دیگران ← پیدا نمی‌شود (بدون نشت اطلاعات).
            Assert.False((await service.ReorderAsync(otherUser, order.OrderNumber, cart.Id)).IsSuccess());
            Assert.False((await service.ReorderAsync(userId, "NOPE-1", cart.Id)).IsSuccess());
            Assert.False((await service.ReorderAsync(userId, "  ", cart.Id)).IsSuccess());

            var result = await service.ReorderAsync(userId, order.OrderNumber, cart.Id);
            Assert.True(result.IsSuccess());
            Assert.Equal(2, result.Data.Added);
            Assert.Contains("کالای ناموجود", result.Data.Skipped);
            Assert.Contains("کالای حذف‌شده", result.Data.Skipped);

            var items = await context.CartItems.Where(i => i.CartId == cart.Id).ToDictionaryAsync(i => i.ProductId, i => i.Quantity);
            Assert.Equal(2, items[plenty]);
            Assert.Equal(1, items[scarce]);       // ۳ خواسته، ۱ موجود
            Assert.False(items.ContainsKey(empty));

            // تکرار: «کم» دیگر جا ندارد ولی چون در سبد است «ناموجود» حساب نمی‌شود.
            var again = await service.ReorderAsync(userId, order.OrderNumber, cart.Id);
            Assert.True(again.IsSuccess());
            Assert.DoesNotContain("کالای کم", again.Data.Skipped);
            Assert.Equal(1, await context.CartItems.Where(i => i.CartId == cart.Id && i.ProductId == scarce).SumAsync(i => i.Quantity));
        }
        finally
        {
            context.ChangeTracker.Clear();
            await context.CartItems.IgnoreQueryFilters().Where(i => i.CartId == cart.Id).ExecuteDeleteAsync();
            await context.Carts.IgnoreQueryFilters().Where(c => c.Id == cart.Id).ExecuteDeleteAsync();
            await context.OrderItems.IgnoreQueryFilters().Where(i => i.OrderId == order.Id).ExecuteDeleteAsync();
            await context.Orders.IgnoreQueryFilters().Where(o => o.Id == order.Id).ExecuteDeleteAsync();
        }
    }

    // ── رتبه‌بندی یادگیرنده ─────────────────────────────────────

    [SqlFact]
    public async Task SearchClick_is_deduplicated_and_ignores_bad_input()
    {
        var productId = await db.CreateProductAsync();
        var query = "هدفون آزمون " + Guid.NewGuid().ToString("N")[..6];
        var key = SearchLearning.Key(query)!;
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ISearchLearningService>();
        try
        {
            var visitor = "v-" + Guid.NewGuid().ToString("N");
            Assert.True(await service.RecordClickAsync(query, productId, visitor));
            Assert.False(await service.RecordClickAsync(query, productId, visitor));            // همان روز، همان مرورگر
            Assert.False(await service.RecordClickAsync("  " + query.Replace(" ", "  ") + " ", productId, visitor)); // همان عبارت نرمال‌شده
            Assert.True(await service.RecordClickAsync(query, productId, "v-" + Guid.NewGuid().ToString("N"))); // مرورگر دیگر

            Assert.False(await service.RecordClickAsync(query, productId, null));
            Assert.False(await service.RecordClickAsync(query, productId, new string('x', 65)));
            Assert.False(await service.RecordClickAsync("a", productId, visitor));
            Assert.False(await service.RecordClickAsync(query, Guid.Empty, visitor));
            Assert.False(await service.RecordClickAsync(query, Guid.NewGuid(), "v-other"));     // کالای ناموجود در پایگاه

            context.ChangeTracker.Clear();
            var stat = await context.SearchClickStats.SingleAsync(s => s.NormalizedQuery == key && s.ProductId == productId);
            Assert.Equal(2, stat.Clicks);
        }
        finally
        {
            context.ChangeTracker.Clear();
            await context.SearchClickStats.IgnoreQueryFilters().Where(s => s.NormalizedQuery == key).ExecuteDeleteAsync();
        }
    }

    [SqlFact]
    public async Task SearchClick_hourly_cap_per_visitor()
    {
        var products = new List<Guid>();
        for (var i = 0; i < 3; i++)
            products.Add(await db.CreateProductAsync());
        var prefix = "سقف " + Guid.NewGuid().ToString("N")[..6];
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ISearchLearningService>();
        var visitor = "v-cap-" + Guid.NewGuid().ToString("N")[..10];
        try
        {
            var accepted = 0;
            for (var i = 0; i < ISearchLearningService.MaxClicksPerHour + 5; i++)
            {
                if (await service.RecordClickAsync($"{prefix} {i}", products[i % products.Count], visitor))
                    accepted++;
            }
            Assert.Equal(ISearchLearningService.MaxClicksPerHour, accepted);
        }
        finally
        {
            context.ChangeTracker.Clear();
            await context.SearchClickStats.IgnoreQueryFilters().Where(s => s.NormalizedQuery.StartsWith(prefix)).ExecuteDeleteAsync();
        }
    }

    [SqlFact]
    public async Task SearchMaintain_halves_stale_rows_and_removes_old_ones()
    {
        var productId = await db.CreateProductAsync();
        var stale = "کهنه " + Guid.NewGuid().ToString("N")[..6];
        var ancient = "باستانی " + Guid.NewGuid().ToString("N")[..6];
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<ISearchLearningService>();
        try
        {
            context.SearchClickStats.Add(new Fratak_Shop.Data.Entities.Searches.SearchClickStat { NormalizedQuery = stale, ProductId = productId, Clicks = 10, LastClickAt = DateTimeOffset.UtcNow.AddDays(-40) });
            context.SearchClickStats.Add(new Fratak_Shop.Data.Entities.Searches.SearchClickStat { NormalizedQuery = ancient, ProductId = productId, Clicks = 10, LastClickAt = DateTimeOffset.UtcNow.AddDays(-200) });
            await context.SaveChangesAsync();
            await context.SearchClickStats.Where(s => s.NormalizedQuery == stale || s.NormalizedQuery == ancient)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.UpdatedAt, DateTimeOffset.UtcNow.AddDays(-40)));
            context.ChangeTracker.Clear();

            Assert.True(await service.MaintainAsync() >= 2);
            context.ChangeTracker.Clear();
            Assert.Equal(5, (await context.SearchClickStats.SingleAsync(s => s.NormalizedQuery == stale)).Clicks);
            Assert.False(await context.SearchClickStats.IgnoreQueryFilters().AnyAsync(s => s.NormalizedQuery == ancient));
        }
        finally
        {
            context.ChangeTracker.Clear();
            await context.SearchClickStats.IgnoreQueryFilters().Where(s => s.NormalizedQuery == stale || s.NormalizedQuery == ancient).ExecuteDeleteAsync();
        }
    }

    // ── آمار واقعی خرید ─────────────────────────────────────────

    [SqlFact]
    public async Task SocialProof_counts_only_recent_real_orders()
    {
        var productId = await db.CreateProductAsync();
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var proof = scope.ServiceProvider.GetRequiredService<ISocialProofService>();
        var orders = new List<Order>();
        try
        {
            Assert.Null((await proof.GetAsync(productId)).Text);

            // ۳ سفارش پرداخت‌شده + ۲ لغوشده (لغوشده‌ها شمرده نمی‌شوند).
            for (var i = 0; i < 5; i++)
            {
                var order = NewOrder(null);
                if (i >= 3)
                    order.Status = OrderStatus.Cancelled;
                order.Items.Add(Line(productId, "کالا", 1));
                orders.Add(order);
                context.Orders.Add(order);
            }
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            // سرویس تازه (کش ۱۰ دقیقه‌ای نتیجهٔ قبلی را نگه داشته؛ کلید را پاک می‌کنیم).
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>().Remove($"proof:{productId:N}");
            var result = await proof.GetAsync(productId);
            Assert.Equal(3, result.Buyers);
            Assert.Equal("۳ نفر در ۷ روز گذشته این کالا را خریده‌اند", result.Text);
        }
        finally
        {
            context.ChangeTracker.Clear();
            var ids = orders.Select(o => o.Id).ToList();
            await context.OrderItems.IgnoreQueryFilters().Where(i => ids.Contains(i.OrderId)).ExecuteDeleteAsync();
            await context.Orders.IgnoreQueryFilters().Where(o => ids.Contains(o.Id)).ExecuteDeleteAsync();
        }
    }
}
