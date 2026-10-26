using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Wallets;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Loyalty;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Support;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Data.Entities.Wallets;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Customers;

[AdminPermission(AdminPermissions.CustomersView)]
public class DetailModel(
    IAdminDataService data,
    IAdminCustomerService customers,
    IWalletService wallet,
    IAdminAccessService access,
    ICustomerCrmService crm,
    global::Fratak_Shop.Core.Services.Team.IApprovalService approvals,
    global::Fratak_Shop.Core.Services.Team.IMentionService mentions,
    global::Fratak_Shop.Core.Services.Settings.IStoreSettingService settingsService) : AdminPageModel
{
    public User Customer { get; private set; } = null!;
    public List<Order> Orders { get; private set; } = [];
    public List<UserAddress> Addresses { get; private set; } = [];
    public Dictionary<Guid, string> LocationNames { get; private set; } = new();
    public List<WalletTransaction> WalletTransactions { get; private set; } = [];
    public List<LoyaltyTransaction> PointTransactions { get; private set; } = [];
    public List<SupportTicket> Tickets { get; private set; } = [];
    public List<SecurityLog> SecurityEvents { get; private set; } = [];
    public List<string> RoleNames { get; private set; } = [];
    public int OrderCount { get; private set; }
    public decimal TotalSpent { get; private set; }
    public int CommentCount { get; private set; }
    public int ReferralCount { get; private set; }
    public string? ReferredBy { get; private set; }

    // ── CRM (مرحلهٔ هفتم – فاز ۴) ──
    /// <summary>برچسب‌های دستی کارکنان روی این مشتری.</summary>
    public List<CustomerTagDto> Tags { get; private set; } = [];

    /// <summary>برچسب‌های پرکاربرد فروشگاه برای پیشنهاد هنگام تایپ (datalist).</summary>
    public List<CustomerTagCount> TagSuggestions { get; private set; } = [];

    /// <summary>یادداشت‌های داخلی؛ سنجاق‌شده‌ها اول.</summary>
    public List<CustomerNoteDto> Notes { get; private set; } = [];

    /// <summary>بخش‌بندی RFM مشتری (تازگی، دفعات، مبلغ خرید).</summary>
    public CustomerRfm Rfm { get; private set; } = null!;

    /// <summary>کلاس رنگ نشان هر بخش RFM؛ سبز = خوب، زرد = نیاز به اقدام، قرمز = فوری، خاکستری = غیرفعال.</summary>
    public static string SegmentBadge(CustomerSegment segment) => segment switch
    {
        CustomerSegment.Champion => "a-badge-green",
        CustomerSegment.Loyal => "a-badge-blue",
        CustomerSegment.New => "a-badge-sky",
        CustomerSegment.Promising => "a-badge-purple",
        CustomerSegment.AtRisk or CustomerSegment.NeedsAttention => "a-badge-yellow",
        CustomerSegment.CantLose => "a-badge-red",
        _ => "a-badge-gray",
    };

    public async Task<IActionResult> OnGetAsync(Guid id)
    {
        var user = await data.Query<User>().FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
            return NotFound();
        Customer = user;

        var orders = data.Query<Order>().Where(o => o.UserId == id);
        OrderCount = await orders.CountAsync();
        TotalSpent = await orders.Where(o => o.PaymentStatus == PaymentStatus.Paid && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Returned)
            .SumAsync(o => (decimal?)(o.PayableAmount - o.RefundedAmount)) ?? 0;
        Orders = await orders.OrderByDescending(o => o.CreatedAt).Take(15).ToListAsync();
        Addresses = await data.Query<UserAddress>().Where(a => a.UserId == id).OrderByDescending(a => a.IsDefault).ToListAsync();
        var locationIds = Addresses.SelectMany(a => new[] { a.ProvinceId, a.CityId }).Distinct().ToList();
        var locations = await data.Query<Data.Entities.Locations.Location>().Where(l => locationIds.Contains(l.Id)).Select(l => new { l.Id, l.Name }).ToListAsync();
        LocationNames = locations.ToDictionary(l => l.Id, l => l.Name);
        if (CanAny(AdminPermissions.CustomersWallet, AdminPermissions.FinanceView))
            WalletTransactions = await data.Query<WalletTransaction>().Where(t => t.UserId == id).OrderByDescending(t => t.CreatedAt).Take(15).ToListAsync();
        PointTransactions = await data.Query<LoyaltyTransaction>().Where(t => t.UserId == id).OrderByDescending(t => t.CreatedAt).Take(10).ToListAsync();
        if (Can(AdminPermissions.TicketsManage))
            Tickets = await data.Query<SupportTicket>().Where(t => t.UserId == id).OrderByDescending(t => t.LastActivityAt).Take(10).ToListAsync();
        if (CanAny(AdminPermissions.AuditView, AdminPermissions.MonitoringView))
            SecurityEvents = await data.Query<SecurityLog>().Where(s => s.UserId == id).OrderByDescending(s => s.CreatedAt).Take(12).ToListAsync();
        RoleNames = (await access.GetUserRolesAsync(id)).Select(r => r.Name).ToList();
        CommentCount = await data.Query<Data.Entities.Comments.Comment>().CountAsync(c => c.UserId == id);
        ReferralCount = await data.Query<User>().CountAsync(u => u.ReferredByUserId == id);
        if (user.ReferredByUserId is { } referrerId)
            ReferredBy = await data.Query<User>().Where(u => u.Id == referrerId).Select(u => u.PhoneNumber).FirstOrDefaultAsync();
        Tags = await crm.GetTagsAsync(id);
        Notes = await crm.GetNotesAsync(id);
        Rfm = await crm.GetRfmAsync(id);
        if (Can(AdminPermissions.CustomersManage))
            TagSuggestions = await crm.PopularTagsAsync(50);
        return Page();
    }

    [AdminPermission(AdminPermissions.CustomersManage)]
    public async Task<IActionResult> OnPostProfileAsync(Guid id, string? firstName, string? lastName, string? email, string? adminNote)
    {
        // فاز ۱۰: کارمندی که ایمیل را پوشانده می‌بیند، فیلد ایمیل ندارد؛ ایمیل فعلی دست نمی‌خورد.
        if (!Request.Form.ContainsKey("email"))
            email = await data.Query<User>().Where(u => u.Id == id).Select(u => u.Email).FirstOrDefaultAsync();
        ShowResult(await customers.UpdateProfileAsync(id, firstName, lastName, email, adminNote));
        return Redirect($"/admin/customers/{id}");
    }

    /// <summary>افزودن برچسب CRM (مثلاً «عمده‌فروش»).</summary>
    [AdminPermission(AdminPermissions.CustomersManage)]
    public async Task<IActionResult> OnPostTagAsync(Guid id, string? tag)
    {
        ShowResult(await crm.AddTagAsync(id, tag, ActorName));
        return Redirect($"/admin/customers/{id}#crm");
    }

    /// <summary>برداشتن یک برچسب.</summary>
    [AdminPermission(AdminPermissions.CustomersManage)]
    public async Task<IActionResult> OnPostUntagAsync(Guid id, Guid tagId)
    {
        ShowResult(await crm.RemoveTagAsync(id, tagId));
        return Redirect($"/admin/customers/{id}#crm");
    }

    /// <summary>ثبت یادداشت داخلی (اختیاری: سنجاق بالای صفحه).</summary>
    [AdminPermission(AdminPermissions.CustomersManage)]
    public async Task<IActionResult> OnPostNoteAsync(Guid id, string? content, bool pinned = false)
    {
        var result = await crm.AddNoteAsync(id, content, pinned, ActorId, ActorName);
        ShowResult(result);
        // فاز ۱۰: همکارانی که با @نام در یادداشت آمده‌اند اعلان می‌گیرند.
        if (Succeeded(result))
            await mentions.NotifyAsync(content, "یادداشت مشتری", $"/admin/customers/{id}#notes", Access);
        return Redirect($"/admin/customers/{id}#notes");
    }

    /// <summary>سنجاق کردن / برداشتن سنجاق یادداشت.</summary>
    [AdminPermission(AdminPermissions.CustomersManage)]
    public async Task<IActionResult> OnPostPinAsync(Guid id, Guid noteId)
    {
        ShowResult(await crm.TogglePinAsync(id, noteId));
        return Redirect($"/admin/customers/{id}#notes");
    }

    /// <summary>حذف یادداشت: فقط نویسنده یا مالک فروشگاه.</summary>
    [AdminPermission(AdminPermissions.CustomersManage)]
    public async Task<IActionResult> OnPostDeleteNoteAsync(Guid id, Guid noteId)
    {
        ShowResult(await crm.DeleteNoteAsync(id, noteId, ActorId, Access.IsOwner));
        return Redirect($"/admin/customers/{id}#notes");
    }

    [AdminPermission(AdminPermissions.CustomersManage)]
    public async Task<IActionResult> OnPostBlockAsync(Guid id, bool blocked, string? reason)
    {
        ShowResult(await customers.SetBlockedAsync(id, blocked, reason, Access));
        return Redirect($"/admin/customers/{id}");
    }

    [AdminPermission(AdminPermissions.CustomersManage)]
    [global::Fratak_Shop.Web.Infrastructure.Team.RequireReauth]
    public async Task<IActionResult> OnPostSessionsAsync(Guid id)
    {
        ShowResult(await customers.RevokeSessionsAsync(id, Access));
        return Redirect($"/admin/customers/{id}");
    }

    [AdminPermission(AdminPermissions.CustomersWallet)]
    [global::Fratak_Shop.Web.Infrastructure.Team.RequireReauth]
    public async Task<IActionResult> OnPostWalletAsync(Guid id, decimal amount, string direction, string? description, bool promotional = true)
    {
        if (amount <= 0 || amount > 1_000_000_000)
        {
            ErrorMessage = "مبلغ معتبر وارد کنید.";
            return Redirect($"/admin/customers/{id}#wallet");
        }
        if (string.IsNullOrWhiteSpace(description))
        {
            ErrorMessage = "علت تغییر موجودی را بنویسید (در تاریخچه مشتری نمایش داده می‌شود).";
            return Redirect($"/admin/customers/{id}#wallet");
        }

        var signed = direction == "debit" ? -amount : amount;

        // فاز ۱۰: مبلغ بزرگ‌تر از سقف «تأیید دونفره» → فقط درخواست ثبت می‌شود و بعد از تأیید همکار دوم انجام می‌شود.
        if (approvals.NeedsWalletApproval(await settingsService.GetAsync(), amount))
        {
            var request = await approvals.RequestWalletAdjustAsync(
                new global::Fratak_Shop.Core.Services.Team.WalletAdjustPayload(id, signed, description.Trim(), promotional), Access);
            ShowResult(request);
            return Redirect($"/admin/customers/{id}#wallet");
        }

        var text = $"{description.Trim()} – {ActorName}";
        ShowResult(await wallet.AdjustBalanceAsync(id, signed, text.Length > 450 ? text[..450] : text, promotional));
        return Redirect($"/admin/customers/{id}#wallet");
    }

    [AdminPermission(AdminPermissions.LoyaltyManage, AdminPermissions.CustomersManage)]
    public async Task<IActionResult> OnPostPointsAsync(Guid id, int points, string direction, string? reason)
    {
        var signed = direction == "remove" ? -Math.Abs(points) : Math.Abs(points);
        ShowResult(await customers.AdjustPointsAsync(id, signed, reason));
        return Redirect($"/admin/customers/{id}#points");
    }
}
