using System.Net;
using System.Text;
using System.Text.Json;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Tests.E2E.Storefront;

/// <summary>
/// تست سرتاسری دور ۲۴ برای «دستیار هوشمند» روی برنامهٔ واقعی (TestServer + SQL Server).
/// <list type="bullet">
/// <item>پرسش‌های پیشنهادی هر صفحه (/api/assistant/suggest): نام دستیار، سلام و دکمه‌های مخصوص صفحهٔ کالا؛ مسیر بیرونی نادیده گرفته می‌شود.</item>
/// <item>نشانه‌های راهنمای زنده (data-assistant) در صفحه‌های کالا، سبد و ورود هست؛ قدم‌های ورود فقط مسیر داخلی‌اند و رمز را پر نمی‌کنند.</item>
/// <item>سلیقهٔ مهمان (style) پاک‌سازی می‌شود: اسم مخرب ← null، لحن نامعتبر ← خودکار، طرز نوشتن ← سقف.</item>
/// <item>گفت‌وگو با کارشناس: باز شدن از چت، رفتن پیام به کارشناس، جواب مدیر از پنل، دریافت در مرورگر و بستن با توکن ضدجعل.</item>
/// <item>پنل «گفت‌وگو با کارشناس» برای مدیر باز و برای مشتری بسته است.</item>
/// </list>
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Assistant")]
public class AssistantRound24E2ETests(ShopApp app)
{
    private static string NewSession() => "e2e24" + Guid.NewGuid().ToString("N")[..20];

    private static async Task<HttpResponseMessage> PostJsonAsync(ShopClient client, string url, object body, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        if (token is not null)
            request.Headers.Add("RequestVerificationToken", token);
        return await client.Http.SendAsync(request);
    }

    private static async Task<JsonElement> AskAsync(ShopClient client, string session, string text, object? extra = null)
    {
        var token = await client.AntiforgeryTokenAsync();
        var body = new Dictionary<string, object?> { ["text"] = text, ["session"] = session, ["last"] = Array.Empty<Guid>() };
        if (extra is not null)
            foreach (var property in JsonSerializer.SerializeToElement(extra).EnumerateObject())
                body[property.Name] = property.Value;
        using var response = await PostJsonAsync(client, "/api/assistant/message", body, token);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {content}");
        return JsonDocument.Parse(content).RootElement.Clone();
    }

    private static async Task<JsonElement> GetJsonAsync(ShopClient client, string url)
    {
        using var response = await client.GetAsync(url);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {content}");
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? "");
        return JsonDocument.Parse(content).RootElement.Clone();
    }

    // ── پرسش‌های پیشنهادی ───────────────────────────────────────

    [SqlFact]
    public async Task Suggest_returns_identity_and_page_specific_questions()
    {
        var (productId, slug) = await app.CreateProductAsync(stock: 5);
        using var client = app.CreateClient();

        var home = await GetJsonAsync(client, "/api/assistant/suggest?page=%2F");
        Assert.False(string.IsNullOrWhiteSpace(home.GetProperty("name").GetString()));
        Assert.StartsWith("سلام", home.GetProperty("intro").GetString(), StringComparison.Ordinal);
        Assert.True(home.GetProperty("chips").GetArrayLength() > 0);

        var product = await GetJsonAsync(client, $"/api/assistant/suggest?page=%2Fproduct%2F{slug}&product={productId}");
        var says = product.GetProperty("chips").EnumerateArray().Select(c => c.GetProperty("say").GetString()).ToList();
        Assert.Contains("در مورد این کالا توضیح بده", says);
        Assert.Contains("مشخصات این چیه؟", says);
        Assert.Contains("اینو به سبد اضافه کن", says);
        Assert.Contains("کالای آزمون", product.GetProperty("intro").GetString());

        // مسیر بیرونی ← مثل صفحهٔ اصلی رفتار می‌شود؛ پارامتر خراب خطای سرور نمی‌دهد.
        var evil = await GetJsonAsync(client, "/api/assistant/suggest?page=https%3A%2F%2Fevil.example%2Fproduct%2Fx&callMe=%3Cscript%3E&tone=99&register=50");
        Assert.Equal(home.GetProperty("chips").GetArrayLength(), evil.GetProperty("chips").GetArrayLength());
        Assert.DoesNotContain("<script>", evil.GetProperty("intro").GetString());
    }

    // ── راهنمای زنده ─────────────────────────────────────────────

    [SqlFact]
    public async Task Guide_targets_exist_on_pages_and_login_steps_are_safe()
    {
        var (productId, slug) = await app.CreateProductAsync(stock: 5);
        using var client = app.CreateClient();

        var productPage = await client.GetPageAsync($"/product/{slug}");
        Assert.NotNull(productPage.One("[data-assistant='add-to-cart']"));

        await client.AddToCartAsync(productId);
        var cartPage = await client.GetPageAsync("/cart");
        Assert.NotNull(cartPage.One("[data-assistant='cart-summary']"));
        Assert.NotNull(cartPage.One("[data-assistant='checkout-button']"));

        var session = NewSession();
        var login = await AskAsync(client, session, "با رمز عبور وارد بشم", new { page = "/cart" });
        Assert.Equal("Login", login.GetProperty("intent").GetString());
        var steps = login.GetProperty("steps").EnumerateArray().ToList();
        Assert.NotEmpty(steps);
        var go = steps[0].GetProperty("url").GetString();
        Assert.StartsWith("/login?mode=password&returnUrl=%2Fcart", go, StringComparison.Ordinal);
        Assert.Contains(steps, s => s.GetProperty("kind").GetString() == "focus" && s.GetProperty("target").GetString() == "login-password");
        Assert.DoesNotContain(steps, s => s.GetProperty("kind").GetString() == "fill");
        foreach (var step in steps)
            if (step.GetProperty("url").GetString() is { } url)
                Assert.StartsWith("/", url, StringComparison.Ordinal);

        var loginPage = await client.GetPageAsync(go!);
        Assert.NotNull(loginPage.One("[data-assistant='login-phone']"));
        Assert.NotNull(loginPage.One("[data-assistant='login-password']"));

        // صفحهٔ بیرونی به‌عنوان بازگشت پذیرفته نمی‌شود.
        var external = await AskAsync(client, session, "میخوام وارد بشم", new { page = "https://evil.example" });
        Assert.StartsWith("/login?mode=", external.GetProperty("steps")[0].GetProperty("url").GetString(), StringComparison.Ordinal);
        Assert.DoesNotContain("evil", external.GetProperty("steps")[0].GetProperty("url").GetString());
    }

    [SqlFact]
    public async Task Guest_style_is_sanitized_and_name_preference_round_trips()
    {
        using var client = app.CreateClient();
        var session = NewSession();

        var hostile = await AskAsync(client, session, "سلام", new { style = new { callMe = "<img src=x>", tone = 99, register = 50 } });
        var style = hostile.GetProperty("style");
        Assert.Equal(JsonValueKind.Null, style.GetProperty("callMe").ValueKind);
        Assert.Equal(0, style.GetProperty("tone").GetInt32());
        Assert.True(style.GetProperty("register").GetInt32() <= 3);
        Assert.DoesNotContain("<img", hostile.GetProperty("text").GetString());

        var call = await AskAsync(client, session, "منو نگار صدا کن");
        Assert.Equal("Preference", call.GetProperty("intent").GetString());
        Assert.Equal("نگار", call.GetProperty("style").GetProperty("callMe").GetString());

        var hello = await AskAsync(client, session, "سلام", new { style = new { callMe = "نگار", tone = 0, register = 0 } });
        Assert.Contains("نگار", hello.GetProperty("text").GetString());
    }

    // ── گفت‌وگو با کارشناس ──────────────────────────────────────

    [SqlFact]
    public async Task Handoff_round_trip_between_customer_chat_and_admin_panel()
    {
        using var customer = app.CreateClient();
        var session = NewSession();
        Guid handoffId = default;
        try
        {
            var open = await AskAsync(customer, session, "میخوام با کارشناس صحبت کنم", new { page = "/cart" });
            Assert.Equal("Human", open.GetProperty("intent").GetString());
            Assert.True(open.GetProperty("handoff").GetBoolean());

            var forwarded = await AskAsync(customer, session, "سلام، سفارشم دیر شده");
            Assert.Equal("Handoff", forwarded.GetProperty("intent").GetString());
            Assert.True(forwarded.GetProperty("handoff").GetBoolean());

            handoffId = await app.WithDbAsync(db => db.AssistantHandoffs.AsNoTracking().Where(h => h.SessionKey == session).Select(h => h.Id).SingleAsync());

            var waiting = await GetJsonAsync(customer, $"/api/assistant/handoff?session={session}&after=0");
            Assert.True(waiting.GetProperty("open").GetBoolean());
            Assert.Equal("Waiting", waiting.GetProperty("status").GetString());
            Assert.Equal(0, waiting.GetProperty("messages").GetArrayLength());

            // مدیر از پنل جواب می‌دهد.
            using var owner = app.CreateClient();
            await owner.LoginAsync(ShopApp.OwnerPhone);
            var panel = await owner.GetPageAsync($"/admin/smart/assistant/handoffs?id={handoffId}");
            Assert.Contains("سفارشم دیر شده", panel.Text);
            using var reply = await owner.PostFormAsync($"/admin/smart/assistant/handoffs?id={handoffId}&handler=Reply", ("text", "سلام، پیگیری می‌کنم."));
            Assert.Equal(HttpStatusCode.Redirect, reply.StatusCode);

            var answered = await GetJsonAsync(customer, $"/api/assistant/handoff?session={session}&after=0");
            Assert.Equal("Active", answered.GetProperty("status").GetString());
            var message = Assert.Single(answered.GetProperty("messages").EnumerateArray());
            Assert.True(message.GetProperty("fromStaff").GetBoolean());
            Assert.Equal("سلام، پیگیری می‌کنم.", message.GetProperty("text").GetString());
            var at = message.GetProperty("at").GetInt64();
            Assert.Equal(0, (await GetJsonAsync(customer, $"/api/assistant/handoff?session={session}&after={at}")).GetProperty("messages").GetArrayLength());

            // ورودی نامعتبر و بستن بدون توکن رد می‌شود.
            using var badSession = await customer.GetAsync("/api/assistant/handoff?session=%3Cx%3E");
            Assert.Equal(HttpStatusCode.BadRequest, badSession.StatusCode);
            using var noToken = await PostJsonAsync(customer, "/api/assistant/handoff/close", new { session }, null);
            Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);

            var token = await customer.AntiforgeryTokenAsync();
            using var close = await PostJsonAsync(customer, "/api/assistant/handoff/close", new { session }, token);
            Assert.Equal(HttpStatusCode.OK, close.StatusCode);
            using (var closed = JsonDocument.Parse(await close.Content.ReadAsStringAsync()))
                Assert.True(closed.RootElement.GetProperty("closed").GetBoolean());
            Assert.False((await GetJsonAsync(customer, $"/api/assistant/handoff?session={session}&after=0")).GetProperty("open").GetBoolean());

            // بعد از بستن، دستیار دوباره خودش جواب می‌دهد.
            var back = await AskAsync(customer, session, "سلام");
            Assert.Equal("Greeting", back.GetProperty("intent").GetString());
            Assert.False(back.GetProperty("handoff").GetBoolean());
        }
        finally
        {
            await app.WithDbAsync(async db =>
            {
                var ids = await db.AssistantHandoffs.IgnoreQueryFilters().Where(h => h.SessionKey == session).Select(h => h.Id).ToListAsync();
                foreach (var id in ids)
                {
                    var marker = id.ToString();
                    await db.StaffNotifications.IgnoreQueryFilters().Where(n => n.Url != null && n.Url.Contains(marker)).ExecuteDeleteAsync();
                }
                await db.AssistantHandoffMessages.IgnoreQueryFilters().Where(m => ids.Contains(m.HandoffId)).ExecuteDeleteAsync();
                await db.AssistantHandoffs.IgnoreQueryFilters().Where(h => ids.Contains(h.Id)).ExecuteDeleteAsync();
                await db.AssistantMessages.IgnoreQueryFilters().Where(m => m.SessionKey == session).ExecuteDeleteAsync();
            });
        }
    }

    [SqlFact]
    public async Task Handoffs_admin_page_opens_for_owner_and_is_closed_to_customers()
    {
        using var owner = app.CreateClient();
        await owner.LoginAsync(ShopApp.OwnerPhone);
        var page = await owner.GetPageAsync("/admin/smart/assistant/handoffs");
        Assert.Contains("گفت‌وگو با کارشناس", page.Text);
        var all = await owner.GetPageAsync("/admin/smart/assistant/handoffs?all=true");
        Assert.Contains("گفت‌وگو با کارشناس", all.Text);
        using var missing = await owner.GetAsync($"/admin/smart/assistant/handoffs?id={Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        using var customer = app.CreateClient();
        await customer.LoginAsync(ShopApp.DemoCustomerPhone);
        using var denied = await customer.GetAsync("/admin/smart/assistant/handoffs");
        Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);
    }
}
