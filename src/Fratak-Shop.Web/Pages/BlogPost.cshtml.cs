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

public class BlogPostModel(IPostService postService, ICommentService commentService, IPageMetaService pageMeta) : ShopPageModel
{
    public PostDto Post { get; private set; } = null!;
    public List<CommentDto> Comments { get; private set; } = [];
    public List<BlogPostFilterData> LatestPosts { get; private set; } = [];

    [BindProperty]
    [Required(ErrorMessage = "متن دیدگاه را وارد کنید.")]
    [StringLength(2000, MinimumLength = 3, ErrorMessage = "متن دیدگاه باید بین ۳ تا ۲۰۰۰ کاراکتر باشد.")]
    public string CommentText { get; set; } = string.Empty;

    public async Task<IActionResult> OnGetAsync(string slug)
    {
        var post = await postService.GetPostByAsync(slug);
        if (post is null || !post.IsPublished || post.PublishedAt > DateTimeOffset.UtcNow)
            return NotFound();

        Post = post;
        Comments = await commentService.GetCommentsByPostAsync(post.Id, onlyApproved: true);
        LatestPosts = await postService.GetLatestPostsAsync(5, post.Id);

        await postService.AddPostVisitAsync(post.Id);

        pageMeta.Apply(ViewData, post.Seo, post.Title, post.Excerpt ?? Format.Excerpt(post.Content, 160),
            ImageHelper.Blog(post.ImageName), "article");
        ViewData[PageMetaKeys.JsonLd] ??= BuildJsonLd(post);

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
        var request = HttpContext.Request;
        var baseUrl = $"{request.Scheme}://{request.Host}";

        var data = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "BlogPosting",
            ["headline"] = post.Title,
            ["image"] = baseUrl + ImageHelper.Blog(post.ImageName),
            ["datePublished"] = (post.PublishedAt ?? post.CreatedAt).ToString("O"),
            ["dateModified"] = post.UpdatedAt.ToString("O"),
            ["author"] = new Dictionary<string, object> { ["@type"] = "Person", ["name"] = string.IsNullOrWhiteSpace(post.AuthorName) ? "تیم محتوا" : post.AuthorName },
            ["mainEntityOfPage"] = $"{baseUrl}/blog/{post.Slug}",
        };

        return JsonSerializer.Serialize(data, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }
}
