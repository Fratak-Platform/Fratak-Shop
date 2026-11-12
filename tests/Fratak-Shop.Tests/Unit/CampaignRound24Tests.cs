using Fratak_Shop.Core.Services.Design;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Data.Entities.Marketing;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست واحد دور ۲۴ (بستهٔ C – بنر هوشمند): خواندن «امتیاز n برابر» از متن، تصمیم «قاعدهٔ واقعی لازم است یا نه»
/// (<see cref="DesignBrief.Offer"/>)، متن بنر با قاعدهٔ کمپین، انتخاب کالا از فهرست و حساب درصد کوپن روی کالای تخفیف‌دار.
/// از بهترین حالت تا بدترین: متن عادی، عدد بزرگ‌تر از سقف، متن بی‌ربط، کارت هدیه، کالای ناموجود.
/// </summary>
[Trait("Category", "Unit")]
public class CampaignRound24Tests
{
    private static IdeaProduct Idea(string name, decimal price, decimal final, int stock = 5, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), name, "p-" + Guid.NewGuid().ToString("N")[..6], "/uploads/products/a.webp", 800, 800, price, final, stock, 10, 3, "هدفون", "headphone", DateTimeOffset.UtcNow);

    private static readonly BriefCategory Headphones = new(Guid.NewGuid(), "هدفون", "headphone", 12);

    // ── خواندن ضریب امتیاز ─────────────────────────────────────

    [Theory]
    [InlineData("امتیاز دوبرابر", 2)]
    [InlineData("حراج هدفون با امتیاز دو برابر", 2)]
    [InlineData("۳ برابر امتیاز برای یلدا", 3)]
    [InlineData("امتیاز x4 برای خرید", 4)]
    [InlineData("امتیاز ×۵", 5)]
    [InlineData("امتیاز سه برابر", 3)]
    [InlineData("امتیاز ۱۰ برابر", 10)]
    public void Multiplier_is_read_only_next_to_the_word_points(string text, int expected)
    {
        Assert.Equal(expected, DesignBrief.ParseMultiplier(DesignBrief.Normalize(text)));
        Assert.Equal(expected, DesignBrief.Parse(text).PointsMultiplier);
    }

    [Theory]
    [InlineData("هدفون x2 بلوتوثی")]
    [InlineData("باشگاه مشتریان")]
    [InlineData("حراج ۲۰٪ هدفون")]
    [InlineData("امتیاز ۱ برابر")]
    [InlineData("")]
    public void No_multiplier_without_points_word_or_with_meaningless_value(string text)
    {
        Assert.Null(DesignBrief.ParseMultiplier(DesignBrief.Normalize(text)));
    }

    [Fact]
    public void Points_campaign_is_not_mistaken_for_a_club_banner()
    {
        Assert.Equal(BriefKind.Club, DesignBrief.Parse("امتیاز").Kind);
        Assert.Equal(BriefKind.Club, DesignBrief.Parse("باشگاه مشتریان").Kind);
        var campaign = DesignBrief.Parse("هدفون با امتیاز دوبرابر");
        Assert.NotEqual(BriefKind.Club, campaign.Kind);
        Assert.Equal(2, campaign.PointsMultiplier);
        // «باشگاه» صریح ← همچنان بنر باشگاه، ولی ضریب هم خوانده می‌شود.
        var club = DesignBrief.Parse("باشگاه مشتریان امتیاز دوبرابر");
        Assert.Equal(BriefKind.Club, club.Kind);
        Assert.Equal(2, club.PointsMultiplier);
    }

    // ── تصمیم قاعدهٔ واقعی ─────────────────────────────────────

    [Fact]
    public void Asking_more_than_real_discount_creates_a_category_offer()
    {
        var products = new List<IdeaProduct> { Idea("هدفون سونی", 100_000, 90_000) };
        var text = "حراج هدفون ۳۰٪";
        var intent = DesignBrief.Parse(text);
        var data = new BriefData(products, Headphones, null, "فراتک");
        var plan = DesignBrief.Plan(text, intent, data, DateTimeOffset.UtcNow);
        Assert.Equal(10, plan.MaxDiscount);

        var (offer, warnings) = DesignBrief.Offer(intent, plan, data);
        Assert.NotNull(offer);
        Assert.Empty(warnings);
        Assert.Equal(30, offer.Percent);
        Assert.Null(offer.Points);
        Assert.Equal(CampaignOfferScope.Category, offer.Scope);
        Assert.Equal(Headphones.Id, offer.ScopeId);
        Assert.Equal("هدفون", offer.ScopeName);
    }

    [Fact]
    public void Real_discount_that_already_covers_the_claim_needs_no_new_rule()
    {
        var products = new List<IdeaProduct> { Idea("هدفون سونی", 100_000, 60_000) };
        var text = "حراج هدفون ۳۰٪";
        var intent = DesignBrief.Parse(text);
        var data = new BriefData(products, Headphones, null, "فراتک");
        var plan = DesignBrief.Plan(text, intent, data, DateTimeOffset.UtcNow);
        var (offer, _) = DesignBrief.Offer(intent, plan, data);
        // تخفیف روی تخفیف = ضرر ← قاعدهٔ تازه ساخته نمی‌شود.
        Assert.Null(offer);
    }

    [Fact]
    public void Percent_and_multiplier_are_capped_with_warnings()
    {
        var text = "حراج همه چیز ۸۰٪ با امتیاز ۹ برابر";
        var intent = DesignBrief.Parse(text);
        var data = new BriefData(new List<IdeaProduct> { Idea("کالا", 100, 100) }, null, null, "فراتک");
        var plan = DesignBrief.Plan(text, intent, data, DateTimeOffset.UtcNow);
        var (offer, warnings) = DesignBrief.Offer(intent, plan, data);
        Assert.NotNull(offer);
        Assert.Equal(DesignBrief.MaxOfferPercent, offer.Percent);
        Assert.Equal(DesignBrief.MaxPointsMultiplier, offer.Points);
        Assert.Equal(CampaignOfferScope.All, offer.Scope);
        Assert.Null(offer.ScopeId);
        Assert.Equal(2, warnings.Count);
    }

    [Fact]
    public void Gift_card_and_blog_never_get_a_rule_and_club_gets_points_only()
    {
        var data = new BriefData(new List<IdeaProduct> { Idea("هدفون", 100, 100) }, null, null, "فراتک");
        foreach (var text in new[] { "کارت هدیه ۲۰٪ امتیاز دوبرابر", "مقاله راهنمای خرید ۲۰٪" })
        {
            var intent = DesignBrief.Parse(text);
            var (offer, _) = DesignBrief.Offer(intent, DesignBrief.Plan(text, intent, data, DateTimeOffset.UtcNow), data);
            Assert.Null(offer);
        }

        var clubText = "باشگاه مشتریان ۲۰٪ امتیاز دوبرابر";
        var clubIntent = DesignBrief.Parse(clubText);
        var (club, _) = DesignBrief.Offer(clubIntent, DesignBrief.Plan(clubText, clubIntent, data, DateTimeOffset.UtcNow), data);
        Assert.NotNull(club);
        Assert.Null(club.Percent);
        Assert.Equal(2, club.Points);
    }

    [Fact]
    public void No_numbers_means_no_rule()
    {
        var text = "حراج هدفون";
        var intent = DesignBrief.Parse(text);
        var data = new BriefData(new List<IdeaProduct> { Idea("هدفون سونی", 100_000, 90_000) }, Headphones, null, "فراتک");
        var (offer, warnings) = DesignBrief.Offer(intent, DesignBrief.Plan(text, intent, data, DateTimeOffset.UtcNow), data);
        Assert.Null(offer);
        Assert.Empty(warnings);
    }

    // ── متن بنر با قاعده ───────────────────────────────────────

    [Fact]
    public void Plan_with_offer_tells_the_real_rule_with_code_and_warns_it_activates_on_publish()
    {
        var products = new List<IdeaProduct> { Idea("هدفون سونی", 100_000, 90_000) };
        var text = "حراج هدفون ۳۰٪ با امتیاز دوبرابر";
        var intent = DesignBrief.Parse(text);
        var data = new BriefData(products, Headphones, null, "فراتک");
        var plan0 = DesignBrief.Plan(text, intent, data, DateTimeOffset.UtcNow);
        // بدون قاعده: عدد واقعی و هشدار «بیشترین تخفیف واقعی».
        Assert.Contains(DesignTemplates.FaDigits("10"), plan0.Badge);
        Assert.Contains(plan0.Warnings, w => w.Contains("واقعی", StringComparison.Ordinal));

        var (offer, _) = DesignBrief.Offer(intent, plan0, data);
        var plan = DesignBrief.Plan(text, intent, data, DateTimeOffset.UtcNow, offer! with { Code = "CMPABCDE" });
        Assert.Equal(DesignTemplates.FaDigits("30%") + " تخفیف", plan.Badge);
        Assert.Contains("CMPABCDE", plan.Subline);
        Assert.Contains("برابر", plan.Subline);
        Assert.DoesNotContain(plan.Warnings, w => w.Contains("بیشترین تخفیف واقعی", StringComparison.Ordinal));
        Assert.Contains(plan.Warnings, w => w.Contains("انتشار بنر", StringComparison.Ordinal));
        Assert.NotNull(plan.Offer);

        var ideas = DesignBrief.Variants(plan, null, null);
        Assert.All(ideas, i => Assert.Equal(plan.Badge, i.Spec.Badge));
        Assert.Single(ideas.Select(i => DesignBrief.VariantGroup(i.Key)).Distinct());
    }

    [Fact]
    public void Points_only_offer_shows_points_badge()
    {
        var text = "هدفون با امتیاز سه برابر";
        var intent = DesignBrief.Parse(text);
        var data = new BriefData(new List<IdeaProduct> { Idea("هدفون سونی", 100_000, 100_000) }, Headphones, null, "فراتک");
        var plan0 = DesignBrief.Plan(text, intent, data, DateTimeOffset.UtcNow);
        var (offer, _) = DesignBrief.Offer(intent, plan0, data);
        Assert.NotNull(offer);
        var plan = DesignBrief.Plan(text, intent, data, DateTimeOffset.UtcNow, offer);
        Assert.Equal("امتیاز " + DesignTemplates.FaDigits("3") + " برابر", plan.Badge);
        Assert.DoesNotContain("کد", plan.Subline ?? "");
    }

    // ── انتخاب کالا از فهرست ───────────────────────────────────

    [Fact]
    public void Picked_product_becomes_the_hero_and_scope()
    {
        var pickedId = Guid.NewGuid();
        var products = new List<IdeaProduct> { Idea("هدفون پرفروش", 100_000, 90_000), Idea("اسپیکر انتخابی", 200_000, 200_000, id: pickedId) };
        var text = "پیشنهاد ویژه ۱۵٪";
        var intent = DesignBrief.Parse(text) with { ProductId = pickedId };
        var data = new BriefData(products, null, null, "فراتک");
        var plan = DesignBrief.Plan(text, intent, data, DateTimeOffset.UtcNow);
        Assert.Equal(pickedId, plan.Hero?.Id);
        Assert.StartsWith("/product/", plan.LinkUrl, StringComparison.Ordinal);

        var (offer, _) = DesignBrief.Offer(intent, plan, data);
        Assert.NotNull(offer);
        Assert.Equal(CampaignOfferScope.Product, offer.Scope);
        Assert.Equal(pickedId, offer.ScopeId);
        Assert.Equal(0, offer.ExistingDiscount);
    }

    [Fact]
    public void Picked_product_that_is_out_of_stock_is_reported_not_used()
    {
        var pickedId = Guid.NewGuid();
        var products = new List<IdeaProduct> { Idea("هدفون", 100_000, 90_000), Idea("ناموجود", 100_000, 100_000, stock: 0, id: pickedId) };
        var intent = DesignBrief.Parse("معرفی کالا") with { ProductId = pickedId };
        var plan = DesignBrief.Plan("معرفی کالا", intent, new BriefData(products, null, null, "فراتک"), DateTimeOffset.UtcNow);
        Assert.NotEqual(pickedId, plan.Hero?.Id);
        Assert.Contains(plan.Warnings, w => w.Contains("انتخاب‌شده", StringComparison.Ordinal));
    }

    [Fact]
    public void Picked_discounted_product_keeps_its_existing_discount_for_coupon_math()
    {
        var pickedId = Guid.NewGuid();
        var products = new List<IdeaProduct> { Idea("اسپیکر", 100_000, 90_000, id: pickedId) };
        var text = "حراج ۳۰٪";
        var intent = DesignBrief.Parse(text) with { ProductId = pickedId };
        var data = new BriefData(products, null, null, "فراتک");
        var plan = DesignBrief.Plan(text, intent, data, DateTimeOffset.UtcNow);
        var (offer, _) = DesignBrief.Offer(intent, plan, data);
        Assert.NotNull(offer);
        Assert.Equal(10, offer.ExistingDiscount);
        Assert.Equal(23, CampaignOffers.CouponPercentFor(offer.Percent!.Value, offer.ExistingDiscount));
    }

    // ── حساب درصد کوپن و متن ───────────────────────────────────

    [Theory]
    [InlineData(30, 0, 30)]
    [InlineData(30, 10, 23)]
    [InlineData(50, 40, 17)]
    [InlineData(10, 20, 1)]
    [InlineData(10, 10, 1)]
    [InlineData(0, 0, 1)]
    public void Coupon_percent_makes_the_total_discount_at_least_the_claim(int total, int existing, int expected)
    {
        var coupon = CampaignOffers.CouponPercentFor(total, existing);
        Assert.Equal(expected, coupon);
        if (existing < total && total > 0)
        {
            // جمع واقعی دو تخفیف ≥ ادعا.
            var combined = 100m - (100m - existing) * (100m - coupon) / 100m;
            Assert.True(combined >= total, $"{combined} < {total}");
        }
    }

    [Fact]
    public void Describe_reads_naturally()
    {
        Assert.Equal("تخفیف " + DesignTemplates.FaDigits("20%") + " (کد CMPX) و امتیاز " + DesignTemplates.FaDigits("2") + " برابر",
            CampaignOffers.Describe(20, 2m, "CMPX"));
        Assert.Equal("امتیاز " + DesignTemplates.FaDigits("3") + " برابر", CampaignOffers.Describe(null, 3m, null));
        Assert.Equal("", CampaignOffers.Describe(null, null, null));
    }
}
