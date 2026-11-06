using Fratak_Shop.Core.DTOs.SeoRedirects.Command;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.SeoRedirects;
using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Data.Entities.Brands;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Contents;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Searches;
using Fratak_Shop.Data.Entities.SeoRedirects;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Seo;

public sealed record SeoAuditItem(string Kind, string Title, string EditUrl, string PublicUrl, string Problem);
public sealed record SeoAuditCount(string Kind, int Total, int MissingDescription, int MissingTitle, int NoIndex);

/// <summary>
/// SEO workspace: content audit (missing meta / noindex), 404s → one-click redirect, searches without
/// results (→ synonyms / new products) and shortcuts to redirects, static-page SEO, sitemap and robots.
/// </summary>
[AdminPermission(AdminPermissions.SeoManage)]
public class IndexModel(IAdminDataService data, ISeoRedirectService redirects, IMonitoringService monitoring) : AdminPageModel
{
    [BindProperty(SupportsGet = true, Name = "tab")] public string? Tab { get; set; }

    public List<SeoAuditCount> Counts { get; private set; } = [];
    public List<SeoAuditItem> Audit { get; private set; } = [];
    public List<SeoNotFoundLog> NotFoundLogs { get; private set; } = [];
    public List<SearchQueryLog> EmptySearches { get; private set; } = [];
    public int RedirectCount { get; private set; }
    public int Unresolved404 { get; private set; }

    public async Task OnGetAsync()
    {
        Tab ??= "audit";
        RedirectCount = await data.Query<SeoRedirect>().CountAsync(r => r.IsActive);
        Unresolved404 = await data.Query<SeoNotFoundLog>().CountAsync(l => !l.IsResolved);

        Counts =
        [
            new("محصولات", await data.Query<Product>().CountAsync(p => p.IsActive),
                await data.Query<Product>().CountAsync(p => p.IsActive && (p.Seo == null || p.Seo.MetaDescription == null || p.Seo.MetaDescription == "")),
                await data.Query<Product>().CountAsync(p => p.IsActive && (p.Seo == null || p.Seo.Title == null || p.Seo.Title == "")),
                await data.Query<Product>().CountAsync(p => p.IsActive && p.Seo != null && p.Seo.NoIndex == true)),
            new("دسته‌بندی‌ها", await data.Query<Category>().CountAsync(c => c.IsActive),
                await data.Query<Category>().CountAsync(c => c.IsActive && (c.Seo == null || c.Seo.MetaDescription == null || c.Seo.MetaDescription == "")),
                await data.Query<Category>().CountAsync(c => c.IsActive && (c.Seo == null || c.Seo.Title == null || c.Seo.Title == "")),
                await data.Query<Category>().CountAsync(c => c.IsActive && c.Seo != null && c.Seo.NoIndex == true)),
            new("برندها", await data.Query<Brand>().CountAsync(b => b.IsActive),
                await data.Query<Brand>().CountAsync(b => b.IsActive && (b.Seo == null || b.Seo.MetaDescription == null || b.Seo.MetaDescription == "")),
                await data.Query<Brand>().CountAsync(b => b.IsActive && (b.Seo == null || b.Seo.Title == null || b.Seo.Title == "")),
                await data.Query<Brand>().CountAsync(b => b.IsActive && b.Seo != null && b.Seo.NoIndex == true)),
            new("مقاله‌ها", await data.Query<BlogPost>().CountAsync(p => p.IsPublished),
                await data.Query<BlogPost>().CountAsync(p => p.IsPublished && (p.Seo == null || p.Seo.MetaDescription == null || p.Seo.MetaDescription == "")),
                await data.Query<BlogPost>().CountAsync(p => p.IsPublished && (p.Seo == null || p.Seo.Title == null || p.Seo.Title == "")),
                await data.Query<BlogPost>().CountAsync(p => p.IsPublished && p.Seo != null && p.Seo.NoIndex == true)),
        ];

        switch (Tab)
        {
            case "404":
                NotFoundLogs = await data.Query<SeoNotFoundLog>().Where(l => !l.IsResolved).OrderByDescending(l => l.HitCount).ThenByDescending(l => l.LastHitAt).Take(200).ToListAsync();
                break;
            case "search":
                EmptySearches = await data.Query<SearchQueryLog>().Where(q => q.LastResultCount == 0).OrderByDescending(q => q.SearchCount).Take(100).ToListAsync();
                break;
            default:
                await LoadAuditAsync();
                break;
        }
    }

    private async Task LoadAuditAsync()
    {
        const int take = 40;
        var products = await data.Query<Product>()
            .Where(p => p.IsActive && (p.Seo == null || p.Seo.MetaDescription == null || p.Seo.MetaDescription == "" || p.Seo.NoIndex == true))
            .OrderByDescending(p => p.ViewCount).Take(take)
            .Select(p => new { p.Id, p.Name, p.Slug, NoIndex = p.Seo != null && p.Seo.NoIndex == true }).ToListAsync();
        Audit.AddRange(products.Select(p => new SeoAuditItem("محصول", p.Name, $"/admin/products/edit/{p.Id}#seo", $"/product/{p.Slug}", p.NoIndex ? "noindex فعال است" : "بدون توضیحات متا")));

        var posts = await data.Query<BlogPost>()
            .Where(p => p.IsPublished && (p.Seo == null || p.Seo.MetaDescription == null || p.Seo.MetaDescription == "" || p.Seo.NoIndex == true))
            .OrderByDescending(p => p.ViewCount).Take(take)
            .Select(p => new { p.Id, p.Title, p.Slug, NoIndex = p.Seo != null && p.Seo.NoIndex == true }).ToListAsync();
        Audit.AddRange(posts.Select(p => new SeoAuditItem("مقاله", p.Title, $"/admin/blog/edit/{p.Id}#seo", $"/blog/{p.Slug}", p.NoIndex ? "noindex فعال است" : "بدون توضیحات متا")));

        var categories = await data.Query<Category>()
            .Where(c => c.IsActive && (c.Seo == null || c.Seo.MetaDescription == null || c.Seo.MetaDescription == "" || c.Seo.NoIndex == true))
            .OrderBy(c => c.Name).Take(take)
            .Select(c => new { c.Id, c.Name, c.Slug, NoIndex = c.Seo != null && c.Seo.NoIndex == true }).ToListAsync();
        Audit.AddRange(categories.Select(c => new SeoAuditItem("دسته‌بندی", c.Name, $"/admin/manage/categories/edit/{c.Id}", $"/category/{c.Slug}", c.NoIndex ? "noindex فعال است" : "بدون توضیحات متا")));

        var brands = await data.Query<Brand>()
            .Where(b => b.IsActive && (b.Seo == null || b.Seo.MetaDescription == null || b.Seo.MetaDescription == "" || b.Seo.NoIndex == true))
            .OrderBy(b => b.Name).Take(take)
            .Select(b => new { b.Id, b.Name, b.Slug, NoIndex = b.Seo != null && b.Seo.NoIndex == true }).ToListAsync();
        Audit.AddRange(brands.Select(b => new SeoAuditItem("برند", b.Name, $"/admin/manage/brands/edit/{b.Id}", $"/brand/{b.Slug}", b.NoIndex ? "noindex فعال است" : "بدون توضیحات متا")));
    }

    /// <summary>404 → redirect in one step (marks the 404 as resolved).</summary>
    public async Task<IActionResult> OnPostRedirectAsync(Guid id, string? toUrl, RedirectType type = RedirectType.MovedPermanently)
    {
        var log = await data.FindAsync<SeoNotFoundLog>(id);
        if (log is null)
            return NotFound();
        var result = await redirects.CreateRedirectAsync(new CreateSeoRedirectCommand { FromPath = log.Path, ToUrl = toUrl?.Trim() ?? "", Type = type, Notes = "از گزارش ۴۰۴", IsActive = true });
        if (Succeeded(result))
        {
            log.IsResolved = true;
            await data.UpdateAsync(log);
            await monitoring.LogActionAsync("seo.redirect", "SeoNotFoundLog", id, $"{log.Path} → {toUrl}");
        }
        ShowResult(result);
        return Redirect("/admin/seo?tab=404");
    }

    public async Task<IActionResult> OnPostResolveAsync(List<Guid> ids)
    {
        var count = 0;
        foreach (var id in ids.Distinct().Take(500))
        {
            var log = await data.FindAsync<SeoNotFoundLog>(id);
            if (log is null) continue;
            log.IsResolved = true;
            if (Succeeded(await data.UpdateAsync(log))) count++;
        }
        SuccessMessage = $"{count} مورد بررسی‌شده علامت خورد.";
        return Redirect("/admin/seo?tab=404");
    }
}
