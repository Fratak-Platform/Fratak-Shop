using Fratak_Shop.Core.DTOs.Notifications;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Blog;

public sealed class PostInput
{
    public string? Title { get; set; }
    public string? Slug { get; set; }
    public Guid? CategoryId { get; set; }
    public string? Excerpt { get; set; }
    public string? Content { get; set; }
    public string? ImageAlt { get; set; }
    public bool RemoveImage { get; set; }
    public string? PublishDate { get; set; }
    public SeoInput Seo { get; set; } = new();
}

/// <summary>
/// Write / edit an article. Writers: own drafts → "submit for review" (published posts are locked for them).
/// Editors (publish permission): every post, publish now / schedule, unpublish, return to the writer with a note.
/// SEO specialists: the SEO section of every post.
/// </summary>
[AdminPermission(AdminPermissions.BlogWrite, AdminPermissions.BlogPublish, AdminPermissions.SeoManage)]
[RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
[RequestSizeLimit(7 * 1024 * 1024)]
public class EditModel(IAdminDataService data, IUploadStore uploads, INotificationService notifications, IMonitoringService monitoring) : AdminPageModel
{
    [BindProperty] public PostInput Input { get; set; } = new();

    public BlogPost? Post { get; private set; }
    public string? AuthorName { get; private set; }
    public List<BlogCategory> Categories { get; private set; } = [];
    public bool IsNew => Post is null;
    public bool IsEditor => Can(AdminPermissions.BlogPublish);

    /// <summary>Can change the article body (not only SEO).</summary>
    public bool CanEditContent => IsEditor
                                  || (Can(AdminPermissions.BlogWrite) && (Post is null || (Post.AuthorId == Access.UserId && !Post.IsPublished)));

    public bool CanSeo => IsEditor || Can(AdminPermissions.SeoManage) || CanEditContent;

    public async Task<IActionResult> OnGetAsync(Guid? id)
    {
        if (id is null && !Can(AdminPermissions.BlogWrite) && !IsEditor)
            return Redirect("/admin/denied?need=" + AdminPermissions.BlogWrite);
        if (id is { } postId)
        {
            if (!await LoadPostAsync(postId))
                return NotFound();
            if (!CanView())
                return Redirect("/admin/denied?need=" + AdminPermissions.BlogPublish);
            Input = ToInput(Post!);
        }
        await LoadListsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(Guid? id, string intent, IFormFile? image)
    {
        BlogPost? post = null;
        if (id is { } postId)
        {
            if (!await LoadPostAsync(postId))
                return NotFound();
            post = await data.FindAsync<BlogPost>(postId);
            if (post is null)
                return NotFound();
        }
        if (!CanEditContent)
            return Redirect("/admin/denied?need=" + AdminPermissions.BlogPublish);
        if (intent is "publish" or "unpublish" && !IsEditor)
            return Redirect("/admin/denied?need=" + AdminPermissions.BlogPublish);

        var title = Input.Title?.Trim();
        var content = Input.Content.SanitizeOrNull();
        if (string.IsNullOrWhiteSpace(title) || Input.CategoryId is null || string.IsNullOrWhiteSpace(content))
            return await FailAsync("عنوان، دسته و متن مقاله الزامی است.");
        if (title.Length > 250 || Input.Excerpt?.Length > 1000 || Input.ImageAlt?.Length > 250)
            return await FailAsync("عنوان حداکثر ۲۵۰ و خلاصه حداکثر ۱۰۰۰ نویسه است.");
        if (!await data.Query<BlogCategory>().AnyAsync(c => c.Id == Input.CategoryId))
            return await FailAsync("دسته انتخاب‌شده معتبر نیست.");
        var seoError = Input.Seo.Validate();
        if (seoError is not null)
            return await FailAsync(seoError);

        var slug = EditModelSlug(string.IsNullOrWhiteSpace(Input.Slug) ? title : Input.Slug);
        if (string.IsNullOrEmpty(slug))
            return await FailAsync("نامک (آدرس) معتبر نیست.");
        if (await data.Query<BlogPost>().AnyAsync(p => p.Slug == slug && p.Id != (post == null ? Guid.Empty : post.Id)))
            return await FailAsync("مقاله دیگری با همین نامک وجود دارد.");

        DateTimeOffset? publishAt = null;
        if (!string.IsNullOrWhiteSpace(Input.PublishDate))
        {
            var date = PersianDateInput.Parse(Input.PublishDate);
            if (date is null)
                return await FailAsync("تاریخ انتشار را به‌صورت ۱۴۰۴/۰۸/۰۱ وارد کنید.");
            publishAt = IranClock.StartOfDay(date.Value);
        }

        string? newImage = null;
        if (image is { Length: > 0 })
        {
            var (files, error) = await uploads.SaveAsync(UploadArea.Blogs, [image], 1, 4_096, HttpContext.RequestAborted);
            if (error is not null)
                return await FailAsync(error);
            newImage = files[0].FileName;
        }

        var isNew = post is null;
        post ??= new BlogPost { AuthorId = Access.UserId };
        var oldImage = post.ImageName;
        var wasPublished = post.IsPublished;
        post.Title = title;
        post.Slug = slug;
        post.BlogCategoryId = Input.CategoryId.Value;
        post.Excerpt = string.IsNullOrWhiteSpace(Input.Excerpt) ? null : Input.Excerpt.Trim();
        post.Content = content;
        post.ImageAlt = string.IsNullOrWhiteSpace(Input.ImageAlt) ? null : Input.ImageAlt.Trim();
        if (newImage is not null) post.ImageName = newImage;
        else if (Input.RemoveImage) post.ImageName = null;
        post.Seo = Input.Seo.ApplyToCopy(post.Seo);

        switch (intent)
        {
            case "review":
                post.SubmittedForReviewAt = DateTimeOffset.UtcNow;
                post.ReviewNote = null;
                break;
            case "publish":
                post.IsPublished = true;
                post.PublishedAt = publishAt ?? (wasPublished ? post.PublishedAt : null) ?? DateTimeOffset.UtcNow;
                post.SubmittedForReviewAt = null;
                post.ReviewNote = null;
                break;
            case "unpublish":
                post.IsPublished = false;
                break;
            default:
                if (IsEditor && post.IsPublished && publishAt is not null)
                    post.PublishedAt = publishAt;
                break;
        }

        var result = isNew ? await data.AddAsync(post) : await data.UpdateAsync(post);
        if (!Succeeded(result))
        {
            if (newImage is not null) uploads.Delete(UploadArea.Blogs, [newImage]);
            return await FailAsync(result.Message);
        }

        if (oldImage is not null && oldImage != post.ImageName && !oldImage.StartsWith('/') && !oldImage.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            uploads.Delete(UploadArea.Blogs, [oldImage]);

        if (intent == "publish" && !wasPublished)
        {
            await monitoring.LogActionAsync("blog.publish", "BlogPost", post.Id, post.Title);
            await NotifyAuthorAsync(post, "مقاله شما منتشر شد", $"مقاله «{post.Title}» منتشر شد.", $"/blog/{post.Slug}");
        }
        else if (intent == "unpublish")
            await monitoring.LogActionAsync("blog.unpublish", "BlogPost", post.Id, post.Title);

        SuccessMessage = intent switch
        {
            "review" => "مقاله برای بررسی سردبیر ارسال شد.",
            "publish" => post.PublishedAt > DateTimeOffset.UtcNow ? $"مقاله برای انتشار در {Format.Date(post.PublishedAt.Value)} زمان‌بندی شد." : "مقاله منتشر شد.",
            "unpublish" => "مقاله از حالت انتشار خارج شد.",
            _ => "تغییرات ذخیره شد.",
        };
        return Redirect($"/admin/blog/edit/{post.Id}");
    }

    /// <summary>Editor: send the article back to its writer with a note.</summary>
    [AdminPermission(AdminPermissions.BlogPublish)]
    public async Task<IActionResult> OnPostReturnAsync(Guid id, string? note)
    {
        var post = await data.FindAsync<BlogPost>(id);
        if (post is null)
            return NotFound();
        if (string.IsNullOrWhiteSpace(note))
        {
            ErrorMessage = "یادداشت اصلاحات را برای نویسنده بنویسید.";
            return Redirect($"/admin/blog/edit/{id}");
        }
        post.SubmittedForReviewAt = null;
        post.IsPublished = false;
        post.ReviewNote = note.Trim().Length > 1000 ? note.Trim()[..1000] : note.Trim();
        var result = await data.UpdateAsync(post);
        if (Succeeded(result))
            await NotifyAuthorAsync(post, "مقاله نیاز به اصلاح دارد", $"سردبیر مقاله «{post.Title}» را برای اصلاح برگرداند: {post.ReviewNote}", $"/admin/blog/edit/{post.Id}");
        SuccessMessage = Succeeded(result) ? "مقاله با یادداشت به نویسنده برگشت." : null;
        if (!Succeeded(result)) ErrorMessage = result.Message;
        return Redirect($"/admin/blog/edit/{id}");
    }

    /// <summary>SEO-only save (SEO specialists).</summary>
    public async Task<IActionResult> OnPostSeoAsync(Guid id)
    {
        if (!await LoadPostAsync(id))
            return NotFound();
        if (!CanSeo)
            return Redirect("/admin/denied?need=" + AdminPermissions.SeoManage);
        var error = Input.Seo.Validate();
        if (error is not null)
        {
            ErrorMessage = error;
            return Redirect($"/admin/blog/edit/{id}#seo");
        }
        var post = await data.FindAsync<BlogPost>(id);
        post!.Seo = Input.Seo.ApplyToCopy(post.Seo);
        ShowResult(await data.UpdateAsync(post));
        return Redirect($"/admin/blog/edit/{id}#seo");
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        if (!await LoadPostAsync(id))
            return NotFound();
        if (!(IsEditor || (Post!.AuthorId == Access.UserId && !Post.IsPublished && Can(AdminPermissions.BlogWrite))))
            return Redirect("/admin/denied?need=" + AdminPermissions.BlogPublish);
        var result = await data.DeleteAsync<BlogPost>(id);
        if (Succeeded(result))
            await monitoring.LogActionAsync("blog.delete", "BlogPost", id, Post!.Title);
        ShowResult(result);
        return Succeeded(result) ? Redirect("/admin/blog") : Redirect($"/admin/blog/edit/{id}");
    }

    private bool CanView() => IsEditor || Can(AdminPermissions.SeoManage) || Post!.AuthorId == Access.UserId;

    private async Task<bool> LoadPostAsync(Guid id)
    {
        Post = await data.Query<BlogPost>().FirstOrDefaultAsync(p => p.Id == id);
        if (Post?.AuthorId is { } authorId)
            AuthorName = await data.Query<Data.Entities.Users.User>().Where(u => u.Id == authorId)
                .Select(u => ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim()).FirstOrDefaultAsync();
        return Post is not null;
    }

    private async Task NotifyAuthorAsync(BlogPost post, string title, string body, string link)
    {
        if (post.AuthorId is not { } authorId || authorId == Access.UserId)
            return;
        await notifications.NotifyAsync(new NotificationRequest
        {
            UserId = authorId,
            Category = NotificationCategory.System,
            LinkUrl = link,
            Content = new NotificationContent { Title = title, Body = body, Push = true },
        });
    }

    private async Task<IActionResult> FailAsync(string message)
    {
        ErrorMessage = message;
        await LoadListsAsync();
        return Page();
    }

    private async Task LoadListsAsync() =>
        Categories = await data.Query<BlogCategory>().Where(c => c.IsActive || c.Id == Input.CategoryId).OrderBy(c => c.DisplayOrder).ToListAsync();

    private static string EditModelSlug(string value) => Products.EditModel.Slugify(value);

    private static PostInput ToInput(BlogPost p) => new()
    {
        Title = p.Title, Slug = p.Slug, CategoryId = p.BlogCategoryId, Excerpt = p.Excerpt, Content = p.Content, ImageAlt = p.ImageAlt,
        PublishDate = p.PublishedAt is { } d ? PersianDateInput.Format(IranClock.Today(d)) : null,
        Seo = SeoInput.From(p.Seo),
    };
}
