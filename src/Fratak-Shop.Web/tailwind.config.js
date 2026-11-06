/** @type {import('tailwindcss').Config} */

// طیف آبی برند: ۴۰۰ و ۶۰۰ دو سر گرادیان آیکون برند هستند (#019BFF → #0156FE) و ۵۰۰ آبی اصلی.
const brand = {
  50: "#EBF5FF",
  100: "#D6EBFF",
  200: "#ADD6FF",
  300: "#70B8FF",
  400: "#019BFF",
  500: "#0069FD",
  600: "#0156FE",
  700: "#0047C7",
  800: "#003A9E",
  900: "#0A3278",
  950: "#0A1E45",
};

// خاکستری‌های خنثی، کمی مایل به سرمه‌ای برند؛ ۹۰۰ دقیقاً رنگ جوهر برند (#0B1927) است.
const ink = {
  50: "#FAFBFC",
  100: "#F3F5F8",
  200: "#E4E8EE",
  300: "#D2D8E0",
  400: "#9AA5B4",
  500: "#6B7787",
  600: "#4D5868",
  700: "#3A4554",
  800: "#1F2B38",
  900: "#0B1927",
  950: "#06101A",
};

// Tailwind build of the storefront. Run `npm run build:css` (or `npm run watch:css`)
// inside src/Fratak-Shop.Web after changing classes in views or helpers.
module.exports = {
  content: [
    "./Pages/**/*.cshtml",
    "./Helpers/**/*.cs",
    "./ViewComponents/**/*.cs",
    "./wwwroot/assets/js/*.js",
    "./Areas/**/*.cshtml",
    "./Infrastructure/Admin/**/*.cs",
    // دور ۲۱: کلاس‌های اندازه/وزن نوار اطلاعیه.
    "./Infrastructure/AnnouncementLook.cs",
    // دور ۱۹: HTML ساخته‌شده در Core (کارت محصول مقاله، «خلاصه در یک نگاه»).
    "../Fratak-Shop.Core/Services/Smart/**/*.cs",
    // دور بیستم: «توضیحات محصول» حرفه‌ای (fs-review-box، fs-spec-table، fs-proscons در ContentWriter.Review.cs).
    "../Fratak-Shop.Core/Services/Seo/Engine/**/*.cs",
  ],
  // Classes used by the original template that are not referenced literally anymore.
  safelist: [
    // Built in C# strings passed to partials (not always seen by the extractor).
    "[&>svg]:hidden",
    "xl:[&>svg]:block",
    "bg-red-700",
    "bg-sky-200",
    "border-t-blue-700",
    "fill-orange-700",
    "font-medium",
    "grayscale",
    "h-0",
    "h-2.5",
    "h-7",
    "hover:fill-red-400",
    "hover:fill-zinc-800",
    "hover:grayscale-0",
    "hover:scale-105",
    "hover:shadow-xl",
    "hover:text-sky-400",
    "max-w-36",
    "mb-12",
    "ms-0",
    "my-1",
    "object-bottom",
    "object-cover",
    "pl-4",
    "right-2",
    "rounded-t-2xl",
    "rtl:rotate-180",
    "text-blue-300",
    "text-blue-400",
    "text-blue-700",
    "text-red-600",
    "text-yellow-500",
    "w-1",
    "w-2",
    "w-2.5",
    "w-5/12",
  ],
  // The storefront has no dark theme: "dark:" classes must never follow the OS setting
  // (white text on the white filter box was invisible in dark mode).
  darkMode: "class",
  // ── هویت بصری فراتک شاپ (Visual-Identities/Fratak-Shop/tokens.css) ──────────────────────
  // آبی برند #0069FD و سرمه‌ای #0B1927 (رنگ «جوهر»). به‌جای عوض کردن صدها کلاس در قالب، پالت‌های
  // sky و blue با طیف آبی برند و zinc با خاکستری مایل به سرمه‌ای جایگزین شده‌اند؛ پس کلاس‌هایی مثل
  // bg-sky-500 یا text-zinc-800 خودبه‌خود رنگ برند را می‌گیرند. برای کد جدید کلاس‌های brand-* و ink
  // (و font-brand برای نوشتهٔ لاتین برند) را به کار ببرید. بعد از تغییر: npm run build:css
  theme: {
    extend: {
      colors: {
        sky: brand,
        blue: brand,
        brand: { ...brand, DEFAULT: "#0069FD" },
        zinc: ink,
        ink: { ...ink, DEFAULT: "#0B1927" },
      },
      fontFamily: {
        sans: ["Vazirmatn", "Tahoma", "Segoe UI", "sans-serif"],
        brand: ["Space Grotesk", "Vazirmatn", "sans-serif"],
      },
      backgroundImage: {
        "brand-gradient": "linear-gradient(135deg, #019BFF, #0156FE)",
      },
    },
  },
  plugins: [],
};
