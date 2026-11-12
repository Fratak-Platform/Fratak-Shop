using System.Reflection;
using System.Text;
using Fratak_Shop.Core.Services.Team;
using Fratak_Shop.Data.Entities.Team;
using Fratak_Shop.Web.Areas.Admin.Pages.TeamPages;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Team;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های «امنیت و کار تیمی کارکنان» (مرحلهٔ هفتم – فاز ۱۰) بدون دیتابیس:
/// • TOTP: بردارهای رسمی RFC 6238، رفت‌وبرگشت Base32، تحمل یک گام اختلاف ساعت، جلوگیری از استفادهٔ دوباره، ارقام فارسی.
/// • کدهای بازیابی: شکل، یکتایی و یکسان‌سازی ورودی.
/// • منشن‌ها: یکسان‌سازی نام، پیدا کردن همکار، نادیده گرفتن ایمیل و منشن ناقص، برجسته‌سازی امن (بدون تزریق HTML).
/// • پوشاندن اطلاعات (موبایل/ایمیل/نشانی/شبا/کارت)، آدرس امن کار و بازگشت امن بعد از تأیید دوباره.
/// • کوکی تأیید دوباره: فقط برای همان کارمند معتبر است و دست‌کاری‌شده‌اش پذیرفته نمی‌شود.
/// • برچسب [RequireReauth] روی همهٔ کارهای حساس.
/// </summary>
public class TeamTests
{
    // RFC 6238 پیوست B: کلید ASCII «12345678901234567890» (SHA1). کد ۶ رقمی = ۶ رقم آخر کد ۸ رقمی جدول.
    private static readonly byte[] RfcSecret = Encoding.ASCII.GetBytes("12345678901234567890");

    // ═══ TOTP ═══════════════════════════════════════════════════════════

    [Theory]
    [InlineData(59L, "287082")]
    [InlineData(1111111109L, "081804")]
    [InlineData(1111111111L, "050471")]
    [InlineData(1234567890L, "005924")]
    [InlineData(2000000000L, "279037")]
    public void Totp_matches_rfc6238_vectors(long unixSeconds, string expected)
    {
        var step = Totp.StepAt(DateTimeOffset.FromUnixTimeSeconds(unixSeconds));
        Assert.Equal(expected, Totp.Compute(RfcSecret, step));
    }

    [Fact]
    public void Base32_round_trips_and_matches_the_known_encoding()
    {
        Assert.Equal("GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ", Totp.ToBase32(RfcSecret));
        Assert.Equal(RfcSecret, Totp.FromBase32("gezd gnbv gy3t qojq gezd gnbv gy3t qojq"));

        for (var i = 0; i < 20; i++)
        {
            var secret = Totp.NewSecret();
            Assert.Equal(20, secret.Length);
            Assert.Equal(secret, Totp.FromBase32(Totp.ToBase32(secret)));
        }

        Assert.Null(Totp.FromBase32(""));
        Assert.Null(Totp.FromBase32("ABC1")); // «1» در الفبای Base32 نیست
    }

    [Fact]
    public void Verify_accepts_one_step_of_clock_drift_and_rejects_replays()
    {
        var at59 = DateTimeOffset.FromUnixTimeSeconds(59); // گام ۱

        Assert.Equal(1L, Totp.Verify(RfcSecret, "287082", at59));
        Assert.Equal(1L, Totp.Verify(RfcSecret, "287 082", at59));
        Assert.Equal(1L, Totp.Verify(RfcSecret, "۲۸۷۰۸۲", at59));

        // ۳۰ ثانیه بعد (گام ۲) هنوز پذیرفته است؛ ۹۰ ثانیه بعد نه.
        Assert.Equal(1L, Totp.Verify(RfcSecret, "287082", at59.AddSeconds(30)));
        Assert.Null(Totp.Verify(RfcSecret, "287082", at59.AddSeconds(90)));

        // کدی که یک بار مصرف شده (گام ۱) دوباره پذیرفته نمی‌شود.
        Assert.Null(Totp.Verify(RfcSecret, "287082", at59, lastUsedStep: 1));

        Assert.Null(Totp.Verify(RfcSecret, "287083", at59));
        Assert.Null(Totp.Verify(RfcSecret, "28708", at59));
        Assert.Null(Totp.Verify(RfcSecret, "28708a", at59));
        Assert.Null(Totp.Verify(RfcSecret, null, at59));
    }

    [Fact]
    public void Recovery_codes_are_unique_and_normalized()
    {
        var codes = Totp.NewRecoveryCodes();
        Assert.Equal(10, codes.Count);
        Assert.Equal(10, codes.Distinct().Count());
        Assert.All(codes, c => Assert.Matches("^[a-z2-9]{5}-[a-z2-9]{5}$", c));

        Assert.Equal("abcde23456", Totp.NormalizeRecoveryCode(" ABCDE-23456 "));
        Assert.Null(Totp.NormalizeRecoveryCode("abc"));
        Assert.Null(Totp.NormalizeRecoveryCode("abcde-2345!"));
        Assert.Equal(Totp.HashRecoveryCode("abcde23456"), Totp.HashRecoveryCode(Totp.NormalizeRecoveryCode("abcde 23456")!));
    }

    [Fact]
    public void Setup_uri_and_grouped_secret_are_app_compatible()
    {
        var uri = Totp.BuildUri("فراتاک", "09120000009", "GEZDGNBV");
        Assert.StartsWith("otpauth://totp/", uri);
        Assert.Contains("secret=GEZDGNBV", uri);
        Assert.Contains("digits=6", uri);
        Assert.Contains("period=30", uri);
        Assert.DoesNotContain("فراتاک", uri); // نام فارسی باید کدگذاری شود

        Assert.Equal("GEZD GNBV GY", StaffTotpService.GroupSecret("GEZDGNBVGY"));
        Assert.Contains("<svg", QrCodes.Svg(uri));
    }

    // ═══ منشن‌ها ═══════════════════════════════════════════════════════

    private static readonly StaffMention Sara = new(Guid.NewGuid(), "سارا محمدی", Mentions.HandleFor("سارا محمدی"));
    private static readonly StaffMention Reza = new(Guid.NewGuid(), "رضا کریمی", Mentions.HandleFor("رضا کریمی"));

    [Fact]
    public void Mentions_normalize_persian_variants_and_spaces()
    {
        Assert.Equal("سارا_محمدی", Mentions.Normalize("سارا  محمدي"));
        Assert.Equal("سارا_محمدی", Mentions.Normalize("@سارا\u200cمحمدی"));
        Assert.Equal("ali_rezaei", Mentions.Normalize("Ali Rezaei"));
        Assert.Equal("سارا_محمدی", Mentions.HandleFor("سارا  محمدی"));
        Assert.Equal(string.Empty, Mentions.Normalize("   "));
    }

    [Fact]
    public void Mentions_find_each_known_colleague_once()
    {
        var directory = new[] { Sara, Reza };

        var found = Mentions.Find("@سارا_محمدی و @رضا لطفاً بررسی کنید. باز هم @سارا_محمدي،", directory);
        Assert.Equal(Sara.UserId, Assert.Single(found).UserId); // «@رضا» کامل نیست؛ سارا یک بار

        Assert.Empty(Mentions.Find("ایمیل مشتری ali@رضا_کریمی.com است", directory)); // ایمیل منشن نیست
        Assert.Empty(Mentions.Find("بدون منشن", directory));
        Assert.Equal(2, Mentions.Find("@رضا_کریمی @سارا_محمدی", directory).Count);
    }

    [Fact]
    public void Mention_highlighter_encodes_html_before_highlighting()
    {
        var html = MentionHighlighter.Render("<b>سلام</b> @سارا_محمدی <script>x</script>").Value!;

        Assert.Contains("<span class=\"a-mention\">@سارا_محمدی</span>", html);
        Assert.DoesNotContain("<b>", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Equal(string.Empty, MentionHighlighter.Render(null).Value);
        Assert.DoesNotContain("a-mention", MentionHighlighter.Render("ali@site.com").Value);
    }

    // ═══ پوشاندن اطلاعات ═══════════════════════════════════════════════

    [Fact]
    public void Masking_hides_the_sensitive_middle()
    {
        Assert.Equal("0912•••4567", Masking.Phone("09121234567"));
        Assert.Equal(Masking.Dots, Masking.Phone("123"));
        Assert.Equal(string.Empty, Masking.Phone(null));

        Assert.Equal("a•••@gmail.com", Masking.Email("ali.rezaei@gmail.com"));
        Assert.Equal(Masking.Dots, Masking.Email("no-at-sign"));

        Assert.Equal("خیابان ولیعصر، •••", Masking.Address("خیابان ولیعصر، کوچه نیلوفر، پلاک ۱۲، واحد ۳"));
        Assert.DoesNotContain("پلاک", Masking.Address("تهران خیابان آزادی پلاک ۵"));

        Assert.Equal("IR12 ••• 9012", Masking.Iban("IR12 0570 0000 0012 3456 7890 12"));
        Assert.Equal("6037 ••• 7890", Masking.Card("6037-9912-3456-7890"));
        Assert.Equal(Masking.Dots, Masking.Card("1234"));
    }

    // ═══ کارها و تأییدها ═══════════════════════════════════════════════

    [Theory]
    [InlineData("/admin/orders/5", "/admin/orders/5")]
    [InlineData("https://shop.example/admin/orders?status=1#notes", "/admin/orders?status=1#notes")]
    [InlineData("https://evil.com", null)]
    [InlineData("//evil.com/admin", null)]
    [InlineData("/products/1", null)]
    [InlineData("/admin\\..\\x", null)]
    [InlineData("", null)]
    public void Task_links_stay_inside_the_panel(string input, string? expected) =>
        Assert.Equal(expected, StaffTaskService.CleanUrl(input));

    [Theory]
    [InlineData("/admin/customers/1#wallet", "/admin/customers/1#wallet")]
    [InlineData("https://evil.com", "/admin")]
    [InlineData("//evil.com", "/admin")]
    [InlineData("/admin/reauth?returnUrl=x", "/admin")]
    [InlineData("/admin\\evil", "/admin")]
    [InlineData(null, "/admin")]
    public void Reauth_returns_only_to_panel_pages(string? input, string expected) =>
        Assert.Equal(expected, ReauthModel.SafeReturnUrl(input));

    [Fact]
    public void Labels_cover_every_status_and_priority()
    {
        foreach (var status in Enum.GetValues<ApprovalStatus>())
        {
            var (text, css) = ApprovalsModel.StatusLabel(new ApprovalRequest { Status = status, ExpiresAt = DateTimeOffset.UtcNow.AddHours(1) });
            Assert.False(string.IsNullOrWhiteSpace(text));
            Assert.StartsWith("a-badge-", css);
        }

        // درخواست «در انتظار» که مهلتش گذشته، منقضی نمایش داده می‌شود (حتی پیش از اجرای کار پس‌زمینه).
        Assert.Equal("منقضی", ApprovalsModel.StatusLabel(new ApprovalRequest { Status = ApprovalStatus.Pending, ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) }).Text);

        foreach (var kind in Enum.GetValues<ApprovalKind>())
            Assert.NotEqual(kind.ToString(), ApprovalsModel.KindLabel(kind));

        Assert.Equal("فوری", TasksModel.PriorityLabel(StaffTaskPriority.High).Text);
        Assert.Equal("عادی", TasksModel.PriorityLabel(StaffTaskPriority.Normal).Text);
        Assert.Equal("کم", TasksModel.PriorityLabel(StaffTaskPriority.Low).Text);
    }

    [Fact]
    public void Due_label_flags_overdue_tasks()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal("بدون موعد", TasksModel.DueLabel(new StaffTask { Title = "x" }, now).Text);
        Assert.StartsWith("دیر شده", TasksModel.DueLabel(new StaffTask { Title = "x", DueAt = now.AddHours(-1) }, now).Text);
        Assert.Equal("a-muted", TasksModel.DueLabel(new StaffTask { Title = "x", DueAt = now.AddHours(-1), IsDone = true }, now).Css);
        Assert.Equal("text-amber-600", TasksModel.DueLabel(new StaffTask { Title = "x", DueAt = now.AddHours(3) }, now).Css);
    }

    // ═══ تأیید دوباره ══════════════════════════════════════════════════

    [Fact]
    public void Reauth_ticket_is_bound_to_the_staff_member_and_tamper_proof()
    {
        var ticket = new ReauthTicket(new EphemeralDataProtectionProvider());
        var userId = Guid.NewGuid();

        var issuing = new DefaultHttpContext();
        ticket.Issue(issuing, userId);
        var setCookie = issuing.Response.Headers.SetCookie.ToString();
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/admin", setCookie, StringComparison.OrdinalIgnoreCase);
        var value = setCookie.Split(';')[0][(ReauthTicket.CookieName.Length + 1)..];

        HttpContext WithCookie(string cookie)
        {
            var context = new DefaultHttpContext();
            context.Request.Headers.Cookie = $"{ReauthTicket.CookieName}={cookie}";
            return context;
        }

        Assert.True(ticket.IsFresh(WithCookie(value), userId, 15));
        Assert.False(ticket.IsFresh(WithCookie(value), Guid.NewGuid(), 15)); // کارمند دیگر
        Assert.False(ticket.IsFresh(WithCookie(value[..^4] + "AAAA"), userId, 15)); // دست‌کاری‌شده
        Assert.False(ticket.IsFresh(new DefaultHttpContext(), userId, 15)); // بدون کوکی
    }

    [Theory]
    [InlineData(typeof(Fratak_Shop.Web.Areas.Admin.Pages.Customers.DetailModel), "OnPostWalletAsync")]
    [InlineData(typeof(Fratak_Shop.Web.Areas.Admin.Pages.Customers.DetailModel), "OnPostSessionsAsync")]
    [InlineData(typeof(Fratak_Shop.Web.Areas.Admin.Pages.Customers.IndexModel), "OnGetExportAsync")]
    [InlineData(typeof(Fratak_Shop.Web.Areas.Admin.Pages.Finance.WithdrawalsModel), "OnPostStatusAsync")]
    [InlineData(typeof(Fratak_Shop.Web.Areas.Admin.Pages.Staff.MemberModel), "OnPostAsync")]
    [InlineData(typeof(Fratak_Shop.Web.Areas.Admin.Pages.Staff.MemberModel), "OnPostOwnerAsync")]
    [InlineData(typeof(Fratak_Shop.Web.Areas.Admin.Pages.Staff.MemberModel), "OnPostResetTotpAsync")]
    [InlineData(typeof(Fratak_Shop.Web.Areas.Admin.Pages.Staff.RoleModel), "OnPostAsync")]
    [InlineData(typeof(Fratak_Shop.Web.Areas.Admin.Pages.Staff.RoleModel), "OnPostDeleteAsync")]
    [InlineData(typeof(Fratak_Shop.Web.Areas.Admin.Pages.SystemPages.BackupsModel), "OnPostDeleteAsync")]
    [InlineData(typeof(Fratak_Shop.Web.Areas.Admin.Pages.SystemPages.BackupsModel), "OnGetDownloadAsync")]
    [InlineData(typeof(ApprovalsModel), "OnPostDecideAsync")]
    public void Sensitive_handlers_require_reauthentication(Type pageType, string handler)
    {
        var method = pageType.GetMethod(handler, BindingFlags.Public | BindingFlags.Instance);
        Assert.NotNull(method);
        Assert.True(AdminPageFilter.RequiresReauth(pageType, method), $"{pageType.Name}.{handler}");
    }

    [Fact]
    public void Everyday_handlers_do_not_require_reauthentication()
    {
        var type = typeof(Fratak_Shop.Web.Areas.Admin.Pages.Customers.DetailModel);
        Assert.False(AdminPageFilter.RequiresReauth(type, type.GetMethod("OnPostPointsAsync")));
        Assert.False(AdminPageFilter.RequiresReauth(type, type.GetMethod("OnGetAsync")));
    }

    [Fact]
    public void Setup_and_reauth_pages_stay_reachable_when_totp_is_mandatory()
    {
        Assert.NotNull(typeof(AccountModel).GetCustomAttribute<AllowWithoutTotpAttribute>());
        Assert.NotNull(typeof(ReauthModel).GetCustomAttribute<AllowWithoutTotpAttribute>());
        Assert.Null(typeof(TasksModel).GetCustomAttribute<AllowWithoutTotpAttribute>());
    }
}
