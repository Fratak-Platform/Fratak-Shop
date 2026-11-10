using Fratak_Shop.Core.Services.Insights;
using Fratak_Shop.Core.Services.Learning;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Entities.Orders;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست واحد دور ۲۴ بستهٔ D (یادگیری ماشین داخلی): پیش‌بینی تقاضا، شباهت کالا، نظر دوم دسته‌بندی، تحلیل احساس،
/// رگرسیون لجستیک و ریسک سفارش، زمان تحویل، تشخیص ناهنجاری، ریزش مشتری و کیفیت تصویر.
/// ترتیب هر بخش: حالت درست ← مرزها ← ورودی خراب/حمله‌ای (نباید استثنا یا خروجی بی‌معنا بدهد).
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "ML")]
public class MlRound24Tests
{
    // ── ۵۱ پیش‌بینی تقاضا ───────────────────────────────────────────

    [Fact]
    public void Forecast_constant_series_is_exact_and_prefers_learned_model()
    {
        var result = DemandForecaster.Forecast(Enumerable.Repeat(2, 56).ToList());
        Assert.Equal(2m, result.DailyRate);
        Assert.Equal("holt", result.Method);
        Assert.Equal(0m, result.Error);
    }

    [Fact]
    public void Forecast_follows_upward_trend_more_than_plain_mean()
    {
        var series = Enumerable.Range(0, 56).Select(i => 1 + i / 8).ToList(); // ۱ تا ۷ پله‌ای
        var result = DemandForecaster.Forecast(series);
        Assert.True(result.DailyRate > (decimal)series.Average(), $"rate {result.DailyRate} via {result.Method}");
    }

    [Fact]
    public void Forecast_intermittent_demand_gives_small_positive_rate()
    {
        var series = Enumerable.Range(0, 60).Select(i => i % 10 == 9 ? 2 : 0).ToList();
        var result = DemandForecaster.Forecast(series);
        Assert.InRange(result.DailyRate, 0.05m, 0.5m);
    }

    [Fact]
    public void Forecast_edge_cases_never_negative_or_throw()
    {
        Assert.Equal("none", DemandForecaster.Forecast([]).Method);
        Assert.Equal(0m, DemandForecaster.Forecast([0, 0, 0, -5]).DailyRate);
        var shortSeries = DemandForecaster.Forecast([1, 2, 3]);
        Assert.Equal("mean", shortSeries.Method);
        Assert.Equal(2m, shortSeries.DailyRate);
        var falling = DemandForecaster.Forecast(Enumerable.Range(0, 40).Select(i => Math.Max(0, 40 - i * 2)).ToList());
        Assert.True(falling.DailyRate >= 0);
        Assert.True(DemandForecaster.Forecast(Enumerable.Repeat(int.MaxValue / 1000, 30).ToList()).DailyRate > 0);
    }

    [Fact]
    public void EstimateSeries_and_FromRate_levels()
    {
        var soon = StockForecastRules.EstimateSeries(10, Enumerable.Repeat(2, 56).ToList());
        Assert.Equal(StockForecastLevel.Soon, soon.Level);
        Assert.Equal(5.0m, soon.DaysLeft);
        Assert.Equal(32, soon.SuggestedReorder);
        Assert.Equal("holt", soon.Method);

        Assert.Equal(StockForecastLevel.NoSales, StockForecastRules.EstimateSeries(10, Enumerable.Repeat(0, 30).ToList()).Level);
        Assert.Equal(StockForecastLevel.NoSales, StockForecastRules.EstimateSeries(10, []).Level);

        var oos = StockForecastRules.FromRate(0, 1.5m);
        Assert.Equal(StockForecastLevel.OutOfStock, oos.Level);
        Assert.Equal(32, oos.SuggestedReorder);
        var ok = StockForecastRules.FromRate(100, 1m);
        Assert.Equal(StockForecastLevel.Ok, ok.Level);
        Assert.Equal(0, ok.SuggestedReorder);
        Assert.Equal(StockForecastLevel.Critical, StockForecastRules.FromRate(3, 1m).Level);
        Assert.Equal(StockForecastLevel.NoSales, StockForecastRules.FromRate(5, -1m).Level);
        Assert.Equal(StockForecastLevel.OutOfStock, StockForecastRules.FromRate(-4, 1m).Level);
    }

    // ── ۵۲ شباهت کالا ────────────────────────────────────────────────

    [Fact]
    public void ItemSimilarity_prefers_specific_companions_over_popular_items()
    {
        var niche = Guid.NewGuid();
        var popular = Guid.NewGuid();
        var co = new Dictionary<Guid, int> { [niche] = 5, [popular] = 8 };
        var totals = new Dictionary<Guid, int> { [niche] = 5, [popular] = 1000 };
        Assert.Equal(new List<Guid> { niche, popular }, ItemSimilarity.Rank(co, totals, 10, 5));
        Assert.Single(ItemSimilarity.Rank(co, totals, 10, 1));
        Assert.Empty(ItemSimilarity.Rank(co, totals, 10, -3));
        Assert.Equal(0, ItemSimilarity.Score(0, 10, 10));
        Assert.True(ItemSimilarity.Score(5, 5, 10) > ItemSimilarity.Score(8, 1000, 10));
        // تعداد کل کمتر از هم‌خرید (دادهٔ ناسازگار) ← خطا نمی‌دهد.
        Assert.Single(ItemSimilarity.Rank(new Dictionary<Guid, int> { [niche] = 3 }, new Dictionary<Guid, int> { [niche] = 1 }, 0, 3));
    }

    // ── ۵۳ نظر دوم دسته‌بندی ─────────────────────────────────────────

    [Fact]
    public void CategoryConsensus_veto_and_agreement()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var confident = new CategoryGuess(a, 0.6, 0.2);
        var weak = new CategoryGuess(a, 0.25, 0.05);
        Assert.Equal(a, CategoryConsensus.Decide(confident, null));
        Assert.Equal(a, CategoryConsensus.Decide(confident, new TextPrediction(a.ToString("N"), 0.99)));
        Assert.Null(CategoryConsensus.Decide(confident, new TextPrediction(b.ToString("N"), 0.95)));
        Assert.Equal(a, CategoryConsensus.Decide(confident, new TextPrediction(b.ToString("N"), 0.6)));
        Assert.Equal(a, CategoryConsensus.Decide(weak, new TextPrediction(a.ToString("N"), 0.85)));
        Assert.Null(CategoryConsensus.Decide(weak, new TextPrediction(a.ToString("N"), 0.5)));
        Assert.Null(CategoryConsensus.Decide(weak, null));
        Assert.Null(CategoryConsensus.Decide(null, new TextPrediction(a.ToString("N"), 0.99)));
        Assert.Equal(a, CategoryConsensus.Decide(confident, new TextPrediction("not-a-guid", 0.99)));
    }

    [Fact]
    public void CategoryConsensus_trains_only_with_two_categories()
    {
        var phones = Guid.NewGuid();
        var cables = Guid.NewGuid();
        Assert.Null(CategoryConsensus.TrainSecondOpinion([("گوشی سامسونگ", phones), ("گوشی شیائومی", phones)]));
        var model = CategoryConsensus.TrainSecondOpinion([
            ("گوشی موبایل سامسونگ", phones), ("گوشی موبایل شیائومی", phones), ("گوشی موبایل اپل", phones),
            ("کابل شارژ تایپ سی", cables), ("کابل شارژ لایتنینگ", cables), ("کابل شارژ میکرو", cables),
        ]);
        Assert.NotNull(model);
        Assert.Equal(cables.ToString("N"), CategoryConsensus.Ask(model, "کابل شارژ انکر")?.Label);
        Assert.Null(CategoryConsensus.Ask(null, "کابل"));
    }

    // ── ۵۴ تحلیل احساس دیدگاه ───────────────────────────────────────

    [Fact]
    public void Sentiment_understands_negation_rating_and_recommendation()
    {
        Assert.Equal(SentimentLabel.Negative, ReviewSentiment.Analyze("خوب نبود").Label);
        Assert.Equal(SentimentLabel.Positive, ReviewSentiment.Analyze("عالی بود ممنون", 5).Label);
        Assert.NotEqual(SentimentLabel.Negative, ReviewSentiment.Analyze("مشکل نداشت").Label);
        Assert.Equal(SentimentLabel.Negative, ReviewSentiment.Analyze(null, 1).Label);
        Assert.Equal(SentimentLabel.Neutral, ReviewSentiment.Analyze("", 3).Label);
        Assert.Equal(-0.3, ReviewSentiment.Analyze("", null, false).Score);
        Assert.True(ReviewSentiment.Analyze("خیلی بد", null).Score < 0);
        Assert.Contains("کیفیت", ReviewSentiment.Analyze("کالا معیوب بود و خراب شد").Topics);
        Assert.Contains("ارسال", ReviewSentiment.Analyze("ارسال با تاخیر زیاد").Topics);
    }

    [Fact]
    public void Sentiment_hostile_input_is_bounded()
    {
        var spam = string.Concat(Enumerable.Repeat("افتضاح ", 2000));
        var result = ReviewSentiment.Analyze(spam + "<script>alert(1)</script>", 9);
        Assert.InRange(result.Score, -1, 1);
        Assert.Equal(SentimentLabel.Negative, result.Label);
        Assert.Equal(SentimentLabel.Neutral, ReviewSentiment.Analyze("؟؟؟ !!! ...").Label);
    }

    [Fact]
    public void Sentiment_model_needs_ten_examples_per_side()
    {
        var few = Enumerable.Range(0, 9).Select(i => ("کالا خراب و بد بود " + i, (byte)1))
            .Concat(Enumerable.Range(0, 20).Select(i => ("کالا عالی و سالم بود " + i, (byte)5)));
        Assert.Null(ReviewSentiment.Train(few));
        var enough = Enumerable.Range(0, 12).Select(i => ("کالا شکسته رسید جنسش ضعیف " + i, (byte)1))
            .Concat(Enumerable.Range(0, 12).Select(i => ("کالا شیک و تمیز رسید " + i, (byte)5)))
            .Concat(Enumerable.Range(0, 30).Select(i => ("متوسط " + i, (byte)3)));
        var model = ReviewSentiment.Train(enough);
        Assert.NotNull(model);
        Assert.True(ReviewSentiment.Analyze("شکسته رسید جنسش ضعیف", null, null, model).Score < 0);
    }

    // ── ۵۵ رگرسیون لجستیک و ریسک سفارش ──────────────────────────────

    [Fact]
    public void Logistic_sigmoid_and_training()
    {
        Assert.Equal(0.5, LogisticModel.Sigmoid(0), 6);
        Assert.InRange(LogisticModel.Sigmoid(-1000), 0, 1e-10);
        Assert.InRange(LogisticModel.Sigmoid(1000), 1 - 1e-10, 1);

        var samples = Enumerable.Range(0, 60).Select(i => (Features: new[] { i % 2 == 0 ? 1.0 : 0.0, 0.5 }, Label: i % 2 == 0)).ToList();
        var model = LogisticModel.Train(samples);
        Assert.NotNull(model);
        Assert.True(model!.Predict([1, 0.5]) > 0.6);
        Assert.True(model.Predict([0, 0.5]) < 0.4);
        Assert.Equal(0.5, model.BaseRate, 3);

        var fewPositives = samples.Where(s => !s.Label).Concat(samples.Where(s => s.Label).Take(19)).ToList();
        Assert.Null(LogisticModel.Train(fewPositives));
        Assert.Null(LogisticModel.Train([]));
    }

    private static OrderRiskSignals Signals(double ageHours = 1000, int prior = 5, int bad = 0, decimal amount = 500_000, decimal typical = 500_000,
        bool cod = false, bool coupon = false, int hour = 14) =>
        new(ageHours, prior, bad, amount, typical, coupon, false, hour, cod, 0);

    [Fact]
    public void OrderRisk_levels_follow_signals()
    {
        var model = OrderRiskRules.Default;
        var returning = OrderRiskRules.Assess(Signals(), model, false);
        Assert.Equal(OrderRiskLevel.Low, returning.Level);
        Assert.InRange(returning.Percent, 4, 6);
        Assert.Empty(returning.Reasons);

        var fresh = OrderRiskRules.Assess(Signals(ageHours: 2, prior: 0), model, false);
        Assert.Equal(OrderRiskLevel.Low, fresh.Level);
        Assert.InRange(fresh.Percent, 10, 12);

        var cod = OrderRiskRules.Assess(Signals(ageHours: 2, prior: 0, cod: true), model, false);
        Assert.Equal(OrderRiskLevel.Medium, cod.Level);
        Assert.InRange(cod.Percent, 24, 26);

        var big = OrderRiskRules.Assess(Signals(ageHours: 2, prior: 0, cod: true, amount: 1_500_000), model, true);
        Assert.Equal(OrderRiskLevel.High, big.Level);
        Assert.InRange(big.Percent, 43, 46);
        Assert.Equal("پرداخت در محل", big.Reasons[0]);
        Assert.Contains("مبلغ بالاتر از معمول", big.Reasons);
        Assert.True(big.Learned);
    }

    [Fact]
    public void OrderRisk_features_and_bad_outcome()
    {
        var features = OrderRiskRules.Features(Signals(prior: 2, bad: 2, hour: 3, amount: 100, typical: 0));
        Assert.Equal(9, features.Length);
        Assert.Equal(0.5, features[2]);
        Assert.Equal(0, features[3]);
        Assert.Equal(1, features[6]);
        // مبلغ کمتر از معمول امتیاز منفی نمی‌گیرد؛ مبلغ نجومی سقف دارد.
        Assert.Equal(0, OrderRiskRules.Features(Signals(amount: 10, typical: 1_000_000))[3]);
        Assert.Equal(2.5, OrderRiskRules.Features(Signals(amount: 1_000_000_000_000, typical: 1))[3]);

        Assert.True(OrderRiskRules.IsBad(OrderStatus.Returned, null, null));
        Assert.True(OrderRiskRules.IsBad(OrderStatus.Cancelled, DateTimeOffset.UtcNow, null));
        Assert.False(OrderRiskRules.IsBad(OrderStatus.Cancelled, null, null));
        Assert.False(OrderRiskRules.IsBad(OrderStatus.Delivered, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow));
    }

    // ── ۵۹ زمان تحویل یادگرفته ──────────────────────────────────────

    [Fact]
    public void DeliveryEta_quantiles_and_fallback()
    {
        Assert.Equal(2.5, DeliveryEta.Quantile([1, 2, 3, 4], 0.5), 6);
        Assert.Equal(0.0, DeliveryEta.Quantile([], 0.5));
        double[] hours = [24, 30, 48, 50, 70, 72, 90, 96];
        Assert.Equal<(int Min, int Max)?>((2, 4), DeliveryEta.FromHistory(hours, 8));
        Assert.Null(DeliveryEta.FromHistory(hours, 9));
        // دادهٔ خراب (منفی/بیش از ۶۰ روز) کنار گذاشته می‌شود.
        Assert.Null(DeliveryEta.FromHistory([-5, 0, 99_999, 24], 2));

        var many = Enumerable.Range(0, 25).Select(i => 48.0 + i).ToList();
        Assert.Equal(DeliveryEta.FromHistory(many, DeliveryEta.MinAllSamples), DeliveryEta.Pick([24], [24, 30], many));
        Assert.Equal<(int Min, int Max)?>((2, 4), DeliveryEta.Pick(hours, many, many));
        Assert.Null(DeliveryEta.Pick([], [], [24]));
    }

    // ── ۵۶ تشخیص ناهنجاری ──────────────────────────────────────────

    [Fact]
    public void Anomaly_detects_drop_and_spike_but_not_normal_days()
    {
        var history = Enumerable.Repeat(20.0, 30).ToList();
        Assert.Equal(AnomalyDirection.Drop, AnomalyDetector.Check(history, 2).Direction);
        Assert.Equal(AnomalyDirection.Spike, AnomalyDetector.Check(history, 60).Direction);
        Assert.Equal(AnomalyDirection.None, AnomalyDetector.Check(history, 22).Direction);
        Assert.Equal(AnomalyDirection.None, AnomalyDetector.Check(history.Take(13).ToList(), 0).Direction);
        Assert.Equal(AnomalyDirection.None, AnomalyDetector.Check(Enumerable.Repeat(1.0, 30).ToList(), 50).Direction);
    }

    [Fact]
    public void Anomaly_learns_weekly_pattern()
    {
        // ۲۶ روز: روز «۵» هر هفته کم‌فروش (۸)، بقیه ۲۰؛ روز بعد (اندیس ۲۶) دوباره روز ۵ است.
        var history = Enumerable.Range(0, 26).Select(i => i % 7 == 5 ? 8.0 : 20.0).ToList();
        var weekdays = Enumerable.Range(0, 27).Select(i => i % 7).ToList();
        Assert.Equal(AnomalyDirection.None, AnomalyDetector.Check(history, 8, weekdays).Direction);
        Assert.Equal(AnomalyDirection.Spike, AnomalyDetector.Check(history, 40, weekdays).Direction);
        // آرایهٔ روزهای ناهم‌طول نادیده گرفته می‌شود (خطا نمی‌دهد).
        Assert.Equal(AnomalyDirection.Drop, AnomalyDetector.Check(Enumerable.Repeat(20.0, 30).ToList(), 2, [1, 2]).Direction);
    }

    [Fact]
    public void AnomalyService_title()
    {
        var metric = new DailyMetric("orders", "تعداد سفارش", [], 0, [], [AnomalyDirection.Drop], "/admin/orders", "orders.view");
        Assert.Equal("افت غیرعادی تعداد سفارش (۱۴۰۵/۰۸/۰۱)",
            AnomalyService.Title(metric, new AnomalyResult(2, 20, -6, AnomalyDirection.Drop), "۱۴۰۵/۰۸/۰۱"));
        Assert.StartsWith("افزایش", AnomalyService.Title(metric, new AnomalyResult(60, 20, 9, AnomalyDirection.Spike), "x"));
    }

    // ── ۵۷ ریزش مشتری ──────────────────────────────────────────────

    [Fact]
    public void Churn_expected_gap_probability_and_cap()
    {
        var now = DateTimeOffset.UtcNow;
        Assert.Equal(60, ChurnModel.ExpectedGap(1, now, now, 60));
        Assert.Equal(40, ChurnModel.ExpectedGap(5, now.AddDays(-120), now, 60), 6);
        Assert.Equal(7, ChurnModel.ExpectedGap(1, now, now, 1));
        Assert.Equal(365, ChurnModel.ExpectedGap(1, now, now, 10_000));
        Assert.Equal(7, ChurnModel.ExpectedGap(50, now.AddDays(-1), now, 7));

        Assert.Equal(0.393, ChurnModel.Probability(30, 30), 3);
        Assert.Equal(0.865, ChurnModel.Probability(60, 30), 3);
        Assert.Equal(0, ChurnModel.Probability(0, 30));
        Assert.Equal(0, ChurnModel.Probability(30, 0));
        Assert.True(ChurnModel.Probability(120, 30) >= ChurnModel.WinBackThreshold);

        Assert.Equal(80_000m, ChurnModel.CapFor(10, 800_000, 200_000));
        Assert.Equal(200_000m, ChurnModel.CapFor(10, 5_000_000, 200_000));
        Assert.Equal(10_000m, ChurnModel.CapFor(10, 1_000, 200_000));
        Assert.Equal(10_000m, ChurnModel.CapFor(90, -5, -1));
        Assert.Equal(250_000m, ChurnModel.CapFor(90, 500_000, 1_000_000)); // درصد حداکثر ۵۰
    }

    [Fact]
    public void Churn_insight_flags()
    {
        var insight = new ChurnInsight(0.82, 30, 95, []);
        Assert.Equal(82, insight.Percent);
        Assert.True(insight.AtRisk);
        Assert.False(new ChurnInsight(0.2, 30, 10, []).AtRisk);
    }

    // ── ۵۸ کیفیت تصویر ─────────────────────────────────────────────

    private static byte[] Image(int size, Func<int, int, int> pixel)
    {
        var gray = new byte[size * size];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                gray[y * size + x] = (byte)Math.Clamp(pixel(x, y), 0, 255);
        return gray;
    }

    [Fact]
    public void ImageQuality_sharp_product_on_white_beats_blurry_dark_photo()
    {
        // کالای تیز (شطرنجی پرجزئیات) وسط زمینهٔ سفید.
        var sharp = Image(200, (x, y) => x is < 30 or >= 170 || y is < 30 or >= 170 ? 250 : ((x / 3 + y / 3) % 2 == 0 ? 40 : 200));
        // عکس تار و تیره (شیب ملایم بدون لبه).
        var blurry = Image(200, (x, y) => 40 + (x + y) / 20);
        var good = ImageQuality.Score(sharp, 200, 200, 1200);
        var bad = ImageQuality.Score(blurry, 200, 200, 600);
        Assert.True(good.Total >= 75, $"good {good}");
        Assert.True(bad.Total <= 45, $"bad {bad}");
        Assert.True(good.Sharpness > bad.Sharpness);
        Assert.True(good.Background > bad.Background);
        Assert.Equal(1, good.Resolution);
        Assert.InRange(bad.Resolution, 0.33, 0.34);
    }

    [Fact]
    public void ImageQuality_invalid_input_scores_zero()
    {
        Assert.Equal(0, ImageQuality.Score([], 0, 0, 1000).Total);
        Assert.Equal(0, ImageQuality.Score(new byte[10], 100, 100, 1000).Total);
        Assert.Equal(0, ImageQuality.Score(new byte[4], 2, 2, 1000).Total);
        var flat = ImageQuality.Score(Image(50, (_, _) => 255), 50, 50, 50);
        Assert.InRange(flat.Total, 0, 100);
        Assert.Equal(0, flat.Sharpness);
        Assert.Equal(0, flat.Resolution);
    }

    [Fact]
    public void ImageQuality_pick_primary_is_conservative()
    {
        var primary = Guid.NewGuid();
        var other = Guid.NewGuid();
        Assert.Equal(other, ImageQuality.PickPrimary([(primary, true, 30), (other, false, 75)]));
        Assert.Null(ImageQuality.PickPrimary([(primary, true, 60), (other, false, 95)]));
        Assert.Null(ImageQuality.PickPrimary([(primary, true, 40), (other, false, 55)]));
        Assert.Null(ImageQuality.PickPrimary([(primary, true, null), (other, false, 95)]));
        Assert.Null(ImageQuality.PickPrimary([(primary, true, 20), (other, false, null)]));
        Assert.Equal(other, ImageQuality.PickPrimary([(primary, false, 50), (other, false, 70)]));
        Assert.Null(ImageQuality.PickPrimary([]));
    }
}
