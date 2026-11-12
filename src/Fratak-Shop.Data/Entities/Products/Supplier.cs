using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Products;

/// <summary>Store / website the shop buys products from. Internal data, never shown on the storefront.</summary>
public class Supplier : BaseEntity
{
    [Required]
    [MaxLength(150)]
    public string Name { get; set; } = null!;

    [MaxLength(500)]
    public string? Website { get; set; }

    [MaxLength(150)]
    public string? ContactName { get; set; }

    [MaxLength(30)]
    public string? Phone { get; set; }

    [MaxLength(150)]
    public string? Email { get; set; }

    [MaxLength(1000)]
    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>
    /// روش خواندن خودکار موجودی و قیمت از سایت این تأمین‌کننده (مرحلهٔ هفتم – فاز ۵).
    /// برای تأمین‌کنندهٔ تازه با سایت متفاوت کافی است یک آداپتور در Core/Services/Suppliers اضافه شود.
    /// </summary>
    public SupplierSyncMode SyncMode { get; set; } = SupplierSyncMode.Auto;

    public int DisplayOrder { get; set; }

    public List<Product> Products { get; set; } = [];
}

/// <summary>روش خواندن اطلاعات کالا از سایت تأمین‌کننده.</summary>
public enum SupplierSyncMode
{
    /// <summary>تشخیص خودکار: اول API عمومی فروشگاه ووکامرس، اگر نبود داده‌های ساختاریافتهٔ صفحهٔ محصول (JSON-LD).</summary>
    Auto = 0,

    /// <summary>فقط API عمومی فروشگاه ووکامرس (Store API) – دقیق‌ترین و سبک‌ترین روش (مثل پخش عبدی).</summary>
    WooCommerce = 1,

    /// <summary>خواندن صفحهٔ محصول و داده‌های ساختاریافتهٔ آن (schema.org Product) – برای اکثر فروشگاه‌های دیگر.</summary>
    StructuredData = 2,

    /// <summary>این تأمین‌کننده بررسی خودکار نشود.</summary>
    Disabled = 3,
}
