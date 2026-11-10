using Fratak_Shop.Core.Services.Design;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Design;
using Fratak_Shop.Web.Services;
using SkiaSharp;

namespace Fratak_Shop.Web.Infrastructure.Design;

/// <summary>
/// هماهنگ‌کنندهٔ استودیوی طراحی در لایهٔ وب (دور ۲۱): هر جا «فایل» در کار است (ذخیرهٔ تصویر، پاک کردن فایل قدیمی، رندر سرور) از این‌جا می‌گذرد
/// و دادهٔ پایگاه داده از <see cref="IDesignService"/>.
/// <list type="bullet">
/// <item><b>ایده‌های هوشمند</b> (<see cref="SuggestIdeasAsync"/>): ایده‌ساز بدون هوش مصنوعی (<see cref="DesignIdeas"/>) از دادهٔ واقعی فروشگاه
/// ایده می‌سازد ← قالب فابریک (دسکتاپ + موبایل) ← رندر سرور ← ذخیرهٔ WebP ← طرح «پیشنهادی» منتظر تأیید مدیر. ایدهٔ تکراری یا ردشده دوباره نمی‌آید.</item>
/// <item><b>ذخیرهٔ ویرایشگر</b> (<see cref="SaveFromEditorAsync"/>): PNG ساخت مرورگر ← WebP در پوشهٔ درست (بنر/مقاله/محتوا) ← فایل قبلی پاک.</item>
/// <item><b>حذف</b>: فایل‌های طرح هم پاک می‌شود (طرح منتشرشده حذف نمی‌شود).</item>
/// </list>
/// <para>محل استفاده: صفحه‌های پنل Design/Index و Design/Editor، کاور مقاله و SmartWorker (ایده‌های روزانه).</para>
/// </summary>
public sealed class DesignStudio(
    IDesignService designs,
    IDesignRenderer renderer,
    IUploadStore uploads,
    IStoreSettingService settings,
    ILogger<DesignStudio> logger,
    Fratak_Shop.Core.Services.Smart.ISmartConfigService? smart = null,
    Fratak_Shop.Core.Services.Marketing.ICampaignOfferService? campaigns = null)
{
    /// <summary>نام انجام‌دهندهٔ کارهای خودکار.</summary>
    public const string Actor = "دستیار هوشمند";

    /// <summary>سقف حجم PNG دریافتی از ویرایشگر (۱۰ مگابایت).</summary>
    public const int MaxUploadKb = 10_240;

    /// <summary>پوشهٔ فایل هر نوع طرح. مثال: بنر ← /uploads/banners؛ کاور مقاله ← /uploads/blogs؛ پست/آزاد ← /uploads/content.</summary>
    public static UploadArea AreaFor(DesignKind kind) => kind switch
    {
        DesignKind.Banner => UploadArea.Banners,
        DesignKind.BlogCover => UploadArea.Blogs,
        _ => UploadArea.Content,
    };

    /// <summary>آدرس عمومی تصویر طرح (یا null). مثال: (Banner، «a.webp») ← «/uploads/banners/a.webp».</summary>
    public static string? Url(DesignKind kind, string? fileName) =>
        string.IsNullOrWhiteSpace(fileName) ? null : $"/uploads/{AreaFor(kind).Name}/{fileName}";

    /// <summary>
    /// ساخت ایده‌های بنر تازه (حداکثر <paramref name="max"/>). خروجی = تعداد ایدهٔ ساخته‌شده. هر ایده جداگانه در try؛ خطای یکی بقیه را متوقف نمی‌کند.
    /// </summary>
    public async Task<int> SuggestIdeasAsync(int max, CancellationToken cancellationToken)
    {
        max = Math.Clamp(max, 0, 6);
        if (max == 0)
            return 0;
        var store = await settings.GetAsync(cancellationToken);
        var input = await designs.IdeaInputAsync(store.SiteName, cancellationToken);
        input = WithImageSizes(input) with { FreeShippingFrom = Math.Max(0, store.FreeShippingThreshold) };
        var ideas = DesignIdeas.Generate(input, DateTimeOffset.UtcNow, max * 3);
        // دور ۲۳: روزهای سوگواری (اگر تنظیم «توقف بنر شاد در عزا» روشن باشد) ایدهٔ جشن/مناسبت شاد ساخته نمی‌شود.
        if (smart is not null && (await smart.GetAsync(cancellationToken)).PauseBannersOnMourning
            && Fratak_Shop.Core.Services.Marketing.MourningCalendar.IsMourning(DateTimeOffset.UtcNow))
            ideas = ideas.Where(i => !IsFestive(i)).ToList();
        if (ideas.Count == 0)
            return 0;
        var existing = await designs.ExistingIdeaKeysAsync(ideas.Select(i => i.Key), cancellationToken);
        var created = 0;
        foreach (var idea in ideas.Where(i => !existing.Contains(i.Key)))
        {
            if (created >= max)
                break;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await CreateIdeaAsync(idea, cancellationToken))
                    created++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Banner idea {Key} failed", idea.Key);
            }
        }
        return created;
    }

    /// <summary>ایدهٔ شاد/جشن؟ (کلید مناسبت یا چیدمان festive). مثال: «occasion:yalda:1405» ← true.</summary>
    public static bool IsFestive(DesignIdea idea) =>
        idea.Key.StartsWith("occasion:", StringComparison.Ordinal) || idea.Spec.Layout == "festive";

    private async Task<bool> CreateIdeaAsync(DesignIdea idea, CancellationToken cancellationToken) =>
        await CreateIdeaAsync(idea, DesignPresets.Find(DesignPresets.Hero)!, Actor, cancellationToken) is not null;

    /// <summary>ساخت یک طرح «پیشنهادی» از ایده در اندازهٔ داده‌شده (+ نسخهٔ موبایل اگر جایگاه دارد). خروجی: شناسهٔ طرح.</summary>
    private async Task<Guid?> CreateIdeaAsync(DesignIdea idea, DesignPreset preset, string? actor, CancellationToken cancellationToken, Guid? categoryId = null)
    {
        var json = DesignTemplates.Build(idea.Spec, preset.Width, preset.Height, preset.Transparent);
        var mobile = preset.MobileKey is null ? null : DesignPresets.Find(preset.MobileKey);
        var mobileJson = mobile is null ? null : DesignTemplates.Build(idea.Spec, mobile.Width, mobile.Height, mobile.Transparent);

        var imageName = await RenderAndStoreAsync(json, preset.Width, preset.Height, UploadArea.Banners, cancellationToken);
        var mobileName = mobile is null || mobileJson is null ? null : await RenderAndStoreAsync(mobileJson, mobile.Width, mobile.Height, UploadArea.Banners, cancellationToken);
        var doc = new DesignDoc
        {
            Title = idea.Title,
            Kind = DesignKind.Banner,
            Status = DesignStatus.Suggested,
            IsSmart = true,
            PresetKey = preset.Key,
            Width = preset.Width,
            Height = preset.Height,
            Json = json,
            MobileJson = mobileJson,
            ImageName = imageName,
            MobileImageName = mobileName,
            IdeaKey = idea.Key,
            IdeaNote = idea.Reason.Length > 500 ? idea.Reason[..500] : idea.Reason,
            Headline = idea.Spec.Headline,
            Subline = idea.Spec.Subline,
            ButtonText = idea.Spec.ButtonText,
            LinkUrl = DesignService.SafeLink(idea.LinkUrl),
            ExpiresAt = idea.ExpiresAt,
            CategoryId = categoryId,
            UpdatedByName = actor ?? Actor,
            // دور ۲۶: بنر هوشمند پیش‌فرض «زنده» است (عنصرها نرم وارد می‌شوند)؛ مدیر در ویرایشگر می‌تواند خاموشش کند.
            Animation = BannerMotion.Soft,
        };
        try
        {
            return await designs.AddAsync(doc, cancellationToken);
        }
        catch
        {
            uploads.Delete(UploadArea.Banners, new[] { imageName, mobileName }.Where(n => n is not null).Select(n => n!));
            throw;
        }
    }

    /// <summary>
    /// دور ۲۳ – «ایده ← طرح»: متن کوتاه مدیر (مثل «حراج هدفون یلدا ۲۰٪» یا «کارت هدیه») ← ۳ طرح کامل «پیشنهادی» (دسکتاپ + موبایل)
    /// با تیتر، متن، کالای واقعی، عکس، رنگ عکس کالا (با بررسی کنتراست) و لینک درست. زمینهٔ سفید عکس کالا شفاف می‌شود تا روی رنگ بنر بنشیند.
    /// هیچ طرحی خودکار منتشر نمی‌شود؛ نسخه‌هایی که مدیر منتشر کند خودکار یک گروه آزمون A/B می‌شوند.
    /// </summary>
    /// <param name="text">متن ایده (۲ تا ۲۰۰ نویسه).</param>
    /// <param name="presetKey">اندازه/جایگاه: hero، wide، promo یا category (بقیه ← hero).</param>
    /// <param name="productId">دور ۲۴: کالای انتخاب‌شده از فهرست (اختیاری؛ بر تطبیق متن مقدم است).</param>
    /// <remarks>
    /// دور ۲۴: اگر متن درصدی بیشتر از تخفیف واقعی یا «امتیاز n برابر» بخواهد، یک «قاعدهٔ واقعی» پیش‌نویس (کد تخفیف غیرفعال/ضریب امتیاز)
    /// ساخته می‌شود و بنر همان را می‌گوید؛ با انتشار بنر فعال می‌شود. سربرگ دسته فقط برای ایدهٔ دارای دسته ساخته می‌شود.
    /// </remarks>
    public async Task<OperationResult<BriefOutcome>> BuildFromBriefAsync(string? text, string? presetKey, string? actor, CancellationToken cancellationToken,
        Guid? productId = null, BriefTarget? target = null)
    {
        text = text?.Trim();
        target ??= BriefTarget.Auto;
        if (target.Kind == BriefTargetKind.Product && productId is null)
            return OperationResult<BriefOutcome>.Error("برای بنر کالا، کالا را از فهرست انتخاب کنید.");
        if (string.IsNullOrWhiteSpace(text) || text.Length < 2)
            return OperationResult<BriefOutcome>.Error("ایده را در یک جملهٔ کوتاه بنویسید؛ مثلاً «حراج هدفون برای یلدا».");
        if (text.Length > DesignBrief.MaxLength)
            return OperationResult<BriefOutcome>.Error($"متن ایده حداکثر {DesignBrief.MaxLength} نویسه باشد.");
        var preset = DesignPresets.Find(presetKey) is { Kind: DesignKind.Banner, Transparent: false } found
                     && !DesignPresets.IsLegacy(found)
            ? found
            : DesignPresets.Find(DesignPresets.Hero)!;

        var store = await settings.GetAsync(cancellationToken);
        var intent = DesignBrief.Parse(text) with { ProductId = productId, Target = target };
        var data = await designs.BriefDataAsync(text, intent, store.SiteName, cancellationToken);
        if (preset.Position == Fratak_Shop.Data.Entities.Marketing.BannerPosition.CategoryHeader && data.Category is null)
            return OperationResult<BriefOutcome>.Error("برای سربرگ صفحهٔ دسته، دسته را در «مقصد بنر» انتخاب کنید یا نامش را در ایده بنویسید؛ مثلاً «حراج هدفون ۲۰٪».");
        if (target.Kind == BriefTargetKind.Category && data.Category is null)
            return OperationResult<BriefOutcome>.Error("دستهٔ انتخاب‌شده پیدا نشد یا غیرفعال است.");
        var plan = DesignBrief.Plan(text, intent, data, DateTimeOffset.UtcNow);
        // دور ۲۴: قاعدهٔ واقعی کمپین (اگر ادعای متن با دادهٔ فعلی راست نیست ولی می‌شود راستش کرد).
        var offerWarnings = new List<string>();
        Fratak_Shop.Core.Services.Design.BriefOffer? offer = null;
        if (campaigns is not null)
        {
            var (candidate, candidateWarnings) = DesignBrief.Offer(intent, plan, data);
            offerWarnings.AddRange(candidateWarnings);
            if (candidate is not null)
            {
                offer = candidate.Percent is null ? candidate : candidate with { Code = await campaigns.NewCodeAsync(cancellationToken) };
                plan = DesignBrief.Plan(text, intent, data, DateTimeOffset.UtcNow, offer);
            }
        }
        var mourningPause = smart is not null && (await smart.GetAsync(cancellationToken)).PauseBannersOnMourning
                            && Fratak_Shop.Core.Services.Marketing.MourningCalendar.IsMourning(DateTimeOffset.UtcNow);

        string? color = null;
        DesignImage? image = null;
        string? cutoutFile = null;
        if (plan.Hero?.ImageUrl is { } url && uploads.GetPath(UploadArea.Products, Path.GetFileName(url)) is { } path)
        {
            try
            {
                using var bitmap = SKBitmap.Decode(path);
                if (bitmap is not null)
                {
                    color = ImageColors.Dominant(bitmap);
                    image = new DesignImage(url, bitmap.Width, bitmap.Height);
                    if (ImageColors.HasWhiteBackground(bitmap) && ImageColors.CutoutWhite(bitmap) is { } png
                        && await uploads.SaveImageAsync(UploadArea.Content, png, cancellationToken) is { } stored)
                    {
                        cutoutFile = stored.FileName;
                        var scale = Math.Min(1.0, 800.0 / Math.Max(bitmap.Width, bitmap.Height));
                        image = new DesignImage($"/uploads/{UploadArea.Content.Name}/{stored.FileName}",
                            Math.Max(1, (int)Math.Round(bitmap.Width * scale)), Math.Max(1, (int)Math.Round(bitmap.Height * scale)));
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                logger.LogWarning(exception, "Brief image analysis failed for {Path}", path);
            }
        }

        // دور ۲۶: چند کالای دامنه برای چیدمان «کنار هم» (بنر دسته/کمپین) و عکس واقعی دسته برای «عکس تمام‌قاب».
        // کارت هدیه، باشگاه، بلاگ و بنر صفحه/اطلاع‌رسانی کالاهای تصادفی نمی‌گیرند.
        var galleryKinds = plan.Kind is BriefKind.Category or BriefKind.Deal or BriefKind.Campaign or BriefKind.Occasion or BriefKind.Brand;
        var gallery = !galleryKinds ? new List<DesignImage>() : plan.Products.Concat(plan.Hero is null ? Array.Empty<IdeaProduct>() : new[] { plan.Hero })
            .Where(p => !string.IsNullOrWhiteSpace(p.ImageUrl)).DistinctBy(p => p.Id).Take(3)
            .Select(p => ProductPicture(p.ImageUrl!)).Where(p => p is not null).Select(p => p!).ToList();
        var photo = CategoryPhoto(data.Category?.ImageName);
        var ideas = DesignBrief.Variants(plan, color, image, DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture),
            gallery, photo);
        var warnings = offerWarnings.Concat(plan.Warnings).ToList();
        if (mourningPause && ideas.Any(IsFestive))
        {
            // روز سوگواری: نسخهٔ جشن به چیدمان ساده و پالت آرام تبدیل می‌شود (طرح شاد ساخته نمی‌شود).
            ideas = ideas.Select(i => IsFestive(i) ? i with { Key = i.Key.Replace("occasion:", "calm:", StringComparison.Ordinal), Spec = i.Spec with { Layout = "minimal", Palette = "night" } } : i).ToList();
            warnings.Add("امروز روز سوگواری است؛ نسخهٔ شاد ساخته نشد.");
        }
        var ids = new List<Guid>();
        foreach (var idea in ideas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await CreateIdeaAsync(idea, preset, actor, cancellationToken, data.Category?.Id) is { } id)
                    ids.Add(id);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Brief variant {Key} failed", idea.Key);
            }
        }
        if (ids.Count == 0)
        {
            if (cutoutFile is not null)
                uploads.Delete(UploadArea.Content, [cutoutFile]);
            return OperationResult<BriefOutcome>.Error("ساخت طرح‌ها ناموفق بود؛ دوباره تلاش کنید.");
        }
        if (offer is not null && campaigns is not null && DesignBrief.VariantGroup(ideas[0].Key) is { } group)
        {
            // پیش‌نویس قاعده فقط وقتی طرحی واقعاً ساخته شده باشد؛ مدت = مدت اعتبار ایده (حراج ۷، کالا ۱۴، بقیه ۳۰ روز).
            var days = plan.ExpiresAt is { } until ? Math.Max(1, (int)Math.Ceiling((until - DateTimeOffset.UtcNow).TotalDays)) : 7;
            await campaigns.CreateDraftAsync(offer, group, plan.Title, days, actor, cancellationToken);
        }
        return OperationResult<BriefOutcome>.Success(new BriefOutcome(ids, warnings, color), $"{ids.Count} نسخه از ایدهٔ شما ساخته شد.");
    }

    /// <summary>عکس کالا با ابعاد واقعی (از سرآیند فایل). فایل نبود/خراب ← null.</summary>
    private DesignImage? ProductPicture(string url)
    {
        var path = uploads.GetPath(UploadArea.Products, Path.GetFileName(url));
        if (path is null)
            return null;
        try
        {
            using var codec = SKCodec.Create(path);
            return codec is null ? null : new DesignImage(url, codec.Info.Width, codec.Info.Height);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// دور ۲۶: عکس «فضاسازی» واقعی دسته (از جست‌وجوی وب؛ نام فایل «…-photo-….webp») برای چیدمان عکس تمام‌قاب.
    /// فقط عکس افقی با عرض ≥ ۹۰۰؛ تصویر ساخته‌شده از کالاهای خود دسته (زمینهٔ سفید) استفاده نمی‌شود.
    /// </summary>
    private DesignImage? CategoryPhoto(string? imageName)
    {
        if (!Fratak_Shop.Core.Services.Smart.CategoryImages.IsPhoto(imageName))
            return null;
        var path = uploads.GetPath(UploadArea.Categories, imageName!);
        if (path is null)
            return null;
        try
        {
            using var codec = SKCodec.Create(path);
            return codec is { Info.Width: >= 900 } && codec.Info.Width > codec.Info.Height
                ? new DesignImage($"/uploads/{UploadArea.Categories.Name}/{imageName}", codec.Info.Width, codec.Info.Height)
                : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>رندر سرور و ذخیره (رندر ناموفق ← null؛ طرح بدون پیش‌نمایش ذخیره می‌شود و ویرایشگر خودش می‌سازد).</summary>
    public async Task<string?> RenderAndStoreAsync(string json, int width, int height, UploadArea area, CancellationToken cancellationToken)
    {
        var png = renderer.RenderPng(json, width, height);
        if (png is null)
            return null;
        var stored = await uploads.SaveImageAsync(area, png, cancellationToken);
        return stored?.FileName;
    }

    /// <summary>
    /// ابعاد واقعی عکس کالاها از سرآیند فایل (بدون باز کردن کامل تصویر) تا قالب عکس را بدون کشیدگی جا بدهد.
    /// </summary>
    private IdeaInput WithImageSizes(IdeaInput input)
    {
        IdeaProduct Fill(IdeaProduct p)
        {
            if (p.ImageUrl is null || p.ImageWidth > 0)
                return p;
            var name = Path.GetFileName(p.ImageUrl);
            var path = uploads.GetPath(UploadArea.Products, name);
            if (path is null)
                return p with { ImageUrl = null };
            try
            {
                using var codec = SKCodec.Create(path);
                return codec is null ? p with { ImageUrl = null } : p with { ImageWidth = codec.Info.Width, ImageHeight = codec.Info.Height };
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return p with { ImageUrl = null };
            }
        }

        var products = input.Products.Select(Fill).ToList();
        var byId = products.ToDictionary(p => p.Id);
        var categories = input.Categories
            .Select(c => c with { Hero = c.Hero is null ? null : byId.GetValueOrDefault(c.Hero.Id) ?? Fill(c.Hero) })
            .ToList();
        return input with { Products = products, Categories = categories };
    }

    /// <summary>
    /// ذخیرهٔ ویرایشگر: تصویر(های) PNG مرورگر ← WebP در پوشهٔ نوع طرح ← سرویس داده ← پاک کردن فایل‌های قبلی.
    /// اگر ذخیرهٔ پایگاه داده شکست بخورد، فایل‌های تازه هم پاک می‌شوند (فایل یتیم نمی‌ماند).
    /// </summary>
    public async Task<OperationResult<DesignSaveResult>> SaveFromEditorAsync(Guid id, string title, string json, string? mobileJson,
        IFormFile? image, IFormFile? mobileImage, string? headline, string? actor, CancellationToken cancellationToken, BannerMotion? motion = null)
    {
        var doc = await designs.GetAsync(id, cancellationToken);
        if (doc is null)
            return OperationResult<DesignSaveResult>.NotFound("طرح پیدا نشد.");
        var area = AreaFor(doc.Kind);
        var created = new List<string>();
        string? imageName = null, mobileName = null;
        foreach (var (file, isMobile) in new[] { (image, false), (mobileImage, true) })
        {
            if (file is null || file.Length == 0)
                continue;
            var (files, error) = await uploads.SaveAsync(area, [file], 1, MaxUploadKb, cancellationToken);
            if (error is not null || files.Count != 1)
            {
                uploads.Delete(area, created);
                return OperationResult<DesignSaveResult>.Error(error ?? "تصویر طرح ذخیره نشد.");
            }
            created.Add(files[0].FileName);
            if (isMobile)
                mobileName = files[0].FileName;
            else
                imageName = files[0].FileName;
        }

        OperationResult<DesignSaveResult> result;
        try
        {
            result = await designs.SaveAsync(id, title, json, mobileJson, imageName, mobileName, headline, actor, cancellationToken, motion);
        }
        catch
        {
            uploads.Delete(area, created);
            throw;
        }
        if (!result.IsSuccess())
        {
            uploads.Delete(area, created);
            return result;
        }
        if (result.Data.ReplacedFiles.Count > 0)
            uploads.Delete(area, result.Data.ReplacedFiles);
        return result;
    }

    /// <summary>حذف طرح و فایل‌هایش (طرح روی سایت حذف نمی‌شود).</summary>
    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var doc = await designs.GetAsync(id, cancellationToken);
        if (doc is null)
            return OperationResult.NotFound("طرح پیدا نشد.");
        var result = await designs.DeleteAsync(id, cancellationToken);
        if (!result.IsSuccess())
            return OperationResult.Error(result.Message);
        uploads.Delete(AreaFor(doc.Kind), result.Data);
        return OperationResult.Success("طرح حذف شد.");
    }

    /// <summary>
    /// طرح تازه: خالی یا از قالب (layout + پالت + متن نمونه). اندازه از پیش‌تنظیم؛ «free» با ابعاد دلخواه (محدود به ۲۰۰ تا ۴۰۰۰).
    /// مثال: CreateAsync("hero", "sale", "sunset", …) ← طرح «پیش‌نویس» بنر اسلایدر با تیتر نمونه و نسخهٔ موبایل.
    /// <paramref name="headline"/> = تیتر طرح به‌جای متن نمونه (کاور مقاله ← عنوان همان مقاله).
    /// </summary>
    public async Task<OperationResult<Guid>> CreateAsync(string presetKey, string? layout, string? palette, int? width, int? height, string? title,
        string? headline, string? actor, CancellationToken cancellationToken)
    {
        var preset = DesignPresets.Find(presetKey) ?? DesignPresets.Find("free")!;
        var (w, h) = preset.Key == "free" && width is > 0 && height is > 0 ? DesignPresets.ClampSize(width.Value, height.Value) : (preset.Width, preset.Height);
        var mobile = preset.MobileKey is null ? null : DesignPresets.Find(preset.MobileKey);
        string json, mobileJson;
        if (string.IsNullOrWhiteSpace(layout) || layout == "blank")
        {
            json = DesignTemplates.Blank(w, h, preset.Transparent);
            mobileJson = mobile is null ? "" : DesignTemplates.Blank(mobile.Width, mobile.Height, mobile.Transparent);
        }
        else
        {
            var spec = SampleSpec(layout, palette, preset.Kind);
            if (!string.IsNullOrWhiteSpace(headline))
                spec = spec with { Headline = DesignTemplates.Shorten(headline.Trim(), 90) };
            json = DesignTemplates.Build(spec, w, h, preset.Transparent);
            mobileJson = mobile is null ? "" : DesignTemplates.Build(spec, mobile.Width, mobile.Height, mobile.Transparent);
        }
        var doc = new DesignDoc
        {
            Title = string.IsNullOrWhiteSpace(title) ? preset.Title + " تازه" : DesignTemplates.Shorten(title.Trim(), 200),
            Kind = preset.Kind,
            Status = DesignStatus.Draft,
            PresetKey = preset.Key,
            Width = w,
            Height = h,
            Json = json,
            MobileJson = string.IsNullOrEmpty(mobileJson) ? null : mobileJson,
            UpdatedByName = actor,
        };
        var id = await designs.AddAsync(doc, cancellationToken);
        return OperationResult<Guid>.Success(id, "طرح ساخته شد.");
    }

    /// <summary>
    /// متن نمونهٔ قالب (مدیر روی بوم عوضش می‌کند). مثال: sale ← «حراج ویژهٔ این هفته» + «تا ۳۰٪ تخفیف».
    /// </summary>
    public static DesignSpec SampleSpec(string layout, string? palette, DesignKind kind)
    {
        var known = DesignTemplates.Layouts.Any(l => l.Key == layout) ? layout : "center";
        var paletteKey = DesignPalettes.All.Any(p => p.Key == palette) ? palette! : known switch
        {
            "sale" => "sunset",
            "festive" => "festive",
            "minimal" => "clean",
            _ => "ocean",
        };
        return known switch
        {
            "sale" => new DesignSpec(known, paletteKey, "حراج ویژهٔ این هفته", "تخفیف واقعی روی کالاهای منتخب", "تا ۳۰٪ تخفیف", "خرید کنید"),
            "festive" => new DesignSpec(known, paletteKey, "جشن خرید", "هدیه‌ای برای عزیزانتان انتخاب کنید", null, "دیدن پیشنهادها"),
            "split" => new DesignSpec(known, paletteKey, "تیتر اصلی بنر", "یک جملهٔ کوتاه دربارهٔ پیشنهاد", null, "مشاهده"),
            "minimal" => new DesignSpec(known, paletteKey, "تازه رسید", "کالاهای جدید این هفته", null, "مشاهده"),
            _ => kind == DesignKind.BlogCover
                ? new DesignSpec(known, paletteKey, "عنوان مقاله", "راهنمای خرید")
                : new DesignSpec(known, paletteKey, "تیتر اصلی", "متن کوتاه زیر تیتر", null, "مشاهده"),
        };
    }
}

/// <summary>نتیجهٔ «ایده ← طرح»: شناسهٔ طرح‌های ساخته‌شده، هشدارها (مثل ادعای تخفیف غیرواقعی) و رنگ عکس کالا.</summary>
public sealed record BriefOutcome(IReadOnlyList<Guid> Ids, IReadOnlyList<string> Warnings, string? ImageColor);
