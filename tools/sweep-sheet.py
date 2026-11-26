"""Merge one --sweep folder into a single contact sheet (sweep-sheet.png): one block per step with its
hardware readback line from sweep.log and the step's screenshots side by side.

Usage: python tools/sweep-sheet.py "%LOCALAPPDATA%\\AeroControl\\sweeps\\<yyyyMMdd-HHmm>"
"""
import re
import sys
import textwrap
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont

ROW_H = 400          # screenshots are scaled to this height
GAP = 16
WIDTH = 1700
BG, CARD, TEXT, SUB, ACCENT = (28, 28, 30), (42, 42, 45), (245, 245, 247), (161, 161, 166), (10, 132, 255)


def font(size, bold=False):
    for name in (("segoeuib.ttf" if bold else "segoeui.ttf"), "arial.ttf"):
        try:
            return ImageFont.truetype(name, size)
        except OSError:
            pass
    return ImageFont.load_default()


def main(folder: Path):
    log = {}
    for line in (folder / "sweep.log").read_text(encoding="utf-8").splitlines():
        m = re.match(r"(\d\d:\d\d:\d\d) #(\d+) ([^:]+): (.*)", line)
        if m:
            log[int(m.group(2))] = (m.group(1), m.group(3), m.group(4))
    header = next((l for l in (folder / "sweep.log").read_text(encoding="utf-8").splitlines() if l.startswith("start:")), "")

    steps = {}
    for png in sorted(folder.glob("[0-9][0-9]-*.png")):
        steps.setdefault(int(png.name[:2]), []).append(png)

    f_title, f_line, f_cap, f_head = font(30, True), font(19), font(16), font(40, True)
    blocks = []
    for n in sorted(steps):
        t, name, hw = log.get(n, ("", steps[n][0].stem[3:], ""))
        imgs = []
        for p in steps[n]:
            im = Image.open(p).convert("RGB")
            scale = ROW_H / im.height if p.stem.split("-")[-1] != "osd" else 1.0
            imgs.append((p.stem.split("-")[-1], im.resize((int(im.width * scale), int(im.height * scale)), Image.LANCZOS)))
        lines = textwrap.wrap(hw, 150)
        h = 30 + 40 + len(lines) * 26 + 16 + ROW_H + 30 + 24
        block = Image.new("RGB", (WIDTH, h), CARD)
        d = ImageDraw.Draw(block)
        d.text((24, 22), f"#{n:02d}  {name}", font=f_title, fill=TEXT)
        d.text((WIDTH - 24, 28), t, font=f_line, fill=SUB, anchor="ra")
        y = 70
        for l in lines:
            d.text((24, y), l, font=f_line, fill=SUB)
            y += 26
        y += 16
        x = 24
        for cap, im in imgs:
            if x + im.width > WIDTH - 24:  # wrap is never needed at these sizes, but stay inside the card
                im = im.resize((WIDTH - 24 - x, int(im.height * (WIDTH - 24 - x) / im.width)), Image.LANCZOS)
            block.paste(im, (x, y))
            d.text((x, y + ROW_H + 6), cap, font=f_cap, fill=SUB)
            x += im.width + GAP
        blocks.append(block)

    total = 140 + sum(b.height + GAP for b in blocks) + 40
    sheet = Image.new("RGB", (WIDTH + 2 * 40, total), BG)
    d = ImageDraw.Draw(sheet)
    d.text((40, 36), f"Aero Control settings sweep · {folder.name}", font=f_head, fill=TEXT)
    d.text((40, 92), header, font=f_line, fill=ACCENT)
    y = 140
    for b in blocks:
        sheet.paste(b, (40, y))
        y += b.height + GAP
    out = folder / "sweep-sheet.png"
    sheet.save(out, optimize=True)
    print(f"{out}  {sheet.width}x{sheet.height}  {len(blocks)} steps")


if __name__ == "__main__":
    main(Path(sys.argv[1]))
