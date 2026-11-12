using Fratak_Shop.Web.Services;

namespace Fratak_Shop.Web.Infrastructure.Admin.Manage;

public enum MKind
{
    Text,
    TextArea,
    Html,
    Url,
    Slug,
    Color,
    Int,
    Decimal,
    Bool,
    Enum,
    Ref,
    Image,
    Date,
    Time,
    ReadOnly,
}

/// <summary>One field of a generic admin table (property of the entity).</summary>
public sealed record MField(string Name, string Label, MKind Kind = MKind.Text)
{
    public bool Required { get; init; }
    public bool InList { get; init; }
    public string? Help { get; init; }
    public bool Ltr { get; init; }
    public string? Placeholder { get; init; }

    /// <summary>Enum: Persian labels by member name (members without a label – except None – use the name).</summary>
    public IReadOnlyDictionary<string, string>? Labels { get; init; }

    /// <summary>Ref: the referenced entity and the property shown as its label.</summary>
    public Type? RefType { get; init; }
    public string RefLabel { get; init; } = "Name";

    /// <summary>Image: upload area; when <see cref="StoreAsPath"/> the full /uploads/… path is stored.</summary>
    public UploadArea? Area { get; init; }
    public bool StoreAsPath { get; init; }

    /// <summary>Slug: source field used when the slug is left empty.</summary>
    public string? SlugFrom { get; init; }

    /// <summary>Takes the full row of the form grid.</summary>
    public bool Wide { get; init; }
    public int Rows { get; init; } = 3;

    /// <summary>
    /// فیلد اعشاری که مبلغ (تومان) است؟ در فرم با جداکنندهٔ سه‌رقمی و «مبلغ به حروف» نمایش داده می‌شود (fa-inputs.js).
    /// تشخیص از روی نام: «…Amount»، «…Price»، «Cost»، «FreeShippingThreshold»، «AmountStep».
    /// فیلدهایی مثل «Value» کوپن که می‌تواند درصد هم باشد مبلغ حساب نمی‌شوند.
    /// </summary>
    /// <summary>
    /// قابل ویرایش درجا در خود جدول؟ (کلیک روی خانه ← ورودی کوچک ← Enter). فقط نوع‌های ساده؛
    /// متن بلند، HTML، تصویر، تاریخ و ارجاع از فرم کامل ویرایش می‌شوند. با <see cref="NoInline"/> می‌شود برای یک فیلد خاموشش کرد.
    /// </summary>
    public bool CanInline => !NoInline && Kind is MKind.Text or MKind.Int or MKind.Decimal or MKind.Bool or MKind.Enum or MKind.Slug or MKind.Url or MKind.Color;

    /// <summary>ویرایش درجا برای این فیلد خاموش باشد (مثلاً کدی که تغییرش باید با دقت و از فرم انجام شود).</summary>
    public bool NoInline { get; init; }

    public bool IsMoney => Kind == MKind.Decimal
        && (Name.EndsWith("Amount", StringComparison.Ordinal) || Name.EndsWith("Price", StringComparison.Ordinal)
            || Name is "Cost" or "FreeShippingThreshold" or "AmountStep");
}

/// <summary>Definition of a table managed by the generic admin engine (<c>/admin/manage/{key}</c>).</summary>
public sealed class MDef
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Singular { get; init; }
    public required string Group { get; init; }
    public required Type Entity { get; init; }
    public required string[] Permissions { get; init; }
    public required MField[] Fields { get; init; }
    public string? Description { get; init; }
    public string Icon { get; init; } = "Sliders";
    public string[] Search { get; init; } = [];
    public string Sort { get; init; } = "DisplayOrder";
    public bool SortDescending { get; init; }

    /// <summary>Foreign key that scopes the list (e.g. videos of a product: <c>?parent=</c>).</summary>
    public string? ParentField { get; init; }
    public bool HasSeo { get; init; }
    public bool CanCreate { get; init; } = true;
    public bool CanDelete { get; init; } = true;

    /// <summary>Public page of a row, e.g. <c>/page/{Slug}</c>.</summary>
    public string? PublicUrl { get; init; }

    /// <summary>
    /// دور ۲۴: بررسی/تکمیل اختصاصی بعد از خواندن همهٔ فیلدها (خروجی = پیام خطا یا null).
    /// مثال: نمادهای اعتماد ← برداشتن تصویر و لینک از «کد نماد».
    /// </summary>
    public Func<object, string?>? AfterBind { get; init; }

    /// <summary>Related tables shown as links on the edit page: (title, key, parent field of the child).</summary>
    public (string Title, string Key)[] Children { get; init; } = [];

    public MField? Field(string name) => Fields.FirstOrDefault(f => f.Name == name);
}

public sealed record ManageList(IReadOnlyList<object> Rows, int Total, int Page, int Take, IReadOnlyDictionary<string, Dictionary<Guid, string>> RefLabels)
{
    public int PageCount => Total == 0 ? 1 : (int)Math.Ceiling(Total / (double)Take);
}

/// <summary>
/// گزینه‌های فهرست یک جدول مدیریت داده (از آدرس صفحه): جست‌وجو، والد، صفحه، مرتب‌سازی، اندازهٔ صفحه و نمای سطل بازیافت.
/// </summary>
/// <example><c>/admin/manage/coupons?sort=Value&amp;dir=desc&amp;take=50</c></example>
public sealed record ManageQuery
{
    /// <summary>اندازه‌های مجاز صفحه (انتخاب در پایین جدول).</summary>
    public static readonly int[] AllowedPageSizes = [15, 30, 50, 100];

    public string? Search { get; init; }
    public Guid? Parent { get; init; }
    public int Page { get; init; } = 1;
    public string? Sort { get; init; }
    public bool Descending { get; init; }
    public int Take { get; init; } = ManageEngine.PageSize;

    /// <summary>نمایش ردیف‌های حذف‌شده (سطل بازیافت) به جای ردیف‌های فعال.</summary>
    public bool Deleted { get; init; }
}

/// <summary>یک ردیف سطل بازیافت: شناسه، برچسب قابل نمایش و زمان حذف.</summary>
public sealed record TrashRow(Guid Id, string Label, DateTimeOffset? DeletedAt);
