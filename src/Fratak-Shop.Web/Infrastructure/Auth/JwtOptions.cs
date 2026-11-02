using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Fratak_Shop.Web.Infrastructure.Auth;

/// <summary>Token settings (section <c>Jwt</c>). Secrets stay in configuration / environment, never in the database.</summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "fratak-shop";
    public string Audience { get; set; } = "fratak-shop";

    /// <summary>
    /// HMAC-SHA256 signing key, at least 32 characters (env: <c>Jwt__SigningKey</c>).
    /// مرحلهٔ هفدهم: خالی = یک کلید تصادفی یک بار ساخته و رمزشده در App_Data/keys ذخیره می‌شود و همهٔ اجراها و پروسه‌ها
    /// همان را به کار می‌برند (<see cref="JwtSigningKeyProvider"/>). برای چند سرور پشت لود بالانسر حتماً تنظیم شود.
    /// </summary>
    public string? SigningKey { get; set; }

    /// <summary>Short lived access token (minutes).</summary>
    public int AccessTokenMinutes { get; set; } = 10;

    /// <summary>Refresh token lifetime for "remember me" logins (days, sliding).</summary>
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>Refresh token lifetime for browser-session logins (hours, fixed).</summary>
    public int SessionRefreshTokenHours { get; set; } = 12;

    public TimeSpan AccessTokenLifetime => TimeSpan.FromMinutes(Math.Clamp(AccessTokenMinutes, 1, 60));
    public TimeSpan PersistentRefreshLifetime => TimeSpan.FromDays(Math.Clamp(RefreshTokenDays, 1, 365));
    public TimeSpan SessionRefreshLifetime => TimeSpan.FromHours(Math.Clamp(SessionRefreshTokenHours, 1, 72));
}

/// <summary>
/// کلید امضای توکن‌های ورود، یک بار در هر اجرای برنامه (مرحلهٔ هفدهم: کلید پایدار).
/// <para>ترتیب انتخاب:</para>
/// <list type="number">
/// <item><c>Jwt:SigningKey</c> (یا متغیر محیطی <c>Jwt__SigningKey</c>) اگر تنظیم شده باشد: حداقل ۳۲ نویسه، وگرنه برنامه بالا نمی‌آید.</item>
/// <item>وگرنه کلید تصادفی ۶۴ بایتی که <b>یک بار</b> ساخته و رمزشده (DataProtection) در پوشهٔ کلیدها ذخیره می‌شود
/// (<c>App_Data/keys/jwt-signing-key.dat</c>) و در اجراهای بعدی همان خوانده می‌شود.</item>
/// <item>فقط اگر نوشتن در آن پوشه ممکن نباشد: کلید موقت حافظه‌ای (با خطای بلند در لاگ و صفحهٔ سلامت).</item>
/// </list>
/// <para>چرا مهم است: قبلاً بدون تنظیم، هر بار که IIS برنامه را بازیافت می‌کرد (بیکاری ۲۰ دقیقه، بازیافت دوره‌ای، دو پروسهٔ
/// هم‌زمان هنگام بازیافت) کلید عوض می‌شد و همهٔ توکن‌ها باطل می‌شدند: خروج ناگهانی، رفتن به صفحهٔ ورود وسط کار و
/// «دسترسی ندارید». حالا همهٔ پروسه‌های روی یک سرور و همهٔ اجراها یک کلید دارند.</para>
/// </summary>
public sealed class JwtSigningKeyProvider
{
    /// <summary>نام فایل کلید در پوشهٔ کلیدهای DataProtection.</summary>
    public const string FileName = "jwt-signing-key.dat";

    private const string Purpose = "Fratak.Jwt.SigningKey.v1";

    public SymmetricSecurityKey Key { get; }

    /// <summary>منبع کلید برای صفحهٔ سلامت: configured | persisted | ephemeral.</summary>
    public string Source { get; }

    /// <summary>اگر کلید پایدار ساخته نشد، دلیلش (برای صفحهٔ سلامت)؛ وگرنه null.</summary>
    public string? Problem { get; }

    public JwtSigningKeyProvider(IOptions<JwtOptions> options, IWebHostEnvironment environment, IConfiguration configuration,
        Microsoft.AspNetCore.DataProtection.IDataProtectionProvider dataProtection, ILogger<JwtSigningKeyProvider> logger)
    {
        var configured = options.Value.SigningKey;

        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (configured.Length < 32)
                throw new InvalidOperationException("Jwt:SigningKey must be at least 32 characters long.");

            Key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(configured)) { KeyId = "configured" };
            Source = "configured";
            return;
        }

        var folder = configuration["Security:DataProtectionKeysPath"] is { Length: > 0 } path
            ? path
            : Path.Combine(environment.ContentRootPath, "App_Data", "keys");
        try
        {
            var bytes = LoadOrCreate(folder, dataProtection.CreateProtector(Purpose), logger);
            Key = new SymmetricSecurityKey(bytes) { KeyId = "persisted" };
            Source = "persisted";
            return;
        }
        catch (Exception exception)
        {
            Problem = exception.Message;
            logger.LogError(exception, "The JWT signing key could not be stored in {Folder}; a temporary key is used and every restart signs users out. Give the app write access to that folder or set Jwt__SigningKey.", folder);
        }

        Key = new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64)) { KeyId = "ephemeral" };
        Source = "ephemeral";
    }

    /// <summary>
    /// کلید ذخیره‌شده را می‌خواند یا یک بار می‌سازد (امن برای چند پروسهٔ هم‌زمان: نوشتن در فایل موقت و جابه‌جایی بدون
    /// بازنویسی؛ اگر پروسهٔ دیگری زودتر ساخته بود، همان خوانده می‌شود).
    /// <para>اگر فایل خراب باشد یا با کلیدهای فعلی DataProtection باز نشود (مثلاً پوشهٔ کلیدها پاک شده)، کلید تازه جایگزین
    /// می‌شود (کاربران فقط یک بار دوباره وارد می‌شوند). مثال: اولین اجرا ← فایل ساخته می‌شود؛ اجراهای بعدی ← همان ۶۴ بایت.</para>
    /// </summary>
    public static byte[] LoadOrCreate(string folder, Microsoft.AspNetCore.DataProtection.IDataProtector protector, ILogger? logger = null)
    {
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, FileName);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (File.Exists(file))
            {
                try
                {
                    var stored = protector.Unprotect(Convert.FromBase64String(File.ReadAllText(file).Trim()));
                    if (stored.Length >= 32)
                        return stored;
                }
                catch (Exception exception) when (exception is CryptographicException or FormatException)
                {
                    logger?.LogWarning("The stored JWT signing key could not be read ({Error}); a new key replaces it (users sign in again once).", exception.Message);
                }

                File.Delete(file);
            }

            var bytes = RandomNumberGenerator.GetBytes(64);
            var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, Convert.ToBase64String(protector.Protect(bytes)));
            try
            {
                File.Move(temp, file, overwrite: false);
                return bytes;
            }
            catch (IOException) when (File.Exists(file))
            {
                // پروسهٔ دیگری هم‌زمان ساخت؛ همان را می‌خوانیم.
                File.Delete(temp);
            }
        }

        throw new IOException("The JWT signing key file could not be created in " + folder);
    }
}
