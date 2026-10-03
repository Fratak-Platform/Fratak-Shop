using Fratak_Shop.Core.DTOs.Blogs.Categories.Query;
using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.Mappers.Seo;
using Fratak_Shop.Core.Services.Blogs;
using Fratak_Shop.Core.SeoEngine;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Web.Utilities;
using Fratak_Shop.Core.Services.Media;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Data.Utilities.SeoManagement;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Blog;

[Authorize(Policy = PolicyNames.Content)]
public class PostsEditModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    IPostService posts,
    IBlogCategoryService blogCategories,
    IMediaService media)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public List<BlogCategoryDto> Categories { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public Guid Id { get; set; }

    [BindProperty]
    public PostInput Input { get; set; } = new();

    [BindProperty]
    public SeoInput Seo { get; set; } = new();

    [BindProperty]
    public IFormFile? Image { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        ViewData["Title"] = "ویرایش مقاله";
        Categories = blogCategories.GetAllCategoriesAsync();

        var post = await posts.GetPostByAsync(Id);
        if (post is null)
        {
            Notify("مقاله یافت نشد.", "error");
            return RedirectToPage("/Blog/Posts", new { area = "Admin" });
        }

        Input = new PostInput
        {
            Title = post.Title,
            Slug = post.Slug,
            BlogCategoryId = post.BlogCategoryId,
            Excerpt = post.Excerpt,
            Content = post.Content,
            ImageName = post.ImageName,
            ImageAlt = post.ImageAlt,
            IsPublished = post.IsPublished,
            PublishedAt = post.PublishedAt,
        };

        Seo = SeoInput.FromDto(post.Seo);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        Categories = blogCategories.GetAllCategoriesAsync();

        if (!ModelState.IsValid)
            return Page();

        var imageName = Input.ImageName;
        if (Image is { Length: > 0 })
        {
            var saved = await media.SaveImageAsync(Image, MediaFolders.Blog);
            if (saved.IsSuccess())
                imageName = saved.Data;
        }

        var result = await posts.EditPostAsync(new Core.DTOs.Blogs.Posts.Command.EditBlogPostCommand
        {
            Id = Id,
            AuthorId = CurrentUserId ?? Guid.Empty,
            BlogCategoryId = Input.BlogCategoryId,
            Title = Input.Title,
            Slug = string.IsNullOrWhiteSpace(Input.Slug) ? Core.Utilities.SlugHelper.Generate(Input.Title) : Input.Slug,
            Excerpt = Input.Excerpt,
            Content = Input.Content,
            ImageName = imageName,
            ImageAlt = Input.ImageAlt,
            IsPublished = Input.IsPublished,
            PublishedAt = Input.PublishedAt,
            Seo = Seo.ToEntity(),
        });

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        if (!result.IsSuccess())
            return Page();

        return RedirectToPage("/Blog/PostsEdit", new { area = "Admin", id = Id });
    }

    /// <summary>«سئوی هوشمند»: تحلیل و پیشنهاد فیلدها بدون ذخیره خودکار.</summary>
    public async Task<IActionResult> OnPostSmartSeoAsync()
    {
        var result = await seoAudits.GenerateAsync(SeoEntityType.BlogPost, Id, CurrentUserId);
        if (!result.IsSuccess() || result.Data is null)
            return new JsonResult(new { success = false, message = result.Message });

        return new JsonResult(new
        {
            success = true,
            message = $"تحلیل انجام شد؛ امتیاز فعلی {result.Data.Score} از ۱۰۰.",
            recordId = result.Data.Id,
            score = result.Data.Score,
            proposal = result.Data.Proposal,
            findings = result.Data.Findings.Select(finding => new
            {
                title = finding.Title,
                description = finding.Description,
                severityClass = Ui.SeverityClass(finding.Severity),
                severityTitle = finding.Severity switch
                {
                    SeoFindingSeverity.Critical => "بحرانی",
                    SeoFindingSeverity.Warning => "هشدار",
                    SeoFindingSeverity.Good => "بررسی‌شده",
                    _ => "اطلاع",
                },
            }),
        });
    }

    /// <summary>«هوشمندسازی نوشته‌ها»: پیش‌نویس بهتر خلاصه و متن مقاله.</summary>
    public async Task<IActionResult> OnPostEnhanceAsync()
    {
        var result = await seoAudits.EnhanceAsync(SeoEntityType.BlogPost, Id);
        if (!result.IsSuccess() || result.Data is null)
            return new JsonResult(new { success = false, message = result.Message });

        return new JsonResult(new
        {
            success = true,
            message = "پیش‌نویس نوشته آماده شد؛ پس از بازبینی ذخیره کنید.",
            shortDescription = result.Data.EnhancedShortDescription,
            content = result.Data.EnhancedContent,
            summary = result.Data.Summary,
        });
    }

    public class PostInput
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "عنوان مقاله را وارد کنید.")]
        public string Title { get; set; } = null!;

        public string? Slug { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "دسته‌بندی مقاله را انتخاب کنید.")]
        public Guid BlogCategoryId { get; set; }

        public string? Excerpt { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "متن مقاله را وارد کنید.")]
        public string Content { get; set; } = null!;

        public string? ImageName { get; set; }
        public string? ImageAlt { get; set; }
        public bool IsPublished { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
    }
}
