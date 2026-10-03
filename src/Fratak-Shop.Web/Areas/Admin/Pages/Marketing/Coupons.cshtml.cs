using Fratak_Shop.Core.DTOs.Marketing.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Marketing;

[Authorize(Policy = PolicyNames.AdminOnly)]
public class CouponsModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    ICouponService coupons)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public CouponFilterResult Result { get; private set; } = null!;

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "edit")]
    public Guid? Edit { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageId { get; set; } = 1;

    [BindProperty]
    public CouponInput Input { get; set; } = new();

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "کدهای تخفیف";

        Result = await coupons.GetFilterAsync(new CouponFilterParams
        {
            Search = Search,
            PageId = PageId,
            Take = 20,
        });

        if (Edit.HasValue)
        {
            var current = await coupons.GetAsync(Edit.Value);
            if (current is not null)
            {
                Input = new CouponInput
                {
                    Id = current.Id,
                    Code = current.Code,
                    Title = current.Title,
                    Description = current.Description,
                    DiscountType = current.DiscountType,
                    Value = current.Value,
                    MinOrderAmount = current.MinOrderAmount,
                    MaxDiscountAmount = current.MaxDiscountAmount,
                    UsageLimit = current.UsageLimit,
                    PerUserLimit = current.PerUserLimit,
                    StartsAt = current.StartsAt,
                    EndsAt = current.EndsAt,
                    IsActive = current.IsActive,
                };
            }
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var result = await coupons.SaveAsync(new Core.DTOs.Marketing.Command.SaveCouponCommand
        {
            Id = Input.Id,
            Code = Input.Code,
            Title = Input.Title,
            Description = Input.Description,
            DiscountType = Input.DiscountType,
            Value = Input.Value,
            MinOrderAmount = Input.MinOrderAmount,
            MaxDiscountAmount = Input.MaxDiscountAmount,
            UsageLimit = Input.UsageLimit,
            PerUserLimit = Input.PerUserLimit,
            StartsAt = Input.StartsAt,
            EndsAt = Input.EndsAt,
            IsActive = Input.IsActive,
        });

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Marketing/Coupons", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id)
    {
        var result = await coupons.ToggleActiveAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Marketing/Coupons", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await coupons.DeleteAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Marketing/Coupons", new { area = "Admin" });
    }

    public class CouponInput
    {
        public Guid? Id { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "کد تخفیف را وارد کنید.")]
        public string Code { get; set; } = null!;

        public string? Title { get; set; }
        public string? Description { get; set; }
        public CouponDiscountType DiscountType { get; set; } = CouponDiscountType.Percent;
        public decimal Value { get; set; }
        public decimal? MinOrderAmount { get; set; }
        public decimal? MaxDiscountAmount { get; set; }
        public int? UsageLimit { get; set; }
        public int PerUserLimit { get; set; } = 1;
        public DateTimeOffset? StartsAt { get; set; }
        public DateTimeOffset? EndsAt { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
