#!/usr/bin/env python3
"""Build the brandbook: colour swatches and reference diagrams.

    python3 tools/make_brandbook.py

Everything is generated from the same geometry and colours as build_assets.py,
so the brandbook can never drift from the delivered artwork.
"""
import math
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
BASE = os.path.dirname(HERE)
sys.path.insert(0, BASE)

import build_assets as B  # noqa: E402

OUT = os.path.join(BASE, 'brandbook')

NAVY = B.NAVY
BLUE = B.BLUE
WHITE = B.WHITE
PAPER = '#FDFDFD'
GREY = '#B9C1CA'
GREY_LIGHT = '#E7EAEE'

FONT = "'Space Grotesk','Helvetica Neue',Helvetica,Arial,sans-serif"
MONO = "'IBM Plex Mono','SFMono-Regular',Menlo,Consolas,monospace"


# ----------------------------------------------------------------- helpers
def esc(s):
    return str(s).replace('&', '&amp;').replace('<', '&lt;').replace('>', '&gt;')


def svg(w, h, body, bg=None):
    bgr = f'<rect width="{w}" height="{h}" fill="{bg}"/>' if bg else ''
    return (f'<svg xmlns="http://www.w3.org/2000/svg" width="{w}" height="{h}" '
            f'viewBox="0 0 {w} {h}">{bgr}{body}</svg>')


def text(x, y, s, size=13, fill=NAVY, anchor='start', weight=500, family=FONT,
         spacing=None):
    ls = f' letter-spacing="{spacing}"' if spacing else ''
    return (f'<text x="{x}" y="{y}" font-family="{family}" font-size="{size}" '
            f'font-weight="{weight}" fill="{fill}" text-anchor="{anchor}"{ls}>'
            f'{esc(s)}</text>')


def dashed_rect(x, y, w, h, stroke=BLUE, dash='6 5', width=1.2, fill='none'):
    return (f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="{fill}" '
            f'stroke="{stroke}" stroke-width="{width}" stroke-dasharray="{dash}"/>')


def line(x1, y1, x2, y2, stroke=BLUE, width=1.2, dash=None):
    d = f' stroke-dasharray="{dash}"' if dash else ''
    return (f'<line x1="{x1}" y1="{y1}" x2="{x2}" y2="{y2}" stroke="{stroke}" '
            f'stroke-width="{width}"{d}/>')


def arrow_h(x1, x2, y, stroke=NAVY):
    return (line(x1, y, x2, y, stroke) +
            f'<path d="M {x1},{y} l 7,-3.5 v 7 z" fill="{stroke}"/>'
            f'<path d="M {x2},{y} l -7,-3.5 v 7 z" fill="{stroke}"/>')


def arrow_v(y1, y2, x, stroke=NAVY):
    return (line(x, y1, x, y2, stroke) +
            f'<path d="M {x},{y1} l -3.5,7 h 7 z" fill="{stroke}"/>'
            f'<path d="M {x},{y2} l -3.5,-7 h 7 z" fill="{stroke}"/>')


def write(rel, content):
    p = os.path.join(OUT, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, 'w', encoding='utf-8') as fh:
        fh.write(content)
    print('brandbook/' + rel)


# ----------------------------------------------------------------- geometry
MX0, MY0, MX1, MY1 = B.MARK_BOX
MK_W = B.MK_W          # 240.9
MK_H = B.MK_H          # 192.9
LW = B.MK_W + B.GAP + B.WD_W   # lockup width 621.9
LH = B.MK_H            # lockup height 192.9
BAR_T = B.BAR[2][1] - B.BAR[0][1]       # 42.1 — the bar's thickness = one stroke
WHEEL_D = 2 * B.WHEELS[0][2]            # 31.8


def mark(w, h, navy=NAVY, blue=BLUE, scale=1.0):
    """Mark drawn from its ink box, scaled to w x h, origin at (0,0)."""
    s = w / MK_W
    return B.mark_group(navy, blue, 0, 0, scale=s)


def lockup(w, navy=NAVY, blue=BLUE):
    """Full horizontal lockup scaled to width w."""
    s = w / LW
    dy = (LH - MK_H) / 2.0
    return (B.mark_group(navy, blue, 0, dy * s, scale=s)
            + B.word_group(navy, blue, MK_W * s + B.GAP * s, 0, scale=s))


# ----------------------------------------------------------------- swatches
SWATCHES = [
    ('navy', NAVY, 'Fratak Navy', 'Primary. The F, the wordmark, dark surfaces.'),
    ('blue', BLUE, 'Fratak Blue', 'The cart and the word "Shop".'),
    ('gradient', None, 'App-icon gradient', '#019BFF → #0156FE, diagonal.'),
    ('white', WHITE, 'White', 'The mark on dark.'),
    ('paper', PAPER, 'Paper', 'Default page background.'),
    ('grey', '#E7EAEE', 'Divider', 'Hairlines and rules only.'),
]


def swatches():
    W, H = 720, 720
    for key, colour, name, note in SWATCHES:
        if key == 'gradient':
            body = (f'<defs><linearGradient id="g" x1="0" y1="0" x2="1" y2="1">'
                    f'<stop offset="0" stop-color="#019BFF"/>'
                    f'<stop offset="1" stop-color="#0156FE"/>'
                    f'</linearGradient></defs>'
                    f'<rect width="{W}" height="{H}" fill="url(#g)"/>')
            label_fill = WHITE
        else:
            body = f'<rect width="{W}" height="{H}" fill="{colour}"/>'
            label_fill = NAVY if key in ('white', 'paper', 'grey') else WHITE
        body += text(64, H - 172, name, 44, label_fill, weight=700)
        hexes = ('#019BFF  #0156FE' if key == 'gradient' else colour)
        body += text(64, H - 112, hexes, 26, label_fill, weight=500, family=MONO)
        body += text(64, H - 64, note, 20,
                     label_fill if key != 'grey' else '#5B6672')
        write(f'swatches/{colour.lstrip("#").lower() if colour else "gradient"}.svg',
              svg(W, H, body))
    # friendly aliases that match how people look for them
    os.replace(os.path.join(OUT, 'swatches/0b1927.svg'),
               os.path.join(OUT, 'swatches/navy.svg'))
    os.replace(os.path.join(OUT, 'swatches/0069fd.svg'),
               os.path.join(OUT, 'swatches/blue.svg'))
    os.replace(os.path.join(OUT, 'swatches/ffffff.svg'),
               os.path.join(OUT, 'swatches/white.svg'))
    os.replace(os.path.join(OUT, 'swatches/fdfdfd.svg'),
               os.path.join(OUT, 'swatches/paper.svg'))
    os.replace(os.path.join(OUT, 'swatches/e7eaee.svg'),
               os.path.join(OUT, 'swatches/grey.svg'))


# ----------------------------------------------------------------- diagrams
def d_lockup():
    PAD, TOP, CAP = 72, 88, 120
    iw = 900
    ih = iw * LH / LW
    W = iw + PAD * 2
    H = TOP + ih + CAP
    body = text(PAD, 50, '01  Primary lockup', 22, NAVY, weight=700)
    body += (f'<g transform="translate({PAD},{TOP})">' + lockup(iw) + '</g>')
    body += text(PAD, H - 34,
                 f'Horizontal lockup · {LW:.1f} × {LH:.1f} · mark + 26 u gap + wordmark',
                 15, '#5B6672')
    write('diagrams/01-lockup.svg', svg(W, H, body, PAPER))






def d_versions():
    PAD, ROW = 72, 168
    iw = 640
    W = iw + PAD * 2 + 210
    H = ROW * 4 + 112
    body = text(PAD, 52, '04  Versions', 22, NAVY, weight=700)
    rows = [
        ('Primary', 'on paper', PAPER, NAVY, BLUE),
        ('On dark', 'on Fratak Navy', NAVY, WHITE, BLUE),
        ('Mono', 'one colour, on paper', PAPER, NAVY, NAVY),
        ('Mono white', 'one colour, on dark', NAVY, WHITE, WHITE),
    ]
    for i, (name, sub, bg, nv, bl) in enumerate(rows):
        y = 92 + i * ROW
        body += (f'<rect x="{PAD}" y="{y}" width="{iw}" height="{ROW - 26}" '
                 f'fill="{bg}"/>')
        if bg == PAPER:
            body += (f'<rect x="{PAD}" y="{y}" width="{iw}" height="{ROW - 26}" '
                     f'fill="none" stroke="{GREY_LIGHT}"/>')
        inner_w = iw - 96
        s = inner_w / LW
        ih = LH * s
        body += (f'<g transform="translate({PAD + 48},{y + (ROW - 26 - ih) / 2})">'
                 + lockup(inner_w, nv, bl) + '</g>')
        body += text(PAD + iw + 34, y + 54, name, 16, NAVY, weight=700)
        body += text(PAD + iw + 34, y + 76, sub, 13, '#5B6672')
    body += text(PAD, H - 34,
                 'Never recolour the mark outside this list; never split the '
                 'cart from the F.', 15, '#5B6672')
    write('diagrams/04-versions.svg', svg(W, H, body, PAPER))




def main():
    swatches()
    d_lockup()
    d_versions()


if __name__ == '__main__':
    main()
