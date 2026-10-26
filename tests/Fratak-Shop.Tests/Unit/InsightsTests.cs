using System.Globalization;
using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Insights;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Insights;
using Fratak_Shop.Web.Areas.Admin.Pages.Reports;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Insights;
using MailKit.Security;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست قواعد خالص گزارش‌های مدیریتی (مرحلهٔ هفتم – فاز ۷ب): ماه شمسی، سررسید هزینه‌های تکراری، پیش‌بینی هدف فروش،
/// زمان‌بندی گزارش خودکار، اعتبارسنجی ایمیل/شناسهٔ گفت‌وگو/توکن ربات، قالب متن و HTML گزارش و چیدمان داشبورد.
/// </summary>
public class InsightsTests
{
    private static readonly PersianCalendar Persian = new();

    private static DateOnly Jalali(int year, int month, int day) => DateOnly.FromDateTime(Persian.ToDateTime(year, month, day, 0, 0, 0, 0));

    // ── ماه شمسی ─────────────────────────────────────────────────────────

    [Fact]
    public void JalaliMonth_of_and_range_cover_the_whole_month()
    {
        Assert.Equal((1405, 8), JalaliMonth.Of(new DateOnly(2026, 10, 26)));

        var (start, end) = JalaliMonth.Range(1405, 8);
        Assert.Equal(Jalali(1405, 8, 1), start);
        Assert.Equal(Jalali(1405, 9, 1), end);
        Assert.Equal(30, end.DayNumber - start.DayNumber);
        Assert.Equal((1405, 7), JalaliMonth.Of(start.AddDays(-1)));

        // نیمهٔ اول سال ۳۱ روزه است.
        var (s1, e1) = JalaliMonth.Range(1405, 1);
        Assert.Equal(31, e1.DayNumber - s1.DayNumber);
    }

    [Theory]
    [InlineData(1405, 12, 1, 1406, 1)]
    [InlineData(1405, 1, -1, 1404, 12)]
    [InlineData(1405, 8, -11, 1404, 9)]
    [InlineData(1405, 8, 0, 1405, 8)]
    [InlineData(1405, 8, 24, 1407, 8)]
    public void JalaliMonth_add_wraps_years(int year, int month, int add, int expectedYear, int expectedMonth) =>
        Assert.Equal((expectedYear, expectedMonth), JalaliMonth.Add(year, month, add));

    [Fact]
    public void JalaliMonth_title_and_validation()
    {
        Assert.Equal("آبان 1405", JalaliMonth.Title(1405, 8));
        Assert.True(JalaliMonth.IsValid(1405, 12));
        Assert.False(JalaliMonth.IsValid(1405, 13));
        Assert.False(JalaliMonth.IsValid(1405, 0));
        Assert.False(JalaliMonth.IsValid(999, 1));
    }

    // ── هزینه‌ها ─────────────────────────────────────────────────────────

    [Fact]
    public void NextDue_follows_the_persian_calendar()
    {
        // ۳۱ شهریور + یک ماه = ۳۰ مهر (مهر ۳۰ روزه است)
        Assert.Equal(Jalali(1405, 7, 30), ExpenseRules.NextDue(Jalali(1405, 6, 31), ExpenseRecurrence.Monthly));
        Assert.Equal(Jalali(1405, 9, 10), ExpenseRules.NextDue(Jalali(1405, 8, 10), ExpenseRecurrence.Monthly));
        Assert.Equal(Jalali(1406, 2, 10), ExpenseRules.NextDue(Jalali(1405, 2, 10), ExpenseRecurrence.Yearly));
        Assert.Null(ExpenseRules.NextDue(Jalali(1405, 2, 10), ExpenseRecurrence.None));
    }

    [Fact]
    public void NextDueAfter_skips_past_periods_but_never_returns_a_past_date()
    {
        var today = Jalali(1405, 4, 20);
        Assert.Equal(Jalali(1405, 5, 5), ExpenseService.NextDueAfter(Jalali(1405, 1, 5), ExpenseRecurrence.Monthly, today));
        // سررسید دقیقاً امروز همان امروز است (گذشته حساب نمی‌شود).
        Assert.Equal(today, ExpenseService.NextDueAfter(Jalali(1405, 3, 20), ExpenseRecurrence.Monthly, today));
        Assert.Equal(Jalali(1405, 12, 1), ExpenseService.NextDueAfter(Jalali(1404, 12, 1), ExpenseRecurrence.Yearly, today));
        Assert.Null(ExpenseService.NextDueAfter(Jalali(1405, 1, 5), ExpenseRecurrence.None, today));
    }

    private static ExpenseInput Expense(string title = "تمدید هاست", decimal amount = 1_200_000, ExpenseCategory category = ExpenseCategory.Hosting,
        DateOnly? spentOn = null, ExpenseRecurrence recurrence = ExpenseRecurrence.Yearly) =>
        new(null, title, category, amount, spentOn ?? new DateOnly(2026, 10, 1), "پارس‌پک", null, recurrence);

    [Fact]
    public void Expense_validation_rejects_bad_input()
    {
        var today = new DateOnly(2026, 10, 26);
        Assert.Null(ExpenseRules.Validate(Expense(), today));
        Assert.NotNull(ExpenseRules.Validate(Expense(title: " "), today));
        Assert.NotNull(ExpenseRules.Validate(Expense(title: "a"), today));
        Assert.NotNull(ExpenseRules.Validate(Expense(amount: 0), today));
        Assert.NotNull(ExpenseRules.Validate(Expense(amount: -5), today));
        Assert.NotNull(ExpenseRules.Validate(Expense(amount: 1000.5m), today));
        Assert.NotNull(ExpenseRules.Validate(Expense(category: ExpenseCategory.None), today));
        Assert.NotNull(ExpenseRules.Validate(Expense(category: (ExpenseCategory)55), today));
        Assert.NotNull(ExpenseRules.Validate(Expense(recurrence: (ExpenseRecurrence)9), today));
        Assert.NotNull(ExpenseRules.Validate(Expense(spentOn: new DateOnly(1999, 1, 1)), today));
        Assert.NotNull(ExpenseRules.Validate(Expense(spentOn: today.AddDays(400)), today));
    }

    [Fact]
    public void Every_expense_category_has_a_persian_title()
    {
        Assert.Equal(ExpenseRules.Categories.Length, ExpenseRules.Categories.Distinct().Count());
        Assert.DoesNotContain(ExpenseCategory.None, ExpenseRules.Categories);
        foreach (var category in Enum.GetValues<ExpenseCategory>().Where(c => c != ExpenseCategory.None))
        {
            Assert.Contains(category, ExpenseRules.Categories);
            Assert.False(string.IsNullOrWhiteSpace(ExpenseRules.CategoryTitle(category)));
        }
        Assert.Equal("ماهانه", ExpenseRules.RecurrenceTitle(ExpenseRecurrence.Monthly));
        Assert.Equal("یک‌باره", ExpenseRules.RecurrenceTitle(ExpenseRecurrence.None));
    }

    // ── هدف فروش ─────────────────────────────────────────────────────────

    [Fact]
    public void Goal_forecast_matches_the_documented_example()
    {
        var f = SalesGoalRules.Compute(300_000_000, 120_000_000, 10, 30);
        Assert.Equal(40m, f.Percent);
        Assert.Equal(360_000_000m, f.Projected);
        Assert.Equal(9_000_000m, f.RequiredPerDay);
        Assert.Equal(100_000_000m, f.ExpectedByNow);
        Assert.Equal("ahead", f.Status);
    }

    [Theory]
    [InlineData(300_000_000d, 300_000_000d, 20, 30, "done")]
    [InlineData(300_000_000d, 101_000_000d, 10, 30, "on-track")]
    [InlineData(300_000_000d, 50_000_000d, 10, 30, "behind")]
    [InlineData(300_000_000d, 0d, 0, 30, "on-track")]
    public void Goal_status(double target, double achieved, int passed, int days, string status) =>
        Assert.Equal(status, SalesGoalRules.Compute((decimal)target, (decimal)achieved, passed, days).Status);

    [Fact]
    public void Goal_without_target_still_projects_sales()
    {
        var f = SalesGoalRules.Compute(null, 10_000_000, 5, 30);
        Assert.Equal("none", f.Status);
        Assert.Equal(60_000_000m, f.Projected);
        Assert.Equal(0m, f.Percent);

        // ماه تمام شده و هدف نرسیده: همهٔ باقی‌مانده «لازم» است.
        var late = SalesGoalRules.Compute(100, 40, 30, 30);
        Assert.Equal(60m, late.RequiredPerDay);
        Assert.Equal("behind", late.Status);
    }

    [Fact]
    public void Goal_days_passed_includes_today()
    {
        var start = new DateOnly(2026, 10, 23);
        var end = start.AddDays(30);
        Assert.Equal(0, SalesGoalRules.DaysPassed(start, end, start.AddDays(-1)));
        Assert.Equal(1, SalesGoalRules.DaysPassed(start, end, start));
        Assert.Equal(4, SalesGoalRules.DaysPassed(start, end, start.AddDays(3)));
        Assert.Equal(30, SalesGoalRules.DaysPassed(start, end, end));
        Assert.Equal("هدف محقق شد", SalesGoalRules.StatusTitle("done"));
        Assert.Equal("بدون هدف", SalesGoalRules.StatusTitle("??"));
    }

    // ── زمان‌بندی گزارش خودکار ──────────────────────────────────────────

    private static readonly DateOnly Monday = new(2026, 10, 26);

    [Fact]
    public void Daily_report_runs_today_if_the_hour_has_not_passed_otherwise_tomorrow()
    {
        var at7 = IranClock.At(Monday, new TimeOnly(7, 0));
        var at9 = IranClock.At(Monday, new TimeOnly(9, 0));
        Assert.Equal(IranClock.At(Monday, new TimeOnly(8, 0)), ScheduledReportRules.NextRun(ReportFrequency.Daily, 8, 0, at7));
        Assert.Equal(IranClock.At(Monday.AddDays(1), new TimeOnly(8, 0)), ScheduledReportRules.NextRun(ReportFrequency.Daily, 8, 0, at9));
        // دقیقاً سر ساعت: نوبت بعدی (هرگز همان لحظه، تا دوبار اجرا نشود).
        var at8 = IranClock.At(Monday, new TimeOnly(8, 0));
        Assert.True(ScheduledReportRules.NextRun(ReportFrequency.Daily, 8, 0, at8) > at8);
    }

    [Fact]
    public void Weekly_report_runs_on_the_chosen_iranian_weekday()
    {
        Assert.Equal(DayOfWeek.Monday, Monday.DayOfWeek);
        Assert.Equal(2, ScheduledReportRules.IranWeekDay(Monday)); // شنبه=۰، یکشنبه=۱، دوشنبه=۲
        Assert.Equal(6, ScheduledReportRules.IranWeekDay(new DateOnly(2026, 10, 30))); // جمعه

        var now = IranClock.At(Monday, new TimeOnly(10, 0));
        var saturday = ScheduledReportRules.NextRun(ReportFrequency.Weekly, 8, 0, now);
        Assert.Equal(IranClock.At(new DateOnly(2026, 10, 31), new TimeOnly(8, 0)), saturday);

        // همان روز ولی ساعت گذشته → هفتهٔ بعد
        Assert.Equal(IranClock.At(Monday.AddDays(7), new TimeOnly(8, 0)), ScheduledReportRules.NextRun(ReportFrequency.Weekly, 8, 2, now));
        // همان روز و ساعت نرسیده → امروز
        Assert.Equal(IranClock.At(Monday, new TimeOnly(18, 0)), ScheduledReportRules.NextRun(ReportFrequency.Weekly, 18, 2, now));
    }

    [Fact]
    public void Monthly_report_runs_on_the_first_day_of_the_next_jalali_month()
    {
        var now = IranClock.At(Monday, new TimeOnly(10, 0)); // ۴ آبان
        Assert.Equal(IranClock.At(Jalali(1405, 9, 1), new TimeOnly(9, 0)), ScheduledReportRules.NextRun(ReportFrequency.Monthly, 9, 0, now));

        var firstMorning = IranClock.At(Jalali(1405, 9, 1), new TimeOnly(6, 0));
        Assert.Equal(IranClock.At(Jalali(1405, 9, 1), new TimeOnly(9, 0)), ScheduledReportRules.NextRun(ReportFrequency.Monthly, 9, 0, firstMorning));

        var esfand = IranClock.At(Jalali(1405, 12, 15), new TimeOnly(10, 0));
        Assert.Equal(IranClock.At(Jalali(1406, 1, 1), new TimeOnly(9, 0)), ScheduledReportRules.NextRun(ReportFrequency.Monthly, 9, 0, esfand));
    }

    [Theory]
    [InlineData(ReportFrequency.Daily)]
    [InlineData(ReportFrequency.Weekly)]
    [InlineData(ReportFrequency.Monthly)]
    public void Next_run_is_always_in_the_future_and_within_one_period(ReportFrequency frequency)
    {
        var now = IranClock.At(Monday, new TimeOnly(23, 59));
        for (var i = 0; i < 400; i += 7)
        {
            var moment = now.AddDays(i).AddHours(-i % 24);
            for (var hour = 0; hour < 24; hour += 5)
            {
                var next = ScheduledReportRules.NextRun(frequency, hour, i % 7, moment);
                Assert.True(next > moment);
                Assert.True(next - moment <= TimeSpan.FromDays(frequency == ReportFrequency.Monthly ? 32 : frequency == ReportFrequency.Weekly ? 7 : 1));
                Assert.Equal(hour, IranClock.ToLocal(next).Hour);
            }
        }
    }

    [Fact]
    public void Report_window_covers_whole_iranian_days()
    {
        var runAt = IranClock.At(Monday, new TimeOnly(8, 0));

        var daily = ScheduledReportRules.Window(ReportFrequency.Daily, runAt);
        Assert.Equal(IranClock.StartOfDay(Monday.AddDays(-1)), daily.Current.From);
        Assert.Equal(IranClock.StartOfDay(Monday), daily.Current.To);
        Assert.Equal(IranClock.StartOfDay(Monday.AddDays(-2)), daily.Previous.From);
        Assert.StartsWith("یکشنبه", daily.Label);

        var weekly = ScheduledReportRules.Window(ReportFrequency.Weekly, runAt);
        Assert.Equal(IranClock.StartOfDay(Monday.AddDays(-7)), weekly.Current.From);
        Assert.Equal(IranClock.StartOfDay(Monday), weekly.Current.To);
        Assert.Equal(weekly.Current.From, weekly.Previous.To);

        var monthly = ScheduledReportRules.Window(ReportFrequency.Monthly, IranClock.At(Jalali(1405, 9, 1), new TimeOnly(9, 0)));
        Assert.Equal(IranClock.StartOfDay(Jalali(1405, 8, 1)), monthly.Current.From);
        Assert.Equal(IranClock.StartOfDay(Jalali(1405, 9, 1)), monthly.Current.To);
        Assert.Equal(IranClock.StartOfDay(Jalali(1405, 7, 1)), monthly.Previous.From);
        Assert.Equal("آبان 1405", monthly.Label);
    }

    // ── اعتبارسنجی ───────────────────────────────────────────────────────

    [Fact]
    public void Emails_are_split_deduplicated_and_validated()
    {
        var (valid, invalid) = ScheduledReportRules.ParseEmails("a@x.ir, b@x;  A@X.ir\nowner@fratak.ir، bad@ <script>@x.ir");
        Assert.Equal("a@x.ir|owner@fratak.ir", string.Join("|", valid));
        Assert.Contains("b@x", invalid);
        Assert.Contains("bad@", invalid);
        Assert.Contains("<script>@x.ir", invalid);

        Assert.Empty(ScheduledReportRules.ParseEmails(null).Valid);
        Assert.Empty(ScheduledReportRules.ParseEmails("   ").Invalid);
    }

    [Theory]
    [InlineData("-1001234567890", true)]
    [InlineData("123456789", true)]
    [InlineData("@fratak_reports", true)]
    [InlineData(" @fratak ", true)]
    [InlineData("@ab", false)]
    [InlineData("@1channel", false)]
    [InlineData("fratak", false)]
    [InlineData("12 34", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Chat_id_validation(string? value, bool expected) => Assert.Equal(expected, ScheduledReportRules.IsValidChatId(value));

    private static ScheduledReportInput Report(ReportChannels channels = ReportChannels.Email, string? email = "owner@fratak.ir",
        ReportSections sections = ReportSections.Sales, string? bale = null, ReportFrequency frequency = ReportFrequency.Daily, int hour = 8, string title = "خلاصهٔ روزانه") =>
        new(null, title, frequency, hour, 0, channels, sections, email, bale, null, true);

    [Fact]
    public void Report_validation()
    {
        Assert.Null(ScheduledReportRules.Validate(Report()));
        Assert.Null(ScheduledReportRules.Validate(Report(ReportChannels.Bale, null, bale: "-100123")));
        Assert.NotNull(ScheduledReportRules.Validate(Report(title: "x")));
        Assert.NotNull(ScheduledReportRules.Validate(Report(ReportChannels.None)));
        Assert.NotNull(ScheduledReportRules.Validate(Report(sections: ReportSections.None)));
        Assert.NotNull(ScheduledReportRules.Validate(Report(email: "")));
        Assert.NotNull(ScheduledReportRules.Validate(Report(email: "owner@fratak.ir, nope")));
        Assert.NotNull(ScheduledReportRules.Validate(Report(email: string.Join(",", Enumerable.Range(1, 11).Select(i => $"u{i}@fratak.ir")))));
        Assert.NotNull(ScheduledReportRules.Validate(Report(ReportChannels.Bale, null, bale: "bad id")));
        Assert.NotNull(ScheduledReportRules.Validate(Report(frequency: ReportFrequency.None)));
        Assert.NotNull(ScheduledReportRules.Validate(Report(hour: 24)));
    }

    [Theory]
    [InlineData("123456789:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw", true)]
    [InlineData("1234567890:AbC-xyz_0123456789", true)]
    [InlineData("123456789", false)]
    [InlineData("abc:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw", false)]
    [InlineData("123456789:short", false)]
    [InlineData("123456789:AAHdqTcvCH1vGWJx fSeofSAs0K5P", false)]
    public void Bot_token_format(string token, bool expected) => Assert.Equal(expected, ScheduledReportService.IsBotToken(token));

    // ── قالب اعداد و متن گزارش ──────────────────────────────────────────

    [Fact]
    public void Numbers_are_written_with_persian_digits_and_separators()
    {
        Assert.Equal("۱٬۲۵۰٬۰۰۰", ScheduledReportRules.Number(1_250_000));
        Assert.Equal("۰", ScheduledReportRules.Number(0));
        Assert.Equal("۱٬۲۵۰٬۰۰۰ تومان", ScheduledReportRules.Toman(1_250_000));
        Assert.Equal("▲ ۲۰٪", ScheduledReportRules.Change(120, 100));
        Assert.Equal("▼ ۲۰٪", ScheduledReportRules.Change(80, 100));
        Assert.Equal("▲ ۱۲٫۵٪", ScheduledReportRules.Change(112.5m, 100));
        Assert.Equal("جدید", ScheduledReportRules.Change(5, 0));
        Assert.Equal("بدون تغییر", ScheduledReportRules.Change(0, 0));
        Assert.Equal("بدون تغییر", ScheduledReportRules.Change(100, 100));
        Assert.Equal("۱۴۰۵/۰۸/۰۴", ScheduledReportRules.DateText(Monday));
    }

    private static ReportDigest Digest(string value) => new()
    {
        Title = "خلاصهٔ <روزانه>",
        ShopName = "فروشگاه & کالا",
        PeriodLabel = "روزانه · یکشنبه",
        GeneratedAt = IranClock.At(Monday, new TimeOnly(8, 0)),
        Sections = [new DigestSection("فروش", [new DigestLine("فروش خالص", value, "▲ ۲۰٪"), new DigestLine("سفارش", "۵")])],
    };

    [Fact]
    public void Digest_html_is_encoded_and_text_is_plain()
    {
        var digest = Digest("<script>alert(1)</script>");
        var html = digest.ToHtml();
        Assert.Contains("dir=\"rtl\"", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("فروشگاه &amp; کالا", html);

        var text = digest.ToText();
        Assert.Contains("• فروش خالص: <script>alert(1)</script> (▲ ۲۰٪)", text);
        Assert.Contains("• سفارش: ۵", text);
        Assert.Contains("فروشگاه & کالا", text);
    }

    [Fact]
    public void Digest_text_is_capped_for_bots()
    {
        var text = Digest(new string('x', 10_000)).ToText();
        Assert.Equal(ScheduledReportRules.MaxBotMessageLength, text.Length);
        Assert.EndsWith("…", text);
    }

    [Fact]
    public void Section_titles_cover_every_section_flag_once()
    {
        var flags = Enum.GetValues<ReportSections>().Where(s => s is not ReportSections.None and not ReportSections.All).ToList();
        Assert.Equal(flags.OrderBy(f => f), ScheduledReportRules.SectionTitles.Select(s => s.Section).OrderBy(f => f));
        Assert.Equal(ReportSections.All, flags.Aggregate(ReportSections.None, (all, f) => all | f));
    }

    // ── ارسال (بدون شبکه) ───────────────────────────────────────────────

    [Fact]
    public void Bot_endpoints_and_smtp_socket_options()
    {
        Assert.Equal("https://tapi.bale.ai/bot1:x/sendMessage", ReportDelivery.BotEndpoint(ReportChannels.Bale, "1:x"));
        Assert.Equal("https://api.telegram.org/bot1:x/sendMessage", ReportDelivery.BotEndpoint(ReportChannels.Telegram, "1:x"));
        Assert.Throws<ArgumentOutOfRangeException>(() => ReportDelivery.BotEndpoint(ReportChannels.Email, "1:x"));

        Assert.Equal(SecureSocketOptions.SslOnConnect, ReportDelivery.SocketOptions(465));
        Assert.Equal(SecureSocketOptions.StartTls, ReportDelivery.SocketOptions(587));
        Assert.Equal(SecureSocketOptions.StartTlsWhenAvailable, ReportDelivery.SocketOptions(25));
    }

    // ── فرم گزارش و صفحه‌ها ─────────────────────────────────────────────

    [Fact]
    public void Report_form_combines_checkboxes_into_flags_and_round_trips()
    {
        var form = new ScheduledReportForm
        {
            Title = "گزارش",
            Channels = [ReportChannels.None, ReportChannels.Email, ReportChannels.Bale],
            Sections = [ReportSections.None, ReportSections.Sales, ReportSections.Goal, ReportSections.Sales],
        };
        Assert.Equal(ReportChannels.Email | ReportChannels.Bale, form.ChannelFlags);
        Assert.Equal(ReportSections.Sales | ReportSections.Goal, form.SectionFlags);

        var input = form.ToInput();
        Assert.Equal(form.ChannelFlags, input.Channels);
        Assert.Equal(form.SectionFlags, input.Sections);

        var row = new ScheduledReportRow(Guid.NewGuid(), "گزارش", ReportFrequency.Weekly, 9, 3, ReportChannels.Telegram | ReportChannels.Email,
            ReportSections.Stock | ReportSections.TopProducts, "a@x.ir", null, "@fratak", true, null, null, null, null);
        var back = ScheduledReportForm.From(row);
        Assert.Equal(row.Channels, back.ChannelFlags);
        Assert.Equal(row.Sections, back.SectionFlags);
        Assert.Equal(9, back.Hour);
        Assert.Equal(3, back.WeekDay);
        Assert.Equal("ایمیل، تلگرام", ScheduledModel.ChannelsText(row.Channels));
        Assert.Equal($"هفتگی · {ScheduledReportRules.WeekDayNames[3]} ساعت 9:00", ScheduledModel.ScheduleText(row.Frequency, row.Hour, row.WeekDay));
        Assert.Equal("—", ScheduledModel.ChannelsText(ReportChannels.None));
    }

    [Theory]
    [InlineData(null, "—")]
    [InlineData(0.4, "کمتر از ۱ دقیقه")]
    [InlineData(45d, "45 دقیقه")]
    [InlineData(95d, "1 ساعت و 35 دقیقه")]
    [InlineData(120d, "2 ساعت")]
    [InlineData(1500d, "1 روز و 1 ساعت")]
    public void Staff_response_time_text(double? minutes, string expected) => Assert.Equal(expected, StaffModel.Minutes(minutes));

    [Fact]
    public void Goal_badges() => Assert.Equal("a-badge-red", GoalsModel.StatusBadge("behind"));

    // ── چیدمان داشبورد ──────────────────────────────────────────────────

    [Fact]
    public void Dashboard_default_layout_shows_every_available_widget_in_order()
    {
        var all = DashboardLayout.Arrange(null, _ => true);
        Assert.Equal(DashboardLayout.Widgets.Select(w => w.Key), all.Select(a => a.Widget.Key));
        Assert.All(all, a => Assert.True(a.Visible));

        var limited = DashboardLayout.Arrange(null, w => w.Key != "goal");
        Assert.DoesNotContain(limited, a => a.Widget.Key == "goal");
        Assert.Equal(DashboardLayout.Widgets.Length, DashboardLayout.Widgets.Select(w => w.Key).Distinct().Count());
    }

    [Fact]
    public void Dashboard_layout_respects_user_order_hidden_keys_and_permissions()
    {
        var preference = new DashboardLayoutPreference
        {
            Order = ["low-stock", "unknown", "tasks", "low-stock", "goal"],
            Hidden = ["tasks"],
        };
        var layout = DashboardLayout.Arrange(preference, w => w.Key != "goal");

        Assert.Equal("low-stock", layout[0].Widget.Key);
        Assert.True(layout[0].Visible);
        Assert.Equal("tasks", layout[1].Widget.Key);
        Assert.False(layout[1].Visible);
        // ویجت‌های تازه (در تنظیم ذخیره‌شده نیستند) به ترتیب پیش‌فرض و نمایان اضافه می‌شوند.
        Assert.Equal("sales-stats", layout[2].Widget.Key);
        Assert.True(layout[2].Visible);
        Assert.DoesNotContain(layout, a => a.Widget.Key is "goal" or "unknown");
        Assert.Equal(layout.Count, layout.Select(a => a.Widget.Key).Distinct().Count());
    }

    [Fact]
    public void Dashboard_layout_from_form_keeps_only_known_keys()
    {
        var pref = DashboardLayout.FromForm(["goal", "<x>", "tasks", "goal", "low-stock"], ["tasks", "evil"]);
        Assert.Equal("goal|tasks|low-stock", string.Join("|", pref.Order));
        Assert.Equal("goal|low-stock", string.Join("|", pref.Hidden));

        var empty = DashboardLayout.FromForm(null, null);
        Assert.Empty(empty.Order);
        Assert.Empty(empty.Hidden);

        Assert.Equal("a-w-3", DashboardLayout.SpanClass(3));
        Assert.Equal("a-w-2", DashboardLayout.SpanClass(2));
        Assert.Equal("a-w-1", DashboardLayout.SpanClass(1));
        Assert.Equal("a-w-1", DashboardLayout.SpanClass(0));
    }

    [Fact]
    public void Dashboard_widgets_require_permissions_except_tasks()
    {
        var none = AdminAccess.None(Guid.NewGuid());
        var available = DashboardLayout.Widgets.Where(w => w.IsAvailable(none)).Select(w => w.Key).ToList();
        Assert.Equal("tasks", string.Join("|", available));
    }
}
