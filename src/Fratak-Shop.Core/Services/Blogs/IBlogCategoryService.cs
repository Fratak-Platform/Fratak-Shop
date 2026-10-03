using Fratak_Shop.Core.DTOs.Blogs.Categories.Command;
using Fratak_Shop.Core.DTOs.Blogs.Categories.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Blogs;

public interface IBlogCategoryService
{
    //blog category
    public Task<OperationResult> CreateCategoryAsync(CreateBlogCategoryCommand command);
    public Task<OperationResult> EditCategoryAsync(EditBlogCategoryCommand command);
    public Task<OperationResult> DeleteCategoryAsync(Guid id);
    public List<BlogCategoryDto> GetAllCategoriesAsync();
    public Task<BlogCategoryDto?> GetCategoryByIdAsync(Guid id);
}
