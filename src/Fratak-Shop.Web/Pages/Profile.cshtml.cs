using Microsoft.AspNetCore.Mvc.RazorPages;
using FratakShop.Models;
using System.Collections.Generic;

namespace FratakShop.Pages;

public class ProfileModel : PageModel
{
    public List<OrderRow> RecentOrders { get; set; } = SampleData.RecentOrders;
    public List<ProfileMenuItem> SidebarItems { get; set; } = SampleData.ProfileSidebar;
    public void OnGet()
    {
    }
}
