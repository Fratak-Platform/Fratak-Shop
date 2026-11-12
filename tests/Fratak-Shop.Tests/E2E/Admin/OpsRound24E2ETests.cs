using System.Net;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Support;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Tests.E2E.Admin;

/// <summary>
/// تست سرتاسری دور ۲۴ بستهٔ F روی برنامهٔ واقعی (TestServer + SQL Server):
/// <list type="bullet">
/// <item>۷۳ صفحهٔ «دسترس‌پذیری» و ۷۴ «گزارش امنیتی» برای مدیر باز می‌شوند (با موارد data-security-check و دکمهٔ بازسازی) و برای نقش بی‌ربط بسته‌اند.</item>
/// <item>۲۷ صفحهٔ تیکت پیشنهاد پاسخ هوشمند با وضعیت واقعی سفارش مشتری نشان می‌دهد (فقط درج در کادر، هرگز ارسال خودکار).</item>
/// <item>۶۹ ایمیل سفارش خرید: تأمین‌کنندهٔ بی‌ایمیل یا بی‌کالا پیام روشن می‌گیرد؛ شناسهٔ ناموجود ۴۰۴؛ بدون توکن ضدجعل رد.</item>
/// <item>۷۱ کد تخفیف: بعد از ۳ کد اشتباه از یک IP کد امنیتی لازم می‌شود؛ IP دیگر تحت تأثیر نیست.</item>
/// </list>
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Round24Ops")]
public class OpsRound24E2ETests(ShopApp app)
{
    private static string Key() => Guid.NewGuid().ToString("N")[..8];

    // ── ۷۳ و ۷۴: صفحه‌های فنی ─────────────────────────────────────────

    [SqlFact]
    public async Task Uptime_page_shows_availability_and_external_monitoring_hint()
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);
        var page = await client.GetPageAsync("/admin/system/uptime");
        Assert.Contains("دسترس‌پذیری", page.Text, StringComparison.Ordinal);
        Assert.Contains("/healthz", page.Html, StringComparison.Ordinal);
        Assert.Contains("٪", page.Text, StringComparison.Ordinal);
    }

    [SqlFact]
    public async Task Security_report_page_lists_checks_and_can_be_rebuilt()
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);
        var page = await client.GetPageAsync("/admin/system/security-report");
        Assert.NotNull(page.One("[data-security-check='default-password']"));
        Assert.NotNull(page.One("[data-security-check='captcha']"));
        Assert.NotNull(page.One("[data-security-check='backup']"));
        Assert.Contains("از ۱۰۰", page.Text, StringComparison.Ordinal);
        // هیچ رمزی در صفحه نیست.
        Assert.DoesNotContain("12345678", page.Html, StringComparison.Ordinal);

        using var refreshed = await client.PostFormAsync("/admin/system/security-report?handler=Refresh");
        Assert.Equal(HttpStatusCode.Redirect, refreshed.StatusCode);
        Assert.StartsWith("/admin/system/security-report", refreshed.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);

        // بدون توکن ضدجعل پذیرفته نمی‌شود.
        using var forged = await client.PostRawFormAsync("/admin/system/security-report?handler=Refresh");
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
    }

    [SqlTheory]
    [InlineData(ShopApp.WriterPhone, "/admin/system/security-report")]
    [InlineData(ShopApp.WriterPhone, "/admin/system/uptime")]
    public async Task Staff_without_monitoring_permission_are_denied(string phone, string url)
    {
        using var client = app.CreateClient();
        await client.LoginAsync(phone);
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/admin/denied", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [SqlFact]
    public async Task Customers_cannot_see_the_new_pages()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(customer.Phone, customer.Password);
        using var report = await client.GetAsync("/admin/system/security-report");
        Assert.Equal(HttpStatusCode.NotFound, report.StatusCode);
        using var uptime = await client.GetAsync("/admin/system/uptime");
        Assert.Equal(HttpStatusCode.NotFound, uptime.StatusCode);
    }

    // ── ۲۷: پیشنهاد پاسخ تیکت ─────────────────────────────────────────

    [SqlFact]
    public async Task Ticket_page_offers_a_smart_reply_with_the_real_order_status()
    {
        var customer = await app.CreateCustomerAsync();
        var orderNumber = "FR-" + Random.Shared.Next(100_000, 999_999);
        var tracking = "77" + Random.Shared.NextInt64(1_000_000_000, 9_999_999_999);
        var ticketId = await app.WithDbAsync(async db =>
        {
            var department = new SupportDepartment { Title = "پشتیبانی " + Key(), IsActive = true };
            db.SupportDepartments.Add(department);
            db.Orders.Add(new Order
            {
                UserId = customer.Id,
                OrderNumber = orderNumber,
                Status = OrderStatus.Shipped,
                PaymentMethod = PaymentMethod.OnlineGateway,
                PaymentStatus = PaymentStatus.Paid,
                SubtotalAmount = 300_000,
                PayableAmount = 300_000,
                CustomerName = "مشتری آزمون",
                CustomerPhone = customer.Phone,
                ShippingProvince = "تهران",
                ShippingCity = "تهران",
                ShippingAddress = "خیابان آزمون",
                ShippingPostalCode = Random.Shared.NextInt64(1_000_000_000, 9_999_999_999).ToString(),
                ShippingPhone = customer.Phone,
                TrackingCode = tracking,
                ShippedAt = DateTimeOffset.UtcNow.AddDays(-1),
            });
            var ticket = new SupportTicket
            {
                TicketNumber = "T" + Random.Shared.Next(10_000_000, 99_999_999),
                UserId = customer.Id,
                Department = department,
                Subject = "سفارشم کجاست",
                Status = TicketStatus.Open,
                LastActivityAt = DateTimeOffset.UtcNow,
            };
            ticket.Messages.Add(new SupportTicketMessage { UserId = customer.Id, AuthorName = "مشتری آزمون", Content = $"سفارش {orderNumber} کی میرسه؟ کد رهگیری ندارم" });
            db.SupportTickets.Add(ticket);
            await db.SaveChangesAsync();
            return ticket.Id;
        });

        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);
        var page = await client.GetPageAsync($"/admin/tickets/{ticketId}");
        var box = page.One("[data-reply-suggestions]");
        Assert.NotNull(box);
        Assert.Contains("پیشنهاد پاسخ هوشمند", box.TextContent, StringComparison.Ordinal);
        var buttons = page.All("[data-reply-suggestions] button[data-fill-target='#reply-content']");
        Assert.NotEmpty(buttons);
        Assert.Contains(buttons, b => (b.GetAttribute("data-fill-text") ?? "").Contains(tracking, StringComparison.Ordinal));
        // پیشنهاد فقط درج می‌کند: دکمه از نوع button است، نه submit.
        Assert.All(buttons, b => Assert.Equal("button", b.GetAttribute("type")));
        // پاسخی خودکار ثبت نشده است.
        Assert.False(await app.WithDbAsync(db => db.SupportTicketMessages.AnyAsync(m => m.TicketId == ticketId && m.IsStaff)));
    }

    // ── ۶۹: ایمیل سفارش خرید ──────────────────────────────────────────

    [SqlFact]
    public async Task Purchase_email_to_supplier_validates_address_and_lines()
    {
        var (noEmail, withEmail) = await app.WithDbAsync(async db =>
        {
            var a = new Supplier { Name = "بی‌ایمیل " + Key(), Phone = "02100000000" };
            var b = new Supplier { Name = "با ایمیل " + Key(), Email = "sales@supplier.example" };
            db.Suppliers.AddRange(a, b);
            await db.SaveChangesAsync();
            return (a.Id, b.Id);
        });
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);

        using var missing = await client.PostFormAsync("/admin/products/inventory?handler=EmailSupplier", ("supplierId", noEmail.ToString()), ("days", "30"));
        Assert.Equal(HttpStatusCode.Redirect, missing.StatusCode);
        Assert.StartsWith("/admin/products/inventory?tab=po", missing.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
        var afterMissing = await client.GetPageAsync(missing.Headers.Location!.OriginalString);
        Assert.Contains("نشانی ایمیل معتبر", afterMissing.Text, StringComparison.Ordinal);

        // ایمیل دارد ولی کالایی برای خرید ندارد ← چیزی فرستاده نمی‌شود.
        using var empty = await client.PostFormAsync("/admin/products/inventory?handler=EmailSupplier", ("supplierId", withEmail.ToString()), ("days", "999"));
        Assert.Equal(HttpStatusCode.Redirect, empty.StatusCode);
        Assert.Contains("days=30", empty.Headers.Location?.OriginalString, StringComparison.Ordinal);
        var afterEmpty = await client.GetPageAsync(empty.Headers.Location!.OriginalString);
        Assert.Contains("نیاز به خرید ندارد", afterEmpty.Text, StringComparison.Ordinal);

        using var unknown = await client.PostFormAsync("/admin/products/inventory?handler=EmailSupplier", ("supplierId", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        using var forged = await client.PostRawFormAsync("/admin/products/inventory?handler=EmailSupplier", ("supplierId", withEmail.ToString()));
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
    }

    [SqlFact]
    public async Task Writer_cannot_email_suppliers()
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.WriterPhone);
        using var response = await client.PostFormAsync("/admin/products/inventory?handler=EmailSupplier", ("supplierId", Guid.NewGuid().ToString()));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/admin/denied", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    // ── ۷۱: کد امنیتی کد تخفیف ────────────────────────────────────────

    [SqlFact]
    public async Task Coupon_box_requires_captcha_after_three_wrong_codes_from_one_ip()
    {
        var code = "CAP" + Key().ToUpperInvariant();
        await app.WithDbAsync(async db =>
        {
            db.Coupons.Add(new Coupon { Code = code, Title = "تخفیف آزمون کپچا", Type = CouponType.FixedAmount, Value = 10_000, IsActive = true });
            await db.SaveChangesAsync();
        });
        var customer = await app.CreateCustomerAsync();
        var (productId, _) = await app.CreateProductAsync(300_000, 10);
        using var client = app.CreateClient();
        await client.LoginAsync(customer.Phone, customer.Password);
        await client.AddToCartAsync(productId);

        var before = await client.GetPageAsync("/cart");
        Assert.DoesNotContain("CaptchaToken", before.Html, StringComparison.Ordinal);

        for (var i = 0; i < 3; i++)
        {
            using var wrong = await client.PostFormAsync("/cart?handler=Coupon", ("code", "WRONG" + i + Key()), ("returnUrl", "/cart"));
            Assert.Equal(HttpStatusCode.Redirect, wrong.StatusCode);
        }

        var after = await client.GetPageAsync("/cart");
        Assert.Contains("CaptchaToken", after.Html, StringComparison.Ordinal);

        // حتی کد درست بدون حل کد امنیتی پذیرفته نمی‌شود.
        using var blocked = await client.PostFormAsync("/cart?handler=Coupon", ("code", code), ("returnUrl", "/cart"));
        Assert.Equal(HttpStatusCode.Redirect, blocked.StatusCode);
        Assert.Null(await app.WithDbAsync(db => db.Carts.Where(c => c.UserId == customer.Id).Select(c => c.CouponCode).FirstOrDefaultAsync()));

        // مشتری دیگری با IP دیگر بدون کد امنیتی کد را می‌زند.
        var other = await app.CreateCustomerAsync();
        using var otherClient = app.CreateClient();
        await otherClient.LoginAsync(other.Phone, other.Password);
        await otherClient.AddToCartAsync(productId);
        Assert.DoesNotContain("CaptchaToken", (await otherClient.GetPageAsync("/cart")).Html, StringComparison.Ordinal);
        using var ok = await otherClient.PostFormAsync("/cart?handler=Coupon", ("code", code), ("returnUrl", "/cart"));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        Assert.Equal(code, await app.WithDbAsync(db => db.Carts.Where(c => c.UserId == other.Id).Select(c => c.CouponCode).FirstOrDefaultAsync()));
    }
}
