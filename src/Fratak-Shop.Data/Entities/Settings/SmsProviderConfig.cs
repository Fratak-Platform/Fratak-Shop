using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Settings;

/// <summary>سرویس‌دهندهٔ پیامک. فعلاً فقط SMS.ir؛ برای افزودن سرویس دیگر یک مقدار جدید و یک پیاده‌سازی در لایهٔ وب کافی است.</summary>
public enum SmsProvider
{
    /// <summary>پیامکی ارسال نمی‌شود؛ متن فقط در لاگ سرور نوشته می‌شود (برای توسعه و آزمون).</summary>
    LogOnly = 0,

    /// <summary>سامانهٔ پیامکی SMS.ir (REST نسخهٔ ۱: https://api.sms.ir/v1/).</summary>
    SmsIr = 1,
}

/// <summary>
/// تنظیمات سرویس پیامک (فقط یک ردیف؛ صفحهٔ «فنی ← سرویس پیامک» در پنل مدیریت).
/// <para>
/// کلید API با ISecretProtector رمزنگاری ذخیره می‌شود و هرگز در فرم یا لاگ نمایش داده نمی‌شود (فقط «ثبت شده/نشده»).
/// اگر کلیدی در پایگاه داده نباشد، مقدار <c>SmsIr:ApiKey</c> از تنظیمات سرور (متغیر محیطی SmsIr__ApiKey یا user-secrets)
/// استفاده می‌شود.
/// </para>
/// مثال: Provider = SmsIr، LineNumber = 30000123456، OtpTemplateId = 718643، OtpParameterName = «Code».
/// </summary>
public class SmsProviderConfig : BaseEntity
{
    public SmsProvider Provider { get; set; } = SmsProvider.SmsIr;

    [MaxLength(1000)]
    public string? ApiKeyProtected { get; set; }

    /// <summary>شمارهٔ خط ارسال برای پیامک‌های متنی (اعلان سفارش، پیام فروشگاه و …). مثال: 30000123456.</summary>
    public long? LineNumber { get; set; }

    /// <summary>
    /// شناسهٔ قالب «ارسال سریع/وریفای» در پنل SMS.ir برای کدهای یک‌بارمصرف. خالی = کد با متن معمولی از خط ارسال فرستاده می‌شود.
    /// قالب در پنل SMS.ir مثلاً این است: «کد تایید شما: #Code#».
    /// </summary>
    public int? OtpTemplateId { get; set; }

    /// <summary>نام متغیر کد در قالب (بدون #). مثال: Code.</summary>
    [MaxLength(50)]
    public string OtpParameterName { get; set; } = "Code";
}
