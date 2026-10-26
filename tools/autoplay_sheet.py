"""The sheet of a campaign played by itself (CoreBench --autoplay): charts of its days and the map on some of them.

python3 -I tools/autoplay_sheet.py <the run's folder> [title]   →   <folder>/sheet.png
"""
import csv
import os
import sys

from PIL import Image, ImageDraw, ImageFont

run = sys.argv[1]
title = sys.argv[2] if len(sys.argv) > 2 else os.path.basename(run.rstrip("/"))
rows = list(csv.DictReader(open(os.path.join(run, "days.csv"))))
days = [int(r["day"]) for r in rows]


def col(name):
    return [float(r[name]) for r in rows]


def font(size):
    for f in ("/System/Library/Fonts/Supplemental/Arial.ttf", "/Library/Fonts/Arial.ttf"):
        if os.path.exists(f):
            return ImageFont.truetype(f, size)
    return ImageFont.load_default()


INK, DIM, BG, GRID = (235, 222, 196), (150, 140, 120), (26, 24, 22), (55, 50, 46)
OURS, HORDE, GOLD = (90, 140, 255), (225, 70, 55), (230, 190, 70)


def chart(w, h, head, series, unit=""):
    """A line chart of some series over the days: (name, values, colour)."""
    im = Image.new("RGB", (w, h), BG)
    d = ImageDraw.Draw(im)
    d.text((12, 8), head, fill=INK, font=font(20))
    left, top, right, bottom = 56, 40, w - 14, h - 30
    top_v = max(1.0, max(max(v) for _, v, _ in series)) * 1.08
    for k in range(5):
        y = bottom - (bottom - top) * k / 4
        d.line((left, y, right, y), fill=GRID)
        d.text((6, y - 8), f"{top_v * k / 4:.0f}{unit}", fill=DIM, font=font(13))
    n = max(days)
    for k in range(0, n + 1, 10 if n > 40 else 5):
        x = left + (right - left) * (k - 1) / max(1, n - 1)
        d.text((x - 6, bottom + 6), str(k), fill=DIM, font=font(13))
    ly = 12
    for name, vals, c in series:
        pts = [(left + (right - left) * (dd - 1) / max(1, n - 1), bottom - (bottom - top) * v / top_v) for dd, v in zip(days, vals)]
        d.line(pts, fill=c, width=3)
        d.text((w - 210, ly), "— " + name, fill=c, font=font(15))
        ly += 18
    return im


def frame(day, w):
    f = os.path.join(run, "frames", f"day_{day:03d}.ppm")
    if not os.path.exists(f):
        return None
    im = Image.open(f).convert("RGB")
    return im.resize((w, int(im.height * w / im.width)), Image.NEAREST)


W = 760
charts = [
    chart(W, 300, "Золото державы по дням", [("золото", col("gold"), GOLD)]),
    chart(W, 300, "Сила войск: наши и орды", [("наши (герои)", col("our_strength"), OURS), ("орда (войска и гарнизоны)", col("horde_strength"), HORDE)]),
    chart(W, 300, "Рудники и клетки", [("наши рудники", col("our_mines"), OURS), ("рудники орды", col("horde_mines"), (255, 140, 120)), ("клетки орды", col("horde_cells"), HORDE)]),
    chart(W, 300, "Разведано карты, %, и уровень лучшего героя ×10", [("туман снят, %", col("explored"), (120, 220, 160)), ("уровень героя ×10", [v * 10 for v in col("best_level")], INK)]),
]
last = max(days)
picks = sorted(set([1, max(1, last // 4), max(1, last // 2), max(1, 3 * last // 4), last]))
frames = [(dd, frame(dd, W // 2 - 8)) for dd in picks]
frames = [(dd, f) for dd, f in frames if f is not None]
fh = max(f.height for _, f in frames) + 30 if frames else 0
rowsN = (len(frames) + 3) // 4
H = 60 + 2 * 310 + rowsN * (fh + 10)
sheet = Image.new("RGB", (2 * W + 30, H), (16, 15, 14))
d = ImageDraw.Draw(sheet)
d.text((14, 14), title, fill=INK, font=font(26))
for i, im in enumerate(charts):
    sheet.paste(im, (10 + (i % 2) * (W + 10), 60 + (i // 2) * 310))
y0 = 60 + 2 * 310
for i, (dd, f) in enumerate(frames):
    x = 10 + (i % 4) * (W // 2 + 2)
    y = y0 + (i // 4) * (fh + 10)
    d.text((x, y), f"День {dd}", fill=INK, font=font(18))
    sheet.paste(f, (x, y + 26))
sheet.save(os.path.join(run, "sheet.png"))
print(os.path.join(run, "sheet.png"), sheet.size)
