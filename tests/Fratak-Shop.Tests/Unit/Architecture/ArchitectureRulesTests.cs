using System.Reflection;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Tests.Unit.Architecture;

/// <summary>
/// قانون‌های معماری که با بازتاب (Reflection) روی کد کامپایل‌شده بررسی می‌شوند؛ هر صفحه یا موجودیت تازه‌ای که قاعده را
/// بشکند همین‌جا در CI گیر می‌افتد، پیش از آنکه به یک حفرهٔ امنیتی یا دادهٔ خراب برسد:
/// • هر صفحهٔ پنل مدیریت یا مجوز دارد یا آگاهانه در فهرست استثنا (با دلیل) آمده؛
/// • هیچ هندلر GET کار تغییردهنده انجام نمی‌دهد (GET بدون توکن CSRF است)؛
/// • نادیده گرفتن توکن ضدجعل فقط در صفحهٔ ۴۰۴ مجاز است؛
/// • فرم‌های فروشگاه هیچ فیلد حساسی (نقش، موجودی کیف پول، مسدودی…) را از ورودی کاربر نمی‌پذیرند (Over-posting)؛
/// • هر ستون مبلغ در پایگاه داده دقت صریح دارد تا مبلغ‌ها گرد یا بریده نشوند.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Architecture")]
public class ArchitectureRulesTests
{
    private static readonly Assembly Web = typeof(Program).Assembly;

    /// <summary>صفحه‌های پنل که عمداً مجوز کلاسی ندارند؛ هر کدام کنترل دسترسی خودش را دارد یا برای همهٔ کارکنان است.</summary>
    private static readonly Dictionary<string, string> AdminPagesWithoutAttribute = new()
    {
        ["Fratak_Shop.Web.Areas.Admin.Pages.IndexModel"] = "داشبورد؛ هر ویجت با CanAny نمایش داده می‌شود.",
        ["Fratak_Shop.Web.Areas.Admin.Pages.DeniedModel"] = "صفحهٔ «دسترسی ندارید».",
        ["Fratak_Shop.Web.Areas.Admin.Pages.TeamPages.AccountModel"] = "حساب شخصی کارمند (رمز، ورود دومرحله‌ای).",
        ["Fratak_Shop.Web.Areas.Admin.Pages.TeamPages.ReauthModel"] = "تأیید دوبارهٔ هویت.",
        ["Fratak_Shop.Web.Areas.Admin.Pages.TeamPages.TasksModel"] = "کارهای شخصی؛ سرویس با AdminAccess مالکیت را می‌سنجد.",
        ["Fratak_Shop.Web.Areas.Admin.Pages.Inbox.IndexModel"] = "صندوق اعلان‌های شخصی.",
        ["Fratak_Shop.Web.Areas.Admin.Pages.Ux.IndexModel"] = "جست‌وجوی سراسری؛ دامنه‌ها با Can محدود می‌شوند.",
        ["Fratak_Shop.Web.Areas.Admin.Pages.Manage.IndexModel"] = "فهرست جدول‌ها؛ فقط جدول‌های مجاز نشان داده می‌شوند.",
        ["Fratak_Shop.Web.Areas.Admin.Pages.Manage.ListModel"] = "همهٔ هندلرها مجوز جدول (MDef.Permissions) را بررسی می‌کنند.",
        ["Fratak_Shop.Web.Areas.Admin.Pages.Manage.EditModel"] = "LoadAsync مجوز جدول را پیش از هر کاری بررسی می‌کند.",
    };

    private static IEnumerable<Type> PageModels(string namespacePrefix) => Web.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(PageModel).IsAssignableFrom(t)
            && t.Namespace?.StartsWith(namespacePrefix, StringComparison.Ordinal) == true);

    [Fact]
    public void Every_admin_page_requires_a_permission_or_is_a_reviewed_exception()
    {
        var admin = PageModels("Fratak_Shop.Web.Areas.Admin").ToList();
        Assert.True(admin.Count > 60, $"only {admin.Count} admin pages found");

        var unprotected = admin
            .Where(t => t.GetCustomAttribute<AdminPermissionAttribute>(true) is null && t.GetCustomAttribute<AdminOwnerOnlyAttribute>(true) is null)
            .Select(t => t.FullName!)
            .Where(name => !AdminPagesWithoutAttribute.ContainsKey(name))
            .ToList();

        Assert.True(unprotected.Count == 0, "Admin pages without [AdminPermission]: " + string.Join(", ", unprotected));
    }

    [Fact]
    public void The_exception_list_only_names_pages_that_still_exist_and_still_lack_an_attribute()
    {
        var byName = PageModels("Fratak_Shop.Web.Areas.Admin").ToDictionary(t => t.FullName!);
        foreach (var name in AdminPagesWithoutAttribute.Keys)
        {
            Assert.True(byName.TryGetValue(name, out var type), $"{name} no longer exists – remove it from the list.");
            Assert.Null(type!.GetCustomAttribute<AdminPermissionAttribute>(true));
        }
    }

    [Fact]
    public void Staff_and_finance_pages_need_the_matching_permission()
    {
        static string[] Needs(Type t) => t.GetCustomAttributes<AdminPermissionAttribute>(true).SelectMany(a => a.AnyOf).ToArray();
        var pages = PageModels("Fratak_Shop.Web.Areas.Admin").ToList();

        foreach (var page in pages.Where(t => t.Namespace!.EndsWith(".Staff", StringComparison.Ordinal)))
            Assert.True(page.GetCustomAttribute<AdminOwnerOnlyAttribute>() is not null || Needs(page).SequenceEqual([AdminPermissions.StaffManage]),
                $"{page.FullName} is not limited to staff managers");
        foreach (var page in pages.Where(t => t.Namespace!.EndsWith(".Finance", StringComparison.Ordinal)))
            Assert.NotEmpty(Needs(page));
    }

    private static readonly HashSet<string> MutatingVerbs = new(StringComparer.Ordinal)
    {
        "Add", "Create", "Delete", "Remove", "Update", "Save", "Set", "Toggle", "Approve", "Reject", "Cancel", "Block", "Unblock",
        "Restore", "Purge", "Refund", "Pay", "Send", "Reset", "Clear", "Mark", "Revoke", "Run", "Apply", "Import", "Publish", "Archive",
    };

    [Fact]
    public void Get_handlers_never_perform_state_changes()
    {
        var offenders = new List<string>();
        foreach (var type in PageModels("Fratak_Shop.Web"))
        {
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                         .Where(m => m.Name.StartsWith("OnGet", StringComparison.Ordinal)))
            {
                var handler = method.Name["OnGet".Length..];
                if (handler.EndsWith("Async", StringComparison.Ordinal))
                    handler = handler[..^"Async".Length];
                var words = Regex.Matches(handler, "[A-Z][a-z0-9]*").Select(m => m.Value);
                if (words.Any(MutatingVerbs.Contains))
                    offenders.Add($"{type.FullName}.{method.Name}");
            }
        }

        Assert.True(offenders.Count == 0, "State-changing GET handlers (no CSRF protection): " + string.Join(", ", offenders));
    }

    [Fact]
    public void Antiforgery_is_only_disabled_on_the_404_page()
    {
        var types = Web.GetTypes().Where(t => t.GetCustomAttribute<IgnoreAntiforgeryTokenAttribute>(true) is not null
            || t.GetMethods().Any(m => m.GetCustomAttribute<IgnoreAntiforgeryTokenAttribute>() is not null))
            .Select(t => t.Name).ToList();

        Assert.Equal(new[] { "Error404Model" }, types);
    }

    private static readonly string[] SensitiveNames =
    [
        "Role", "IsAdmin", "IsStaff", "IsBlocked", "WalletBalance", "WalletBonusBalance", "SecurityStamp", "PasswordHash",
        "LoyaltyPoints", "StaffRoleId", "PaymentStatus", "TotalAmount", "FinalPrice", "IsPaid", "RefId", "TransactionId",
    ];

    [Fact]
    public void Storefront_forms_do_not_bind_sensitive_fields()
    {
        var offenders = new List<string>();
        foreach (var type in PageModels("Fratak_Shop.Web.Pages"))
        {
            var bound = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.GetCustomAttribute<BindPropertyAttribute>() is not null || type.GetCustomAttribute<BindPropertiesAttribute>() is not null);
            foreach (var property in bound)
            {
                if (SensitiveNames.Contains(property.Name))
                    offenders.Add($"{type.Name}.{property.Name}");
                var inner = property.PropertyType;
                if (inner.IsClass && inner != typeof(string) && inner.Assembly == Web)
                    offenders.AddRange(inner.GetProperties().Where(p => p.CanWrite && SensitiveNames.Contains(p.Name)).Select(p => $"{type.Name}.{property.Name}.{p.Name}"));
            }
        }

        Assert.True(offenders.Count == 0, "Over-posting risk: " + string.Join(", ", offenders));
    }

    [Fact]
    public void Every_money_column_has_an_explicit_precision()
    {
        var options = new DbContextOptionsBuilder<FratakShopDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True;TrustServerCertificate=True").Options;
        using var db = new FratakShopDbContext(options);

        var missing = db.Model.GetEntityTypes()
            .SelectMany(e => e.GetProperties().Select(p => (Entity: e, Property: p)))
            .Where(x => Nullable.GetUnderlyingType(x.Property.ClrType) is { } u ? u == typeof(decimal) : x.Property.ClrType == typeof(decimal))
            .Where(x => x.Property.GetPrecision() is null && x.Property.GetColumnType() is null)
            .Select(x => $"{x.Entity.ClrType.Name}.{x.Property.Name}")
            .ToList();

        Assert.True(missing.Count == 0, "Decimal columns without precision: " + string.Join(", ", missing));
    }

    [Fact]
    public void Every_entity_with_a_soft_delete_flag_hides_deleted_rows_by_default()
    {
        var options = new DbContextOptionsBuilder<FratakShopDbContext>()
            .UseSqlServer("Server=unused;Database=unused;Trusted_Connection=True;TrustServerCertificate=True").Options;
        using var db = new FratakShopDbContext(options);

        var unfiltered = db.Model.GetEntityTypes()
            .Where(e => e.BaseType is null && e.FindProperty("IsDeleted") is not null)
            .Where(e => e.GetDeclaredQueryFilters().Count == 0)
            .Select(e => e.ClrType.Name)
            .ToList();

        Assert.True(unfiltered.Count == 0, "Soft-deletable entities without a query filter: " + string.Join(", ", unfiltered));
    }
}
