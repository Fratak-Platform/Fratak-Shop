using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Insights;
using Fratak_Shop.Data.Entities.Insights;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Reports;

/// <summary>
/// صفحهٔ «گزارش‌های خودکار» (/admin/reports/scheduled، مرحلهٔ هفتم – فاز ۷ب).
/// <para>
/// تعریف گزارش روزانه/هفتگی/ماهانه با بخش‌های دلخواه (فروش، کارهای مانده، بازدید، موجودی، هزینه و سود، هدف ماه، پشتیبانی، پرفروش‌ها)
/// و ارسال خودکار به ایمیل، ربات بله یا ربات تلگرام. پیش‌نمایش و «ارسال آزمایشی» همین حالا.
/// </para>
/// دسترسی: «گزارش مالی» یا «تنظیمات فروشگاه»؛ تنظیم SMTP و توکن ربات‌ها فقط با «تنظیمات فروشگاه».
/// منطق: Core/Services/Insights/ScheduledReportService ؛ ارسال: Web/Infrastructure/Insights/ReportDelivery.
/// </summary>
[AdminPermission(AdminPermissions.FinanceView, AdminPermissions.SettingsManage)]
public class ScheduledModel(IScheduledReportService reports) : AdminPageModel
{
    [FromQuery(Name = "edit")] public Guid? EditId { get; set; }

    [FromQuery(Name = "preview")] public Guid? PreviewId { get; set; }

    [BindProperty] public ScheduledReportForm Form { get; set; } = new();

    [BindProperty] public DeliveryForm Delivery { get; set; } = new();

    public List<ScheduledReportRow> Reports { get; private set; } = [];
    public ReportDeliveryView DeliveryView { get; private set; } = new(null, 587, null, null, null, false, false, false);
    public ReportDigest? Preview { get; private set; }
    public string? PreviewTitle { get; private set; }

    public bool CanConfigure => Can(AdminPermissions.SettingsManage);
    public bool FormOpen { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        await LoadAsync();
        if (EditId is { } id)
        {
            var row = await reports.GetAsync(id, ct);
            if (row is null)
                return NotFound();
            Form = ScheduledReportForm.From(row);
            FormOpen = true;
        }
        else
        {
            FormOpen = Request.Query.ContainsKey("new") || Reports.Count == 0;
        }

        if (PreviewId is { } previewId && await reports.GetAsync(previewId, ct) is { } report)
        {
            Preview = await reports.BuildDigestAsync(report.Title, report.Frequency, report.Sections, DateTimeOffset.UtcNow, ct);
            PreviewTitle = report.Title;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        var result = await reports.SaveAsync(Form.ToInput(), ActorId, HttpContext.RequestAborted);
        if (!Succeeded(result))
        {
            ErrorMessage = result.Message;
            FormOpen = true;
            await LoadAsync();
            return Page();
        }
        ShowResult(result);
        return LocalRedirect("/admin/reports/scheduled");
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        ShowResult(await reports.DeleteAsync(id, HttpContext.RequestAborted));
        return LocalRedirect("/admin/reports/scheduled");
    }

    public async Task<IActionResult> OnPostToggleAsync(Guid id, bool active)
    {
        ShowResult(await reports.SetActiveAsync(id, active, HttpContext.RequestAborted));
        return LocalRedirect("/admin/reports/scheduled");
    }

    /// <summary>ارسال همین حالا (آزمایشی) با همهٔ کانال‌های گزارش؛ نتیجه در «وضعیت آخرین ارسال» هم ثبت می‌شود.</summary>
    public async Task<IActionResult> OnPostSendAsync(Guid id)
    {
        ShowResult(await reports.SendNowAsync(id, HttpContext.RequestAborted));
        return LocalRedirect("/admin/reports/scheduled");
    }

    [AdminPermission(AdminPermissions.SettingsManage)]
    public async Task<IActionResult> OnPostDeliveryAsync()
    {
        ShowResult(await reports.SaveDeliveryAsync(new ReportDeliveryInput(Delivery.SmtpHost, Delivery.SmtpPort, Delivery.SmtpUserName, Delivery.SmtpPassword,
            Delivery.ClearSmtpPassword, Delivery.FromAddress, Delivery.FromName, Delivery.BaleToken, Delivery.ClearBaleToken, Delivery.TelegramToken,
            Delivery.ClearTelegramToken), HttpContext.RequestAborted));
        return LocalRedirect("/admin/reports/scheduled#delivery");
    }

    private async Task LoadAsync()
    {
        var ct = HttpContext.RequestAborted;
        Reports = await reports.ListAsync(ct);
        DeliveryView = await reports.GetDeliveryAsync(ct);
    }

    /// <summary>متن کانال‌های یک گزارش. مثال: «ایمیل، بله».</summary>
    public static string ChannelsText(ReportChannels channels)
    {
        var list = new List<string>();
        if (channels.HasFlag(ReportChannels.Email)) list.Add("ایمیل");
        if (channels.HasFlag(ReportChannels.Bale)) list.Add("بله");
        if (channels.HasFlag(ReportChannels.Telegram)) list.Add("تلگرام");
        return list.Count == 0 ? "—" : string.Join("، ", list);
    }

    /// <summary>متن زمان‌بندی. مثال: «هفتگی · شنبه ساعت ۸:۰۰».</summary>
    public static string ScheduleText(ReportFrequency frequency, int hour, int weekDay) => frequency switch
    {
        ReportFrequency.Weekly => $"هفتگی · {ScheduledReportRules.WeekDayNames[Math.Clamp(weekDay, 0, 6)]} ساعت {hour}:00",
        ReportFrequency.Monthly => $"ماهانه · روز اول ماه ساعت {hour}:00",
        _ => $"روزانه · ساعت {hour}:00",
    };
}

/// <summary>فرم گزارش. کانال‌ها و بخش‌ها به‌صورت چند چک‌باکس هم‌نام می‌آیند و با OR ترکیب می‌شوند.</summary>
public sealed class ScheduledReportForm
{
    public Guid? Id { get; set; }
    public string? Title { get; set; } = "خلاصهٔ روزانهٔ فروشگاه";
    public ReportFrequency Frequency { get; set; } = ReportFrequency.Daily;
    public int Hour { get; set; } = 8;
    public int WeekDay { get; set; }
    public List<ReportChannels> Channels { get; set; } = [];
    public List<ReportSections> Sections { get; set; } = [ReportSections.Sales, ReportSections.Orders, ReportSections.Visitors, ReportSections.Goal];
    public string? EmailTo { get; set; }
    public string? BaleChatId { get; set; }
    public string? TelegramChatId { get; set; }
    public bool IsActive { get; set; } = true;

    public ReportChannels ChannelFlags => Channels.Aggregate(ReportChannels.None, (all, c) => all | c);
    public ReportSections SectionFlags => Sections.Aggregate(ReportSections.None, (all, s) => all | s);

    public ScheduledReportInput ToInput() => new(Id, Title ?? "", Frequency, Hour, WeekDay, ChannelFlags, SectionFlags, EmailTo, BaleChatId, TelegramChatId, IsActive);

    public static ScheduledReportForm From(ScheduledReportRow row) => new()
    {
        Id = row.Id,
        Title = row.Title,
        Frequency = row.Frequency,
        Hour = row.Hour,
        WeekDay = row.WeekDay,
        Channels = Enum.GetValues<ReportChannels>().Where(c => c != ReportChannels.None && row.Channels.HasFlag(c)).ToList(),
        Sections = ScheduledReportRules.SectionTitles.Select(s => s.Section).Where(s => row.Sections.HasFlag(s)).ToList(),
        EmailTo = row.EmailTo,
        BaleChatId = row.BaleChatId,
        TelegramChatId = row.TelegramChatId,
        IsActive = row.IsActive,
    };
}

/// <summary>فرم تنظیمات کانال‌ها. رمز/توکن خالی = بدون تغییر.</summary>
public sealed class DeliveryForm
{
    public string? SmtpHost { get; set; }
    public int SmtpPort { get; set; } = 587;
    public string? SmtpUserName { get; set; }
    public string? SmtpPassword { get; set; }
    public bool ClearSmtpPassword { get; set; }
    public string? FromAddress { get; set; }
    public string? FromName { get; set; }
    public string? BaleToken { get; set; }
    public bool ClearBaleToken { get; set; }
    public string? TelegramToken { get; set; }
    public bool ClearTelegramToken { get; set; }
}
