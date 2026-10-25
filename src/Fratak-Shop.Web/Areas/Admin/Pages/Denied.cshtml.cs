using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Web.Infrastructure.Admin;

namespace Fratak_Shop.Web.Areas.Admin.Pages;

public class DeniedModel : AdminPageModel
{
    public string? NeedTitle { get; private set; }

    public void OnGet(string? need)
    {
        if (string.IsNullOrWhiteSpace(need))
            return;
        if (need == "owner")
        {
            NeedTitle = "مالک فروشگاه";
            return;
        }

        var titles = need.Split('|').Select(key => AdminPermissions.All.FirstOrDefault(p => p.Key == key)?.Title).Where(t => t is not null);
        NeedTitle = string.Join(" یا ", titles);
    }
}
