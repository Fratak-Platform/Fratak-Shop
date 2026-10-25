using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Brands;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Products;

/// <summary>
/// فرم ابزار تغییر گروهی قیمت. همهٔ فیلدها در هر مرحله (پیش‌نمایش ← اعمال) دوباره ارسال می‌شوند تا
/// چیزی در سرور نگه داشته نشود؛ <see cref="Token"/> اثر انگشت پیش‌نمایش است.
/// </summary>
public sealed class BulkPriceForm
{
    /// <summary>«filter» = با فیلتر، «selected» = محصولات انتخاب‌شده در فهرست.</summary>
    public string ScopeKind { get; set; } = "filter";
    public List<Guid> Ids { get; set; } = [];
    public Guid? CategoryId { get; set; }
    public Guid? BrandId { get; set; }
    public Guid? SupplierId { get; set; }
    public string? Search { get; set; }
    public string? Stock { get; set; }
    public string? State { get; set; } = "active";

    public BulkPriceTarget Target { get; set; } = BulkPriceTarget.Price;
    public BulkPriceMode Mode { get; set; } = BulkPriceMode.Percent;

    /// <summary>برای حالت‌های درصدی (درصد تغییر، درصد سود، درصد حراج).</summary>
    public decimal? Percent { get; set; }

    /// <summary>برای حالت‌های مبلغی (تومان).</summary>
    public decimal? Amount { get; set; }

    public int RoundStep { get; set; } = 1000;
    public BulkPriceRoundDirection RoundDirection { get; set; } = BulkPriceRoundDirection.Nearest;
    public bool ScaleDiscount { get; set; } = true;
    public bool SkipLoss { get; set; } = true;

    /// <summary>شروع/پایان حراج زمان‌دار؛ قالب «1404/08/10 18:30» (ساعت اختیاری).</summary>
    public string? DiscountStart { get; set; }
    public string? DiscountEnd { get; set; }

    public string? Token { get; set; }
}

/// <summary>
/// صفحهٔ «تغییر گروهی قیمت» (مرحلهٔ هفتم – فاز ۵): محدوده + عملیات ← پیش‌نمایش اثر ← اعمال ← تاریخچه و برگرداندن.
/// منطق محاسبه در Core/Services/Products/BulkPriceService.cs است.
/// <para>دسترسی: «مدیریت محصولات». عملیات روی «قیمت خرید» دسترسی «اطلاعات تأمین‌کننده و قیمت خرید» هم می‌خواهد.</para>
/// </summary>
[AdminPermission(AdminPermissions.ProductsManage)]
public class BulkPriceModel(IBulkPriceService prices, IAdminDataService data) : AdminPageModel
{
    [BindProperty] public BulkPriceForm Form { get; set; } = new();

    public BulkPricePreview? Preview { get; private set; }
    public List<PriceBatchRow> Batches { get; private set; } = [];
    public List<(Guid Id, string Name)> Categories { get; private set; } = [];
    public List<(Guid Id, string Name)> Brands { get; private set; } = [];
    public List<(Guid Id, string Name)> Suppliers { get; private set; } = [];

    /// <summary>آیا کاربر قیمت خرید و سود را می‌بیند؟</summary>
    public bool CanSeeCost => Can(AdminPermissions.ProductsSupplier);

    public async Task OnGetAsync() => await LoadAsync();

    /// <summary>ورود از فهرست محصولات با محصولات انتخاب‌شده (دکمهٔ «تغییر قیمت با پیش‌نمایش» در نوار گروهی).</summary>
    public async Task<IActionResult> OnPostSelectAsync(List<Guid> ids)
    {
        ModelState.Clear();
        Form = new BulkPriceForm { ScopeKind = "selected", Ids = ids.Distinct().Take(BulkPriceRules.MaxProducts).ToList(), State = null };
        if (Form.Ids.Count == 0)
        {
            Form.ScopeKind = "filter";
            ErrorMessage = "هیچ محصولی انتخاب نشده بود؛ محدوده را با فیلتر مشخص کنید.";
        }
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostPreviewAsync()
    {
        if (BuildOperation(out var operation) is { } error)
            return await FailAsync(error);

        var result = await prices.PreviewAsync(Scope(), operation, HttpContext.RequestAborted);
        if (!result.IsSuccess())
            return await FailAsync(result.Message ?? "پیش‌نمایش ساخته نشد.");

        Preview = result.Data;
        Form.Token = Preview!.Token;
        ModelState.Remove("Form.Token");
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostApplyAsync()
    {
        if (BuildOperation(out var operation) is { } error)
            return await FailAsync(error);
        if (string.IsNullOrWhiteSpace(Form.Token))
            return await FailAsync("ابتدا پیش‌نمایش را ببینید.");

        var result = await prices.ApplyAsync(Scope(), operation, Form.Token, ActorId, ActorName);
        if (result.Code == "stale")
        {
            // دادهٔ تازه را دوباره نشان می‌دهیم تا کارمند دوباره تأیید کند.
            ErrorMessage = result.Message;
            return await OnPostPreviewAsync();
        }
        if (!result.IsSuccess())
            return await FailAsync(result.Message ?? "اعمال انجام نشد.");

        SuccessMessage = result.Message;
        return Redirect("/admin/products/bulk-price#history");
    }

    public async Task<IActionResult> OnPostRevertAsync(Guid batchId)
    {
        ShowResult(await prices.RevertAsync(batchId, ActorId, ActorName));
        return Redirect("/admin/products/bulk-price#history");
    }

    /// <summary>نمایش دوبارهٔ فرم با پیام خطا (بدون از دست رفتن انتخاب‌ها).</summary>
    private async Task<IActionResult> FailAsync(string message)
    {
        ErrorMessage = message;
        Preview = null;
        await LoadAsync();
        return Page();
    }

    /// <summary>ساخت عملیات از فرم؛ خطای فرم یا null.</summary>
    private string? BuildOperation(out BulkPriceOperation operation)
    {
        var f = Form;
        operation = null!;
        if (f.Target == BulkPriceTarget.PurchasePrice && !CanSeeCost)
            return "تغییر قیمت خرید دسترسی «اطلاعات تأمین‌کننده و قیمت خرید» می‌خواهد.";
        if (f.Target == BulkPriceTarget.Price && f.Mode == BulkPriceMode.MarginOverPurchase && !CanSeeCost)
            return "قیمت‌گذاری بر اساس سود دسترسی «اطلاعات تأمین‌کننده و قیمت خرید» می‌خواهد.";

        var percentMode = f.Mode is BulkPriceMode.Percent or BulkPriceMode.MarginOverPurchase or BulkPriceMode.PercentOff;
        decimal value = 0;
        if (f.Target != BulkPriceTarget.RemoveDiscount)
        {
            var raw = percentMode ? f.Percent : f.Amount;
            if (raw is null)
                return percentMode ? "درصد را وارد کنید." : "مبلغ را وارد کنید.";
            value = raw.Value;
        }

        DateTimeOffset? start = null, end = null;
        if (f.Target == BulkPriceTarget.DiscountPrice)
        {
            start = PersianDateInput.ParseDateTime(f.DiscountStart);
            end = PersianDateInput.ParseDateTime(f.DiscountEnd, new TimeOnly(23, 59, 59));
            if ((!string.IsNullOrWhiteSpace(f.DiscountStart) && start is null) || (!string.IsNullOrWhiteSpace(f.DiscountEnd) && end is null))
                return "زمان شروع/پایان حراج را به شکل ۱۴۰۴/۰۸/۱۰ یا ۱۴۰۴/۰۸/۱۰ ۱۸:۳۰ وارد کنید.";
            if (end is { } e && e <= DateTimeOffset.UtcNow)
                return "پایان حراج گذشته است.";
        }

        if (f.ScopeKind == "selected" && f.Ids.Count == 0)
            return "هیچ محصولی انتخاب نشده است.";

        operation = new BulkPriceOperation(f.Target, f.Mode, value, f.RoundStep, f.RoundDirection, f.ScaleDiscount, f.SkipLoss, start, end);
        return BulkPriceRules.Validate(operation);
    }

    private BulkPriceScope Scope() => Form.ScopeKind == "selected"
        ? new BulkPriceScope(ProductIds: Form.Ids)
        : new BulkPriceScope(null, Form.CategoryId, Form.BrandId, CanSeeCost ? Form.SupplierId : null, Form.Search, Form.Stock, Form.State);

    private async Task LoadAsync()
    {
        Batches = await prices.ListBatchesAsync();
        Categories = (await data.Query<Category>().OrderBy(c => c.ParentId != null).ThenBy(c => c.Name)
                .Select(c => new { c.Id, c.Name, Parent = c.ParentCategory != null ? c.ParentCategory.Name : null }).ToListAsync())
            .Select(c => (c.Id, c.Parent is null ? c.Name : $"{c.Parent} › {c.Name}")).ToList();
        Brands = (await data.Query<Brand>().OrderBy(b => b.Name).Select(b => new { b.Id, b.Name }).ToListAsync())
            .Select(b => (b.Id, b.Name)).ToList();
        if (CanSeeCost)
            Suppliers = (await data.Query<Supplier>().OrderBy(s => s.Name).Select(s => new { s.Id, s.Name }).ToListAsync())
                .Select(s => (s.Id, s.Name)).ToList();
    }

    // ── کمک‌های نمایش (در Razor عبارت switch مجاز نیست) ─────────────────────

    /// <summary>کلاس رنگ اختلاف: افزایش سبز، کاهش قرمز.</summary>
    public static string DeltaCss(decimal delta) => delta > 0 ? "text-emerald-600" : delta < 0 ? "text-rose-600" : "a-muted";

    /// <summary>متن درصد سود؛ مثال: «۲۵٪».</summary>
    public static string MarginText(decimal? margin) => margin is { } m ? m.ToString("0.#", global::System.Globalization.CultureInfo.InvariantCulture) + "٪" : "—";

    public static string SourceTitle(PriceChangeSource source) => source == PriceChangeSource.Import ? "ورود از فایل" : "ابزار گروهی";
}
