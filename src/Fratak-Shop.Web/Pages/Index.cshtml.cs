using Microsoft.AspNetCore.Mvc.RazorPages;
using FratakShop.Models;
using System.Collections.Generic;

namespace FratakShop.Pages;

public class IndexModel : PageModel
{
    public List<BlogCard> BlogCards { get; set; } = SampleData.HomeBlogCards;
    public List<CategoryItem> Categories { get; set; } = SampleData.Categories;
    public List<HeroSlide> HeroSlides { get; set; } = SampleData.HeroSlides;
    public List<DealProduct> OffProducts { get; set; } = SampleData.OffProducts;
    public List<BrandItem> PartnerBrands { get; set; } = SampleData.PartnerBrands;
    public List<ServiceItem> Services { get; set; } = SampleData.Services;
    public List<ProductCard> Slider1Products { get; set; } = SampleData.Slider1Products;
    public List<ProductCard> Slider2Products { get; set; } = SampleData.Slider2Products;
    public void OnGet()
    {
    }
}
