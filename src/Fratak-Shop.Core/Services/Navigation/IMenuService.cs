using Fratak_Shop.Core.DTOs.Navigation.Command;
using Fratak_Shop.Core.DTOs.Navigation.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Navigation;

namespace Fratak_Shop.Core.Services.Navigation;

/// <summary>
/// Site navigation of the storefront. Header, footer columns, profile sidebar and
/// quick-access links are all rows of the menu table, so nothing in the layout is
/// hard-coded and the admin can hide/show or reorder every item.
/// </summary>
public interface IMenuService
{
    // -------------------------------------------------------------- storefront
    Task<List<MenuItemDto>> GetByLocationAsync(MenuLocation location, CancellationToken cancellationToken = default);

    Task<List<MenuItemDto>> GetHeaderAsync(CancellationToken cancellationToken = default);

    Task<List<MenuItemDto>> GetFooterColumnsAsync(CancellationToken cancellationToken = default);

    Task<List<MenuItemDto>> GetFooterBottomAsync(CancellationToken cancellationToken = default);

    Task<List<MenuItemDto>> GetProfileMenuAsync(CancellationToken cancellationToken = default);

    Task<List<MenuItemDto>> GetQuickAccessAsync(CancellationToken cancellationToken = default);

    Task<List<SocialLinkDto>> GetSocialLinksAsync(bool? inHeader = null, bool? inFooter = null);

    // ------------------------------------------------------------------- admin
    Task<List<MenuItemDto>> GetAllAsync();

    Task<MenuItemDto?> GetAsync(Guid id);

    Task<OperationResult> SaveAsync(SaveMenuItemCommand command);

    Task<OperationResult> DeleteAsync(Guid id);

    Task<OperationResult> ToggleActiveAsync(Guid id);

    Task<OperationResult> ReorderAsync(Guid id, int displayOrder);

    Task<List<SocialLinkDto>> GetAllSocialLinksAsync();

    Task<OperationResult> SaveSocialLinkAsync(SaveSocialLinkCommand command);

    Task<OperationResult> DeleteSocialLinkAsync(Guid id);

    Task<OperationResult> ToggleSocialLinkAsync(Guid id);

    /// <summary>ساخت منوهای پیش‌فرض در اولین اجرا تا قالب سایت خالی نباشد.</summary>
    Task EnsureDefaultsAsync(CancellationToken cancellationToken = default);
}
