#!/usr/bin/env node
/* Render an SVG to PNG with resvg, loading the brand fonts shipped in assets/fonts.
 *
 *   node tools/render.js <in.svg> <out.png> [width] [background]
 *
 * width       target pixel width (keeps aspect ratio)
 * background  optional solid background, e.g. "#09090B" — omit for transparency
 *
 * Requires the npm package @resvg/resvg-js. Set RESVG_PATH to point at a local
 * copy if it is not installed next to this file.
 */
const { Resvg } = require(process.env.RESVG_PATH || '@resvg/resvg-js');
const fs = require('fs');
const path = require('path');

const [inp, outp, w, bg] = process.argv.slice(2);
if (!inp || !outp) {
  console.error('usage: node tools/render.js <in.svg> <out.png> [width] [bg]');
  process.exit(1);
}

const fontDir = path.join(__dirname, '..', 'assets', 'fonts');
const fontFiles = fs.existsSync(fontDir)
  ? fs.readdirSync(fontDir).filter((f) => f.endsWith('.ttf')).map((f) => path.join(fontDir, f))
  : [];

const svg = fs.readFileSync(inp, 'utf8');
const resvg = new Resvg(svg, {
  fitTo: w ? { mode: 'width', value: parseInt(w, 10) } : undefined,
  background: bg || undefined,
  font: { fontFiles, loadSystemFonts: false, defaultFontFamily: 'SpaceGrotesk-700' },
});

const image = resvg.render();
fs.mkdirSync(path.dirname(outp), { recursive: true });
fs.writeFileSync(outp, image.asPng());
console.log(`${outp}  ${image.width}x${image.height}`);
