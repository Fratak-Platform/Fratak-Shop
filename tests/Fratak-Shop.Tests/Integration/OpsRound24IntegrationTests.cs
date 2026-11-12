using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Ops;
using Fratak_Shop.Core.Services.Support;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Ops;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Support;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست یکپارچهٔ دور ۲۴ بستهٔ F روی SQL Server واقعی:
/// <list type="bullet">
/// <item>۷۳ دسترس‌پذیری: ثبت قطعی (کوتاه و تکراری/هم‌پوشان رد می‌شود)، اعلان به کارکنان فنی، خلاصهٔ ۲۴ ساعت/۷ روز/۳۰ روز و پاک‌سازی قدیمی‌ها.</item>
/// <item>۷۴ گزارش امنیتی: کارمند با رمز اولیه ← هشدار؛ حملهٔ حدس رمز از یک IP ← در آمار و موارد؛ اعلان هفتگی یک بار در هفته.</item>
/// <item>۲۷ پیشنهاد پاسخ تیکت: وضعیت واقعی سفارش خود مشتری (هرگز سفارش مشتری دیگر) و یادگیری از پاسخ تیکت مشابه با پوشاندن نام و عدد.</item>
/// </list>
/// هر آزمون دادهٔ خودش را می‌سازد و در finally پاک می‌کند.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
[Trait("Area", "Round24Ops")]
public class OpsRound24IntegrationTests(SqlServerFixture db)
{
    private static string Key() => Guid.NewGuid().ToString("N")[..10];

    private static async Task<User> StaffAsync(FratakShopDbContext context, UserRole role, string password)
    {
        var user = new User
        {
            PhoneNumber = "09" + Random.Shared.NextInt64(100_000_000, 999_999_999),
            FirstName = "کارمند",
            LastName = "آزمون " + Key(),
            Role = role,
            IsPhoneVerified = true,
            PasswordHash = PasswordHasher.Hash(password),
            SecurityStamp = PasswordHasher.NewSecurityStamp(),
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static async Task RetireAsync(FratakShopDbContext context, params Guid[] userIds)
    {
        context.ChangeTracker.Clear();
        await context.StaffNotifications.IgnoreQueryFilters().Where(n => userIds.Contains(n.RecipientUserId)).ExecuteDeleteAsync();
        // کاربر آزمون دیگر «کارمند فعال با رمز اولیه» حساب نشود.
        await context.Users.IgnoreQueryFilters().Where(u => userIds.Contains(u.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.IsBlocked, true).SetProperty(u => u.PasswordHash, (string?)null));
    }

    // ── ۷۳: دسترس‌پذیری ───────────────────────────────────────────────

    [SqlFact]
    public async Task Uptime_records_real_incidents_once_and_summarises_them()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var uptime = scope.ServiceProvider.GetRequiredService<IUptimeService>();
        // زمان دور از اکنون تا رخداد واقعی یا آزمون دیگری اثر نگذارد.
        var now = new DateTimeOffset(2031, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var monitor = await StaffAsync(context, UserRole.SuperAdmin, "Strong-" + Key());
        async Task CleanAsync()
        {
            context.ChangeTracker.Clear();
            await context.UptimeIncidents.IgnoreQueryFilters().Where(i => i.StartedAt >= now.AddYears(-3) && i.StartedAt <= now.AddDays(1)).ExecuteDeleteAsync();
        }
        await CleanAsync();
        try
        {
            // ۲ دقیقه = ری‌استارت عادی ← ثبت نمی‌شود.
            Assert.Null(await uptime.RecordAsync(UptimeIncidentKind.Downtime, now.AddHours(-3), now.AddHours(-3).AddMinutes(2), "کوتاه"));
            // نوع نامعتبر ← رد.
            Assert.Null(await uptime.RecordAsync((UptimeIncidentKind)99, now.AddHours(-3), now.AddHours(-2), null));

            var first = await uptime.RecordAsync(UptimeIncidentKind.Downtime, now.AddHours(-5), now.AddHours(-5).AddMinutes(144), "توقف ناگهانی برنامه");
            Assert.NotNull(first);
            // پروسهٔ دوم IIS همان قطعی را دوباره گزارش می‌کند ← تکراری ثبت نمی‌شود.
            Assert.Null(await uptime.RecordAsync(UptimeIncidentKind.Downtime, now.AddHours(-5).AddMinutes(10), now.AddHours(-5).AddMinutes(60), null));
            // نوع دیگر (قطع پایگاه داده) در همان بازه جداست.
            Assert.NotNull(await uptime.RecordAsync(UptimeIncidentKind.DatabaseDown, now.AddHours(-5).AddMinutes(30), now.AddHours(-5).AddMinutes(40), new string('x', 900)));
            var stored = await context.UptimeIncidents.Where(i => i.StartedAt >= now.AddDays(-1) && i.StartedAt <= now).ToListAsync();
            Assert.Equal(2, stored.Count);
            Assert.All(stored, i => Assert.True((i.Detail?.Length ?? 0) <= 500));

            // اعلان به کارکنان فنی با پیوند صفحهٔ دسترس‌پذیری.
            Assert.True(await context.StaffNotifications.AnyAsync(n => n.RecipientUserId == monitor.Id && n.Url == "/admin/system/uptime"));

            var summary = await uptime.SummaryAsync(now);
            Assert.Equal(90d, summary.Day);          // ۱۴۴ دقیقه از ۱۴۴۰ (قطع DB داخل همان بازه است و دوباره شمرده نمی‌شود)
            Assert.True(summary.Week > summary.Day);
            Assert.True(summary.Month > summary.Week);
            Assert.Equal(144, summary.DowntimeMinutes30);
            Assert.Equal(2, summary.IncidentCount30);

            // قطعی خیلی قدیمی پاک می‌شود؛ تازه‌ها می‌مانند.
            context.UptimeIncidents.Add(new UptimeIncident { Kind = UptimeIncidentKind.Downtime, StartedAt = now.AddYears(-2), EndedAt = now.AddYears(-2).AddMinutes(10) });
            await context.SaveChangesAsync();
            Assert.Equal(1, await uptime.PruneAsync(now));
            Assert.Equal(2, await context.UptimeIncidents.CountAsync(i => i.StartedAt >= now.AddYears(-3) && i.StartedAt <= now));
        }
        finally
        {
            await CleanAsync();
            await RetireAsync(context, monitor.Id);
        }
    }

    // ── ۷۴: گزارش امنیتی ──────────────────────────────────────────────

    [SqlFact]
    public async Task Security_report_flags_default_passwords_and_brute_force()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var reports = scope.ServiceProvider.GetRequiredService<ISecurityReportService>();
        var weak = await StaffAsync(context, UserRole.Admin, DefaultAdminAccount.DefaultInitialPassword);
        var ip = "203.0.113." + Random.Shared.Next(10, 250);
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < 25; i++)
            context.SecurityLogs.Add(new SecurityLog { Type = SecurityEventType.LoginFailed, IpAddress = ip, Identifier = "0912***0000" });
        await context.SaveChangesAsync();
        try
        {
            var report = await reports.BuildAsync(now, refresh: true);

            var password = Assert.Single(report.Checks, c => c.Key == "default-password");
            Assert.Equal(SecurityCheckLevel.Warning, password.Level);
            Assert.Equal("/admin/account", password.FixUrl);
            Assert.Contains(report.Checks, c => c.Key == "captcha");
            Assert.Contains(report.Checks, c => c.Key == "backup");
            var brute = Assert.Single(report.Checks, c => c.Key == "brute-force");
            Assert.NotEqual(SecurityCheckLevel.Ok, brute.Level);
            Assert.Contains(ip, brute.Detail, StringComparison.Ordinal);
            Assert.Contains(report.Activity.TopIps, t => t.Ip == ip && t.Count >= 25);
            Assert.True(report.Activity.FailedLogins >= 25);

            // مهم‌ترین موارد اول؛ امتیاز با قواعد یکی است و رمز هیچ‌جا در متن نیست.
            Assert.Equal(report.Checks.OrderByDescending(c => c.Level).Select(c => c.Key), report.Checks.Select(c => c.Key));
            Assert.Equal(SecurityReportRules.Score(report.Checks), report.Score);
            Assert.True(report.Score < 100);
            Assert.DoesNotContain(report.Checks, c => c.Detail.Contains(DefaultAdminAccount.DefaultInitialPassword, StringComparison.Ordinal));

            // کش: بدون refresh همان گزارش برمی‌گردد (صفحه زیر بار سنگین کوئری نمی‌زند).
            Assert.Same(report, await reports.BuildAsync(now.AddMinutes(1)));
        }
        finally
        {
            context.ChangeTracker.Clear();
            await context.SecurityLogs.Where(l => l.IpAddress == ip).ExecuteDeleteAsync();
            await RetireAsync(context, weak.Id);
            scope.ServiceProvider.GetRequiredService<IMemoryCache>().Remove("ops:security-report");
        }
    }

    [SqlFact]
    public async Task Weekly_security_report_is_sent_once_a_week_in_working_hours()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var reports = scope.ServiceProvider.GetRequiredService<ISecurityReportService>();
        var owner = await StaffAsync(context, UserRole.SuperAdmin, "Strong-" + Key());
        async Task CleanAsync()
        {
            context.ChangeTracker.Clear();
            await context.StaffNotifications.IgnoreQueryFilters().Where(n => n.Title.StartsWith(SecurityReportRules.WeeklyTitlePrefix)).ExecuteDeleteAsync();
        }
        await CleanAsync();
        // ساعت ۰۸:۰۰ UTC = ۱۱:۳۰ تهران (داخل ساعت کاری).
        var now = new DateTimeOffset(DateTime.UtcNow.Date.AddHours(8), TimeSpan.Zero);
        try
        {
            Assert.True(await reports.SendWeeklyAsync(now) >= 1);
            var sent = await context.StaffNotifications.Where(n => n.RecipientUserId == owner.Id && n.Title.StartsWith(SecurityReportRules.WeeklyTitlePrefix)).ToListAsync();
            var notification = Assert.Single(sent);
            Assert.Equal("/admin/system/security-report", notification.Url);
            Assert.StartsWith("امتیاز", notification.Body, StringComparison.Ordinal);

            // نیم ساعت بعد ← محدودیت یک‌ساعته؛ دو ساعت بعد ← این هفته فرستاده شده.
            Assert.Equal(0, await reports.SendWeeklyAsync(now.AddMinutes(30)));
            Assert.Equal(0, await reports.SendWeeklyAsync(now.AddHours(2)));
            Assert.Equal(1, await context.StaffNotifications.CountAsync(n => n.RecipientUserId == owner.Id && n.Title.StartsWith(SecurityReportRules.WeeklyTitlePrefix)));

            // شب (۲۲:۳۰ تهران) هیچ اعلانی نمی‌رود، حتی اگر اعلان قبلی پاک شده باشد.
            await CleanAsync();
            Assert.Equal(0, await reports.SendWeeklyAsync(now.AddHours(11)));
            Assert.False(await context.StaffNotifications.AnyAsync(n => n.Title.StartsWith(SecurityReportRules.WeeklyTitlePrefix)));
        }
        finally
        {
            await CleanAsync();
            await RetireAsync(context, owner.Id);
            scope.ServiceProvider.GetRequiredService<IMemoryCache>().Remove("ops:security-report");
        }
    }

    // ── ۲۷: پیشنهاد پاسخ تیکت ─────────────────────────────────────────

    private static Order NewOrder(Guid userId, OrderStatus status, string? tracking = null) => new()
    {
        UserId = userId,
        OrderNumber = "FR-" + Random.Shared.Next(100_000, 999_999),
        Status = status,
        PaymentMethod = PaymentMethod.OnlineGateway,
        PaymentStatus = PaymentStatus.Paid,
        SubtotalAmount = 500_000,
        PayableAmount = 500_000,
        CustomerName = "مشتری آزمون",
        CustomerPhone = "09120000000",
        ShippingProvince = "تهران",
        ShippingCity = "تهران",
        ShippingAddress = "خیابان آزمون",
        ShippingPostalCode = Random.Shared.NextInt64(1_000_000_000, 9_999_999_999).ToString(),
        ShippingPhone = "09120000000",
        TrackingCode = tracking,
        ShippedAt = tracking is null ? null : DateTimeOffset.UtcNow.AddDays(-1),
    };

    private static SupportTicket NewTicket(Guid userId, Guid departmentId, string subject, TicketStatus status, params (bool Staff, string Text)[] messages)
    {
        var ticket = new SupportTicket
        {
            TicketNumber = "T" + Random.Shared.Next(10_000_000, 99_999_999),
            UserId = userId,
            DepartmentId = departmentId,
            Subject = subject,
            Status = status,
            LastActivityAt = DateTimeOffset.UtcNow,
        };
        foreach (var (staff, text) in messages)
            ticket.Messages.Add(new SupportTicketMessage { IsStaff = staff, UserId = staff ? null : userId, AuthorName = staff ? "پشتیبانی" : "مشتری", Content = text });
        return ticket;
    }

    [SqlFact]
    public async Task Ticket_advice_uses_this_customers_real_order_and_learns_from_similar_answers()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var advisor = scope.ServiceProvider.GetRequiredService<ITicketReplyAdvisor>();
        var cache = scope.ServiceProvider.GetRequiredService<IMemoryCache>();
        var customerId = await db.CreateUserAsync();
        var otherId = await db.CreateUserAsync();
        var department = new SupportDepartment { Title = "پشتیبانی آزمون " + Key(), IsActive = true };
        context.SupportDepartments.Add(department);
        var mine = NewOrder(customerId, OrderStatus.Shipped, "998877665544");
        var foreign = NewOrder(otherId, OrderStatus.Processing);
        context.Orders.AddRange(mine, foreign);
        await context.SaveChangesAsync();

        // تیکت قدیمی مشتری دیگر (مریم کاظمی) دربارهٔ تعویض سایز که کارمند جوابش را داده.
        await context.Users.Where(u => u.Id == otherId).ExecuteUpdateAsync(s => s.SetProperty(u => u.FirstName, "مریم").SetProperty(u => u.LastName, "کاظمی"));
        var old = NewTicket(otherId, department.Id, "تعویض سایز کفش ورزشی", TicketStatus.Answered,
            (false, "کفش ورزشی سایز ۴۲ کوچک است، تعویض سایز کفش ممکن است؟"),
            (true, "سلام مریم کاظمی عزیز، برای تعویض سایز کفش کافی است از پروفایل درخواست مرجوعی بزنید. کد پیگیری 4455667788 است."));
        // تیکت فعلی: شمارهٔ سفارش مشتری دیگر را هم نوشته؛ فقط سفارش خودش باید استفاده شود.
        var current = NewTicket(customerId, department.Id, "سفارشم کی میرسه", TicketStatus.Open,
            (false, $"سلام، سفارش {foreign.OrderNumber} یا {mine.OrderNumber} من کی میرسه؟ کد رهگیری ندارم"));
        var shoe = NewTicket(customerId, department.Id, "تعویض سایز کفش", TicketStatus.Open,
            (false, "کفش ورزشی که خریدم سایزش کوچک است، تعویض سایز کفش ممکن است؟"));
        context.SupportTickets.AddRange(old, current, shoe);
        await context.SaveChangesAsync();
        cache.Remove("support:reply-history");
        try
        {
            var advice = await advisor.SuggestAsync(current.Id);
            Assert.Equal(TicketTopic.Shipping, advice.Topic);
            var order = advice.Suggestions.First();
            Assert.Equal("اطلاعات سفارش", order.Source);
            Assert.Contains(mine.OrderNumber, order.Body, StringComparison.Ordinal);
            Assert.Contains("998877665544", order.Body, StringComparison.Ordinal);
            Assert.DoesNotContain(advice.Suggestions, s => s.Body.Contains(foreign.OrderNumber, StringComparison.Ordinal));
            Assert.True(advice.Suggestions.Count <= 3);

            var learned = await advisor.SuggestAsync(shoe.Id);
            var similar = Assert.Single(learned.Suggestions, s => s.Source == "پاسخ تیکت مشابه");
            Assert.Contains("{FirstName}", similar.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("مریم", similar.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("کاظمی", similar.Body, StringComparison.Ordinal);
            Assert.DoesNotContain("4455667788", similar.Body, StringComparison.Ordinal);
            Assert.Contains("تعویض سایز", similar.Body, StringComparison.Ordinal);

            // تیکت ناموجود ← پیشنهاد خالی (نه خطا).
            Assert.Empty((await advisor.SuggestAsync(Guid.NewGuid())).Suggestions);
        }
        finally
        {
            context.ChangeTracker.Clear();
            var ticketIds = new[] { old.Id, current.Id, shoe.Id };
            await context.SupportTicketMessages.IgnoreQueryFilters().Where(m => ticketIds.Contains(m.TicketId)).ExecuteDeleteAsync();
            await context.SupportTickets.IgnoreQueryFilters().Where(t => ticketIds.Contains(t.Id)).ExecuteDeleteAsync();
            await context.SupportDepartments.IgnoreQueryFilters().Where(d => d.Id == department.Id).ExecuteDeleteAsync();
            await context.Orders.IgnoreQueryFilters().Where(o => o.Id == mine.Id || o.Id == foreign.Id).ExecuteDeleteAsync();
            cache.Remove("support:reply-history");
        }
    }
}
