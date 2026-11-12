using System.Text.RegularExpressions;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Seo.Knowledge;

namespace Fratak_Shop.Core.Services.Seo.Smart;

/// <summary>
/// موتور «پیشنهاد هوشمند» متن (مرحلهٔ شانزدهم) — رایگان، داخلی و قطعی (همان ورودی همیشه همان خروجی را می‌دهد).
/// <para>برای هر نوع فیلد (<see cref="SmartPurpose"/>) چند نسخه می‌سازد و بر اساس هدف (<see cref="SmartGoal"/>) مرتب می‌کند:</para>
/// <list type="bullet">
/// <item>clean (ویرایش): املا، نیم‌فاصله، رسمی‌نویسی، نشانه‌گذاری و حذف حشو با <see cref="PersianWriting"/>.</item>
/// <item>concise (روان‌تر): ویرایش + حذف همهٔ عبارت‌های زائد و شکستن جمله‌های خیلی بلند.</item>
/// <item>structured (ساختار): برای متن بلند — تیتر H2، فهرست، پاراگراف کوتاه‌تر و پررنگ کردن نکته‌ها؛ برای نام محصول — ترتیب «نوع، برند، مدل».</item>
/// <item>seo: کلمهٔ کلیدی در جای درست (ابتدای عنوان، ۶۰ نویسهٔ اول متا، پاراگراف اول و یک تیتر) بدون انباشت.</item>
/// <item>persuasive (ترغیبی): مزیت‌های واقعی فروشگاه (گارانتی، بازگشت، ارسال) و دعوت به اقدام.</item>
/// </list>
/// همهٔ نسخه‌ها از <see cref="SpamGuard"/> می‌گذرند تا هیچ پیشنهادی باعث جریمهٔ گوگل نشود.
/// استفاده: <see cref="SmartTextService"/> (صفحهٔ /admin/seo/smart?handler=Text).
/// </summary>
public static partial class TextRefiner
{
    public const string Clean = "clean";
    public const string Concise = "concise";
    public const string Structured = "structured";
    public const string Seo = "seo";
    public const string Persuasive = "persuasive";

    /// <summary>بیشترین طول ورودی (حدود ۲۰۰ هزار نویسه HTML) — جلوگیری از سوءاستفاده و کندی.</summary>
    public const int MaxInput = 200_000;

    private static readonly Dictionary<string, string> Titles = new()
    {
        [Clean] = "ویرایش و درست‌نویسی",
        [Concise] = "روان‌تر و کوتاه‌تر",
        [Structured] = "ساختاربندی و خوانایی",
        [Seo] = "بهینه برای گوگل (سئو)",
        [Persuasive] = "جذاب‌تر برای مشتری",
    };

    /// <summary>
    /// ساخت پیشنهادها. خروجی حداکثر ۳ نسخهٔ متفاوت (نسخه‌های تکراری حذف می‌شوند) به‌علاوهٔ تحلیل متن فعلی.
    /// مثال: Refine(new() { Purpose = ProductName, Text = "گوشی سامسونگ a55 فوق العاده!!!", Category = "گوشی موبایل", Brand = "سامسونگ" }, kb)
    /// → «گوشی موبایل سامسونگ مدل A55» و …
    /// </summary>
    public static SmartTextResult Refine(SmartTextRequest request, SeoKnowledgeBase knowledge)
    {
        var result = new SmartTextResult();
        var text = request.Text ?? "";
        if (text.Length > MaxInput)
        {
            result.Warnings.Add("متن بیش از حد بلند است؛ فقط بخش اول بررسی شد.");
            text = text[..MaxInput];
        }
        var isHtml = request.Purpose == SmartPurpose.Body && (request.IsHtml || LooksLikeHtml(text));
        var seed = (int)(TextModels.Fnv1a(text) & 0x7FFFFFFF);
        var facts = new SmartFacts(request);
        var keyword = PersianText.Clean(request.Keyword);

        result.Before = Analyze(text, isHtml, request, knowledge);
        if (string.IsNullOrWhiteSpace(isHtml ? PersianText.HtmlToText(text) : text))
        {
            // فیلد خالی: برای نام محصول/عنوان/متا از نام و کلمهٔ کلیدی چیزی ساخته می‌شود؛ برای بقیه راهنمایی.
            if (request.Purpose is SmartPurpose.Plain or SmartPurpose.Body && string.IsNullOrWhiteSpace(request.Subject))
            {
                result.Warnings.Add("فیلد خالی است؛ ابتدا متنی بنویسید تا پیشنهاد هوشمند آن را بهتر کند.");
                return result;
            }
        }

        var candidates = request.Purpose switch
        {
            SmartPurpose.ProductName => ProductNameVariants(text, request, knowledge, facts),
            SmartPurpose.SeoTitle => TitleVariants(text, request, knowledge, facts, keyword, seed),
            SmartPurpose.MetaDescription => MetaVariants(text, request, knowledge, facts, keyword, seed),
            SmartPurpose.ShortText => ShortVariants(text, request, knowledge, facts, keyword, seed),
            SmartPurpose.Body => BodyVariants(text, isHtml, request, knowledge, facts, keyword, seed),
            _ => PlainVariants(text, request, knowledge, keyword),
        };

        foreach (var variant in candidates)
        {
            variant.Score = Analyze(variant.Text, variant.IsHtml, request, knowledge).Score;
            var spam = SpamGuard.Inspect(
                request.Purpose is SmartPurpose.SeoTitle or SmartPurpose.ProductName ? variant.Text : null,
                request.Purpose == SmartPurpose.MetaDescription ? variant.Text : null,
                variant.IsHtml ? variant.Text : null,
                keyword, knowledge);
            foreach (var issue in spam.Where(i => i.Severity >= 2))
                variant.Notes.Add("هشدار: " + issue.Message);
        }

        var order = Order(request.Purpose, request.Goal);
        var distinct = candidates
            .Where(v => !string.IsNullOrWhiteSpace(v.Text))
            .OrderBy(v => Rank(order, v.Id))
            .DistinctBy(v => PersianText.Normalize(v.IsHtml ? Compact(v.Text) : v.Text))
            .Take(3)
            .ToList();

        // اگر متن تغییری نکرد هم یک نسخه برمی‌گردد تا کاربر بداند متن مشکلی نداشت.
        foreach (var variant in distinct)
        {
            if (Same(variant.Text, text, variant.IsHtml))
                variant.Notes.Insert(0, "این متن نیاز به تغییر نداشت؛ همان متن فعلی است.");
            result.Variants.Add(variant);
        }
        return result;
    }

    /// <summary>ترتیب نسخه‌ها برای هر هدف (اولی پیش‌فرض نمایش است).</summary>
    public static IReadOnlyList<string> Order(SmartPurpose purpose, SmartGoal goal) => goal switch
    {
        SmartGoal.Seo => [Seo, Structured, Clean, Persuasive, Concise],
        SmartGoal.Persuasive => [Persuasive, Structured, Clean, Seo, Concise],
        SmartGoal.Clarity => [Clean, Concise, Structured, Seo, Persuasive],
        _ => purpose switch
        {
            SmartPurpose.Body => [Structured, Seo, Persuasive, Clean, Concise],
            SmartPurpose.ProductName => [Structured, Seo, Clean],
            SmartPurpose.SeoTitle or SmartPurpose.MetaDescription => [Seo, Persuasive, Clean],
            SmartPurpose.ShortText => [Clean, Persuasive, Seo, Concise],
            _ => [Clean, Concise, Seo],
        },
    };

    private static int Rank(IReadOnlyList<string> order, string id)
    {
        var index = order.ToList().IndexOf(id);
        return index < 0 ? 100 : index;
    }

    // ── نسخه‌ها: متن ساده ───────────────────────────────────────────

    private static SmartVariant NewVariant(string id, bool html = false) => new() { Id = id, Title = Titles.GetValueOrDefault(id, id), IsHtml = html };

    /// <summary>اعمال ویرایش نگارشی روی یک نسخه و ثبت تغییرها.</summary>
    private static string PolishInto(SmartVariant variant, string text, SeoKnowledgeBase knowledge, WritingOptions options)
    {
        var polished = PersianWriting.Polish(text, knowledge, options);
        Record(variant, polished);
        return polished.Text;
    }

    private static void Record(SmartVariant variant, WritingResult polished)
    {
        foreach (var group in polished.Changes.GroupBy(c => c.Category))
        {
            var samples = group.Where(c => c.From.Length > 0).Take(4).Select(c => c.To.Length > 0 ? $"{c.From} ← {c.To}" : $"حذف «{c.From}»").ToList();
            var counted = group.Where(c => c.From.Length == 0 && c.To.Length > 0).Select(c => c.To).ToList();
            var description = samples.Count > 0 ? string.Join("، ", samples) : counted.Count > 0 ? $"{string.Join(" + ", counted)} مورد" : "یکدست شد";
            AddChange(variant, $"{group.Key}: {description}");
        }
        foreach (var change in polished.Changes)
            if (!change.RuleId.StartsWith("b:", StringComparison.Ordinal) && !variant.RuleIds.Contains(change.RuleId))
                variant.RuleIds.Add(change.RuleId);
        foreach (var note in polished.Notes)
            if (!variant.Notes.Contains(note))
                variant.Notes.Add(note);
    }

    private static void AddChange(SmartVariant variant, string change)
    {
        if (!variant.Changes.Contains(change))
            variant.Changes.Add(change);
    }

    private static WritingOptions Options(SmartTextRequest request, bool title = false) => new()
    {
        Style = !title,
        Vocabulary = request.Vocabulary,
    };

    private static List<SmartVariant> PlainVariants(string text, SmartTextRequest request, SeoKnowledgeBase knowledge, string keyword)
    {
        var clean = NewVariant(Clean);
        clean.Text = PolishInto(clean, text, knowledge, Options(request));

        var concise = NewVariant(Concise);
        concise.Text = Shorten(PolishInto(concise, text, knowledge, Options(request)), knowledge, concise);

        var list = new List<SmartVariant> { clean, concise };
        if (keyword.Length > 0 && !KeywordMatcher.Contains(clean.Text, keyword, partial: true))
        {
            var seo = NewVariant(Seo);
            seo.Text = PolishInto(seo, text, knowledge, Options(request));
            seo.Text = LeadWithKeyword(seo.Text, keyword);
            AddChange(seo, $"کلمهٔ کلیدی «{keyword}» به ابتدای متن آمد.");
            list.Add(seo);
        }
        return list;
    }

    /// <summary>
    /// روان‌تر کردن: حذف همهٔ عبارت‌های زائد (حتی کم‌اطمینان‌ها که حذف‌شدنی‌اند) و شکستن جمله‌های بیش از ۳۵ واژه در «و»ِ میانی.
    /// </summary>
    private static string Shorten(string text, SeoKnowledgeBase knowledge, SmartVariant variant)
    {
        var removed = 0;
        foreach (var rule in knowledge.Styles.Where(r => string.IsNullOrEmpty(r.Value) && r.Confidence >= 0.5))
        {
            var replaced = SpamGuard.ReplacePhrase(text, rule.Term, "");
            if (!ReferenceEquals(replaced, text) && replaced != text)
            {
                text = replaced;
                removed++;
                if (!variant.RuleIds.Contains(rule.Id))
                    variant.RuleIds.Add(rule.Id);
            }
        }
        var split = 0;
        var sentences = SplitKeepingPunctuation(text);
        for (var i = 0; i < sentences.Count; i++)
        {
            if (PersianText.WordCount(sentences[i]) <= 35)
                continue;
            var index = MiddleConjunction(sentences[i]);
            if (index < 0)
                continue;
            var first = sentences[i][..index].TrimEnd(' ', '،');
            var second = sentences[i][(index + 3)..].TrimStart();
            sentences[i] = first + ". " + second;
            split++;
        }
        if (removed > 0)
            AddChange(variant, $"حذف {PersianText.Digits(removed)} عبارت زائد");
        if (split > 0)
            AddChange(variant, $"{PersianText.Digits(split)} جملهٔ خیلی بلند به دو جمله شکسته شد");
        return PersianText.Clean(string.Join(" ", sentences)).Replace(" .", ".").Replace("..", ".");
    }

    private static List<string> SplitKeepingPunctuation(string text)
    {
        var list = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is '.' or '؟' or '!' or '?' && !(i > 0 && i < text.Length - 1 && char.IsDigit(text[i - 1]) && char.IsDigit(text[i + 1])))
            {
                list.Add(text[start..(i + 1)].Trim());
                start = i + 1;
            }
        }
        if (start < text.Length && text[start..].Trim().Length > 0)
            list.Add(text[start..].Trim());
        return list;
    }

    /// <summary>جای « و » نزدیک وسط جمله (برای شکستن). ۱- = جای مناسب نیست.</summary>
    private static int MiddleConjunction(string sentence)
    {
        var middle = sentence.Length / 2;
        var best = -1;
        for (var index = sentence.IndexOf(" و ", StringComparison.Ordinal); index >= 0; index = sentence.IndexOf(" و ", index + 1, StringComparison.Ordinal))
        {
            if (index < sentence.Length * 0.3 || index > sentence.Length * 0.7)
                continue;
            if (best < 0 || Math.Abs(index - middle) < Math.Abs(best - middle))
                best = index;
        }
        return best;
    }

    /// <summary>
    /// قرار دادن کلمهٔ کلیدی در ابتدای متن کوتاه: اگر متن با «این محصول/این کالا/این مدل» شروع شود، همان عبارت با کلمه جایگزین
    /// می‌شود؛ وگرنه «کلمه: متن». مثال: («این محصول برای سفر عالی است»، «هدفون سونی») → «هدفون سونی برای سفر عالی است».
    /// </summary>
    public static string LeadWithKeyword(string text, string keyword)
    {
        if (keyword.Length == 0 || KeywordMatcher.FirstPosition(text, keyword) is >= 0 and <= 0.15)
            return text;
        foreach (var lead in new[] { "این محصول", "این کالا", "این دستگاه", "این مدل", "این مجموعه", "این مقاله", "این صفحه" })
        {
            if (text.StartsWith(lead + " ", StringComparison.Ordinal))
                return keyword + text[lead.Length..];
        }
        return keyword + ": " + text;
    }

    // ── نسخه‌ها: نام محصول ─────────────────────────────────────────

    private static List<SmartVariant> ProductNameVariants(string text, SmartTextRequest request, SeoKnowledgeBase knowledge, SmartFacts facts)
    {
        var source = string.IsNullOrWhiteSpace(text) ? request.Subject ?? "" : text;

        var clean = NewVariant(Clean);
        var removed = new List<string>();
        var cleaned = SpamGuard.CleanTitle(source, knowledge, removed);
        if (removed.Count > 0)
            AddChange(clean, "حذف عبارت اغراق‌آمیز/اسپم: " + string.Join("، ", removed.Distinct().Take(4)));
        cleaned = RemoveHype(cleaned, knowledge, clean);
        clean.Text = PolishInto(clean, cleaned, knowledge, Options(request, title: true));
        clean.Text = UpperModels(clean.Text);

        var structured = NewVariant(Structured);
        foreach (var change in clean.Changes) structured.Changes.Add(change);
        structured.RuleIds.AddRange(clean.RuleIds);
        structured.Text = StructureName(clean.Text, request, facts, structured);

        var seo = NewVariant(Seo);
        foreach (var change in structured.Changes) seo.Changes.Add(change);
        seo.RuleIds.AddRange(structured.RuleIds);
        seo.Text = AddKeySpec(structured.Text, request, seo);

        foreach (var variant in new[] { clean, structured, seo })
        {
            if (variant.Text.Length > 100)
                variant.Notes.Add("نام محصول بلند است (بیش از ۱۰۰ نویسه)؛ نام کوتاه‌تر در نتایج و کارت محصول خواناتر است.");
            foreach (var note in clean.Notes)
                if (!variant.Notes.Contains(note)) variant.Notes.Add(note);
        }
        return [clean, structured, seo];
    }

    /// <summary>حذف واژه‌های تبلیغاتی از نام (نام محصول باید توصیفی باشد، نه تبلیغی). مثال: «باکیفیت»، «فوق‌العاده».</summary>
    private static string RemoveHype(string name, SeoKnowledgeBase knowledge, SmartVariant variant)
    {
        var removed = new List<string>();
        foreach (var hype in knowledge.PowerWords.Select(r => r.Term).Concat(HypeWords))
        {
            var replaced = SpamGuard.ReplacePhrase(name, hype, "");
            if (!ReferenceEquals(replaced, name) && replaced != name)
            {
                removed.Add(hype);
                name = replaced;
            }
        }
        if (removed.Count > 0)
            AddChange(variant, "حذف واژهٔ تبلیغاتی از نام: " + string.Join("، ", removed.Take(4)));
        return PersianText.Clean(name);
    }

    private static readonly string[] HypeWords = ["فوق العاده", "فوق‌العاده", "بی نظیر", "بی‌نظیر", "عالی", "شگفت انگیز", "شگفت‌انگیز", "اورجینال", "اصل", "درجه یک", "درجه‌یک"];

    /// <summary>
    /// ترتیب استاندارد نام کالا در فروشگاه‌های ایرانی: «نوع + توصیف + برند + مدل + ویژگی».
    /// مثال: «a55 سامسونگ ۲۵۶ گیگابایت» با دستهٔ «گوشی موبایل» → «گوشی موبایل سامسونگ مدل A55 ۲۵۶ گیگابایت».
    /// </summary>
    public static string StructureName(string name, SmartTextRequest request, SmartFacts facts, SmartVariant? variant = null)
    {
        var result = PersianText.Clean(name);
        if (result.Length == 0)
            return result;
        var type = facts.Type;
        var brand = PersianText.Clean(request.Brand);
        var brandEnglish = PersianText.Clean(request.Product?.BrandEnglishName);

        // ۱) نوع کالا در ابتدا
        if (type is not null && PersianText.PersianRatio(type) > 0.5 && !KeywordMatcher.Contains(result, type, partial: true))
        {
            result = type + " " + result;
            if (variant is not null) AddChange(variant, $"نوع کالا («{type}») به ابتدای نام آمد.");
        }

        // ۲) برند (اگر نه فارسی و نه انگلیسی‌اش در نام نیست)
        var hasBrand = brand.Length > 0 && KeywordMatcher.Contains(result, brand)
                       || brandEnglish.Length > 0 && result.Contains(brandEnglish, StringComparison.OrdinalIgnoreCase);
        var model = LatinModel().Match(result);
        if (brand.Length > 0 && !hasBrand)
        {
            result = model.Success && model.Index > 0
                ? result[..model.Index].TrimEnd() + " " + brand + " " + result[model.Index..]
                : result + " " + brand;
            model = LatinModel().Match(result);
            if (variant is not null) AddChange(variant, $"برند «{brand}» به نام اضافه شد.");
        }
        // اگر برند انگلیسی پیش از مدل آمده (Samsung A55)، برند فارسی جایگزینش می‌شود تا «مدل» بعد از برند بیاید.
        if (model.Success && brand.Length > 0 && brandEnglish.Length > 0 && model.Value.StartsWith(brandEnglish + " ", StringComparison.OrdinalIgnoreCase)
            && KeywordMatcher.Contains(result, brand))
        {
            result = result[..model.Index] + result[(model.Index + brandEnglish.Length + 1)..];
            model = LatinModel().Match(result);
        }

        // ۳) «مدل» پیش از نام مدل لاتین (فقط وقتی نام فارسی است)
        if (model.Success && model.Index > 0 && PersianText.PersianRatio(result[..model.Index]) > 0.5)
        {
            var before = result[..model.Index].TrimEnd();
            if (!before.EndsWith("مدل", StringComparison.Ordinal))
            {
                result = before + " مدل " + result[model.Index..];
                if (variant is not null) AddChange(variant, "واژهٔ «مدل» پیش از نام مدل آمد.");
            }
        }
        return PersianText.Clean(result);
    }

    /// <summary>حروف نام مدل‌های لاتین کوتاه بزرگ می‌شود: «a55» → «A55»، «wh-1000xm5» → «WH-1000XM5».</summary>
    private static string UpperModels(string text) =>
        ModelCode().Replace(text, m => UnitSuffix().IsMatch(m.Value) ? m.Value : m.Value.ToUpperInvariant());

    /// <summary>عدد + واحد (5000mAh، 2.4GHz) بزرگ نمی‌شود چون واحد شکل ثابت خودش را دارد.</summary>
    [GeneratedRegex(@"^\d+(?:mah|mhz|ghz|khz|hz|mm|cm|kg|ml|w|v)$", RegexOptions.IgnoreCase)]
    private static partial Regex UnitSuffix();

    /// <summary>افزودن ویژگی کلیدی (حافظه/ظرفیت/رنگ) به نام، اگر در نام نیست و طول اجازه می‌دهد.</summary>
    private static string AddKeySpec(string name, SmartTextRequest request, SmartVariant variant)
    {
        var features = request.Product?.Features ?? [];
        foreach (var key in new[] { "حافظه داخلی", "حافظهٔ داخلی", "ظرفیت", "حجم", "توان", "رنگ" })
        {
            var feature = features.FirstOrDefault(f => PersianText.Normalize(f.Key).Contains(PersianText.Normalize(key), StringComparison.Ordinal));
            if (feature is null || string.IsNullOrWhiteSpace(feature.Value) || feature.Value.Length > 25)
                continue;
            var value = PersianText.Clean(feature.Value);
            if (KeywordMatcher.Contains(name, value, partial: true) || name.Length + value.Length > 95)
                continue;
            AddChange(variant, $"ویژگی کلیدی «{value}» برای جست‌وجوی دقیق‌تر به نام اضافه شد.");
            return name + " " + value;
        }
        if (!string.IsNullOrWhiteSpace(request.Keyword) && !KeywordMatcher.Contains(name, request.Keyword, partial: true))
            variant.Notes.Add($"کلمهٔ کلیدی «{request.Keyword}» در نام نیست؛ اگر نام واقعی کالا آن را دارد اضافه کنید.");
        return name;
    }

    // ── نسخه‌ها: عنوان سئو ─────────────────────────────────────────

    private static List<SmartVariant> TitleVariants(string text, SmartTextRequest request, SeoKnowledgeBase knowledge, SmartFacts facts, string keyword, int seed)
    {
        var budget = request.Store.TitleBudget;
        var source = string.IsNullOrWhiteSpace(text) ? request.Subject ?? keyword : text;

        var clean = NewVariant(Clean);
        var removed = new List<string>();
        var cleaned = SpamGuard.CleanTitle(source, knowledge, removed);
        if (removed.Count > 0)
            AddChange(clean, "حذف عبارت اغراق‌آمیز/اسپم: " + string.Join("، ", removed.Distinct().Take(4)));
        clean.Text = Fit(PolishInto(clean, cleaned, knowledge, Options(request, title: true)), budget, clean);

        var seo = NewVariant(Seo);
        seo.RuleIds.AddRange(clean.RuleIds);
        foreach (var change in clean.Changes) seo.Changes.Add(change);
        var seoText = clean.Text;
        if (keyword.Length > 0)
        {
            var position = KeywordMatcher.FirstPosition(seoText, keyword);
            if (position < 0)
            {
                seoText = keyword + " | " + seoText;
                AddChange(seo, $"کلمهٔ کلیدی «{keyword}» به ابتدای عنوان آمد.");
            }
            else if (position > 0.34)
            {
                var rest = SpamGuard.ReplacePhrase(seoText, keyword, " ").Trim(' ', '|', '-', '–', '،', ':');
                seoText = rest.Length > 0 ? keyword + " | " + rest : keyword;
                AddChange(seo, "کلمهٔ کلیدی به ابتدای عنوان منتقل شد (گوگل ابتدای عنوان را مهم‌تر می‌داند).");
            }
        }
        var commercial = request.PageType == "product" && !Regex.IsMatch(seoText, "(خرید|قیمت)")
            ? knowledge.Modifiers.FirstOrDefault(m => m.Value == "پیش" && m.Topic == "commercial" && m.IsAuto)
            : null;
        if (commercial is not null && seoText.Length + commercial.Term.Length + 1 <= budget)
        {
            seoText = commercial.Term + " " + seoText;
            seo.RuleIds.Add(commercial.Id);
            AddChange(seo, $"«{commercial.Term}» (نیت خرید کاربران) به عنوان اضافه شد.");
        }
        seo.Text = Fit(seoText, budget, seo);

        var persuasive = NewVariant(Persuasive);
        persuasive.RuleIds.AddRange(clean.RuleIds);
        foreach (var change in clean.Changes) persuasive.Changes.Add(change);
        var persuasiveText = keyword.Length > 0 && KeywordMatcher.FirstPosition(clean.Text, keyword) < 0 ? seo.Text : clean.Text;
        foreach (var (rule, benefit) in facts.Benefits(knowledge, "short", 3, seed))
        {
            var candidate = persuasiveText + " | " + benefit;
            if (candidate.Length > budget)
                continue;
            persuasiveText = candidate;
            persuasive.RuleIds.Add(rule.Id);
            AddChange(persuasive, $"مزیت واقعی «{benefit}» برای نرخ کلیک بیشتر اضافه شد.");
            break;
        }
        persuasive.Text = Fit(persuasiveText, budget, persuasive);
        return [clean, seo, persuasive];
    }

    /// <summary>کوتاه کردن عنوان تا بودجه (بدون شکستن واژه و بدون «…»؛ بخش‌های جداشده با | از آخر حذف می‌شوند).</summary>
    private static string Fit(string title, int budget, SmartVariant variant)
    {
        title = PersianText.Clean(title).Trim(' ', '|', '-', '–', '،');
        if (title.Length <= budget)
            return title;
        var parts = title.Split(" | ").ToList();
        while (parts.Count > 1 && string.Join(" | ", parts).Length > budget)
            parts.RemoveAt(parts.Count - 1);
        var fitted = string.Join(" | ", parts);
        if (fitted.Length > budget)
        {
            var cut = fitted[..budget];
            var space = cut.LastIndexOf(' ');
            fitted = (space > budget / 2 ? cut[..space] : cut).TrimEnd('،', ',', '-', '–', '|', ' ', ':');
        }
        AddChange(variant, $"عنوان به {PersianText.Digits(fitted.Length)} نویسه کوتاه شد تا در نتایج گوگل بریده نشود.");
        return fitted;
    }

    // ── نسخه‌ها: توضیحات متا ───────────────────────────────────────

    private static List<SmartVariant> MetaVariants(string text, SmartTextRequest request, SeoKnowledgeBase knowledge, SmartFacts facts, string keyword, int seed)
    {
        var source = string.IsNullOrWhiteSpace(text) ? request.Subject ?? keyword : text;
        var clean = NewVariant(Clean);
        var removed = new List<string>();
        var cleaned = SpamGuard.CleanText(source, knowledge, removed);
        if (removed.Count > 0)
            AddChange(clean, "حذف عبارت اسپم: " + string.Join("، ", removed.Distinct().Take(4)));
        clean.Text = FitMeta(PolishInto(clean, cleaned, knowledge, Options(request)), clean);

        var seo = NewVariant(Seo);
        seo.RuleIds.AddRange(clean.RuleIds);
        foreach (var change in clean.Changes) seo.Changes.Add(change);
        var seoText = clean.Text.TrimEnd('…');
        if (keyword.Length > 0 && !KeywordInFirst(seoText, keyword, 60))
        {
            seoText = LeadWithKeyword(seoText, keyword);
            AddChange(seo, "کلمهٔ کلیدی در ۶۰ نویسهٔ اول توضیحات آمد (همان بخشی که در موبایل دیده می‌شود).");
        }
        seoText = Extend(seoText, request, knowledge, facts, seed, seo, includeBenefits: false);
        seo.Text = FitMeta(seoText, seo);

        var persuasive = NewVariant(Persuasive);
        persuasive.RuleIds.AddRange(clean.RuleIds);
        foreach (var change in clean.Changes) persuasive.Changes.Add(change);
        persuasive.Text = FitMeta(Extend(clean.Text.TrimEnd('…'), request, knowledge, facts, seed + 1, persuasive, includeBenefits: true), persuasive);
        return [clean, seo, persuasive];
    }

    private static bool KeywordInFirst(string text, string keyword, int chars) =>
        KeywordMatcher.Contains(text.Length <= chars ? text : text[..chars], keyword);

    /// <summary>
    /// تکمیل متای کوتاه (کمتر از ۱۲۰ نویسه) با مزیت‌های واقعی و دعوت به اقدام مناسب نوع صفحه.
    /// </summary>
    private static string Extend(string text, SmartTextRequest request, SeoKnowledgeBase knowledge, SmartFacts facts, int seed, SmartVariant variant, bool includeBenefits)
    {
        text = PersianText.Clean(text);
        if (text.Length > 0 && !".؟!".Contains(text[^1]))
            text += ".";
        if (includeBenefits || text.Length < SeoLimits.MetaMin)
        {
            var benefits = facts.Benefits(knowledge, "short", 3, seed).Where(b => text.Length + b.Text.Length + 3 <= SeoLimits.MetaMax - 25).ToList();
            if (benefits.Count > 0)
            {
                var picked = new List<string>();
                foreach (var (rule, benefit) in benefits)
                {
                    if (text.Length + string.Join("، ", picked.Append(benefit)).Length + 2 > SeoLimits.MetaMax - 20)
                        break;
                    picked.Add(benefit);
                    variant.RuleIds.Add(rule.Id);
                }
                if (picked.Count > 0)
                {
                    text += " " + PersianText.JoinList(picked) + ".";
                    AddChange(variant, "مزیت‌های واقعی فروشگاه اضافه شد: " + string.Join("، ", picked));
                }
            }
        }
        if (text.Length < SeoLimits.MetaMin + 10)
        {
            var action = request.PageType switch
            {
                "product" => "مشخصات، قیمت و نظر خریداران را ببینید.",
                "blog" => "ادامهٔ این مطلب را بخوانید.",
                "category" or "brand" => "مدل‌ها و قیمت‌ها را مقایسه کنید.",
                "contact" => "راه‌های ارتباط با ما را ببینید.",
                _ => "جزئیات بیشتر را در همین صفحه ببینید.",
            };
            if (!text.Contains(action, StringComparison.Ordinal) && text.Length + action.Length + 1 <= SeoLimits.MetaMax)
            {
                text += " " + action;
                AddChange(variant, "دعوت به اقدام کوتاه اضافه شد.");
            }
        }
        return text;
    }

    /// <summary>متا حداکثر ۱۶۰ نویسه (بریدن در پایان جمله یا واژه). کمتر از ۱۲۰ → یادداشت.</summary>
    private static string FitMeta(string text, SmartVariant variant)
    {
        text = PersianText.Clean(text);
        if (text.Length > SeoLimits.MetaMax)
        {
            var cut = text[..SeoLimits.MetaMax];
            var sentenceEnd = cut.LastIndexOfAny(['.', '؟', '!']);
            text = sentenceEnd >= SeoLimits.MetaMin - 10 ? cut[..(sentenceEnd + 1)] : PersianText.Truncate(text, SeoLimits.MetaMax - 1);
            AddChange(variant, $"توضیحات به {PersianText.Digits(text.Length)} نویسه کوتاه شد (حداکثر ۱۶۰).");
        }
        if (text.Length < SeoLimits.MetaMin && !variant.Notes.Any(n => n.Contains("۱۲۰")))
            variant.Notes.Add($"توضیحات {PersianText.Digits(text.Length)} نویسه است؛ ۱۲۰ تا ۱۶۰ نویسه بهترین نمایش را در گوگل دارد.");
        return text;
    }

    // ── نسخه‌ها: متن کوتاه (توضیح کوتاه، چکیده) ───────────────────

    private static List<SmartVariant> ShortVariants(string text, SmartTextRequest request, SeoKnowledgeBase knowledge, SmartFacts facts, string keyword, int seed)
    {
        var plain = LooksLikeHtml(text) ? PersianText.HtmlToText(text) : text;
        var clean = NewVariant(Clean);
        clean.Text = PolishInto(clean, plain, knowledge, Options(request));

        var concise = NewVariant(Concise);
        concise.RuleIds.AddRange(clean.RuleIds);
        foreach (var change in clean.Changes) concise.Changes.Add(change);
        concise.Text = Shorten(clean.Text, knowledge, concise);

        var seo = NewVariant(Seo);
        seo.RuleIds.AddRange(concise.RuleIds);
        foreach (var change in concise.Changes) seo.Changes.Add(change);
        seo.Text = concise.Text;
        if (keyword.Length > 0 && KeywordMatcher.FirstPosition(seo.Text, keyword) is < 0 or > 0.3)
        {
            seo.Text = LeadWithKeyword(seo.Text, keyword);
            AddChange(seo, $"کلمهٔ کلیدی «{keyword}» به ابتدای متن آمد.");
        }

        var list = new List<SmartVariant> { clean, concise, seo };
        if (request.PageType is "product" or "category" or "brand")
        {
            var persuasive = NewVariant(Persuasive);
            persuasive.RuleIds.AddRange(clean.RuleIds);
            foreach (var change in clean.Changes) persuasive.Changes.Add(change);
            var body = clean.Text.TrimEnd();
            if (body.Length > 0 && !".؟!".Contains(body[^1]))
                body += ".";
            var benefits = facts.Benefits(knowledge, "short", 3, seed).Where(b => !body.Contains(b.Text, StringComparison.Ordinal)).ToList();
            if (benefits.Count > 0)
            {
                body += " " + PersianText.JoinList(benefits.Select(b => b.Text).ToList()) + ".";
                persuasive.RuleIds.AddRange(benefits.Select(b => b.Rule.Id));
                AddChange(persuasive, "مزیت‌های واقعی خرید اضافه شد: " + string.Join("، ", benefits.Select(b => b.Text)));
            }
            persuasive.Text = body;
            list.Add(persuasive);
        }
        return list;
    }

    // ── تحلیل و امتیاز ─────────────────────────────────────────────

    /// <summary>
    /// امتیاز کیفیت ۰ تا ۱۰۰ و فهرست ایرادها (فارسی و قابل فهم). همان سنجه‌ها برای «قبل» و هر نسخه استفاده می‌شود
    /// تا مقایسه منصفانه باشد: املا و محاوره، حشو، اسپم، طول مناسب، جای کلمهٔ کلیدی، تیتر و طول پاراگراف و جمله.
    /// </summary>
    public static SmartTextAnalysis Analyze(string? text, bool isHtml, SmartTextRequest request, SeoKnowledgeBase knowledge)
    {
        var issues = new List<string>();
        var plain = isHtml ? PersianText.HtmlToText(text) : PersianText.Clean(text);
        var words = PersianText.WordCount(plain);
        if (plain.Length == 0)
            return new SmartTextAnalysis { Score = 0, Words = 0, Characters = 0, Issues = ["متن خالی است."] };

        var score = 100.0;
        var keyword = PersianText.Clean(request.Keyword);
        var check = PersianWriting.Polish(plain.Length > 20_000 ? plain[..20_000] : plain, knowledge, new WritingOptions { Vocabulary = request.Vocabulary, SpellingNotes = true });
        var spelling = check.Changes.Count(c => c.Category is "املا") + check.Notes.Count(n => n.StartsWith("املای", StringComparison.Ordinal));
        var informal = check.Changes.Count(c => c.Category == "رسمی‌نویسی");
        var style = check.Changes.Count(c => c.Category == "سبک");
        var spacing = check.Changes.Count(c => c.Category == "نیم‌فاصله");
        if (spelling > 0) { score -= Math.Min(15, spelling * 3); issues.Add($"{PersianText.Digits(spelling)} مورد غلط یا املای مشکوک"); }
        if (informal > 0) { score -= Math.Min(12, informal * 3); issues.Add("واژه‌های محاوره‌ای در متن رسمی"); }
        if (style > 0) { score -= Math.Min(10, style * 2); issues.Add("عبارت‌های زائد و حشو"); }
        if (spacing > 0) { score -= Math.Min(6, spacing * 2); issues.Add("نیم‌فاصله‌های رعایت‌نشده"); }

        var spam = SpamGuard.Inspect(
            request.Purpose is SmartPurpose.SeoTitle or SmartPurpose.ProductName ? plain : null,
            request.Purpose == SmartPurpose.MetaDescription ? plain : null,
            isHtml ? text : request.Purpose is SmartPurpose.Body or SmartPurpose.ShortText ? "<p>" + System.Net.WebUtility.HtmlEncode(plain) + "</p>" : null,
            keyword, knowledge);
        foreach (var issue in spam)
        {
            score -= issue.Severity >= 2 ? 12 : 5;
            issues.Add(issue.Message);
        }

        switch (request.Purpose)
        {
            case SmartPurpose.SeoTitle:
            {
                var full = plain.Length + request.Store.TitleSuffixLength;
                if (full is < SeoLimits.TitleMin or > SeoLimits.TitleMax) { score -= 15; issues.Add($"طول عنوان {PersianText.Digits(full)} نویسه است (۳۰ تا ۶۰ مناسب است)."); }
                if (keyword.Length > 0)
                {
                    var position = KeywordMatcher.FirstPosition(plain, keyword);
                    if (position < 0) { score -= 20; issues.Add("کلمهٔ کلیدی در عنوان نیست."); }
                    else if (position > 0.34) { score -= 8; issues.Add("کلمهٔ کلیدی در ابتدای عنوان نیست."); }
                }
                break;
            }
            case SmartPurpose.MetaDescription:
                if (plain.Length < 70) { score -= 25; issues.Add("توضیحات خیلی کوتاه است."); }
                else if (plain.Length is < SeoLimits.MetaMin or > SeoLimits.MetaMax) { score -= 12; issues.Add($"طول توضیحات {PersianText.Digits(plain.Length)} نویسه است (۱۲۰ تا ۱۶۰ مناسب است)."); }
                if (keyword.Length > 0 && !KeywordMatcher.Contains(plain, keyword)) { score -= 15; issues.Add("کلمهٔ کلیدی در توضیحات نیست."); }
                else if (keyword.Length > 0 && !KeywordInFirst(plain, keyword, 60)) { score -= 5; issues.Add("کلمهٔ کلیدی در ابتدای توضیحات نیست."); }
                break;
            case SmartPurpose.ProductName:
                if (plain.Length > 100) { score -= 10; issues.Add("نام محصول خیلی بلند است."); }
                if (request.Brand is { Length: > 0 } brand && !KeywordMatcher.Contains(plain, brand)
                    && !(request.Product?.BrandEnglishName is { Length: > 0 } en && plain.Contains(en, StringComparison.OrdinalIgnoreCase)))
                { score -= 8; issues.Add("نام برند در نام محصول نیست."); }
                if (new SmartFacts(request).Type is { } type && !KeywordMatcher.Contains(plain, type, partial: true)) { score -= 8; issues.Add("نوع کالا در نام نیست."); }
                break;
            case SmartPurpose.ShortText:
                if (plain.Length > 400) { score -= 5; issues.Add("متن کوتاه بیش از حد بلند است."); }
                if (keyword.Length > 0 && !KeywordMatcher.Contains(plain, keyword, partial: true)) { score -= 5; issues.Add("کلمهٔ کلیدی در متن نیست."); }
                break;
            case SmartPurpose.Body:
                score -= BodyPenalty(text ?? "", isHtml, plain, words, keyword, request, issues);
                break;
        }

        var sentences = PersianText.Sentences(plain);
        if (sentences.Count > 0)
        {
            var average = sentences.Average(s => PersianText.WordCount(s));
            if (average > 28) { score -= 8; issues.Add("جمله‌ها بلندند (میانگین بیش از ۲۸ واژه)؛ جملهٔ کوتاه‌تر خواناتر است."); }
        }

        return new SmartTextAnalysis
        {
            Score = (int)Math.Clamp(Math.Round(score), 0, 100),
            Words = words,
            Characters = plain.Length,
            Issues = issues,
        };
    }

    private static bool Same(string a, string b, bool html) =>
        html ? Compact(a) == Compact(b) : PersianText.Clean(a) == PersianText.Clean(b);

    private static string Compact(string html) => WhitespaceRun().Replace(html ?? "", " ").Replace("> <", "><").Trim();

    /// <summary>متن شبیه HTML است؟ (تگ بلوکی یا خطی رایج دارد)</summary>
    public static bool LooksLikeHtml(string? text) => !string.IsNullOrEmpty(text) && HtmlTag().IsMatch(text);

    [GeneratedRegex(@"<(p|div|br|h[1-6]|ul|ol|li|strong|b|em|span|a|table|img)[\s>/]", RegexOptions.IgnoreCase)]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();

    /// <summary>نام مدل لاتین (حداقل یک رقم یا حرف بزرگ، می‌تواند با برند لاتین شروع شود). مثال: «Galaxy A55»، «WH-1000XM5».</summary>
    [GeneratedRegex(@"[A-Za-z][A-Za-z0-9\-+./]*(?:\s+[A-Za-z0-9][A-Za-z0-9\-+./]*)*")]
    private static partial Regex LatinModel();

    /// <summary>کد مدل کوتاه با حرف و رقم (a55، wh-1000xm5، rtx4060).</summary>
    [GeneratedRegex(@"(?<![A-Za-z])(?=[A-Za-z0-9\-]*\d)(?=[A-Za-z0-9\-]*[A-Za-z])[A-Za-z0-9\-]{2,14}(?![A-Za-z])")]
    private static partial Regex ModelCode();
}
