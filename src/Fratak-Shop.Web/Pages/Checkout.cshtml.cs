using Microsoft.AspNetCore.Mvc.RazorPages;
using FratakShop.Models;
using System.Collections.Generic;

namespace FratakShop.Pages;

public class CheckoutModel : PageModel
{
    public List<SelectOption> CityOptions { get; set; } = SampleData.CityOptions;
    public List<DeliveryDay> DeliveryDays { get; set; } = SampleData.DeliveryDays;
    public List<PaymentMethod> PaymentMethods { get; set; } = SampleData.PaymentMethods;
    public List<SelectOption> ProvinceOptions { get; set; } = SampleData.ProvinceOptions;
    public List<ShippingMethod> ShippingMethods { get; set; } = SampleData.ShippingMethods;
    public void OnGet()
    {
    }
}
