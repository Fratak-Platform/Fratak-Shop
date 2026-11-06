using System.Reflection;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Data.Auditing;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Admin.Manage;
using Fratak_Shop.Web.Infrastructure.PageCache;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های منطق خالص مرحلهٔ هفتم – فاز ۳: جدول‌های مدیریت (مرتب‌سازی امن، ویرایش درجا، ترتیب دستی)،
/// سطل بازیافت، نسخهٔ رکورد (ویرایش هم‌زمان)، تاریخچه/بازگردانی و پیش‌نویس خودکار.
/// </summary>
public class AdminTablesTests
{
    private static MDef Def(string key) => ManageRegistry.Find(key) ?? throw new InvalidOperationException($"جدول {key} تعریف نشده است.");

    // ── مرتب‌سازی ─────────────────────────────────────────────────────────

    [Fact]
    public void SortableField_AllowsOnlyDefinedColumns()
    {
        var coupons = Def("coupons");
        var first = coupons.Fields.First(f => f.Kind is not (MKind.Html or MKind.Image));

        Assert.Equal(first.Name, ManageEngine.SortableField(coupons, first.Name));
        Assert.Equal("CreatedAt", ManageEngine.SortableField(coupons, "CreatedAt"));
        Assert.Equal("UpdatedAt", ManageEngine.SortableField(coupons, "UpdatedAt"));

        // نام ستون از آدرس می‌آید: هر چیز ناشناخته نادیده گرفته می‌شود (نه خطا، نه تزریق).
        Assert.Null(ManageEngine.SortableField(coupons, null));
        Assert.Null(ManageEngine.SortableField(coupons, ""));
        Assert.Null(ManageEngine.SortableField(coupons, "Id; DROP TABLE Coupons"));
        Assert.Null(ManageEngine.SortableField(coupons, "IsDeleted"));
        Assert.Null(ManageEngine.SortableField(coupons, "UnknownColumn"));
    }

    [Fact]
    public void SortableField_NeverAllowsHtmlOrImageColumns()
    {
        foreach (var def in ManageRegistry.All)
            foreach (var field in def.Fields.Where(f => f.Kind is MKind.Html or MKind.Image))
                Assert.Null(ManageEngine.SortableField(def, field.Name));
    }

    // ── ویرایش درجا ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(MKind.Text, true)]
    [InlineData(MKind.Int, true)]
    [InlineData(MKind.Decimal, true)]
    [InlineData(MKind.Bool, true)]
    [InlineData(MKind.Enum, true)]
    [InlineData(MKind.Slug, true)]
    [InlineData(MKind.Url, true)]
    [InlineData(MKind.Color, true)]
    [InlineData(MKind.Html, false)]
    [InlineData(MKind.Image, false)]
    [InlineData(MKind.Ref, false)]
    public void CanInline_OnlySimpleKinds(MKind kind, bool expected)
    {
        Assert.Equal(expected, new MField("X", "x", kind).CanInline);
        // NoInline همیشه ویرایش درجا را خاموش می‌کند.
        Assert.False(new MField("X", "x", kind) { NoInline = true }.CanInline);
    }

    [Fact]
    public void CanReorder_RequiresAscendingIntDisplayOrder()
    {
        foreach (var def in ManageRegistry.All)
        {
            var expected = def.Sort == "DisplayOrder" && !def.SortDescending && def.Entity.GetProperty("DisplayOrder")?.PropertyType == typeof(int);
            Assert.Equal(expected, ManageEngine.CanReorder(def));
        }
        // حداقل یک جدول (مثلاً سؤالات متداول یا منوها) باید ترتیب دستی داشته باشد؛ وگرنه کشیدن و رها کردن عملاً بی‌استفاده است.
        Assert.Contains(ManageRegistry.All, ManageEngine.CanReorder);
    }

    [Fact]
    public void ManageQuery_PageSizes_IncludeDefault()
    {
        Assert.Contains(ManageEngine.PageSize, ManageQuery.AllowedPageSizes);
        Assert.All(ManageQuery.AllowedPageSizes, size => Assert.InRange(size, 10, 100));
    }

    // ── سطل بازیافت ───────────────────────────────────────────────────────

    [Fact]
    public void TrashRegistry_KeysUnique_LabelsValid_UrlsTemplated()
    {
        var keys = TrashRegistry.All.Select(s => s.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var source in TrashRegistry.All)
        {
            Assert.Matches("^[a-z0-9-]+$", source.Key);
            Assert.Contains("{id}", source.EditUrl);
            Assert.StartsWith("/admin/", source.EditUrl);
            Assert.NotEmpty(source.Permissions);
            // برچسب باید ویژگی رشته‌ای واقعی باشد (EF.Property<string> روی غیر رشته خطا می‌دهد).
            if (source.LabelProperty.Length > 0)
                Assert.Equal(typeof(string), source.Entity.GetProperty(source.LabelProperty)?.PropertyType);
            Assert.True(Fratak_Shop.Web.Helpers.Icons.Has(source.Icon), $"آیکن {source.Icon} برای {source.Key} وجود ندارد.");
        }

        Assert.NotNull(TrashRegistry.Find("PRODUCTS"));
        Assert.Null(TrashRegistry.Find("../products"));
        Assert.Null(TrashRegistry.Find(null));
    }

    /// <summary>
    /// مجوزهای صفحهٔ سطل بازیافت و آیتم منو باید دقیقاً اجتماع مجوزهای همهٔ نوع‌ها باشد؛
    /// وگرنه کسی که فقط به یک جدول دسترسی دارد صفحه را نمی‌بیند (یا برعکس، صفحه برای کسی باز می‌شود که چیزی در آن ندارد).
    /// </summary>
    [Fact]
    public void TrashPermissions_MatchAllSources()
    {
        var union = TrashRegistry.All.SelectMany(s => s.Permissions).ToHashSet();
        Assert.Equal(union.OrderBy(p => p), TrashRegistry.AnyPermission.Distinct().OrderBy(p => p));

        var attribute = typeof(Fratak_Shop.Web.Areas.Admin.Pages.Trash.IndexModel).GetCustomAttribute<AdminPermissionAttribute>();
        Assert.NotNull(attribute);
        Assert.Equal(union.OrderBy(p => p), attribute!.AnyOf.Distinct().OrderBy(p => p));

        var nav = AdminNav.Groups.SelectMany(g => g.Items).Single(i => i.Url == "/admin/trash");
        Assert.Equal(union.OrderBy(p => p), nav.AnyOf!.Distinct().OrderBy(p => p));
    }

    // ── نسخهٔ رکورد (ویرایش هم‌زمان) ─────────────────────────────────────

    private static FormCollection Form(params (string Key, string Value)[] values) =>
        new(values.GroupBy(v => v.Key).ToDictionary(g => g.Key, g => new StringValues(g.Select(v => v.Value).ToArray())));

    [Fact]
    public void EditStamp_DetectsStaleForm()
    {
        Assert.False(EditStamp.IsStale(Form((EditStamp.FieldName, "100")), 100));
        Assert.True(EditStamp.IsStale(Form((EditStamp.FieldName, "100")), 200));
        // تیک «با وجود این ذخیره شود»
        Assert.False(EditStamp.IsStale(Form((EditStamp.FieldName, "100"), (EditStamp.ForceName, "true")), 200));
        // فرم قدیمی بدون نسخه یا مقدار خراب: بررسی نمی‌شود (رفتار قبلی حفظ می‌شود).
        Assert.False(EditStamp.IsStale(Form(), 200));
        Assert.False(EditStamp.IsStale(Form((EditStamp.FieldName, "abc")), 200));
        Assert.False(EditStamp.IsStale(Form((EditStamp.FieldName, "-5")), 200));
        Assert.Equal("638000000000000000", EditStamp.Format(638000000000000000));
    }

    // ── تاریخچه و بازگردانی ───────────────────────────────────────────────

    [Fact]
    public void ParseChanges_ReadsBeforeAfter_AndToleratesBadJson()
    {
        var changes = IAuditHistoryService.ParseChanges("""{"Price":["400000","415000"],"Name":[null,"گوشی"],"Stock":[3,4],"Bad":"x"}""");
        Assert.Equal(3, changes.Count);
        Assert.Equal(new AuditFieldChange("Price", "400000", "415000"), changes[0]);
        Assert.Equal(new AuditFieldChange("Name", null, "گوشی"), changes[1]);
        Assert.Equal(new AuditFieldChange("Stock", "3", "4"), changes[2]);

        Assert.Empty(IAuditHistoryService.ParseChanges(null));
        Assert.Empty(IAuditHistoryService.ParseChanges("{not json"));
        Assert.Empty(IAuditHistoryService.ParseChanges("[]"));
    }

    public enum Sample { First, Second }

    public static TheoryData<object?, Type> RoundTripValues => new()
    {
        { "متن فارسی", typeof(string) },
        { 42, typeof(int) },
        { 42, typeof(int?) },
        { 9_000_000_000L, typeof(long) },
        { 415000m, typeof(decimal) },
        { 12.5m, typeof(decimal) },
        { true, typeof(bool) },
        { Sample.Second, typeof(Sample) },
        { Guid.Parse("3f2a1b4c-5d6e-4f70-8a9b-0c1d2e3f4a5b"), typeof(Guid) },
        { null, typeof(string) },
        { null, typeof(int?) },
    };

    /// <summary>هر مقداری که گزارش فعالیت می‌نویسد باید با بازگردانی دقیقاً همان مقدار شود.</summary>
    [Theory]
    [MemberData(nameof(RoundTripValues))]
    public void AuditValues_RoundTripForRevert(object? value, Type type)
    {
        var text = AuditSaveChangesInterceptor.FormatValue(value);
        Assert.True(AuditHistoryService.TryParse(text, type, out var parsed));
        Assert.Equal(value, parsed);
    }

    [Fact]
    public void AuditDates_RoundTripAsSameInstant_WhateverTheOffset()
    {
        // ۱۰:۳۰ به وقت تهران = ۰۷:۰۰ جهانی؛ بازگردانی باید همان لحظه را بسازد.
        var tehran = new DateTimeOffset(2026, 10, 25, 10, 30, 0, TimeSpan.FromHours(3.5));
        var text = AuditSaveChangesInterceptor.FormatValue(tehran);
        Assert.Equal("2026-10-25 07:00", text);
        Assert.True(AuditHistoryService.TryParse(text, typeof(DateTimeOffset), out var parsed));
        Assert.Equal(tehran.UtcDateTime, ((DateTimeOffset)parsed!).UtcDateTime);
    }

    [Fact]
    public void TryParse_RejectsInvalidValues()
    {
        Assert.False(AuditHistoryService.TryParse("abc", typeof(int), out _));
        Assert.False(AuditHistoryService.TryParse("Third", typeof(Sample), out _));
        Assert.False(AuditHistoryService.TryParse("99", typeof(Sample), out _)); // عدد خارج از اعضای enum
        Assert.False(AuditHistoryService.TryParse(null, typeof(int), out _));    // null برای نوع غیر nullable
        Assert.False(AuditHistoryService.TryParse("x", typeof(byte[]), out _));  // نوع پشتیبانی‌نشده
    }

    [Fact]
    public void HistoryItem_OnlyUpdatesWithChangesAreRevertible()
    {
        var change = new[] { new AuditFieldChange("Price", "1", "2") };
        Assert.True(new AuditHistoryItem(Guid.NewGuid(), DateTimeOffset.UtcNow, "update", null, null, change).CanRevert);
        Assert.False(new AuditHistoryItem(Guid.NewGuid(), DateTimeOffset.UtcNow, "update", null, null, []).CanRevert);
        Assert.False(new AuditHistoryItem(Guid.NewGuid(), DateTimeOffset.UtcNow, "create", null, null, change).CanRevert);
        Assert.False(new AuditHistoryItem(Guid.NewGuid(), DateTimeOffset.UtcNow, "delete", null, null, change).CanRevert);
    }

    [Fact]
    public void ProtectedProperties_AreNeverRevertible()
    {
        Assert.True(AuditSaveChangesInterceptor.IsProtectedProperty("PasswordHash"));
        Assert.True(AuditSaveChangesInterceptor.IsProtectedProperty("Id"));
        Assert.True(AuditSaveChangesInterceptor.IsProtectedProperty("IsDeleted"));
        Assert.True(AuditSaveChangesInterceptor.IsProtectedProperty("MerchantId"));
        Assert.False(AuditSaveChangesInterceptor.IsProtectedProperty("Price"));
        Assert.False(AuditSaveChangesInterceptor.IsProtectedProperty("Name"));
    }

    // ── پیش‌نویس خودکار ──────────────────────────────────────────────────

    [Theory]
    [InlineData("product:3f2a1b4c-5d6e-4f70-8a9b-0c1d2e3f4a5b", true)]
    [InlineData("manage:faq-items:new", true)]
    [InlineData("blog:new", true)]
    [InlineData("", false)]
    [InlineData("Product:1", false)]
    [InlineData("product/1", false)]
    [InlineData("<script>", false)]
    public void DraftKeys_AreValidated(string key, bool expected)
    {
        Assert.Equal(expected, EditDraftService.IsValidKey(key));
    }

    [Fact]
    public void DraftKeys_LongerThan120_AreRejected()
    {
        Assert.True(EditDraftService.IsValidKey(new string('a', 120)));
        Assert.False(EditDraftService.IsValidKey(new string('a', 121)));
    }

    /// <summary>ذخیرهٔ پیش‌نویس هر چند ثانیه نباید کش صفحات عمومی سایت را خالی کند.</summary>
    [Fact]
    public void AdminOnlyData_DoesNotEvictPublicPageCache()
    {
        Assert.False(PageCachePolicy.AffectsPages([typeof(EditDraft)]));
        Assert.False(PageCachePolicy.AffectsPages([typeof(AuditLog), typeof(StaffNotification)]));
        Assert.True(PageCachePolicy.AffectsPages([typeof(EditDraft), typeof(Fratak_Shop.Data.Entities.Products.Product)]));
    }
}
