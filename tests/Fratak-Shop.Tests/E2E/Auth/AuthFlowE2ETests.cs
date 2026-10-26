using System.Net;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Tests.E2E.Auth;

/// <summary>
/// ثبت‌نام، تأیید شماره، ورود با رمز، ورود با کد پیامکی، ورود دومرحله‌ای، خروج و تغییر رمز – از فرم‌های واقعی،
/// از بهترین حالت تا بدترین (رمز اشتباه، کد اشتباه، حساب مسدود، قفل حساب، کپچا).
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Auth")]
public class AuthFlowE2ETests(ShopApp app)
{
    private const string WrongCredentials = "شماره موبایل یا رمز عبور اشتباه است.";

    // ── ثبت‌نام ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_then_verify_phone_with_the_sms_code()
    {
        using var client = app.CreateClient();
        var phone = ShopApp.NewPhone();

        using var register = await client.PostFormAsync("/register",
            ("Input.PhoneNumber", phone), ("Input.Password", "secret123"), ("Input.ConfirmPassword", "secret123"), ("Input.AcceptRules", "true"));

        Assert.Equal(HttpStatusCode.Redirect, register.StatusCode);
        Assert.StartsWith("/verify", register.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(ShopClient.SetCookies(register), c => c.StartsWith("fratak_at=", StringComparison.Ordinal));
        var verifyPage = await client.GetPageAsync("/verify");
        Assert.NotEmpty(verifyPage.All("input[name='Digits']"));

        var code = app.LastOtp(phone);
        Assert.NotNull(code);
        using var verify = await client.PostFormAsync("/verify", code!.Select(d => ("Digits", d.ToString())).ToArray());

        Assert.Equal(HttpStatusCode.Redirect, verify.StatusCode);
        Assert.StartsWith("/welcome", verify.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
        var user = await app.WithDbAsync(db => db.Users.FirstAsync(u => u.PhoneNumber == phone));
        Assert.True(user.IsPhoneVerified);
        Assert.NotEqual("secret123", user.PasswordHash);
        Assert.DoesNotContain("secret123", user.PasswordHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Wrong_verification_code_keeps_the_phone_unverified()
    {
        using var client = app.CreateClient();
        var phone = ShopApp.NewPhone();
        using (await client.PostFormAsync("/register",
                   ("Input.PhoneNumber", phone), ("Input.Password", "secret123"), ("Input.ConfirmPassword", "secret123"), ("Input.AcceptRules", "true")))
        {
        }

        var wrong = app.LastOtp(phone) == "11111" ? "22222" : "11111";
        using var verify = await client.PostFormAsync("/verify", wrong.Select(d => ("Digits", d.ToString())).ToArray());

        Assert.Equal(HttpStatusCode.Redirect, verify.StatusCode);
        Assert.StartsWith("/verify", verify.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.False(await app.WithDbAsync(db => db.Users.Where(u => u.PhoneNumber == phone).Select(u => u.IsPhoneVerified).FirstAsync()));
    }

    [Theory]
    [InlineData("0912", "secret123", "secret123", "true")]          // شمارهٔ ناقص
    [InlineData("08123456789", "secret123", "secret123", "true")]   // پیش‌شمارهٔ غیرموبایل
    [InlineData("{phone}", "123", "123", "true")]                   // رمز کوتاه
    [InlineData("{phone}", "secret123", "secret999", "true")]       // تکرار نادرست
    [InlineData("{phone}", "secret123", "secret123", "false")]      // قوانین پذیرفته نشده
    public async Task Invalid_registration_creates_no_account(string phone, string password, string confirm, string accept)
    {
        using var client = app.CreateClient();
        var actualPhone = phone == "{phone}" ? ShopApp.NewPhone() : phone;

        using var response = await client.PostFormAsync("/register",
            ("Input.PhoneNumber", actualPhone), ("Input.Password", password), ("Input.ConfirmPassword", confirm), ("Input.AcceptRules", accept));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(ShopClient.SetCookies(response), c => c.StartsWith("fratak_at=", StringComparison.Ordinal));
        Assert.False(await app.WithDbAsync(db => db.Users.IgnoreQueryFilters().AnyAsync(u => u.PhoneNumber == actualPhone)));
    }

    [Fact]
    public async Task Registering_an_existing_phone_does_not_take_over_the_account()
    {
        var owner = await app.CreateCustomerAsync();
        var hashBefore = await app.WithDbAsync(db => db.Users.Where(u => u.Id == owner.Id).Select(u => u.PasswordHash).FirstAsync());
        using var client = app.CreateClient();

        using var response = await client.PostFormAsync("/register",
            ("Input.PhoneNumber", owner.Phone), ("Input.Password", "attacker1"), ("Input.ConfirmPassword", "attacker1"), ("Input.AcceptRules", "true"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(ShopClient.SetCookies(response), c => c.StartsWith("fratak_at=", StringComparison.Ordinal));
        Assert.Equal(1, await app.WithDbAsync(db => db.Users.IgnoreQueryFilters().CountAsync(u => u.PhoneNumber == owner.Phone)));
        Assert.Equal(hashBefore, await app.WithDbAsync(db => db.Users.Where(u => u.Id == owner.Id).Select(u => u.PasswordHash).FirstAsync()));
    }

    [Fact]
    public async Task Bulk_sign_up_from_one_ip_is_stopped_by_the_captcha()
    {
        using var client = app.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            var phone = ShopApp.NewPhone();
            using var ok = await client.PostFormAsync("/register",
                ("Input.PhoneNumber", phone), ("Input.Password", "secret123"), ("Input.ConfirmPassword", "secret123"), ("Input.AcceptRules", "true"));
            Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
            await client.LogoutAsync();
        }

        var blockedPhone = ShopApp.NewPhone();
        using var blocked = await client.PostFormAsync("/register",
            ("Input.PhoneNumber", blockedPhone), ("Input.Password", "secret123"), ("Input.ConfirmPassword", "secret123"), ("Input.AcceptRules", "true"));

        Assert.Equal(HttpStatusCode.OK, blocked.StatusCode);
        Assert.Contains("CaptchaToken", await blocked.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.False(await app.WithDbAsync(db => db.Users.AnyAsync(u => u.PhoneNumber == blockedPhone)));
    }

    // ── ورود با رمز عبور ──────────────────────────────────────────────────

    [Fact]
    public async Task Password_login_issues_secure_http_only_jwt_cookies()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();

        using var response = await client.PostFormAsync("/login",
            ("Input.PhoneNumber", customer.Phone), ("Input.Password", customer.Password), ("Input.RememberMe", "true"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/", response.Headers.Location?.OriginalString);
        var cookies = ShopClient.SetCookies(response);
        foreach (var name in new[] { "fratak_at=", "fratak_rt=" })
        {
            var cookie = Assert.Single(cookies, c => c.StartsWith(name, StringComparison.Ordinal));
            Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("samesite=", cookie, StringComparison.OrdinalIgnoreCase);
        }

        await client.GetPageAsync("/profile");
    }

    [Fact]
    public async Task Wrong_password_and_unknown_phone_get_the_same_answer()
    {
        var customer = await app.CreateCustomerAsync();

        using var wrongPassword = app.CreateClient();
        using var first = await wrongPassword.PostFormAsync("/login", ("Input.PhoneNumber", customer.Phone), ("Input.Password", "not-the-password"));
        using var unknownPhone = app.CreateClient();
        using var second = await unknownPhone.PostFormAsync("/login", ("Input.PhoneNumber", ShopApp.NewPhone()), ("Input.Password", "not-the-password"));

        foreach (var response in new[] { first, second })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(WrongCredentials, await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.DoesNotContain(ShopClient.SetCookies(response), c => c.StartsWith("fratak_at=", StringComparison.Ordinal));
        }

        await wrongPassword.GetPageAsync("/profile", HttpStatusCode.Redirect);
    }

    [Fact]
    public async Task Captcha_appears_after_three_failures_from_one_ip()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        for (var i = 0; i < 3; i++)
        {
            using var failed = await client.PostFormAsync("/login", ("Input.PhoneNumber", customer.Phone), ("Input.Password", "wrong-" + i));
            Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        }

        // حتی رمز درست بدون حل کپچا پذیرفته نمی‌شود.
        using var response = await client.PostFormAsync("/login", ("Input.PhoneNumber", customer.Phone), ("Input.Password", customer.Password));
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("CaptchaToken", html, StringComparison.Ordinal);
        Assert.DoesNotContain(ShopClient.SetCookies(response), c => c.StartsWith("fratak_at=", StringComparison.Ordinal));

        // کاربر دیگری با IP دیگر تحت تأثیر نیست.
        using var other = app.CreateClient();
        await other.LoginAsync(customer.Phone, customer.Password);
    }

    [Fact]
    public async Task Distributed_password_guessing_locks_the_account()
    {
        var customer = await app.CreateCustomerAsync();

        // مهاجم از ۵ IP مختلف حدس می‌زند (کپچای هر IP فعال نمی‌شود) → قفل حساب.
        for (var i = 0; i < 5; i++)
        {
            using var attacker = app.CreateClient();
            using (await attacker.PostFormAsync("/login", ("Input.PhoneNumber", customer.Phone), ("Input.Password", "guess-" + i)))
            {
            }
        }

        using var owner = app.CreateClient();
        using var response = await owner.PostFormAsync("/login", ("Input.PhoneNumber", customer.Phone), ("Input.Password", customer.Password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(ShopClient.SetCookies(response), c => c.StartsWith("fratak_at=", StringComparison.Ordinal));
        var lockout = await app.WithDbAsync(db => db.Users.Where(u => u.Id == customer.Id).Select(u => u.LockoutEndAt).FirstAsync());
        Assert.NotNull(lockout);
        Assert.True(lockout > DateTimeOffset.UtcNow.AddMinutes(10));

        // رویداد امنیتی «قفل حساب» برای پنل نظارت ثبت می‌شود (شماره به‌صورت ماسک‌شده).
        var masked = customer.Phone[..4] + "***" + customer.Phone[^4..];
        Assert.True(await app.WithDbAsync(db => db.SecurityLogs.AnyAsync(e =>
            e.Type == Fratak_Shop.Data.Entities.Admin.SecurityEventType.LockedOut && e.Identifier == masked)));
    }

    [Fact]
    public async Task Blocked_customer_cannot_sign_in_with_password_or_sms_code()
    {
        var customer = await app.CreateCustomerAsync();
        await app.WithDbAsync(db => db.Users.Where(u => u.Id == customer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsBlocked, true)));

        using var client = app.CreateClient();
        using var password = await client.PostFormAsync("/login", ("Input.PhoneNumber", customer.Phone), ("Input.Password", customer.Password));
        Assert.Equal(HttpStatusCode.OK, password.StatusCode);
        Assert.DoesNotContain(ShopClient.SetCookies(password), c => c.StartsWith("fratak_at=", StringComparison.Ordinal));

        using var otpClient = app.CreateClient();
        using (await otpClient.PostFormAsync("/login?handler=SendCode", ("Otp.PhoneNumber", customer.Phone)))
        {
        }
        var code = app.LastOtp(customer.Phone);
        Assert.NotNull(code);
        using var otp = await otpClient.PostFormAsync("/login/code?phone=" + customer.Phone, code!.Select(d => ("Digits", d.ToString())).ToArray());
        Assert.DoesNotContain(ShopClient.SetCookies(otp), c => c.StartsWith("fratak_at=", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("https://evil.example/steal")]
    [InlineData("//evil.example/steal")]
    [InlineData("/\\evil.example/steal")]
    [InlineData("javascript:alert(1)")]
    public async Task Login_never_redirects_to_another_site(string returnUrl)
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();

        using var response = await client.PostFormAsync("/login?returnUrl=" + Uri.EscapeDataString(returnUrl),
            ("Input.PhoneNumber", customer.Phone), ("Input.Password", customer.Password));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.StartsWith("/", location, StringComparison.Ordinal);
        Assert.False(location.StartsWith("//", StringComparison.Ordinal) || location.StartsWith("/\\", StringComparison.Ordinal), location);
        Assert.DoesNotContain("evil", location, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_returns_to_the_local_page_that_asked_for_it()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();

        using var challenge = await client.GetAsync("/profile/orders");
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        Assert.StartsWith("/login", challenge.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);

        using var response = await client.PostFormAsync("/login?returnUrl=%2Fprofile%2Forders",
            ("Input.PhoneNumber", customer.Phone), ("Input.Password", customer.Password));

        Assert.Equal("/profile/orders", response.Headers.Location?.OriginalString);
    }

    // ── ورود با کد پیامکی ─────────────────────────────────────────────────

    [Fact]
    public async Task Sms_code_login_works_once_and_the_code_cannot_be_reused()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();

        using var send = await client.PostFormAsync("/login?handler=SendCode", ("Otp.PhoneNumber", customer.Phone));
        Assert.Equal(HttpStatusCode.Redirect, send.StatusCode);
        Assert.StartsWith("/login/code", send.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
        var code = app.LastOtp(customer.Phone)!;
        Assert.Equal(5, code.Length);

        using var login = await client.PostFormAsync("/login/code?phone=" + customer.Phone, code.Select(d => ("Digits", d.ToString())).ToArray());
        Assert.Contains(ShopClient.SetCookies(login), c => c.StartsWith("fratak_at=", StringComparison.Ordinal));
        await client.GetPageAsync("/profile");

        using var replayClient = app.CreateClient();
        using var replay = await replayClient.PostFormAsync("/login/code?phone=" + customer.Phone, code.Select(d => ("Digits", d.ToString())).ToArray());
        Assert.DoesNotContain(ShopClient.SetCookies(replay), c => c.StartsWith("fratak_at=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sms_code_is_destroyed_after_five_wrong_guesses()
    {
        var customer = await app.CreateCustomerAsync();
        using (var sender = app.CreateClient())
        using (await sender.PostFormAsync("/login?handler=SendCode", ("Otp.PhoneNumber", customer.Phone)))
        {
        }
        var code = app.LastOtp(customer.Phone)!;
        var wrong = code == "12345" ? "54321" : "12345";

        // هر حدس از یک IP تازه (تا کپچای هر IP جلوی شمارش را نگیرد) – بدترین حالت: مهاجم توزیع‌شده.
        for (var i = 0; i < 5; i++)
        {
            using var attacker = app.CreateClient();
            using var guess = await attacker.PostFormAsync("/login/code?phone=" + customer.Phone, wrong.Select(d => ("Digits", d.ToString())).ToArray());
            Assert.DoesNotContain(ShopClient.SetCookies(guess), c => c.StartsWith("fratak_at=", StringComparison.Ordinal));
        }

        using var owner = app.CreateClient();
        using var late = await owner.PostFormAsync("/login/code?phone=" + customer.Phone, code.Select(d => ("Digits", d.ToString())).ToArray());
        Assert.DoesNotContain(ShopClient.SetCookies(late), c => c.StartsWith("fratak_at=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sms_code_resend_is_throttled()
    {
        var phone = (await app.CreateCustomerAsync()).Phone;
        using var client = app.CreateClient();
        int Sent() { lock (app.Sms.Sent) return app.Sms.Sent.Count(s => s.Phone == phone); }

        using (await client.PostFormAsync("/login?handler=SendCode", ("Otp.PhoneNumber", phone)))
        {
        }
        using (await client.PostFormAsync("/login?handler=SendCode", ("Otp.PhoneNumber", phone)))
        {
        }
        using var other = app.CreateClient();
        using (await other.PostFormAsync("/login?handler=SendCode", ("Otp.PhoneNumber", phone)))
        {
        }

        Assert.Equal(1, Sent());
    }

    // ── ورود دومرحله‌ای ───────────────────────────────────────────────────

    [Fact]
    public async Task Two_factor_login_needs_the_sms_code_after_the_password()
    {
        var customer = await app.CreateCustomerAsync();
        await app.WithDbAsync(db => db.Users.Where(u => u.Id == customer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.TwoFactorEnabled, true)));
        using var client = app.CreateClient();

        using var password = await client.PostFormAsync("/login", ("Input.PhoneNumber", customer.Phone), ("Input.Password", customer.Password));

        Assert.Equal(HttpStatusCode.Redirect, password.StatusCode);
        Assert.StartsWith("/login/2fa", password.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(ShopClient.SetCookies(password), c => c.StartsWith("fratak_at=", StringComparison.Ordinal));
        await client.GetPageAsync("/profile", HttpStatusCode.Redirect);

        var code = app.LastOtp(customer.Phone)!;
        using var second = await client.PostFormAsync("/login/2fa", code.Select(d => ("Digits", d.ToString())).ToArray());
        Assert.Contains(ShopClient.SetCookies(second), c => c.StartsWith("fratak_at=", StringComparison.Ordinal));
        await client.GetPageAsync("/profile");
    }

    [Fact]
    public async Task Two_factor_step_cannot_be_used_without_the_password_step()
    {
        using var client = app.CreateClient();

        using var response = await client.PostFormAsync("/login/2fa", ("Digits", "1"), ("Digits", "2"), ("Digits", "3"), ("Digits", "4"), ("Digits", "5"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.DoesNotContain(ShopClient.SetCookies(response), c => c.StartsWith("fratak_at=", StringComparison.Ordinal));
    }

    // ── خروج و تغییر رمز ─────────────────────────────────────────────────

    [Fact]
    public async Task Logout_revokes_the_refresh_token_and_ends_the_session()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(customer.Phone, customer.Password);
        await client.GetPageAsync("/profile");

        await client.LogoutAsync();

        await client.GetPageAsync("/profile", HttpStatusCode.Redirect);
        var active = await app.WithDbAsync(db => db.UserRefreshTokens.CountAsync(t => t.UserId == customer.Id && t.RevokedAt == null));
        Assert.Equal(0, active);
    }

    [Fact]
    public async Task Changing_the_password_signs_out_every_other_device()
    {
        var customer = await app.CreateCustomerAsync();
        using var laptop = app.CreateClient();
        using var phone = app.CreateClient();
        await laptop.LoginAsync(customer.Phone, customer.Password);
        await phone.LoginAsync(customer.Phone, customer.Password);
        await phone.GetPageAsync("/profile");

        using var change = await laptop.PostFormAsync("/profile/password",
            ("Input.CurrentPassword", customer.Password), ("Input.NewPassword", "NewSecret456"), ("Input.ConfirmPassword", "NewSecret456"));

        Assert.Equal(HttpStatusCode.Redirect, change.StatusCode);
        await laptop.GetPageAsync("/profile");
        await phone.GetPageAsync("/profile", HttpStatusCode.Redirect);

        using var oldPassword = app.CreateClient();
        using var rejected = await oldPassword.PostFormAsync("/login", ("Input.PhoneNumber", customer.Phone), ("Input.Password", customer.Password));
        Assert.Contains(WrongCredentials, await rejected.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var newPassword = app.CreateClient();
        await newPassword.LoginAsync(customer.Phone, "NewSecret456");
    }

    [Fact]
    public async Task Password_change_with_a_wrong_current_password_is_refused()
    {
        var customer = await app.CreateCustomerAsync();
        using var client = app.CreateClient();
        await client.LoginAsync(customer.Phone, customer.Password);

        using var change = await client.PostFormAsync("/profile/password",
            ("Input.CurrentPassword", "not-mine"), ("Input.NewPassword", "NewSecret456"), ("Input.ConfirmPassword", "NewSecret456"));

        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        using var other = app.CreateClient();
        await other.LoginAsync(customer.Phone, customer.Password);
    }

    [Theory]
    [InlineData("/profile")]
    [InlineData("/profile/orders")]
    [InlineData("/profile/addresses")]
    [InlineData("/profile/wallet")]
    [InlineData("/profile/sessions")]
    [InlineData("/profile/password")]
    [InlineData("/checkout")]
    public async Task Private_pages_send_guests_to_login(string url)
    {
        using var client = app.CreateClient();

        using var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/login", response.Headers.Location?.OriginalString, StringComparison.OrdinalIgnoreCase);
    }
}
