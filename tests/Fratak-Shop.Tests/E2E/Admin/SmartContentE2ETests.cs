using System.Net;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Entities.Smart;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Tests.E2E.Admin;

/// <summary>
/// دور نوزدهم از دید مرورگر (برنامهٔ واقعی + SQL Server؛ کارگر پس‌زمینه در آزمون خاموش است و اینترنتی صدا زده نمی‌شود):
/// <list type="bullet">
/// <item>صفحه‌های «محتوای هوشمند» برای مدیر باز می‌شوند و نقش بی‌ربط به «دسترسی ندارید» می‌رسد.</item>
/// <item>دکمهٔ «تکمیل هوشمند» یک کار در صف می‌سازد (بدون تکرار با کلیک دوباره).</item>
/// <item>API دستیار محلی بدون کلید یا با کلید غلط ۴۰۱ می‌دهد (حمله با کلید حدسی/طولانی).</item>
/// <item>فروشگاه: جست‌وجو با کیبورد اشتباه نتیجه می‌دهد؛ جست‌وجوی بی‌نتیجه صفحهٔ خالی نیست؛ پرسش‌نامهٔ «کدام را بخرم».</item>
/// </list>
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Admin")]
public class SmartContentE2ETests(ShopApp app)
{
    [SqlTheory]
    [InlineData("/admin/smart")]
    [InlineData("/admin/smart/categories")]
    [InlineData("/admin/smart/blog")]
    [InlineData("/admin/smart/brief")]
    [InlineData("/admin/smart/settings")]
    public async Task Owner_can_open_every_smart_page(string url)
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);
        var page = await client.GetPageAsync(url);
        Assert.NotNull(page.One(".a-page-head, h1"));
    }

    [SqlFact]
    public async Task Owner_can_queue_smart_completion_for_a_product_once()
    {
        var (productId, _) = await app.CreateProductAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);

        var page = await client.GetPageAsync($"/admin/smart/product/{productId}");
        Assert.Contains("تکمیل هوشمند", page.Text, StringComparison.Ordinal);

        using (var first = await client.PostFormAsync($"/admin/smart/product/{productId}?handler=Run", ("kind", "all")))
            Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        using (var second = await client.PostFormAsync($"/admin/smart/product/{productId}?handler=Run", ("kind", "all")))
            Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);

        var tasks = await app.WithDbAsync(db => db.SmartTasks
            .Where(t => t.TargetId == productId && t.Kind == SmartTaskKind.ProductComplete).ToListAsync());
        var task = Assert.Single(tasks);
        Assert.Equal(SmartTaskStatus.Queued, task.Status);
        Assert.Equal(SmartPriority.Manual, task.Priority);

        // تمیزکاری: کار آزمون در صف نماند.
        await app.WithDbAsync(db => db.SmartTasks.Where(t => t.Id == task.Id).ExecuteDeleteAsync());
    }

    [SqlFact]
    public async Task Smart_product_page_404s_for_unknown_products()
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);
        using var response = await client.GetAsync($"/admin/smart/product/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [SqlTheory]
    [InlineData(ShopApp.AccountantPhone, "/admin/smart")]
    [InlineData(ShopApp.AccountantPhone, "/admin/smart/settings")]
    [InlineData(ShopApp.WriterPhone, "/admin/smart/settings")]
    public async Task Staff_without_permission_are_denied(string phone, string url)
    {
        using var client = app.CreateClient();
        await client.LoginAsync(phone);
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/admin/denied", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [SqlFact]
    public async Task Guests_are_sent_to_login_for_smart_pages()
    {
        using var client = app.CreateClient();
        using var response = await client.GetAsync("/admin/smart");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [SqlTheory]
    [InlineData("/api/helper/ping")]
    [InlineData("/api/helper/next")]
    public async Task Removed_local_helper_api_no_longer_exists(string url)
    {
        // دور ۲۱: «دستیار محلی» و Ollama کاملاً حذف شدند؛ این مسیرها دیگر نباید هیچ کاری انجام دهند.
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Helper-Key", "fh_wrong-key-0123456789abcdefghijklmnop");
        using var response = await client.Http.SendAsync(request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── فروشگاه ───────────────────────────────────────────────────

    private const string Persian = "ضصثقفغعهخحجچشسیبلاتنمکگظطزرذدپو";
    private const string Latin = "qwertyuiop[]asdfghjkl;'zxcvbnm,";

    /// <summary>واژهٔ فارسی تصادفی که فقط از حروفی ساخته شده که روی کیبورد انگلیسی «حرف» هستند (نه نشانه).</summary>
    private static (string Persian, string Latin) RandomWord(int length = 7)
    {
        var letters = Enumerable.Range(0, Persian.Length).Where(i => char.IsLetter(Latin[i])).ToList();
        var picks = Enumerable.Range(0, length).Select(_ => letters[Random.Shared.Next(letters.Count)]).ToList();
        return (new string(picks.Select(i => Persian[i]).ToArray()), new string(picks.Select(i => Latin[i]).ToArray()));
    }

    [SqlFact]
    public async Task Search_typed_with_the_wrong_keyboard_layout_still_finds_the_product()
    {
        var (productId, _) = await app.CreateProductAsync();
        var (persian, latin) = RandomWord();
        await app.WithDbAsync(db => db.Products.Where(p => p.Id == productId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.Name, "کالای " + persian)));

        using var client = app.CreateClient();
        var page = await client.GetPageAsync("/search?q=" + Uri.EscapeDataString(latin));

        Assert.Contains(persian, page.Html, StringComparison.Ordinal);
        Assert.Contains("زبان کیبورد اشتباه بود", page.Text, StringComparison.Ordinal);
    }

    [SqlFact]
    public async Task Search_without_results_shows_in_stock_suggestions_instead_of_an_empty_page()
    {
        await app.CreateProductAsync(); // دست‌کم یک کالای موجود
        using var client = app.CreateClient();
        var page = await client.GetPageAsync("/search?q=" + Uri.EscapeDataString("zzqqxxnotfound" + Guid.NewGuid().ToString("N")[..6]));

        Assert.Contains("شاید این‌ها به کارتان بیاید", page.Text, StringComparison.Ordinal);
    }

    [SqlFact]
    public async Task Category_quiz_offers_budgets_and_returns_in_stock_picks()
    {
        for (var i = 0; i < 3; i++)
            await app.CreateProductAsync(price: 100_000 * (i + 1));

        using var client = app.CreateClient();
        var closed = await client.GetPageAsync("/category/e2e-category");
        Assert.NotNull(closed.One("#quiz a[href*='quiz=1']"));
        Assert.Null(closed.One("#quiz form")); // بدون درخواست، پرسش‌نامه هزینه‌ای ندارد

        var open = await client.GetPageAsync("/category/e2e-category?quiz=1");
        var form = open.One("#quiz form");
        Assert.NotNull(form);
        Assert.NotNull(open.One("#quiz select[name='budget']"));
        Assert.NotNull(open.One("#quiz select[name='priority']"));

        var picks = await client.GetPageAsync("/category/e2e-category?quiz=1&priority=cheap");
        Assert.NotEmpty(picks.All("#quiz a[href^='/product/']"));
    }

    [SqlFact]
    public async Task Product_page_shows_no_fake_urgency_and_renders_buyer_guide_safely()
    {
        var (_, slug) = await app.CreateProductAsync();
        using var client = app.CreateClient();
        var page = await client.GetPageAsync("/product/" + slug);

        // الگوهای فریبنده‌ای که هیچ‌وقت نباید ساخته شوند (دادهٔ ساختگی).
        foreach (var fake in new[] { "نفر در حال مشاهده", "نفر همین الان", "فقط امروز", "آخرین فرصت" })
            Assert.DoesNotContain(fake, page.Text, StringComparison.Ordinal);
        // کالای تازه نه سابقهٔ قیمت دارد نه ترب: ادعای «کمترین قیمت» یا «ارزان‌تر از ترب» نباید باشد.
        Assert.DoesNotContain("در ۳۰ روز گذشته", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("قیمت ۳۰ روز گذشته", page.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("ترب", page.Text, StringComparison.Ordinal);
    }
}
