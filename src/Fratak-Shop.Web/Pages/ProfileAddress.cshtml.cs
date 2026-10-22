using Microsoft.AspNetCore.Mvc.RazorPages;
using FratakShop.Models;
using System.Collections.Generic;

namespace FratakShop.Pages;

public class ProfileAddressModel : PageModel
{
    public List<ProfileMenuItem> SidebarItems { get; set; } = SampleData.ProfileSidebarProfileAddress;
    public void OnGet()
    {
    }
}
