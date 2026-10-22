// Temporary visual QA: screenshots of key pages (desktop + mobile) as JPEG files.
// usage: node screenshots.mjs http://localhost:5080 out-dir
import { chromium } from "playwright";
import fs from "node:fs";

const base = process.argv[2] ?? "http://localhost:5080";
const outDir = process.argv[3] ?? "shots";
fs.mkdirSync(outDir, { recursive: true });

const pages = [
  ["cart", "/cart"],
  ["checkout", "/checkout"],
  ["orderdetail", "/profile/orders"],
  ["product", "/product/xiaomi-redmi-watch-3-active"],
  ["home", "/"],
];

const browser = await chromium.launch();

for (const [device, viewport] of [["mobile", { width: 390, height: 844 }]]) {
  const context = await browser.newContext({ viewport, locale: "fa-IR" });
  const page = await context.newPage();

  // sign in with the demo customer and put something into the cart
  await page.goto(`${base}/login`);
  await page.fill("#Input_PhoneNumber", "09120000000");
  await page.fill("#Input_Password", "123456");
  await Promise.all([page.waitForNavigation(), page.$eval("form[method=post]", (f) => f.requestSubmit())]);
  await page.goto(`${base}/product/xiaomi-redmi-watch-3-active`);
  await Promise.all([page.waitForNavigation(), page.$eval("form[action^='/cart']", (f) => f.requestSubmit())]);

  for (const [name, path] of pages) {
    await page.goto(`${base}${path}`, { waitUntil: "networkidle" });
    if (name === "orderdetail") {
      const link = await page.$("a[href*='/profile/orders/']");
      if (link) await Promise.all([page.waitForNavigation(), link.click()]);
    }
    await page.waitForTimeout(600);
    const height = await page.evaluate(() => document.documentElement.scrollHeight);
    const maxHeight = device === "mobile" ? 2600 : 2000;
    await page.screenshot({
      path: `${outDir}/${device}-${name}.jpg`,
      type: "jpeg",
      quality: 40,
      fullPage: true,
      clip: { x: 0, y: 0, width: viewport.width, height: Math.min(height, maxHeight) },
    });
  }

  await context.close();
}

await browser.close();
