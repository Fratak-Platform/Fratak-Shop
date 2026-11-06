using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.GiftCards;

public sealed class AdminGiftCardRow
{
    public Guid Id { get; init; }
    public string Reference { get; init; } = "";
    public string CodeLast4 { get; init; } = "";
    public decimal Amount { get; init; }
    public GiftCardStatus Status { get; init; }
    public GiftCardPaymentMethod PaymentMethod { get; init; }
    public Guid PurchaserId { get; init; }
    public string PurchaserName { get; init; } = "";
    public string PurchaserPhone { get; init; } = "";
    public string? RecipientName { get; init; }
    public Guid? RedeemedById { get; init; }
    public string? RedeemedByPhone { get; init; }
    public DateTimeOffset? ActivatedAt { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public DateTimeOffset? RedeemedAt { get; init; }
    public bool RedeemedAsWithdrawable { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public sealed record GiftCardTotals(int SoldCount, decimal SoldAmount, int ActiveCount, decimal ActiveAmount, int RedeemedCount, decimal RedeemedAmount, int ExpiredCount, decimal ExpiredAmount);

[AdminPermission(AdminPermissions.GiftCardsManage, AdminPermissions.FinanceView)]
public class IndexModel(IAdminDataService data) : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "status")] public GiftCardStatus? Status { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [BindProperty(SupportsGet = true, Name = "from")] public string? From { get; set; }
    [BindProperty(SupportsGet = true, Name = "to")] public string? To { get; set; }
    [FromQuery(Name = "page")] public int PageNumber { get; set; } = 1;

    public GiftCardTotals Totals { get; private set; } = new(0, 0, 0, 0, 0, 0, 0, 0);
    public PagedList<AdminGiftCardRow> Items { get; private set; } = new([], 0, 1, 30);

    public async Task OnGetAsync()
    {
        var all = data.Query<GiftCard>();
        var groups = await all.GroupBy(g => g.Status).Select(g => new { g.Key, Count = g.Count(), Amount = g.Sum(x => x.Amount) }).ToListAsync();
        int C(params GiftCardStatus[] s) => groups.Where(g => s.Contains(g.Key)).Sum(g => g.Count);
        decimal A(params GiftCardStatus[] s) => groups.Where(g => s.Contains(g.Key)).Sum(g => g.Amount);
        var sold = new[] { GiftCardStatus.Active, GiftCardStatus.Redeemed, GiftCardStatus.Expired };
        Totals = new GiftCardTotals(C(sold), A(sold), C(GiftCardStatus.Active), A(GiftCardStatus.Active), C(GiftCardStatus.Redeemed), A(GiftCardStatus.Redeemed),
            C(GiftCardStatus.Expired), A(GiftCardStatus.Expired));

        var query = Filtered();
        var total = await query.CountAsync();
        var pageNumber = ClampPage(PageNumber, total, 30);
        var rows = await Project(query.OrderByDescending(g => g.CreatedAt).Skip((pageNumber - 1) * 30).Take(30)).ToListAsync();
        Items = new PagedList<AdminGiftCardRow>(rows, total, pageNumber, 30);
    }

    public async Task<IActionResult> OnGetExportAsync(string format = "xlsx")
    {
        var rows = await Project(Filtered().OrderByDescending(g => g.CreatedAt).Take(50000)).ToListAsync();
        var table = new ExportTable("کارت‌های هدیه", "تاریخ", "شناسه", "۴ رقم آخر", "مبلغ", "وضعیت", "پرداخت", "خریدار", "موبایل خریدار", "گیرنده", "استفاده‌کننده", "تاریخ استفاده", "انقضا");
        foreach (var r in rows)
            table.Add(Format.DateTime(r.CreatedAt), r.Reference, r.CodeLast4, r.Amount.ToString("0"), StatusTitle(r.Status), r.PaymentMethod == GiftCardPaymentMethod.Wallet ? "کیف پول" : "درگاه",
                r.PurchaserName, r.PurchaserPhone, r.RecipientName ?? "", r.RedeemedByPhone ?? "", r.RedeemedAt is { } ra ? Format.DateTime(ra) : "", r.ExpiresAt is { } e ? Format.Date(e) : "");
        return Exporter.File(table, format, "gift-cards");
    }

    private IQueryable<GiftCard> Filtered()
    {
        var query = data.Query<GiftCard>();
        if (Status is { } status) query = query.Where(g => g.Status == status);
        var period = PersianDateInput.Period(From, To);
        if (period.From is { } from) query = query.Where(g => g.CreatedAt >= from);
        if (period.To is { } to) query = query.Where(g => g.CreatedAt < to);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = SearchText.NormalizeDigits(Search.Trim());
            query = query.Where(g => g.Reference.Contains(term) || g.CodeLast4 == term || g.Purchaser.PhoneNumber.Contains(term)
                                     || (g.RedeemedBy != null && g.RedeemedBy.PhoneNumber.Contains(term)) || (g.RecipientName != null && g.RecipientName.Contains(term)));
        }
        return query;
    }

    private static IQueryable<AdminGiftCardRow> Project(IQueryable<GiftCard> query) => query.Select(g => new AdminGiftCardRow
    {
        Id = g.Id, Reference = g.Reference, CodeLast4 = g.CodeLast4, Amount = g.Amount, Status = g.Status, PaymentMethod = g.PaymentMethod,
        PurchaserId = g.PurchaserUserId, PurchaserName = ((g.Purchaser.FirstName ?? "") + " " + (g.Purchaser.LastName ?? "")).Trim(), PurchaserPhone = g.Purchaser.PhoneNumber,
        RecipientName = g.RecipientName, RedeemedById = g.RedeemedByUserId, RedeemedByPhone = g.RedeemedBy != null ? g.RedeemedBy.PhoneNumber : null,
        ActivatedAt = g.ActivatedAt, ExpiresAt = g.ExpiresAt, RedeemedAt = g.RedeemedAt, RedeemedAsWithdrawable = g.RedeemedAsWithdrawable, CreatedAt = g.CreatedAt,
    });

    public static string StatusTitle(GiftCardStatus status) => status switch
    {
        GiftCardStatus.PendingPayment => "در انتظار پرداخت",
        GiftCardStatus.Active => "فعال – استفاده‌نشده",
        GiftCardStatus.Redeemed => "استفاده‌شده",
        GiftCardStatus.Cancelled => "لغو",
        GiftCardStatus.Expired => "منقضی",
        _ => "—",
    };

    public static string StatusBadge(GiftCardStatus status) => status switch
    {
        GiftCardStatus.Active => "a-badge-blue",
        GiftCardStatus.Redeemed => "a-badge-green",
        GiftCardStatus.PendingPayment => "a-badge-yellow",
        GiftCardStatus.Expired => "a-badge-red",
        _ => "a-badge-gray",
    };
}
