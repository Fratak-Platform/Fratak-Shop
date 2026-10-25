using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Infrastructure.Admin;

/// <summary>A table to export: column titles and rows of cell values (string, numbers, dates, bool).</summary>
public sealed class ExportTable(string title, params string[] columns)
{
    public string Title { get; } = title;
    public string[] Columns { get; } = columns;
    public List<object?[]> Rows { get; } = [];

    public ExportTable Add(params object?[] cells)
    {
        Rows.Add(cells);
        return this;
    }
}

/// <summary>
/// CSV (UTF-8 with BOM, opens correctly in Excel) and XLSX (real spreadsheet, numbers stay numbers)
/// exports without extra packages. Text cells that start with = + - @ are prefixed so a spreadsheet
/// never evaluates them as formulas (CSV injection).
/// </summary>
public static class Exporter
{
    public static IActionResult File(ExportTable table, string format, string baseName)
    {
        var stamp = Helpers.Format.Date(DateTimeOffset.UtcNow).Replace('/', '-');
        var safeName = $"{baseName}-{stamp}";
        return string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase)
            ? new FileContentResult(Csv(table), "text/csv; charset=utf-8") { FileDownloadName = safeName + ".csv" }
            : new FileContentResult(Xlsx(table), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet") { FileDownloadName = safeName + ".xlsx" };
    }

    public static byte[] Csv(ExportTable table)
    {
        var builder = new StringBuilder();
        builder.AppendJoin(',', table.Columns.Select(Escape)).Append("\r\n");
        foreach (var row in table.Rows)
            builder.AppendJoin(',', row.Select(cell => Escape(Text(cell)))).Append("\r\n");

        var preamble = Encoding.UTF8.GetPreamble();
        var body = Encoding.UTF8.GetBytes(builder.ToString());
        return [.. preamble, .. body];
    }

    private static string Text(object? value) => value switch
    {
        null => "",
        DateTimeOffset date => Helpers.Format.DateTime(date),
        DateTime date => Helpers.Format.DateTime(new DateTimeOffset(date, TimeSpan.Zero)),
        bool flag => flag ? "بله" : "خیر",
        decimal number => number.ToString("0.##", CultureInfo.InvariantCulture),
        double number => number.ToString("0.##", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => Guard(value.ToString() ?? ""),
    };

    private static string Guard(string text) =>
        text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? "'" + text : text;

    private static string Escape(string value) =>
        value.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    public static byte[] Xlsx(ExportTable table)
    {
        using var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                <Default Extension="xml" ContentType="application/xml"/>
                <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                </Types>
                """);
            Write(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            var sheetName = SecurityElement.Escape(new string(table.Title.Where(c => !"[]:*?/\\".Contains(c)).Take(31).ToArray()) is { Length: > 0 } name ? name : "Sheet1");
            Write(zip, "xl/workbook.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                <workbookViews><workbookView rightToLeft="1"/></workbookViews>
                <sheets><sheet name="{sheetName}" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);
            Write(zip, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """);
            // Style 1: bold header, style 2: thousands separator.
            Write(zip, "xl/styles.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                <fonts count="2"><font><sz val="11"/><name val="Tahoma"/></font><font><b/><sz val="11"/><name val="Tahoma"/></font></fonts>
                <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                <borders count="1"><border/></borders>
                <cellStyleXfs count="1"><xf/></cellStyleXfs>
                <cellXfs count="3"><xf/><xf fontId="1" applyFont="1"/><xf numFmtId="3" applyNumberFormat="1"/></cellXfs>
                </styleSheet>
                """);

            var sheet = new StringBuilder();
            sheet.Append("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetViews><sheetView rightToLeft="1" workbookViewId="0"><pane ySplit="1" topLeftCell="A2" activePane="bottomLeft" state="frozen"/></sheetView></sheetViews><sheetData>""");
            sheet.Append("<row r=\"1\">");
            foreach (var column in table.Columns)
                sheet.Append("<c t=\"inlineStr\" s=\"1\"><is><t>").Append(Xml(column)).Append("</t></is></c>");
            sheet.Append("</row>");

            var rowIndex = 1;
            foreach (var row in table.Rows)
            {
                rowIndex++;
                sheet.Append("<row r=\"").Append(rowIndex).Append("\">");
                foreach (var cell in row)
                {
                    switch (cell)
                    {
                        case null:
                            sheet.Append("<c/>");
                            break;
                        case int or long or short or byte:
                            sheet.Append("<c><v>").Append(Convert.ToString(cell, CultureInfo.InvariantCulture)).Append("</v></c>");
                            break;
                        case decimal or double or float:
                            sheet.Append("<c s=\"2\"><v>").Append(Convert.ToString(cell, CultureInfo.InvariantCulture)).Append("</v></c>");
                            break;
                        default:
                            sheet.Append("<c t=\"inlineStr\"><is><t xml:space=\"preserve\">").Append(Xml(Text(cell))).Append("</t></is></c>");
                            break;
                    }
                }
                sheet.Append("</row>");
            }

            sheet.Append("</sheetData></worksheet>");
            Write(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
        }

        return stream.ToArray();
    }

    private static string Xml(string value)
    {
        // Strip characters that are invalid in XML 1.0.
        var clean = new string(value.Where(c => c == '\t' || c == '\n' || c == '\r' || c >= ' ').ToArray());
        return SecurityElement.Escape(clean) ?? "";
    }

    private static void Write(ZipArchive zip, string path, string content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(content.Trim());
    }
}

/// <summary>Persian (Jalali) date input of the admin filters: «1404/08/03» (Persian or Latin digits).</summary>
public static class PersianDateInput
{
    private static readonly PersianCalendar Calendar = new();

    public static DateOnly? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = Core.Utilities.SearchText.NormalizeDigits(value).Replace('-', '/').Replace('.', '/');
        var parts = text.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 3 || !int.TryParse(parts[0], out var year) || !int.TryParse(parts[1], out var month) || !int.TryParse(parts[2], out var day))
            return null;

        if (year < 100)
            year += 1400;

        // Gregorian input (from <input type=date>) is accepted too.
        if (year > 1700)
            return year <= 2200 && month is >= 1 and <= 12 && day >= 1 && day <= DateTime.DaysInMonth(year, month) ? new DateOnly(year, month, day) : null;

        if (year is < 1300 or > 1500 || month is < 1 or > 12 || day < 1 || day > Calendar.GetDaysInMonth(year, month))
            return null;

        return DateOnly.FromDateTime(Calendar.ToDateTime(year, month, day, 0, 0, 0, 0));
    }

    public static string Format(DateOnly? date)
    {
        if (date is null)
            return "";
        var value = date.Value.ToDateTime(TimeOnly.MinValue);
        return $"{Calendar.GetYear(value)}/{Calendar.GetMonth(value):00}/{Calendar.GetDayOfMonth(value):00}";
    }

    /// <summary>
    /// تاریخ و ساعت شمسی به وقت ایران؛ مثال: «1404/08/10 18:30» → همان لحظه. بدون ساعت ← <paramref name="defaultTime"/>
    /// (پیش‌فرض ابتدای روز). نامعتبر → null. ورودی <c>data-jalali="datetime"</c> همین قالب را می‌سازد.
    /// </summary>
    public static DateTimeOffset? ParseDateTime(string? value, TimeOnly? defaultTime = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var text = Core.Utilities.SearchText.NormalizeDigits(value).Trim();
        var parts = text.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var date = Parse(parts[0]);
        if (date is null)
            return null;
        var time = defaultTime ?? TimeOnly.MinValue;
        if (parts.Length == 2)
        {
            if (!TimeOnly.TryParseExact(parts[1], ["H:mm", "HH:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
                return null;
        }
        return Core.Utilities.IranClock.At(date.Value, time);
    }

    /// <summary>«1404/08/10 18:30» به وقت ایران (برای پر کردن دوبارهٔ فرم).</summary>
    public static string FormatDateTime(DateTimeOffset? moment)
    {
        if (moment is null)
            return "";
        var local = Core.Utilities.IranClock.ToLocal(moment.Value);
        return $"{Format(DateOnly.FromDateTime(local.DateTime))} {local:HH:mm}";
    }

    /// <summary>Iran-local [from, to) period of two inclusive Persian dates.</summary>
    public static Core.DTOs.Reports.ReportPeriod Period(string? from, string? to)
    {
        var start = Parse(from);
        var end = Parse(to);
        return new Core.DTOs.Reports.ReportPeriod(
            start is { } s ? Core.Utilities.IranClock.StartOfDay(s) : null,
            end is { } e ? Core.Utilities.IranClock.StartOfDay(e.AddDays(1)) : null);
    }
}
