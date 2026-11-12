using Fratak_Shop.Core.Services.Blogs;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Entities.Smart;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های واحد دور ۲۳ برای بلاگ (بدون پایگاه داده):
/// <list type="bullet">
/// <item>به‌روزرسانی مقالهٔ قدیمی (مورد ۳۶): کی لازم است، متن بخش، جایگزینی بدون تکرار، امنیت نام دسته.</item>
/// <item>موضوع از مناسبت‌ها (مورد ۳۸): راهنمای خرید مناسبتی با دستهٔ واقعی، رد مناسبت نزدیک، بدون «بهترین».</item>
/// </list>
/// </summary>
[Trait("Category", "Unit")]
public class Round23BlogTests
{
    [Theory]
    [InlineData(4, 2, true)]   // نیمی ناموجود ← به‌روزرسانی
    [InlineData(4, 0, true)]   // همه ناموجود
    [InlineData(1, 0, true)]
    [InlineData(4, 3, false)]  // فقط یکی ناموجود ← دست نمی‌خورد
    [InlineData(3, 2, false)]
    [InlineData(0, 0, false)]  // مقالهٔ بدون کالا
    public void Refresh_is_needed_only_when_half_the_products_are_gone(int referenced, int available, bool expected)
    {
        Assert.Equal(expected, BlogRefresh.NeedsRefresh(referenced, available));
    }

    [Fact]
    public void Refresh_section_lists_real_cards_with_jalali_month_and_encodes_category()
    {
        var section = BlogRefresh.Section("هدفون <script>", new[] { "a-1", "b-2", "c-3", "d-4" }, new DateOnly(2026, 10, 23)); // ۱ آبان ۱۴۰۵
        Assert.StartsWith("<h2>به‌روزرسانی آبان ۱۴۰۵: گزینه‌های موجود امروز</h2>", section);
        Assert.Contains("<p>[[product:a-1]]</p>", section);
        Assert.Contains("<p>[[product:c-3]]</p>", section);
        Assert.DoesNotContain("d-4", section); // حداکثر ۳ کالا
        Assert.DoesNotContain("<script>", section);
        Assert.DoesNotContain("دسته‌بندی نشده", section);
    }

    [Fact]
    public void Refresh_replaces_the_previous_section_instead_of_stacking()
    {
        const string body = "<p>متن اصلی</p><p>[[product:old-1]]</p><p>[[product:old-2]]</p>";
        var first = BlogRefresh.Apply(body, BlogRefresh.Section("هدفون", new[] { "new-1" }, new DateOnly(2026, 10, 23)));
        var second = BlogRefresh.Apply(first, BlogRefresh.Section("هدفون", new[] { "new-2" }, new DateOnly(2027, 2, 1)));

        Assert.StartsWith(body, second);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(second, "گزینه‌های موجود امروز"));
        Assert.DoesNotContain("new-1", second);
        Assert.Contains("[[product:new-2]]", second);
        // کالاهای اصلی مقاله جدا از بخش به‌روزرسانی شمرده می‌شوند (تا معیار «نیمی ناموجود» درست بماند).
        Assert.Equal(new[] { "old-1", "old-2" }, BlogRefresh.OriginalSlugs(second).ToArray());
        Assert.Equal(new[] { "old-1", "old-2" }, BlogRefresh.OriginalSlugs(body).ToArray());
    }

    [Fact]
    public void Planner_turns_upcoming_occasions_into_real_category_buying_guides()
    {
        var headphones = new PlannerCategory(Guid.NewGuid(), "هدفون", 12, [(Guid.NewGuid(), "هدفون سونی")]);
        var powerbanks = new PlannerCategory(Guid.NewGuid(), "پاوربانک", 5, []);
        var thin = new PlannerCategory(Guid.NewGuid(), "کابل", 1, []);
        var occasions = new List<SeoOccasion>
        {
            new("شب یلدا", new DateOnly(2026, 12, 21), 25, new[] { "x" }),
            new("سپندارمذگان (روز عشق ایرانی)", new DateOnly(2027, 2, 18), 30, new[] { "x" }),
            new("روز دانشجو", new DateOnly(2026, 12, 7), 5, new[] { "x" }), // خیلی نزدیک ← رد
        };

        var ideas = BlogPlanner.Propose(new[] { headphones, powerbanks, thin }, Array.Empty<string>(), Array.Empty<string>(), 10, occasions);

        var occasional = ideas.Where(i => i.Origin == "occasion").ToList();
        Assert.Equal(2, occasional.Count);
        Assert.Equal("راهنمای خرید هدفون برای شب یلدا", occasional[0].Topic);
        Assert.Equal(headphones.Id, occasional[0].CategoryId);
        Assert.Equal("راهنمای خرید پاوربانک برای سپندارمذگان", occasional[1].Topic); // پرانتز حذف، دسته‌ها به نوبت
        Assert.All(occasional, i => Assert.Equal(BlogArticleType.Campaign, i.Type)); // دور ۲۶: مقالهٔ مناسبتی = نوع «کمپین»
        Assert.All(occasional, i => Assert.Equal(i.Topic, i.Keyword)); // کلیدواژه در عنوان هست (شرط نویسنده)
        Assert.DoesNotContain(ideas, i => i.Topic.Contains("روز دانشجو", StringComparison.Ordinal));
        // دور ۲۶: دستهٔ تک‌کالا هم ایدهٔ دسته‌ای می‌گیرد، ولی راهنمای خرید (نیازمند ≥ ۲ کالا) نه.
        Assert.DoesNotContain(ideas, i => i.CategoryId == thin.Id && i.Type == BlogArticleType.BuyingGuide);
        Assert.DoesNotContain(ideas, i => i.Topic.Contains("بهترین", StringComparison.Ordinal));
    }

    [Fact]
    public void Planner_does_not_repeat_an_occasion_topic_already_written()
    {
        var headphones = new PlannerCategory(Guid.NewGuid(), "هدفون", 12, []);
        var occasions = new List<SeoOccasion> { new("شب یلدا", new DateOnly(2026, 12, 21), 25, new[] { "x" }) };

        var ideas = BlogPlanner.Propose(new[] { headphones }, new[] { "راهنمای خرید هدفون برای شب یلدا" }, Array.Empty<string>(), 5, occasions);

        Assert.DoesNotContain(ideas, i => i.Origin == "occasion");
        Assert.Empty(BlogPlanner.Propose(Array.Empty<PlannerCategory>(), Array.Empty<string>(), Array.Empty<string>(), 5, occasions));
    }
}
