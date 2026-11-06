using System.Globalization;
using System.Runtime.CompilerServices;

namespace Fratak_Shop.Tests;

/// <summary>
/// آزمون‌ها با همان فرهنگ ثابتی اجرا می‌شوند که Program.cs برای برنامه تنظیم می‌کند (InvariantCulture)،
/// تا نتیجه روی ویندوزِ فارسی (fa-IR، تقویم شمسی، اعشار «٫») و سرور لینوکسی CI یکی باشد.
/// آزمون‌های Integration سرویس‌های Core را بدون Program.cs صدا می‌زنند؛ پس اینجا هم باید ثابت شود.
/// </summary>
internal static class TestCulture
{
    [ModuleInitializer]
    internal static void Pin()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
    }
}
