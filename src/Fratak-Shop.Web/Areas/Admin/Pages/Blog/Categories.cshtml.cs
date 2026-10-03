using Fratak_Shop.Core.DTOs.Blogs.Categories.Query;
using Fratak_Shop.Core.Services.Blogs;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Blog;

[Authorize(Policy = PolicyNames.Content)]
public class CategoriesModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    IBlogCategoryService blogCategories)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public List<BlogCategoryDto> Items { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public Guid? Edit { get; set; }

    [BindProperty]
    public CategoryInput Input { get; set; } = new();

    public void OnGet()
    {
        ViewData["Title"] = "دسته‌بندی مقالات";
        Items = blogCategories.GetAllCategoriesAsync();

        if (Edit.HasValue)
        {
            var current = Items.FirstOrDefault(item => item.Id == Edit.Value);
            if (current is not null)
            {
                Input = new CategoryInput
                {
                    Id = current.Id,
                    Name = current.Name,
                    Slug = current.Slug,
                    Description = current.Description,
                    DisplayOrder = current.DisplayOrder,
                    IsActive = current.IsActive,
                    SeoTitle = current.Seo?.Title,
                    SeoDescription = current.Seo?.MetaDescription,
                };
            }
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!ModelState.IsValid)
        {
            Notify("اطلاعات دسته‌بندی کامل نیست.", "error");
            return RedirectToPage("/Blog/Categories", new { area = "Admin" });
        }

        var seo = new Data.Utilities.SeoManagement.SeoMetaData
        {
            Title = Input.SeoTitle,
            MetaDescription = Input.SeoDescription,
        };

        var result = Input.Id.HasValue
            ? await blogCategories.EditCategoryAsync(new Core.DTOs.Blogs.Categories.Command.EditBlogCategoryCommand
            {
                Id = Input.Id.Value,
                Name = Input.Name,
                Slug = string.IsNullOrWhiteSpace(Input.Slug) ? Core.Utilities.SlugHelper.Generate(Input.Name) : Input.Slug,
                Description = Input.Description,
                DisplayOrder = Input.DisplayOrder,
                IsActive = Input.IsActive,
                Seo = seo,
            })
            : await blogCategories.CreateCategoryAsync(new Core.DTOs.Blogs.Categories.Command.CreateBlogCategoryCommand
            {
                Name = Input.Name,
                Slug = string.IsNullOrWhiteSpace(Input.Slug) ? Core.Utilities.SlugHelper.Generate(Input.Name) : Input.Slug,
                Description = Input.Description,
                DisplayOrder = Input.DisplayOrder,
                IsActive = Input.IsActive,
                Seo = seo,
            });

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Blog/Categories", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await blogCategories.DeleteCategoryAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Blog/Categories", new { area = "Admin" });
    }

    public class CategoryInput
    {
        public Guid? Id { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "نام دسته‌بندی را وارد کنید.")]
        public string Name { get; set; } = null!;

        public string? Slug { get; set; }
        public string? Description { get; set; }
        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public string? SeoTitle { get; set; }
        public string? SeoDescription { get; set; }
    }
}
