using System.Security.Cryptography;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Products;

/// <summary>
/// صفحهٔ «ورود از اکسل» (مرحلهٔ هفتم – فاز ۵): به‌روزرسانی گروهی قیمت، حراج، موجودی، قیمت خرید و وضعیت
/// محصولات موجود با فایل xlsx/CSV.
/// <para>
/// روند: بارگذاری ← پیش‌نمایش (ردیف‌های خوانده‌شده تا ۳۰ دقیقه در حافظهٔ سرور و فقط برای همین کاربر نگه داشته می‌شوند)
/// ← تأیید. منطق در Core/Services/Products/ProductImportService.cs است.
/// </para>
/// </summary>
[AdminPermission(AdminPermissions.ProductsManage)]
public class ImportModel(IProductImportService importer, IAdminDataService data, IMemoryCache cache) : AdminPageModel
{
    /// <summary>بیشترین حجم فایل (۵ مگابایت).</summary>
    public const long MaxFileBytes = 5 * 1024 * 1024;

    /// <summary>داده‌ای که بین پیش‌نمایش و تأیید در حافظه می‌ماند.</summary>
    private sealed record PendingImport(Guid? UserId, string FileName, List<ProductImportLine> Lines);

    public ProductImportPreview? Preview { get; private set; }
    public ProductImportParse? Parsed { get; private set; }
    public string? Token { get; private set; }
    public string? FileName { get; private set; }
    public List<string> Notes { get; } = [];

    public bool CanSeeCost => Can(AdminPermissions.ProductsSupplier);

    public void OnGet()
    {
    }

    /// <summary>
    /// فایل نمونه: همهٔ محصولات با ستون‌های قابل ورود (همان قالبی که دوباره بارگذاری می‌شود).
    /// </summary>
    public async Task<IActionResult> OnGetTemplateAsync(string format = "xlsx")
    {
        var withCost = CanSeeCost;
        var rows = await data.Query<Product>().OrderBy(p => p.Name).Take(20_000)
            .Select(p => new { p.Name, p.SKU, p.Price, p.DiscountPrice, p.StockQuantity, p.PurchasePrice, p.IsActive })
            .ToListAsync();
        var columns = new List<string> { "کد کالا", "نام", "قیمت", "قیمت با تخفیف", "موجودی", "وضعیت" };
        if (withCost)
            columns.Add("قیمت خرید");
        var table = new ExportTable("ورود محصولات", [.. columns]);
        foreach (var p in rows)
        {
            var cells = new List<object?> { p.SKU, p.Name, p.Price, p.DiscountPrice, p.StockQuantity, p.IsActive ? "فعال" : "غیرفعال" };
            if (withCost)
                cells.Add(p.PurchasePrice);
            table.Add([.. cells]);
        }
        return Exporter.File(table, format == "csv" ? "csv" : "xlsx", "products-import");
    }

    public async Task<IActionResult> OnPostUploadAsync(IFormFile? file)
    {
        if (file is null || file.Length == 0)
            return Fail("فایلی انتخاب نشده است.");
        if (file.Length > MaxFileBytes)
            return Fail("حجم فایل بیشتر از ۵ مگابایت است.");
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (extension is not (".xlsx" or ".csv"))
            return Fail("فقط فایل اکسل (xlsx) یا CSV پذیرفته می‌شود. فایل xls قدیمی را در اکسل با «Save As ← xlsx» ذخیره کنید.");

        List<string[]> table;
        try
        {
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream, HttpContext.RequestAborted);
            stream.Position = 0;
            table = SpreadsheetReader.Read(stream, file.FileName);
        }
        catch (Exception exception) when (exception is InvalidDataException or global::System.Xml.XmlException or IOException)
        {
            return Fail(exception is InvalidDataException ? exception.Message : "فایل خوانده نشد؛ مطمئن شوید فایل سالم و با قالب درست است.");
        }

        Parsed = ProductImportRules.Parse(table);
        var lines = Parsed.Lines.ToList();
        if (!CanSeeCost && lines.Any(l => l.PurchasePrice is not null))
        {
            // کسی که قیمت خرید را نمی‌بیند نباید بتواند آن را عوض کند.
            lines = lines.Select(l => l with { PurchasePrice = null }).ToList();
            Notes.Add("ستون «قیمت خرید» نادیده گرفته شد (دسترسی «اطلاعات تأمین‌کننده و قیمت خرید» ندارید).");
        }

        FileName = Path.GetFileName(file.FileName);
        if (lines.Count == 0)
        {
            ErrorMessage = Parsed.Errors.FirstOrDefault() ?? "ردیف قابل استفاده‌ای در فایل نبود.";
            return Page();
        }

        Preview = await importer.PreviewAsync(lines, HttpContext.RequestAborted);
        Token = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        cache.Set(CacheKey(Token), new PendingImport(ActorId, FileName, lines), TimeSpan.FromMinutes(30));
        return Page();
    }

    public async Task<IActionResult> OnPostApplyAsync(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 40 || !cache.TryGetValue(CacheKey(token), out PendingImport? pending)
            || pending is null || pending.UserId != ActorId)
        {
            ErrorMessage = "مهلت پیش‌نمایش تمام شده است؛ فایل را دوباره بارگذاری کنید.";
            return Redirect("/admin/products/import");
        }

        cache.Remove(CacheKey(token));
        ShowResult(await importer.ApplyAsync(pending.Lines, pending.FileName, ActorId, ActorName));
        return Redirect("/admin/products/import");
    }

    /// <summary>عنوان فارسی کلید ستون شناسایی‌شده (sku → «کد کالا»).</summary>
    public static string ColumnTitle(string key) => key switch
    {
        "sku" => "کد کالا",
        "price" => "قیمت",
        "discount" => "قیمت با تخفیف",
        "stock" => "موجودی",
        "purchase" => "قیمت خرید",
        "active" => "وضعیت",
        _ => key,
    };

    private IActionResult Fail(string message)
    {
        ErrorMessage = message;
        return Page();
    }

    /// <summary>کلید حافظه به کاربر گره خورده تا توکن کاربر دیگری قابل استفاده نباشد.</summary>
    private string CacheKey(string token) => $"admin-import:{ActorId}:{token}";
}
