using System.Net;
using Fratak_Shop.Data.Entities.Assistant;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Tests.E2E.Admin;

/// <summary>
/// تست سرتاسری دور ۲۸ – صفحه‌های «دانش دستیار» و «دستیار مدیریت»:
/// <list type="bullet">
/// <item>دانش دستیار: مدیر می‌بیند، ثبت/خاموش/حذف می‌کند؛ لینک بیرونی رد می‌شود؛ بدون توکن ضدجعل ← 400؛ مشتری ← 404؛ کارمند بی‌دسترسی ← نه 200.</item>
/// <item>دستیار مدیریت: «فروش امروز» و «راهنما» جواب می‌گیرند؛ «منتشر کن …» فقط پیش‌نمایش با دکمهٔ تأیید است و خودش چیزی را منتشر نمی‌کند؛
/// تأیید ← کالا فعال می‌شود؛ نویسنده (بی‌دسترسی) راه ندارد.</item>
/// </list>
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Round28")]
public class AssistantAdminRound28E2ETests(ShopApp app)
{
    [SqlFact]
    public async Task Knowledge_page_saves_toggles_deletes_and_is_admin_only()
    {
        var term = "آزمون دانش " + Guid.NewGuid().ToString("N")[..6];
        using var owner = app.CreateClient();
        await owner.LoginAsync(ShopApp.OwnerPhone);
        try
        {
            var page = await owner.GetPageAsync("/admin/smart/assistant/knowledge");
            Assert.NotNull(page.One("form#form"));

            using (var bad = await owner.PostFormAsync("/admin/smart/assistant/knowledge?handler=Save",
                       ("kind", "Learned"), ("term", term), ("answer", "جواب"), ("url", "https://evil.example")))
                Assert.Equal(HttpStatusCode.Redirect, bad.StatusCode);
            Assert.False(await app.WithDbAsync(db => db.AssistantFacts.AnyAsync(f => f.Term == term)));

            using (var saved = await owner.PostFormAsync("/admin/smart/assistant/knowledge?handler=Save",
                       ("kind", "Learned"), ("term", term), ("aliases", "شکل یک، شکل دو"), ("answer", "جواب آزمون دانش."), ("url", "/faq")))
            {
                Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
                Assert.Equal("/admin/smart/assistant/knowledge", saved.Headers.Location?.ToString());
            }
            var id = await app.WithDbAsync(db => db.AssistantFacts.AsNoTracking().Where(f => f.Term == term).Select(f => f.Id).SingleAsync());
            var listed = await owner.GetPageAsync("/admin/smart/assistant/knowledge?q=" + Uri.EscapeDataString(term));
            Assert.Contains(term, listed.Text, StringComparison.Ordinal);
            var editing = await owner.GetPageAsync($"/admin/smart/assistant/knowledge?id={id}");
            Assert.Contains("جواب آزمون دانش", editing.Text, StringComparison.Ordinal);
            using (var missing = await owner.GetAsync($"/admin/smart/assistant/knowledge?id={Guid.NewGuid()}"))
                Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

            using (var csrf = await owner.PostRawFormAsync("/admin/smart/assistant/knowledge?handler=Toggle", ("id", id.ToString())))
                Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
            using (var toggled = await owner.PostFormAsync("/admin/smart/assistant/knowledge?handler=Toggle", ("id", id.ToString())))
                Assert.Equal(HttpStatusCode.Redirect, toggled.StatusCode);
            Assert.False(await app.WithDbAsync(db => db.AssistantFacts.Where(f => f.Id == id).Select(f => f.IsActive).SingleAsync()));

            using (var deleted = await owner.PostFormAsync("/admin/smart/assistant/knowledge?handler=Delete", ("id", id.ToString())))
                Assert.Equal(HttpStatusCode.Redirect, deleted.StatusCode);
            Assert.False(await app.WithDbAsync(db => db.AssistantFacts.AnyAsync(f => f.Id == id)));

            using var customer = app.CreateClient();
            await customer.LoginAsync(ShopApp.DemoCustomerPhone);
            using (var denied = await customer.GetAsync("/admin/smart/assistant/knowledge"))
                Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            using var accountant = app.CreateClient();
            await accountant.LoginAsync(ShopApp.AccountantPhone);
            using (var staff = await accountant.GetAsync("/admin/smart/assistant/knowledge"))
                Assert.NotEqual(HttpStatusCode.OK, staff.StatusCode);
        }
        finally
        {
            await app.WithDbAsync(db => db.AssistantFacts.IgnoreQueryFilters().Where(f => f.Term == term).ExecuteDeleteAsync());
        }
    }

    [SqlFact]
    public async Task Ops_assistant_answers_and_publish_needs_explicit_confirmation()
    {
        var (productId, _) = await app.CreateProductAsync(stock: 4);
        var name = await app.WithDbAsync(async db =>
        {
            await db.Products.Where(p => p.Id == productId).ExecuteUpdateAsync(s => s.SetProperty(p => p.IsActive, false));
            return await db.Products.Where(p => p.Id == productId).Select(p => p.Name).SingleAsync();
        });
        using var owner = app.CreateClient();
        await owner.LoginAsync(ShopApp.OwnerPhone);

        var page = await owner.GetPageAsync("/admin/smart/assistant/ops");
        Assert.NotNull(page.One("input[name='q']"));

        using (var sales = await owner.PostFormAsync("/admin/smart/assistant/ops?handler=Ask", ("q", "فروش امروز")))
        {
            Assert.Equal(HttpStatusCode.OK, sales.StatusCode);
            var html = await sales.Content.ReadAsStringAsync();
            Assert.Contains("data-ops-answer", html, StringComparison.Ordinal);
            Assert.Contains("تومان", html, StringComparison.Ordinal);
        }
        using (var help = await owner.PostFormAsync("/admin/smart/assistant/ops?handler=Ask", ("q", "راهنما")))
            Assert.Contains("کارهای امروز", await help.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // «منتشر کن …» ← فقط پیش‌نمایش با دکمهٔ تأیید؛ کالا هنوز منتشر نشده.
        using (var preview = await owner.PostFormAsync("/admin/smart/assistant/ops?handler=Ask", ("q", "منتشر کن " + name.Split(' ')[^1])))
        {
            var html = await preview.Content.ReadAsStringAsync();
            Assert.Contains("handler=Confirm", html, StringComparison.Ordinal);
            Assert.Contains(productId.ToString(), html, StringComparison.OrdinalIgnoreCase);
        }
        Assert.False(await app.WithDbAsync(db => db.Products.Where(p => p.Id == productId).Select(p => p.IsActive).SingleAsync()));

        using (var csrf = await owner.PostRawFormAsync($"/admin/smart/assistant/ops?handler=Confirm&id={productId}&activate=true"))
            Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        using (var confirmed = await owner.PostFormAsync($"/admin/smart/assistant/ops?handler=Confirm&id={productId}&activate=true"))
            Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode);
        Assert.True(await app.WithDbAsync(db => db.Products.Where(p => p.Id == productId).Select(p => p.IsActive).SingleAsync()));

        // نویسنده (بدون دسترسی سفارش/کالا/گزارش) ← راه ندارد؛ مشتری ← 404.
        using var writer = app.CreateClient();
        await writer.LoginAsync(ShopApp.WriterPhone);
        using (var denied = await writer.GetAsync("/admin/smart/assistant/ops"))
            Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
        using var customer = app.CreateClient();
        await customer.LoginAsync(ShopApp.DemoCustomerPhone);
        using (var hidden = await customer.GetAsync("/admin/smart/assistant/ops"))
            Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }
}
