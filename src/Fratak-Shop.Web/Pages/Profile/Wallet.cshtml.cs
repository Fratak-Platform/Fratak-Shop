using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Wallets;
using Fratak_Shop.Core.Services.Wallets;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Models;
using Fratak_Shop.Web.Services;
using Fratak_Shop.Web.Services.Payments;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

/// <summary>
/// Customer wallet: balance, top-up through ZarinPal, withdrawal requests to a bank
/// account (IBAN) and the transaction ledger. Everything is controlled by the store
/// settings (wallet / top-up / withdrawal switches and limits).
/// </summary>
public class WalletModel(
    IWalletService walletService,
    IPaymentFlowService paymentFlow,
    ISiteContext site,
    IPageMetaService pageMeta) : ShopPageModel
{
    public WalletSummaryDto Summary { get; private set; } = new();
    public WalletTransactionFilterResult Transactions { get; private set; } = null!;
    public List<WithdrawalRequestDto> Withdrawals { get; private set; } = [];
    public PagerModel Pager { get; private set; } = null!;
    public bool HasPendingWithdrawal => Withdrawals.Count(w => w.CanBeCancelled) >= Math.Max(1, site.Settings.WalletMaxPendingWithdrawals);

    /// <summary>Error of the withdrawal form (shown next to the form, values are kept).</summary>
    public string? WithdrawalError { get; private set; }

    public static readonly decimal[] TopUpPresets = [100_000, 200_000, 500_000, 1_000_000];

    // FromQuery: "page" is also a Razor Pages route value (the page path), which would win over the query string.
    [FromQuery(Name = "page")]
    public int PageNumber { get; set; } = 1;

    [BindProperty]
    public TopUpInput TopUp { get; set; } = new();

    [BindProperty]
    public WithdrawalInput Withdrawal { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (!site.Settings.IsWalletEnabled)
            return NotFound();

        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostTopUpAsync()
    {
        if (!site.Settings.CanTopUpWallet)
            return NotFound();

        ModelState.Clear();
        if (!TryValidateModel(TopUp, nameof(TopUp)) || TopUp.Amount is not { } amount)
        {
            ErrorMessage = "مبلغ شارژ را به درستی وارد کنید.";
            return RedirectToPage();
        }

        var start = await paymentFlow.StartWalletTopUpAsync(CurrentUserId!.Value, amount);
        if (start.Succeeded)
            return Redirect(start.RedirectUrl!);

        ErrorMessage = start.Error;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostWithdrawAsync()
    {
        if (!site.Settings.CanWithdraw)
            return NotFound();

        ModelState.Clear();
        if (!TryValidateModel(Withdrawal, nameof(Withdrawal)))
        {
            // Re-render with the entered values so the customer doesn't type everything again.
            WithdrawalError = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault()
                              ?? "اطلاعات درخواست برداشت معتبر نیست.";
            await LoadAsync();
            return Page();
        }

        var result = await walletService.RequestWithdrawalAsync(new CreateWithdrawalCommand
        {
            UserId = CurrentUserId!.Value,
            Amount = Withdrawal.Amount!.Value,
            AccountHolder = Withdrawal.AccountHolder.Trim(),
            CardNumber = Withdrawal.CardNumber,
            Iban = Withdrawal.Iban,
            AccountNumber = Withdrawal.AccountNumber,
        });

        if (!Succeeded(result))
        {
            WithdrawalError = result.Message;
            await LoadAsync();
            return Page();
        }

        ShowResult(result);
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostCancelWithdrawalAsync(Guid id)
    {
        var result = await walletService.CancelWithdrawalAsync(CurrentUserId!.Value, id);
        ShowResult(result);
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        var userId = CurrentUserId!.Value;
        Summary = await walletService.GetSummaryAsync(userId);
        Transactions = await walletService.GetTransactionsAsync(new WalletTransactionFilterParams
        {
            UserId = userId,
            PageId = PageNumber,
            Take = 15,
        });
        var withdrawals = await walletService.GetWithdrawalsAsync(new WithdrawalFilterParams { UserId = userId, Take = 10 });
        Withdrawals = withdrawals.Data;
        Pager = PagerModel.From(Transactions, Request);

        await pageMeta.ApplyAsync(ViewData, SitePage.UserWallet, "کیف پول");
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
    }

    public class TopUpInput
    {
        [Required(ErrorMessage = "مبلغ شارژ را وارد کنید.")]
        [Range(1_000, 1_000_000_000, ErrorMessage = "مبلغ شارژ معتبر نیست.")]
        public decimal? Amount { get; set; }
    }

    public class WithdrawalInput : IValidatableObject
    {
        [Required(ErrorMessage = "مبلغ برداشت را وارد کنید.")]
        [Range(1_000, 10_000_000_000, ErrorMessage = "مبلغ برداشت معتبر نیست.")]
        public decimal? Amount { get; set; }

        [Required(ErrorMessage = "نام و نام خانوادگی صاحب حساب را وارد کنید.")]
        [StringLength(100, MinimumLength = 5, ErrorMessage = "نام و نام خانوادگی صاحب حساب را کامل وارد کنید.")]
        public string AccountHolder { get; set; } = string.Empty;

        [MaxLength(30)]
        public string? CardNumber { get; set; }

        [MaxLength(40)]
        public string? Iban { get; set; }

        [MaxLength(40)]
        public string? AccountNumber { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (string.IsNullOrWhiteSpace(CardNumber) && string.IsNullOrWhiteSpace(Iban))
                yield return new ValidationResult("برای شناسایی حساب، شماره کارت یا شماره شبا را وارد کنید.", [nameof(CardNumber), nameof(Iban)]);
        }
    }
}
