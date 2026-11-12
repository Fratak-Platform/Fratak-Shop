using System.Net;
using System.Net.Sockets;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Ops;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Ops;

// ═══════════════════════════════════════════════════════════════════════════
//  دیوار آتش سبک سایت: مسدود کردن IP/بازه و ربات‌ها (مرحلهٔ هفتم – فاز ۹)
//   • IpRange         : تجزیهٔ «1.2.3.4» یا «1.2.3.0/24» یا IPv6 و بررسی «این IP داخل بازه است؟».
//   • IpList          : فهرست IP با کاما/خط (مثل «IPهای مجاز در حالت تعمیر»).
//   • FirewallSnapshot: نسخهٔ فقط‌خواندنی قانون‌ها در حافظه؛ middleware هر درخواست را با آن می‌سنجد (بدون دیتابیس).
//   • FirewallState   : نگه‌دارندهٔ snapshot فعلی + شمارندهٔ درخواست‌های ردشده تا ذخیرهٔ دوره‌ای.
//   • FirewallService : ثبت/حذف قانون، مسدودسازی خودکار از روی تلاش‌های ناموفق ورود، پاک کردن قانون‌های منقضی.
//  middleware: Web/Infrastructure/Ops/FirewallMiddleware.cs؛ پنل: /admin/system/firewall.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// یک IP یا بازهٔ CIDR. مثال: <c>IpRange.TryParse("185.10.20.0/24", out var r)</c> و سپس <c>r.Contains(IPAddress.Parse("185.10.20.7"))</c> = true.
/// IPv4 نگاشته‌شده در IPv6 (مثل ::ffff:1.2.3.4) هم درست مقایسه می‌شود.
/// </summary>
public sealed class IpRange
{
    private readonly byte[] _network;

    private IpRange(byte[] network, int prefix, string text)
    {
        _network = network;
        Prefix = prefix;
        Text = text;
    }

    /// <summary>طول پیشوند (۳۲ برای یک IPv4 تکی، ۱۲۸ برای IPv6 تکی).</summary>
    public int Prefix { get; }

    /// <summary>شکل استاندارد. مثال: «185.10.20.0/24» یا «5.6.7.8».</summary>
    public string Text { get; }

    public bool IsSingle => Prefix == _network.Length * 8;

    public static bool TryParse(string? value, out IpRange range)
    {
        range = null!;
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var text = value.Trim();
        var slash = text.IndexOf('/');
        var ipText = slash < 0 ? text : text[..slash];
        if (!IPAddress.TryParse(ipText, out var ip))
            return false;
        ip = Normalize(ip);
        var bytes = ip.GetAddressBytes();
        var max = bytes.Length * 8;
        var prefix = max;
        if (slash >= 0 && (!int.TryParse(text[(slash + 1)..], System.Globalization.NumberStyles.None, null, out prefix) || prefix < 0 || prefix > max))
            return false;
        // بیت‌های میزبان صفر می‌شوند تا «1.2.3.4/24» همان «1.2.3.0/24» باشد.
        for (var i = 0; i < bytes.Length; i++)
        {
            var keep = Math.Clamp(prefix - i * 8, 0, 8);
            bytes[i] &= (byte)(0xFF << (8 - keep));
        }
        var canonical = new IPAddress(bytes).ToString();
        range = new IpRange(bytes, prefix, prefix == max ? canonical : $"{canonical}/{prefix}");
        return true;
    }

    public bool Contains(IPAddress? address)
    {
        if (address is null)
            return false;
        var bytes = Normalize(address).GetAddressBytes();
        if (bytes.Length != _network.Length)
            return false;
        var full = Prefix / 8;
        for (var i = 0; i < full; i++)
        {
            if (bytes[i] != _network[i])
                return false;
        }
        var rest = Prefix % 8;
        if (rest == 0)
            return true;
        var mask = (byte)(0xFF << (8 - rest));
        return (bytes[full] & mask) == _network[full];
    }

    /// <summary>هر بازه با پیشوند خیلی کوتاه (مثل /4) تقریباً کل اینترنت را می‌بندد؛ حداقل /8 برای IPv4 و /16 برای IPv6.</summary>
    public bool IsTooWide => _network.Length == 4 ? Prefix < 8 : Prefix < 16;

    private static IPAddress Normalize(IPAddress ip) => ip.IsIPv4MappedToIPv6 ? ip.MapToIPv4() : ip;

    public override string ToString() => Text;
}

/// <summary>فهرست IP/بازه از متن با کاما، فاصله یا خط جدید. مثال: «5.6.7.8, 10.0.0.0/8».</summary>
public static class IpList
{
    public static (List<IpRange> Valid, List<string> Invalid) Parse(string? text)
    {
        var valid = new List<IpRange>();
        var invalid = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return (valid, invalid);
        foreach (var part in text.Split([',', '\n', '\r', ' ', '،', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (IpRange.TryParse(part, out var range))
                valid.Add(range);
            else
                invalid.Add(part);
        }
        return (valid, invalid);
    }

    public static bool Contains(IReadOnlyList<IpRange> ranges, IPAddress? ip)
    {
        foreach (var range in ranges)
        {
            if (range.Contains(ip))
                return true;
        }
        return false;
    }
}

/// <summary>یک قانون IP در حافظه.</summary>
public sealed record FirewallIpEntry(Guid Id, IpRange Range, IpRuleAction Action, DateTimeOffset? ExpiresAt);

/// <summary>یک قانون ربات در حافظه. Pattern با حروف کوچک نگه داشته می‌شود.</summary>
public sealed record FirewallBotEntry(Guid Id, string Pattern);

/// <summary>نتیجهٔ بررسی یک درخواست. RuleId برای شمردن «تعداد رد شده».</summary>
public readonly record struct FirewallDecision(bool Blocked, Guid? RuleId, string? Reason)
{
    public static readonly FirewallDecision Pass = new(false, null, null);
}

/// <summary>
/// قانون‌ها در حافظه برای بررسی سریع هر درخواست. مثال:
/// <c>snapshot.Check(IPAddress.Parse("5.6.7.8"), "sqlmap/1.7", now)</c> → Blocked=true اگر «sqlmap» یا آن IP مسدود باشد.
/// قانون «مجاز» همیشه بر «مسدود» مقدم است (IP دفتر هرگز بسته نمی‌شود).
/// </summary>
public sealed class FirewallSnapshot
{
    public static readonly FirewallSnapshot Empty = new([], [], DateTimeOffset.MinValue);

    public FirewallSnapshot(IReadOnlyList<FirewallIpEntry> ips, IReadOnlyList<FirewallBotEntry> bots, DateTimeOffset loadedAt)
    {
        Allows = ips.Where(i => i.Action == IpRuleAction.Allow).ToList();
        Blocks = ips.Where(i => i.Action == IpRuleAction.Block).ToList();
        Bots = bots;
        LoadedAt = loadedAt;
    }

    public IReadOnlyList<FirewallIpEntry> Allows { get; }
    public IReadOnlyList<FirewallIpEntry> Blocks { get; }
    public IReadOnlyList<FirewallBotEntry> Bots { get; }
    public DateTimeOffset LoadedAt { get; }

    public bool IsEmpty => Blocks.Count == 0 && Bots.Count == 0;

    public FirewallDecision Check(IPAddress? ip, string? userAgent, DateTimeOffset now)
    {
        if (IsEmpty)
            return FirewallDecision.Pass;
        foreach (var allow in Allows)
        {
            if ((allow.ExpiresAt is null || allow.ExpiresAt > now) && allow.Range.Contains(ip))
                return FirewallDecision.Pass;
        }
        foreach (var block in Blocks)
        {
            if ((block.ExpiresAt is null || block.ExpiresAt > now) && block.Range.Contains(ip))
                return new FirewallDecision(true, block.Id, "ip");
        }
        if (!string.IsNullOrEmpty(userAgent) && Bots.Count > 0)
        {
            foreach (var bot in Bots)
            {
                if (userAgent.Contains(bot.Pattern, StringComparison.OrdinalIgnoreCase))
                    return new FirewallDecision(true, bot.Id, "bot");
            }
        }
        return FirewallDecision.Pass;
    }
}

/// <summary>
/// نگه‌دارندهٔ snapshot فعلی (برای همهٔ درخواست‌ها) و شمارندهٔ ردشده‌ها. FirewallService بعد از هر تغییر و
/// سرویس پس‌زمینهٔ فنی هر دقیقه (برای چند سرور) Reload می‌کند؛ شمارنده‌ها هر چند دقیقه در دیتابیس جمع می‌شوند.
/// </summary>
public static class FirewallState
{
    private static FirewallSnapshot _current = FirewallSnapshot.Empty;
    private static System.Collections.Concurrent.ConcurrentDictionary<Guid, long> _hits = new();

    public static FirewallSnapshot Current => Volatile.Read(ref _current);

    private static IReadOnlyList<IpRange> _trusted = [];

    /// <summary>
    /// مرحلهٔ هفدهم: IPهای مورد اعتماد از تنظیمات سرور (<c>Security:TrustedIps</c>، مثلاً IP ثابت دفتر یا خود سرور).
    /// این‌ها هرگز با دیوار آتش رد نمی‌شوند، هرگز خودکار مسدود نمی‌شوند و محدودیت تعداد درخواست ندارند.
    /// چرا در تنظیمات و نه فقط در پنل: اگر مالک به اشتباه یا با مسدودسازی خودکار از سایت بیرون بماند، بدون پنل هم راه برگشت دارد.
    /// </summary>
    public static IReadOnlyList<IpRange> Trusted => Volatile.Read(ref _trusted);

    /// <summary>ثبت IPهای مورد اعتماد (Program.cs در شروع برنامه).</summary>
    public static void SetTrusted(IReadOnlyList<IpRange> ranges) => Volatile.Write(ref _trusted, ranges);

    /// <summary>آیا این IP در فهرست مورد اعتماد تنظیمات است؟ مثال: TrustedIps = «5.6.7.0/24» و IP «5.6.7.9» ← true.</summary>
    public static bool IsTrusted(IPAddress? ip) => IsTrusted(ip, Trusted);

    /// <summary>نسخهٔ خالص (تست‌پذیر) با فهرست داده‌شده.</summary>
    public static bool IsTrusted(IPAddress? ip, IReadOnlyList<IpRange> trusted)
    {
        if (ip is null)
            return false;
        foreach (var range in trusted)
        {
            if (range.Contains(ip))
                return true;
        }
        return false;
    }

    public static void Set(FirewallSnapshot snapshot) => Volatile.Write(ref _current, snapshot);

    public static void CountHit(Guid ruleId) => _hits.AddOrUpdate(ruleId, 1, (_, n) => n + 1);

    /// <summary>شمارنده‌ها را برمی‌دارد و صفر می‌کند (برای ذخیره).</summary>
    public static IReadOnlyDictionary<Guid, long> DrainHits() => Interlocked.Exchange(ref _hits, new());
}

/// <summary>ورودی فرم قانون IP. Hours خالی = دائمی.</summary>
public sealed record IpRuleInput(string Value, IpRuleAction Action, string? Reason, int? Hours);

/// <summary>خلاصهٔ IP مشکوک برای پنل: تعداد ورود ناموفق در ۲۴ ساعت و این‌که الان مسدود است یا نه.</summary>
public sealed record SuspiciousIp(string Ip, int FailedLogins, DateTimeOffset LastAt, bool IsBlocked);

/// <summary>سرویس قانون‌های دیوار آتش. محل استفاده: /admin/system/firewall و OpsWorker.</summary>
public interface IFirewallService
{
    Task<List<IpRule>> ListIpRulesAsync(CancellationToken cancellationToken = default);
    Task<List<BotRule>> ListBotRulesAsync(CancellationToken cancellationToken = default);

    /// <param name="currentIp">IP خود کارمند؛ مسدود کردن IP خودتان مجاز نیست (قفل شدن بیرون از پنل).</param>
    Task<OperationResult> AddIpRuleAsync(IpRuleInput input, string? staffName, IPAddress? currentIp, CancellationToken cancellationToken = default);
    Task<OperationResult> DeleteIpRuleAsync(Guid id, CancellationToken cancellationToken = default);
    Task<OperationResult> AddBotRuleAsync(string pattern, string? reason, CancellationToken cancellationToken = default);
    Task<OperationResult> SetBotRuleActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default);
    Task<OperationResult> DeleteBotRuleAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>IPهایی که در ۲۴ ساعت اخیر حداقل minFailures بار ورود ناموفق داشته‌اند.</summary>
    Task<List<SuspiciousIp>> SuspiciousAsync(int minFailures, CancellationToken cancellationToken = default);

    /// <summary>بارگذاری دوبارهٔ قانون‌ها در حافظه (FirewallState).</summary>
    Task<FirewallSnapshot> ReloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// مسدودسازی خودکار: IPهایی که در یک ساعت اخیر threshold بار یا بیشتر ورود ناموفق داشته‌اند برای hours ساعت بسته می‌شوند
    /// (به‌جز IPهای مجاز و IPهایی که همین الان قانون دارند). خروجی: تعداد IP مسدودشده.
    /// </summary>
    Task<int> AutoBlockAsync(int threshold, int hours, DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>ذخیرهٔ شمارنده‌های «ردشده» و حذف قانون‌های منقضی. خروجی: تعداد قانون حذف‌شده.</summary>
    Task<int> MaintainAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>قانون‌های پیش‌فرض ربات‌های مخرب (ابزارهای اسکن و حمله)، فقط یک بار.</summary>
    Task EnsureDefaultsAsync(CancellationToken cancellationToken = default);
}

public sealed class FirewallService(FratakShopDbContext context, ILogger<FirewallService> logger) : IFirewallService
{
    /// <summary>
    /// ابزارهای اسکن/حمله که هیچ بازدیدکنندهٔ واقعی ندارند. موتورهای جست‌وجو و ربات‌های پیش‌نمایش (تلگرام، واتس‌اپ) عمداً اینجا نیستند.
    /// </summary>
    public static readonly (string Pattern, string Reason)[] DefaultBots =
    [
        ("sqlmap", "ابزار حملهٔ تزریق SQL"),
        ("nikto", "اسکنر آسیب‌پذیری"),
        ("nmap", "اسکنر شبکه"),
        ("masscan", "اسکنر گستردهٔ پورت"),
        ("zgrab", "اسکنر گستردهٔ اینترنت"),
        ("acunetix", "اسکنر آسیب‌پذیری"),
        ("wpscan", "اسکنر وردپرس (این سایت وردپرس نیست)"),
        ("nuclei", "اسکنر خودکار آسیب‌پذیری"),
        ("dirbuster", "حدس زدن مسیرهای مخفی"),
        ("gobuster", "حدس زدن مسیرهای مخفی"),
        ("hydra", "ابزار حدس رمز"),
    ];

    /// <summary>مقدار ویژهٔ حاشیه برای ثبت «پیش‌فرض‌ها اضافه شد» تا با حذف همه دوباره برنگردند.</summary>
    private const string SeedMarker = "__defaults__";

    public Task<List<IpRule>> ListIpRulesAsync(CancellationToken cancellationToken = default) =>
        context.IpRules.AsNoTracking().OrderBy(r => r.Action).ThenByDescending(r => r.CreatedAt).ToListAsync(cancellationToken);

    public Task<List<BotRule>> ListBotRulesAsync(CancellationToken cancellationToken = default) =>
        context.BotRules.AsNoTracking().Where(r => r.Pattern != SeedMarker).OrderBy(r => r.Pattern).ToListAsync(cancellationToken);

    public async Task<OperationResult> AddIpRuleAsync(IpRuleInput input, string? staffName, IPAddress? currentIp, CancellationToken cancellationToken = default)
    {
        if (!IpRange.TryParse(input.Value, out var range))
            return OperationResult.Error("IP یا بازه معتبر نیست. مثال: 5.6.7.8 یا 5.6.7.0/24");
        if (input.Action == IpRuleAction.Block)
        {
            if (range.IsTooWide)
                return OperationResult.Error("این بازه خیلی بزرگ است (بخش بزرگی از اینترنت بسته می‌شد). حداقل /8 برای IPv4 و /16 برای IPv6.");
            if (currentIp is not null && range.Contains(currentIp))
                return OperationResult.Error("این قانون IP خود شما را هم می‌بندد و دسترسی‌تان به پنل قطع می‌شد.");
            if (IPAddress.IsLoopback(IPAddress.Parse(range.Text.Split('/')[0])))
                return OperationResult.Error("IP خود سرور (loopback) را نمی‌توان مسدود کرد.");
        }
        if (input.Hours is < 1 or > 24 * 365)
            return OperationResult.Error("مدت باید بین ۱ ساعت تا ۱ سال باشد (یا خالی برای دائمی).");
        var reason = string.IsNullOrWhiteSpace(input.Reason) ? null : input.Reason.Trim();
        if (reason?.Length > 300)
            return OperationResult.Error("دلیل حداکثر ۳۰۰ نویسه.");

        var existing = await context.IpRules.AsTracking().FirstOrDefaultAsync(r => r.Value == range.Text, cancellationToken);
        if (existing is not null)
        {
            // قانون قبلی همین IP به‌روز می‌شود (مثلاً تمدید مسدودی یا تبدیل به مجاز).
            existing.Action = input.Action;
            existing.Reason = reason;
            existing.ExpiresAt = input.Hours is { } h ? DateTimeOffset.UtcNow.AddHours(h) : null;
            existing.IsAutomatic = false;
            existing.CreatedBy = staffName;
        }
        else
        {
            context.IpRules.Add(new IpRule
            {
                Value = range.Text,
                Action = input.Action,
                Reason = reason,
                ExpiresAt = input.Hours is { } h ? DateTimeOffset.UtcNow.AddHours(h) : null,
                CreatedBy = staffName,
            });
        }
        await context.SaveChangesAsync(cancellationToken);
        await ReloadAsync(cancellationToken);
        return OperationResult.Success(input.Action == IpRuleAction.Block ? $"«{range.Text}» مسدود شد." : $"«{range.Text}» در فهرست مجاز قرار گرفت.");
    }

    public async Task<OperationResult> DeleteIpRuleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // حذف واقعی (نه سطل بازیافت) تا ایندکس یکتای مقدار برای قانون بعدی آزاد شود.
        var removed = await context.IpRules.Where(r => r.Id == id).ExecuteDeleteAsync(cancellationToken);
        if (removed == 0)
            return OperationResult.NotFound("قانون پیدا نشد.");
        await ReloadAsync(cancellationToken);
        return OperationResult.Success("قانون حذف شد.");
    }

    public async Task<OperationResult> AddBotRuleAsync(string pattern, string? reason, CancellationToken cancellationToken = default)
    {
        var value = (pattern ?? "").Trim().ToLowerInvariant();
        if (value.Length is < 3 or > 120 || value == SeedMarker)
            return OperationResult.Error("بخشی از User-Agent با ۳ تا ۱۲۰ نویسه وارد کنید. مثال: sqlmap");
        if (BotRules.IsProtected(value))
            return OperationResult.Error("این الگو موتورهای جست‌وجو یا مرورگرهای عادی را هم می‌بندد و به سئو و فروش آسیب می‌زند.");
        if (await context.BotRules.AnyAsync(r => r.Pattern == value, cancellationToken))
            return OperationResult.Error("این الگو قبلاً ثبت شده است.");
        context.BotRules.Add(new BotRule { Pattern = value, Reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim()[..Math.Min(300, reason.Trim().Length)] });
        await context.SaveChangesAsync(cancellationToken);
        await ReloadAsync(cancellationToken);
        return OperationResult.Success($"ربات‌هایی که «{value}» در User-Agent دارند مسدود شدند.");
    }

    public async Task<OperationResult> SetBotRuleActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var changed = await context.BotRules.Where(r => r.Id == id && r.Pattern != SeedMarker)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.IsActive, active).SetProperty(r => r.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
        if (changed == 0)
            return OperationResult.NotFound("قانون پیدا نشد.");
        await ReloadAsync(cancellationToken);
        return OperationResult.Success(active ? "قانون فعال شد." : "قانون غیرفعال شد.");
    }

    public async Task<OperationResult> DeleteBotRuleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var removed = await context.BotRules.Where(r => r.Id == id && r.Pattern != SeedMarker).ExecuteDeleteAsync(cancellationToken);
        if (removed == 0)
            return OperationResult.NotFound("قانون پیدا نشد.");
        await ReloadAsync(cancellationToken);
        return OperationResult.Success("قانون حذف شد.");
    }

    public async Task<List<SuspiciousIp>> SuspiciousAsync(int minFailures, CancellationToken cancellationToken = default)
    {
        var since = DateTimeOffset.UtcNow.AddHours(-24);
        var rows = await context.SecurityLogs.AsNoTracking()
            .Where(l => (l.Type == SecurityEventType.LoginFailed || l.Type == SecurityEventType.OtpFailed || l.Type == SecurityEventType.TotpFailed) && l.CreatedAt >= since && l.IpAddress != null)
            .GroupBy(l => l.IpAddress!)
            .Select(g => new { Ip = g.Key, Count = g.Count(), Last = g.Max(l => l.CreatedAt) })
            .Where(x => x.Count >= minFailures)
            .OrderByDescending(x => x.Count)
            .Take(30)
            .ToListAsync(cancellationToken);
        var snapshot = FirewallState.Current;
        var now = DateTimeOffset.UtcNow;
        return rows.Select(r => new SuspiciousIp(r.Ip, r.Count, r.Last,
            IPAddress.TryParse(r.Ip, out var ip) && snapshot.Check(ip, null, now).Blocked)).ToList();
    }

    public async Task<FirewallSnapshot> ReloadAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var ips = await context.IpRules.AsNoTracking().Where(r => r.ExpiresAt == null || r.ExpiresAt > now)
            .Select(r => new { r.Id, r.Value, r.Action, r.ExpiresAt }).ToListAsync(cancellationToken);
        var bots = await context.BotRules.AsNoTracking().Where(r => r.IsActive && r.Pattern != SeedMarker)
            .Select(r => new { r.Id, r.Pattern }).ToListAsync(cancellationToken);
        var entries = new List<FirewallIpEntry>(ips.Count);
        foreach (var rule in ips)
        {
            if (IpRange.TryParse(rule.Value, out var range))
                entries.Add(new FirewallIpEntry(rule.Id, range, rule.Action, rule.ExpiresAt));
            else
                logger.LogWarning("Ignoring invalid IP rule {Value}", rule.Value);
        }
        var snapshot = new FirewallSnapshot(entries, bots.Select(b => new FirewallBotEntry(b.Id, b.Pattern.ToLowerInvariant())).ToList(), now);
        FirewallState.Set(snapshot);
        return snapshot;
    }

    public async Task<int> AutoBlockAsync(int threshold, int hours, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var since = now.AddHours(-1);
        var candidates = await context.SecurityLogs.AsNoTracking()
            .Where(l => (l.Type == SecurityEventType.LoginFailed || l.Type == SecurityEventType.OtpFailed || l.Type == SecurityEventType.TotpFailed) && l.CreatedAt >= since && l.IpAddress != null)
            .GroupBy(l => l.IpAddress!)
            .Select(g => new { Ip = g.Key, Count = g.Count() })
            .Where(x => x.Count >= threshold)
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0)
            return 0;

        var snapshot = await ReloadAsync(cancellationToken);
        var allows = snapshot.Allows.Select(a => a.Range).ToList();
        var staffIps = await StaffIpsAsync(now, cancellationToken);
        // قانون‌های موجود (حتی منقضی‌شده‌هایی که هنوز پاک نشده‌اند) به‌خاطر ایندکس یکتای Value؛ منقضی‌ها تمدید می‌شوند.
        // بدون ردیابی خوانده می‌شود تا مقدار واقعی دیتابیس دیده شود (نه نسخهٔ قدیمی‌ای که همین DbContext قبلاً ردیابی کرده).
        var existing = (await context.IpRules.AsNoTracking().ToListAsync(cancellationToken)).ToDictionary(r => r.Value, StringComparer.OrdinalIgnoreCase);
        var added = 0;
        var newRules = new List<IpRule>();
        foreach (var candidate in candidates)
        {
            if (!IpRange.TryParse(candidate.Ip, out var range) || !range.IsSingle)
                continue;
            var ip = IPAddress.Parse(range.Text);
            // IP مجاز یا IP خود سرور/شبکهٔ داخلی (پراکسی معکوس) هرگز خودکار بسته نمی‌شود.
            // مرحلهٔ هفدهم: IPهای مورد اعتماد تنظیمات و IPهایی که کارکنان در ۳۰ روز اخیر از آن‌ها موفق وارد شده‌اند (دفتر، خانهٔ مالک)
            // هم خودکار بسته نمی‌شوند؛ چند بار اشتباه زدن رمز نباید مالک را از سایت بیرون بیندازد.
            if (IPAddress.IsLoopback(ip) || IsPrivate(ip) || IpList.Contains(allows, ip) || FirewallState.IsTrusted(ip) || staffIps.Contains(range.Text))
                continue;
            var reason = $"مسدودسازی خودکار: {PersianNumbers.Digits(candidate.Count)} ورود ناموفق در یک ساعت";
            if (existing.TryGetValue(range.Text, out var rule))
            {
                // قانون فعال (مسدود یا مجاز دستی) دست نمی‌خورد؛ فقط مسدودی منقضی‌شده دوباره فعال می‌شود.
                if (rule.Action != IpRuleAction.Block || rule.ExpiresAt is null || rule.ExpiresAt > now)
                    continue;
                var expires = now.AddHours(hours);
                await context.IpRules.Where(r => r.Id == rule.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.ExpiresAt, expires)
                    .SetProperty(r => r.IsAutomatic, true)
                    .SetProperty(r => r.Reason, reason)
                    .SetProperty(r => r.CreatedBy, "سیستم")
                    .SetProperty(r => r.UpdatedAt, now), cancellationToken);
            }
            else
            {
                newRules.Add(new IpRule
                {
                    Value = range.Text,
                    Action = IpRuleAction.Block,
                    IsAutomatic = true,
                    ExpiresAt = now.AddHours(hours),
                    Reason = reason,
                    CreatedBy = "سیستم",
                });
            }
            added++;
        }
        if (added == 0)
            return 0;
        if (newRules.Count > 0)
        {
            context.IpRules.AddRange(newRules);
            await context.SaveChangesAsync(cancellationToken);
            // ردیف‌های تازه از ردیابی خارج می‌شوند تا اجرای بعدی (در همین scope) مقدار تازهٔ دیتابیس را ببیند.
            foreach (var entity in newRules)
                context.Entry(entity).State = EntityState.Detached;
        }
        await ReloadAsync(cancellationToken);
        logger.LogWarning("Firewall auto-blocked {Count} IP address(es) after repeated failed logins", added);
        return added;
    }

    public async Task<int> MaintainAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        foreach (var (id, hits) in FirewallState.DrainHits())
        {
            // هر قانون در یکی از دو جدول است؛ دستور روی جدول دیگر ۰ ردیف تغییر می‌دهد.
            var updated = await context.IpRules.Where(r => r.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(r => r.Hits, r => r.Hits + hits).SetProperty(r => r.LastHitAt, now), cancellationToken);
            if (updated == 0)
                await context.BotRules.Where(r => r.Id == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(r => r.Hits, r => r.Hits + hits).SetProperty(r => r.LastHitAt, now), cancellationToken);
        }
        // قانون‌های منقضی یک روز بعد از انقضا پاک می‌شوند (تا در پنل دیده شوند که چه چیزی تمام شد).
        var cutoff = now.AddDays(-1);
        var removed = await context.IpRules.Where(r => r.ExpiresAt != null && r.ExpiresAt < cutoff).ExecuteDeleteAsync(cancellationToken);
        await ReloadAsync(cancellationToken);
        return removed;
    }

    public async Task EnsureDefaultsAsync(CancellationToken cancellationToken = default)
    {
        if (await context.BotRules.IgnoreQueryFilters().AnyAsync(r => r.Pattern == SeedMarker, cancellationToken))
            return;
        var existing = (await context.BotRules.Select(r => r.Pattern).ToListAsync(cancellationToken)).ToHashSet();
        foreach (var (pattern, reason) in DefaultBots)
        {
            if (!existing.Contains(pattern))
                context.BotRules.Add(new BotRule { Pattern = pattern, Reason = reason });
        }
        // ردیف نشانه (غیرفعال، در پنل نمایش داده نمی‌شود): پیش‌فرض‌ها فقط یک بار اضافه شوند.
        context.BotRules.Add(new BotRule { Pattern = SeedMarker, IsActive = false, Reason = "نشانهٔ افزودن پیش‌فرض‌ها" });
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // سرور دیگری هم‌زمان اضافه کرد.
            context.ChangeTracker.Clear();
        }
    }

    /// <summary>
    /// مرحلهٔ هفدهم: IPهایی که کارکنان (مالک یا دارای نقش پنل) در ۳۰ روز اخیر از آن‌ها با موفقیت وارد شده‌اند (شکل استاندارد IpRange).
    /// </summary>
    private async Task<HashSet<string>> StaffIpsAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var since = now.AddDays(-30);
        const Fratak_Shop.Data.Entities.Users.UserRole owner = Fratak_Shop.Data.Entities.Users.UserRole.SuperAdmin;
        var ips = await context.SecurityLogs.AsNoTracking()
            .Where(l => l.Type == SecurityEventType.LoginSucceeded && l.CreatedAt >= since && l.IpAddress != null && l.UserId != null
                        && (context.Users.Any(u => u.Id == l.UserId && u.Role == owner)
                            || context.UserAdminRoles.Any(m => m.UserId == l.UserId)))
            .Select(l => l.IpAddress!)
            .Distinct()
            .Take(500)
            .ToListAsync(cancellationToken);
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ip in ips)
        {
            if (IpRange.TryParse(ip, out var range))
                set.Add(range.Text);
        }
        return set;
    }

    /// <summary>شبکه‌های خصوصی (10/8، 172.16/12، 192.168/16، fc00::/7) که معمولاً پراکسی یا شبکهٔ داخلی‌اند.</summary>
    public static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
        }
        return ip.AddressFamily == AddressFamily.InterNetworkV6 && (ip.GetAddressBytes()[0] & 0xFE) == 0xFC;
    }
}

/// <summary>قواعد قانون‌های ربات.</summary>
public static class BotRules
{
    /// <summary>
    /// الگوهایی که نباید مسدود شوند چون موتورهای جست‌وجو، پیش‌نمایش پیام‌رسان‌ها یا همهٔ مرورگرها را می‌گیرند.
    /// مثال: «bot» (Googlebot را هم می‌بندد)، «mozilla» (همهٔ مرورگرها).
    /// </summary>
    private static readonly string[] Protected =
    [
        "googlebot", "bingbot", "yandex", "duckduckbot", "applebot", "telegrambot", "whatsapp", "facebookexternalhit", "twitterbot",
        "mozilla", "chrome", "safari", "firefox", "android", "iphone", "windows", "linux", "mac os", "gecko", "applewebkit", "mobile",
    ];

    /// <summary>true = الگو یکی از User-Agentهای مهم را می‌گیرد (مثل «bot» که در «googlebot» هست).</summary>
    public static bool IsProtected(string pattern)
    {
        var value = pattern.Trim().ToLowerInvariant();
        return Protected.Any(p => p.Contains(value, StringComparison.Ordinal) || value.Contains(p, StringComparison.Ordinal));
    }
}

/// <summary>تبدیل عدد به رقم فارسی برای پیام‌ها (بدون وابستگی به لایهٔ Web).</summary>
public static class PersianNumbers
{
    public static string Digits(long value) => Digits(value.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '٬'));

    public static string Digits(string text)
    {
        var chars = text.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= '0' and <= '9')
                chars[i] = (char)('۰' + (chars[i] - '0'));
        }
        return new string(chars);
    }
}
