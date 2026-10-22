using Fratak_Shop.Core.DTOs.Blogs.Posts.Command;
using Fratak_Shop.Core.DTOs.Blogs.Posts.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Blogs;

public interface IPostService
{
    //blog post 
    public Task<OperationResult> CreatePostAsync(CreateBlogPostCommand command);
    public Task<OperationResult> EditPostAsync(EditBlogPostCommand command);
    public Task<OperationResult> DeletePostAsync(Guid id);
    public Task AddPostVisitAsync(Guid id);

    public Task<BlogPostFilterResult> GetPostByFilterAsync(BlogPostFilterParams filterParams);
    public Task<PostDto?> GetPostByAsync(string slug);
    public Task<PostDto?> GetPostByAsync(Guid id);

    /// <summary>آخرین مقالات منتشرشده (برای صفحه نخست و ستون کناری مقاله).</summary>
    public Task<List<BlogPostFilterData>> GetLatestPostsAsync(int take = 4, Guid? excludePostId = null);





}
