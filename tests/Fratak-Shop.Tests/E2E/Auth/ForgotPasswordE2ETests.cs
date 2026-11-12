using System.Net;
using Fratak_Shop.Tests.E2E.Infrastructure;

namespace Fratak_Shop.Tests.E2E.Auth;

/// <summary>
/// دور ۳۰ (#۷) – «فراموشی رمز عبور» از فرم‌های واقعی: مسیر کامل (کد پیامکی ← رمز تازه ← ورود با رمز تازه)، و حالت‌های بد:
/// شمارهٔ غیرعضو (همان جواب، بدون پیامک)، کد اشتباه، کد «ورود» به‌جای کد «بازیابی»، تکرار رمز نادرست و استفادهٔ دوباره از کد.
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Auth")]
public class ForgotPasswordE2ETests(ShopApp app)
{
    private const string NewPassword = "Fresh#Pass2026";

    private static (string, string)[] ResetForm(string code, string password, string confirm) =>
        code.Select(d => ("Digits", d.ToString()))
            .Append(("Input.NewPassword", password))
            .Append(("Input.ConfirmPassword", confirm))
            .ToArray();

    [SqlFact]
    public async Task Forgot_password_resets_with_the_sms_code_and_the_new_password_works()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();

        var page = await client.GetPageAsync("/forgot-password");
        Assert.NotEmpty(page.All("input[name='Input.PhoneNumber']"));

        using var send = await client.PostFormAsync("/forgot-password", ("Input.PhoneNumber", customer.Phone));
        Assert.Equal(HttpStatusCode.Redirect, send.StatusCode);
        Assert.StartsWith("/forgot-password/reset", send.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
        var code = app.LastOtp(customer.Phone);
        Assert.NotNull(code);

        using var reset = await client.PostFormAsync("/forgot-password/reset?phone=" + customer.Phone, ResetForm(code!, NewPassword, NewPassword));
        Assert.Equal(HttpStatusCode.Redirect, reset.StatusCode);
        Assert.StartsWith("/login", reset.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
        // بازیابی کسی را خودکار وارد نمی‌کند (کوکی نشست فقط «پاک» می‌شود: «fratak_at=;» بدون مقدار).
        Assert.DoesNotContain(ShopClient.SetCookies(reset),
            c => c.StartsWith("fratak_at=", StringComparison.Ordinal) && !c.StartsWith("fratak_at=;", StringComparison.Ordinal));

        // رمز تازه کار می‌کند و رمز قدیمی نه.
        using var fresh = app.CreateClient();
        await fresh.LoginAsync(customer.Phone, NewPassword);
        using var old = app.CreateClient();
        using var oldLogin = await old.PostFormAsync("/login", ("Input.PhoneNumber", customer.Phone), ("Input.Password", customer.Password));
        Assert.Equal(HttpStatusCode.OK, oldLogin.StatusCode);
        Assert.DoesNotContain(ShopClient.SetCookies(oldLogin), c => c.StartsWith("fratak_at=", StringComparison.Ordinal));

        // همان کد دوباره پذیرفته نمی‌شود.
        using var replay = await app.CreateClient().PostFormAsync("/forgot-password/reset?phone=" + customer.Phone, ResetForm(code!, "Other#Pass2026", "Other#Pass2026"));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var still = app.CreateClient();
        await still.LoginAsync(customer.Phone, NewPassword);
    }

    [SqlFact]
    public async Task Unknown_phone_gets_the_same_answer_but_no_sms()
    {
        var phone = ShopApp.NewPhone();
        using var client = app.CreateClient();

        using var send = await client.PostFormAsync("/forgot-password", ("Input.PhoneNumber", phone));

        Assert.Equal(HttpStatusCode.Redirect, send.StatusCode);
        Assert.StartsWith("/forgot-password/reset", send.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.Null(app.LastOtp(phone));
    }

    [SqlFact]
    public async Task Wrong_code_or_mismatched_password_changes_nothing()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        using (await client.PostFormAsync("/forgot-password", ("Input.PhoneNumber", customer.Phone)))
        {
        }
        var code = app.LastOtp(customer.Phone)!;
        var wrong = code == "11111" ? "22222" : "11111";

        using var bad = await client.PostFormAsync("/forgot-password/reset?phone=" + customer.Phone, ResetForm(wrong, NewPassword, NewPassword));
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode);
        Assert.Contains("کد وارد شده صحیح نیست", await bad.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var mismatch = await app.CreateClient().PostFormAsync("/forgot-password/reset?phone=" + customer.Phone, ResetForm(code, NewPassword, NewPassword + "x"));
        Assert.Equal(HttpStatusCode.OK, mismatch.StatusCode);

        // رمز قبلی هنوز کار می‌کند.
        using var login = app.CreateClient();
        await login.LoginAsync(customer.Phone, customer.Password);
    }

    [SqlFact]
    public async Task A_login_code_cannot_reset_the_password()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        using (await client.PostFormAsync("/login?handler=SendCode", ("Otp.PhoneNumber", customer.Phone)))
        {
        }
        var loginCode = app.LastOtp(customer.Phone);
        Assert.NotNull(loginCode);

        using var attempt = await app.CreateClient().PostFormAsync("/forgot-password/reset?phone=" + customer.Phone, ResetForm(loginCode!, NewPassword, NewPassword));

        Assert.Equal(HttpStatusCode.OK, attempt.StatusCode);
        using var login = app.CreateClient();
        await login.LoginAsync(customer.Phone, customer.Password);
    }

    [SqlFact]
    public async Task Reset_page_without_a_valid_phone_goes_back_to_the_first_step()
    {
        using var client = app.CreateClient();

        using var noPhone = await client.GetAsync("/forgot-password/reset");
        Assert.Equal(HttpStatusCode.Redirect, noPhone.StatusCode);
        Assert.StartsWith("/forgot-password", noPhone.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);

        using var attack = await client.GetAsync("/forgot-password/reset?phone=%3Cscript%3E");
        Assert.Equal(HttpStatusCode.Redirect, attack.StatusCode);
    }
}
