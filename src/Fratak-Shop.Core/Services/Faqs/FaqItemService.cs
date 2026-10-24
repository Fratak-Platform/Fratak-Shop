using Fratak_Shop.Core.DTOs.Faqs.Command;
using Fratak_Shop.Core.DTOs.Faqs.Query;
using Fratak_Shop.Core.Mappers.Faqs;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Faqs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Faqs;

public class FaqItemService(FratakShopDbContext context, IMemoryCache cache) : IFaqItemService
{
    public async Task<OperationResult> CreateFaqItemAsync(CreateFaqItemCommand command)
    {
        var error = await ValidateAsync(command);
        if (error is not null)
            return OperationResult.Error(error);

        var model = new FaqItem
        {
            CategoryId = command.CategoryId,
            Question = command.Question.Trim().ToPlainText(),
            Answer = command.Answer.Trim().SanitizeText(),
            DisplayOrder = command.DisplayOrder,
            IsActive = command.IsActive,
        };

        context.FaqItems.Add(model);
        await context.SaveChangesAsync();
        cache.Remove(CacheKeys.FaqGroups);

        return OperationResult.Success("سوال متداول با موفقیت ایجاد شد.");
    }

    public async Task<OperationResult> EditFaqItemAsync(EditFaqItemCommand command)
    {
        var faqItem = await context.FaqItems.AsTracking().FirstOrDefaultAsync(x => x.Id == command.Id);
        if (faqItem is null)
            return OperationResult.NotFound("سوال متداول مورد نظر یافت نشد.");

        var error = await ValidateAsync(command);
        if (error is not null)
            return OperationResult.Error(error);

        faqItem.CategoryId = command.CategoryId;
        faqItem.Question = command.Question.Trim().ToPlainText();
        faqItem.Answer = command.Answer.Trim().SanitizeText();
        faqItem.DisplayOrder = command.DisplayOrder;
        faqItem.IsActive = command.IsActive;

        await context.SaveChangesAsync();
        cache.Remove(CacheKeys.FaqGroups);

        return OperationResult.Success("سوال متداول با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeleteFaqItemAsync(Guid id)
    {
        var faqItem = await context.FaqItems.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (faqItem is null)
            return OperationResult.NotFound("سوال متداول مورد نظر یافت نشد.");

        context.FaqItems.Remove(faqItem);
        await context.SaveChangesAsync();
        cache.Remove(CacheKeys.FaqGroups);

        return OperationResult.Success("سوال متداول با موفقیت حذف شد.");
    }

    public List<FaqItemDto> GetAllFaqItemsAsync()
    {
        return context.FaqItems
            .Include(x => x.Category)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.CreatedAt)
            .ToList()
            .Map();
    }

    public async Task<List<FaqItemDto>> GetActiveFaqItemsAsync()
    {
        var faqItems = await context.FaqItems
            .Include(x => x.Category)
            .Where(x => x.IsActive && (x.CategoryId == null || x.Category!.IsActive))
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync();

        return faqItems.Map();
    }

    public async Task<FaqItemDto?> GetFaqItemByIdAsync(Guid id)
    {
        var faqItem = await context.FaqItems.Include(x => x.Category).FirstOrDefaultAsync(x => x.Id == id);
        return faqItem?.Map();
    }

    public async Task<List<FaqGroupDto>> GetFaqGroupsAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue<List<FaqGroupDto>>(CacheKeys.FaqGroups, out var cached) && cached is not null)
            return cached;

        var categories = await context.FaqCategories
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Title)
            .ToListAsync(cancellationToken);

        var items = await context.FaqItems
            .Where(x => x.IsActive && (x.CategoryId == null || x.Category!.IsActive))
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

        var groups = categories
            .Select(c => new FaqGroupDto
            {
                CategoryId = c.Id,
                Title = c.Title,
                Slug = c.Slug,
                Description = c.Description,
                IconKey = c.IconKey,
                Items = items.Where(i => i.CategoryId == c.Id).Select(i => i.Map()).ToList(),
            })
            .Where(g => g.Items.Count > 0)
            .ToList();

        var uncategorized = items.Where(i => i.CategoryId == null).Select(i => i.Map()).ToList();
        if (uncategorized.Count > 0)
            groups.Add(new FaqGroupDto { Title = "سایر سوالات", Slug = "other", Items = uncategorized });

        cache.Set(CacheKeys.FaqGroups, groups, CacheKeys.DefaultLifetime);
        return groups;
    }

    public async Task<List<FaqCategoryDto>> GetCategoriesAsync()
    {
        var categories = await context.FaqCategories
            .Include(c => c.Items)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync();

        return categories.Select(c => c.Map()).ToList();
    }

    public async Task<OperationResult<Guid>> SaveCategoryAsync(FaqCategoryCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Title))
            return OperationResult<Guid>.Error("عنوان دسته را وارد کنید.");

        var slug = (string.IsNullOrWhiteSpace(command.Slug) ? command.Title : command.Slug).ToSlug();
        if (await context.FaqCategories.AnyAsync(c => c.Slug == slug && c.Id != command.Id))
            return OperationResult<Guid>.Error("نامک دسته تکراری است.");

        FaqCategory? category;
        if (command.Id == Guid.Empty)
        {
            category = new FaqCategory();
            context.FaqCategories.Add(category);
        }
        else
        {
            category = await context.FaqCategories.AsTracking().FirstOrDefaultAsync(c => c.Id == command.Id);
            if (category is null)
                return OperationResult<Guid>.NotFound();
        }

        category.Title = command.Title.Trim().ToPlainText();
        category.Slug = slug;
        category.Description = command.Description.ToPlainText() is { Length: > 0 } d ? d : null;
        category.IconKey = string.IsNullOrWhiteSpace(command.IconKey) ? null : command.IconKey.Trim();
        category.IsActive = command.IsActive;
        category.DisplayOrder = command.DisplayOrder;

        await context.SaveChangesAsync();
        cache.Remove(CacheKeys.FaqGroups);
        return OperationResult<Guid>.Success(category.Id, "دسته سوالات ذخیره شد.");
    }

    public async Task<OperationResult> DeleteCategoryAsync(Guid id)
    {
        var category = await context.FaqCategories.AsTracking().FirstOrDefaultAsync(c => c.Id == id);
        if (category is null)
            return OperationResult.NotFound();

        // Questions of a deleted category move to "other questions" instead of disappearing.
        await context.FaqItems
            .Where(i => i.CategoryId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(i => i.CategoryId, (Guid?)null));

        context.FaqCategories.Remove(category);
        await context.SaveChangesAsync();
        cache.Remove(CacheKeys.FaqGroups);
        return OperationResult.Success("دسته سوالات حذف شد.");
    }

    private async Task<string?> ValidateAsync(CreateFaqItemCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Question))
            return "سوال را وارد کنید.";

        if (string.IsNullOrWhiteSpace(command.Answer))
            return "پاسخ را وارد کنید.";

        if (command.CategoryId is { } categoryId && !await context.FaqCategories.AnyAsync(c => c.Id == categoryId))
            return "دسته انتخاب‌شده معتبر نیست.";

        return null;
    }
}
