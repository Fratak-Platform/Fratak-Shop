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
}
