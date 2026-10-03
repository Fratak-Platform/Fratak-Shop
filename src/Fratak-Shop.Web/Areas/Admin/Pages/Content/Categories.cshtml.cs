using Fratak_Shop.Core.DTOs.Categories.Command;
using Fratak_Shop.Core.DTOs.Categories.Query;
using Fratak_Shop.Core.Services.Categories;
using Fratak_Shop.Core.Services.Media;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Content;

[Authorize(Policy = PolicyNames.Products)]
public class CategoriesModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    ICategoryService categories,
    IMediaService media)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public List<CategoryDto> Items { get; private set; } = [];

    public List<CategoryDto> Parents { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public Guid? Edit { get; set; }

    [BindProperty]
    public CategoryInput Input { get; set; } = new();

    [BindProperty]
    public IFormFile? Image { get; set; }

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "مدیریت دسته‌بندی‌ها";
        Items = await categories.GetFlatAsync();
        Parents = await categories.GetParentCandidatesAsync(Edit);

        if (Edit.HasValue)
        {
            var current = await categories.GetCategoryByIdAsync(Edit.Value);
            if (current is not null)
            {
                Input = new CategoryInput
                {
                    Id = current.Id,
                    Name = current.Name,
                    Slug = current.Slug,
                    ParentId = current.ParentId,
                    Description = current.Description,
                    ImageName = current.ImageName,
                    DisplayOrder = current.DisplayOrder,
                    IsActive = current.IsActive,
                    SeoTitle = current.Seo?.Title,
                    SeoDescription = current.Seo?.MetaDescription,
                    SeoKeywords = current.Seo?.MetaKeywords,
                    SeoFocusKeyword = current.Seo?.FocusKeyword,
                };
            }
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!ModelState.IsValid)
        {
            Notify("اطلاعات دسته‌بندی کامل نیست.", "error");
            return RedirectToPage("/Content/Categories", new { area = "Admin", edit = Input.Id });
        }

        var imageName = Input.ImageName;
        if (Image is { Length: > 0 })
        {
            var saved = await media.SaveImageAsync(Image, MediaFolders.Categories);
            if (saved.IsSuccess())
                imageName = saved.Data;
            else
                Notify(saved.Message, "warning");
        }

        var seo = new Data.Utilities.SeoManagement.SeoMetaData
        {
            Title = Input.SeoTitle,
            MetaDescription = Input.SeoDescription,
            MetaKeywords = Input.SeoKeywords,
            FocusKeyword = Input.SeoFocusKeyword,
        };

        if (Input.Id.HasValue)
        {
            var result = await categories.EditCategoryAsync(new EditCategoryCommand
            {
                Id = Input.Id.Value,
                Name = Input.Name,
                Slug = string.IsNullOrWhiteSpace(Input.Slug) ? Core.Utilities.SlugHelper.Generate(Input.Name) : Input.Slug,
                ParentId = Input.ParentId,
                Description = Input.Description,
                ImageName = imageName,
                DisplayOrder = Input.DisplayOrder,
                IsActive = Input.IsActive,
                Seo = seo,
            });

            Notify(result.Message, result.IsSuccess() ? "success" : "error");
        }
        else
        {
            var result = await categories.CreateCategoryAsync(new CreateCategoryCommand
            {
                Name = Input.Name,
                Slug = string.IsNullOrWhiteSpace(Input.Slug) ? Core.Utilities.SlugHelper.Generate(Input.Name) : Input.Slug,
                ParentId = Input.ParentId,
                Description = Input.Description,
                ImageName = imageName,
                DisplayOrder = Input.DisplayOrder,
                IsActive = Input.IsActive,
                Seo = seo,
            });

            Notify(result.Message, result.IsSuccess() ? "success" : "error");
        }

        return RedirectToPage("/Content/Categories", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await categories.DeleteCategoryAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Content/Categories", new { area = "Admin" });
    }

    public class CategoryInput
    {
        public Guid? Id { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "نام دسته‌بندی را وارد کنید.")]
        public string Name { get; set; } = null!;

        public string? Slug { get; set; }
        public Guid? ParentId { get; set; }
        public string? Description { get; set; }
        public string? ImageName { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public string? SeoTitle { get; set; }
        public string? SeoDescription { get; set; }
        public string? SeoKeywords { get; set; }
        public string? SeoFocusKeyword { get; set; }
    }
}
