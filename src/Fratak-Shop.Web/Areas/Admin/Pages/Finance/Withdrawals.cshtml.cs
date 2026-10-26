using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.DTOs.Wallets;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Wallets;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Wallets;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Finance;

public sealed class AdminWithdrawalRow
{
    public Guid Id { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public Guid UserId { get; init; }
    public string UserName { get; init; } = "";
    public string Phone { get; init; } = "";
    public decimal Amount { get; init; }
    public string AccountHolder { get; init; } = "";
    public string? Iban { get; init; }
    public string? CardNumber { get; init; }
    public string? AccountNumber { get; init; }
    public WithdrawalStatus Status { get; init; }
    public string? AdminNote { get; init; }
    public string? BankTrackingNumber { get; init; }
    public DateTimeOffset? ProcessedAt { get; init; }
    public decimal CurrentWallet { get; init; }
}

[AdminPermission(AdminPermissions.WithdrawalsManage, AdminPermissions.FinanceView)]
public class WithdrawalsModel(
    IAdminDataService data,
    IWalletService wallet,
    IMonitoringService monitoring,
    global::Fratak_Shop.Core.Services.Team.IApprovalService approvals,
    global::Fratak_Shop.Core.Services.Settings.IStoreSettingService settingsService,
    global::Fratak_Shop.Web.Infrastructure.Team.SensitiveData pii) : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "status")] public WithdrawalStatus? Status { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [FromQuery(Name = "page")] public int PageNumber { get; set; } = 1;

    public PagedList<AdminWithdrawalRow> Items { get; private set; } = new([], 0, 1, 30);
    public Dictionary<WithdrawalStatus, (int Count, decimal Amount)> Counts { get; private set; } = new();

    public async Task OnGetAsync()
    {
        Counts = (await data.Query<WithdrawalRequest>().GroupBy(w => w.Status)
                .Select(g => new { g.Key, Count = g.Count(), Amount = g.Sum(w => w.Amount) }).ToListAsync())
            .ToDictionary(x => x.Key, x => (x.Count, x.Amount));

        if (Status is null && !Request.Query.ContainsKey("status"))
            Status = WithdrawalStatus.Pending;
        var query = Filter();
        var total = await query.CountAsync();
        var pageNumber = ClampPage(PageNumber, total, 30);
        var rows = await Project(query.OrderBy(w => w.Status == WithdrawalStatus.Pending ? 0 : w.Status == WithdrawalStatus.Approved ? 1 : 2).ThenBy(w => w.CreatedAt))
            .Skip((pageNumber - 1) * 30).Take(30).ToListAsync();
        Items = new PagedList<AdminWithdrawalRow>(rows, total, pageNumber, 30);
    }

    [AdminPermission(AdminPermissions.FinanceExport)]
    public async Task<IActionResult> OnGetExportAsync(string format = "xlsx")
    {
        var rows = await Project(Filter().OrderByDescending(w => w.CreatedAt)).Take(50_000).ToListAsync();
        var table = new ExportTable("درخواست‌های برداشت", "تاریخ", "مشتری", "موبایل", "مبلغ", "صاحب حساب", "شبا", "کارت", "حساب", "وضعیت", "شماره پیگیری واریز", "تاریخ پردازش", "یادداشت");
        // فاز ۱۰: اگر «پوشاندن اطلاعات» روشن است، خروجی Excel هم همان شکل پوشانده را دارد.
        foreach (var w in rows)
            table.Add(w.CreatedAt, w.UserName, pii.Phone(w.Phone), w.Amount, w.AccountHolder, pii.Iban(w.Iban), pii.Card(w.CardNumber), pii.Card(w.AccountNumber),
                Display.WithdrawalStatus(w.Status), w.BankTrackingNumber, w.ProcessedAt, w.AdminNote);
        return Exporter.File(table, format, "withdrawals");
    }

    [AdminPermission(AdminPermissions.WithdrawalsManage)]
    [global::Fratak_Shop.Web.Infrastructure.Team.RequireReauth]
    public async Task<IActionResult> OnPostStatusAsync(Guid id, WithdrawalStatus status, string? adminNote, string? tracking)
    {
        // فاز ۱۰: «پرداخت شد» برای مبلغ‌های از سقف به بالا → درخواست تأیید دونفره (پرداخت بعد از تأیید همکار ثبت می‌شود).
        if (status == WithdrawalStatus.Paid)
        {
            var amount = await data.Query<WithdrawalRequest>().Where(w => w.Id == id).Select(w => (decimal?)w.Amount).FirstOrDefaultAsync();
            if (amount is { } value && approvals.NeedsWithdrawalApproval(await settingsService.GetAsync(), status, value))
            {
                ShowResult(await approvals.RequestWithdrawalPaidAsync(new global::Fratak_Shop.Core.Services.Team.WithdrawalPaidPayload(id,
                    string.IsNullOrWhiteSpace(adminNote) ? null : adminNote.Trim(),
                    string.IsNullOrWhiteSpace(tracking) ? null : SearchText.NormalizeDigits(tracking.Trim())), Access));
                return BackToList();
            }
        }

        var result = await wallet.ChangeWithdrawalStatusAsync(new ChangeWithdrawalStatusCommand
        {
            Id = id,
            Status = status,
            AdminNote = string.IsNullOrWhiteSpace(adminNote) ? null : adminNote.Trim(),
            BankTrackingNumber = string.IsNullOrWhiteSpace(tracking) ? null : SearchText.NormalizeDigits(tracking.Trim()),
        });
        if (Succeeded(result))
            await monitoring.LogActionAsync("WithdrawalStatus", "WithdrawalRequest", id, $"تغییر وضعیت برداشت به {Display.WithdrawalStatus(status)}");
        ShowResult(result);
        return BackToList();
    }

    private IQueryable<WithdrawalRequest> Filter()
    {
        var query = data.Query<WithdrawalRequest>();
        if (Status is { } s)
            query = query.Where(w => w.Status == s);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = SearchText.NormalizeDigits(Search.Trim());
            query = query.Where(w => w.User.PhoneNumber.Contains(term) || w.AccountHolder.Contains(term) || (w.Iban != null && w.Iban.Contains(term))
                                     || (w.BankTrackingNumber != null && w.BankTrackingNumber.Contains(term)));
        }
        return query;
    }

    private static IQueryable<AdminWithdrawalRow> Project(IQueryable<WithdrawalRequest> query) => query.Select(w => new AdminWithdrawalRow
    {
        Id = w.Id,
        CreatedAt = w.CreatedAt,
        UserId = w.UserId,
        UserName = ((w.User.FirstName ?? "") + " " + (w.User.LastName ?? "")).Trim(),
        Phone = w.User.PhoneNumber,
        Amount = w.Amount,
        AccountHolder = w.AccountHolder,
        Iban = w.Iban,
        CardNumber = w.CardNumber,
        AccountNumber = w.AccountNumber,
        Status = w.Status,
        AdminNote = w.AdminNote,
        BankTrackingNumber = w.BankTrackingNumber,
        ProcessedAt = w.ProcessedAt,
        CurrentWallet = w.User.WalletBalance,
    });
}
