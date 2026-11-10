using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Core.DTOs.Banners.Command;
using Fratak_Shop.Core.DTOs.Banners.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Marketing;

public interface IBannerService
{
    Task<OperationResult> CreateBannerAsync(CreateBannerCommand command);
    Task<OperationResult> EditBannerAsync(EditBannerCommand command);
    Task<OperationResult> DeleteBannerAsync(Guid id);
    List<BannerDto> GetAllBannersAsync();
    /// <summary>بنرهای فعال در بازه زمانی نمایش؛ در صورت تعیین جایگاه، فقط همان جایگاه.</summary>
    Task<List<BannerDto>> GetActiveBannersAsync(BannerPosition? position = null);
    Task<BannerDto?> GetBannerByIdAsync(Guid id);

    /// <summary>دور ۲۳: سربرگ فعال یک دسته (۱۹۲۰×۳۰۰ + نسخهٔ موبایل) یا null.</summary>
    Task<BannerDto?> GetCategoryHeaderAsync(Guid categoryId);

    /// <summary>دور ۲۳: افزودن شمارش نمایش/کلیک جمع‌شده در حافظه.</summary>
    Task<int> AddStatsAsync(IReadOnlyDictionary<Guid, (int Views, int Clicks)> stats, CancellationToken cancellationToken = default);

    /// <summary>دور ۲۳: لینک امن مقصد بنر برای شمارش کلیک (یا null).</summary>
    Task<string?> GetClickTargetAsync(Guid id);
}
