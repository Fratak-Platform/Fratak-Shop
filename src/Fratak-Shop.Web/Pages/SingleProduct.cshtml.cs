using Microsoft.AspNetCore.Mvc.RazorPages;
using FratakShop.Models;
using System.Collections.Generic;

namespace FratakShop.Pages;

public class SingleProductModel : PageModel
{
    public List<ActionButton> ActionButtons { get; set; } = SampleData.ActionButtons;
    public List<SpecRow> FeatureRows { get; set; } = SampleData.FeatureRows;
    public List<GalleryImage> GalleryImages { get; set; } = SampleData.GalleryImages;
    public List<SpecBox> SpecBoxes { get; set; } = SampleData.SpecBoxes;
    public void OnGet()
    {
    }
}
