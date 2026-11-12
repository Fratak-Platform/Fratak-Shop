namespace Fratak_Shop.Core.Services.Learning;

/// <summary>
/// دور ۲۴: رگرسیون لجستیک کوچک و داخلی (بدون کتابخانه) برای پیش‌بینی «احتمال یک رخداد بله/خیر» از چند ویژگی عددی.
/// <para>آموزش با گرادیان کاهشی دسته‌ای و جریمهٔ L2 (جلوگیری از بیش‌برازش روی دادهٔ کم)؛ وزن کلاس کمیاب متعادل می‌شود
/// تا مدل فقط «همیشه خوب» نگوید.</para>
/// <para>مثال: ویژگی‌های [حساب تازه، پرداخت در محل] و برچسب «مرجوعی/لغو پس از پرداخت» ← وزن هر ویژگی و احتمال.</para>
/// <para>محل استفاده: OrderRiskService (ریسک سفارش)، ChurnService (احتمال ریزش مشتری).</para>
/// </summary>
public sealed class LogisticModel
{
    /// <summary>عرض از مبدأ (bias).</summary>
    public double Intercept { get; }

    /// <summary>وزن هر ویژگی (هم‌ترتیب با بردار ورودی).</summary>
    public IReadOnlyList<double> Weights { get; }

    /// <summary>نسبت برچسب مثبت در دادهٔ آموزش (نرخ پایه).</summary>
    public double BaseRate { get; }

    /// <summary>ساخت مدل با وزن‌های معلوم (مثلاً وزن‌های پیش‌فرض قانون‌محور وقتی داده کم است).</summary>
    public LogisticModel(double intercept, IReadOnlyList<double> weights, double baseRate)
    {
        Intercept = intercept;
        Weights = weights;
        BaseRate = Math.Clamp(baseRate, 0.0001, 0.9999);
    }

    /// <summary>تابع سیگموید. مثال: Sigmoid(0)=۰٫۵.</summary>
    public static double Sigmoid(double z) => z >= 0 ? 1 / (1 + Math.Exp(-z)) : Math.Exp(z) / (1 + Math.Exp(z));

    /// <summary>احتمال برچسب مثبت برای یک بردار ویژگی (طول بردار باید با وزن‌ها برابر باشد؛ اضافه نادیده گرفته می‌شود).</summary>
    public double Predict(IReadOnlyList<double> features) => Sigmoid(Logit(features));

    /// <summary>سهم هر ویژگی در نتیجه (وزن × مقدار) برای توضیح «چرا». مثال: [۱٫۰، ۰، ۰٫۸].</summary>
    public double[] Contributions(IReadOnlyList<double> features) =>
        Enumerable.Range(0, Weights.Count).Select(i => Weights[i] * (i < features.Count ? features[i] : 0)).ToArray();

    private double Logit(IReadOnlyList<double> features)
    {
        var z = Intercept;
        for (var i = 0; i < Weights.Count && i < features.Count; i++)
            z += Weights[i] * features[i];
        return z;
    }

    /// <summary>
    /// آموزش. هر کلاس باید دست‌کم <paramref name="minPerClass"/> نمونه داشته باشد وگرنه null (یادگیری قابل اعتماد نیست).
    /// <paramref name="l2"/> شدت جریمه، <paramref name="epochs"/> تعداد دور. حداکثر ۲۰ هزار نمونه.
    /// </summary>
    public static LogisticModel? Train(IReadOnlyList<(double[] Features, bool Label)> samples, int minPerClass = 20, double l2 = 0.05,
        int epochs = 400, double learningRate = 0.3)
    {
        var data = samples.Where(s => s.Features.Length > 0).Take(20_000).ToList();
        var positives = data.Count(s => s.Label);
        var negatives = data.Count - positives;
        if (positives < minPerClass || negatives < minPerClass)
            return null;
        var width = data.Max(s => s.Features.Length);
        // وزن کلاس‌ها: هر کلاس روی هم نیمی از اثر را دارد.
        var positiveWeight = data.Count / (2.0 * positives);
        var negativeWeight = data.Count / (2.0 * negatives);
        var weights = new double[width];
        var intercept = 0.0;
        for (var epoch = 0; epoch < epochs; epoch++)
        {
            var gradient = new double[width];
            var gradientIntercept = 0.0;
            foreach (var (features, label) in data)
            {
                var z = intercept;
                for (var i = 0; i < features.Length; i++)
                    z += weights[i] * features[i];
                var error = (Sigmoid(z) - (label ? 1 : 0)) * (label ? positiveWeight : negativeWeight);
                gradientIntercept += error;
                for (var i = 0; i < features.Length; i++)
                    gradient[i] += error * features[i];
            }
            intercept -= learningRate * gradientIntercept / data.Count;
            for (var i = 0; i < width; i++)
                weights[i] -= learningRate * (gradient[i] / data.Count + l2 * weights[i]);
        }
        // برگرداندن عرض از مبدأ به نرخ واقعی (وزن‌دهی کلاس احتمال‌ها را به ۵۰٪ متمایل کرده بود).
        var baseRate = positives / (double)data.Count;
        intercept += Math.Log(baseRate / (1 - baseRate));
        return new LogisticModel(intercept, weights, baseRate);
    }
}
