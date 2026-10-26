using System.Reflection;
using System.Text;
using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Web.Areas.Admin.Pages.Settings;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Admin.Manage;

namespace Fratak_Shop.Tests.Unit;

public class AdminPermissionTests
{
    private static readonly Type[] AdminPages = typeof(AdminPageModel).Assembly.GetTypes()
        .Where(t => !t.IsAbstract && typeof(AdminPageModel).IsAssignableFrom(t))
        .ToArray();

    private static AdminAccess Staff(params string[] permissions) =>
        new(Guid.NewGuid(), "کارمند", "09120000001", false, new HashSet<string>(permissions), ["نقش"]);

    [Fact]
    public void Catalog_HasUniqueKnownKeys_AndDefaultRolesUseThem()
    {
        var keys = AdminPermissions.All.Select(p => p.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());
        Assert.All(keys, k => Assert.True(AdminPermissions.IsKnown(k)));
        Assert.False(AdminPermissions.IsKnown("made.up"));
        Assert.False(AdminPermissions.IsKnown(null));

        Assert.NotEmpty(AdminPermissions.DefaultRoles);
        foreach (var role in AdminPermissions.DefaultRoles)
            Assert.All(role.Permissions, p => Assert.True(AdminPermissions.IsKnown(p), $"{role.Name}: {p}"));
    }

    [Fact]
    public void EveryAdminPage_IsProtected_ByKnownPermissions()
    {
        Assert.True(AdminPages.Length > 20);
        foreach (var page in AdminPages)
        {
            var attributes = page.GetCustomAttributes<AdminPermissionAttribute>(true)
                .Concat(page.GetMethods().SelectMany(m => m.GetCustomAttributes<AdminPermissionAttribute>(true)))
                .ToList();
            foreach (var attribute in attributes)
                Assert.All(attribute.AnyOf, p => Assert.True(AdminPermissions.IsKnown(p), $"{page.FullName}: {p}"));

            // Only the dashboard, the access-denied page and the personal inbox are open to every staff member.
            if (page.Name is "DeniedModel" || page.FullName is "Fratak_Shop.Web.Areas.Admin.Pages.IndexModel" or "Fratak_Shop.Web.Areas.Admin.Pages.Inbox.IndexModel"
                    // «میانبرهای من» فقط تنظیمات شخصی خود کارمند را تغییر می‌دهد؛ جست‌وجویش بر اساس مجوز محدود می‌شود.
                    or "Fratak_Shop.Web.Areas.Admin.Pages.Ux.IndexModel"
                    // فاز ۱۰: «حساب من» (TOTP و نشست‌های خود کارمند)، «تأیید دوباره» و «کارهای من» شخصی‌اند و هر کارمند باید به آن‌ها برسد.
                    or "Fratak_Shop.Web.Areas.Admin.Pages.TeamPages.AccountModel"
                    or "Fratak_Shop.Web.Areas.Admin.Pages.TeamPages.ReauthModel"
                    or "Fratak_Shop.Web.Areas.Admin.Pages.TeamPages.TasksModel")
                continue;
            var protectedByManage = page.Namespace?.EndsWith(".Manage") == true; // checked per table (MDef.Permissions)
            Assert.True(attributes.Count > 0 || protectedByManage || page.GetCustomAttribute<AdminOwnerOnlyAttribute>() is not null,
                $"{page.FullName} has no [AdminPermission]");
        }
    }

    [Fact]
    public void IsAllowed_RespectsRolePermissions()
    {
        var writer = Staff(AdminPermissions.Dashboard, AdminPermissions.BlogWrite);
        var owner = new AdminAccess(Guid.NewGuid(), "مالک", "09120000009", true, new HashSet<string>(), []);

        var blog = AdminPages.Single(t => t.FullName == "Fratak_Shop.Web.Areas.Admin.Pages.Blog.IndexModel");
        var finance = AdminPages.Single(t => t.FullName == "Fratak_Shop.Web.Areas.Admin.Pages.Finance.IndexModel");
        var settings = AdminPages.Single(t => t.FullName == "Fratak_Shop.Web.Areas.Admin.Pages.Settings.IndexModel");

        Assert.True(AdminPageFilter.IsAllowed(writer, blog, null, out _));
        Assert.False(AdminPageFilter.IsAllowed(writer, finance, null, out var missing));
        Assert.Contains(AdminPermissions.FinanceView, missing);
        Assert.False(AdminPageFilter.IsAllowed(writer, settings, null, out _));

        Assert.True(AdminPageFilter.IsAllowed(owner, finance, null, out _));
        Assert.True(AdminPageFilter.IsAllowed(owner, settings, null, out _));
    }

    [Fact]
    public void Access_StaffAndPermissionChecks()
    {
        Assert.False(AdminAccess.None(Guid.NewGuid()).IsStaff);
        var seller = Staff(AdminPermissions.OrdersView);
        Assert.True(seller.IsStaff);
        Assert.True(seller.Has(AdminPermissions.OrdersView));
        Assert.False(seller.Has(AdminPermissions.OrdersManage));
        Assert.True(seller.HasAny(AdminPermissions.OrdersManage, AdminPermissions.OrdersView));
    }

    [Fact]
    public void Navigation_UsesKnownPermissions_AndUniqueUrls()
    {
        var items = AdminNav.Groups.SelectMany(g => g.Items).ToList();
        Assert.Equal(items.Count, items.Select(i => i.Url).Distinct().Count());
        foreach (var item in items)
        {
            Assert.StartsWith("/admin", item.Url);
            Assert.All(item.AnyOf ?? [], p => Assert.True(AdminPermissions.IsKnown(p), $"{item.Title}: {p}"));
        }
    }
}

public class AdminRegistryTests
{
    [Fact]
    public void SettingsTabs_CoverEveryStoreSettingField_Once()
    {
        var commandProps = typeof(StoreSettingCommand).GetProperties().ToDictionary(p => p.Name);
        var fields = SettingsRegistry.Groups.SelectMany(g => g.Fields).ToList();

        Assert.Equal(fields.Count, fields.Select(f => f.Name).Distinct().Count());
        Assert.Equal(SettingsRegistry.Groups.Length, SettingsRegistry.Groups.Select(g => g.Key).Distinct().Count());

        foreach (var field in fields)
        {
            Assert.True(commandProps.TryGetValue(field.Name, out var prop), $"unknown setting {field.Name}");
            var type = Nullable.GetUnderlyingType(prop!.PropertyType) ?? prop.PropertyType;
            var ok = field.Kind switch
            {
                SKind.Bool => type == typeof(bool),
                SKind.Int => type == typeof(int),
                SKind.Money or SKind.Decimal => type == typeof(decimal) || type == typeof(long) || type == typeof(int),
                SKind.Select => type == typeof(string) || type == typeof(int) || type.IsEnum,
                _ => type == typeof(string),
            };
            Assert.True(ok, $"{field.Name}: {field.Kind} vs {type.Name}");
        }

        var missing = commandProps.Keys.Except(fields.Select(f => f.Name)).ToList();
        Assert.True(missing.Count == 0, "settings without a tab: " + string.Join(", ", missing));
    }

    [Fact]
    public void ManageTables_ReferenceRealEntityProperties()
    {
        var defs = ManageRegistry.All;

        Assert.True(defs.Length >= 25, $"only {defs.Length} manage tables found");
        Assert.Equal(defs.Length, defs.Select(d => d.Key).Distinct().Count());

        foreach (var def in defs)
        {
            Assert.Same(def, ManageRegistry.Find(def.Key));
            Assert.NotEmpty(def.Permissions);
            Assert.All(def.Permissions, p => Assert.True(AdminPermissions.IsKnown(p), $"{def.Key}: {p}"));
            foreach (var name in def.Fields.Select(f => f.Name).Concat(def.Search).Append(def.Sort))
                Assert.True(def.Entity.GetProperty(name) is not null, $"{def.Key}: {def.Entity.Name}.{name} does not exist");
            if (def.ParentField is not null)
                Assert.NotNull(def.Entity.GetProperty(def.ParentField));
            foreach (var child in def.Children)
                Assert.NotNull(ManageRegistry.Find(child.Key));
        }
    }
}

public class AdminPeriodAndExportTests
{
    [Theory]
    [InlineData("1404/01/01", 2025, 3, 21)]
    [InlineData("۱۴۰۴/۰۱/۰۱", 2025, 3, 21)]
    [InlineData("04-1-1", 2025, 3, 21)]
    [InlineData("2025-03-21", 2025, 3, 21)]
    [InlineData("1403/12/30", 2025, 3, 20)]
    public void PersianDate_ParsesPersianAndGregorianInput(string text, int year, int month, int day)
    {
        Assert.Equal(new DateOnly(year, month, day), PersianDateInput.Parse(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("1404/13/01")]
    [InlineData("1404/12/31")]
    [InlineData("1404/01")]
    public void PersianDate_RejectsInvalidInput(string? text)
    {
        Assert.Null(PersianDateInput.Parse(text));
    }

    [Fact]
    public void PersianDate_FormatRoundTrips()
    {
        Assert.Equal("1404/01/01", PersianDateInput.Format(new DateOnly(2025, 3, 21)));
        Assert.Equal("", PersianDateInput.Format(null));
    }

    [Fact]
    public void Periods_ResolvePresets_WithEqualLengthPreviousPeriod()
    {
        foreach (var (key, _) in AdminPeriods.Presets)
        {
            var period = AdminPeriods.Resolve(key, null, null);
            Assert.Equal(key, period.Preset);
            if (key == "all")
            {
                Assert.Null(period.Current.From);
                Assert.Null(period.Previous);
                continue;
            }

            Assert.NotNull(period.Current.From);
            Assert.NotNull(period.Current.To);
            Assert.True(period.Current.From < period.Current.To);
            Assert.NotNull(period.Previous);
            Assert.Equal(period.Current.From, period.Previous!.To);
        }

        var week = AdminPeriods.Resolve("7d", null, null);
        Assert.Equal(TimeSpan.FromDays(7), week.Current.To!.Value - week.Current.From!.Value);
        Assert.Equal(TimeSpan.FromDays(7), week.Previous!.To!.Value - week.Previous.From!.Value);

        Assert.Equal("30d", AdminPeriods.Resolve("bogus", null, null).Preset);
    }

    [Fact]
    public void Periods_CustomRange_IsInclusiveOfBothDays()
    {
        var period = AdminPeriods.Resolve("today", "1404/01/01", "1404/01/10");
        Assert.Equal("custom", period.Preset);
        Assert.Equal(IranClock.StartOfDay(new DateOnly(2025, 3, 21)), period.Current.From);
        Assert.Equal(IranClock.StartOfDay(new DateOnly(2025, 3, 31)), period.Current.To);
        Assert.Equal(IranClock.StartOfDay(new DateOnly(2025, 3, 11)), period.Previous!.From);
    }

    [Fact]
    public void Csv_HasBom_EscapesCells_AndBlocksFormulaInjection()
    {
        var table = new ExportTable("t", "نام", "مبلغ", "یادداشت")
            .Add("علی, رضایی", 1500000m, "=HYPERLINK(\"x\")")
            .Add("ساده", 12, "+98");

        var bytes = Exporter.Csv(table);
        Assert.Equal(Encoding.UTF8.GetPreamble(), bytes.Take(3).ToArray());
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(3, lines.Length);
        Assert.Equal("نام,مبلغ,یادداشت", lines[0]);
        Assert.StartsWith("\"علی, رضایی\",1500000,", lines[1]);
        Assert.Contains("'=HYPERLINK", lines[1]);
        Assert.EndsWith(",'+98", lines[2]);
    }

    [Fact]
    public void Xlsx_IsAValidZipPackage()
    {
        var table = new ExportTable("گزارش", "a", "b").Add("x", 1).Add(null, DateTimeOffset.UtcNow);
        var bytes = Exporter.Xlsx(table);

        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("[Content_Types].xml", names);
        Assert.Contains(names, n => n.StartsWith("xl/worksheets/"));
    }
}
