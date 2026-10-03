using Fratak_Shop.Core.DTOs.Categories.Query;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.Mappers.Seo;
using Fratak_Shop.Core.Services.Categories;
using Fratak_Shop.Core.Services.Media;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Data.Utilities.SeoManagement;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Products;

[Authorize(Policy = PolicyNames.Products)]
public class EditModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    IProductService products,
    ICategoryService categories,
    IMediaService media)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public List<CategoryDto> Categories { get; private set; } = [];

    public bool IsNew => Id is null;

    [BindProperty(SupportsGet = true)]
    public Guid? Id { get; set; }

    [BindProperty]
    public ProductInput Input { get; set; } = new();

    [BindProperty]
    public SeoInput Seo { get; set; } = new();

    [BindProperty]
    public IFormFile? PrimaryImage { get; set; }

    /// <summary>
    /// نقش‌هایی که اجازه تغییر داده‌های تجاری (قیمت، موجودی، وضعیت) را دارند.
    /// کارشناس سئو فقط فیلدهای محتوایی و سئو را ویرایش می‌کند.
    /// </summary>
    public bool CanEditCommerce => IsSuperAdmin || HasRole(UserRole.Admin) || HasRole(UserRole.ProductManager);

    public async Task<IActionResult> OnGetAsync()
    {
        ViewData["Title"] = IsNew ? "افزودن محصول" : "ویرایش محصول";
        Categories = await categories.GetFlatAsync();

        if (IsNew)
            return Page();

        var product = await products.GetForEditAsync(Id!.Value);
        if (product is null)
            return NotFound();

        Input = new ProductInput
        {
            Name = product.Name,
            Slug = product.Slug,
            SKU = product.SKU,
            CategoryId = product.CategoryId,
            ShortDescription = product.ShortDescription,
            Description = product.Description,
            Price = product.Price,
            DiscountPrice = product.DiscountPrice,
            DiscountStartAt = product.DiscountStartAt,
            DiscountEndAt = product.DiscountEndAt,
            StockQuantity = product.StockQuantity,
            WarrantyTitle = product.WarrantyTitle,
            WarrantyMonths = product.WarrantyMonths,
            IsActive = product.IsActive,
            IsFeatured = product.IsFeatured,
            PublishedAt = product.PublishedAt,
            ImageName = product.Images.FirstOrDefault(image => image.IsPrimary)?.ImageName ?? product.Images.FirstOrDefault()?.ImageName,
            ImageAlt = product.Images.FirstOrDefault(image => image.IsPrimary)?.AltText,
            Features = product.Features.Select(feature => new FeatureInput
            {
                Key = feature.Key,
                Value = feature.Value,
                ValueType = feature.ValueType,
            }).ToList(),
        };

        if (Input.Features.Count == 0)
            Input.Features.Add(new FeatureInput());

        Seo = SeoInput.FromEntity(product.Seo);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        ViewData["Title"] = IsNew ? "افزودن محصول" : "ویرایش محصول";
        Categories = await categories.GetFlatAsync();

        // Roles without commercial rights (the SEO specialist) keep the current
        // price/stock/visibility values; only the content and the SEO are replaced.
        // Their fields are rendered disabled, so their validation entries are dropped.
        if (!CanEditCommerce)
        {
            var current = Id.HasValue ? await products.GetForEditAsync(Id.Value) : null;

            if (current is not null)
            {
                Input.SKU = current.SKU;
                Input.Price = current.Price;
                Input.DiscountPrice = current.DiscountPrice;
                Input.DiscountStartAt = current.DiscountStartAt;
                Input.DiscountEndAt = current.DiscountEndAt;
                Input.StockQuantity = current.StockQuantity;
                Input.IsActive = current.IsActive;
                Input.IsFeatured = current.IsFeatured;
                Input.ImageName ??= current.Images.FirstOrDefault(image => image.IsPrimary)?.ImageName;
            }

            foreach (var key in new[]
                     {
                         "Input.SKU", "Input.Price", "Input.DiscountPrice", "Input.DiscountStartAt",
                         "Input.DiscountEndAt", "Input.StockQuantity", "Input.IsActive", "Input.IsFeatured",
                     })
            {
                ModelState.Remove(key);
            }
        }

        if (!ModelState.IsValid)
            return Page();

        var imageName = Input.ImageName;
        if (PrimaryImage is { Length: > 0 })
        {
            var saved = await media.SaveImageAsync(PrimaryImage, MediaFolders.Products);
            if (!saved.IsSuccess())
            {
                ModelState.AddModelError(string.Empty, saved.Message);
                return Page();
            }

            imageName = saved.Data;
        }

        var productSeo = Seo.ToEntity();

        var command = new EditProductCommand
        {
            Id = Id ?? Guid.Empty,
            CategoryId = Input.CategoryId,
            Name = Input.Name,
            Slug = string.IsNullOrWhiteSpace(Input.Slug) ? SlugHelper.Generate(Input.Name) : Input.Slug,
            SKU = Input.SKU,
            ShortDescription = Input.ShortDescription,
            Description = Input.Description,
            Price = Input.Price,
            DiscountPrice = Input.DiscountPrice,
            DiscountStartAt = Input.DiscountStartAt,
            DiscountEndAt = Input.DiscountEndAt,
            StockQuantity = Input.StockQuantity,
            WarrantyTitle = Input.WarrantyTitle,
            WarrantyMonths = Input.WarrantyMonths,
            IsActive = Input.IsActive,
            IsFeatured = Input.IsFeatured,
            PublishedAt = Input.PublishedAt,
            Seo = productSeo,
            Images = string.IsNullOrWhiteSpace(imageName)
                ? []
                : [new ProductImageCommand { ImageName = imageName!, AltText = string.IsNullOrWhiteSpace(Seo.ImageAlt) ? Input.Name : Seo.ImageAlt, IsPrimary = true, DisplayOrder = 1 }],
            Features = Input.Features
                .Where(feature => !string.IsNullOrWhiteSpace(feature.Key) && !string.IsNullOrWhiteSpace(feature.Value))
                .Select((feature, index) => new ProductFeatureCommand
                {
                    Key = feature.Key!,
                    Value = feature.Value!,
                    ValueType = feature.ValueType,
                    DisplayOrder = index + 1,
                })
                .ToList(),
        };

        var result = IsNew
            ? await products.CreateProductAsync(command)
            : await products.UpdateProductAsync(command);

        if (!result.IsSuccess())
        {
            ModelState.AddModelError(string.Empty, result.Message);
            return Page();
        }

        Notify(IsNew ? "محصول جدید ثبت شد." : "تغییرات محصول ذخیره شد.");
        return RedirectToPage("/Products/Index", new { area = "Admin" });
    }

    /// <summary>«سئوی هوشمند»: تحلیل + تولید پیشنهاد، بدون ذخیره خودکار.</summary>
    public async Task<IActionResult> OnPostSmartSeoAsync()
    {
        if (Id is null)
            return new JsonResult(new { success = false, message = "برای اجرای سئوی هوشمند ابتدا محصول را ذخیره کنید." });

        var result = await seoAudits.GenerateAsync(SeoEntityType.Product, Id.Value, CurrentUserId);
        if (!result.IsSuccess() || result.Data is null)
            return new JsonResult(new { success = false, message = result.Message });

        var proposal = result.Data.Proposal;
        return new JsonResult(new
        {
            success = true,
            message = $"تحلیل انجام شد؛ امتیاز فعلی {result.Data.Score} از ۱۰۰.",
            recordId = result.Data.Id,
            score = result.Data.Score,
            proposal,
            findings = result.Data.Findings.Select(finding => new
            {
                title = finding.Title,
                description = finding.Description,
                severityClass = Ui.SeverityClass(finding.Severity),
                severityTitle = finding.Severity switch
                {
                    SeoFindingSeverity.Critical => "بحرانی",
                    SeoFindingSeverity.Warning => "هشدار",
                    SeoFindingSeverity.Success => "بررسی‌شده",
                    _ => "اطلاع",
                },
            }),
        });
    }

    /// <summary>«هوشمندسازی نوشته‌ها»: پیش‌نویس بهتر متن توضیحات و ویژگی‌ها.</summary>
    public async Task<IActionResult> OnPostEnhanceAsync()
    {
        if (Id is null)
            return new JsonResult(new { success = false, message = "ابتدا محصول را ذخیره کنید." });

        var result = await seoAudits.EnhanceAsync(SeoEntityType.Product, Id.Value);
        if (!result.IsSuccess() || result.Data is null)
            return new JsonResult(new { success = false, message = result.Message });

        return new JsonResult(new
        {
            success = true,
            message = "پیش‌نویس نوشته آماده شد؛ پس از بازبینی ذخیره کنید.",
            shortDescription = result.Data.EnhancedShortDescription,
            content = result.Data.EnhancedContent,
            summary = result.Data.Summary,
        });
    }

    public class ProductInput
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "نام محصول را وارد کنید.")]
        public string Name { get; set; } = null!;

        public string? Slug { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "کد SKU را وارد کنید.")]
        public string SKU { get; set; } = null!;

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "دسته‌بندی را انتخاب کنید.")]
        public Guid CategoryId { get; set; }

        public string? ShortDescription { get; set; }
        public string? Description { get; set; }

        [System.ComponentModel.DataAnnotations.Range(1, double.MaxValue, ErrorMessage = "قیمت باید بزرگ‌تر از صفر باشد.")]
        public decimal Price { get; set; }

        public decimal? DiscountPrice { get; set; }
        public DateTimeOffset? DiscountStartAt { get; set; }
        public DateTimeOffset? DiscountEndAt { get; set; }
        public int StockQuantity { get; set; }
        public string? WarrantyTitle { get; set; }
        public int? WarrantyMonths { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsFeatured { get; set; }
        public DateTimeOffset? PublishedAt { get; set; }
        public string? ImageName { get; set; }
        public string? ImageAlt { get; set; }
        public List<FeatureInput> Features { get; set; } = [new()];
    }

    public class FeatureInput
    {
        public string? Key { get; set; }
        public string? Value { get; set; }
        public FeatureValueType ValueType { get; set; } = FeatureValueType.Text;
    }
}
