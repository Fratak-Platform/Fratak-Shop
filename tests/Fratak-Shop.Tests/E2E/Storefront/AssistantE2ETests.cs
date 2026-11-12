using System.Net;
using System.Text;
using System.Text.Json;
using Fratak_Shop.Data.Entities.Assistant;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Tests.E2E.Storefront;

/// <summary>
/// تست سرتاسری دور ۲۳ برای «دستیار فروشگاه» روی برنامهٔ واقعی (TestServer + SQL Server).
/// <list type="bullet">
/// <item>ویجت روی همهٔ صفحه‌ها بارگذاری می‌شود (فایل JS/CSS، بدون اسکریپت درون‌خطی).</item>
/// <item>بدون توکن ضدجعل ← 400؛ بدنهٔ بزرگ ← 413؛ پیام خالی ← 400.</item>
/// <item>سلام، پرسش بی‌ربط (رد مؤدبانه)، قیمت کالای همین صفحه، افزودن به سبد و دیدن سبد با همان کوکی.</item>
/// <item>کالای ناموجود در فروشگاه ← «متأسفانه موجود نداریم، درخواست شما ثبت شد» و ثبت در گزارش.</item>
/// <item>پیگیری سفارش برای مهمان ← دکمهٔ ورود (هیچ سفارشی از دیگران نشان داده نمی‌شود).</item>
/// <item>توکن جست‌وجوی اینترنت نامعتبر ← 400؛ پنل /admin/smart/assistant برای مدیر 200.</item>
/// </list>
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Assistant")]
public class AssistantE2ETests(ShopApp app)
{
    private static readonly string Session = "e2e" + Guid.NewGuid().ToString("N")[..20];

    private static async Task<HttpResponseMessage> SendAsync(ShopClient client, object body, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/assistant/message")
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        if (token is not null)
            request.Headers.Add("RequestVerificationToken", token);
        return await client.Http.SendAsync(request);
    }

    private static async Task<JsonElement> AskAsync(ShopClient client, string text, Guid? productId = null, Guid? action = null)
    {
        var token = await client.AntiforgeryTokenAsync();
        using var response = await SendAsync(client, new { text, session = Session, productId, action, last = Array.Empty<Guid>() }, token);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {body}");
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    [SqlFact]
    public async Task Widget_assets_are_loaded_on_storefront_pages()
    {
        using var client = app.CreateClient();
        var page = await client.GetPageAsync("/");
        Assert.NotNull(page.One("script[src*='/assets/js/assistant.js']"));
        Assert.NotNull(page.One("link[href*='/assets/css/assistant.css']"));
        using var js = await client.GetAsync("/assets/js/assistant.js");
        Assert.Equal(HttpStatusCode.OK, js.StatusCode);
    }

    [SqlFact]
    public async Task Requests_without_antiforgery_or_with_bad_bodies_are_rejected()
    {
        using var client = app.CreateClient();
        using var noToken = await SendAsync(client, new { text = "سلام", session = Session }, null);
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);

        using var forged = await SendAsync(client, new { text = "سلام", session = Session }, "forged");
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);

        var token = await client.AntiforgeryTokenAsync();
        using var empty = await SendAsync(client, new { text = "   ", session = Session }, token);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        using var huge = await SendAsync(client, new { text = new string('x', 6000), session = Session }, token);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, huge.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/assistant/message")
        {
            Content = new StringContent("{not json", Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("RequestVerificationToken", token);
        using var broken = await client.Http.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, broken.StatusCode);
    }

    [SqlFact]
    public async Task Greeting_and_off_topic_are_answered_politely()
    {
        using var client = app.CreateClient();
        var hello = await AskAsync(client, "سلام");
        Assert.Equal("Greeting", hello.GetProperty("intent").GetString());
        Assert.StartsWith("سلام", hello.GetProperty("text").GetString(), StringComparison.Ordinal);
        Assert.True(hello.GetProperty("actions").GetArrayLength() > 0);

        var weather = await AskAsync(client, "هوا فردا چطوره");
        Assert.Equal("OffTopic", weather.GetProperty("intent").GetString());
        Assert.Contains("ببخشید", weather.GetProperty("text").GetString());
    }

    [SqlFact]
    public async Task Price_question_on_product_page_uses_that_product_and_cart_flow_works()
    {
        var product = await app.WithDbAsync(db => db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.StockQuantity > 2 && p.Price > 0)
            .OrderByDescending(p => p.StockQuantity)
            .Select(p => new { p.Id, p.Name })
            .FirstAsync());

        using var client = app.CreateClient();
        var price = await AskAsync(client, "قیمت این چنده", product.Id);
        Assert.Equal("ProductQuestion", price.GetProperty("intent").GetString());
        Assert.Contains("قیمت", price.GetProperty("text").GetString());
        Assert.Equal(product.Id, price.GetProperty("cards")[0].GetProperty("id").GetGuid());

        var add = await AskAsync(client, $"افزودن «{product.Name}» به سبد", action: product.Id);
        Assert.Equal("CartAdd", add.GetProperty("intent").GetString());
        Assert.True(add.GetProperty("cartChanged").GetBoolean(), add.GetProperty("text").GetString());

        var cart = await AskAsync(client, "سبد خرید من");
        Assert.Equal("CartShow", cart.GetProperty("intent").GetString());
        Assert.Contains(product.Name, cart.GetProperty("text").GetString());
        foreach (var action in cart.GetProperty("actions").EnumerateArray())
            if (action.GetProperty("url").GetString() is { } url)
                Assert.StartsWith("/", url, StringComparison.Ordinal);

        var checkout = await AskAsync(client, "تسویه حساب");
        Assert.Equal("Checkout", checkout.GetProperty("intent").GetString());
        Assert.Contains("وارد حساب", checkout.GetProperty("text").GetString());
    }

    [SqlFact]
    public async Task Unknown_product_is_logged_as_demand_with_masked_text()
    {
        using var client = app.CreateClient();
        var word = "زژقلمب" + Guid.NewGuid().ToString("N")[..4];
        var reply = await AskAsync(client, $"{word} دارید؟ شمارم 09121112233");
        Assert.Equal("Demand", reply.GetProperty("intent").GetString());
        Assert.StartsWith("متأسفانه موجود نداریم، درخواست شما ثبت شد", reply.GetProperty("text").GetString(), StringComparison.Ordinal);

        var row = await app.WithDbAsync(db => db.AssistantMessages.AsNoTracking()
            .Where(m => m.SessionKey == Session && m.Intent == AssistantIntent.Demand)
            .OrderByDescending(m => m.CreatedAt)
            .FirstAsync());
        Assert.DoesNotContain("09121112233", row.Text);
        Assert.Contains("***", row.Text);
        Assert.Contains(word, row.Query);
    }

    [SqlFact]
    public async Task Guests_cannot_see_orders_through_the_assistant()
    {
        using var client = app.CreateClient();
        var reply = await AskAsync(client, "سفارشم کجاست");
        Assert.Equal("Track", reply.GetProperty("intent").GetString());
        Assert.DoesNotContain("FR-", reply.GetProperty("text").GetString());
        Assert.Contains(reply.GetProperty("actions").EnumerateArray(), a => a.GetProperty("url").GetString()?.StartsWith("/login", StringComparison.Ordinal) == true);
    }

    [SqlFact]
    public async Task Lookup_rejects_bad_tokens_and_answers_unknown_ones()
    {
        using var client = app.CreateClient();
        using var bad = await client.GetAsync("/api/assistant/lookup/not-a-token");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        using var unknown = await client.GetAsync("/api/assistant/lookup/" + Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.OK, unknown.StatusCode);
        using var json = JsonDocument.Parse(await unknown.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("done").GetBoolean());
    }

    [SqlFact]
    public async Task Admin_insights_page_opens_for_owner_and_is_closed_to_customers()
    {
        using var owner = app.CreateClient();
        await owner.LoginAsync(ShopApp.OwnerPhone);
        var page = await owner.GetPageAsync("/admin/smart/assistant");
        Assert.Contains("دستیار فروشگاه", page.Text);

        using var customer = app.CreateClient();
        await customer.LoginAsync(ShopApp.DemoCustomerPhone);
        using var denied = await customer.GetAsync("/admin/smart/assistant");
        Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
    }
}
