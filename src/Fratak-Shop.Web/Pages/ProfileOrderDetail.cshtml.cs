using Microsoft.AspNetCore.Mvc.RazorPages;
using FratakShop.Models;
using System.Collections.Generic;

namespace FratakShop.Pages;

public class ProfileOrderDetailModel : PageModel
{
    public List<ProductAttribute> OrderItems1 { get; set; } = SampleData.OrderItems1;
    public List<ProductAttribute> OrderItems2 { get; set; } = SampleData.OrderItems2;
    public List<SummaryItem> ReceiptRows { get; set; } = SampleData.OrderDetailReceipt;
    public List<ProfileMenuItem> SidebarItems { get; set; } = SampleData.ProfileSidebarProfileOrderDetail;
    public void OnGet()
    {
    }
}
