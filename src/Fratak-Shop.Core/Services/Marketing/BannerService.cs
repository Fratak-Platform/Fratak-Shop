using Fratak_Shop.Core.DTOs.Banners.Command;
using Fratak_Shop.Core.DTOs.Banners.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Marketing;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Marketing;

public class BannerService(FratakShopDbContext context) : IBannerService
{
    public async Task<OperationResult> CreateBannerAsync(CreateBannerCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.ImageName))
            return OperationResult.Error("نام تصویر بنر را وارد کنید.");

        if (command.StartAt.HasValue && command.EndAt.HasValue && command.StartAt > command.EndAt)
            return OperationResult.Error("تاریخ شروع نباید از تاریخ پایان بزرگ‌تر باشد.");

        var banner = new Banner
        {
            Title = command.Title,
            Subtitle = command.Subtitle,
            ImageName = command.ImageName.Trim(),
            MobileImageName = command.MobileImageName,
            ImageAlt = command.ImageAlt,
            ButtonText = command.ButtonText,
            LinkUrl = command.LinkUrl,
            DisplayOrder = command.DisplayOrder,
            StartAt = command.StartAt,
            EndAt = command.EndAt,
            IsActive = command.IsActive,
        };

        context.Banners.Add(banner);
        await context.SaveChangesAsync();

        return OperationResult.Success("بنر با موفقیت ایجاد شد.");
    }

    public async Task<OperationResult> EditBannerAsync(EditBannerCommand command)
    {
        var banner = await context.Banners.AsTracking().FirstOrDefaultAsync(x => x.Id == command.Id);
        if (banner is null)
            return OperationResult.NotFound("بنر مورد نظر یافت نشد.");

        if (string.IsNullOrWhiteSpace(command.ImageName))
            return OperationResult.Error("نام تصویر بنر را وارد کنید.");

        if (command.StartAt.HasValue && command.EndAt.HasValue && command.StartAt > command.EndAt)
            return OperationResult.Error("تاریخ شروع نباید از تاریخ پایان بزرگ‌تر باشد.");

        banner.Title = command.Title;
        banner.Subtitle = command.Subtitle;
        banner.ImageName = command.ImageName.Trim();
        banner.MobileImageName = command.MobileImageName;
        banner.ImageAlt = command.ImageAlt;
        banner.ButtonText = command.ButtonText;
        banner.LinkUrl = command.LinkUrl;
        banner.DisplayOrder = command.DisplayOrder;
        banner.StartAt = command.StartAt;
        banner.EndAt = command.EndAt;
        banner.IsActive = command.IsActive;

        await context.SaveChangesAsync();

        return OperationResult.Success("تغییرات بنر با موفقیت اعمال شد.");
    }

    public async Task<OperationResult> DeleteBannerAsync(Guid id)
    {
        var banner = await context.Banners.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (banner is null)
            return OperationResult.NotFound("بنر مورد نظر یافت نشد.");

        context.Banners.Remove(banner);
        await context.SaveChangesAsync();

        return OperationResult.Success("بنر با موفقیت حذف شد.");
    }

    public List<BannerDto> GetAllBannersAsync()
    {
        return context.Banners
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.CreatedAt)
            .Select(x => new BannerDto()
            {
                Id = x.Id,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                IsDeleted = x.IsDeleted,
                DeletedAt = x.DeletedAt,
                Title = x.Title,
                Subtitle = x.Subtitle,
                ImageName = x.ImageName,
                MobileImageName = x.MobileImageName,
                Position = x.Position,
                ImageAlt = x.ImageAlt,
                ButtonText = x.ButtonText,
                LinkUrl = x.LinkUrl,
                DisplayOrder = x.DisplayOrder,
                StartAt = x.StartAt,
                EndAt = x.EndAt,
                IsActive = x.IsActive,
            })
            .ToList();
    }

    public async Task<List<BannerDto>> GetActiveBannersAsync()
    {
        var now = DateTimeOffset.UtcNow;

        return await context.Banners
            .Where(x => x.IsActive && (!x.StartAt.HasValue || x.StartAt <= now) && (!x.EndAt.HasValue || x.EndAt >= now))
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.CreatedAt)
            .Select(x => new BannerDto
            {
                Id = x.Id,
                CreatedAt = x.CreatedAt,
                UpdatedAt = x.UpdatedAt,
                IsDeleted = x.IsDeleted,
                DeletedAt = x.DeletedAt,
                Title = x.Title,
                Subtitle = x.Subtitle,
                ImageName = x.ImageName,
                MobileImageName = x.MobileImageName,
                Position = x.Position,
                ImageAlt = x.ImageAlt,
                ButtonText = x.ButtonText,
                LinkUrl = x.LinkUrl,
                DisplayOrder = x.DisplayOrder,
                StartAt = x.StartAt,
                EndAt = x.EndAt,
                IsActive = x.IsActive,
            })
            .ToListAsync();
    }

    public async Task<BannerDto?> GetBannerByIdAsync(Guid id)
    {
        var banner = await context.Banners.FirstOrDefaultAsync(x => x.Id == id);
        if (banner is null)
            return null;

        return new BannerDto
        {
            Id = banner.Id,
            CreatedAt = banner.CreatedAt,
            UpdatedAt = banner.UpdatedAt,
            IsDeleted = banner.IsDeleted,
            DeletedAt = banner.DeletedAt,
            Title = banner.Title,
            Subtitle = banner.Subtitle,
            ImageName = banner.ImageName,
            MobileImageName = banner.MobileImageName,
            Position = banner.Position,
            ImageAlt = banner.ImageAlt,
            ButtonText = banner.ButtonText,
            LinkUrl = banner.LinkUrl,
            DisplayOrder = banner.DisplayOrder,
            StartAt = banner.StartAt,
            EndAt = banner.EndAt,
            IsActive = banner.IsActive,
        };
    }
}
