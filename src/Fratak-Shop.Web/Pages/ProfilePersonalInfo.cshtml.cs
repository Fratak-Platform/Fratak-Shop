using Microsoft.AspNetCore.Mvc.RazorPages;
using FratakShop.Models;
using System.Collections.Generic;

namespace FratakShop.Pages;

public class ProfilePersonalInfoModel : PageModel
{
    public List<ProfileMenuItem> SidebarItems { get; set; } = SampleData.ProfileSidebarProfilePersonalInfo;
    public void OnGet()
    {
    }
}
