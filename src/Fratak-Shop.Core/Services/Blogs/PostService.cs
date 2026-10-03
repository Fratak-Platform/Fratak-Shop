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
        if ((string.IsNullOrWhiteSpace(command.Title)))
            return OperationResult.Error("عنوان را وارد کنید ");

        if ((string.IsNullOrWhiteSpace(command.Slug)))
            return OperationResult.Error("را وارد کنید ");

        if ((string.IsNullOrWhiteSpace(command.Content)))
            return OperationResult.Error("توضیحات را وارد کنید");

        var model = new BlogPost
        {
            AuthorId = command.AuthorId,
            BlogCategoryId = command.BlogCategoryId,
            Title = command.Title,
            Slug = command.Slug,
            Excerpt = command.Excerpt,
            Content = command.Content,
            ImageName = command.ImageName,
            ImageAlt = command.ImageAlt,
            ViewCount = command.ViewCount,
            IsPublished = command.IsPublished,
            PublishedAt = command.PublishedAt,
            Seo = command.Seo,
        };

        context.BlogPosts.Add(model);
        await context.SaveChangesAsync();
        return OperationResult.Success();
    }

    public async Task<OperationResult> EditPostAsync(EditBlogPostCommand command)
    {
        var post = await context.BlogPosts.AsTracking().FirstOrDefaultAsync(p => p.Id == command.Id);
        if (post == null)
            return OperationResult.NotFound();

        if ((string.IsNullOrWhiteSpace(command.Title)))
            return OperationResult.Error("عنوان را وارد کنید ");

        if ((string.IsNullOrWhiteSpace(command.Slug)))
            return OperationResult.Error("را وارد کنید ");

        if ((string.IsNullOrWhiteSpace(command.Content)))
            return OperationResult.Error("توضیحات را وارد کنید");


        post.Title = command.Title;
        post.Slug = command.Slug;
        post.Content = command.Content;
        post.AuthorId = command.AuthorId;
        post.BlogCategoryId = command.BlogCategoryId;
        post.IsPublished = command.IsPublished;
        post.PublishedAt = command.PublishedAt;
        post.Seo = command.Seo;
        post.Excerpt = command.Excerpt;
        post.ImageAlt = command.ImageAlt;
        post.ImageName = command.ImageName;
        post.ViewCount = command.ViewCount;
        await context.SaveChangesAsync();
        return OperationResult.Success();
    }

    public async Task<OperationResult> DeletePostAsync(Guid id)
    {
        var post = await context.BlogPosts.AsTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (post == null)
            return OperationResult.NotFound();

        context.BlogPosts.Remove(post);
        await context.SaveChangesAsync();
        return OperationResult.Success();
    }

    public async Task AddPostVisitAsync(Guid id)
    {
        var post = await context.BlogPosts.AsTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (post != null)
        {
            post.ViewCount++;
            await context.SaveChangesAsync();
        }
    }

    public async Task<BlogPostFilterResult> GetPostByFilterAsync(BlogPostFilterParams filterParams)
    {
        var posts = context.BlogPosts.Include(s => s.BlogCategory)
            .OrderBy(p => p.CreatedAt).AsQueryable();

        if (filterParams.Search != null)
            posts = posts.Where(p => p.Title.Contains(filterParams.Search)
                                   || p.Slug.Contains(filterParams.Search));

        if (filterParams.CategorySlug != null)
            posts = posts.Where(p => p.Slug.Contains(filterParams.CategorySlug)
                                   || p.BlogCategory.Slug.Contains(filterParams.CategorySlug));

        var skip = (filterParams.PageId - 1) * filterParams.Take;
        var result = await posts.Skip(skip).Take(filterParams.Take).ToListAsync();
        var model = new BlogPostFilterResult
        {
            Data = result.MapFilterData(),
            FilterParams = filterParams
        };
        model.GeneratePaging(posts, filterParams.Take, filterParams.PageId);
        return model;
    }

    public async Task<PostDto?> GetPostByAsync(string slug)
    {
        var post = await context.BlogPosts.FirstOrDefaultAsync(p => p.Slug == slug);
        return post?.Map();
    }

    public async Task<PostDto?> GetPostByAsync(Guid id)
    {
        var post = await context.BlogPosts.FirstOrDefaultAsync(p => p.Id == id);
        return post?.Map();
    }
}
