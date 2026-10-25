using System.Globalization;
using Fratak_Shop.Core.DTOs.Reports;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Web.Infrastructure.Admin;

/// <summary>A resolved report period (Iran days) with the same-length previous period for comparisons.</summary>
public sealed record ResolvedPeriod(string Preset, string Label, ReportPeriod Current, ReportPeriod? Previous, string FromText, string ToText);

/// <summary>Report period presets of the admin panel (today, last 7/30 days, Persian month/year, all time, custom).</summary>
public static class AdminPeriods
{
    private static readonly PersianCalendar Persian = new();

    public static readonly (string Key, string Title)[] Presets =
    [
        ("today", "امروز"), ("yesterday", "دیروز"), ("7d", "۷ روز اخیر"), ("30d", "۳۰ روز اخیر"),
        ("month", "ماه جاری"), ("last-month", "ماه گذشته"), ("90d", "۹۰ روز اخیر"), ("year", "سال جاری"), ("all", "از ابتدا"),
    ];

    public static ResolvedPeriod Resolve(string? preset, string? from, string? to, string fallback = "30d")
    {
        if (!string.IsNullOrWhiteSpace(from) || !string.IsNullOrWhiteSpace(to))
        {
            var start = PersianDateInput.Parse(from);
            var end = PersianDateInput.Parse(to);
            if (start is not null || end is not null)
            {
                var label = $"{(start is null ? "ابتدا" : PersianDateInput.Format(start))} تا {(end is null ? "امروز" : PersianDateInput.Format(end))}";
                return Build("custom", label, start, end);
            }
        }

        var today = IranClock.Today(DateTimeOffset.UtcNow);
        var key = Presets.Any(p => p.Key == preset) ? preset! : fallback;
        var title = Presets.First(p => p.Key == key).Title;
        var pDate = today.ToDateTime(TimeOnly.MinValue);
        var monthStart = DateOnly.FromDateTime(Persian.ToDateTime(Persian.GetYear(pDate), Persian.GetMonth(pDate), 1, 0, 0, 0, 0));

        return key switch
        {
            "today" => Build(key, title, today, today),
            "yesterday" => Build(key, title, today.AddDays(-1), today.AddDays(-1)),
            "7d" => Build(key, title, today.AddDays(-6), today),
            "90d" => Build(key, title, today.AddDays(-89), today),
            "month" => Build(key, title, monthStart, today),
            "last-month" => LastMonth(key, title, monthStart),
            "year" => Build(key, title, DateOnly.FromDateTime(Persian.ToDateTime(Persian.GetYear(pDate), 1, 1, 0, 0, 0, 0)), today),
            "all" => Build(key, title, null, null),
            _ => Build(key, title, today.AddDays(-29), today),
        };
    }

    private static ResolvedPeriod LastMonth(string key, string title, DateOnly thisMonthStart)
    {
        var lastDay = thisMonthStart.AddDays(-1);
        var d = lastDay.ToDateTime(TimeOnly.MinValue);
        var start = DateOnly.FromDateTime(Persian.ToDateTime(Persian.GetYear(d), Persian.GetMonth(d), 1, 0, 0, 0, 0));
        return Build(key, title, start, lastDay);
    }

    /// <summary>Inclusive Iran days [start, end] → [from, to) instants, plus the previous period of equal length.</summary>
    private static ResolvedPeriod Build(string key, string label, DateOnly? start, DateOnly? end)
    {
        var current = new ReportPeriod(
            start is { } s ? IranClock.StartOfDay(s) : null,
            end is { } e ? IranClock.StartOfDay(e.AddDays(1)) : null);

        ReportPeriod? previous = null;
        if (start is { } ps)
        {
            var last = end ?? IranClock.Today(DateTimeOffset.UtcNow);
            var days = last.DayNumber - ps.DayNumber + 1;
            if (days > 0)
                previous = new ReportPeriod(IranClock.StartOfDay(ps.AddDays(-days)), IranClock.StartOfDay(ps));
        }

        return new ResolvedPeriod(key, label, current, previous, PersianDateInput.Format(start), PersianDateInput.Format(end));
    }
}
