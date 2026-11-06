using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Data.Entities.Notifications;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.ViewComponents;

/// <summary>مدل نمای انتخابگر پیام آماده.</summary>
/// <param name="Items">پیام‌های فعال همین محل.</param>
/// <param name="BodyTarget">انتخابگر CSS فیلد متن، مثلاً <c>#d-body</c>.</param>
/// <param name="TitleTarget">انتخابگر CSS فیلد عنوان (اختیاری).</param>
/// <param name="LinkTarget">انتخابگر CSS فیلد لینک (اختیاری).</param>
public sealed record ReadyMessagePickerModel(List<ReadyMessageDto> Items, string BodyTarget, string? TitleTarget, string? LinkTarget);

/// <summary>
/// فهرست کشویی «پیام آماده» کنار فرم‌های ارسال پیام در پنل.
/// <para>
/// نحوهٔ استفاده در یک صفحه:
/// <code>@await Component.InvokeAsync("ReadyMessagePicker", new { scope = ReadyMessageScope.Ticket, bodyTarget = "#reply-body" })</code>
/// با انتخاب یک پیام، admin.js متن را در فیلد مقصد می‌گذارد (اگر فیلد خالی نباشد، به انتهای آن اضافه می‌کند)،
/// عنوان/لینک خالی را پر می‌کند و شمارندهٔ استفاده را یک واحد بالا می‌برد.
/// برچسب‌هایی مثل {FirstName} همان‌جا باقی می‌مانند و هنگام ارسال با اطلاعات مشتری پر می‌شوند.
/// اگر برای آن محل پیامی تعریف نشده باشد، فقط یک لینک کوچک «تعریف پیام آماده» نمایش داده می‌شود.
/// </para>
/// </summary>
public sealed class ReadyMessagePickerViewComponent(IReadyMessageService readyMessages) : ViewComponent
{
    public async Task<IViewComponentResult> InvokeAsync(ReadyMessageScope scope, string bodyTarget, string? titleTarget = null, string? linkTarget = null)
    {
        var items = await readyMessages.ListAsync(scope);
        return View(new ReadyMessagePickerModel(items, bodyTarget, titleTarget, linkTarget));
    }
}
