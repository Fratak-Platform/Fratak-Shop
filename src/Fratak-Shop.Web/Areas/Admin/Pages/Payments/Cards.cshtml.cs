using Fratak_Shop.Core.DTOs.Payments.Query;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Payments;

[Authorize(Policy = PolicyNames.AdminOnly)]
public class CardsModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public List<PaymentCardDto> Cards { get; private set; } = [];

    public CardToCardInstructionsDto Instructions { get; private set; } = new();

    [BindProperty]
    public CardInput Input { get; set; } = new();

    [BindProperty]
    public string? InstructionsText { get; set; }

    [BindProperty]
    public int DeadlineHours { get; set; } = 24;

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "کارت‌های بانکی و کارت به کارت";
        Cards = await bankTransfers.GetCardsAsync();
        Instructions = await bankTransfers.GetInstructionsAsync();
        InstructionsText = Instructions.Instructions;
        DeadlineHours = Instructions.DeadlineHours;
    }

    /// <summary>ذخیره متن راهنما و مهلت ارسال فیش.</summary>
    public async Task<IActionResult> OnPostInstructionsAsync()
    {
        var result = await settings.SetManyAsync(new Dictionary<string, string?>
        {
            ["Payment.Instructions"] = InstructionsText,
            ["Payment.BankTransferDeadlineHours"] = DeadlineHours.ToString(),
        });
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Payments/Cards", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var result = await bankTransfers.SaveCardAsync(Input.Id, new PaymentCardDto
        {
            BankName = Input.BankName,
            CardNumber = Core.Utilities.PriceHelper.ToEnglishDigits(Input.CardNumber),
            AccountHolder = Input.AccountHolder,
            ShebaNumber = string.IsNullOrWhiteSpace(Input.ShebaNumber) ? null : Core.Utilities.PriceHelper.ToEnglishDigits(Input.ShebaNumber),
            BranchName = Input.BranchName,
            Instructions = Input.Instructions,
            IsDefault = Input.IsDefault,
            DisplayOrder = Input.DisplayOrder,
            IsActive = Input.IsActive,
        });

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Payments/Cards", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id)
    {
        var result = await bankTransfers.ToggleCardAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Payments/Cards", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await bankTransfers.DeleteCardAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Payments/Cards", new { area = "Admin" });
    }

    public class CardInput
    {
        public Guid? Id { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "نام بانک را وارد کنید.")]
        public string BankName { get; set; } = null!;

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "شماره کارت را وارد کنید.")]
        [System.ComponentModel.DataAnnotations.StringLength(16, MinimumLength = 16, ErrorMessage = "شماره کارت باید ۱۶ رقم باشد.")]
        public string CardNumber { get; set; } = null!;

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "نام صاحب حساب را وارد کنید.")]
        public string AccountHolder { get; set; } = null!;

        public string? ShebaNumber { get; set; }
        public string? BranchName { get; set; }
        public string? Instructions { get; set; }
        public bool IsDefault { get; set; }
        public bool IsActive { get; set; } = true;
        public int DisplayOrder { get; set; }
    }
}
