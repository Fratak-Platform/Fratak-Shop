using System.Net;
using System.Text.Json;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Tests.E2E.Admin;

/// <summary>
/// «سئوی هوشمند» و «پیشنهاد هوشمند» درجا و جدول دانش سئو (مرحلهٔ شانزدهم) از دید مرورگر:
/// • مهمان و نقش بی‌ربط راه ندارند؛ هر نوع صفحه مجوز خودش را می‌خواهد (نویسنده سئوی محصول را نمی‌تواند).
/// • پاسخ‌ها JSON درست دارند (نسخه‌ها، عنوان‌ها، صفحهٔ اصلی) و هیچ چیز ذخیره نمی‌شود.
/// • متن بیش از حد بلند و درخواست‌های پشت‌سرهم (۴۲۹) رد می‌شوند؛ بدون توکن CSRF هم پذیرفته نمی‌شود.
/// • جدول دانش: افزودن ردیف دستی و دیدن آن در فهرست؛ دکمه‌های هوشمند فقط برای کسانی که مجوز دارند.
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Admin")]
public class SmartSeoE2ETests(ShopApp app)
{
    private const string Smart = "/admin/seo/smart";

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.Content.Headers.ContentType?.MediaType == "application/json", $"{(int)response.StatusCode}: {ShopClient.Excerpt(body)}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    [SqlFact]
    public async Task Guests_cannot_call_the_smart_handlers()
    {
        using var client = app.CreateClient();
        using var response = await client.PostRawFormAsync(Smart + "?handler=Text", ("Purpose", "Plain"), ("Text", "سلام"));
        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Redirect, HttpStatusCode.BadRequest });
        Assert.NotEqual("application/json", response.Content.Headers.ContentType?.MediaType);
    }

    [SqlTheory]
    [InlineData(ShopApp.AccountantPhone, Smart)]
    [InlineData(ShopApp.SalesManagerPhone, Smart)]
    [InlineData(ShopApp.WriterPhone, "/admin/seo/knowledge")]
    public async Task Staff_without_a_related_permission_are_denied(string phone, string url)
    {
        using var client = app.CreateClient();
        await client.LoginAsync(phone);
        using var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/admin/denied", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [SqlFact]
    public async Task Writer_cannot_draft_product_seo_but_can_rewrite_blog_text()
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.WriterPhone);

        using var denied = await client.PostFormAsync(Smart + "?handler=Draft", ("Kind", "Product"), ("Name", "هدفون سونی"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.False((await JsonAsync(denied)).GetProperty("ok").GetBoolean());

        using var text = await client.PostFormAsync(Smart + "?handler=Text",
            ("Purpose", "Body"), ("Kind", "BlogPost"), ("IsHtml", "true"), ("Keyword", "هدفون بی سیم"),
            ("Text", "<p>لازم به ذکر است که این هدفون خیلی خوبه و باطری اش قوی است.</p><script>alert(1)</script>"));
        Assert.Equal(HttpStatusCode.OK, text.StatusCode);
        var json = await JsonAsync(text);
        Assert.True(json.GetProperty("ok").GetBoolean(), json.ToString());
        var variants = json.GetProperty("variants").EnumerateArray().ToList();
        Assert.InRange(variants.Count, 1, 3);
        Assert.All(variants, v =>
        {
            Assert.True(v.GetProperty("isHtml").GetBoolean());
            Assert.DoesNotContain("<script", v.GetProperty("text").GetString());
            Assert.DoesNotContain("باطری", v.GetProperty("text").GetString());
        });
        Assert.True(json.GetProperty("before").GetProperty("score").GetInt32() >= 0);
    }

    [SqlFact]
    public async Task Seo_expert_gets_an_in_place_draft_for_an_unsaved_product_and_nothing_is_saved()
    {
        var suggestions = await app.WithDbAsync(db => db.SeoSuggestions.IgnoreQueryFilters().CountAsync());
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.SeoPhone);

        using var response = await client.PostFormAsync(Smart + "?handler=Draft",
            ("Kind", "Product"), ("Name", "هدفون بی‌سیم سونی مدل WH-1000XM5"), ("EnglishName", "Sony WH-1000XM5"),
            ("ShortText", "حذف نویز فعال و ۳۰ ساعت شارژدهی"), ("Research", "false"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonAsync(response);
        Assert.True(json.GetProperty("ok").GetBoolean(), json.ToString());
        Assert.NotEmpty(json.GetProperty("titles").EnumerateArray());
        Assert.NotEmpty(json.GetProperty("metas").EnumerateArray());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("keyword").GetString()));
        Assert.Equal(suggestions, await app.WithDbAsync(db => db.SeoSuggestions.IgnoreQueryFilters().CountAsync()));
    }

    [SqlFact]
    public async Task Owner_gets_a_draft_for_the_home_page()
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);
        using var response = await client.PostFormAsync(Smart + "?handler=Draft", ("Kind", "StaticPage"), ("Page", "Home"), ("Research", "false"));
        var json = await JsonAsync(response);
        Assert.True(json.GetProperty("ok").GetBoolean(), json.ToString());
        Assert.Equal("/", json.GetProperty("path").GetString());
        Assert.NotEmpty(json.GetProperty("titles").EnumerateArray());
    }

    [SqlFact]
    public async Task Invalid_or_oversized_requests_are_rejected_politely()
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.SeoPhone);

        using var huge = await client.PostFormAsync(Smart + "?handler=Text", ("Purpose", "Plain"), ("Text", new string('a', 200_001)));
        Assert.False((await JsonAsync(huge)).GetProperty("ok").GetBoolean());

        using var badKind = await client.PostFormAsync(Smart + "?handler=Draft", ("Kind", "None"), ("Name", "x"));
        Assert.False((await JsonAsync(badKind)).GetProperty("ok").GetBoolean());

        using var badFeedback = await client.PostFormAsync(Smart + "?handler=Feedback", ("Purpose", "Plain"), ("Variant", ""));
        Assert.False((await JsonAsync(badFeedback)).GetProperty("ok").GetBoolean());

        using var noToken = await client.PostRawFormAsync(Smart + "?handler=Text", ("Purpose", "Plain"), ("Text", "سلام دنیا"));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
    }

    [SqlFact]
    public async Task Rapid_feedback_is_rate_limited_per_staff_member()
    {
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.OwnerPhone);
        // یک توکن برای همه (گرفتن توکن تازه برای هر درخواست به محدودیت /api می‌خورد).
        var token = await client.AntiforgeryTokenAsync();
        var limited = false;
        for (var i = 0; i < 130 && !limited; i++)
        {
            using var response = await client.PostRawFormAsync(Smart + "?handler=Feedback",
                ("Purpose", "Plain"), ("Variant", "clean"), ("Accepted", "true"), ("__RequestVerificationToken", token));
            limited = response.StatusCode == HttpStatusCode.TooManyRequests;
            if (!limited)
                Assert.True((await JsonAsync(response)).GetProperty("ok").GetBoolean());
        }
        Assert.True(limited, "پس از ۶۰ درخواست در دقیقه باید ۴۲۹ برگردد.");
    }

    [SqlFact]
    public async Task Knowledge_page_adds_a_manual_row_and_lists_it()
    {
        // فقط حرف فارسی (رقم لاتین ممکن است هنگام یکسان‌سازی به رقم فارسی تبدیل شود).
        const string letters = "ابپتثجچحخدذرزژسشصضطظعغفقکگلمنوهی";
        var term = "غلطآزمون" + string.Concat(Guid.NewGuid().ToByteArray().Take(8).Select(b => letters[b % letters.Length]));
        using var client = app.CreateClient();
        await client.LoginAsync(ShopApp.SeoPhone);

        var page = await client.GetPageAsync("/admin/seo/knowledge");
        Assert.NotNull(page.One("form[method='post'] input[name='term']"));

        using var add = await client.PostFormAsync("/admin/seo/knowledge?handler=Add",
            ("kind", "Correction"), ("term", term), ("value", "درست"), ("topic", "املا"), ("weight", "1"));
        Assert.Equal(HttpStatusCode.Redirect, add.StatusCode);
        Assert.True(await app.WithDbAsync(db => db.SeoKnowledgeEntries.AnyAsync(e =>
            e.Term == term && e.Kind == SeoKnowledgeKind.Correction && e.Source == SeoKnowledgeSource.Manual && e.IsActive)));

        var list = await client.GetPageAsync("/admin/seo/knowledge?q=" + Uri.EscapeDataString(term));
        Assert.Contains(list.All("table td b"), b => b.TextContent.Trim() == term);

        var id = await app.WithDbAsync(db => db.SeoKnowledgeEntries.Where(e => e.Term == term).Select(e => e.Id).SingleAsync());
        using var toggle = await client.PostFormAsync("/admin/seo/knowledge?handler=Toggle", ("id", id.ToString()), ("active", "false"));
        Assert.Equal(HttpStatusCode.Redirect, toggle.StatusCode);
        Assert.False(await app.WithDbAsync(db => db.SeoKnowledgeEntries.Where(e => e.Id == id).Select(e => e.IsActive).SingleAsync()));

        // نوع غیرمجاز (مثلاً واژهٔ ایست) از فرم پذیرفته نمی‌شود.
        using var bad = await client.PostFormAsync("/admin/seo/knowledge?handler=Add", ("kind", "StopWord"), ("term", term + "x"));
        Assert.Equal(HttpStatusCode.Redirect, bad.StatusCode);
        Assert.False(await app.WithDbAsync(db => db.SeoKnowledgeEntries.AnyAsync(e => e.Term == term + "x")));
    }

    [SqlFact]
    public async Task Smart_buttons_are_offered_only_to_staff_with_permission()
    {
        using var seo = app.CreateClient();
        await seo.LoginAsync(ShopApp.SeoPhone);
        var editor = await seo.GetPageAsync("/admin/blog/edit");
        Assert.NotNull(editor.One("meta[name='fratak-smart']"));
        Assert.NotNull(editor.One("[data-smart-context]"));
        Assert.Contains(editor.All("script[src]"), s => (s.GetAttribute("src") ?? "").Contains("smart.js", StringComparison.Ordinal));

        var tool = await seo.GetPageAsync(Smart);
        Assert.NotNull(tool.One("[data-smart-tool]"));

        using var sales = app.CreateClient();
        await sales.LoginAsync(ShopApp.SalesManagerPhone);
        var orders = await sales.GetPageAsync("/admin/orders");
        Assert.Null(orders.One("meta[name='fratak-smart']"));
    }
}
