using Fratak_Shop.Core.DTOs.Faqs.Command;
using Fratak_Shop.Core.DTOs.Faqs.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Faqs;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Faqs;

public class FaqItemService(FratakShopDbContext context) : IFaqItemService
{
    public async Task<OperationResult> CreateFaqItemAsync(CreateFaqItemCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Question))
            return OperationResult.Error("سوال را وارد کنید.");

        if (string.IsNullOrWhiteSpace(command.Answer))
            return OperationResult.Error("پاسخ را وارد کنید.");

        var model = new FaqItem
        {
            Question = command.Question.Trim(),
            Answer = command.Answer.Trim(),
            DisplayOrder = command.DisplayOrder,
            IsActive = command.IsActive,
        };

        context.FaqItems.Add(model);
        await context.SaveChangesAsync();

        return OperationResult.Success("سوال متداول با موفقیت ایجاد شد.");
    }

    public async Task<OperationResult> EditFaqItemAsync(EditFaqItemCommand command)
    {
        var faqItem = await context.FaqItems.AsTracking().FirstOrDefaultAsync(x => x.Id == command.Id);
        if (faqItem is null)
            return OperationResult.NotFound("سوال متداول مورد نظر یافت نشد.");

        if (string.IsNullOrWhiteSpace(command.Question))
            return OperationResult.Error("سوال را وارد کنید.");

        if (string.IsNullOrWhiteSpace(command.Answer))
            return OperationResult.Error("پاسخ را وارد کنید.");

        faqItem.Question = command.Question.Trim();
        faqItem.Answer = command.Answer.Trim();
        faqItem.DisplayOrder = command.DisplayOrder;
        faqItem.IsActive = command.IsActive;

        await context.SaveChangesAsync();

        return OperationResult.Success("سوال متداول با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeleteFaqItemAsync(Guid id)
    {
        var faqItem = await context.FaqItems.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (faqItem is null)
            return OperationResult.NotFound("سوال متداول مورد نظر یافت نشد.");

        context.FaqItems.Remove(faqItem);
        await context.SaveChangesAsync();

        return OperationResult.Success("سوال متداول با موفقیت حذف شد.");
    }

    public List<FaqItemDto> GetAllFaqItemsAsync()
    {
        return context.FaqItems
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.CreatedAt)
            .Select(x => new FaqItemDto
            {
                Id = x.Id,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                IsDeleted = x.IsDeleted,
                DeletedAt = x.DeletedAt,
                Question = x.Question,
                Answer = x.Answer,
                DisplayOrder = x.DisplayOrder,
                IsActive = x.IsActive,
            })
            .ToList();
    }

    public async Task<List<FaqItemDto>> GetActiveFaqItemsAsync()
    {
        return await context.FaqItems
            .Where(x => x.IsActive)
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.CreatedAt)
            .Select(x => new FaqItemDto
            {
                Id = x.Id,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                IsDeleted = x.IsDeleted,
                DeletedAt = x.DeletedAt,
                Question = x.Question,
                Answer = x.Answer,
                DisplayOrder = x.DisplayOrder,
                IsActive = x.IsActive,
            })
            .ToListAsync();
    }

    public async Task<FaqItemDto?> GetFaqItemByIdAsync(Guid id)
    {
        var faqItem = await context.FaqItems
            .FirstOrDefaultAsync(x => x.Id == id);

        if (faqItem is null)
            return null;

        return new FaqItemDto
        {
            Id = faqItem.Id,
            CreatedAt = faqItem.CreatedAt,
            UpdatedAt = faqItem.UpdatedAt,
            IsDeleted = faqItem.IsDeleted,
            DeletedAt = faqItem.DeletedAt,
            Question = faqItem.Question,
            Answer = faqItem.Answer,
            DisplayOrder = faqItem.DisplayOrder,
            IsActive = faqItem.IsActive,
        };
    }
}
