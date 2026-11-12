using Fratak_Shop.Core.DTOs.Banners.Query;
using Fratak_Shop.Core.DTOs.Notifications;
using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Models;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// دور ۲۹ – قاعده‌های خالص (بدون پایگاه داده):
/// <list type="bullet">
/// <item>سقف و چرخش نوبتی بنرها با پشتیبانی «سنجاق» (BannerRules.Slots).</item>
/// <item>خطاب مناسب نسل (UserGreeting).</item>
/// <item>ترتیب یکسان منوی حساب کاربری در هدر و ستون پروفایل (ProfileMenu).</item>
/// <item>پیش‌فرض «قابل حذف بودن» اعلان‌ها و بازه‌های تنظیمات تازه.</item>
/// </list>
/// ترتیب: حالت‌های عادی ← مرزی ← نامعتبر/حمله.
/// </summary>
[Trait("Category", "Unit")]
public class Round29RulesTests
{
    private static readonly DateTimeOffset Noon = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static BannerDto Banner(string title, bool pinned = false, int order = 0) =>
        new() { Id = Guid.NewGuid(), Title = title, IsPinned = pinned, DisplayOrder = order, Position = BannerPosition.HeroSlider, IsActive = true };

    // ── بنرها: سقف + چرخش + سنجاق ─────────────────────────────────────

    [Fact]
    public void Slots_keeps_everything_when_under_the_limit()
    {
        var list = new[] { Banner("a"), Banner("b") };
        Assert.Equal(list, BannerRules.Slots(list, 5, 6, Noon));
    }

    [Fact]
    public void Slots_caps_to_the_limit_and_pinned_banners_always_stay()
    {
        var pinned = Banner("pin", pinned: true);
        var list = new List<BannerDto> { pinned, Banner("a"), Banner("b"), Banner("c"), Banner("d") };
        for (var h = 0; h < 72; h += 3)
        {
            var result = BannerRules.Slots(list, 3, 6, Noon.AddHours(h));
            Assert.Equal(3, result.Count);
            Assert.Equal(pinned, result[0]); // سنجاق همیشه اول
            Assert.Equal(3, result.Select(b => b.Id).Distinct().Count());
        }
    }

    [Fact]
    public void Slots_rotates_so_every_banner_is_shown_over_time()
    {
        var list = new List<BannerDto> { Banner("a"), Banner("b"), Banner("c"), Banner("d"), Banner("e") };
        var seen = new HashSet<Guid>();
        for (var w = 0; w < 10; w++)
            foreach (var b in BannerRules.Slots(list, 2, 6, Noon.AddHours(6 * w)))
                seen.Add(b.Id);
        Assert.Equal(5, seen.Count);
    }

    [Fact]
    public void Slots_is_deterministic_inside_one_window_for_page_cache()
    {
        var list = new List<BannerDto> { Banner("a"), Banner("b"), Banner("c") };
        var start = new DateTimeOffset(2026, 10, 22, 6, 0, 0, TimeSpan.Zero); // ابتدای یک پنجرهٔ ۶ ساعته
        var first = BannerRules.Slots(list, 2, 6, start).Select(b => b.Id).ToList();
        var later = BannerRules.Slots(list, 2, 6, start.AddHours(5).AddMinutes(59)).Select(b => b.Id).ToList();
        Assert.Equal(first, later);
        var next = BannerRules.Slots(list, 2, 6, start.AddHours(6)).Select(b => b.Id).ToList();
        Assert.NotEqual(first, next);
    }

    [Fact]
    public void Slots_keeps_the_original_display_order()
    {
        var list = new List<BannerDto> { Banner("a"), Banner("b"), Banner("c"), Banner("d") };
        for (var w = 0; w < 8; w++)
        {
            var result = BannerRules.Slots(list, 3, 1, Noon.AddHours(w));
            var indexes = result.Select(b => list.IndexOf(b)).ToList();
            Assert.Equal(indexes.Order().ToList(), indexes);
        }
    }

    [Fact]
    public void Slots_with_more_pinned_than_the_limit_shows_only_pinned_up_to_the_limit()
    {
        var list = new List<BannerDto> { Banner("p1", true), Banner("p2", true), Banner("p3", true), Banner("x") };
        var result = BannerRules.Slots(list, 2, 6, Noon);
        Assert.Equal(new[] { "p1", "p2" }, result.Select(b => b.Title).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void Slots_with_a_bad_limit_returns_nothing(int max)
    {
        Assert.Empty(BannerRules.Slots(new[] { Banner("a") }, max, 6, Noon));
    }

    [Fact]
    public void Slots_handles_empty_lists_and_zero_rotation_hours()
    {
        Assert.Empty(BannerRules.Slots(Array.Empty<BannerDto>(), 3, 6, Noon));
        var list = new List<BannerDto> { Banner("a"), Banner("b"), Banner("c") };
        Assert.Equal(2, BannerRules.Slots(list, 2, 0, Noon).Count); // ساعت صفر ← ۱ (بدون تقسیم بر صفر)
        Assert.Throws<ArgumentNullException>(() => BannerRules.Slots(null!, 2, 6, Noon));
    }

    // ── خطاب مناسب نسل ───────────────────────────────────────────────

    private static readonly DateOnly Today = new(2026, 10, 22);

    [Theory]
    [InlineData("محمد", "ناظری", 2002, "محمد عزیز")]          // ۲۴ سال
    [InlineData("محمد", "ناظری", 1990, "محمد عزیز")]         // ۳۶ سال
    [InlineData("محمد", "ناظری", 1975, "ناظری گرامی")]       // ۵۱ سال
    [InlineData("محمد", "ناظری", 1955, "محمد ناظری گرامی")]  // ۷۱ سال
    public void Greeting_depends_on_the_generation(string first, string last, int year, string expected)
    {
        Assert.Equal(expected, UserGreeting.For(first, last, new DateOnly(year, 5, 1), Today));
    }

    [Fact]
    public void Greeting_without_birth_date_is_neutral_and_without_name_is_anonymous()
    {
        Assert.Equal("محمد عزیز", UserGreeting.For("محمد", "ناظری", null, Today));
        Assert.Equal("ناظری عزیز", UserGreeting.For(null, "ناظری", null, Today));
        Assert.Equal(UserGreeting.Anonymous, UserGreeting.For(null, null, null, Today));
        Assert.Equal(UserGreeting.Anonymous, UserGreeting.For("  ", "", new DateOnly(1990, 1, 1), Today));
    }

    [Fact]
    public void Greeting_middle_age_without_surname_falls_back_to_first_name()
    {
        Assert.Equal("محمد گرامی", UserGreeting.For("محمد", null, new DateOnly(1975, 1, 1), Today));
    }

    [Fact]
    public void Greeting_ignores_impossible_ages()
    {
        Assert.Equal("محمد عزیز", UserGreeting.For("محمد", null, new DateOnly(2024, 1, 1), Today)); // ۲ سال ← نامعلوم
        Assert.Equal("محمد عزیز", UserGreeting.For("محمد", null, new DateOnly(1900, 1, 1), Today)); // ۱۲۶ سال ← نامعلوم
    }

    [Theory]
    [InlineData(8, "صبح بخیر، محمد عزیز")]
    [InlineData(13, "ظهر بخیر، محمد عزیز")]
    [InlineData(18, "عصر بخیر، محمد عزیز")]
    [InlineData(23, "شب بخیر، محمد عزیز")]
    [InlineData(2, "شب بخیر، محمد عزیز")]
    public void Hello_uses_the_time_of_day(int hour, string expected)
    {
        Assert.Equal(expected, UserGreeting.Hello("محمد عزیز", hour));
    }

    // ── منوی حساب کاربری ───────────────────────────────────────────────

    [Fact]
    public void Staff_see_the_admin_panel_first_in_both_menus()
    {
        var settings = new StoreSettingDto();
        Assert.Equal("/admin", ProfileMenu.Build(settings, true)[0].Url);
        Assert.Equal("/admin", ProfileMenu.Dropdown(settings, true)[0].Url);
        Assert.DoesNotContain(ProfileMenu.Build(settings, false), i => i.Url == "/admin");
    }

    [Fact]
    public void Password_and_security_comes_right_after_personal_info()
    {
        var items = ProfileMenu.Build(new StoreSettingDto(), false);
        var info = items.FindIndex(i => i.Section == ProfileSection.PersonalInfo);
        Assert.Equal(ProfileSection.Password, items[info + 1].Section);
    }

    [Fact]
    public void Dropdown_has_profile_and_keeps_the_same_order_as_the_sidebar()
    {
        var settings = new StoreSettingDto();
        var full = ProfileMenu.Build(settings, true).Select(i => i.Url).ToList();
        var dropdown = ProfileMenu.Dropdown(settings, true).Select(i => i.Url).ToList();
        Assert.Contains("/profile/info", dropdown);
        var positions = dropdown.Select(u => full.IndexOf(u)).ToList();
        Assert.Equal(positions.Order().ToList(), positions);
    }

    [Fact]
    public void Messages_is_gone_and_optional_sections_follow_settings()
    {
        var off = new StoreSettingDto { IsWalletEnabled = false, IsTicketEnabled = false, IsReturnEnabled = false };
        var items = ProfileMenu.Build(off, false);
        Assert.DoesNotContain(items, i => i.Url.Contains("messages", StringComparison.Ordinal));
        Assert.DoesNotContain(items, i => i.Section is ProfileSection.Wallet or ProfileSection.Tickets or ProfileSection.Returns);
        Assert.All(items, i => Assert.StartsWith("/", i.Url, StringComparison.Ordinal));
        Assert.Throws<ArgumentNullException>(() => ProfileMenu.Build(null!, false));
    }

    // ── اعلان‌ها و تنظیمات ───────────────────────────────────────────

    [Theory]
    [InlineData(NotificationCategory.Order, true)]
    [InlineData(NotificationCategory.Promotion, true)]
    [InlineData(NotificationCategory.System, true)]
    [InlineData(NotificationCategory.Security, false)]
    public void Security_notifications_are_admin_delete_only_by_default(NotificationCategory category, bool deletable)
    {
        Assert.Equal(deletable, NotificationRequest.DeletableByDefault(category));
    }

    [Fact]
    public void Round29_options_are_clamped()
    {
        var low = new SmartOptions { BannerSlotsHero = 0, BannerSlotsWide = -1, BannerSlotsPromo = 0, BannerRotateHours = 0, NotificationPurgeDays = 5, StaffNotificationReadDays = -2 }.Normalize();
        Assert.Equal(1, low.BannerSlotsHero);
        Assert.Equal(1, low.BannerSlotsWide);
        Assert.Equal(1, low.BannerSlotsPromo);
        Assert.Equal(1, low.BannerRotateHours);
        Assert.Equal(30, low.NotificationPurgeDays); // کمتر از ۳۰ روز مجاز نیست (۰ = خاموش)
        Assert.Equal(0, low.StaffNotificationReadDays);

        var high = new SmartOptions { BannerSlotsHero = 99, BannerSlotsPromo = 99, BannerRotateHours = 1000, NotificationPurgeDays = 99_999, StaffNotificationReadDays = 999 }.Normalize();
        Assert.Equal(10, high.BannerSlotsHero);
        Assert.Equal(4, high.BannerSlotsPromo);
        Assert.Equal(72, high.BannerRotateHours);
        Assert.Equal(730, high.NotificationPurgeDays);
        Assert.Equal(90, high.StaffNotificationReadDays);

        Assert.Equal(0, new SmartOptions { NotificationPurgeDays = 0 }.Normalize().NotificationPurgeDays);
    }

    [Fact]
    public void Old_image_defaults_are_upgraded_once_to_five()
    {
        var old = new SmartOptions { OptionsVersion = 28, AutoImagesWhenFewerThan = 2, AutoImagesMax = 3 }.Normalize();
        Assert.Equal(5, old.AutoImagesWhenFewerThan);
        Assert.Equal(5, old.AutoImagesMax);

        // انتخاب دلخواه مدیر بعد از ارتقا دست نمی‌خورد.
        var chosen = new SmartOptions { OptionsVersion = SmartOptions.CurrentVersion, AutoImagesWhenFewerThan = 2, AutoImagesMax = 3 }.Normalize();
        Assert.Equal(2, chosen.AutoImagesWhenFewerThan);
        Assert.Equal(3, chosen.AutoImagesMax);
    }
}
