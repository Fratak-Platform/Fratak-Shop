using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Team;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Settings;
using Fratak_Shop.Data.Entities.Team;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست «امنیت و کار تیمی کارکنان» روی SQL Server واقعی (مرحلهٔ هفتم – فاز ۱۰):
/// • تأیید دونفره: درخواست ← جلوگیری از درخواست تکراری ← درخواست‌دهنده نمی‌تواند خودش تأیید کند ← رد بدون علت ممنوع ←
///   تأیید همکار ← اجرای واقعی (افزایش کیف پول / تغییر نقش) ← تصمیم دوباره ممنوع؛ پس گرفتن و منقضی شدن.
/// • دیده شدن درخواست‌ها فقط برای کارمندانی که دسترسی همان کار را دارند (و خود درخواست‌دهنده).
/// • TOTP: راه‌اندازی، رد کد تکراری، کد بازیابی یک‌بارمصرف، قفل موقت بعد از ۵ خطا و بازنشانی توسط مالک.
/// • کارهای شخصی: سپردن فقط به کارکنان، حذف فقط توسط سازنده، «انجام شد» توسط گیرنده، یادآوری فقط یک بار.
/// • اعتبارسنجی تنظیمات امنیت کارکنان.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class TeamIntegrationTests(SqlServerFixture db)
{
    private static readonly AdminAccess Owner =
        new(Guid.NewGuid(), "مالک آزمایشی", "09120000009", true, new HashSet<string>(), []);

    private static FratakShopDbContext Ctx(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();

    /// <summary>کارمند واقعی در دیتابیس با یک نقش تازه که همین دسترسی‌ها را دارد؛ دسترسی خوانده‌شده از سرویس برمی‌گردد.</summary>
    private async Task<AdminAccess> CreateStaffAsync(params string[] permissions)
    {
        var userId = await db.CreateUserAsync();
        await using var scope = db.Scope();
        var access = scope.ServiceProvider.GetRequiredService<IAdminAccessService>();
        var role = await access.SaveRoleAsync(new SaveAdminRoleCommand
        {
            Name = "نقش تیم " + Guid.NewGuid().ToString("N")[..8],
            Permissions = [.. permissions],
        }, Owner);
        Assert.True(role.IsSuccess(), role.Message);
        var assign = await access.SetUserRolesAsync(userId, [role.Data], Owner);
        Assert.True(assign.IsSuccess(), assign.Message);
        return await access.GetAccessAsync(userId);
    }

    private async Task<Guid> CreateRoleAsync(params string[] permissions)
    {
        await using var scope = db.Scope();
        var role = await scope.ServiceProvider.GetRequiredService<IAdminAccessService>().SaveRoleAsync(new SaveAdminRoleCommand
        {
            Name = "نقش هدف " + Guid.NewGuid().ToString("N")[..8],
            Permissions = [.. permissions],
        }, Owner);
        Assert.True(role.IsSuccess(), role.Message);
        return role.Data;
    }

    // ═══ تأیید دونفره ═══════════════════════════════════════════════════

    [SqlFact]
    public async Task Wallet_adjustment_needs_a_second_staff_member_and_then_runs()
    {
        var requester = await CreateStaffAsync(AdminPermissions.CustomersWallet, AdminPermissions.CustomersView);
        var approver = await CreateStaffAsync(AdminPermissions.CustomersWallet, AdminPermissions.CustomersView);
        var customer = await db.CreateUserAsync();

        Guid requestId;
        await using (var scope = db.Scope())
        {
            var approvals = scope.ServiceProvider.GetRequiredService<IApprovalService>();
            var created = await approvals.RequestWalletAdjustAsync(new WalletAdjustPayload(customer, 2_500_000, "جبران تأخیر ارسال", false), requester);
            Assert.True(created.IsSuccess(), created.Message);
            requestId = created.Data;

            // برای همان مشتری درخواست دوم ساخته نمی‌شود تا اولی روشن شود.
            var duplicate = await approvals.RequestWalletAdjustAsync(new WalletAdjustPayload(customer, 100_000, "دوباره", false), approver);
            Assert.False(duplicate.IsSuccess());
            Assert.Contains("در انتظار", duplicate.Message);
        }

        await using (var scope = db.Scope())
        {
            var approvals = scope.ServiceProvider.GetRequiredService<IApprovalService>();
            var saved = await approvals.GetAsync(requestId);
            Assert.NotNull(saved);
            Assert.Equal(ApprovalStatus.Pending, saved.Status);
            Assert.Equal(ApprovalKind.WalletAdjust, saved.Kind);
            Assert.Equal(2_500_000m, saved.Amount);
            Assert.Equal(requester.UserId, saved.RequestedById);
            Assert.True(saved.ExpiresAt > DateTimeOffset.UtcNow.AddHours(71));

            // هنوز هیچ پولی جابه‌جا نشده.
            Assert.Equal(0m, await Ctx(scope).Users.Where(u => u.Id == customer).Select(u => u.WalletBalance).SingleAsync());

            Assert.False((await approvals.DecideAsync(requestId, true, null, requester)).IsSuccess()); // خودش نه
            var noPermission = new AdminAccess(Guid.NewGuid(), "بی‌دسترسی", "09120000001", false, new HashSet<string> { AdminPermissions.OrdersView }, ["x"]);
            Assert.False((await approvals.DecideAsync(requestId, true, null, noPermission)).IsSuccess());
            Assert.False((await approvals.DecideAsync(requestId, false, "  ", approver)).IsSuccess()); // رد بدون علت

            Assert.True(await approvals.CountDecidableAsync(approver) >= 1);
        }

        await using (var scope = db.Scope())
        {
            var approvals = scope.ServiceProvider.GetRequiredService<IApprovalService>();
            var decided = await approvals.DecideAsync(requestId, true, "تأیید شد", approver);
            Assert.True(decided.IsSuccess(), decided.Message);
        }

        await using (var scope = db.Scope())
        {
            var approvals = scope.ServiceProvider.GetRequiredService<IApprovalService>();
            var done = await approvals.GetAsync(requestId);
            Assert.Equal(ApprovalStatus.Approved, done!.Status);
            Assert.Equal(approver.UserId, done.DecidedById);
            Assert.NotNull(done.DecidedAt);

            var user = await Ctx(scope).Users.Where(u => u.Id == customer).Select(u => new { u.WalletBalance, u.WalletBonusBalance }).SingleAsync();
            Assert.Equal(2_500_000m, user.WalletBalance);
            Assert.Equal(0m, user.WalletBonusBalance); // «غیر هدیه» درخواست شده بود

            // تصمیم دوباره ممکن نیست؛ درخواست تازه برای همان مشتری حالا مجاز است.
            Assert.False((await approvals.DecideAsync(requestId, false, "اشتباه", approver)).IsSuccess());
            var next = await approvals.RequestWalletAdjustAsync(new WalletAdjustPayload(customer, -500_000, "اصلاح", true), requester);
            Assert.True(next.IsSuccess(), next.Message);
            Assert.True((await approvals.CancelAsync(next.Data, requester)).IsSuccess());
        }
    }

    [SqlFact]
    public async Task Rejection_keeps_the_balance_and_records_the_reason()
    {
        var requester = await CreateStaffAsync(AdminPermissions.CustomersWallet);
        var approver = await CreateStaffAsync(AdminPermissions.CustomersWallet);
        var customer = await db.CreateUserAsync();

        await using var scope = db.Scope();
        var approvals = scope.ServiceProvider.GetRequiredService<IApprovalService>();
        var created = await approvals.RequestWalletAdjustAsync(new WalletAdjustPayload(customer, 3_000_000, "هدیهٔ تولد", true), requester);
        Assert.True(created.IsSuccess(), created.Message);

        var rejected = await approvals.DecideAsync(created.Data, false, "مبلغ زیاد است", approver);
        Assert.True(rejected.IsSuccess(), rejected.Message);

        var request = await approvals.GetAsync(created.Data);
        Assert.Equal(ApprovalStatus.Rejected, request!.Status);
        Assert.Equal("مبلغ زیاد است", request.DecisionNote);
        Assert.Equal(0m, await Ctx(scope).Users.Where(u => u.Id == customer).Select(u => u.WalletBalance).SingleAsync());
    }

    [SqlFact]
    public async Task Role_change_runs_with_the_requesters_rights_after_approval()
    {
        var requester = await CreateStaffAsync(AdminPermissions.StaffManage, AdminPermissions.OrdersView);
        var approver = await CreateStaffAsync(AdminPermissions.StaffManage, AdminPermissions.OrdersView);
        var targetRole = await CreateRoleAsync(AdminPermissions.OrdersView);
        var member = await db.CreateUserAsync();

        await using (var scope = db.Scope())
        {
            var approvals = scope.ServiceProvider.GetRequiredService<IApprovalService>();
            Assert.False((await approvals.RequestStaffRolesAsync(new StaffRolesPayload(requester.UserId, [targetRole], null), requester)).IsSuccess()); // خودش
            Assert.False((await approvals.RequestStaffRolesAsync(new StaffRolesPayload(member, null, true), requester)).IsSuccess()); // مالک فقط با مالک

            var created = await approvals.RequestStaffRolesAsync(new StaffRolesPayload(member, [targetRole, targetRole], null), requester);
            Assert.True(created.IsSuccess(), created.Message);
            Assert.False((await scope.ServiceProvider.GetRequiredService<IAdminAccessService>().GetAccessAsync(member)).IsStaff);

            var decided = await approvals.DecideAsync(created.Data, true, null, approver);
            Assert.True(decided.IsSuccess(), decided.Message);
        }

        await using (var scope = db.Scope())
        {
            var granted = await scope.ServiceProvider.GetRequiredService<IAdminAccessService>().GetAccessAsync(member);
            Assert.True(granted.Has(AdminPermissions.OrdersView));
            Assert.False(granted.Has(AdminPermissions.StaffManage));
        }
    }

    [SqlFact]
    public async Task Requests_are_visible_only_to_matching_staff_and_expire()
    {
        var requester = await CreateStaffAsync(AdminPermissions.CustomersWallet);
        var approver = await CreateStaffAsync(AdminPermissions.CustomersWallet);
        var customer = await db.CreateUserAsync();
        var unrelated = new AdminAccess(Guid.NewGuid(), "انباردار", "09120000002", false, new HashSet<string> { AdminPermissions.OrdersView }, ["انبار"]);

        await using var scope = db.Scope();
        var approvals = scope.ServiceProvider.GetRequiredService<IApprovalService>();
        var created = await approvals.RequestWalletAdjustAsync(new WalletAdjustPayload(customer, 1_200_000, "تست دیده شدن", false), requester);
        Assert.True(created.IsSuccess(), created.Message);
        var id = created.Data;

        var pending = new ApprovalFilter(ApprovalStatus.Pending, false, 1, 100);
        Assert.Contains((await approvals.ListAsync(pending, approver)).Items, a => a.Id == id);
        Assert.Contains((await approvals.ListAsync(pending, Owner)).Items, a => a.Id == id);
        Assert.Contains((await approvals.ListAsync(new ApprovalFilter(null, true, 1, 100), requester)).Items, a => a.Id == id);
        Assert.DoesNotContain((await approvals.ListAsync(pending, unrelated)).Items, a => a.Id == id);
        Assert.DoesNotContain((await approvals.ListAsync(new ApprovalFilter(null, true, 1, 100), approver)).Items, a => a.Id == id);
        Assert.Equal(0, await approvals.CountDecidableAsync(unrelated));

        // فقط درخواست‌دهنده پس می‌گیرد.
        Assert.False((await approvals.CancelAsync(id, approver)).IsSuccess());

        // مهلت گذشته ← منقضی؛ دیگر قابل تأیید نیست.
        await Ctx(scope).ApprovalRequests.Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.ExpiresAt, DateTimeOffset.UtcNow.AddMinutes(-1)));
        Assert.True(await approvals.ExpireDueAsync(DateTimeOffset.UtcNow) >= 1);
        Assert.Equal(ApprovalStatus.Expired, (await approvals.GetAsync(id))!.Status);
        Assert.False((await approvals.DecideAsync(id, true, null, approver)).IsSuccess());
    }

    // ═══ TOTP ═══════════════════════════════════════════════════════════

    [SqlFact]
    public async Task Totp_setup_replay_protection_recovery_codes_lockout_and_owner_reset()
    {
        var staff = await CreateStaffAsync(AdminPermissions.OrdersView);
        var userId = staff.UserId;
        byte[] secret;
        List<string> recovery;

        await using (var scope = db.Scope())
        {
            var totp = scope.ServiceProvider.GetRequiredService<IStaffTotpService>();
            Assert.False(await totp.IsActiveAsync(userId));
            Assert.Equal(TotpCheck.NotActive, await totp.VerifyAsync(userId, "123456"));

            var setup = await totp.BeginSetupAsync(userId);
            Assert.True(setup.IsSuccess(), setup.Message);
            Assert.StartsWith("otpauth://totp/", setup.Data!.Uri);
            secret = Totp.FromBase32(setup.Data.Secret)!;
            Assert.Equal(20, secret.Length);
            Assert.True((await totp.GetStatusAsync(userId)).Pending);

            Assert.False((await totp.ConfirmSetupAsync(userId, WrongCode(secret))).IsSuccess());
            var confirm = await totp.ConfirmSetupAsync(userId, Totp.Compute(secret, Totp.StepAt(DateTimeOffset.UtcNow)));
            Assert.True(confirm.IsSuccess(), confirm.Message);
            recovery = confirm.Data!;
            Assert.Equal(10, recovery.Count);
        }

        await using (var scope = db.Scope())
        {
            var totp = scope.ServiceProvider.GetRequiredService<IStaffTotpService>();
            Assert.True(await totp.IsActiveAsync(userId));
            var status = await totp.GetStatusAsync(userId);
            Assert.True(status.Active);
            Assert.Equal(10, status.RecoveryCodesLeft);

            // کدی که برای فعال‌سازی مصرف شد دوباره پذیرفته نمی‌شود؛ کد گام بعد (اختلاف ساعت مجاز) پذیرفته می‌شود.
            var current = Totp.StepAt(DateTimeOffset.UtcNow);
            var lastUsed = await Ctx(scope).Users.Where(u => u.Id == userId).Select(u => u.TotpLastStep).SingleAsync();
            Assert.Equal(TotpCheck.Wrong, await totp.VerifyAsync(userId, Totp.Compute(secret, lastUsed!.Value)));
            Assert.Equal(TotpCheck.Ok, await totp.VerifyAsync(userId, Totp.Compute(secret, current + 1)));

            // کد بازیابی فقط یک بار کار می‌کند؛ جایی که بازیابی مجاز نیست اصلاً پذیرفته نمی‌شود.
            Assert.Equal(TotpCheck.Wrong, await totp.VerifyAsync(userId, recovery[0], allowRecovery: false));
            Assert.Equal(TotpCheck.RecoveryUsed, await totp.VerifyAsync(userId, recovery[0].ToUpperInvariant()));
            Assert.Equal(TotpCheck.Wrong, await totp.VerifyAsync(userId, recovery[0]));
            Assert.Equal(9, (await totp.GetStatusAsync(userId)).RecoveryCodesLeft);
        }

        await using (var scope = db.Scope())
        {
            var totp = scope.ServiceProvider.GetRequiredService<IStaffTotpService>();
            // ۵ کد نادرست پشت‌سرهم ← قفل ۱۰ دقیقه‌ای؛ در این مدت حتی کد درست هم رد می‌شود.
            var results = new List<TotpCheck>();
            for (var i = 0; i < StaffTotpService.MaxFailures; i++)
                results.Add(await totp.VerifyAsync(userId, WrongCode(secret)));
            Assert.Equal(TotpCheck.Locked, results[^1]);
            Assert.Equal(TotpCheck.Locked, await totp.VerifyAsync(userId, recovery[1]));

            // بازنشانی: فقط مالک، نه برای خودش.
            Assert.False((await totp.ResetAsync(userId, staff)).IsSuccess());
            var reset = await totp.ResetAsync(userId, Owner);
            Assert.True(reset.IsSuccess(), reset.Message);
            Assert.False(await totp.IsActiveAsync(userId));
            Assert.Equal(0, (await totp.GetStatusAsync(userId)).RecoveryCodesLeft);
            Assert.True(await Ctx(scope).SecurityLogs.AnyAsync(l => l.UserId == userId && l.Type == Fratak_Shop.Data.Entities.Admin.SecurityEventType.TotpDisabled));
        }
    }

    // ═══ کارهای شخصی ═══════════════════════════════════════════════════

    [SqlFact]
    public async Task Tasks_can_be_delegated_to_staff_only_and_deleted_only_by_their_creator()
    {
        var boss = await CreateStaffAsync(AdminPermissions.OrdersView);
        var colleague = await CreateStaffAsync(AdminPermissions.OrdersView);
        var customer = await db.CreateUserAsync();

        await using var scope = db.Scope();
        var tasks = scope.ServiceProvider.GetRequiredService<IStaffTaskService>();

        Assert.False((await tasks.SaveAsync(new StaffTaskInput(null, null, "  ", null, null, StaffTaskPriority.Normal, null), boss)).IsSuccess());
        Assert.False((await tasks.SaveAsync(new StaffTaskInput(null, customer, "به مشتری؟", null, null, StaffTaskPriority.Normal, null), boss)).IsSuccess());

        var due = DateTimeOffset.UtcNow.AddMinutes(-5);
        var created = await tasks.SaveAsync(new StaffTaskInput(null, colleague.UserId, "پیگیری سفارش ۱۰۲۴", "با @مشتری تماس بگیرید", due,
            StaffTaskPriority.High, "https://evil.com/x"), boss);
        Assert.True(created.IsSuccess(), created.Message);

        var saved = await Ctx(scope).StaffTasks.AsNoTracking().SingleAsync(t => t.Id == created.Data);
        Assert.Equal(colleague.UserId, saved.AssigneeId);
        Assert.Equal(boss.UserId, saved.CreatedById);
        Assert.Null(saved.Url); // لینک بیرونی ذخیره نمی‌شود

        Assert.Contains(await tasks.ListAsync(colleague.UserId, StaffTaskView.Open), t => t.Id == created.Data);
        Assert.Contains(await tasks.ListAsync(boss.UserId, StaffTaskView.Delegated), t => t.Id == created.Data);
        Assert.DoesNotContain(await tasks.ListAsync(boss.UserId, StaffTaskView.Open), t => t.Id == created.Data);
        Assert.True((await tasks.CountsAsync(colleague.UserId, DateTimeOffset.UtcNow)).Overdue >= 1);

        // یادآوری موعد فقط یک بار فرستاده می‌شود.
        Assert.True(await tasks.SendDueRemindersAsync(DateTimeOffset.UtcNow) >= 1);
        Assert.NotNull(await Ctx(scope).StaffTasks.AsNoTracking().Where(t => t.Id == created.Data).Select(t => t.ReminderSentAt).SingleAsync());
        var again = await Ctx(scope).StaffTasks.AsNoTracking().Where(t => t.Id == created.Data).Select(t => t.ReminderSentAt).SingleAsync();
        await tasks.SendDueRemindersAsync(DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(again, await Ctx(scope).StaffTasks.AsNoTracking().Where(t => t.Id == created.Data).Select(t => t.ReminderSentAt).SingleAsync());

        // گیرنده نمی‌تواند حذف کند ولی می‌تواند «انجام شد» بزند؛ غریبه هیچ‌کدام.
        var stranger = new AdminAccess(Guid.NewGuid(), "غریبه", "09120000003", false, new HashSet<string> { AdminPermissions.OrdersView }, ["x"]);
        Assert.False((await tasks.DeleteAsync(created.Data, colleague)).IsSuccess());
        Assert.False((await tasks.SetDoneAsync(created.Data, true, stranger)).IsSuccess());
        Assert.True((await tasks.SetDoneAsync(created.Data, true, colleague)).IsSuccess());
        Assert.Contains(await tasks.ListAsync(colleague.UserId, StaffTaskView.Done), t => t.Id == created.Data);

        Assert.True((await tasks.DeleteAsync(created.Data, boss)).IsSuccess());
        Assert.DoesNotContain(await tasks.ListAsync(boss.UserId, StaffTaskView.Delegated), t => t.Id == created.Data);
    }

    // ═══ تنظیمات ═══════════════════════════════════════════════════════

    [SqlTheory]
    [InlineData(1, 1_000_000d, "دقیقه")]
    [InlineData(500, 1_000_000d, "دقیقه")]
    [InlineData(15, -5d, "سقف")]
    public async Task Team_security_settings_are_validated(int reauthMinutes, double walletThreshold, string expectedWord)
    {
        await using var scope = db.Scope();
        var service = scope.ServiceProvider.GetRequiredService<IStoreSettingService>();
        var entity = await Ctx(scope).StoreSettings.AsNoTracking().OrderBy(s => s.CreatedAt).FirstAsync();
        var command = ToCommand(entity);
        command.ReauthMinutes = reauthMinutes;
        command.TwoPersonWalletThreshold = (decimal)walletThreshold;

        var result = await service.UpdateAsync(command);

        Assert.False(result.IsSuccess());
        Assert.Contains(expectedWord, result.Message);
    }

    /// <summary>همان کاری که صفحهٔ تنظیمات می‌کند: کپی همهٔ ویژگی‌های هم‌نام از ردیف فعلی تنظیمات.</summary>
    private static StoreSettingCommand ToCommand(StoreSetting entity)
    {
        var command = new StoreSettingCommand();
        foreach (var property in typeof(StoreSettingCommand).GetProperties().Where(p => p.CanWrite))
        {
            var source = typeof(StoreSetting).GetProperty(property.Name);
            if (source is not null && source.PropertyType == property.PropertyType)
                property.SetValue(command, source.GetValue(entity));
        }

        return command;
    }

    /// <summary>کد ۶ رقمی که مطمئناً در بازهٔ مجاز (گام فعلی ± ۱) درست نیست.</summary>
    private static string WrongCode(byte[] secret)
    {
        var step = Totp.StepAt(DateTimeOffset.UtcNow);
        var valid = Enumerable.Range(-2, 5).Select(d => Totp.Compute(secret, step + d)).ToHashSet();
        return Enumerable.Range(0, 10).Select(i => new string((char)('0' + i), 6)).First(c => !valid.Contains(c));
    }
}
