using Fratak_Shop.Web.Utilities;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.DTOs.Blogs.Posts.Query;
using Fratak_Shop.Core.DTOs.Comments.Command;
using Fratak_Shop.Core.DTOs.Comments.Query;
using Fratak_Shop.Core.Services.Blogs;
using Fratak_Shop.Core.Services.Comments;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Pages;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

public class BlogPostModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IPostService posts,
    ICommentService comments,
    IPageAlertService pageAlerts)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    public PostDto Post { get; private set; } = null!;

    public List<BlogPostFilterData> Related { get; private set; } = [];

    public List<CommentDto> Comments { get; private set; } = [];

    [BindProperty]
    public CommentInput Comment { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        var post = await posts.GetPostByAsync(slug);
        if (post is null || !post.IsPublished)
            return NotFound();

        Post = post;
        Comments = await comments.GetCommentsByPostAsync(post.Id);
        await posts.AddPostVisitAsync(post.Id);

        var related = await posts.GetPostByFilterAsync(new BlogPostFilterParams { Take = 4 });
        Related = related.Data.Where(item => item.Id != post.Id).Take(3).ToList();

        var alerts = await pageAlerts.GetVisibleAsync(SitePage.BlogPostDetails);
        if (alerts.Count > 0)
            ViewData["PageAlertsTop"] = alerts;

        ApplySeo(post.Seo, new SeoFallback
        {
            SiteName = Site.SiteName,
            SiteUrl = Site.SiteUrl,
            DefaultDescription = Site.DefaultMetaDescription,
            DefaultKeywords = Site.DefaultMetaKeywords,
            DefaultShareImageName = Site.DefaultShareImageName,
            Path = $"/blog/{post.Slug}",
            ContentTitle = post.Title,
            ShortDescription = post.Excerpt,
            ImageName = post.ImageName,
            ImageFolder = "blog",
            ImageAlt = post.ImageAlt,
            EntityType = SeoEntityType.BlogPost,
            AutoSchemaJsonLd = SeoJsonLd.BlogPosting(post.Title, post.Excerpt ?? post.Title,
                $"{Site.SiteUrl}/blog/{post.Slug}", $"{Site.SiteUrl}{Ui.BlogImage(post.ImageName)}",
                Site.SiteName, post.AuthorName, post.PublishedAt),
        });

        return Page();
    }

    public async Task<IActionResult> OnPostCommentAsync(string slug)
    {
        var post = await posts.GetPostByAsync(slug);
        if (post is null)
            return NotFound();

        if (!ModelState.IsValid)
        {
            Notify("اطلاعات دیدگاه کامل نیست.", "error");
            return Redirect($"/blog/{slug}#comments");
        }

        var result = await comments.CreateCommentAsync(new CreateCommentCommand
        {
            UserId = CurrentUserId ?? Guid.Empty,
            BlogPostId = post.Id,
            Type = CommentType.BlogComment,
            AuthorName = Comment.AuthorName,
            AuthorPhone = Comment.AuthorPhone,
            Content = Comment.Content,
        });

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return Redirect($"/blog/{slug}#comments");
    }

    public class CommentInput
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "نام خود را وارد کنید.")]
        public string AuthorName { get; set; } = null!;

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "شماره موبایل را وارد کنید.")]
        [System.ComponentModel.DataAnnotations.RegularExpression(@"^09\d{9}$", ErrorMessage = "شماره موبایل معتبر نیست.")]
        public string AuthorPhone { get; set; } = null!;

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "متن دیدگاه را وارد کنید.")]
        public string Content { get; set; } = null!;
    }
}
