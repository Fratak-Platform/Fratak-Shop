#!/usr/bin/env python3
"""Render every SVG of the brand to production PNGs, plus the icon/favicon set.

    python3 tools/export_png.py

Needs node with @resvg/resvg-js (set RESVG_PATH if it is installed elsewhere).
"""
import json
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
BASE = os.path.dirname(HERE)
SVG = os.path.join(BASE, 'svg')
PNG = os.path.join(BASE, 'png')

JOBS = {
    'logo-horizontal': [512, 1024, 2048],
    'logo-horizontal-on-dark': [512, 1024, 2048],
    'logo-horizontal-mono': [512, 1024],
    'mark': [128, 256, 512, 1024],
    'mark-on-dark': [256, 512, 1024],
    'mark-mono': [256, 512],
    'mark-mono-white': [256, 512],
}


def run(src, out, width, bg=None):
    cmd = ['node', os.path.join(BASE, 'tools', 'render.js'), src, out, str(width)]
    if bg:
        cmd.append(bg)
    subprocess.run(cmd, check=True, env=dict(os.environ), stdout=subprocess.DEVNULL)


def icons():
    """Square app tiles: blue gradient, navy, white — the three versions shown
    in the supplied presentation."""
    from PIL import Image, ImageDraw

    app = os.path.join(PNG, 'app-icon')
    fav = os.path.join(PNG, 'favicon')
    os.makedirs(app, exist_ok=True)
    os.makedirs(fav, exist_ok=True)

    mark_w = os.path.join(PNG, 'mark-1024.png')
    mark_white = os.path.join(PNG, 'mark-mono-white-512.png')
    if not (os.path.exists(mark_w) and os.path.exists(mark_white)):
        print('skip icons (marks not rendered yet)')
        return

    def tile(size, bg, inner, inset=0.20, radius=0.225):
        S = size * 4
        img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        r = int(S * radius)
        d.rounded_rectangle([0, 0, S - 1, S - 1], radius=r, fill=bg)
        im = Image.open(inner).convert('RGBA')
        w = int(S * (1 - inset * 2))
        h = int(im.height * w / im.width)
        im = im.resize((w, h), Image.LANCZOS)
        img.alpha_composite(im, ((S - w) // 2, (S - h) // 2))
        return img.resize((size, size), Image.LANCZOS)

    def gradient_tile(size):
        """The brand-blue diagonal gradient used on the primary app icon."""
        S = size * 4
        grad = Image.new('RGB', (S, S))
        px = grad.load()
        c0 = (0x01, 0x9B, 0xFF)      # measured: #019BFF
        c1 = (0x01, 0x56, 0xFE)      # measured: #0156FE
        for y in range(S):
            for x in range(0, S):
                t = (x + y) / (2 * S - 2)
                px[x, y] = (int(c0[0] + (c1[0] - c0[0]) * t),
                            int(c0[1] + (c1[1] - c0[1]) * t),
                            int(c0[2] + (c1[2] - c0[2]) * t))
        mask = Image.new('L', (S, S), 0)
        ImageDraw.Draw(mask).rounded_rectangle([0, 0, S - 1, S - 1],
                                               radius=int(S * 0.225), fill=255)
        out = Image.new('RGBA', (S, S), (0, 0, 0, 0))
        out.paste(grad, (0, 0), mask)
        im = Image.open(mark_white).convert('RGBA')
        w = int(S * 0.60)
        h = int(im.height * w / im.width)
        im = im.resize((w, h), Image.LANCZOS)
        out.alpha_composite(im, ((S - w) // 2, (S - h) // 2))
        return out.resize((size, size), Image.LANCZOS)

    for size in (192, 512, 1024):
        gradient_tile(size).save(os.path.join(app, f'icon-gradient-{size}.png'))
        tile(size, (0x0B, 0x19, 0x27, 255), mark_white).save(
            os.path.join(app, f'icon-navy-{size}.png'))
        tile(size, (255, 255, 255, 255), mark_w).save(
            os.path.join(app, f'icon-white-{size}.png'))

    # ---- favicon / PWA set (white tile keeps the cart blue on any tab bar) ----
    base = Image.open(os.path.join(app, 'icon-white-1024.png')).convert('RGBA')
    base.save(os.path.join(fav, 'favicon.ico'),
              format='ICO', sizes=[(16, 16), (32, 32), (48, 48), (64, 64),
                                   (128, 128), (256, 256)])
    for s, name in ((16, 'favicon-16.png'), (32, 'favicon-32.png'),
                    (48, 'favicon-48.png'), (180, 'apple-touch-icon.png'),
                    (192, 'android-chrome-192.png'),
                    (512, 'android-chrome-512.png')):
        base.resize((s, s), Image.LANCZOS).save(os.path.join(fav, name))

    manifest = {
        "name": "Fratak Shop",
        "short_name": "Fratak Shop",
        "icons": [
            {"src": "android-chrome-192.png", "sizes": "192x192",
             "type": "image/png"},
            {"src": "android-chrome-512.png", "sizes": "512x512",
             "type": "image/png"},
        ],
        "theme_color": "#0B1927",
        "background_color": "#FFFFFF",
        "display": "standalone",
    }
    with open(os.path.join(fav, 'site.webmanifest'), 'w') as fh:
        json.dump(manifest, fh, indent=2)
        fh.write('\n')
    print('ok icons + favicon set')


def main():
    os.makedirs(PNG, exist_ok=True)
    for name, widths in JOBS.items():
        src = os.path.join(SVG, name + '.svg')
        if not os.path.exists(src):
            print('skip (missing)', name)
            continue
        for w in widths:
            run(src, os.path.join(PNG, f'{name}-{w}.png'), w)
        top = max(widths)
        run(src, os.path.join(PNG, f'{name}-{top}-on-white.png'), top, '#FFFFFF')
        run(src, os.path.join(PNG, f'{name}-{top}-on-dark.png'), top, '#0B1927')
        print('ok', name, widths)
    icons()


if __name__ == '__main__':
    main()
