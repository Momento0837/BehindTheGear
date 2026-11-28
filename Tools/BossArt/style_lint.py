"""Style lint for the boss sprites (Docs/BossImplementationPlan.md §2.5). Exit code 1 when a rule fails.

    python Tools/BossArt/style_lint.py

Rules
 1. alpha is 0 or 255 only (no anti-aliasing, no translucent pixels)
 2. every colour is in the boss palette, and the boss uses at most 22 colours in total
 3. isolated pixels (no 4-neighbour of the same colour) are at most 3 % of a frame's opaque pixels
 4. no outline: of the silhouette-edge pixels of a frame, at most 40 % are near-black (iron_dd, iron_d, cloth_d, cloth,
    leather_d). The plan words this as "the two darkest colours";
    measuring against the fixed near-black set means a bright frame (steam, fire) is not flagged for its own fill.
    Skipped for frames made of near-black only (the chain link): there the dark colour is the fill, not a border.
"""
import json, os, sys
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
from boss_sprites import P, OUT  # noqa: E402

PALETTE = {v: k for k, v in P.items()}
MAX_COLOURS, MAX_ISOLATED, MAX_DARK_EDGE = 22, 0.03, 0.40
NEAR_BLACK = {'iron_dd', 'iron_d', 'cloth_d', 'cloth', 'leather_d'}   # red is dark too, but it is the accent, not a border


def luminance(c): return .299 * c[0] + .587 * c[1] + .114 * c[2]


def lint_frame(name, im, problems, used):
    w, h = im.size; px = im.load()
    opaque = edge = dark_edge = isolated = 0
    colours = {}
    for y in range(h):
        for x in range(w):
            r, g, b, a = px[x, y]
            if a == 0: continue
            if a != 255: problems.append(f'{name}: translucent pixel at ({x},{y}) alpha {a}'); return
            colours[(r, g, b)] = colours.get((r, g, b), 0) + 1
    for c in colours:
        if c not in PALETTE: problems.append(f'{name}: colour #{c[0]:02x}{c[1]:02x}{c[2]:02x} is not in the palette'); return
    used.update(colours)
    if not colours: problems.append(f'{name}: empty frame'); return
    darkest = {c for c in colours if PALETTE[c] in NEAR_BLACK}
    all_dark = len(darkest) == len(colours)
    for y in range(h):
        for x in range(w):
            if px[x, y][3] == 0: continue
            opaque += 1; me = px[x, y][:3]
            same = False; on_edge = False
            for dx, dy in ((1, 0), (-1, 0), (0, 1), (0, -1)):
                nx, ny = x + dx, y + dy
                if not (0 <= nx < w and 0 <= ny < h) or px[nx, ny][3] == 0: on_edge = True
                elif px[nx, ny][:3] == me: same = True
            if not same: isolated += 1
            if on_edge:
                edge += 1
                if me in darkest: dark_edge += 1
    if isolated / opaque > MAX_ISOLATED:
        problems.append(f'{name}: {isolated}/{opaque} isolated pixels ({isolated / opaque:.1%} > {MAX_ISOLATED:.0%})')
    if not all_dark and edge and dark_edge / edge > MAX_DARK_EDGE:
        problems.append(f'{name}: {dark_edge / edge:.0%} of the silhouette edge is near-black (> {MAX_DARK_EDGE:.0%}): looks outlined')
    return isolated / opaque, (dark_edge / edge if edge else 0)


def main():
    layout = json.load(open(os.path.join(HERE, 'boss_layout.json'), encoding='utf-8'))
    problems, used, rows = [], set(), []
    for sheet, info in layout['sheets'].items():
        im = Image.open(os.path.join(OUT, sheet + '.png')).convert('RGBA')
        fw, fh = info['frameWidth'], info['frameHeight']
        names = info['frames'] if isinstance(info['frames'], list) else [str(i) for i in range(info['frames'])]
        if im.size != (fw * len(names), fh): problems.append(f'{sheet}: sheet is {im.size}, expected {fw * len(names)}x{fh}')
        worst_iso = worst_edge = 0
        for i, n in enumerate(names):
            result = lint_frame(f'{sheet}[{n}]', im.crop((i * fw, 0, (i + 1) * fw, fh)), problems, used)
            if result: worst_iso, worst_edge = max(worst_iso, result[0]), max(worst_edge, result[1])
        rows.append(f'  {sheet:<18} {len(names):>2} frames  {fw}x{fh}  isolated <= {worst_iso:.1%}  dark edge <= {worst_edge:.0%}')
    if len(used) > MAX_COLOURS: problems.append(f'boss uses {len(used)} colours (> {MAX_COLOURS})')
    print('\n'.join(rows))
    print(f'colours used: {len(used)} / {MAX_COLOURS}  ({", ".join(sorted(PALETTE[c] for c in used))})')
    if problems:
        print('FAIL'); print('\n'.join('  - ' + p for p in problems)); sys.exit(1)
    print('PASS')


if __name__ == '__main__':
    main()
