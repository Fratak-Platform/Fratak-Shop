#!/usr/bin/env python3
"""
FRATAK SHOP — brand asset builder
=================================
Vector master for the *supplied* Fratak Shop logo. The artwork is reproduced as
delivered; nothing about the mark, the lettering or the colours was redesigned.

  mark      = the parent Fratak F (bar + bowl, #0B1927) whose third fragment has
              become a shopping cart (#0069FD) with two wheels
  wordmark  = "Fratak" (#0B1927) over "Shop" (#0069FD) in a custom geometric
              sans — traced from the artwork because it is bespoke lettering

Geometry of the mark is rebuilt from measurements of the artwork (rounded
polygons + circles); the wordmark is a high-fidelity trace, smoothed to clean
Bezier outlines.

Colours measured from the source:
    navy  #0B1927
    blue  #0069FD

Usage:  python3 build_assets.py     ->  regenerates every SVG in svg/
"""
import json
import math
import os

HERE = os.path.dirname(os.path.abspath(__file__))

# =============================================================== COLOUR
NAVY = "#0B1927"
BLUE = "#0069FD"
WHITE = "#FFFFFF"

# =============================================================== MARK GEOMETRY
# coordinates: the artwork's own pixel space
BAR = [(61.6, 34.9), (259.6, 34.6), (235.0, 76.7), (89.2, 76.7)]
BOWL = [(221.6, 93.2), (100.0, 93.2), (90.4, 94.9), (17.1, 220.7),
        (64.9, 220.7), (126.4, 128.2), (199.2, 126.9)]
CART = [(248.3, 141.5), (133.7, 141.5), (124.2, 148.4), (103.3, 187.0),
        (105.1, 188.5), (213.6, 187.8), (219.8, 185.4), (226.2, 179.3)]
WHEELS = [(130.35, 211.65, 15.90), (182.40, 211.55, 15.95)]

R_BAR, R_BOWL, R_CART = 4.0, 4.0, 6.0
MARK_BOX = (17.1, 34.6, 258.0, 227.5)      # ink box of the mark


def _unit(a, b):
    d = math.hypot(b[0] - a[0], b[1] - a[1])
    return ((b[0] - a[0]) / d, (b[1] - a[1]) / d)


def rounded_poly(pts, r):
    n = len(pts)
    corners = []
    for i in range(n):
        p, a, b = pts[i], pts[i - 1], pts[(i + 1) % n]
        u1, u2 = _unit(p, a), _unit(p, b)
        l1 = math.hypot(p[0] - a[0], p[1] - a[1])
        l2 = math.hypot(b[0] - p[0], b[1] - p[1])
        rr = min(r, l1 * 0.45, l2 * 0.45)
        corners.append(((p[0] + u1[0] * rr, p[1] + u1[1] * rr), p,
                        (p[0] + u2[0] * rr, p[1] + u2[1] * rr)))
    d = f"M {corners[0][2][0]:.2f},{corners[0][2][1]:.2f}"
    for i in range(1, n + 1):
        a1, c, a2 = corners[i % n]
        d += (f" L {a1[0]:.2f},{a1[1]:.2f}"
              f" Q {c[0]:.2f},{c[1]:.2f} {a2[0]:.2f},{a2[1]:.2f}")
    return d + " Z"


def d_circle(cx, cy, r):
    return (f"M {cx - r:.2f},{cy:.2f} a {r:.2f},{r:.2f} 0 1,0 {2 * r:.2f},0 "
            f"a {r:.2f},{r:.2f} 0 1,0 {-2 * r:.2f},0 Z")


def mark_navy():
    return rounded_poly(BAR, R_BAR) + " " + rounded_poly(BOWL, R_BOWL)


def mark_blue():
    return rounded_poly(CART, R_CART) + " " + " ".join(d_circle(*w) for w in WHEELS)


# =============================================================== WORDMARK
# high-fidelity trace of the supplied lettering (stored in src/wordmark-paths.json)
_WD = None


def word_masks():
    global _WD
    if _WD is None:
        path = os.path.join(HERE, 'src', 'wordmark-paths.json')
        _WD = json.load(open(path))
    return _WD


# =============================================================== COMPOSE
MX0, MY0, MX1, MY1 = MARK_BOX
MK_W, MK_H = MX1 - MX0, MY1 - MY0
WD_X0, WD_Y0, WD_X1, WD_Y1 = 0.0, 52.0, 355.0, 241.0
WD_W, WD_H = WD_X1 - WD_X0, WD_Y1 - WD_Y0


def _shift(d, dx, dy):
    toks = re.findall(r'[MLCQZmlcqz]|-?\d+\.?\d*', d)
    out, i = [], 0
    while i < len(toks):
        t = toks[i]
        if t in 'MLCQZmlcqz':
            out.append(t)
            i += 1
            continue
        x, y = float(toks[i]), float(toks[i + 1])
        i += 2
        out.append(f'{x + dx:.3f}')
        out.append(f'{y + dy:.3f}')
    return ' '.join(out)


import re  # noqa: E402  (used by _shift)


def mark_group(navy=NAVY, blue=BLUE, dx=0.0, dy=0.0, scale=1.0):
    t = f'translate({dx - MX0 * scale:.3f},{dy - MY0 * scale:.3f})'
    if scale != 1.0:
        t += f' scale({scale:.6f})'
    g = f'<g transform="{t}">'
    if navy:
        g += f'<path d="{mark_navy()}" fill="{navy}"/>'
    if blue:
        g += f'<path d="{mark_blue()}" fill="{blue}"/>'
    return g + '</g>'


def word_group(navy=NAVY, blue=BLUE, dx=0.0, dy=0.0, scale=1.0):
    wd = word_masks()
    g = f'<g transform="translate({dx - WD_X0 * scale:.3f},{dy - WD_Y0 * scale:.3f})'
    if scale != 1.0:
        g += f' scale({scale:.6f})'
    g += '">'
    for key, fill in (('navy', navy), ('blue', blue)):
        if key in wd and wd[key]:
            g += (f'<path d="{_shift(wd[key], 0, 0)}" '
                  f'fill="{fill}" fill-rule="evenodd"/>')
    return g + '</g>'


def svg_doc(w, h, body, bg=None):
    bgr = f'<rect width="{w:.2f}" height="{h:.2f}" fill="{bg}"/>' if bg else ''
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w:.2f}" height="{h:.2f}" '
            f'viewBox="0 0 {w:.2f} {h:.2f}">{bgr}{body}</svg>')


def write(rel, content):
    p = os.path.join(HERE, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    open(p, 'w', encoding='utf-8').write(content)
    return rel


MANIFEST = []


def emit(rel, body, w, h):
    write(rel, svg_doc(w, h, body))
    MANIFEST.append((rel, w, h))


# =============================================================== ARTWORK SET
GAP = 26.0                      # optical space between mark and wordmark
ROW_GAP = 0.0


def build_svgs():
    # ---------------- 01 · the mark ----------------
    emit('svg/mark.svg', mark_group(), MK_W, MK_H)
    emit('svg/mark-on-dark.svg',
         f'<rect width="{MK_W:.2f}" height="{MK_H:.2f}" fill="{NAVY}"/>'
         + mark_group(NAVY, BLUE, 0, 0).replace(f'fill="{NAVY}"', f'fill="{WHITE}"'),
         MK_W, MK_H)
    # single-colour version on white
    emit('svg/mark-mono.svg',
         f'<rect width="{MK_W:.2f}" height="{MK_H:.2f}" fill="{WHITE}"/>'
         + f'<g transform="translate({-MX0:.3f},{-MY0:.3f})">'
           f'<path d="{mark_navy()}" fill="{NAVY}"/>'
           f'<path d="{mark_blue()}" fill="{NAVY}"/></g>',
         MK_W, MK_H)
    emit('svg/mark-mono-white.svg',
         f'<g transform="translate({-MX0:.3f},{-MY0:.3f})">'
         f'<path d="{mark_navy()}" fill="{WHITE}"/>'
         f'<path d="{mark_blue()}" fill="{WHITE}"/></g>',
         MK_W, MK_H)

    # ---------------- 02 · the locked-up logo ----------------
    W = MK_W + GAP + WD_W
    H = MK_H
    # optically centre the mark against the traced wordmark
    wm_dy = (WD_H - MK_H) / 2.0
    body = mark_group(NAVY, BLUE, 0, wm_dy) + word_group(NAVY, BLUE, MK_W + GAP, 0)
    emit('svg/logo-horizontal.svg', body, W, max(H, WD_H))

    # on dark: "Fratak" becomes white, "Shop" keeps brand blue, cart keeps blue
    Wd = MK_W + GAP + WD_W
    body = (f'<rect width="{Wd:.2f}" height="{max(H, WD_H):.2f}" fill="{NAVY}"/>'
            + mark_group(WHITE, BLUE, 0, wm_dy)
            + word_group(WHITE, BLUE, MK_W + GAP, 0))
    emit('svg/logo-horizontal-on-dark.svg', body, Wd, max(H, WD_H))

    # single colour
    body = mark_group(NAVY, NAVY, 0, wm_dy) + word_group(NAVY, NAVY, MK_W + GAP, 0)
    emit('svg/logo-horizontal-mono.svg', body, W, max(H, WD_H))

    # ---------------- 03 · favicon (white tile keeps the cart blue in any tab bar) ----
    S, INSET, RAD = 64.0, 0.11, 0.225
    r = S * RAD
    inner = S * (1 - 2 * INSET)          # width available for the mark, in tile units
    sc = inner / MK_W
    tile = (f'M {r:.2f},0 H {S - r:.2f} A {r:.2f},{r:.2f} 0 0 1 {S:.2f},{r:.2f} '
            f'V {S - r:.2f} A {r:.2f},{r:.2f} 0 0 1 {S - r:.2f},{S:.2f} '
            f'H {r:.2f} A {r:.2f},{r:.2f} 0 0 1 0,{S - r:.2f} '
            f'V {r:.2f} A {r:.2f},{r:.2f} 0 0 1 {r:.2f},0 Z')
    body = (f'<path d="{tile}" fill="{WHITE}"/>'
            + mark_group(NAVY, BLUE, (S - inner) / 2.0,
                         (S - MK_H * sc) / 2.0, scale=sc))
    emit('svg/favicon.svg', body, S, S)

    return MANIFEST


if __name__ == '__main__':
    for rel, w, h in build_svgs():
        print(f'{rel:40s} {w:7.1f} x {h:7.1f}')
