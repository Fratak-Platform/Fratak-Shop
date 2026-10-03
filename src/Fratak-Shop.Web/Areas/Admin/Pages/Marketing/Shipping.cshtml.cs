using Fratak_Shop.Core.DTOs.Shipping.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shipping;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Marketing;

[Authorize(Policy = PolicyNames.AdminOnly)]
public class ShippingModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    IShippingMethodService shipping)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public List<ShippingMethodDto> Items { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public Guid? Edit { get; set; }

    [BindProperty]
    public ShippingInput Input { get; set; } = new();

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "روش‌های ارسال";
        Items = await shipping.GetAllAsync();

        if (Edit.HasValue)
        {
            var current = Items.FirstOrDefault(item => item.Id == Edit.Value);
            if (current is not null)
            {
                Input = new ShippingInput
                {
                    Id = current.Id,
                    Name = current.Name,
                    Description = current.Description,
                    Cost = current.Cost,
                    FreeShippingThreshold = current.FreeShippingThreshold,
                    EstimatedDeliveryDays = current.EstimatedDeliveryDays,
                    RestrictedToCity = current.RestrictedToCity,
                    Icon = current.Icon,
                    DisplayOrder = current.DisplayOrder,
                    IsActive = current.IsActive,
                };
            }
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var result = await shipping.SaveAsync(Input.Id, new ShippingMethodDto
        {
            Name = Input.Name,
            Description = Input.Description,
            Cost = Input.Cost,
            FreeShippingThreshold = Input.FreeShippingThreshold,
            EstimatedDeliveryDays = Input.EstimatedDeliveryDays,
            RestrictedToCity = Input.RestrictedToCity,
            Icon = Input.Icon,
            DisplayOrder = Input.DisplayOrder,
            IsActive = Input.IsActive,
        });

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Marketing/Shipping", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id)
    {
        var result = await shipping.ToggleActiveAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Marketing/Shipping", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await shipping.DeleteAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Marketing/Shipping", new { area = "Admin" });
    }

    public class ShippingInput
    {
        public Guid? Id { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "نام روش ارسال را وارد کنید.")]
        public string Name { get; set; } = null!;

        public string? Description { get; set; }
        public decimal Cost { get; set; }
        public decimal? FreeShippingThreshold { get; set; }
        public int? EstimatedDeliveryDays { get; set; }
        public string? RestrictedToCity { get; set; }
        public string? Icon { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
