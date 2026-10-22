using Microsoft.AspNetCore.Mvc.RazorPages;
using FratakShop.Models;
using System.Collections.Generic;

namespace FratakShop.Pages;

public class ProfileMessagesModel : PageModel
{
    public List<ProfileMenuItem> SidebarItems { get; set; } = SampleData.ProfileSidebarProfileMessages;
    public void OnGet()
    {
    }
}
