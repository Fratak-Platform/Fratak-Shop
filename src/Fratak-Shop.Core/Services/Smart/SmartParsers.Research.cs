using System.Text.Json;
using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Smart;

public static partial class SmartParsers
{
    /// <summary>
    /// دور ۲۱: متن «description» محصول در بلوک‌های JSON-LD یک صفحه (فقط گره‌های @type=Product). تگ‌ها حذف می‌شوند.
    /// <para>مثال: <c>{"@type":"Product","description":"&lt;p&gt;هدفون بی‌سیم …&lt;/p&gt;"}</c> ← «هدفون بی‌سیم …».</para>
    /// <para>محل استفاده: <see cref="ProductResearch.PageText"/> (تحقیق برای «توضیحات محصول»).</para>
    /// </summary>
    public static List<string> JsonLdDescriptions(string? html)
    {
        var list = new List<string>();
        foreach (var block in JsonLdBlocks(html))
        {
            var root = Parse(block);
            if (root is not { } doc)
                continue;
            using (doc)
            {
                foreach (var product in Products(doc.RootElement, 0))
                {
                    if (Str(product, "description") is { Length: >= 20 } description)
                        list.Add(PersianText.HtmlToText(description.Length > 20_000 ? description[..20_000] : description));
                    if (list.Count >= 3)
                        return list;
                }
            }
        }
        return list;
    }
}
