using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Fratak_Shop.Core.Utilities;

/// <summary>
/// خواندن سادهٔ فایل اکسل (xlsx) و CSV بدون هیچ کتابخانهٔ بیرونی.
/// <para>
/// فقط «اولین برگه» و «مقدار» سلول‌ها خوانده می‌شود (فرمول/قالب نه). برای ورود گروهی محصولات کافی است:
/// همان فایلی که از دکمهٔ «اکسل» فهرست محصولات گرفته می‌شود، ویرایش و دوباره بارگذاری می‌شود.
/// </para>
/// استفاده: ProductImportService. مثال:
/// <code>var rows = SpreadsheetReader.Read(stream, "products.xlsx"); // rows[0] = سرستون‌ها</code>
/// </summary>
public static class SpreadsheetReader
{
    /// <summary>بیشترین تعداد ردیف خوانده‌شده (جلوگیری از فایل‌های خیلی بزرگ).</summary>
    public const int MaxRows = 20_000;

    /// <summary>بیشترین تعداد ستون.</summary>
    public const int MaxColumns = 60;

    /// <summary>
    /// خواندن فایل بر اساس پسوند. خطای قالب با <see cref="InvalidDataException"/> و پیام فارسی اعلام می‌شود.
    /// </summary>
    public static List<string[]> Read(Stream stream, string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".xlsx" => ReadXlsx(stream),
            ".csv" or ".txt" => ReadCsv(stream),
            _ => throw new InvalidDataException("فقط فایل اکسل (xlsx) یا CSV پذیرفته می‌شود."),
        };
    }

    /// <summary>
    /// خواندن CSV با تشخیص خودکار جداکننده (کاما، نقطه‌ویرگول یا تب) و پشتیبانی از "نقل‌قول".
    /// BOM فایل‌های ذخیره‌شده با اکسل حذف می‌شود.
    /// </summary>
    public static List<string[]> ReadCsv(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        var firstLine = text.Split('\n', 2)[0];
        var delimiter = new[] { ',', ';', '\t' }.OrderByDescending(d => firstLine.Count(c => c == d)).First();

        var rows = new List<string[]>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else if (c == '"') quoted = false;
                else cell.Append(c);
                continue;
            }
            if (c == '"' && cell.Length == 0) { quoted = true; continue; }
            if (c == delimiter) { row.Add(cell.ToString()); cell.Clear(); continue; }
            if (c == '\r') continue;
            if (c == '\n')
            {
                row.Add(cell.ToString());
                cell.Clear();
                AddRow(rows, row);
                row = [];
                if (rows.Count >= MaxRows) break;
                continue;
            }
            cell.Append(c);
        }
        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            AddRow(rows, row);
        }
        return rows;
    }

    /// <summary>
    /// خواندن اولین برگهٔ xlsx: رشته‌های مشترک (sharedStrings)، رشتهٔ درون‌خطی (inlineStr)، عدد و بولی.
    /// جای ستون از آدرس سلول (مثل C5) خوانده می‌شود؛ اگر آدرس نداشت، پشت سر هم چیده می‌شود.
    /// </summary>
    public static List<string[]> ReadXlsx(Stream stream)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException)
        {
            throw new InvalidDataException("فایل اکسل معتبر نیست.");
        }

        using (zip)
        {
            var shared = ReadSharedStrings(zip);
            var sheet = zip.GetEntry(FirstSheetPath(zip)) ?? zip.GetEntry("xl/worksheets/sheet1.xml")
                ?? throw new InvalidDataException("برگه‌ای در فایل اکسل پیدا نشد.");

            var rows = new List<string[]>();
            using var sheetStream = sheet.Open();
            using var xml = XmlReader.Create(sheetStream, SafeSettings());
            List<string>? row = null;
            while (xml.Read())
            {
                if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "row")
                {
                    row = [];
                    if (xml.IsEmptyElement)
                    {
                        AddRow(rows, row);
                        row = null;
                    }
                }
                else if (xml.NodeType == XmlNodeType.EndElement && xml.LocalName == "row" && row is not null)
                {
                    AddRow(rows, row);
                    row = null;
                    if (rows.Count >= MaxRows) break;
                }
                else if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "c" && row is not null)
                {
                    var reference = xml.GetAttribute("r");
                    var type = xml.GetAttribute("t");
                    var index = reference is null ? row.Count : ColumnIndex(reference);
                    var value = xml.IsEmptyElement ? "" : ReadCellValue(xml, type, shared);
                    if (index >= MaxColumns)
                        continue;
                    while (row.Count < index) row.Add("");
                    if (row.Count == index) row.Add(value);
                    else row[index] = value;
                }
            }
            return rows;
        }
    }

    /// <summary>
    /// تبدیل متن سلول به عدد: ارقام فارسی، جداکنندهٔ هزارگان (, ٬ ،) و واژهٔ «تومان» پذیرفته می‌شود.
    /// مثال: «۴۱۵٬۰۰۰ تومان» → 415000. خالی → null (یعنی «تغییر نده»).
    /// </summary>
    public static bool TryParseNumber(string? text, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(text))
            return true;
        var cleaned = SearchText.NormalizeDigits(text.Trim())
            .Replace("تومان", "", StringComparison.Ordinal).Replace("ریال", "", StringComparison.Ordinal)
            .Replace(",", "").Replace("٬", "").Replace("،", "").Replace(" ", "").Replace("\u200c", "").Replace("'", "")
            .Replace('٫', '.');
        if (cleaned.Length == 0)
            return true;
        if (!decimal.TryParse(cleaned, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var number))
            return false;
        value = number;
        return true;
    }

    private static void AddRow(List<string[]> rows, List<string> row)
    {
        // ردیف کاملاً خالی نگه داشته نمی‌شود.
        if (row.Count == 0 || row.All(string.IsNullOrWhiteSpace))
            return;
        rows.Add(row.Take(MaxColumns).Select(v => v.Trim()).ToArray());
    }

    private static string ReadCellValue(XmlReader xml, string? type, List<string> shared)
    {
        var depth = xml.Depth;
        string? v = null;
        var inline = new StringBuilder();
        while (xml.Read() && !(xml.NodeType == XmlNodeType.EndElement && xml.LocalName == "c" && xml.Depth == depth))
        {
            if (xml.NodeType != XmlNodeType.Element)
                continue;
            if (xml.LocalName == "v")
                v = xml.ReadElementContentAsString();
            else if (xml.LocalName == "t")
                inline.Append(xml.ReadElementContentAsString());
            // ReadElementContentAsString مکان‌نما را جلو می‌برد؛ اگر به پایان سلول رسیدیم خارج می‌شویم.
            if (xml.NodeType == XmlNodeType.EndElement && xml.LocalName == "c" && xml.Depth == depth)
                break;
        }

        return type switch
        {
            "s" when int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) && i >= 0 && i < shared.Count => shared[i],
            "s" => "",
            "inlineStr" => inline.ToString(),
            "b" => v == "1" ? "TRUE" : "FALSE",
            _ => v ?? inline.ToString(),
        };
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var list = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null)
            return list;

        using var stream = entry.Open();
        using var xml = XmlReader.Create(stream, SafeSettings());
        StringBuilder? current = null;
        while (xml.Read())
        {
            if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "si")
            {
                current = new StringBuilder();
                if (xml.IsEmptyElement) { list.Add(""); current = null; }
            }
            else if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "t" && current is not null)
            {
                current.Append(xml.ReadElementContentAsString());
                if (xml.NodeType == XmlNodeType.EndElement && xml.LocalName == "si")
                {
                    list.Add(current.ToString());
                    current = null;
                }
            }
            else if (xml.NodeType == XmlNodeType.EndElement && xml.LocalName == "si" && current is not null)
            {
                list.Add(current.ToString());
                current = null;
            }
            if (list.Count > 500_000)
                break;
        }
        return list;
    }

    /// <summary>مسیر اولین برگه از روی workbook.xml و رابطه‌هایش (اکسل همیشه sheet1 نمی‌نامد).</summary>
    private static string FirstSheetPath(ZipArchive zip)
    {
        try
        {
            string? relationId = null;
            var workbook = zip.GetEntry("xl/workbook.xml");
            if (workbook is null)
                return "xl/worksheets/sheet1.xml";
            using (var stream = workbook.Open())
            using (var xml = XmlReader.Create(stream, SafeSettings()))
            {
                while (xml.Read())
                {
                    if (xml.NodeType == XmlNodeType.Element && xml.LocalName == "sheet")
                    {
                        relationId = xml.GetAttribute("id", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
                        break;
                    }
                }
            }

            var rels = zip.GetEntry("xl/_rels/workbook.xml.rels");
            if (relationId is null || rels is null)
                return "xl/worksheets/sheet1.xml";
            using var relStream = rels.Open();
            using var relXml = XmlReader.Create(relStream, SafeSettings());
            while (relXml.Read())
            {
                if (relXml.NodeType == XmlNodeType.Element && relXml.LocalName == "Relationship" && relXml.GetAttribute("Id") == relationId)
                {
                    var target = relXml.GetAttribute("Target") ?? "worksheets/sheet1.xml";
                    return target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                }
            }
        }
        catch (XmlException)
        {
            // در صورت خطا مسیر پیش‌فرض.
        }
        return "xl/worksheets/sheet1.xml";
    }

    /// <summary>«C5» → 2 (ستون‌ها از صفر).</summary>
    public static int ColumnIndex(string reference)
    {
        var index = 0;
        foreach (var c in reference)
        {
            if (c is < 'A' or > 'Z')
                break;
            index = index * 26 + (c - 'A' + 1);
            if (index > 16384)
                break;
        }
        return Math.Max(0, index - 1);
    }

    /// <summary>XML امن: بدون DTD و بدون منبع بیرونی (جلوگیری از حملهٔ XXE).</summary>
    private static XmlReaderSettings SafeSettings() => new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        IgnoreComments = true,
        IgnoreWhitespace = false,
    };
}
