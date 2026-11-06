using Fratak_Shop.Core.DTOs.Faqs.Query;
using Fratak_Shop.Data.Entities.Faqs;

namespace Fratak_Shop.Core.Mappers.Faqs;

public static class FaqItemMapper
{
    public static FaqItemDto Map(this FaqItem faqItem)
    {
        return new FaqItemDto
        {
            Id = faqItem.Id,
            CreatedAt = faqItem.CreatedAt,
            UpdatedAt = faqItem.UpdatedAt,
            IsDeleted = faqItem.IsDeleted,
            DeletedAt = faqItem.DeletedAt,
            CategoryId = faqItem.CategoryId,
            CategoryTitle = faqItem.Category?.Title,
            Question = faqItem.Question,
            Answer = faqItem.Answer,
            DisplayOrder = faqItem.DisplayOrder,
            IsActive = faqItem.IsActive,
        };
    }

    public static List<FaqItemDto> Map(this List<FaqItem> faqItems)
    {
        return faqItems.Select(faqItem => faqItem.Map()).ToList();
    }

    public static FaqCategoryDto Map(this FaqCategory category)
    {
        return new FaqCategoryDto
        {
            Id = category.Id,
            CreatedAt = category.CreatedAt,
            UpdatedAt = category.UpdatedAt,
            IsDeleted = category.IsDeleted,
            DeletedAt = category.DeletedAt,
            Title = category.Title,
            Slug = category.Slug,
            Description = category.Description,
            IconKey = category.IconKey,
            IsActive = category.IsActive,
            DisplayOrder = category.DisplayOrder,
            ItemCount = category.Items.Count,
        };
    }
}
