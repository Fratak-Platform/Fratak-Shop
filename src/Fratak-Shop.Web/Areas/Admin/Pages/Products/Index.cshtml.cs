using Fratak_Shop.Core.DTOs.Categories.Query;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Services.Categories;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Products;

[Authorize(Policy = PolicyNames.Products)]
public class IndexModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    IProductService products,
    ICategoryService categories)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public ProductFilterResult Result { get; private set; } = null!;

    public List<CategoryDto> Categories { get; private set; } = [];

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "categoryId")]
    public Guid? CategoryId { get; set; }

    [BindProperty(SupportsGet = true, Name = "status")]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true, Name = "lowStock")]
    public bool LowStock { get; set; }

    [BindProperty(SupportsGet = true, Name = "missingSeo")]
    public bool MissingSeo { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageId { get; set; } = 1;

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "مدیریت محصولات";

        Result = await products.GetFilterAsync(new ProductFilterParams
        {
            Search = Search,
            CategoryId = CategoryId,
            IncludeInactive = true,
            IsActive = Status switch
            {
                "active" => true,
                "inactive" => false,
                _ => null,
            },
            LowStock = LowStock ? true : null,
            MissingSeo = MissingSeo ? true : null,
            SortBy = ProductSortBy.Newest,
            PageId = PageId,
            Take = 20,
        });

        Categories = await categories.GetFlatAsync();
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(Guid id)
    {
        var result = await products.ToggleActiveAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Products/Index", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostToggleFeaturedAsync(Guid id)
    {
        var result = await products.ToggleFeaturedAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Products/Index", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await products.DeleteProductAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Products/Index", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostStockAsync(Guid id, int stockQuantity)
    {
        var result = await products.UpdateStockAsync(id, stockQuantity);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Products/Index", new { area = "Admin" });
    }
}
