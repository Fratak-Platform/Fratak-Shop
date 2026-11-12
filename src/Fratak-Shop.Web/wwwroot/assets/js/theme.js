/*
 * Theme (light / dark / system). Loaded synchronously in <head> so the right theme is applied
 * before the first paint (no inline scripts: CSP). The store default comes from
 * <html data-theme-default="light|dark|system"> (admin setting); the visitor's choice is kept in
 * localStorage. Toggle buttons: [data-theme-toggle] (cycles light → dark → system).
 */
(() => {
  const root = document.documentElement;
  const KEY = "fratak:theme";
  const media = window.matchMedia ? window.matchMedia("(prefers-color-scheme: dark)") : null;
  const read = () => {
    try { return localStorage.getItem(KEY); } catch { return null; }
  };
  const mode = () => {
    const saved = read();
    return saved === "light" || saved === "dark" || saved === "system" ? saved : root.dataset.themeDefault || "light";
  };
  const apply = () => {
    const m = mode();
    const dark = m === "dark" || (m === "system" && !!media && media.matches);
    root.classList.toggle("dark", dark);
    root.dataset.themeMode = m;
    const meta = document.querySelector('meta[name="theme-color"]');
    if (meta) {
      if (!meta.dataset.light) meta.dataset.light = meta.content;
      meta.content = dark ? "#18181b" : meta.dataset.light;
    }
    document.querySelectorAll("[data-theme-toggle]").forEach((btn) => {
      const label = m === "dark" ? "پوسته تیره" : m === "system" ? "پوسته: هماهنگ با دستگاه" : "پوسته روشن";
      btn.setAttribute("aria-label", label + " (برای تغییر بزنید)");
      btn.title = label;
      btn.dataset.mode = m;
    });
  };

  apply();
  media?.addEventListener?.("change", () => { if (mode() === "system") apply(); });

  const next = { light: "dark", dark: "system", system: "light" };
  document.addEventListener("click", (event) => {
    const btn = event.target.closest?.("[data-theme-toggle]");
    if (!btn) return;
    try { localStorage.setItem(KEY, next[mode()] || "dark"); } catch { /* private mode */ }
    apply();
  });
  document.addEventListener("DOMContentLoaded", apply);
  window.addEventListener("storage", (e) => { if (e.key === KEY) apply(); });
})();
