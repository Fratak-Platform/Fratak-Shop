/** @type {import('tailwindcss').Config} */
module.exports = {
  content: [
    // Storefront + admin area views, page models and inline scripts.
    "../../src/Fratak-Shop.Web/Pages/**/*.cshtml",
    "../../src/Fratak-Shop.Web/Pages/**/*.cs",
    "../../src/Fratak-Shop.Web/Areas/**/*.cshtml",
    "../../src/Fratak-Shop.Web/Areas/**/*.cs",
    "../../src/Fratak-Shop.Web/Infrastructure/**/*.cs",
    "../../src/Fratak-Shop.Web/Utilities/**/*.cs",
    "../../src/Fratak-Shop.Web/wwwroot/assets/js/**/*.js",
    // Classes that are composed in C# (status badges, dynamic colours, ...) are
    // listed in the safelist file so the compiler never drops them.
    "./safelist.txt",
  ],
  theme: {
    extend: {
      fontFamily: {
        sans: ["YEKANBAKH", "ui-sans-serif", "system-ui", "sans-serif"],
      },
      colors: {
        brand: {
          50: "#f0f9ff",
          100: "#e0f2fe",
          200: "#bae6fd",
          300: "#7dd3fc",
          400: "#38bdf8",
          500: "#0ea5e9",
          600: "#0284c7",
          700: "#0369a1",
          800: "#075985",
          900: "#0c4a6e",
          950: "#082f49",
        },
      },
      boxShadow: {
        box: "rgba(99, 99, 99, 0.2) 0px 2px 8px 0px",
        "box-sm": "rgba(91, 93, 95, 0.2) 0px 0px 18px",
        "box-md": "rgba(100, 100, 111, 0.2) 0px 7px 29px 0px",
      },
      screens: {
        xs: "400px",
      },
    },
  },
  plugins: [],
};
