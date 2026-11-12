# فونت‌های فروشگاه (هویت بصری فراتک شاپ)

| فایل | فونت | کاربرد |
|---|---|---|
| `vazirmatn-fd-arabic-wght.woff2` | Vazirmatn (متغیر ۱۰۰ تا ۹۰۰) | حروف فارسی + **ارقام** – ارقام لاتین (0-9) در این فایل به شکل ارقام فارسی (۰-۹) نگاشت شده‌اند |
| `vazirmatn-latin-wght.woff2` | Vazirmatn (متغیر) | حروف و علائم لاتین (بدون ارقام) |
| `space-grotesk-latin-wght.woff2` | Space Grotesk (متغیر ۳۰۰ تا ۷۰۰) | نوشته‌های لاتین برند (کلاس `font-brand`) |

- منبع: پوشهٔ `Visual-Identities/Fratak-Shop/assets/fonts/web/` (هویت بصری) – هر دو فونت با مجوز آزاد SIL OFL 1.1
  (`OFL-Vazirmatn.txt` و `OFL-SpaceGrotesk.txt`). Vazirmatn «نام رزروشده» ندارد، پس نسخهٔ تغییر‌یافته مجاز است.
- چرا ارقام نگاشت شده‌اند؟ قیمت‌ها و اعداد در سرور با ارقام لاتین ساخته می‌شوند (`Helpers/Format.cs`) و فونت
  آن‌ها را فارسی نشان می‌دهد (همان رفتار فونت FaNum قبلی). متن و مقدار واقعی (کپی، جستجو، فرم‌ها) لاتین می‌ماند.
- ساخت دوبارهٔ فایل FD (در صورت به‌روز شدن فونت هویت بصری):

```bash
pip install fonttools brotli
python - <<'PY'
from fontTools.ttLib import TTFont
f = TTFont("Visual-Identities/Fratak-Shop/assets/fonts/web/vazirmatn-arabic-wght-normal.woff2")
for t in f["cmap"].tables:
    if t.isUnicode():
        for i in range(10):
            t.cmap[0x30 + i] = "uni06F%d" % i
f.flavor = "woff2"
f.save("src/Fratak-Shop.Web/wwwroot/assets/font/vazirmatn-fd-arabic-wght.woff2")
PY
```
