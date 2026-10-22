using Fratak_Shop.Core.DTOs.Blogs.Posts.Command;
using Fratak_Shop.Core.DTOs.Blogs.Posts.Query;
using Fratak_Shop.Core.Mappers.Blog;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Blogs;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Blogs;

public class PostService(FratakShopDbContext context) : IPostService
{
    public async Task<OperationResult> CreatePostAsync(CreateBlogPostCommand command)
    {
        var error = Validate(command.Title, command.Slug, command.Content);
        if (error is not null)
            return OperationResult.Error(error);

        var authorExists = await context.Users.AnyAsync(x => x.Id == command.AuthorId);
        if (!authorExists)
            return OperationResult.NotFound("نویسنده مقاله یافت نشد.");

        var categoryExists = await context.BlogCategories.AnyAsync(x => x.Id == command.BlogCategoryId);
        if (!categoryExists)
            return OperationResult.NotFound("دسته‌بندی مقاله یافت نشد.");

        var slug = command.Slug.Trim();

        var slugExists = await context.BlogPosts.AnyAsync(x => x.Slug == slug);
        if (slugExists)
            return OperationResult.Error("این اسلاگ قبلاً برای مقاله دیگری ثبت شده است.");

        var model = new BlogPost
        {
            AuthorId = command.AuthorId,
            BlogCategoryId = command.BlogCategoryId,
            Title = command.Title.Trim(),
            Slug = slug,
            Excerpt = command.Excerpt,
            Content = command.Content,
            ImageName = command.ImageName,
            ImageAlt = command.ImageAlt,
            ViewCount = command.ViewCount,
            IsPublished = command.IsPublished,
            PublishedAt = command.PublishedAt ?? (command.IsPublished ? DateTimeOffset.UtcNow : null),
            Seo = command.Seo,
        };

        context.BlogPosts.Add(model);
        await context.SaveChangesAsync();

        return OperationResult.Success("مقاله با موفقیت ایجاد شد.");
    }

    public async Task<OperationResult> EditPostAsync(EditBlogPostCommand command)
    {
        var post = await context.BlogPosts.AsTracking().FirstOrDefaultAsync(p => p.Id == command.Id);
        if (post is null)
            return OperationResult.NotFound("مقاله مورد نظر یافت نشد.");

        var error = Validate(command.Title, command.Slug, command.Content);
        if (error is not null)
            return OperationResult.Error(error);

        var authorExists = await context.Users.AnyAsync(x => x.Id == command.AuthorId);
        if (!authorExists)
            return OperationResult.NotFound("نویسنده مقاله یافت نشد.");

        var categoryExists = await context.BlogCategories.AnyAsync(x => x.Id == command.BlogCategoryId);
        if (!categoryExists)
            return OperationResult.NotFound("دسته‌بندی مقاله یافت نشد.");

        var slug = command.Slug.Trim();

        var slugExists = await context.BlogPosts
            .AnyAsync(x => x.Id != command.Id && x.Slug == slug);

        if (slugExists)
            return OperationResult.Error("این اسلاگ قبلاً برای مقاله دیگری ثبت شده است.");

        post.AuthorId = command.AuthorId;
        post.BlogCategoryId = command.BlogCategoryId;
        post.Title = command.Title.Trim();
        post.Slug = slug;
        post.Excerpt = command.Excerpt;
        post.Content = command.Content;
        post.ImageName = command.ImageName;
        post.ImageAlt = command.ImageAlt;
        post.ViewCount = command.ViewCount;
        post.IsPublished = command.IsPublished;
        post.PublishedAt = command.PublishedAt
                           ?? post.PublishedAt
                           ?? (command.IsPublished ? DateTimeOffset.UtcNow : null);
        post.Seo = command.Seo;

        await context.SaveChangesAsync();

        return OperationResult.Success("مقاله با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeletePostAsync(Guid id)
    {
        var post = await context.BlogPosts.AsTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (post is null)
            return OperationResult.NotFound("مقاله مورد نظر یافت نشد.");

        context.BlogPosts.Remove(post);
        await context.SaveChangesAsync();

        return OperationResult.Success("مقاله با موفقیت حذف شد.");
    }

    public async Task AddPostVisitAsync(Guid id)
    {
        var post = await context.BlogPosts.AsTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (post is null)
            return;

        post.ViewCount++;
        await context.SaveChangesAsync();
    }

    public async Task<BlogPostFilterResult> GetPostByFilterAsync(BlogPostFilterParams filterParams)
    {
        var query = context.BlogPosts
            .Include(x => x.Author)
            .Include(x => x.BlogCategory)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filterParams.Search))
        {
            var search = filterParams.Search.Trim();
            query = query.Where(x => x.Title.Contains(search)
                                     || x.Slug.Contains(search)
                                     || (x.Excerpt != null && x.Excerpt.Contains(search)));
        }

        if (!string.IsNullOrWhiteSpace(filterParams.CategorySlug))
        {
            var categorySlug = filterParams.CategorySlug.Trim();
            query = query.Where(x => x.BlogCategory.Slug == categorySlug);
        }

        if (filterParams.OnlyPublished == true)
        {
            var now = DateTimeOffset.UtcNow;
            query = query
                .Where(x => x.IsPublished && (x.PublishedAt == null || x.PublishedAt <= now))
                .OrderByDescending(x => x.PublishedAt ?? x.CreatedAt);
        }
        else
        {
            query = query.OrderByDescending(x => x.CreatedAt);
        }

        var skip = (filterParams.PageId - 1) * filterParams.Take;

        var posts = await query
            .Skip(skip)
            .Take(filterParams.Take)
            .ToListAsync();

        var result = new BlogPostFilterResult
        {
            Data = posts.MapFilterData(),
            FilterParams = filterParams,
        };

        result.GeneratePaging(query, filterParams.Take, filterParams.PageId);

        return result;
    }

    public async Task<List<BlogPostFilterData>> GetLatestPostsAsync(int take = 4, Guid? excludePostId = null)
    {
        if (take <= 0)
            take = 4;

        var now = DateTimeOffset.UtcNow;

        var posts = await context.BlogPosts
            .Include(x => x.Author)
            .Include(x => x.BlogCategory)
            .Where(x => x.IsPublished
                        && (x.PublishedAt == null || x.PublishedAt <= now)
                        && (excludePostId == null || x.Id != excludePostId))
            .OrderByDescending(x => x.PublishedAt ?? x.CreatedAt)
            .Take(take)
            .ToListAsync();

        return posts.MapFilterData();
    }

    public async Task<PostDto?> GetPostByAsync(string slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        var post = await context.BlogPosts
            .Include(x => x.Author)
            .Include(x => x.BlogCategory)
            .FirstOrDefaultAsync(x => x.Slug == slug.Trim());

        return post?.Map();
    }

    public async Task<PostDto?> GetPostByAsync(Guid id)
    {
        var post = await context.BlogPosts
            .Include(x => x.Author)
            .Include(x => x.BlogCategory)
            .FirstOrDefaultAsync(x => x.Id == id);

        return post?.Map();
    }

    private static string? Validate(string? title, string? slug, string? content)
    {
        if (string.IsNullOrWhiteSpace(title))
            return "عنوان مقاله را وارد کنید.";

        if (string.IsNullOrWhiteSpace(slug))
            return "اسلاگ مقاله را وارد کنید.";

        if (string.IsNullOrWhiteSpace(content))
            return "متن مقاله را وارد کنید.";

        return null;
    }
}
