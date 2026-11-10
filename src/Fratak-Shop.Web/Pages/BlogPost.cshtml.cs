using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Fratak_Shop.Core.DTOs.Blogs.Posts.Query;
using Fratak_Shop.Core.DTOs.Comments.Command;
using Fratak_Shop.Core.DTOs.Comments.Query;
using Fratak_Shop.Core.Services.Blogs;
using Fratak_Shop.Core.Services.Comments;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

[Microsoft.AspNetCore.OutputCaching.OutputCache(PolicyName = Fratak_Shop.Web.Infrastructure.PageCache.PageCachePolicy.Name)]
public class BlogPostModel(IPostService postService, ICommentService commentService, IPageMetaService pageMeta, ISiteContext site,
    Fratak_Shop.Core.Services.Seo.Autopilot.ISeoLinkTargetProvider linkTargets,
    Fratak_Shop.Core.Services.Smart.IBuyerGuideService buyerGuide) : ShopPageModel
{
    /// <summary>متن مقاله برای نمایش: پاک‌سازی‌شده + لینک داخلی خودکار هنگام نمایش (متن ذخیره‌شده دست نمی‌خورد).</summary>
    public string BodyHtml { get; private set; } = string.Empty;

    public PostDto Post { get; private set; } = null!;
    public List<CommentDto> Comments { get; private set; } = [];
    public List<BlogPostFilterData> LatestPosts { get; private set; } = [];

    /// <summary>دور ۲۳: مطالب واقعاً مرتبط (هم‌دسته یا هم‌موضوع)؛ خالی یعنی بخش نمایش داده نمی‌شود.</summary>
    public List<BlogPostFilterData> RelatedPosts { get; private set; } = [];

    [BindProperty]
    [Required(ErrorMessage = "متن دیدگاه را وارد کنید.")]
    [StringLength(2000, MinimumLength = 3, ErrorMessage = "متن دیدگاه باید بین ۳ تا ۲۰۰۰ کاراکتر باشد.")]
    public string CommentText { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        var post = await postService.GetPostByAsync(slug);
        if (post is null)
            return NotFound();
        // دور ۲۳: پیش‌نویس/زمان‌بندی‌شده برای مشتری ۴۰۴ است؛ کارمند محتوا همان صفحه را با نوار «پیش‌نمایش» می‌بیند.
        var preview = !post.IsPublished || post.PublishedAt > DateTimeOffset.UtcNow;
        if (preview && !await StaffPreview.AllowedAsync(HttpContext, Fratak_Shop.Core.Services.Admin.AdminPermissions.BlogWrite,
                Fratak_Shop.Core.Services.Admin.AdminPermissions.BlogPublish, Fratak_Shop.Core.Services.Admin.AdminPermissions.ContentManage,
                Fratak_Shop.Core.Services.Admin.AdminPermissions.SeoManage))
            return NotFound();

        Post = post;
        var (targets, maxLinks) = await linkTargets.ForRenderAsync(HttpContext.RequestAborted);
        BodyHtml = Fratak_Shop.Core.Services.Seo.Autopilot.InternalLinker.Link(Fratak_Shop.Core.Utilities.XssSecurity.SanitizeText(post.Content ?? string.Empty),
            Request.Path.Value ?? "/", targets, maxLinks);
        // دور ۱۹ (B16/C22): کدهای کوتاه [[product:slug]] و [[price:slug]] ← کارت محصول با قیمت و موجودی «امروز».
        // بعد از پاک‌سازی XSS اجرا می‌شود و همهٔ مقدارها encode می‌شوند؛ کالای حذف‌شده بی‌صدا کنار می‌رود (لینک شکسته نمی‌ماند).
        var shortcodeSlugs = Fratak_Shop.Core.Services.Smart.BlogShortcodes.ProductSlugs(BodyHtml);
        if (shortcodeSlugs.Count > 0)
        {
            var cards = await buyerGuide.CardsBySlugsAsync(shortcodeSlugs, HttpContext.RequestAborted);
            BodyHtml = Fratak_Shop.Core.Services.Smart.BlogShortcodes.Render(BodyHtml, cards, ImageHelper.Product);
        }
        Comments = await commentService.GetCommentsByPostAsync(post.Id, onlyApproved: true);
        RelatedPosts = await postService.GetRelatedPostsAsync(post.Id, 4);
        // «آخرین مقالات» تکرار «مطالب مرتبط» نباشد.
        var relatedIds = RelatedPosts.Select(x => x.Slug).ToHashSet(StringComparer.Ordinal);
        LatestPosts = (await postService.GetLatestPostsAsync(5 + RelatedPosts.Count, post.Id))
            .Where(x => !relatedIds.Contains(x.Slug)).Take(5).ToList();

        if (!preview)
            await postService.AddPostVisitAsync(post.Id);

        pageMeta.Apply(ViewData, post.Seo, post.Title, post.Excerpt ?? Format.Excerpt(post.Content, 160),
            ImageHelper.Blog(post.ImageName), "article");
        ViewData[PageMetaKeys.JsonLd] ??= BuildJsonLd(post);
        if (!string.IsNullOrWhiteSpace(post.BlogCategorySlug))
            pageMeta.AddBreadcrumbs(ViewData, ("وبلاگ", "/blog"), (post.BlogCategoryName ?? "مقالات", $"/blog/category/{post.BlogCategorySlug}"), (post.Title, null));
        else
            pageMeta.AddBreadcrumbs(ViewData, ("وبلاگ", "/blog"), (post.Title, null));

        if (preview)
            StaffPreview.Mark(HttpContext, ViewData, post.IsPublished ? "پیش‌نمایش – زمان انتشار این مقاله هنوز نرسیده است" : "پیش‌نمایش – این مقاله هنوز منتشر نشده است");
        return Page();
    }

    public async Task<IActionResult> OnPostCommentAsync(string slug)
    {
        if (!IsSignedIn)
            return RedirectToLogin($"/blog/{slug}#comments");

        var post = await postService.GetPostByAsync(slug);
        if (post is null || !post.IsPublished)
            return NotFound();

        if (!ModelState.IsValid)
        {
            ErrorMessage = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault();
            return Redirect($"/blog/{slug}#comments");
        }

        var result = await commentService.CreateCommentAsync(new CreateCommentCommand
        {
            UserId = CurrentUserId!.Value,
            BlogPostId = post.Id,
            Type = CommentType.BlogComment,
            AuthorName = User.GetDisplayName(),
            AuthorPhone = User.GetPhoneNumber() ?? "-",
            Content = CommentText.Trim(),
        });

        ShowResult(result);
        return Redirect($"/blog/{slug}#comments");
    }

    private string BuildJsonLd(PostDto post)
    {
        var url = pageMeta.AbsoluteUrl($"/blog/{post.Slug}");
        var settings = site.Settings;

        var data = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "BlogPosting",
            ["@id"] = url + "#article",
            ["headline"] = post.Title.Length > 110 ? post.Title[..110] : post.Title,
            ["description"] = post.Excerpt ?? Format.Excerpt(post.Content, 300),
            ["image"] = pageMeta.AbsoluteUrl(ImageHelper.Blog(post.ImageName)),
            ["datePublished"] = (post.PublishedAt ?? post.CreatedAt).ToString("O"),
            ["dateModified"] = (post.UpdatedAt > (post.PublishedAt ?? post.CreatedAt) ? post.UpdatedAt : post.PublishedAt ?? post.CreatedAt).ToString("O"),
            ["inLanguage"] = "fa-IR",
            ["articleSection"] = post.BlogCategoryName,
            ["author"] = new Dictionary<string, object> { ["@type"] = "Person", ["name"] = string.IsNullOrWhiteSpace(post.AuthorName) ? "تیم محتوا" : post.AuthorName },
            ["publisher"] = new Dictionary<string, object?>
            {
                ["@type"] = "Organization",
                ["name"] = settings.SiteName,
                ["logo"] = new Dictionary<string, object?> { ["@type"] = "ImageObject", ["url"] = pageMeta.AbsoluteUrl(site.LogoRasterUrl) },
            },
            ["mainEntityOfPage"] = new Dictionary<string, object?> { ["@type"] = "WebPage", ["@id"] = url },
            ["commentCount"] = Comments.Count,
        };

        var clean = data.Where(pair => pair.Value is not null).ToDictionary(pair => pair.Key, pair => pair.Value);
        return JsonSerializer.Serialize(clean, PageMetaService.JsonLdOptions);
    }
}
