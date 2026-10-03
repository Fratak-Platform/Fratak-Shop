using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Core.DTOs.Users.Command;
using Fratak_Shop.Core.DTOs.Users.Query;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Data.Entities.Users;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Users;

/// <summary>
/// مدیریت اعضای پنل و تخصیص چند نقش هم‌زمان به هر کاربر
/// (مثلاً «مدیر محصولات» + «مدیر محتوا» برای یک نفر).
/// </summary>
[Authorize(Policy = PolicyNames.SuperAdminOnly)]
public class StaffModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    IUserService users)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public List<UserDto> Items { get; private set; } = [];

    public static IReadOnlyList<(UserRole Role, string Title)> Roles => UserRoleExtensions.AssignableRoles
        .Select(role => (role, role.GetTitle()))
        .ToList();

    [BindProperty(SupportsGet = true)]
    public Guid? Edit { get; set; }

    [BindProperty]
    public StaffInput Input { get; set; } = new();

    public async Task OnGetAsync()
    {
        ViewData["Title"] = "اعضای پنل و نقش‌ها";
        Items = await users.GetStaffAsync();

        if (Edit.HasValue)
        {
            var current = Items.FirstOrDefault(item => item.Id == Edit.Value);
            if (current is not null)
            {
                Input = new StaffInput
                {
                    Id = current.Id,
                    PhoneNumber = current.PhoneNumber,
                    Email = current.Email,
                    FirstName = current.FirstName,
                    LastName = current.LastName,
                    PrimaryRole = current.Role == UserRole.SuperAdmin ? UserRole.Admin : current.Role,
                    SelectedRoles = current.Roles.Where(role => role != UserRole.SuperAdmin && role != UserRole.None).ToList(),
                    IsActive = current.IsActive,
                };
            }
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!ModelState.IsValid)
        {
            Notify("اطلاعات عضو پنل کامل نیست.", "error");
            return RedirectToPage("/Users/Staff", new { area = "Admin" });
        }

        var roles = Input.SelectedRoles.Where(role => role != UserRole.None).Distinct().ToList();
        if (roles.Count == 0)
        {
            Notify("حداقل یک نقش برای کاربر انتخاب کنید.", "error");
            return RedirectToPage("/Users/Staff", new { area = "Admin" });
        }

        if (!roles.Contains(Input.PrimaryRole))
            Input.PrimaryRole = roles[0];

        var command = new SaveStaffUserCommand
        {
            Id = Input.Id,
            PhoneNumber = SlugHelper.ToLatinDigits(Input.PhoneNumber).Trim(),
            Email = Input.Email,
            FirstName = Input.FirstName,
            LastName = Input.LastName,
            PrimaryRole = Input.PrimaryRole,
            Roles = roles,
            Password = Input.Password,
            IsActive = Input.IsActive,
            GrantedByUserId = CurrentUserId,
        };

        var result = Input.Id.HasValue
            ? await users.UpdateStaffAsync(command)
            : await users.CreateStaffAsync(command);

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Users/Staff", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id)
    {
        var user = await users.GetUserDtoAsync(id);
        if (user is null)
        {
            Notify("کاربر یافت نشد.", "error");
            return RedirectToPage("/Users/Staff", new { area = "Admin" });
        }

        var result = await users.SetActiveAsync(id, !user.IsActive);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Users/Staff", new { area = "Admin" });
    }

    public class StaffInput
    {
        public Guid? Id { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "شماره موبایل را وارد کنید.")]
        [System.ComponentModel.DataAnnotations.RegularExpression("^09\\d{9}$", ErrorMessage = "شماره موبایل معتبر نیست.")]
        public string PhoneNumber { get; set; } = null!;

        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public UserRole PrimaryRole { get; set; } = UserRole.Admin;
        public List<UserRole> SelectedRoles { get; set; } = [];

        [System.ComponentModel.DataAnnotations.MinLength(6, ErrorMessage = "رمز عبور باید حداقل ۶ کاراکتر باشد.")]
        public string? Password { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
