using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Blog;

public sealed class AdminPostRow
{
    public Guid Id { get; init; }
    public string Title { get; init; } = "";
    public string Slug { get; init; } = "";
    public string Category { get; init; } = "";
    public string? ImageName { get; init; }
    public Guid? AuthorId { get; init; }
    public string? AuthorName { get; init; }
    public bool IsPublished { get; init; }
    public DateTimeOffset? PublishedAt { get; init; }
    public DateTimeOffset? SubmittedForReviewAt { get; init; }
    public string? ReviewNote { get; init; }
    public int ViewCount { get; init; }
    public int CommentCount { get; init; }
    public bool HasSeo { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

/// <summary>
/// Blog list with an editorial workflow: writers see their own posts (draft → review), editors (publish
/// permission) see everything, review the queue and publish / schedule / return with a note.
/// </summary>
[AdminPermission(AdminPermissions.BlogWrite, AdminPermissions.BlogPublish, AdminPermissions.SeoManage)]
public class IndexModel(IAdminDataService data) : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "status")] public string? Status { get; set; }
    [BindProperty(SupportsGet = true, Name = "category")] public Guid? CategoryId { get; set; }
    [BindProperty(SupportsGet = true, Name = "q")] public string? Search { get; set; }
    [FromQuery(Name = "page")] public int PageNumber { get; set; } = 1;

    public PagedList<AdminPostRow> Items { get; private set; } = new([], 0, 1, 20);
    public List<BlogCategory> Categories { get; private set; } = [];
    public Dictionary<string, int> Counts { get; private set; } = new();
    public bool IsEditor => Can(AdminPermissions.BlogPublish);
    public bool SeesAll => IsEditor || Can(AdminPermissions.SeoManage);

    public async Task OnGetAsync()
    {
        Categories = await data.Query<BlogCategory>().OrderBy(c => c.DisplayOrder).ToListAsync();
        var now = DateTimeOffset.UtcNow;
        var scope = Scope();
        Counts = new Dictionary<string, int>
        {
            ["all"] = await scope.CountAsync(),
            ["review"] = await scope.CountAsync(p => !p.IsPublished && p.SubmittedForReviewAt != null),
            ["draft"] = await scope.CountAsync(p => !p.IsPublished && p.SubmittedForReviewAt == null),
            ["published"] = await scope.CountAsync(p => p.IsPublished && (p.PublishedAt == null || p.PublishedAt <= now)),
            ["scheduled"] = await scope.CountAsync(p => p.IsPublished && p.PublishedAt > now),
        };

        var query = Status switch
        {
            "review" => scope.Where(p => !p.IsPublished && p.SubmittedForReviewAt != null),
            "draft" => scope.Where(p => !p.IsPublished && p.SubmittedForReviewAt == null),
            "published" => scope.Where(p => p.IsPublished && (p.PublishedAt == null || p.PublishedAt <= now)),
            "scheduled" => scope.Where(p => p.IsPublished && p.PublishedAt > now),
            _ => scope,
        };
        if (CategoryId is { } categoryId) query = query.Where(p => p.BlogCategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(Search))
        {
            var term = Search.Trim();
            query = query.Where(p => p.Title.Contains(term) || p.Slug.Contains(term));
        }

        var total = await query.CountAsync();
        var pageNumber = ClampPage(PageNumber, total, 20);
        var ordered = Status == "review" ? query.OrderBy(p => p.SubmittedForReviewAt) : query.OrderByDescending(p => p.UpdatedAt);
        var rows = await ordered
            .Skip((pageNumber - 1) * 20).Take(20)
            .Select(p => new AdminPostRow
            {
                Id = p.Id, Title = p.Title, Slug = p.Slug, Category = p.BlogCategory.Name, ImageName = p.ImageName, AuthorId = p.AuthorId,
                AuthorName = p.Author != null ? ((p.Author.FirstName ?? "") + " " + (p.Author.LastName ?? "")).Trim() : null,
                IsPublished = p.IsPublished, PublishedAt = p.PublishedAt, SubmittedForReviewAt = p.SubmittedForReviewAt, ReviewNote = p.ReviewNote,
                ViewCount = p.ViewCount, CommentCount = p.Comments.Count,
                HasSeo = p.Seo != null && p.Seo.MetaDescription != null && p.Seo.MetaDescription != "",
                CreatedAt = p.CreatedAt, UpdatedAt = p.UpdatedAt,
            })
            .ToListAsync();
        Items = new PagedList<AdminPostRow>(rows, total, pageNumber, 20);
    }

    private IQueryable<BlogPost> Scope()
    {
        var query = data.Query<BlogPost>();
        if (!SeesAll)
        {
            var me = Access.UserId;
            query = query.Where(p => p.AuthorId == me);
        }
        return query;
    }
}
