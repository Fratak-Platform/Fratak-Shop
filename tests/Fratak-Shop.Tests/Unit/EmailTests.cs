using System.Net.Sockets;
using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.Services.Branding;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Data.Entities.Settings;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Email;
using MailKit.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های سرویس ایمیل (مرحلهٔ دوازدهم) بدون دیتابیس و شبکه:
/// قواعد ورودی پنل (نشانی، سرور، نشانی سایت، پوشاندن نشانی)، سرویس‌های آماده، انتخاب رمزنگاری SMTP،
/// پیام‌های فارسی خطا، قالب ایمیل (Encode، جلوگیری از تزریق سربرگ، لینک فقط به همین سایت)، صف و توکن تأیید ایمیل.
/// </summary>
[Trait("Category", "Unit")]
public class EmailTests
{
    // ── قواعد ورودی ────────────────────────────────────────────────

    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData("  user@example.com ", true)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("not-an-email", false)]
    [InlineData("a@b.co, c@d.co", false)]
    [InlineData("a@b.co a@b.co", false)]
    [InlineData("a@b.co\r\nBcc: x@y.co", false)]
    [InlineData("<a@b.co>", false)]
    public void Address_must_be_exactly_one_valid_mailbox(string? value, bool valid) =>
        Assert.Equal(valid, EmailRules.IsValidAddress(value));

    [Theory]
    [InlineData("smtp.gmail.com", true)]
    [InlineData("mail.fratak.ir", true)]
    [InlineData("localhost", true)]
    [InlineData("https://smtp.gmail.com", false)]
    [InlineData("smtp.gmail.com/path", false)]
    [InlineData("smtp.gmail.com:587", false)]
    [InlineData("-bad.example.com", false)]
    [InlineData("smtp gmail.com", false)]
    [InlineData("", false)]
    public void Host_is_a_plain_server_name(string value, bool valid) =>
        Assert.Equal(valid, EmailRules.IsValidHost(value));

    [Theory]
    [InlineData("https://Fratak.IR/", "https://fratak.ir")]
    [InlineData("https://shop.example/store/", "https://shop.example/store")]
    [InlineData("http://localhost:5000", "http://localhost:5000")]
    [InlineData("ftp://shop.example", null)]
    [InlineData("https://shop.example/?a=1", null)]
    [InlineData("https://shop.example/#x", null)]
    [InlineData("https://user:pass@shop.example", null)]
    [InlineData("/relative", null)]
    [InlineData("javascript:alert(1)", null)]
    public void Base_url_is_normalised_or_rejected(string value, string? expected) =>
        Assert.Equal(expected, EmailRules.NormalizeBaseUrl(value));

    [Theory]
    [InlineData("/profile/orders", "https://shop.example/profile/orders")]
    [InlineData("https://shop.example/cart", "https://shop.example/cart")]
    [InlineData("https://evil.example/x", null)]
    [InlineData("https://shop.example.evil.com/x", null)]
    [InlineData("//evil.example/x", null)]
    [InlineData("/\\evil.example", null)]
    [InlineData("javascript:alert(1)", null)]
    [InlineData("", null)]
    public void Action_links_only_point_to_this_site(string url, string? expected) =>
        Assert.Equal(expected, EmailRules.AbsoluteLink(url, "https://shop.example"));

    [Theory]
    [InlineData("ali@example.com", "al***@example.com")]
    [InlineData("a@example.com", "a***@example.com")]
    [InlineData("@example.com", "***")]
    [InlineData("noat", "***")]
    [InlineData(null, "-")]
    public void Mask_hides_the_mailbox_name(string? value, string expected) =>
        Assert.Equal(expected, EmailRules.Mask(value));

    [Fact]
    public void One_line_removes_control_characters_and_clips()
    {
        Assert.Equal("سلام Bcc: x@y.co", EmailRules.OneLine("سلام\r\nBcc: x@y.co", 200));
        Assert.Equal("abc", EmailRules.OneLine("abcdef", 3));
        Assert.Equal("", EmailRules.OneLine("  \t ", 10));
    }

    [Theory]
    [InlineData(EmailSecurity.Auto, 465, EmailSecurity.SslOnConnect)]
    [InlineData(EmailSecurity.Auto, 587, EmailSecurity.StartTls)]
    [InlineData(EmailSecurity.Auto, 25, EmailSecurity.Auto)]
    [InlineData(EmailSecurity.None, 465, EmailSecurity.None)]
    [InlineData(EmailSecurity.StartTls, 465, EmailSecurity.StartTls)]
    public void Effective_security_follows_the_port_only_in_auto(EmailSecurity security, int port, EmailSecurity expected) =>
        Assert.Equal(expected, EmailRules.Effective(security, port));

    [Fact]
    public void Presets_are_unique_consistent_and_found_case_insensitively()
    {
        Assert.Equal(EmailPresets.All.Count, EmailPresets.All.Select(p => p.Key).Distinct().Count());
        foreach (var preset in EmailPresets.All)
        {
            Assert.Equal(preset.Security, EmailRules.Effective(EmailSecurity.Auto, preset.Port));
            Assert.False(string.IsNullOrWhiteSpace(preset.Help));
            if (!preset.Host.Contains('{'))
                Assert.True(EmailRules.IsValidHost(preset.Host), preset.Host);
        }
        Assert.Equal("gmail", EmailPresets.Find(" GMAIL ")?.Key);
        Assert.Null(EmailPresets.Find("unknown"));
        Assert.Null(EmailPresets.Find(null));
    }

    [Fact]
    public void Runtime_config_reports_why_it_is_not_ready()
    {
        Assert.False(EmailRuntimeConfig.Default.IsSmtpReady);
        Assert.NotNull(EmailRuntimeConfig.Default.NotReadyReason);
        var ready = EmailRuntimeConfig.Default with { Mode = EmailMode.Smtp, Host = "smtp.gmail.com", FromAddress = "shop@gmail.com" };
        Assert.True(ready.IsSmtpReady);
        Assert.Null(ready.NotReadyReason);
        Assert.Equal("فراتک", ready.ToSmtp("فراتک")?.FromName);
        Assert.NotNull((ready with { FromAddress = null }).NotReadyReason);
        Assert.Null((ready with { Host = null }).ToSmtp());
    }

    // ── SMTP ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(EmailSecurity.Auto, 465, SecureSocketOptions.SslOnConnect)]
    [InlineData(EmailSecurity.Auto, 587, SecureSocketOptions.StartTls)]
    [InlineData(EmailSecurity.Auto, 2525, SecureSocketOptions.StartTlsWhenAvailable)]
    [InlineData(EmailSecurity.SslOnConnect, 587, SecureSocketOptions.SslOnConnect)]
    [InlineData(EmailSecurity.StartTls, 465, SecureSocketOptions.StartTls)]
    [InlineData(EmailSecurity.None, 25, SecureSocketOptions.None)]
    public void Socket_options_match_security_and_port(EmailSecurity security, int port, SecureSocketOptions expected) =>
        Assert.Equal(expected, SmtpTransport.SocketOptions(security, port));

    [Fact]
    public void Describe_turns_errors_into_persian_hints_but_lets_real_cancellation_through()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Assert.Null(SmtpTransport.Describe(new OperationCanceledException(), cancelled.Token));
        Assert.Contains("مهلت", SmtpTransport.Describe(new OperationCanceledException(), CancellationToken.None));
        Assert.Contains("مهلت", SmtpTransport.Describe(new TimeoutException(), CancellationToken.None));
        Assert.Contains("برقرار نشد", SmtpTransport.Describe(new SocketException(), CancellationToken.None));
        Assert.Contains("App Password", SmtpTransport.Describe(new AuthenticationException(), CancellationToken.None));
        Assert.Contains("نشانی ایمیل", SmtpTransport.Describe(new FormatException(), CancellationToken.None));
        Assert.Contains("نشانی ایمیل", SmtpTransport.Describe(new ArgumentException("x"), CancellationToken.None));
        Assert.Contains("NotImplementedException", SmtpTransport.Describe(new NotImplementedException(), CancellationToken.None));
    }

    [Fact]
    public void Only_connection_problems_are_retried()
    {
        Assert.True(EmailDispatcher.IsTransient(SmtpTransport.Describe(new TimeoutException(), CancellationToken.None)!));
        Assert.True(EmailDispatcher.IsTransient(SmtpTransport.Describe(new SocketException(), CancellationToken.None)!));
        Assert.True(EmailDispatcher.IsTransient(SmtpTransport.Describe(new IOException("reset"), CancellationToken.None)!));
        Assert.False(EmailDispatcher.IsTransient(SmtpTransport.Describe(new AuthenticationException(), CancellationToken.None)!));
        Assert.False(EmailDispatcher.IsTransient(SmtpTransport.Describe(new FormatException(), CancellationToken.None)!));
    }

    // ── صف ─────────────────────────────────────────────────────────

    [Fact]
    public void Queue_drops_invalid_messages_and_accepts_valid_ones()
    {
        var diagnostics = new EmailDiagnostics();
        var queue = new EmailQueue(diagnostics, NullLogger<EmailQueue>.Instance);

        Assert.False(queue.Enqueue(new EmailMessage("bad", "s", "b")));
        Assert.False(queue.Enqueue(new EmailMessage("a@b.co", " ", "b")));
        Assert.False(queue.Enqueue(new EmailMessage("a@b.co", "s", "")));
        Assert.False(queue.Enqueue(null!));
        Assert.Equal(4, diagnostics.Dropped);

        Assert.True(queue.Enqueue(new EmailMessage("a@b.co", "s", "b")));
        Assert.Equal(1, queue.Pending);
        Assert.True(queue.Reader.TryRead(out var message));
        Assert.Equal("a@b.co", message!.To);
    }

    [Fact]
    public void Diagnostics_track_the_last_attempt()
    {
        var diagnostics = new EmailDiagnostics();
        Assert.False(diagnostics.LastAttemptFailed);
        diagnostics.Failure("x");
        Assert.True(diagnostics.LastAttemptFailed);
        Assert.Equal(1, diagnostics.Failed);
    }

    // ── قالب ایمیل ─────────────────────────────────────────────────

    private static EmailComposer.Context Ctx(string? baseUrl = "https://shop.example") =>
        new("فراتک شاپ", baseUrl,
            BrandResolver.Resolve(BrandPlace.Email, new StoreSettingDto(), new BrandStyleConfig(), BrandIdentity.Defaults),
            "021-1234", "support@shop.example");

    [Fact]
    public void Compose_encodes_everything_and_keeps_the_subject_on_one_line()
    {
        var message = new EmailMessage("a@b.co", "سفارش <b>\r\nBcc: x@y.co", "سلام <script>alert(1)</script>\n\nپاراگراف دوم")
        {
            ActionUrl = "/profile/orders",
            ActionText = "<i>دیدن</i>",
        };
        var (subject, html, text) = EmailComposer.Compose(message, Ctx());

        Assert.DoesNotContain('\n', subject);
        Assert.DoesNotContain('\r', subject);
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<b>", html);
        Assert.DoesNotContain("<i>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains("href=\"https://shop.example/profile/orders\"", html);
        Assert.Contains("https://shop.example/profile/orders", text);
        Assert.Equal(2, html.Split("<p style=").Length - 1);
        Assert.Contains("support@shop.example", html);
        Assert.Contains("https://shop.example" + BrandIdentity.MarkPng, html);
    }

    [Fact]
    public void Compose_drops_foreign_links_and_works_without_a_site_url()
    {
        var foreign = EmailComposer.Compose(new EmailMessage("a@b.co", "s", "b") { ActionUrl = "https://evil.example/login" }, Ctx());
        Assert.DoesNotContain("evil.example", foreign.Html);
        Assert.DoesNotContain("evil.example", foreign.Text);

        var noBase = EmailComposer.Compose(new EmailMessage("a@b.co", "", "b") { ActionUrl = "/x" }, Ctx(null));
        Assert.Equal("فراتک شاپ", noBase.Subject);
        Assert.DoesNotContain("<img", noBase.Html);
        Assert.DoesNotContain("href=", noBase.Html);
    }

    // ── توکن تأیید ایمیل ───────────────────────────────────────────

    private static EmailVerification Verification(IDataProtectionProvider protection) =>
        new(protection, null!, null!, null!, null!, null!);

    [Fact]
    public void Verification_token_round_trips_and_normalises_the_address()
    {
        var verification = Verification(new EphemeralDataProtectionProvider());
        var id = Guid.NewGuid();

        var token = verification.CreateToken(id, "  Ali@Example.com ");
        var data = verification.ReadToken(token);

        Assert.NotNull(data);
        Assert.Equal(id, data.Value.UserId);
        Assert.Equal("ali@example.com", data.Value.Email);
    }

    [Fact]
    public void Verification_token_rejects_tampering_expiry_other_keys_and_bad_payloads()
    {
        var protection = new EphemeralDataProtectionProvider();
        var verification = Verification(protection);
        var token = verification.CreateToken(Guid.NewGuid(), "ali@example.com");

        var swap = token[5] == 'A' ? 'B' : 'A';
        var tampered = token[..5] + swap + token[6..];
        Assert.Null(verification.ReadToken(tampered));
        Assert.Null(verification.ReadToken(token + "x"));
        Assert.Null(verification.ReadToken(""));
        Assert.Null(verification.ReadToken(null));
        Assert.Null(verification.ReadToken(new string('a', 2001)));
        Assert.Null(Verification(new EphemeralDataProtectionProvider()).ReadToken(token)); // کلید دیگر

        var limited = protection.CreateProtector(EmailVerification.Purpose).ToTimeLimitedDataProtector();
        Assert.Null(verification.ReadToken(limited.Protect($"{Guid.NewGuid():N}|ali@example.com", DateTimeOffset.UtcNow.AddMinutes(-1))));
        Assert.Null(verification.ReadToken(limited.Protect("no-separator", TimeSpan.FromHours(1))));
        Assert.Null(verification.ReadToken(limited.Protect($"{Guid.NewGuid():N}|not-an-email", TimeSpan.FromHours(1))));
        Assert.Null(verification.ReadToken(protection.CreateProtector("Other.Purpose").ToTimeLimitedDataProtector()
            .Protect($"{Guid.NewGuid():N}|ali@example.com", TimeSpan.FromHours(1))));
    }

    [Fact]
    public void Verification_limits_are_sensible()
    {
        Assert.Equal(TimeSpan.FromHours(24), EmailVerification.Lifetime);
        Assert.Equal(TimeSpan.FromMinutes(2), EmailVerification.Cooldown);
        Assert.Equal(5, EmailVerification.DailyLimit);
    }
}
