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
    ILogger<DesignStudio> logger)
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
        input = WithImageSizes(input);
        var ideas = DesignIdeas.Generate(input, DateTimeOffset.UtcNow, max * 3);
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

    private async Task<bool> CreateIdeaAsync(DesignIdea idea, CancellationToken cancellationToken)
    {
        var preset = DesignPresets.Find("hero")!;
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
            UpdatedByName = Actor,
        };
        try
        {
            await designs.AddAsync(doc, cancellationToken);
            return true;
        }
        catch
        {
            uploads.Delete(UploadArea.Banners, new[] { imageName, mobileName }.Where(n => n is not null).Select(n => n!));
            throw;
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
        IFormFile? image, IFormFile? mobileImage, string? headline, string? actor, CancellationToken cancellationToken)
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
            result = await designs.SaveAsync(id, title, json, mobileJson, imageName, mobileName, headline, actor, cancellationToken);
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
