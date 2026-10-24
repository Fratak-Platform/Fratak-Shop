using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Engagement;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Security;
using Fratak_Shop.Web.Services.Payments;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

/// <summary>
/// Gift cards: buy a card (wallet or ZarinPal) and give its code to anyone; whoever enters
/// the code gets the amount in their wallet. Redeeming is captcha- and rate-limit protected.
/// </summary>
public class GiftCardsModel(IGiftCardService giftCards, IPaymentFlowService paymentFlow, ISiteContext site) : ShopPageModel
{
    public List<GiftCardDto> Purchased { get; private set; } = [];
    public List<GiftCardDto> Redeemed { get; private set; } = [];

    [BindProperty]
    public PurchaseInput Purchase { get; set; } = new();

    [BindProperty]
    [Display(Name = "کد کارت هدیه")]
    public string? Code { get; set; }

    /// <summary>Which form the validation summary belongs to.</summary>
    public string? ActiveForm { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!site.Settings.IsGiftCardEnabled)
            return NotFound();

        PrepareCaptcha(CaptchaActions.GiftCard);
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRedeemAsync()
    {
        if (!site.Settings.IsGiftCardEnabled)
            return NotFound();

        ModelState.Clear();
        ActiveForm = "redeem";
        if (!VerifyCaptcha(CaptchaActions.GiftCard))
            return await PageAsync();

        var result = await giftCards.RedeemAsync(CurrentUserId!.Value, Code);
        if (!result.IsSuccess())
        {
            CaptchaFailed(CaptchaActions.GiftCard); // code guessing guard
            ModelState.AddModelError(string.Empty, result.Message ?? "کد کارت هدیه معتبر نیست.");
            return await PageAsync();
        }

        CaptchaPassed(CaptchaActions.GiftCard);
        SuccessMessage = result.Message ?? $"{Format.Toman(result.Data)} به کیف پول شما اضافه شد.";
        return RedirectToPage("/Profile/Wallet");
    }

    public async Task<IActionResult> OnPostBuyAsync()
    {
        if (!site.Settings.IsGiftCardEnabled)
            return NotFound();

        ModelState.Remove(nameof(Code));
        ActiveForm = "buy";
        if (!ModelState.IsValid)
            return await PageAsync();

        var result = await giftCards.PurchaseAsync(new PurchaseGiftCardCommand
        {
            UserId = CurrentUserId!.Value,
            Amount = Purchase.Amount,
            PaymentMethod = Purchase.PayWithWallet ? GiftCardPaymentMethod.Wallet : GiftCardPaymentMethod.OnlineGateway,
            RecipientName = Purchase.RecipientName,
            Message = Purchase.Message,
        });

        if (!result.IsSuccess() || result.Data is null)
        {
            ModelState.AddModelError(string.Empty, result.Message ?? "صدور کارت هدیه انجام نشد.");
            return await PageAsync();
        }

        if (!result.Data.RequiresPayment)
        {
            SuccessMessage = "کارت هدیه صادر شد؛ کد آن را کپی کنید و به هر کسی که می‌خواهید بدهید.";
            return RedirectToPage("/Profile/GiftCards", pageHandler: null, routeValues: null, fragment: "card-" + result.Data.Reference);
        }

        return await StartPaymentAsync(result.Data.GiftCardId);
    }

    public async Task<IActionResult> OnPostPayAsync(Guid id)
    {
        if (!site.Settings.IsGiftCardEnabled)
            return NotFound();

        return await StartPaymentAsync(id);
    }

    private async Task<IActionResult> StartPaymentAsync(Guid giftCardId)
    {
        var payment = await paymentFlow.StartGiftCardPaymentAsync(CurrentUserId!.Value, giftCardId);
        if (payment.Succeeded && payment.RedirectUrl is not null)
            return Redirect(payment.RedirectUrl);

        ErrorMessage = payment.Error ?? "اتصال به درگاه پرداخت برقرار نشد؛ کارت در فهرست شما به صورت «در انتظار پرداخت» باقی می‌ماند.";
        return RedirectToPage();
    }

    private async Task<IActionResult> PageAsync()
    {
        if (Captcha is null)
            PrepareCaptcha(CaptchaActions.GiftCard);
        await LoadAsync();
        return Page();
    }

    private async Task LoadAsync()
    {
        var userId = CurrentUserId!.Value;
        Purchased = await giftCards.GetPurchasedAsync(userId);
        Redeemed = await giftCards.GetRedeemedAsync(userId);
        if (Purchase.Amount == 0)
            Purchase.Amount = site.Settings.GiftCardPresets.FirstOrDefault(site.Settings.GiftCardMinAmount);

        ViewData[PageMetaKeys.Title] = "کارت هدیه";
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
    }

    public class PurchaseInput
    {
        [Display(Name = "مبلغ")]
        [Range(1000, 1_000_000_000, ErrorMessage = "مبلغ کارت هدیه معتبر نیست.")]
        public decimal Amount { get; set; }

        [Display(Name = "نام گیرنده")]
        [StringLength(100)]
        public string? RecipientName { get; set; }

        [Display(Name = "پیام")]
        [StringLength(500)]
        public string? Message { get; set; }

        public bool PayWithWallet { get; set; }
    }
}
