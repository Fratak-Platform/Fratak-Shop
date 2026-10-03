using Fratak_Shop.Core.DTOs.Blogs.Categories.Query;
using Fratak_Shop.Core.DTOs.Blogs.Posts.Query;
using Fratak_Shop.Core.Services.Blogs;
using Fratak_Shop.Core.Services.Media;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Blog;

[Authorize(Policy = PolicyNames.Content)]
public class PostsModel(
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
    public BlogPostFilterResult Result { get; private set; } = null!;

    public List<BlogCategoryDto> Categories { get; private set; } = [];

    [BindProperty(SupportsGet = true, Name = "q")]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true, Name = "page")]
    public int PageId { get; set; } = 1;

    [BindProperty]
    public PostInput Input { get; set; } = new();

    [BindProperty]
    public IFormFile? Image { get; set; }

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "مدیریت مقالات";

        Result = await posts.GetPostByFilterAsync(new BlogPostFilterParams
        {
            Search = Search,
            PageId = PageId,
            Take = 15,
        });

        Categories = blogCategories.GetAllCategoriesAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!ModelState.IsValid || CurrentUserId is not { } userId)
        {
            Notify("اطلاعات مقاله کامل نیست.", "error");
            return RedirectToPage("/Blog/Posts", new { area = "Admin" });
        }

        var imageName = Input.ImageName;
        if (Image is { Length: > 0 })
        {
            var saved = await media.SaveImageAsync(Image, MediaFolders.Blog);
            if (saved.IsSuccess())
                imageName = saved.Data;
        }

        var result = await posts.CreatePostAsync(new Core.DTOs.Blogs.Posts.Command.CreateBlogPostCommand
        {
            AuthorId = userId,
            BlogCategoryId = Input.BlogCategoryId,
            Title = Input.Title,
            Slug = string.IsNullOrWhiteSpace(Input.Slug) ? Core.Utilities.SlugHelper.Generate(Input.Title) : Input.Slug,
            Excerpt = Input.Excerpt,
            Content = Input.Content,
            ImageName = imageName,
            ImageAlt = Input.ImageAlt,
            IsPublished = Input.IsPublished,
            PublishedAt = Input.PublishedAt ?? DateTimeOffset.UtcNow,
            Seo = new Data.Utilities.SeoManagement.SeoMetaData
            {
                Title = Input.SeoTitle,
                MetaDescription = Input.SeoDescription,
                MetaKeywords = Input.SeoKeywords,
                FocusKeyword = Input.SeoFocusKeyword,
            },
        });

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Blog/Posts", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await posts.DeletePostAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Blog/Posts", new { area = "Admin" });
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
        public bool IsPublished { get; set; } = true;
        public DateTimeOffset? PublishedAt { get; set; }
        public string? SeoTitle { get; set; }
        public string? SeoDescription { get; set; }
        public string? SeoKeywords { get; set; }
        public string? SeoFocusKeyword { get; set; }
    }
}
