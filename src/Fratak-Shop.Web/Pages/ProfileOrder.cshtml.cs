using Microsoft.AspNetCore.Mvc.RazorPages;
using FratakShop.Models;
using System.Collections.Generic;

namespace FratakShop.Pages;

public class ProfileOrderModel : PageModel
{
    public List<OrderRow> Orders { get; set; } = SampleData.Orders;
    public List<ProfileMenuItem> SidebarItems { get; set; } = SampleData.ProfileSidebarProfileOrder;
    public void OnGet()
    {
    }
}
