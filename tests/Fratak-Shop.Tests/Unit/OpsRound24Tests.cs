using Fratak_Shop.Core.Services.Ops;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Support;
using Fratak_Shop.Data.Entities.Ops;
using Fratak_Shop.Data.Entities.Orders;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست واحد دور ۲۴ بستهٔ F (منطق خالص، بدون دیتابیس):
/// <list type="bullet">
/// <item>۲۷ پیشنهاد پاسخ تیکت: تشخیص موضوع، متن وضعیت سفارش، پیدا کردن شمارهٔ سفارش در متن، پوشاندن اطلاعات مشتری قبلی و شباهت متن.</item>
/// <item>۷۳ دسترس‌پذیری: درصد با ادغام قطعی‌های هم‌پوشان، تشخیص قطعی از فایل ضربان (خاموشی عادی ≠ قطعی) و متن مدت.</item>
/// <item>۷۴ گزارش امنیتی: امتیاز، رتبه، وضعیت پشتیبان، حملهٔ حدس رمز و متن اعلان هفتگی.</item>
/// <item>۶۹ ایمیل سفارش خرید به تأمین‌کننده: فقط نام، کد و تعداد؛ هرگز قیمت خرید یا آمار داخلی.</item>
/// </list>
/// برای متن فارسی همیشه مقایسهٔ Ordinal (فرهنگ fa-IR روی Contains اثر می‌گذارد).
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Round24Ops")]
public class OpsRound24Tests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 10, 14, 0, TimeSpan.Zero);

    // ── ۲۷: پیشنهاد پاسخ تیکت ─────────────────────────────────────────

    [Theory]
    [InlineData("می‌خوام این کالا رو مرجوع کنم و پولم واریز بشه", TicketTopic.Return)]
    [InlineData("پول از حسابم کم شد ولی سفارش ثبت نشد", TicketTopic.Payment)]
    [InlineData("کد رهگیری مرسوله رو ندارم", TicketTopic.Shipping)]
    [InlineData("رمز عبورم یادم رفته", TicketTopic.Account)]
    [InlineData("سفارشم کجاست؟", TicketTopic.OrderStatus)]
    [InlineData("این هدفون اصله؟ گارانتی داره؟", TicketTopic.Product)]
    [InlineData("سلام وقت بخیر", TicketTopic.General)]
    [InlineData("", TicketTopic.General)]
    [InlineData(null, TicketTopic.General)]
    public void DetectTopic_follows_priority_order(string? text, TicketTopic expected)
        => Assert.Equal(expected, TicketReplyAdvisor.DetectTopic(text));

    [Fact]
    public void TopicTitle_is_persian_for_every_topic()
    {
        foreach (var topic in Enum.GetValues<TicketTopic>())
            Assert.False(string.IsNullOrWhiteSpace(TicketReplyAdvisor.TopicTitle(topic)));
        Assert.Equal("ارسال و رهگیری", TicketReplyAdvisor.TopicTitle(TicketTopic.Shipping));
    }

    [Fact]
    public void OrderReply_uses_real_status_tracking_code_and_greeting_tag()
    {
        var shipped = TicketReplyAdvisor.OrderReply("FR-1024", OrderStatus.Shipped, " 1234567890 ", Now, null);
        Assert.StartsWith("سلام {FirstName} عزیز،", shipped, StringComparison.Ordinal);
        Assert.Contains("FR-1024", shipped, StringComparison.Ordinal);
        Assert.Contains("کد رهگیری مرسوله: 1234567890", shipped, StringComparison.Ordinal);
        Assert.Contains("ارسال شده", shipped, StringComparison.Ordinal);

        var noCode = TicketReplyAdvisor.OrderReply("FR-1", OrderStatus.Shipped, null, null, null);
        Assert.DoesNotContain("کد رهگیری", noCode, StringComparison.Ordinal);

        Assert.Contains("کیف پول", TicketReplyAdvisor.OrderReply("FR-2", OrderStatus.Cancelled, null, null, null), StringComparison.Ordinal);
        Assert.Contains("در حال", TicketReplyAdvisor.OrderReply("FR-3", OrderStatus.Processing, null, null, null), StringComparison.Ordinal);
        Assert.Contains("تحویل شده", TicketReplyAdvisor.OrderReply("FR-4", OrderStatus.Delivered, null, null, Now), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("سفارش ۱۰۲۴ من چی شد؟", "FR-1024")]
    [InlineData("سفارش fr1024 رسید؟", "FR-1024")]
    [InlineData("سفارش FR-2050", "FR-2050")]
    [InlineData("سفارش 10245", null)]       // عدد بلندتر ≠ همان سفارش
    [InlineData("سفارش 99999", null)]       // سفارش مشتری دیگر هرگز
    [InlineData("سلام", null)]
    [InlineData(null, null)]
    public void MentionedOrder_matches_only_this_customers_orders(string? text, string? expected)
        => Assert.Equal(expected, TicketReplyAdvisor.MentionedOrder(text, ["FR-1024", "FR-2050"]));

    [Fact]
    public void Anonymize_hides_previous_customer_name_and_long_numbers()
    {
        var result = TicketReplyAdvisor.Anonymize("سلام علی رضایی، کد رهگیری 123456789 است. تلفن ۰۹۱۲۳۴۵۶۷۸۹", "علی", "رضایی");
        Assert.Equal("سلام {FirstName}، کد رهگیری … است. تلفن …", result);
    }

    [Fact]
    public void Anonymize_replaces_whole_words_only()
    {
        // «علی» داخل «فعلی» یا «عالی» نباید عوض شود.
        var result = TicketReplyAdvisor.Anonymize("علی جان، وضعیت فعلی سفارش عالی است.", "علی", null);
        Assert.Equal("{FirstName} جان، وضعیت فعلی سفارش عالی است.", result);
        // نام یک‌حرفی یا خالی دست نمی‌خورد؛ عدد کوتاه (۴ رقم) هم می‌ماند.
        Assert.Equal("سلام ع، کد 1234", TicketReplyAdvisor.Anonymize("سلام ع، کد 1234", "ع", ""));
    }

    [Fact]
    public void Similarity_is_symmetric_bounded_and_meaningful()
    {
        const string a = "کد رهگیری سفارشم هنوز نیومده";
        const string b = "کد رهگیری سفارشم هنوز نیامده";
        const string unrelated = "رنگ آبی این کفش موجود است";
        var same = TicketReplyAdvisor.Similarity(a, a);
        Assert.InRange(same, 0.99, 1.0001);
        Assert.Equal(TicketReplyAdvisor.Similarity(a, b), TicketReplyAdvisor.Similarity(b, a), 6);
        Assert.True(TicketReplyAdvisor.Similarity(a, b) >= TicketReplyAdvisor.MinSimilarTicket);
        Assert.True(TicketReplyAdvisor.Similarity(a, unrelated) < TicketReplyAdvisor.MinSimilarTicket);
        Assert.Equal(0d, TicketReplyAdvisor.Similarity(null, a));
        Assert.Equal(0d, TicketReplyAdvisor.Similarity("", ""));
        // HTML تیکت (مثلاً از ویرایشگر) خوانده می‌شود، نه برچسب‌ها.
        Assert.True(TicketReplyAdvisor.Similarity("<p>کد رهگیری سفارشم هنوز نیومده</p>", a) > 0.9);
    }

    // ── ۷۳: دسترس‌پذیری ───────────────────────────────────────────────

    private static (DateTimeOffset Start, DateTimeOffset? End) Outage(DateTimeOffset start, int minutes) => (start, start.AddMinutes(minutes));

    [Fact]
    public void Percent_for_one_incident_in_a_day()
    {
        var from = Now.AddDays(-1);
        Assert.Equal(90d, UptimeRules.Percent([Outage(Now.AddHours(-5), 144)], from, Now));
        Assert.Equal(100d, UptimeRules.Percent([], from, Now));
    }

    [Fact]
    public void Overlapping_incidents_are_merged_not_double_counted()
    {
        var ten = new DateTimeOffset(2026, 10, 22, 10, 0, 0, TimeSpan.Zero);
        var spans = new List<(DateTimeOffset, DateTimeOffset?)> { Outage(ten, 30), Outage(ten.AddMinutes(20), 20), Outage(ten.AddMinutes(5), 5) };
        Assert.Equal(2400d, UptimeRules.DownSeconds(spans, ten.AddHours(-1), ten.AddHours(1)));
    }

    [Fact]
    public void Incidents_are_clipped_to_the_window_and_open_ones_count_until_now()
    {
        var from = Now.AddHours(-1);
        // شروع پیش از بازه ← فقط بخش داخل بازه.
        Assert.Equal(600d, UptimeRules.DownSeconds([Outage(from.AddMinutes(-50), 60)], from, Now));
        // رخداد باز (پایان null) ← تا «اکنون».
        Assert.Equal(1800d, UptimeRules.DownSeconds([(Now.AddMinutes(-30), (DateTimeOffset?)null)], from, Now));
        // کاملاً بیرون بازه ← هیچ.
        Assert.Equal(0d, UptimeRules.DownSeconds([Outage(from.AddHours(-5), 30)], from, Now));
        // بازهٔ خراب ← ۱۰۰٪ (نه تقسیم بر صفر).
        Assert.Equal(100d, UptimeRules.Percent([Outage(Now, 10)], Now, Now));
        // قطعی سراسری ← ۰٪ (نه منفی).
        Assert.Equal(0d, UptimeRules.Percent([Outage(from.AddHours(-2), 600)], from, Now));
    }

    [Fact]
    public void Heartbeat_gap_detects_crashes_but_not_normal_stops()
    {
        var gap = UptimeRules.GapFromHeartbeat("beat|2026-10-22T10:00:00+00:00", Now);
        Assert.NotNull(gap);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 10, 0, 0, TimeSpan.Zero), gap.Value.Start);
        Assert.Equal(Now, gap.Value.End);

        Assert.Null(UptimeRules.GapFromHeartbeat("stopped|2026-10-22T10:00:00+00:00", Now));   // خاموشی عادی IIS
        Assert.Null(UptimeRules.GapFromHeartbeat("beat|2026-10-22T10:12:00+00:00", Now));      // ۲ دقیقه = ری‌استارت
        Assert.Null(UptimeRules.GapFromHeartbeat("beat|2026-10-22T11:00:00+00:00", Now));      // آینده (ساعت جابه‌جا)
        Assert.Null(UptimeRules.GapFromHeartbeat("beat|2026-09-01T10:00:00+00:00", Now));      // بیش از ۳۰ روز
        Assert.Null(UptimeRules.GapFromHeartbeat("beat|not-a-date", Now));
        Assert.Null(UptimeRules.GapFromHeartbeat("garbage", Now));
        Assert.Null(UptimeRules.GapFromHeartbeat(null, Now));
        Assert.Null(UptimeRules.GapFromHeartbeat("   ", Now));
    }

    [Fact]
    public void Duration_and_alert_titles_are_persian()
    {
        Assert.Equal("۱۴ دقیقه", UptimeRules.Duration(TimeSpan.FromMinutes(14)));
        Assert.Equal("۱ ساعت و ۳۵ دقیقه", UptimeRules.Duration(TimeSpan.FromMinutes(95)));
        Assert.Equal("۲ ساعت", UptimeRules.Duration(TimeSpan.FromMinutes(120)));
        Assert.Equal("سایت ۱۴ دقیقه در دسترس نبود", UptimeRules.AlertTitle(UptimeIncidentKind.Downtime, TimeSpan.FromMinutes(14)));
        Assert.StartsWith("پایگاه داده", UptimeRules.AlertTitle(UptimeIncidentKind.DatabaseDown, TimeSpan.FromMinutes(5)), StringComparison.Ordinal);
    }

    // ── ۷۴: گزارش امنیتی ──────────────────────────────────────────────

    private static SecurityCheck Check(SecurityCheckLevel level, string title = "مورد") => new("k", title, level, "جزئیات", null);

    [Fact]
    public void Score_subtracts_by_severity_and_is_clamped()
    {
        Assert.Equal(100, SecurityReportRules.Score([]));
        Assert.Equal(88, SecurityReportRules.Score([Check(SecurityCheckLevel.Warning), Check(SecurityCheckLevel.Info), Check(SecurityCheckLevel.Ok)]));
        Assert.Equal(75, SecurityReportRules.Score([Check(SecurityCheckLevel.Critical)]));
        Assert.Equal(0, SecurityReportRules.Score(Enumerable.Repeat(Check(SecurityCheckLevel.Critical), 6)));
    }

    [Theory]
    [InlineData(100, "عالی")]
    [InlineData(90, "عالی")]
    [InlineData(89, "خوب")]
    [InlineData(75, "خوب")]
    [InlineData(74, "نیاز به توجه")]
    [InlineData(50, "نیاز به توجه")]
    [InlineData(49, "ضعیف")]
    [InlineData(0, "ضعیف")]
    public void Grade_boundaries(int score, string expected) => Assert.Equal(expected, SecurityReportRules.Grade(score));

    [Fact]
    public void Backup_check_warns_when_missing_or_older_than_two_days()
    {
        Assert.Equal(SecurityCheckLevel.Warning, SecurityReportRules.Backup(null, Now).Level);
        Assert.Equal(SecurityCheckLevel.Warning, SecurityReportRules.Backup(Now.AddHours(-70), Now).Level);
        Assert.Equal(SecurityCheckLevel.Ok, SecurityReportRules.Backup(Now.AddHours(-48), Now).Level);
        Assert.Equal(SecurityCheckLevel.Ok, SecurityReportRules.Backup(Now.AddHours(-10), Now).Level);
        // ساعت جلوتر از اکنون (اختلاف ساعت سرور) ← منفی نشان داده نمی‌شود.
        Assert.DoesNotContain("-", SecurityReportRules.Backup(Now.AddHours(2), Now).Detail, StringComparison.Ordinal);
        Assert.Equal("/admin/system/backups", SecurityReportRules.Backup(null, Now).FixUrl);
    }

    [Fact]
    public void Brute_force_check_depends_on_volume_and_auto_block()
    {
        Assert.Equal(SecurityCheckLevel.Ok, SecurityReportRules.BruteForce([], false).Level);
        Assert.Equal(SecurityCheckLevel.Ok, SecurityReportRules.BruteForce([("5.6.7.8", 19)], false).Level);
        var warning = SecurityReportRules.BruteForce([("1.2.3.4", 40), ("5.6.7.8", 6)], false);
        Assert.Equal(SecurityCheckLevel.Warning, warning.Level);
        Assert.Contains("1.2.3.4", warning.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain("5.6.7.8", warning.Detail, StringComparison.Ordinal);
        Assert.Equal(SecurityCheckLevel.Info, SecurityReportRules.BruteForce([("1.2.3.4", 40)], true).Level);
    }

    [Fact]
    public void Weekly_body_lists_problems_or_says_all_clear()
    {
        var activity = new SecurityActivity(12, 0, 0, 0, 0, 0, []);
        var clean = new SecurityReport(Now, 100, "عالی", [Check(SecurityCheckLevel.Ok), Check(SecurityCheckLevel.Info, "نکته")], activity);
        var body = SecurityReportRules.WeeklyBody(clean);
        Assert.StartsWith("امتیاز ۱۰۰ از ۱۰۰ (عالی)", body, StringComparison.Ordinal);
        Assert.Contains("مورد مهمی پیدا نشد", body, StringComparison.Ordinal);
        Assert.Contains("۱۲", body, StringComparison.Ordinal);

        var bad = new SecurityReport(Now, 80, "خوب", [Check(SecurityCheckLevel.Warning, "رمز پیش‌فرض کارکنان"), Check(SecurityCheckLevel.Info, "نکته")], activity);
        var badBody = SecurityReportRules.WeeklyBody(bad);
        Assert.Contains("رمز پیش‌فرض کارکنان", badBody, StringComparison.Ordinal);
        Assert.DoesNotContain("نکته", badBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Digits_are_persian()
    {
        Assert.Equal("۴۲", SecurityReportRules.Digits(42));
        Assert.Equal("۰", SecurityReportRules.Digits(0));
        Assert.Equal("-۵", SecurityReportRules.Digits(-5));
    }

    // ── ۶۹: ایمیل سفارش خرید ──────────────────────────────────────────

    private static PurchaseDraftGroup Group(params PurchaseDraftLine[] lines) => new(Guid.NewGuid(), "پخش البرز", "02100000000", lines);

    private static PurchaseDraftLine Line(string name, string sku, int quantity, decimal? cost = 1_250_000m) =>
        new(Guid.NewGuid(), name, sku, 2, 1.5m, 1.3m, quantity, cost, "holt");

    [Fact]
    public void Purchase_email_lists_name_code_and_quantity_but_never_costs()
    {
        var (subject, body) = PurchaseOrderEmail.Build(Group(Line("هدفون X", "HX-1", 12), Line("کابل Y", "", 3)), "فراتک", "علی", "1405/08/01");
        Assert.Equal("درخواست خرید فراتک – 1405/08/01", subject);
        Assert.StartsWith("سلام علی عزیز،", body, StringComparison.Ordinal);
        Assert.Contains("۱) هدفون X (کد HX-1) – ۱۲ عدد", body, StringComparison.Ordinal);
        Assert.Contains("۲) کابل Y – ۳ عدد", body, StringComparison.Ordinal);
        Assert.Contains("جمع: ۲ قلم، ۱۵ عدد.", body, StringComparison.Ordinal);
        // اطلاعات تجاری داخلی (قیمت خرید، سرعت فروش، موجودی، روش پیش‌بینی) هرگز.
        Assert.DoesNotContain("1250000", body.Replace(",", "", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.DoesNotContain("۱٬۲۵۰٬۰۰۰", body, StringComparison.Ordinal);
        Assert.DoesNotContain("holt", body, StringComparison.Ordinal);
        Assert.DoesNotContain("موجودی ما", body, StringComparison.Ordinal);
    }

    [Fact]
    public void Purchase_email_skips_zero_lines_caps_size_and_handles_missing_names()
    {
        var many = Enumerable.Range(1, PurchaseOrderEmail.MaxLines + 20).Select(i => Line("کالا " + i, "S" + i, 1)).Append(Line("صفر", "Z", 0)).ToArray();
        var (subject, body) = PurchaseOrderEmail.Build(Group(many), "  ", null, "1405/08/01");
        Assert.Equal("درخواست خرید فروشگاه – 1405/08/01", subject);
        Assert.StartsWith("سلام عزیز،", body, StringComparison.Ordinal);
        Assert.DoesNotContain("صفر", body, StringComparison.Ordinal);
        Assert.Contains("جمع: ۱۰۰ قلم", body, StringComparison.Ordinal);
        Assert.DoesNotContain("کالا 101", body, StringComparison.Ordinal);
        Assert.Throws<ArgumentNullException>(() => PurchaseOrderEmail.Build(null!, "فراتک", null, "x"));
    }
}
