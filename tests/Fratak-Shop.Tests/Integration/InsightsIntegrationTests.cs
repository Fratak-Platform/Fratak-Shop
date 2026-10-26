using System.Globalization;
using Fratak_Shop.Core.DTOs.Reports;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Insights;
using Fratak_Shop.Core.Services.Reports;
using Fratak_Shop.Core.Services.Security;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Insights;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست گزارش‌های مدیریتی روی دیتابیس واقعی (مرحلهٔ هفتم – فاز ۷ب): هزینه‌ها (ثبت، حذف نرم، پرداخت دورهٔ بعد، یادآوری سررسید)،
/// هدف فروش ماهانه (upsert و محاسبهٔ پیشرفت از سفارش‌های واقعی)، گزارش خودکار با یک «ارسال‌کنندهٔ ضبط‌کننده» به‌جای ایمیل/ربات واقعی
/// و عملکرد کارکنان. داده‌ها در بازه‌های گذشتهٔ مخصوص هر تست ساخته می‌شوند تا با تست‌های دیگر قاطی نشوند.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class InsightsIntegrationTests(SqlServerFixture db)
{
    private static readonly PersianCalendar Persian = new();

    private static DateOnly Jalali(int year, int month, int day) => DateOnly.FromDateTime(Persian.ToDateTime(year, month, day, 0, 0, 0, 0));

    private static DateOnly Today => IranClock.Today(DateTimeOffset.UtcNow);

    /// <summary>ارسال‌کنندهٔ ساختگی: پیام‌ها را نگه می‌دارد و نتیجهٔ دلخواه برمی‌گرداند (بدون شبکه).</summary>
    private sealed class RecordingDelivery(bool succeed = true) : IReportDelivery
    {
        public List<(IReadOnlyList<string> To, string Subject, string Html)> Emails { get; } = [];
        public List<(ReportChannels Channel, string Token, string ChatId, string Text)> Bots { get; } = [];

        public Task<DeliveryAttempt> SendEmailAsync(SmtpSettings smtp, IReadOnlyList<string> to, string subject, string html, string text, CancellationToken cancellationToken)
        {
            Emails.Add((to, subject, html));
            return Task.FromResult(new DeliveryAttempt(succeed, succeed ? "ارسال شد" : "سرور ایمیل در دسترس نیست"));
        }

        public Task<DeliveryAttempt> SendBotMessageAsync(ReportChannels channel, string token, string chatId, string text, CancellationToken cancellationToken)
        {
            Bots.Add((channel, token, chatId, text));
            return Task.FromResult(new DeliveryAttempt(succeed, succeed ? "ارسال شد" : "ربات پاسخ نداد"));
        }
    }

    /// <summary>
    /// سرویس گزارش خودکار با ارسال‌کنندهٔ ساختگی؛ پیاده‌سازی واقعی IReportDelivery در لایهٔ Web است و در این fixture ثبت نشده،
    /// پس سرویس دستی با بقیهٔ وابستگی‌های واقعی DI ساخته می‌شود.
    /// </summary>
    private static ScheduledReportService Reports(IServiceProvider services, IReportDelivery delivery) => new(
        services.GetRequiredService<FratakShopDbContext>(),
        services.GetRequiredService<IAdminReportService>(),
        services.GetRequiredService<IAnalyticsReportService>(),
        services.GetRequiredService<IExpenseService>(),
        services.GetRequiredService<ISalesGoalService>(),
        delivery,
        services.GetRequiredService<ISecretProtector>(),
        services.GetRequiredService<IStaffNotificationService>(),
        services.GetRequiredService<IStoreSettingService>(),
        services.GetRequiredService<ILogger<ScheduledReportService>>());

    /// <summary>کارمند آزمایشی با یک نقش تازه که مجوزهای داده‌شده را دارد.</summary>
    private async Task<Guid> CreateStaffAsync(params string[] permissions)
    {
        var userId = await db.CreateUserAsync();
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var role = new AdminRole
        {
            Name = "نقش گزارش " + Guid.NewGuid().ToString("N")[..8],
            Permissions = permissions.Select(p => new AdminRolePermission { Permission = p }).ToList(),
        };
        context.AdminRoles.Add(role);
        context.UserAdminRoles.Add(new UserAdminRole { UserId = userId, Role = role });
        await context.SaveChangesAsync();
        return userId;
    }

    private async Task<List<StaffNotification>> NotificationsAsync(Guid userId, StaffNotificationKind kind)
    {
        await using var scope = db.Scope();
        return await scope.ServiceProvider.GetRequiredService<FratakShopDbContext>().StaffNotifications
            .Where(n => n.RecipientUserId == userId && n.Kind == kind).ToListAsync();
    }

    /// <summary>سفارش آزمایشی با زمان ثبت دلخواه (CreatedAt بعد از ذخیره با ExecuteUpdate تنظیم می‌شود).</summary>
    private async Task<Guid> CreateOrderAsync(DateTimeOffset createdAt, PaymentStatus payment, decimal amount,
        OrderStatus status = OrderStatus.Confirmed, Guid? staffId = null)
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var order = new Order
        {
            OrderNumber = "G" + Random.Shared.NextInt64(1_000_000_000, 9_999_999_999),
            Status = status,
            PaymentMethod = PaymentMethod.OnlineGateway,
            PaymentStatus = payment,
            SubtotalAmount = amount,
            PayableAmount = amount,
            CustomerName = "مشتری آزمایشی",
            CustomerPhone = "09120000000",
            ShippingProvince = "تهران",
            ShippingCity = "تهران",
            ShippingAddress = "خیابان آزمایش",
            ShippingPostalCode = "1234567890",
            ShippingPhone = "09120000000",
            CreatedByStaffId = staffId,
            CreatedByStaffName = staffId is null ? null : "کارمند آزمایشی",
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        await context.Orders.Where(o => o.Id == order.Id).ExecuteUpdateAsync(s => s.SetProperty(o => o.CreatedAt, createdAt));
        return order.Id;
    }

    // ── هزینه‌ها ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Expenses_are_summed_per_period_and_soft_deleted()
    {
        // ماه مخصوص این تست: خرداد ۱۳۹۸
        var (start, end) = JalaliMonth.Range(1398, 3);
        var period = new ReportPeriod(IranClock.StartOfDay(start), IranClock.StartOfDay(end));
        var title = "هزینهٔ آزمایشی " + Guid.NewGuid().ToString("N")[..6];

        await using var scope = db.Scope();
        var expenses = scope.ServiceProvider.GetRequiredService<IExpenseService>();
        var before = await expenses.GetTotalAsync(period);

        var first = await expenses.SaveAsync(new ExpenseInput(null, title, ExpenseCategory.Hosting, 1_500_000, start.AddDays(2), " پارس‌پک ", null, ExpenseRecurrence.None), null, "مدیر");
        var second = await expenses.SaveAsync(new ExpenseInput(null, title + " ۲", ExpenseCategory.Marketing, 500_000, end.AddDays(-1), null, null, ExpenseRecurrence.None), null, "مدیر");
        var outside = await expenses.SaveAsync(new ExpenseInput(null, title + " بیرون", ExpenseCategory.Marketing, 9_000_000, end, null, null, ExpenseRecurrence.None), null, "مدیر");
        Assert.True(first.IsSuccess(), first.Message);
        Assert.True(second.IsSuccess(), second.Message);
        Assert.True(outside.IsSuccess(), outside.Message);

        Assert.Equal(before + 2_000_000, await expenses.GetTotalAsync(period));
        var row = await expenses.GetAsync(first.Data);
        Assert.Equal("پارس‌پک", row!.Vendor);
        Assert.Null(row.NextDueOn);
        Assert.Equal("مدیر", row.CreatedByName);

        var list = await expenses.ListAsync(new ExpenseQuery(period, ExpenseCategory.Hosting, title));
        Assert.Equal(first.Data, Assert.Single(list.Items).Id);

        var invalid = await expenses.SaveAsync(new ExpenseInput(null, title, ExpenseCategory.Hosting, 1000.5m, start, null, null, ExpenseRecurrence.None), null, null);
        Assert.False(invalid.IsSuccess());

        // حذف نرم: از جمع و فهرست بیرون می‌رود ولی ردیف (برای سطل بازیافت) می‌ماند.
        Assert.True((await expenses.DeleteAsync(second.Data)).IsSuccess());
        Assert.Equal(before + 1_500_000, await expenses.GetTotalAsync(period));
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        Assert.True(await context.Expenses.IgnoreQueryFilters().AnyAsync(e => e.Id == second.Data && e.IsDeleted));
    }

    [Fact]
    public async Task Recurring_expense_records_next_period_and_reminds_once_per_day()
    {
        var staff = await CreateStaffAsync(AdminPermissions.ExpensesManage);
        var title = "تمدید دامنه " + Guid.NewGuid().ToString("N")[..6];
        var paidOn = Today.AddDays(-10);
        Guid id;

        await using (var scope = db.Scope())
        {
            var expenses = scope.ServiceProvider.GetRequiredService<IExpenseService>();
            var saved = await expenses.SaveAsync(new ExpenseInput(null, title, ExpenseCategory.Domain, 300_000, paidOn, "ایرنیک", null, ExpenseRecurrence.Monthly), staff, "کارمند");
            Assert.True(saved.IsSuccess(), saved.Message);
            id = saved.Data;
            var row = await expenses.GetAsync(id);
            Assert.Equal(ExpenseRules.NextDue(paidOn, ExpenseRecurrence.Monthly), row!.NextDueOn);
            Assert.True(row.NextDueOn >= Today);
        }

        // سررسید را به فردا می‌آوریم تا یادآوری فعال شود.
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var tomorrow = Today.AddDays(1);
            await context.Expenses.Where(e => e.Id == id).ExecuteUpdateAsync(s => s.SetProperty(e => e.NextDueOn, (DateOnly?)tomorrow));
        }

        await using (var scope = db.Scope())
            Assert.True(await scope.ServiceProvider.GetRequiredService<IExpenseService>().NotifyDueAsync(Today) >= 1);
        await using (var scope = db.Scope())
            await scope.ServiceProvider.GetRequiredService<IExpenseService>().NotifyDueAsync(Today); // دوباره در همان روز: تکراری نمی‌فرستد

        var reminders = (await NotificationsAsync(staff, StaffNotificationKind.ExpenseDue)).Where(n => n.Title.Contains(title)).ToList();
        var reminder = Assert.Single(reminders);
        Assert.Equal("/admin/finance/expenses?tab=recurring", reminder.Url);

        // پرداخت دورهٔ فعلی با مبلغ تازه: یک هزینهٔ عادی ساخته می‌شود و سررسید یک ماه جلو می‌رود.
        await using (var scope = db.Scope())
        {
            var expenses = scope.ServiceProvider.GetRequiredService<IExpenseService>();
            var paid = await expenses.RecordNextAsync(id, 350_000, staff, "کارمند");
            Assert.True(paid.IsSuccess(), paid.Message);

            var child = await expenses.GetAsync(paid.Data);
            Assert.Equal(Today.AddDays(1), child!.SpentOn);
            Assert.Equal(350_000m, child.Amount);
            Assert.Equal(ExpenseRecurrence.None, child.Recurrence);
            Assert.Equal(id, child.RecurringParentId);

            var parent = await expenses.GetAsync(id);
            Assert.Equal(ExpenseRules.NextDue(Today.AddDays(1), ExpenseRecurrence.Monthly), parent!.NextDueOn);
            Assert.Equal(350_000m, parent.Amount);

            Assert.False((await expenses.RecordNextAsync(paid.Data, null, staff, "کارمند")).IsSuccess()); // هزینهٔ یک‌باره دورهٔ بعد ندارد
            Assert.True((await expenses.StopRecurrenceAsync(id)).IsSuccess());
            Assert.False((await expenses.StopRecurrenceAsync(id)).IsSuccess());
            Assert.DoesNotContain(await expenses.GetUpcomingAsync(50), e => e.Id == id);
        }
    }

    // ── هدف فروش ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Sales_goal_is_upserted_and_progress_counts_only_realized_orders()
    {
        const int year = 1397, month = 4; // تیر ۱۳۹۷؛ ماه گذشته = همهٔ روزها سپری شده
        var (start, end) = JalaliMonth.Range(year, month);

        GoalProgress before;
        await using (var scope = db.Scope())
            before = await scope.ServiceProvider.GetRequiredService<ISalesGoalService>().GetProgressAsync(year, month);

        var middle = IranClock.At(start.AddDays(5), new TimeOnly(12, 0));
        await CreateOrderAsync(middle, PaymentStatus.Paid, 4_000_000);
        await CreateOrderAsync(middle.AddDays(3), PaymentStatus.Paid, 6_000_000);
        await CreateOrderAsync(middle, PaymentStatus.Pending, 50_000_000);                                // پرداخت‌نشده
        await CreateOrderAsync(middle, PaymentStatus.Paid, 70_000_000, OrderStatus.Cancelled);           // لغوشده
        await CreateOrderAsync(IranClock.StartOfDay(end), PaymentStatus.Paid, 80_000_000);               // ماه بعد

        await using (var scope = db.Scope())
        {
            var goals = scope.ServiceProvider.GetRequiredService<ISalesGoalService>();
            Assert.True((await goals.SaveAsync(new SalesGoalInput(year, month, 20_000_000, null, null), "مدیر")).IsSuccess());
            Assert.True((await goals.SaveAsync(new SalesGoalInput(year, month, 40_000_000, 10, " هدف تیر "), "مدیر")).IsSuccess());
            Assert.False((await goals.SaveAsync(new SalesGoalInput(year, 13, 40_000_000, null, null), "مدیر")).IsSuccess());
            Assert.False((await goals.SaveAsync(new SalesGoalInput(year, month, 0, null, null), "مدیر")).IsSuccess());
        }

        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            Assert.Equal(1, await context.SalesGoals.CountAsync(g => g.Year == year && g.Month == month));

            var progress = await scope.ServiceProvider.GetRequiredService<ISalesGoalService>().GetProgressAsync(year, month);
            Assert.Equal(40_000_000m, progress.TargetAmount);
            Assert.Equal(10, progress.TargetOrders);
            Assert.Equal("هدف تیر", progress.Note);
            Assert.Equal(before.Achieved + 10_000_000, progress.Achieved);
            Assert.Equal(before.Orders + 2, progress.Orders);
            Assert.Equal(end.DayNumber - start.DayNumber, progress.DaysPassed);
            Assert.Equal(0, progress.DaysLeft);
            Assert.Equal(SalesGoalRules.Compute(40_000_000, progress.Achieved, progress.DaysPassed, progress.DaysInMonth), progress.Forecast);

            Assert.True((await scope.ServiceProvider.GetRequiredService<ISalesGoalService>().DeleteAsync(year, month)).IsSuccess());
        }

        await using (var scope = db.Scope())
        {
            var progress = await scope.ServiceProvider.GetRequiredService<ISalesGoalService>().GetProgressAsync(year, month);
            Assert.False(progress.HasTarget);
            Assert.Equal("none", progress.Forecast.Status);
            var history = await scope.ServiceProvider.GetRequiredService<ISalesGoalService>().GetHistoryAsync(3);
            Assert.Equal(3, history.Count);
            Assert.Equal(JalaliMonth.Of(Today), (history[0].Year, history[0].Month));
        }
    }

    // ── گزارش خودکار ─────────────────────────────────────────────────────

    [Fact]
    public async Task Scheduled_report_is_sent_to_every_channel_and_runs_once_when_due()
    {
        var delivery = new RecordingDelivery();
        Guid id;
        await using (var scope = db.Scope())
        {
            var reports = Reports(scope.ServiceProvider, delivery);
            var config = await reports.SaveDeliveryAsync(new ReportDeliveryInput("mail.fratak.ir", 587, "reports", "secret-pass", false,
                "reports@fratak.ir", "فراتک", "123456789:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw", false, null, false));
            Assert.True(config.IsSuccess(), config.Message);

            var view = await reports.GetDeliveryAsync();
            Assert.True(view.HasSmtpPassword);
            Assert.True(view.HasBaleToken);
            Assert.False(view.HasTelegramToken);
            // رمزها فقط رمزنگاری‌شده ذخیره می‌شوند.
            var stored = await scope.ServiceProvider.GetRequiredService<FratakShopDbContext>().ReportDeliveryConfigs.FirstAsync();
            Assert.NotEqual("secret-pass", stored.SmtpPasswordProtected);

            var saved = await reports.SaveAsync(new ScheduledReportInput(null, "خلاصهٔ روزانه", ReportFrequency.Daily, 8, 0,
                ReportChannels.Email | ReportChannels.Bale, ReportSections.All, "owner@fratak.ir, OWNER@fratak.ir", " -1001234567890 ", "@ignored", true), null);
            Assert.True(saved.IsSuccess(), saved.Message);
            id = saved.Data;

            var row = await reports.GetAsync(id);
            Assert.Equal("owner@fratak.ir", row!.EmailTo);
            Assert.Equal("-1001234567890", row.BaleChatId);
            Assert.Null(row.TelegramChatId); // کانال انتخاب‌نشده ذخیره نمی‌شود
            Assert.True(row.NextRunAt > DateTimeOffset.UtcNow);

            var sent = await reports.SendNowAsync(id);
            Assert.True(sent.IsSuccess(), sent.Message);
        }

        var email = Assert.Single(delivery.Emails);
        Assert.Equal("owner@fratak.ir", string.Join("|", email.To));
        Assert.Contains("خلاصهٔ روزانه", email.Subject);
        Assert.Contains("dir=\"rtl\"", email.Html);
        var bot = Assert.Single(delivery.Bots);
        Assert.Equal(ReportChannels.Bale, bot.Channel);
        Assert.Equal("123456789:AAHdqTcvCH1vGWJxfSeofSAs0K5PALDsaw", bot.Token);
        Assert.Equal("-1001234567890", bot.ChatId);
        Assert.Contains("فروش", bot.Text);
        Assert.True(bot.Text.Length <= ScheduledReportRules.MaxBotMessageLength);

        // نوبت اجرا گذشته: فقط یک بار اجرا و زمان بعدی جلو برده می‌شود.
        var now = DateTimeOffset.UtcNow;
        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var past = now.AddDays(-3);
            await context.ScheduledReports.Where(r => r.Id == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.NextRunAt, (DateTimeOffset?)past));
        }
        await using (var scope = db.Scope())
            Assert.True(await Reports(scope.ServiceProvider, delivery).RunDueAsync(now) >= 1);
        await using (var scope = db.Scope())
            await Reports(scope.ServiceProvider, delivery).RunDueAsync(now);

        Assert.Equal(2, delivery.Emails.Count);
        await using (var scope = db.Scope())
        {
            var reports = Reports(scope.ServiceProvider, delivery);
            var row = await reports.GetAsync(id);
            Assert.True(row!.LastSucceeded);
            Assert.Equal(ScheduledReportRules.NextRun(ReportFrequency.Daily, 8, 0, now), row.NextRunAt);

            Assert.True((await reports.SetActiveAsync(id, false)).IsSuccess());
            Assert.Null((await reports.GetAsync(id))!.NextRunAt);
            Assert.True((await reports.DeleteAsync(id)).IsSuccess());
            Assert.Null(await reports.GetAsync(id));
        }
    }

    [Fact]
    public async Task Failed_scheduled_report_notifies_staff()
    {
        var staff = await CreateStaffAsync(AdminPermissions.SettingsManage);
        var delivery = new RecordingDelivery(succeed: false);
        var title = "گزارش ناموفق " + Guid.NewGuid().ToString("N")[..6];
        Guid id;

        await using (var scope = db.Scope())
        {
            var reports = Reports(scope.ServiceProvider, delivery);
            var saved = await reports.SaveAsync(new ScheduledReportInput(null, title, ReportFrequency.Weekly, 9, 0,
                ReportChannels.Telegram, ReportSections.Sales | ReportSections.Goal, null, null, "@fratak_reports", true), null);
            Assert.True(saved.IsSuccess(), saved.Message);
            id = saved.Data;

            // توکن تلگرام ثبت نشده → بدون تماس با ارسال‌کننده ناموفق می‌شود.
            var manual = await reports.SendNowAsync(id);
            Assert.False(manual.IsSuccess());
            Assert.Contains("توکن", manual.Message);
            Assert.Empty(delivery.Bots);

            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var past = DateTimeOffset.UtcNow.AddHours(-1);
            await context.ScheduledReports.Where(r => r.Id == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.NextRunAt, (DateTimeOffset?)past));
        }

        await using (var scope = db.Scope())
            Assert.True(await Reports(scope.ServiceProvider, delivery).RunDueAsync(DateTimeOffset.UtcNow) >= 1);

        var alert = Assert.Single((await NotificationsAsync(staff, StaffNotificationKind.ReportFailed)).Where(n => n.Title.Contains(title)));
        Assert.Equal("/admin/reports/scheduled", alert.Url);

        await using (var scope = db.Scope())
        {
            var reports = Reports(scope.ServiceProvider, delivery);
            var row = await reports.GetAsync(id);
            Assert.False(row!.LastSucceeded);
            Assert.Contains("تلگرام", row.LastStatus);
            await reports.DeleteAsync(id);
        }
    }

    [Fact]
    public async Task Digest_contains_the_requested_sections()
    {
        await using var scope = db.Scope();
        var reports = Reports(scope.ServiceProvider, new RecordingDelivery());
        var runAt = DateTimeOffset.UtcNow;

        var all = await reports.BuildDigestAsync("همه", ReportFrequency.Monthly, ReportSections.All, runAt);
        Assert.True(all.Sections.Count >= 6);
        Assert.Contains(all.Sections, s => s.Title == "هدف فروش");
        Assert.All(all.Sections, s => Assert.NotEmpty(s.Lines));
        Assert.Contains(ScheduledReportRules.FrequencyTitle(ReportFrequency.Monthly), all.PeriodLabel);

        var sales = await reports.BuildDigestAsync("فروش", ReportFrequency.Weekly, ReportSections.Sales, runAt);
        Assert.Single(sales.Sections);
    }

    // ── عملکرد کارکنان ───────────────────────────────────────────────────

    [Fact]
    public async Task Staff_performance_counts_each_kind_of_work_in_the_period()
    {
        var staff = await CreateStaffAsync(AdminPermissions.OrdersManage);
        var day = new DateOnly(2019, 3, 10);
        var at = IranClock.At(day, new TimeOnly(10, 30));
        var period = new ReportPeriod(IranClock.StartOfDay(day), IranClock.StartOfDay(day.AddDays(1)));

        var orderId = await CreateOrderAsync(at, PaymentStatus.Paid, 2_500_000, staffId: staff);
        await CreateOrderAsync(at.AddDays(5), PaymentStatus.Paid, 9_000_000, staffId: staff); // خارج از بازه

        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var note = new OrderNote { OrderId = orderId, Kind = OrderNoteKind.Note, AuthorUserId = staff, AuthorName = "کارمند", Content = "پیگیری شد" };
            var status = new OrderNote { OrderId = orderId, Kind = OrderNoteKind.StatusChange, AuthorUserId = staff, AuthorName = "کارمند", Content = "تأیید شد" };
            context.OrderNotes.AddRange(note, status);
            await context.SaveChangesAsync();
            var noteIds = new[] { note.Id, status.Id };
            await context.OrderNotes.Where(n => noteIds.Contains(n.Id)).ExecuteUpdateAsync(s => s.SetProperty(n => n.CreatedAt, at.AddMinutes(30)));
        }

        await using (var scope = db.Scope())
        {
            var performance = scope.ServiceProvider.GetRequiredService<IStaffPerformanceService>();
            var row = Assert.Single(await performance.GetAsync(period), r => r.UserId == staff);
            Assert.Equal(1, row.OrdersCreated);
            Assert.Equal(2_500_000m, row.OrdersCreatedAmount);
            Assert.Equal(1, row.OrderNotes);
            Assert.Equal(1, row.StatusChanges);
            Assert.Equal(at.AddMinutes(30), row.LastActivityAt);
            Assert.NotEmpty(row.Roles);

            var detail = await performance.GetDetailAsync(staff, period);
            Assert.NotNull(detail);
            Assert.Equal(staff, detail!.Row.UserId);
            Assert.Equal(2, detail.Hours.Sum(h => h.Value)); // دو یادداشت ساعت ۱۱ (سفارش‌ها در «ساعت فعالیت» شمرده نمی‌شوند مگر لاگ داشته باشند)

            Assert.Null(await performance.GetDetailAsync(Guid.NewGuid(), period)); // کاربر غیرکارمند
        }
    }
}
