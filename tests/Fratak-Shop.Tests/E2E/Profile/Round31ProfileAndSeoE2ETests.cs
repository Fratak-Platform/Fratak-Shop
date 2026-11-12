using System.Net;
using Fratak_Shop.Data.Entities.SeoEngine;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Tests.E2E.Profile;

/// <summary>
/// دور ۳۱ از فرم‌های واقعی:
/// <list type="bullet">
/// <item>#۶ پروفایل ← «رمز فعلی را نمی‌دانم»: کد پیامکی به شمارهٔ خود حساب ← رمز تازه ← همین دستگاه وارد می‌ماند، دستگاه دیگر خارج می‌شود،
/// رمز قدیمی دیگر کار نمی‌کند. حالت‌های بد: کد اشتباه، کد «ورود» به‌جای کد «بازیابی»، رمز ضعیف، تکرار نادرست، بدون ورود.</item>
/// <item>#۱۳ «حذف همهٔ پیشنهادهای منتظر» سئو: فقط منتظرها؛ بدون مجوز ← رد؛ مشتری ← ۴۰۴؛ بدون توکن CSRF ← ۴۰۰.</item>
/// <item>#۴ منوی کاربر نام کامل را بدون «جان» نشان می‌دهد.</item>
/// </list>
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Round31")]
public class Round31ProfileAndSeoE2ETests(ShopApp app)
{
    private const string Page = "/profile/password";
    private const string NewPassword = "Fresh#Pass2031";

    private static (string, string)[] ForgotForm(string code, string password, string confirm) =>
        code.Select(d => ("Digits", d.ToString()))
            .Append(("forgot.NewPassword", password))
            .Append(("forgot.ConfirmPassword", confirm))
            .ToArray();

    // ───────────── #۶ «رمز فعلی را نمی‌دانم» ─────────────

    [SqlFact]
    public async Task Forgot_from_profile_resets_keeps_this_device_and_signs_out_others()
    {
        var customer = await app.CreateCustomerAsync();
        using var laptop = app.CreateClient();
        using var phone = app.CreateClient();
        await laptop.LoginAsync(customer.Phone, customer.Password);
        await phone.LoginAsync(customer.Phone, customer.Password);
        await phone.GetPageAsync("/profile");

        // لینک «رمز فعلی را نمی‌دانم» روی صفحهٔ عادی هست و حالت forgot فرم کد را نشان می‌دهد (GET چیزی نمی‌فرستد).
        var normal = await laptop.GetPageAsync(Page);
        Assert.Contains("mode=forgot", normal.Html, StringComparison.OrdinalIgnoreCase);
        var forgotPage = await laptop.GetPageAsync(Page + "?mode=forgot");
        Assert.NotEmpty(forgotPage.All("input[name='forgot.NewPassword']"));

        using var send = await laptop.PostFormAsync(Page + "?handler=SendCode");
        Assert.Equal(HttpStatusCode.Redirect, send.StatusCode);
        Assert.Contains("mode=forgot", send.Headers.Location?.OriginalString ?? "", StringComparison.OrdinalIgnoreCase);
        var code = app.LastOtp(customer.Phone);
        Assert.NotNull(code);

        using var reset = await laptop.PostFormAsync(Page + "?handler=Forgot", ForgotForm(code!, NewPassword, NewPassword));
        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
        Assert.Equal("/profile", reset.Headers.Location?.OriginalString.ToLowerInvariant());

        // همین دستگاه وارد می‌ماند؛ دستگاه دیگر خارج شده است.
        await laptop.GetPageAsync("/profile");
        await phone.GetPageAsync("/profile", HttpStatusCode.Redirect);

        // رمز تازه کار می‌کند، رمز قدیمی نه.
        using var fresh = app.CreateClient();
        await fresh.LoginAsync(customer.Phone, NewPassword);
        using var old = app.CreateClient();
        using var oldLogin = await old.PostFormAsync("/login", ("Input.PhoneNumber", customer.Phone), ("Input.Password", customer.Password));
        Assert.DoesNotContain(ShopClient.SetCookies(oldLogin),
            c => c.StartsWith("fratak_at=", StringComparison.Ordinal) && !c.StartsWith("fratak_at=;", StringComparison.Ordinal));

        // همان کد دوباره پذیرفته نمی‌شود.
        using var replay = await fresh.PostFormAsync(Page + "?handler=Forgot", ForgotForm(code!, "Other#Pass2031", "Other#Pass2031"));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var still = app.CreateClient();
        await still.LoginAsync(customer.Phone, NewPassword);
    }

    [SqlFact]
    public async Task Forgot_from_profile_rejects_wrong_code_login_code_weak_and_mismatched_passwords()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(customer.Phone, customer.Password);

        // کد «ورود» (هدف دیگر) برای بازیابی پذیرفته نمی‌شود.
        using (await app.CreateClient().PostFormAsync("/login?handler=SendCode", ("Otp.PhoneNumber", customer.Phone)))
        {
        }
        var loginCode = app.LastOtp(customer.Phone);
        Assert.NotNull(loginCode);
        using var withLoginCode = await client.PostFormAsync(Page + "?handler=Forgot", ForgotForm(loginCode!, NewPassword, NewPassword));
        Assert.Equal(HttpStatusCode.OK, withLoginCode.StatusCode);
        Assert.Contains("کد وارد شده صحیح نیست", await withLoginCode.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using (await client.PostFormAsync(Page + "?handler=SendCode"))
        {
        }
        var code = app.LastOtp(customer.Phone)!;
        var wrong = code == "11111" ? "22222" : "11111";

        using var bad = await client.PostFormAsync(Page + "?handler=Forgot", ForgotForm(wrong, NewPassword, NewPassword));
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
        Assert.Contains("کد وارد شده صحیح نیست", await bad.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var mismatch = await client.PostFormAsync(Page + "?handler=Forgot", ForgotForm(code, NewPassword, NewPassword + "x"));
        Assert.Equal(HttpStatusCode.OK, mismatch.StatusCode);

        using var weak = await client.PostFormAsync(Page + "?handler=Forgot", ForgotForm(code, "123456", "123456"));
        Assert.Equal(HttpStatusCode.OK, weak.StatusCode);

        // هیچ‌کدام رمز را عوض نکرد.
        using var login = app.CreateClient();
        await login.LoginAsync(customer.Phone, customer.Password);
    }

    [SqlFact]
    public async Task Forgot_handlers_need_sign_in_and_antiforgery()
    {
        using var anonymous = app.CreateClient();
        using var send = await anonymous.PostFormAsync(Page + "?handler=SendCode");
        // کاربر واردنشده به فرم نمی‌رسد (به صفحهٔ ورود هدایت یا رد می‌شود).
        Assert.NotEqual(HttpStatusCode.OK, send.StatusCode);
        Assert.DoesNotContain("mode=forgot", send.Headers.Location?.OriginalString ?? "", StringComparison.OrdinalIgnoreCase);

        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(customer.Phone, customer.Password);
        using var forged = await client.PostRawFormAsync(Page + "?handler=SendCode");
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);
    }

    // ───────────── #۴ نام کامل در منوی کاربر ─────────────

    [SqlFact]
    public async Task Header_menu_shows_the_full_name_without_jan()
    {
        var customer = await app.CreateCustomerAsync();
        await app.WithDbAsync(async db =>
        {
            await db.Users.Where(u => u.Id == customer.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.FirstName, "محمدامین").SetProperty(u => u.LastName, "ناظری"));
        });
        using var client = app.CreateClient();
        await client.LoginAsync(customer.Phone, customer.Password);

        var home = await client.GetPageAsync("/profile");

        Assert.Contains("محمدامین ناظری", home.Html, StringComparison.Ordinal);
        Assert.DoesNotContain("محمدامین جان", home.Html, StringComparison.Ordinal);
    }

    // ───────────── #۱۳ حذف همهٔ پیشنهادهای منتظر ─────────────

    private Task<(Guid Pending, Guid Applied)> SeedSuggestionsAsync() => app.WithDbAsync(async db =>
    {
        SeoSuggestion Row(SeoSuggestionStatus status) => new()
        {
            Kind = SeoEntityKind.ContentPage,
            EntityId = Guid.NewGuid(),
            EntityTitle = "صفحهٔ آزمایشی دور ۳۱",
            Path = "/page/r31-" + Guid.NewGuid().ToString("N")[..6],
            Status = status,
            Fields = SeoFields.MetaDescription,
            ContentHash = "r31",
        };
        var pending = Row(SeoSuggestionStatus.Pending);
        var applied = Row(SeoSuggestionStatus.Applied);
        db.SeoSuggestions.AddRange(pending, applied);
        await db.SaveChangesAsync();
        return (pending.Id, applied.Id);
    });

    private Task<bool> ExistsAsync(Guid id) => app.WithDbAsync(db => db.SeoSuggestions.IgnoreQueryFilters().AnyAsync(s => s.Id == id));

    [SqlFact]
    public async Task Owner_deletes_only_pending_suggestions_of_the_selected_kind()
    {
        var (pending, applied) = await SeedSuggestionsAsync();
        using var owner = app.CreateClient();
        await owner.LoginAsync(ShopApp.OwnerPhone);

        var review = await owner.GetPageAsync("/admin/seo/review?kind=ContentPage");
        Assert.Contains("handler=DeletePending", review.Html, StringComparison.OrdinalIgnoreCase);

        using var delete = await owner.PostFormAsync("/admin/seo/review?handler=DeletePending", ("kind", "ContentPage"));

        Assert.Equal(HttpStatusCode.Redirect, delete.StatusCode);
        Assert.False(await ExistsAsync(pending));
        Assert.True(await ExistsAsync(applied));
        var status = await app.WithDbAsync(db => db.SeoSuggestions.Where(s => s.Id == applied).Select(s => s.Status).SingleAsync());
        Assert.Equal(SeoSuggestionStatus.Applied, status);
    }

    [SqlFact]
    public async Task Delete_pending_is_refused_without_permission_or_token()
    {
        var (pending, _) = await SeedSuggestionsAsync();

        using var writer = app.CreateClient();
        await writer.LoginAsync(ShopApp.WriterPhone);
        using var denied = await writer.PostFormAsync("/admin/seo/review?handler=DeletePending", ("kind", "ContentPage"));
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("/admin/denied", denied.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);

        var customer = await app.CreateCustomerAsync();
        using var shopper = app.CreateClient();
        await shopper.LoginAsync(customer.Phone, customer.Password);
        using var hidden = await shopper.PostFormAsync("/admin/seo/review?handler=DeletePending", ("kind", "ContentPage"));
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

        using var owner = app.CreateClient();
        await owner.LoginAsync(ShopApp.OwnerPhone);
        using var forged = await owner.PostRawFormAsync("/admin/seo/review?handler=DeletePending", ("kind", "ContentPage"));
        Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);

        Assert.True(await ExistsAsync(pending));
        await app.WithDbAsync(db => db.SeoSuggestions.Where(s => s.Id == pending).ExecuteDeleteAsync());
    }
}
