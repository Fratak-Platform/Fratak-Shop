using System.Text.Json;
using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.DTOs.Wallets;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Wallets;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Team;
using Fratak_Shop.Data.Entities.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Team;

// ═══════════════════════════════════════════════════════════════════════════
//  تأیید دونفره (مرحلهٔ هفتم – فاز ۱۰)
//  هدف: هیچ کارمندی به‌تنهایی نتواند کار پرخطر انجام دهد (مثلاً ۱۰ میلیون به کیف پول دوستش اضافه کند).
//  روند: صفحهٔ پنل به‌جای اجرای مستقیم، با RequestXxxAsync درخواست می‌سازد ← کارمندان دارای همان دسترسی اعلان می‌گیرند
//  ← در /admin/approvals یک نفر «دیگر» تأیید یا رد می‌کند ← در صورت تأیید همین‌جا اجرا و نتیجه ثبت می‌شود.
//  روشن/خاموش و سقف‌ها: تنظیمات ← امنیت کارکنان. مثال: سقف کیف پول ۱٬۰۰۰٬۰۰۰ یعنی تغییر ۹۹۰ هزار تومانی مستقیم
//  انجام می‌شود ولی تغییر ۱ میلیونی تأیید دوم می‌خواهد.
//  برای افزودن نوع جدید: یک مقدار به ApprovalKind، یک متد RequestXxxAsync، یک case در PermissionFor و ExecuteAsync.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>ورودی تغییر کیف پول (داخل PayloadJson). Amount علامت‌دار است: منفی = کسر.</summary>
public sealed record WalletAdjustPayload(Guid UserId, decimal Amount, string Description, bool Promotional);

/// <summary>ورودی پرداخت درخواست برداشت.</summary>
public sealed record WithdrawalPaidPayload(Guid WithdrawalId, string? AdminNote, string? BankTrackingNumber);

/// <summary>ورودی تغییر نقش‌ها (RoleIds) یا مالکیت (Owner) یک کارمند؛ فقط یکی از این دو پر است.</summary>
public sealed record StaffRolesPayload(Guid UserId, List<Guid>? RoleIds, bool? Owner);

/// <summary>فیلتر فهرست درخواست‌ها. Mine = فقط درخواست‌های خودم.</summary>
public sealed record ApprovalFilter(ApprovalStatus? Status, bool Mine, int Page, int Take = 30);

public interface IApprovalService
{
    /// <summary>
    /// آیا این تغییر کیف پول (قدر مطلق مبلغ) تأیید دوم لازم دارد؟ محل استفاده: Customers/Detail قبل از اجرای مستقیم.
    /// </summary>
    bool NeedsWalletApproval(StoreSettingDto settings, decimal absoluteAmount);

    /// <summary>آیا «پرداخت شد» این برداشت تأیید دوم لازم دارد؟ محل استفاده: Finance/Withdrawals.</summary>
    bool NeedsWithdrawalApproval(StoreSettingDto settings, WithdrawalStatus newStatus, decimal amount);

    /// <summary>آیا تغییر نقش/مالکیت کارکنان تأیید دوم لازم دارد؟ محل استفاده: Staff/Member.</summary>
    bool NeedsRoleApproval(StoreSettingDto settings);

    Task<OperationResult<Guid>> RequestWalletAdjustAsync(WalletAdjustPayload payload, AdminAccess actor);

    Task<OperationResult<Guid>> RequestWithdrawalPaidAsync(WithdrawalPaidPayload payload, AdminAccess actor);

    Task<OperationResult<Guid>> RequestStaffRolesAsync(StaffRolesPayload payload, AdminAccess actor);

    Task<PagedList<ApprovalRequest>> ListAsync(ApprovalFilter filter, AdminAccess actor);

    Task<ApprovalRequest?> GetAsync(Guid id);

    /// <summary>تعداد درخواست‌های در انتظاری که این کارمند می‌تواند تصمیم بگیرد (نشان کنار منو).</summary>
    Task<int> CountDecidableAsync(AdminAccess actor);

    /// <summary>
    /// تأیید یا رد. تأییدکننده باید شخص دیگری باشد و دسترسی همان کار را داشته باشد؛ برای رد، توضیح الزامی است.
    /// در صورت تأیید، کار همین‌جا اجرا می‌شود و نتیجه (موفق/ناموفق) در درخواست ثبت می‌شود.
    /// </summary>
    Task<OperationResult> DecideAsync(Guid id, bool approve, string? note, AdminAccess actor);

    /// <summary>پس گرفتن درخواست توسط خود درخواست‌دهنده (فقط در انتظار).</summary>
    Task<OperationResult> CancelAsync(Guid id, AdminAccess actor);

    /// <summary>درخواست‌های سررسیده را «منقضی» می‌کند. محل استفاده: TeamWorker (هر چند دقیقه).</summary>
    Task<int> ExpireDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>دسترسی لازم برای تأیید هر نوع درخواست.</summary>
    string PermissionFor(ApprovalKind kind);
}

public sealed class ApprovalService(
    FratakShopDbContext context,
    IWalletService wallet,
    IAdminAccessService accessService,
    IStaffNotificationService notifications,
    IMonitoringService monitoring,
    ILogger<ApprovalService> logger) : IApprovalService
{
    /// <summary>مهلت تصمیم‌گیری؛ بعد از آن درخواست منقضی می‌شود و باید دوباره ثبت شود.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(72);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool NeedsWalletApproval(StoreSettingDto settings, decimal absoluteAmount) =>
        settings.IsTwoPersonApprovalEnabled && Math.Abs(absoluteAmount) >= settings.TwoPersonWalletThreshold;

    public bool NeedsWithdrawalApproval(StoreSettingDto settings, WithdrawalStatus newStatus, decimal amount) =>
        settings.IsTwoPersonApprovalEnabled && newStatus == WithdrawalStatus.Paid && amount >= settings.TwoPersonWithdrawalThreshold;

    public bool NeedsRoleApproval(StoreSettingDto settings) =>
        settings.IsTwoPersonApprovalEnabled && settings.IsTwoPersonRoleChangeEnabled;

    public string PermissionFor(ApprovalKind kind) => kind switch
    {
        ApprovalKind.WalletAdjust => AdminPermissions.CustomersWallet,
        ApprovalKind.WithdrawalPaid => AdminPermissions.WithdrawalsManage,
        ApprovalKind.StaffRoles => AdminPermissions.StaffManage,
        _ => AdminPermissions.SystemManage,
    };

    public async Task<OperationResult<Guid>> RequestWalletAdjustAsync(WalletAdjustPayload payload, AdminAccess actor)
    {
        if (payload.Amount == 0 || Math.Abs(payload.Amount) > 1_000_000_000)
            return OperationResult<Guid>.Error("مبلغ معتبر وارد کنید.");
        if (string.IsNullOrWhiteSpace(payload.Description))
            return OperationResult<Guid>.Error("علت تغییر موجودی را بنویسید.");

        var customer = await context.Users.AsNoTracking().Where(u => u.Id == payload.UserId)
            .Select(u => new { u.FirstName, u.LastName, u.PhoneNumber }).FirstOrDefaultAsync();
        if (customer is null)
            return OperationResult<Guid>.NotFound("مشتری یافت نشد.");

        var name = $"{customer.FirstName} {customer.LastName}".Trim();
        var verb = payload.Amount > 0 ? "افزایش" : "کاهش";
        var summary = $"{verb} {Money(Math.Abs(payload.Amount))} کیف پول {(name.Length > 0 ? name : customer.PhoneNumber)}"
                      + (payload.Promotional && payload.Amount > 0 ? " (هدیه، غیرقابل برداشت)" : "") + $" – علت: {payload.Description.Trim()}";
        return await CreateAsync(ApprovalKind.WalletAdjust, summary, payload with { Description = payload.Description.Trim() },
            Math.Abs(payload.Amount), $"/admin/customers/{payload.UserId}#wallet", $"wallet:{payload.UserId:N}", actor);
    }

    public async Task<OperationResult<Guid>> RequestWithdrawalPaidAsync(WithdrawalPaidPayload payload, AdminAccess actor)
    {
        if (string.IsNullOrWhiteSpace(payload.BankTrackingNumber))
            return OperationResult<Guid>.Error("شماره پیگیری واریز را وارد کنید.");

        var request = await context.WithdrawalRequests.AsNoTracking().Where(w => w.Id == payload.WithdrawalId)
            .Select(w => new { w.Amount, w.Status, w.AccountHolder, w.User.PhoneNumber }).FirstOrDefaultAsync();
        if (request is null)
            return OperationResult<Guid>.NotFound("درخواست برداشت یافت نشد.");
        if (request.Status is not (WithdrawalStatus.Pending or WithdrawalStatus.Approved))
            return OperationResult<Guid>.Error("این درخواست برداشت دیگر قابل پرداخت نیست.");

        var summary = $"پرداخت برداشت {Money(request.Amount)} به {request.AccountHolder} ({request.PhoneNumber}) – پیگیری بانک: {payload.BankTrackingNumber}";
        return await CreateAsync(ApprovalKind.WithdrawalPaid, summary, payload, request.Amount,
            $"/admin/finance/withdrawals?status=&q={request.PhoneNumber}", $"withdrawal:{payload.WithdrawalId:N}", actor);
    }

    public async Task<OperationResult<Guid>> RequestStaffRolesAsync(StaffRolesPayload payload, AdminAccess actor)
    {
        if (payload.UserId == actor.UserId)
            return OperationResult<Guid>.Error("نقش‌ها یا مالکیت خودتان را نمی‌توانید تغییر دهید.");
        if (payload.Owner is not null && !actor.IsOwner)
            return OperationResult<Guid>.Error("فقط مالک فروشگاه می‌تواند مالک جدید تعیین کند.");
        if ((payload.RoleIds is null) == (payload.Owner is null))
            return OperationResult<Guid>.Error("درخواست نامعتبر است.");

        var member = await context.Users.AsNoTracking().Where(u => u.Id == payload.UserId)
            .Select(u => new { u.FirstName, u.LastName, u.PhoneNumber }).FirstOrDefaultAsync();
        if (member is null)
            return OperationResult<Guid>.NotFound("کاربر یافت نشد.");
        var name = $"{member.FirstName} {member.LastName}".Trim();
        if (name.Length == 0)
            name = member.PhoneNumber;

        string summary;
        if (payload.Owner is { } owner)
        {
            summary = owner ? $"مالک کردن {name} (دسترسی کامل به همه‌چیز)" : $"برداشتن مالکیت از {name}";
        }
        else
        {
            var ids = payload.RoleIds!.Distinct().ToList();
            var names = await context.AdminRoles.AsNoTracking().Where(r => ids.Contains(r.Id)).Select(r => r.Name).ToListAsync();
            if (names.Count != ids.Count)
                return OperationResult<Guid>.Error("یکی از نقش‌های انتخاب‌شده یافت نشد.");
            summary = names.Count == 0 ? $"برداشتن همهٔ نقش‌های {name}" : $"نقش‌های {name}: {string.Join("، ", names)}";
            payload = payload with { RoleIds = ids };
        }

        return await CreateAsync(ApprovalKind.StaffRoles, summary, payload, null, $"/admin/staff/member/{payload.UserId}", $"staff:{payload.UserId:N}", actor);
    }

    public async Task<PagedList<ApprovalRequest>> ListAsync(ApprovalFilter filter, AdminAccess actor)
    {
        // هر کارمند فقط درخواست‌های نوعی را می‌بیند که دسترسی تصمیمش را دارد، به‌علاوهٔ درخواست‌های خودش.
        // مثال: حسابدار (تسویهٔ برداشت) درخواست‌های «تغییر نقش کارکنان» را نمی‌بیند.
        var actorId = actor.UserId;
        var kinds = Enum.GetValues<ApprovalKind>().Where(k => actor.Has(PermissionFor(k))).ToList();
        var query = context.ApprovalRequests.AsNoTracking().Where(a => kinds.Contains(a.Kind) || a.RequestedById == actorId);
        if (filter.Status is { } status)
            query = query.Where(a => a.Status == status);
        if (filter.Mine)
            query = query.Where(a => a.RequestedById == actorId);

        var total = await query.CountAsync();
        var take = Math.Clamp(filter.Take, 5, 100);
        var page = Math.Clamp(filter.Page, 1, Math.Max(1, (total + take - 1) / take));
        var items = await query.OrderBy(a => a.Status == ApprovalStatus.Pending ? 0 : 1).ThenByDescending(a => a.CreatedAt)
            .Skip((page - 1) * take).Take(take).ToListAsync();
        return new PagedList<ApprovalRequest>(items, total, page, take);
    }

    public Task<ApprovalRequest?> GetAsync(Guid id) => context.ApprovalRequests.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id);

    public async Task<int> CountDecidableAsync(AdminAccess actor)
    {
        var kinds = Enum.GetValues<ApprovalKind>().Where(k => actor.Has(PermissionFor(k))).ToList();
        if (kinds.Count == 0)
            return 0;
        var now = DateTimeOffset.UtcNow;
        return await context.ApprovalRequests.CountAsync(a => a.Status == ApprovalStatus.Pending && a.ExpiresAt > now
                                                              && a.RequestedById != actor.UserId && kinds.Contains(a.Kind));
    }

    public async Task<OperationResult> DecideAsync(Guid id, bool approve, string? note, AdminAccess actor)
    {
        var request = await context.ApprovalRequests.AsTracking().FirstOrDefaultAsync(a => a.Id == id);
        if (request is null)
            return OperationResult.NotFound("درخواست یافت نشد.");
        if (request.Status != ApprovalStatus.Pending)
            return OperationResult.Error("دربارهٔ این درخواست قبلاً تصمیم گرفته شده است.");
        if (request.ExpiresAt <= DateTimeOffset.UtcNow)
            return OperationResult.Error("مهلت این درخواست تمام شده است؛ درخواست‌دهنده باید دوباره ثبت کند.");
        if (request.RequestedById == actor.UserId)
            return OperationResult.Error("درخواست خودتان را نمی‌توانید تأیید یا رد کنید؛ یک همکار دیگر باید تصمیم بگیرد.");
        if (!actor.Has(PermissionFor(request.Kind)))
            return OperationResult.Error("برای تصمیم دربارهٔ این درخواست، دسترسی همان کار را ندارید.");

        var cleanNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim()[..Math.Min(note.Trim().Length, 500)];
        if (!approve && cleanNote is null)
            return OperationResult.Error("علت رد را بنویسید تا درخواست‌دهنده بداند چه چیزی را اصلاح کند.");

        // اول «تصمیم» را با کنترل هم‌زمانی ثبت می‌کنیم تا دو تأییدکننده هم‌زمان دو بار اجرا نکنند.
        var claimed = await context.ApprovalRequests
            .Where(a => a.Id == id && a.Status == ApprovalStatus.Pending)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected)
                .SetProperty(a => a.DecidedById, actor.UserId)
                .SetProperty(a => a.DecidedByName, actor.DisplayName)
                .SetProperty(a => a.DecidedAt, DateTimeOffset.UtcNow)
                .SetProperty(a => a.DecisionNote, cleanNote)
                .SetProperty(a => a.UpdatedAt, DateTimeOffset.UtcNow));
        if (claimed != 1)
            return OperationResult.Error("همکار دیگری هم‌زمان دربارهٔ این درخواست تصمیم گرفت.");
        context.Entry(request).State = EntityState.Detached;

        if (!approve)
        {
            await notifications.NotifyUserAsync(request.RequestedById, StaffNotificationKind.Approval, "درخواست شما رد شد",
                $"{Cut(request.Summary, 300)} – علت: {cleanNote}", $"/admin/approvals?id={request.Id}");
            await monitoring.LogActionAsync("ApprovalRejected", nameof(ApprovalRequest), request.Id, $"رد درخواست: {Cut(request.Summary, 400)}");
            return OperationResult.Success("درخواست رد شد و به درخواست‌دهنده خبر داده شد.");
        }

        OperationResult result;
        try
        {
            result = await ExecuteAsync(request, actor);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Executing approval {Id} ({Kind}) failed", request.Id, request.Kind);
            context.ChangeTracker.Clear();
            result = OperationResult.Error("اجرای کار با خطای غیرمنتظره روبه‌رو شد؛ جزئیات در «خطاها».");
        }

        var ok = result.IsSuccess();
        await context.ApprovalRequests.Where(a => a.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(a => a.Status, ok ? ApprovalStatus.Approved : ApprovalStatus.Failed)
            .SetProperty(a => a.ResultMessage, Cut(result.Message, 1000)));

        await notifications.NotifyUserAsync(request.RequestedById, StaffNotificationKind.Approval,
            ok ? "درخواست شما تأیید و انجام شد" : "درخواست تأیید شد ولی انجام نشد",
            $"{Cut(request.Summary, 300)} – {actor.DisplayName}" + (ok ? "" : $" – {result.Message}"), $"/admin/approvals?id={request.Id}");
        await monitoring.LogActionAsync("ApprovalApproved", nameof(ApprovalRequest), request.Id,
            $"تأیید درخواست {request.RequestedByName}: {Cut(request.Summary, 380)}");

        return ok
            ? OperationResult.Success($"تأیید و انجام شد: {result.Message}")
            : OperationResult.Error($"تأیید ثبت شد ولی اجرا ناموفق بود: {result.Message}");
    }

    public async Task<OperationResult> CancelAsync(Guid id, AdminAccess actor)
    {
        var updated = await context.ApprovalRequests
            .Where(a => a.Id == id && a.RequestedById == actor.UserId && a.Status == ApprovalStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, ApprovalStatus.Cancelled)
                .SetProperty(a => a.DecidedAt, DateTimeOffset.UtcNow)
                .SetProperty(a => a.UpdatedAt, DateTimeOffset.UtcNow));
        return updated == 1
            ? OperationResult.Success("درخواست پس گرفته شد.")
            : OperationResult.Error("فقط درخواست‌های در انتظارِ خودتان را می‌توانید پس بگیرید.");
    }

    public Task<int> ExpireDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default) =>
        context.ApprovalRequests.Where(a => a.Status == ApprovalStatus.Pending && a.ExpiresAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, ApprovalStatus.Expired).SetProperty(a => a.UpdatedAt, now), cancellationToken);

    /// <summary>ثبت درخواست مشترک همهٔ نوع‌ها + اعلان به تأییدکننده‌های ممکن.</summary>
    private async Task<OperationResult<Guid>> CreateAsync<TPayload>(ApprovalKind kind, string summary, TPayload payload, decimal? amount,
        string targetUrl, string targetKey, AdminAccess actor)
    {
        var now = DateTimeOffset.UtcNow;
        if (await context.ApprovalRequests.AnyAsync(a => a.TargetKey == targetKey && a.Status == ApprovalStatus.Pending && a.ExpiresAt > now))
            return OperationResult<Guid>.Error("برای همین مورد یک درخواست در انتظار تأیید وجود دارد؛ اول تکلیف آن روشن شود (صفحهٔ «تأییدها»).");

        // اگر هیچ همکار دیگری دسترسی تأیید نداشته باشد، درخواست هرگز تأیید نمی‌شود؛ از اول خبر بدهیم.
        var permission = PermissionFor(kind);
        var approvers = await notifications.GetRecipientsAsync([permission]);
        approvers.Remove(actor.UserId);
        if (approvers.Count == 0)
            return OperationResult<Guid>.Error("هیچ کارمند دیگری دسترسی تأیید این کار را ندارد. یا به یک همکار این دسترسی را بدهید، یا «تأیید دونفره» را در تنظیمات امنیت کارکنان خاموش کنید.");

        var request = new ApprovalRequest
        {
            Kind = kind,
            Summary = Cut(summary, 500)!,
            PayloadJson = JsonSerializer.Serialize(payload, Json),
            Amount = amount,
            TargetUrl = Cut(targetUrl, 300),
            TargetKey = targetKey,
            RequestedById = actor.UserId,
            RequestedByName = Cut(actor.DisplayName, 150)!,
            ExpiresAt = now + Lifetime,
        };
        context.ApprovalRequests.Add(request);
        await context.SaveChangesAsync();

        await notifications.NotifyAsync(new StaffAlert(StaffNotificationKind.Approval, $"درخواست تأیید از {actor.DisplayName}",
            Cut(summary, 450), $"/admin/approvals?id={request.Id}", [permission], actor.UserId));
        return OperationResult<Guid>.Success(request.Id, "درخواست ثبت شد و بعد از تأیید یک همکار دیگر انجام می‌شود (صفحهٔ «تأییدها»).");
    }

    /// <summary>اجرای واقعی کار بعد از تأیید. هر نوع، همان سرویسی را صدا می‌زند که صفحهٔ پنل مستقیم صدا می‌زد.</summary>
    private async Task<OperationResult> ExecuteAsync(ApprovalRequest request, AdminAccess approver)
    {
        switch (request.Kind)
        {
            case ApprovalKind.WalletAdjust:
            {
                var payload = JsonSerializer.Deserialize<WalletAdjustPayload>(request.PayloadJson, Json);
                if (payload is null)
                    return OperationResult.Error("اطلاعات درخواست خراب است.");
                var text = $"{payload.Description} – {request.RequestedByName} (تأیید: {approver.DisplayName})";
                return await wallet.AdjustBalanceAsync(payload.UserId, payload.Amount, text.Length > 450 ? text[..450] : text, payload.Promotional);
            }
            case ApprovalKind.WithdrawalPaid:
            {
                var payload = JsonSerializer.Deserialize<WithdrawalPaidPayload>(request.PayloadJson, Json);
                if (payload is null)
                    return OperationResult.Error("اطلاعات درخواست خراب است.");
                var result = await wallet.ChangeWithdrawalStatusAsync(new ChangeWithdrawalStatusCommand
                {
                    Id = payload.WithdrawalId,
                    Status = WithdrawalStatus.Paid,
                    AdminNote = payload.AdminNote,
                    BankTrackingNumber = payload.BankTrackingNumber,
                });
                if (result.IsSuccess())
                    await monitoring.LogActionAsync("WithdrawalStatus", "WithdrawalRequest", payload.WithdrawalId,
                        $"پرداخت برداشت (درخواست {request.RequestedByName}، تأیید {approver.DisplayName})");
                return result;
            }
            case ApprovalKind.StaffRoles:
            {
                var payload = JsonSerializer.Deserialize<StaffRolesPayload>(request.PayloadJson, Json);
                if (payload is null)
                    return OperationResult.Error("اطلاعات درخواست خراب است.");

                // تغییر با دسترسی «درخواست‌دهنده» اجرا می‌شود: اگر در این فاصله دسترسی‌اش کم شده باشد، اجرا نمی‌شود.
                var requester = await accessService.GetAccessAsync(request.RequestedById);
                if (!requester.Has(AdminPermissions.StaffManage))
                    return OperationResult.Error("درخواست‌دهنده دیگر دسترسی مدیریت کارکنان را ندارد.");
                return payload.Owner is { } owner
                    ? await accessService.SetOwnerAsync(payload.UserId, owner, requester)
                    : await accessService.SetUserRolesAsync(payload.UserId, payload.RoleIds ?? [], requester);
            }
            default:
                return OperationResult.Error("نوع درخواست شناخته نشد.");
        }
    }

    private static string Money(decimal amount) => amount.ToString("#,0", System.Globalization.CultureInfo.InvariantCulture) + " تومان";

    private static string? Cut(string? value, int max) => value is null ? null : value.Length <= max ? value : value[..max];
}
