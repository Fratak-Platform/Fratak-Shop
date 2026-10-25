using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Customers;

public sealed class AdminCustomerRow
{
    public Guid Id { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string PhoneNumber { get; init; } = "";
    public string? Email { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastLoginAt { get; init; }
    public bool IsBlocked { get; init; }
    public bool IsStaff { get; init; }
    public int OrderCount { get; init; }
    public decimal TotalSpent { get; init; }
    public decimal Wallet { get; init; }
    public decimal WalletBonus { get; init; }
    public int Points { get; init; }
    public string FullName => string.IsNullOrWhiteSpace($"{FirstName} {LastName}") ? "بدون نام" : $"{FirstName} {LastName}".Trim();
}

[AdminPermission(AdminPermissions.CustomersView)]
public class IndexModel(IAdminDataService data, ICustomerCrmService crm) : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true)] public string? State { get; set; }
    [BindProperty(SupportsGet = true)] public string? From { get; set; }
    [BindProperty(SupportsGet = true)] public string? To { get; set; }
    [BindProperty(SupportsGet = true)] public string Sort { get; set; } = "newest";

    /// <summary>فیلتر برچسب CRM؛ مثلاً ?tag=عمده‌فروش فقط مشتری‌هایی با این برچسب را نشان می‌دهد.</summary>
    [BindProperty(SupportsGet = true)] public string? Tag { get; set; }

    /// <summary>برچسب‌های پرکاربرد برای فهرست کشویی فیلتر.</summary>
    public List<CustomerTagCount> PopularTags { get; private set; } = [];

    public PagedList<AdminCustomerRow> Customers { get; private set; } = new([], 0, 1, 30);

    public async Task<IActionResult> OnGetAsync([FromQuery(Name = "page")] int pageNumber = 1)
    {
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var phone = SearchText.NormalizeDigits(Search.Trim());
            if (phone.Length == 11 && phone.StartsWith("09"))
            {
                var id = await data.Query<User>().Where(u => u.PhoneNumber == phone).Select(u => (Guid?)u.Id).FirstOrDefaultAsync();
                if (id is { } userId)
                    return Redirect($"/admin/customers/{userId}");
            }
        }

        PopularTags = await crm.PopularTagsAsync(50);
        var query = Project(Filter());
        var page = ClampPage(pageNumber);
        var total = await query.CountAsync();
        var rows = await SortQuery(query).Skip((page - 1) * 30).Take(30).ToListAsync();
        Customers = new PagedList<AdminCustomerRow>(rows, total, page, 30);
        return Page();
    }

    public async Task<IActionResult> OnGetExportAsync(string format = "xlsx")
    {
        if (!CanAny(AdminPermissions.CustomersManage, AdminPermissions.FinanceExport))
            return Redirect("/admin/denied?need=" + AdminPermissions.CustomersManage + "|" + AdminPermissions.FinanceExport);

        var rows = await SortQuery(Project(Filter())).Take(50_000).ToListAsync();
        var table = new ExportTable("مشتریان", "نام", "موبایل", "ایمیل", "تاریخ عضویت", "آخرین ورود", "تعداد سفارش", "مجموع خرید", "کیف پول", "اعتبار هدیه", "امتیاز", "وضعیت");
        foreach (var c in rows)
            table.Add(c.FullName, c.PhoneNumber, c.Email, c.CreatedAt, c.LastLoginAt, c.OrderCount, c.TotalSpent, c.Wallet, c.WalletBonus, c.Points, c.IsBlocked ? "مسدود" : "فعال");
        return Exporter.File(table, format, "customers");
    }

    private IQueryable<User> Filter()
    {
        var query = data.Query<User>();
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = SearchText.NormalizeDigits(Search.Trim());
            query = query.Where(u => u.PhoneNumber.Contains(term) || (u.Email != null && u.Email.Contains(term))
                                     || (u.FirstName != null && u.FirstName.Contains(term)) || (u.LastName != null && u.LastName.Contains(term))
                                     || ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Contains(term));
        }
        var period = PersianDateInput.Period(From, To);
        if (period.From is { } from)
            query = query.Where(u => u.CreatedAt >= from);
        if (period.To is { } to)
            query = query.Where(u => u.CreatedAt < to);
        var tag = CustomerCrmService.NormalizeTag(Tag);
        if (tag.Length > 0)
        {
            var tags = data.Query<CustomerTag>();
            query = query.Where(u => tags.Any(t => t.UserId == u.Id && t.Tag == tag));
        }
        var staffRoles = data.Query<Data.Entities.Admin.UserAdminRole>();
        var inactiveSince = DateTimeOffset.UtcNow.AddDays(-90);
        query = State switch
        {
            "blocked" => query.Where(u => u.IsBlocked),
            "buyers" => query.Where(u => u.Orders.Any(o => o.PaymentStatus == PaymentStatus.Paid)),
            "no-orders" => query.Where(u => !u.Orders.Any()),
            "wallet" => query.Where(u => u.WalletBalance > 0 || u.WalletBonusBalance > 0),
            "staff" => query.Where(u => u.Role == UserRole.SuperAdmin || staffRoles.Any(r => r.UserId == u.Id)),
            "inactive" => query.Where(u => u.LastLoginAt == null || u.LastLoginAt < inactiveSince),
            _ => query,
        };
        return query;
    }

    private IQueryable<AdminCustomerRow> Project(IQueryable<User> query) => query.Select(u => new AdminCustomerRow
    {
        Id = u.Id,
        FirstName = u.FirstName,
        LastName = u.LastName,
        PhoneNumber = u.PhoneNumber,
        Email = u.Email,
        CreatedAt = u.CreatedAt,
        LastLoginAt = u.LastLoginAt,
        IsBlocked = u.IsBlocked,
        IsStaff = u.Role == UserRole.SuperAdmin,
        OrderCount = u.Orders.Count(),
        TotalSpent = u.Orders.Where(o => o.PaymentStatus == PaymentStatus.Paid && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Returned)
            .Sum(o => (decimal?)(o.PayableAmount - o.RefundedAmount)) ?? 0,
        Wallet = u.WalletBalance,
        WalletBonus = u.WalletBonusBalance,
        Points = u.LoyaltyPoints,
    });

    private IQueryable<AdminCustomerRow> SortQuery(IQueryable<AdminCustomerRow> query) => Sort switch
    {
        "spent" => query.OrderByDescending(c => c.TotalSpent),
        "orders" => query.OrderByDescending(c => c.OrderCount),
        "wallet" => query.OrderByDescending(c => c.Wallet + c.WalletBonus),
        "points" => query.OrderByDescending(c => c.Points),
        "login" => query.OrderByDescending(c => c.LastLoginAt),
        "oldest" => query.OrderBy(c => c.CreatedAt),
        _ => query.OrderByDescending(c => c.CreatedAt),
    };
}
