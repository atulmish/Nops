#!/usr/bin/env python3
"""Generates the Nops logo and README graphics.

    python3 build/generate-brand.py

Writes SVG sources and PNG renders to docs/images/ and the NuGet icon to build/Nops.png.
Needs fontTools, ImageMagick (with the RSVG delegate) and the Montserrat font installed.
Text is converted to paths so the SVGs look the same everywhere (GitHub, NuGet, browsers).
PNGs are what the README references: nuget.org does not render SVG images.
"""
import subprocess
import sys
from pathlib import Path

from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

ROOT = Path(__file__).resolve().parent.parent
OUT = ROOT / "docs" / "images"
FONT_DIR = Path("/usr/share/fonts/julietaula-montserrat-fonts")

NAVY_950, NAVY_900, NAVY_800, NAVY_700 = "#070D18", "#0B1220", "#111B2E", "#1B2A45"
LINE = "#26364F"
TEAL, CYAN, VIOLET = "#2DD4BF", "#22D3EE", "#8B5CF6"
TEXT, MUTED = "#F8FAFC", "#94A3B8"
MONO = "ui-monospace, SFMono-Regular, Menlo, Consolas, 'Liberation Mono', monospace"

_fonts = {}


def font(name):
    if name not in _fonts:
        f = TTFont(FONT_DIR / f"Montserrat-{name}.otf")
        _fonts[name] = (f, f.getGlyphSet(), f.getBestCmap(), f["head"].unitsPerEm)
    return _fonts[name]


def text_width(text, size, weight="Bold"):
    f, gs, cmap, upm = font(weight)
    return sum(f["hmtx"][cmap[ord(c)]][0] for c in text) * size / upm


def text_path(text, x, y, size, fill, weight="Bold", opacity=None):
    """Text as an SVG <path>, baseline-left at (x, y)."""
    f, gs, cmap, upm = font(weight)
    scale = size / upm
    cursor = 0.0
    d = []
    for ch in text:
        g = cmap[ord(ch)]
        pen = SVGPathPen(gs)
        gs[g].draw(TransformPen(pen, (scale, 0, 0, -scale, x + cursor, y)))
        d.append(pen.getCommands())
        cursor += f["hmtx"][g][0] * scale
    op = f' fill-opacity="{opacity}"' if opacity is not None else ""
    return f'<path d="{" ".join(d)}" fill="{fill}"{op}/>'


def defs_common(uid=""):
    return f"""
    <linearGradient id="accent{uid}" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="{TEAL}"/><stop offset="0.55" stop-color="{CYAN}"/><stop offset="1" stop-color="{VIOLET}"/>
    </linearGradient>
    <linearGradient id="bg{uid}" x1="0" y1="0" x2="1" y2="1">
      <stop offset="0" stop-color="{NAVY_900}"/><stop offset="1" stop-color="{NAVY_800}"/>
    </linearGradient>
    <radialGradient id="glowT{uid}" cx="0.12" cy="0.1" r="0.55">
      <stop offset="0" stop-color="{TEAL}" stop-opacity="0.22"/><stop offset="1" stop-color="{TEAL}" stop-opacity="0"/>
    </radialGradient>
    <radialGradient id="glowV{uid}" cx="0.92" cy="0.95" r="0.6">
      <stop offset="0" stop-color="{VIOLET}" stop-opacity="0.28"/><stop offset="1" stop-color="{VIOLET}" stop-opacity="0"/>
    </radialGradient>
    <pattern id="dots{uid}" width="24" height="24" patternUnits="userSpaceOnUse">
      <circle cx="2" cy="2" r="1.1" fill="#FFFFFF" fill-opacity="0.07"/>
    </pattern>
    <linearGradient id="key{uid}" x1="0" y1="0" x2="0" y2="1">
      <stop offset="0" stop-color="#FFFFFF"/><stop offset="1" stop-color="#CBD5E1"/>
    </linearGradient>"""


def background(w, h, uid="", radius=0):
    return f"""
  <rect width="{w}" height="{h}" rx="{radius}" fill="url(#bg{uid})"/>
  <rect width="{w}" height="{h}" rx="{radius}" fill="url(#dots{uid})"/>
  <rect width="{w}" height="{h}" rx="{radius}" fill="url(#glowT{uid})"/>
  <rect width="{w}" height="{h}" rx="{radius}" fill="url(#glowV{uid})"/>"""


def logo_mark(x, y, size, uid="m"):
    """The Nops mark: a keyhole under a shackle-shaped arc, on a rounded navy tile."""
    s = size / 256
    return f"""
  <g transform="translate({x} {y}) scale({s})">
    <rect width="256" height="256" rx="58" fill="url(#bg{uid})"/>
    <rect x="1.5" y="1.5" width="253" height="253" rx="56.5" fill="none" stroke="{LINE}" stroke-width="3"/>
    <path d="M 52 134 A 76 76 0 0 1 204 134" fill="none" stroke="url(#accent{uid})" stroke-width="20" stroke-linecap="round"/>
    <path fill="url(#key{uid})" fill-rule="evenodd"
          d="M 128 74 A 38 38 0 0 0 108 145 L 96 204 Q 95 212 103 212 L 153 212 Q 161 212 160 204 L 148 145 A 38 38 0 0 0 128 74 Z"/>
  </g>"""


def logo_svg():
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256" width="256" height="256" role="img" aria-label="Nops logo">
  <defs>{defs_common("m")}</defs>{logo_mark(0, 0, 256, "m")}
</svg>
"""


def pill(x, y, label, size=16, stroke=TEAL, fill=None, color=TEXT, weight="SemiBold", padx=16, height=None):
    w = text_width(label, size, weight) + padx * 2
    h = height or size + 18
    out = f'<rect x="{x}" y="{y}" width="{w:.1f}" height="{h}" rx="{h/2}" fill="{fill or "none"}" stroke="{stroke}" stroke-opacity="0.55" stroke-width="1.5"/>'
    out += text_path(label, x + padx, y + h / 2 + size * 0.35, size, color, weight)
    return out, w


def banner_svg(w=1280, h=400):
    code = [
        [("using ", VIOLET_L), ("Nops.Configuration", TEXT_L), (";", MUTED)],
        [],
        [("builder", TEXT_L), (".", MUTED), ("Configuration", TEXT_L)],
        [("    .", MUTED), ("AddSops", AMBER), ("(", MUTED), ('"secrets.sops.yaml"', TEAL_L), (");", MUTED)],
        [],
        [("// Password: decrypted in memory", COMMENT)],
    ]
    lines = ""
    for i, segs in enumerate(code):
        if not segs:
            continue
        spans = "".join(f'<tspan fill="{c}">{t.replace("&", "&amp;")}</tspan>' for t, c in segs)
        lines += f'<text x="772" y="{170 + i * 31}" font-family="{MONO}" font-size="19" xml:space="preserve">{spans}</text>\n    '

    chips, cx = "", 72
    for label in ("No sops binary", "No subprocess", "No plaintext on disk"):
        p, pw = pill(cx, 330, label, size=15)
        chips += p
        cx += pw + 12

    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="{w}" height="{h}" role="img" aria-label="Nops: read SOPS-encrypted secrets straight into .NET configuration">
  <defs>{defs_common("b")}{defs_common("m")}
    <clipPath id="clip"><rect width="{w}" height="{h}" rx="24"/></clipPath>
    <filter id="shadow" x="-10%" y="-10%" width="120%" height="130%"><feDropShadow dx="0" dy="14" stdDeviation="18" flood-color="#000" flood-opacity="0.45"/></filter>
  </defs>
  <g clip-path="url(#clip)">{background(w, h, "b", 24)}</g>
  <rect x="1" y="1" width="{w-2}" height="{h-2}" rx="23" fill="none" stroke="{LINE}" stroke-width="2"/>
  {logo_mark(72, 72, 150, "m")}
  {text_path("Nops", 248, 192, 118, TEXT, "ExtraBold")}
  {text_path("Read SOPS-encrypted secrets straight", 74, 262, 25, "#CBD5E1", "Medium")}
  {text_path("into .NET configuration.", 74, 294, 25, "#CBD5E1", "Medium")}
  {chips}
  <g filter="url(#shadow)">
    <rect x="740" y="64" width="500" height="272" rx="18" fill="{NAVY_950}" stroke="{LINE}" stroke-width="1.5"/>
    <path d="M740 118 H1240" stroke="{LINE}" stroke-width="1.5"/>
    <circle cx="768" cy="91" r="6.5" fill="#F87171"/><circle cx="790" cy="91" r="6.5" fill="#FBBF24"/><circle cx="812" cy="91" r="6.5" fill="#34D399"/>
    {text_path("Program.cs", 1016, 97, 14, MUTED, "Medium")}
    {lines}
  </g>
</svg>
"""


VIOLET_L, TEXT_L, TEAL_L, AMBER, COMMENT = "#C4B5FD", "#E2E8F0", "#5EEAD4", "#FCD34D", "#64748B"


def arrow(x1, x2, y, uid):
    return f"""<path d="M{x1} {y} H{x2-14}" stroke="url(#accent{uid})" stroke-width="4" stroke-linecap="round" stroke-dasharray="2 10"/>
  <path d="M{x2-22} {y-10} L{x2-4} {y} L{x2-22} {y+10}" fill="none" stroke="{CYAN}" stroke-width="4" stroke-linecap="round" stroke-linejoin="round"/>"""


def flow_svg(w=1200, h=430):
    mono = lambda x, y, spans, size=15: (
        f'<text x="{x}" y="{y}" font-family="{MONO}" font-size="{size}" xml:space="preserve">'
        + "".join(f'<tspan fill="{c}">{t}</tspan>' for t, c in spans) + "</text>")
    enc = lambda n: f"ENC[AES256_GCM,{n}…]"
    top, ch = 130, 252
    c1x, c1w = 48, 380
    c2x, c2w = 472, 360
    c3x, c3w = 876, 276

    file_lines = "".join([
        mono(c1x + 24, 214, [("Database:", TEAL_L)]),
        mono(c1x + 24, 242, [("  Host: ", TEXT_L), (enc("Q2f9x"), MUTED)]),
        mono(c1x + 24, 270, [("  Password: ", TEXT_L), (enc("7vLm0"), MUTED)]),
        mono(c1x + 24, 298, [("Api:", TEAL_L)]),
        mono(c1x + 24, 326, [("  Key: ", TEXT_L), (enc("u81Zp"), MUTED)]),
        mono(c1x + 24, 354, [("sops:", VIOLET_L), (" {age, mac, ...}", MUTED)]),
    ])
    out_lines = "".join([
        mono(c3x + 28, 214, [("Database:Host", TEAL_L)], 16),
        mono(c3x + 28, 240, [("  = db.internal", TEXT_L)], 16),
        mono(c3x + 28, 276, [("Database:Password", TEAL_L)], 16),
        mono(c3x + 28, 302, [("  = ", TEXT_L), ("\u2022" * 10, AMBER)], 16),
        mono(c3x + 28, 338, [("Api:Key", TEAL_L)], 16),
        mono(c3x + 28, 364, [("  = ", TEXT_L), ("\u2022" * 10, AMBER)], 16),
    ])
    steps = ["Find a key: age, PGP, KMS, Vault", "Unwrap the data key", "Verify the MAC", "Decrypt in memory"]
    steps_svg = ""
    for i, st in enumerate(steps):
        y = 246 + i * 36
        steps_svg += f'<circle cx="{c2x + 36}" cy="{y-5}" r="13" fill="none" stroke="{TEAL}" stroke-width="2"/>'
        steps_svg += text_path(str(i + 1), c2x + 36 - text_width(str(i + 1), 14, "Bold") / 2, y, 14, TEAL, "Bold")
        steps_svg += text_path(st, c2x + 62, y, 17, TEXT_L, "Medium")

    def card(x, cw, title):
        return (f'<rect x="{x}" y="{top}" width="{cw}" height="{ch}" rx="16" fill="{NAVY_950}" stroke="{LINE}" stroke-width="1.5"/>'
                + text_path(title, x + 24, top + 40, 13, MUTED, "SemiBold"))

    lock_x = c1x + c1w - 24 - 28
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="{w}" height="{h}" role="img" aria-label="How Nops works: an encrypted sops file is decrypted in-process into IConfiguration values">
  <defs>{defs_common("f")}{defs_common("m")}
    <filter id="shadow" x="-10%" y="-10%" width="120%" height="130%"><feDropShadow dx="0" dy="10" stdDeviation="14" flood-color="#000" flood-opacity="0.4"/></filter>
    <clipPath id="clip"><rect width="{w}" height="{h}" rx="24"/></clipPath>
  </defs>
  <g clip-path="url(#clip)">{background(w, h, "f", 24)}</g>
  <rect x="1" y="1" width="{w-2}" height="{h-2}" rx="23" fill="none" stroke="{LINE}" stroke-width="2"/>
  {text_path("How it works", 48, 66, 28, TEXT, "Bold")}
  {text_path("Encrypted in git, decrypted only in your process.", 48, 98, 17, MUTED, "Medium")}

  <g filter="url(#shadow)">{card(c1x, c1w, "ENCRYPTED  /  COMMITTED TO GIT")}</g>
  {file_lines}
  <path d="M{lock_x+6} {top+26} v-7 a8 8 0 0 1 16 0 v7" fill="none" stroke="{TEAL}" stroke-width="3" stroke-linecap="round"/>
  <rect x="{lock_x}" y="{top+26}" width="28" height="22" rx="5" fill="{TEAL}"/>

  {arrow(c1x + c1w + 8, c2x - 6, 256, "f")}

  <g filter="url(#shadow)"><rect x="{c2x}" y="{top}" width="{c2w}" height="{ch}" rx="16" fill="{NAVY_800}" stroke="{TEAL}" stroke-opacity="0.5" stroke-width="1.5"/></g>
  {logo_mark(c2x + 20, top + 18, 44, "m")}
  {text_path("Nops", c2x + 76, top + 53, 26, TEXT, "ExtraBold")}
  {text_path("IN-PROCESS", c2x + 170, top + 51, 12, TEAL, "Bold")}
  {steps_svg}

  {arrow(c2x + c2w + 8, c3x - 6, 256, "f")}

  <g filter="url(#shadow)">{card(c3x, c3w, "IConfiguration")}</g>
  {out_lines}
</svg>
"""


def icon(kind, x, y, color=TEAL):
    paths = {
        "chip": ['M14 14 H34 V34 H14 Z', 'M20 20 H28 V28 H20 Z', 'M19 8 V14 M24 8 V14 M29 8 V14 M19 34 V40 M24 34 V40 M29 34 V40 M8 19 H14 M8 24 H14 M8 29 H14 M34 19 H40 M34 24 H40 M34 29 H40'],
        "shield": ['M24 6 L40 12 V24 C40 33 33 40 24 43 C15 40 8 33 8 24 V12 Z', 'M16.5 24.5 L22 30 L32 18.5'],
        "file": ['M13 6 H28 L36 14 V42 H13 Z', 'M28 6 V14 H36', 'M19 24 H30 M19 30 H30 M19 36 H25'],
        "layers": ['M24 8 L41 17 L24 26 L7 17 Z', 'M7 24 L24 33 L41 24', 'M7 31 L24 40 L41 31'],
        "box": ['M24 6 L40 14 V32 L24 42 L8 32 V14 Z', 'M8 14 L24 23 L40 14', 'M24 23 V42'],
        "refresh": ['M38 20 A14 14 0 0 0 11 17', 'M10 28 A14 14 0 0 0 37 31', 'M12 8 V18 H22', 'M36 40 V30 H26'],
    }[kind]
    body = "".join(f'<path d="{d}"/>' for d in paths)
    return (f'<g transform="translate({x} {y})" fill="none" stroke="{color}" stroke-width="2.6" '
            f'stroke-linecap="round" stroke-linejoin="round">{body}</g>')


def features_svg(w=1200, h=538):
    items = [
        ("chip", "In-process", "No sops binary, shell or temp file", "in your container."),
        ("shield", "Tamper-evident", "The MAC is verified before any", "value is returned."),
        ("file", "Same files as sops", "Fixtures are written by real sops", "and checked in CI."),
        ("layers", "Fits .NET configuration", "Layered, overridable, bindable", "to IOptions<T>."),
        ("box", "Pay for what you use", "Core has no cloud SDKs; each", "KMS is its own package."),
        ("refresh", "Reloads on change", "Rotated secrets reach", "IOptionsMonitor<T>."),
    ]
    cards = ""
    cw, ch, gx, gy = 356, 186, 24, 24
    for i, (ic, title, l1, l2) in enumerate(items):
        x = 48 + (i % 3) * (cw + gx)
        y = 96 + (i // 3) * (ch + gy)
        cards += f"""
  <g filter="url(#shadow)"><rect x="{x}" y="{y}" width="{cw}" height="{ch}" rx="16" fill="{NAVY_950}" stroke="{LINE}" stroke-width="1.5"/></g>
  <rect x="{x+24}" y="{y+24}" width="60" height="60" rx="14" fill="{NAVY_800}" stroke="{TEAL}" stroke-opacity="0.35" stroke-width="1.5"/>
  {icon(ic, x + 30, y + 30)}
  {text_path(title, x + 24, y + 124, 21, TEXT, "Bold")}
  {text_path(l1, x + 24, y + 152, 15, MUTED, "Medium")}
  {text_path(l2, x + 24, y + 172, 15, MUTED, "Medium")}"""
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="{w}" height="{h}" role="img" aria-label="Why Nops: in-process, tamper-evident, same files as sops, fits .NET configuration, pay for what you use, reloads on change">
  <defs>{defs_common("g")}
    <filter id="shadow" x="-10%" y="-10%" width="120%" height="130%"><feDropShadow dx="0" dy="8" stdDeviation="10" flood-color="#000" flood-opacity="0.35"/></filter>
    <clipPath id="clip"><rect width="{w}" height="{h}" rx="24"/></clipPath>
  </defs>
  <g clip-path="url(#clip)">{background(w, h, "g", 24)}</g>
  <rect x="1" y="1" width="{w-2}" height="{h-2}" rx="23" fill="none" stroke="{LINE}" stroke-width="2"/>
  {text_path("Your secrets stay encrypted in git. Your app reads them like any other setting.", 48, 68, 18, MUTED, "Medium")}
  {cards}
</svg>
"""


def keys_svg(w=1200, h=170):
    def row(title, labels, y, stroke):
        out = text_path(title, 48, y + 26, 14, MUTED, "SemiBold")
        x = 190
        for label in labels:
            p, pw = pill(x, y, label, size=16, stroke=stroke, fill=NAVY_950)
            out += p
            x += pw + 12
        return out

    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="{w}" height="{h}" role="img" aria-label="Supported formats: YAML, JSON, dotenv, INI, binary. Supported keys: age, PGP, AWS KMS, GCP KMS, Azure Key Vault, HashiCorp Vault Transit">
  <defs>{defs_common("k")}
    <clipPath id="clip"><rect width="{w}" height="{h}" rx="24"/></clipPath>
  </defs>
  <g clip-path="url(#clip)">{background(w, h, "k", 24)}</g>
  <rect x="1" y="1" width="{w-2}" height="{h-2}" rx="23" fill="none" stroke="{LINE}" stroke-width="2"/>
  {row("FORMATS", ["YAML", "JSON", "dotenv", "INI", "binary"], 36, CYAN)}
  {row("KEYS", ["age", "PGP", "AWS KMS", "GCP KMS", "Azure Key Vault", "HashiCorp Vault Transit"], 96, TEAL)}
</svg>
"""


def social_svg(w=1280, h=640):
    """GitHub social preview (1280x640): the banner centred on the brand background."""
    inner = banner_svg().split("\n", 1)[1].rsplit("</svg>", 1)[0]
    return f"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {w} {h}" width="{w}" height="{h}" role="img" aria-label="Nops: read SOPS-encrypted secrets straight into .NET configuration">
  <defs>{defs_common("s")}</defs>
  <rect width="{w}" height="{h}" fill="url(#bgs)"/>
  <rect width="{w}" height="{h}" fill="url(#dotss)"/>
  <rect width="{w}" height="{h}" fill="url(#glowTs)"/>
  <rect width="{w}" height="{h}" fill="url(#glowVs)"/>
  <svg x="0" y="{(h - 400) // 2}" width="1280" height="400" viewBox="0 0 1280 400">{inner}</svg>
</svg>
"""


def render(svg_path, png_path, width):
    subprocess.run(["magick", "-background", "none", "-density", "192", str(svg_path),
                    "-resize", f"{width}x", str(png_path)], check=True)


def main():
    OUT.mkdir(parents=True, exist_ok=True)
    assets = {
        "logo": (logo_svg(), 512),
        "banner": (banner_svg(), 1280),
        "flow": (flow_svg(), 1200),
        "features": (features_svg(), 1200),
        "keys": (keys_svg(), 1200),
        "social-preview": (social_svg(), 1280),
    }
    for name, (svg, width) in assets.items():
        svg_path = OUT / f"{name}.svg"
        svg_path.write_text(svg, encoding="utf-8")
        render(svg_path, OUT / f"{name}.png", width)
        print("wrote", svg_path.relative_to(ROOT), "and .png")
    # NuGet icon (nuget.org accepts PNG/JPEG up to 1 MB; 256 px is plenty).
    render(OUT / "logo.svg", ROOT / "build" / "Nops.png", 256)
    print("wrote build/Nops.png")


if __name__ == "__main__":
    sys.exit(main())
