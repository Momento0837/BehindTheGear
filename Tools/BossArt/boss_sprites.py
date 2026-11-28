"""Forest boss sprites: Bartol the Iron Foreman on the Forge Goliath.

Run (Python 3 + Pillow):
    python Tools/BossArt/boss_sprites.py            # writes Assets/!Animation/Boss/*.png (+ .meta, layout json)
    python Tools/BossArt/boss_sprites.py --review   # also writes Docs/BossArt/review/lineup.png and frames.png

Style rules (Docs/BossImplementationPlan.md §2): no outline, flat fill + one hard shadow per material (right/lower),
almost no highlights, faceless pilot, alpha 0/255 only, palette below (<= 22 colours), big chunks, no perfect symmetry.
Every sheet is a horizontal strip of equal frames (like GangB_*.png); the game slices it at runtime.
The boss is built from parts (body / pilot / gear / furnace light / fists / chain) that code moves in whole pixels;
nothing here is ever rotated or scaled in the game, so every tilt is its own frame.
"""
from PIL import Image, ImageDraw
import hashlib, json, math, os, sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
OUT = os.path.join(ROOT, 'Assets', '!Animation', 'Boss')
REVIEW = os.path.join(ROOT, 'Docs', 'BossArt', 'review')

P = {
    # shared with GangB / Eden (hand-drawn sprites)
    'skin': (216, 190, 159), 'skin_d': (189, 163, 132), 'skin_dd': (181, 151, 116),
    'cloth': (54, 54, 54), 'cloth_d': (39, 39, 39),
    'red': (158, 0, 0),
    'wood': (141, 79, 8), 'wood_d': (122, 68, 6),
    'leather': (83, 59, 45), 'leather_d': (60, 41, 30),
    'steam': (245, 240, 237), 'steam_d': (238, 233, 230), 'steam_dd': (203, 196, 199),
    # new, same muted family
    'iron': (92, 88, 84), 'iron_m': (70, 67, 64), 'iron_d': (47, 45, 44), 'iron_dd': (33, 32, 31),
    'brass': (176, 137, 63), 'brass_d': (138, 106, 46),
    'glow': (208, 99, 42), 'glow_h': (240, 194, 122),
}


class Canvas:
    """Pixel canvas. `xf` (optional) moves every point before drawing: used to bob / lean / slump the upper body."""

    def __init__(s, w, h):
        s.im = Image.new('RGBA', (w, h), (0, 0, 0, 0)); s.d = ImageDraw.Draw(s.im); s.w, s.h = w, h; s.xf = None

    def c(s, k): return P[k] + (255,)

    def t(s, x, y): return s.xf(x, y) if s.xf else (x, y)

    def rect(s, x0, y0, x1, y1, k):
        if s.xf: s.d.polygon([s.t(x0, y0), s.t(x1, y0), s.t(x1, y1), s.t(x0, y1)], fill=s.c(k))
        else: s.d.rectangle([x0, y0, x1, y1], fill=s.c(k))

    def poly(s, pts, k): s.d.polygon([s.t(x, y) for x, y in pts], fill=s.c(k))

    def ell(s, x0, y0, x1, y1, k):
        (a, b), (c2, d2) = s.t(x0, y0), s.t(x1, y1)
        dx, dy = a - x0, b - y0  # ellipses only translate (never sheared)
        s.d.ellipse([x0 + dx, y0 + dy, x1 + dx, y1 + dy], fill=s.c(k))

    def px(s, x, y, k):
        x, y = s.t(x, y)
        if 0 <= x < s.w and 0 <= y < s.h: s.im.putpixel((x, y), s.c(k))

    def clear(s, x0, y0, x1, y1): s.d.rectangle([x0, y0, x1, y1], fill=(0, 0, 0, 0))


# ------------------------------------------------------------------ body (112 x 92)
BODY_W, BODY_H = 112, 92
CX, GROUND = 56, BODY_H - 1
# Where the separate parts sit on an idle body (top-left pixel of each part inside the body frame).
GEAR_SIZE, FURNACE_SIZE, PILOT_SIZE = (22, 22), (28, 10), (52, 40)


def tracks(cv, cx, g, tread=0, broken=False):
    """Phase 2 undercarriage: the legs fold away and the Goliath rides on a pair of crawler tracks.
    `tread` 0..3 moves the track plates one pitch (4 px) in four steps, so the belt visibly runs."""
    x0, x1, top = cx - 46, cx + 45, g - 17
    if broken:                                       # thrown track: a slack belt lying flat, one wheel rolled free
        cv.rect(x0 - 2, g - 6, x1 + 2, g, 'iron_m'); cv.rect(x0 - 2, g - 6, x1 + 2, g - 6, 'iron')
        for x in range(x0, x1, 8): cv.rect(x, g - 4, x + 3, g - 1, 'iron_d')
        cv.ell(x1 - 9, g - 15, x1 + 1, g - 5, 'iron_m'); cv.ell(x1 - 6, g - 12, x1 - 2, g - 8, 'brass_d')
        return
    # belt: a long loop with rounded ends; lit on top, in shadow underneath and at the right end
    cv.poly([(x0 + 5, top), (x1 - 5, top), (x1, top + 5), (x1, g - 4), (x1 - 4, g), (x0 + 4, g), (x0, g - 4), (x0, top + 5)], 'iron_m')
    cv.poly([(x0 + 5, top), (x1 - 5, top), (x1 - 3, top + 2), (x0 + 3, top + 2)], 'iron')
    cv.poly([(x1 - 12, top + 2), (x1 - 3, top + 2), (x1, top + 5), (x1, g - 4), (x1 - 4, g), (x1 - 12, g)], 'iron_d')
    cv.rect(x0 + 4, g - 2, x1 - 4, g, 'iron_d')
    # inside of the loop
    cv.poly([(x0 + 6, top + 4), (x1 - 7, top + 4), (x1 - 4, top + 7), (x1 - 4, g - 5), (x0 + 4, g - 5), (x0 + 4, top + 7)], 'iron_dd')
    # track plates: teeth along the top and the bottom run, stepping with `tread` (the bottom run goes the other way)
    for x in range(x0 + 4 + tread, x1 - 5, 4): cv.rect(x, top - 1, x + 1, top, 'iron_d')
    for x in range(x0 + 7 - tread, x1 - 5, 4): cv.rect(x, g - 1, x + 1, g, 'iron_dd')
    # sprockets at the ends (brass hubs) and road wheels between them; a spoke on each turns with the belt
    spoke = ((0, -1), (1, 0), (0, 1), (-1, 0))[tread % 4]
    for wx, r, hub in ((x0 + 9, 6, 'brass'), (x1 - 9, 6, 'brass_d')):
        cv.ell(wx - r, g - 5 - r * 2 + 1, wx + r, g - 5 + 1, 'iron_m'); cv.ell(wx - 2, g - 5 - r - 1, wx + 2, g - 5 - r + 3, hub)
        cv.rect(wx + spoke[0] * 4 - 1, g - 4 - r + spoke[1] * 4 - 1, wx + spoke[0] * 4, g - 4 - r + spoke[1] * 4, 'iron_d')
    for i, wx in enumerate(range(x0 + 22, x1 - 18, 12)):
        cv.ell(wx - 4, g - 12, wx + 4, g - 4, 'iron_m' if i % 2 == 0 else 'iron')
        cv.rect(wx - 1 + spoke[0] * 2, g - 9 + spoke[1] * 2, wx + spoke[0] * 2, g - 8 + spoke[1] * 2, 'iron_d')
    # mud guard bolted over the front of the track (hand repair: a plank)
    cv.poly([(x0 - 1, top - 2), (x0 + 15, top - 3), (x0 + 16, top), (x0, top + 1)], 'wood')
    cv.rect(x0 + 9, top - 3, x0 + 16, top, 'wood_d')


def body(bob=0, lean=0, slump=0, phase2=False, wreck=0, lift=0, tread=0):
    """bob: upper body 0/1 px down. lean: -1 left / +1 right (sweep telegraph). slump: px the boiler sinks (overheat).
    wreck: 0 alive, 1 kneeling and dead, 2 fallen heap. lift: -1 / +1 = that foot is in the air (walking); the
    boiler rocks over the planted foot."""
    cv = Canvas(BODY_W, BODY_H); cx, g = CX, GROUND
    s = slump + bob
    tilt = {0: 0.0, -1: 0.07, 1: -0.07}[lean]       # y shear: the leaning side drops
    shift = -3 * lean                                # and the whole upper body slides that way
    tilt += .035 * lift; shift += 2 * lift           # walking: weight goes over the planted foot (opposite the lifted one)
    if wreck == 2: tilt, shift = 0.16, -4            # fallen forward-left

    def upper(x, y):
        return (x + shift, y + s + round((x - cx) * tilt))

    def lower(x, y):
        return (x, y)

    # ---- legs (never transformed; they buckle outwards when the boiler sinks) ----
    leg_h = max(3, 24 - slump)
    splay = min(4, slump // 3)
    for lx, w, side in (() if phase2 else ((cx - 31, 13, -1), (cx + 15, 12, 1))):
        fx = lx + side * splay
        if wreck == 2:
            # legs lie flat, feet turned out
            cv.rect(fx - 4 + side * 3, g - 5, fx + w + 5 + side * 3, g, 'iron_m')
            cv.rect(fx - 4 + side * 3, g - 5, fx + w + 5 + side * 3, g - 5, 'iron')
            continue
        top = g - 7 - leg_h
        up = 5 if side == lift else 0                # this foot is off the ground: the leg telescopes in, the knee shows
        cv.rect(lx + 2, top, lx + w, g - 7 - up, 'iron_m'); cv.rect(lx + w - 2, top, lx + w, g - 7 - up, 'iron_d')
        if leg_h >= 12:
            cv.rect(lx + 3, top + 4, lx + w - 1, top + 6, 'brass'); cv.rect(lx + w - 4, top + 4, lx + w - 1, top + 6, 'brass_d')
        if slump >= 9:                               # knee plate pushed out sideways
            kx = lx + (-4 if side < 0 else w - 1)
            cv.rect(kx, top - 2, kx + 5, top + 4, 'iron_m'); cv.rect(kx + 4, top - 1, kx + 5, top + 4, 'iron_d')
        if up:
            kx = lx + (-3 if side < 0 else w - 2)    # knee plate pushed out
            cv.rect(kx, top + 7, kx + 5, top + 12, 'iron_m'); cv.rect(kx + 4, top + 8, kx + 5, top + 12, 'iron_d')
        cv.rect(fx - 3, g - 6 - up, fx + w + 5, g - up, 'iron_m'); cv.rect(fx - 3, g - 6 - up, fx + w + 5, g - 6 - up, 'iron')
        cv.rect(fx + w, g - 5 - up, fx + w + 5, g - up, 'iron_d')

    # ---- smokestacks (behind the boiler): different heights, right one crooked ----
    cv.xf = upper
    left_broken = wreck >= 1
    if not left_broken:
        cv.rect(cx - 33, 16, cx - 27, 44, 'iron'); cv.rect(cx - 29, 16, cx - 27, 44, 'iron_m')
        cv.rect(cx - 34, 14, cx - 26, 16, 'iron_m'); cv.rect(cx - 28, 14, cx - 26, 16, 'iron_d'); cv.rect(cx - 34, 25, cx - 26, 26, 'brass_d')
        if phase2: cv.rect(cx - 33, 13, cx - 28, 13, 'glow')
    else:                                            # snapped off: a short jagged stump
        cv.poly([(cx - 33, 44), (cx - 33, 33), (cx - 27, 30), (cx - 27, 44)], 'iron')
        cv.rect(cx - 29, 33, cx - 27, 44, 'iron_m')
    cv.poly([(cx + 24, 44), (cx + 24, 26), (cx + 27, 22), (cx + 27, 9), (cx + 32, 9), (cx + 32, 23), (cx + 29, 27), (cx + 29, 44)], 'iron')
    cv.poly([(cx + 30, 9), (cx + 32, 9), (cx + 32, 23), (cx + 29, 27), (cx + 29, 44), (cx + 28, 44), (cx + 28, 26), (cx + 30, 22)], 'iron_m')
    cv.rect(cx + 26, 7, cx + 33, 9, 'iron_m'); cv.rect(cx + 31, 7, cx + 33, 9, 'iron_d')
    if phase2 and not wreck: cv.rect(cx + 27, 6, cx + 32, 6, 'glow')

    # ---- hip drum (sinks half as much) ----
    cv.xf = (lambda x, y: (x + shift // 2, y + s // 2 + round((x - cx) * tilt / 2))) if wreck != 2 else upper
    hy = g - 31
    if wreck != 2:
        cv.rect(cx - 33, hy, cx + 32, hy + 9, 'iron_m'); cv.rect(cx + 12, hy, cx + 32, hy + 9, 'iron_d')

    # ---- boiler: hand-shaped hull (flatter top-left, belly sagging to the right) ----
    cv.xf = upper
    ty = 36
    hull = [(cx - 34, ty + 6), (cx - 26, ty + 1), (cx - 8, ty - 1), (cx + 14, ty), (cx + 30, ty + 4), (cx + 38, ty + 12),
            (cx + 39, ty + 26), (cx + 34, ty + 34), (cx + 20, ty + 39), (cx - 6, ty + 40), (cx - 28, ty + 37), (cx - 37, ty + 30), (cx - 39, ty + 16)]
    cv.poly(hull, 'iron')
    cv.poly([(cx + 22, ty + 2), (cx + 30, ty + 4), (cx + 38, ty + 12), (cx + 39, ty + 26), (cx + 34, ty + 34), (cx + 20, ty + 39), (cx + 8, ty + 40), (cx + 20, ty + 31), (cx + 25, ty + 18)], 'iron_m')
    cv.poly([(cx + 35, ty + 13), (cx + 39, ty + 26), (cx + 34, ty + 34), (cx + 24, ty + 38), (cx + 33, ty + 27)], 'iron_d')
    cv.poly([(cx - 37, ty + 30), (cx - 28, ty + 37), (cx - 6, ty + 40), (cx + 20, ty + 39), (cx + 10, ty + 36), (cx - 20, ty + 35)], 'iron_m')

    # ---- hand repairs: bolted plank (hangs loose in phase 2) + mismatched plate ----
    if not phase2 and not wreck:
        cv.poly([(cx - 37, ty + 15), (cx - 24, ty + 12), (cx - 23, ty + 19), (cx - 36, ty + 22)], 'wood')
        cv.poly([(cx - 30, ty + 16), (cx - 24, ty + 14), (cx - 23, ty + 19), (cx - 30, ty + 21)], 'wood_d')
        cv.rect(cx - 35, ty + 17, cx - 34, ty + 18, 'iron_d'); cv.rect(cx - 27, ty + 15, cx - 26, ty + 16, 'iron_d')
    else:                                            # one bolt left: the plank swings down, the hole behind shows
        cv.poly([(cx - 37, ty + 15), (cx - 25, ty + 13), (cx - 25, ty + 20), (cx - 36, ty + 22)], 'iron_d')
        cv.poly([(cx - 36, ty + 16), (cx - 30, ty + 17), (cx - 31, ty + 31), (cx - 37, ty + 29)], 'wood')
        cv.poly([(cx - 33, ty + 17), (cx - 30, ty + 17), (cx - 31, ty + 31), (cx - 34, ty + 30)], 'wood_d')
        cv.rect(cx - 35, ty + 17, cx - 34, ty + 18, 'iron_dd')
    cv.rect(cx + 12, ty + 5, cx + 21, ty + 11, 'iron_m'); cv.rect(cx + 19, ty + 5, cx + 21, ty + 11, 'iron_d')
    cv.rect(cx + 13, ty + 6, cx + 14, ty + 7, 'iron_d'); cv.rect(cx + 13, ty + 9, cx + 14, ty + 10, 'iron_d')

    # ---- cockpit lift rails: the bucket rides down these when the boiler overheats ----
    cv.rect(cx - 19, ty + 5, cx - 18, ty + 23, 'iron_m'); cv.rect(cx + 8, ty + 4, cx + 9, ty + 22, 'iron_m')

    # ---- furnace mouth (lower, slightly off-centre). The light is a separate part (furnace()). ----
    fy = ty + 25
    if not phase2 and not wreck:
        cv.poly([(cx - 15, fy), (cx + 11, fy - 1), (cx + 12, fy + 8), (cx - 14, fy + 9)], 'iron_dd')
    else:                                            # door blown off: a wider, torn opening
        cv.poly([(cx - 17, fy - 2), (cx - 4, fy - 3), (cx + 13, fy - 1), (cx + 14, fy + 9), (cx + 2, fy + 11), (cx - 16, fy + 10)], 'iron_dd')
    for bx in range(cx - 12, cx + 10, 5):            # cold grate bars; furnace() paints the fire over them
        cv.rect(bx, fy + 2, bx + 2, fy + 7, 'iron_d')

    # ---- chest gear socket (the gear itself is a separate, turning part) ----
    gx, gy = cx - 6, ty + 12
    cv.ell(gx - 10, gy - 10, gx + 10, gy + 10, 'iron_m')
    cv.ell(gx - 8, gy - 8, gx + 9, gy + 9, 'iron_d')

    # ---- shoulder chain drums; the right one carries the gang banner (the only red on the machine) ----
    for sx in (cx - 48, cx + 36):
        cv.rect(sx, ty + 1, sx + 11, ty + 12, 'iron_m'); cv.rect(sx + 9, ty + 2, sx + 11, ty + 12, 'iron_d'); cv.rect(sx + 2, ty + 3, sx + 8, ty + 10, 'wood')
        cv.rect(sx + 6, ty + 3, sx + 8, ty + 10, 'wood_d'); cv.rect(sx + 2, ty + 6, sx + 8, ty + 7, 'iron_dd')
    if not phase2 and not wreck:
        cv.poly([(cx + 40, ty + 12), (cx + 46, ty + 12), (cx + 46, ty + 27), (cx + 43, ty + 24), (cx + 40, ty + 28)], 'red')
    elif wreck < 2:                                  # torn: shorter, ragged
        cv.poly([(cx + 40, ty + 12), (cx + 46, ty + 12), (cx + 46, ty + 17), (cx + 44, ty + 21), (cx + 43, ty + 17), (cx + 40, ty + 20)], 'red')

    # ---- cockpit seat: the collar the pilot bucket sits in ----
    cv.poly([(cx - 23, ty + 2), (cx + 21, ty), (cx + 22, ty + 4), (cx - 22, ty + 6)], 'iron_m')
    cv.poly([(cx + 6, ty + 1), (cx + 21, ty), (cx + 22, ty + 4), (cx + 6, ty + 5)], 'iron_d')

    if wreck >= 1:                                   # burst seams: dark gashes where the steam came out
        cv.poly([(cx + 2, ty + 6), (cx + 9, ty + 4), (cx + 7, ty + 13), (cx + 3, ty + 11)], 'iron_dd')
        cv.poly([(cx - 30, ty + 26), (cx - 22, ty + 24), (cx - 24, ty + 31)], 'iron_dd')
        cv.poly([(cx + 24, ty + 20), (cx + 31, ty + 17), (cx + 30, ty + 26)], 'iron_dd')
    cv.xf = None
    if phase2: tracks(cv, cx, g, tread, broken=wreck == 2)
    return cv.im


def body_anchor(bob=0, lean=0, slump=0, wreck=0, lift=0, tread=0):
    """Top-left of gear / furnace / pilot / drums in a body frame (same maths as body())."""
    s = slump + bob; tilt = {0: 0.0, -1: 0.07, 1: -0.07}[lean] + .035 * lift; shift = -3 * lean + 2 * lift
    up = lambda x, y: [x + shift, y + s + round((x - CX) * tilt)]
    ty = 36
    return {
        'gear': up(CX - 6 - 11, ty + 12 - 11),
        'furnace': up(CX - 14, ty + 26),
        'pilot': up(CX - 25, 1),
        'drumLeft': up(CX - 43, ty + 12),
        'drumRight': up(CX + 41, ty + 12),
        'stackLeft': up(CX - 30, 13),
        'stackRight': up(CX + 29, 6),
    }


BODY_FRAMES = [   # name, kwargs
    ('idle0', dict()), ('idle1', dict(bob=1)),
    ('leanLeft', dict(lean=-1)), ('leanRight', dict(lean=1)),
    ('slump0', dict(slump=5)), ('slump1', dict(slump=10)), ('slump2', dict(slump=14)),
    ('rage0', dict(phase2=True)), ('rage1', dict(phase2=True, bob=1)),
    ('wreck0', dict(phase2=True, slump=14, wreck=1)), ('wreck1', dict(phase2=True, slump=15, wreck=2)),
    ('rageLeanLeft', dict(phase2=True, lean=-1)), ('rageLeanRight', dict(phase2=True, lean=1)),
    ('rageSlump0', dict(phase2=True, slump=5)), ('rageSlump1', dict(phase2=True, slump=10)), ('rageSlump2', dict(phase2=True, slump=14)),
    ('walkLeftUp', dict(lift=-1)), ('walkRightUp', dict(lift=1, bob=1)),
    # phase 2 rolling: four steps of the track belt, the hull rattling up and down on every other one
    ('rageRoll0', dict(phase2=True, tread=0)), ('rageRoll1', dict(phase2=True, tread=1, bob=1)),
    ('rageRoll2', dict(phase2=True, tread=2)), ('rageRoll3', dict(phase2=True, tread=3, bob=1)),
]


# ------------------------------------------------------------------ pilot (52 x 40), bucket included
def pilot(left='down', right='lever', head=(0, 0), goggles='up', lean=0, lever='up'):
    """Bartol in his wooden bucket. Faceless: bald head with one shadow plane; goggles are the only 'feature'."""
    cv = Canvas(*PILOT_SIZE); c, p = 25, 16
    hx, hy = head
    back, front = [], []   # arms behind / in front of the bucket rim

    # tall control lever (right) - straight up, or yanked forward
    if lever == 'up':
        cv.rect(c + 17, p - 14, c + 18, p + 16, 'iron_d'); cv.rect(c + 16, p - 15, c + 19, p - 14, 'brass')
    else:
        cv.poly([(c + 17, p + 16), (c + 18, p + 16), (c + 24, p - 5), (c + 23, p - 5)], 'iron_d')
        cv.rect(c + 22, p - 7, c + 25, p - 6, 'brass')
    # short lever (left)
    cv.rect(c - 23, p + 9, c - 22, p + 18, 'iron_d')

    # torso: tank top, shadow on the right
    l = lean
    cv.poly([(c - 14 + l, p + 5), (c + 10 + l, p + 3), (c + 13, p + 16), (c - 13, p + 17)], 'cloth')
    cv.poly([(c + 2 + l, p + 4), (c + 10 + l, p + 3), (c + 13, p + 16), (c + 4, p + 16)], 'cloth_d')

    # ---- left arm (lit side: skin / skin_d) ----
    if left == 'down':        # hand on the short lever
        cv.rect(c - 20, p + 6, c - 14, p + 15, 'skin'); cv.rect(c - 21, p + 13, c - 15, p + 17, 'skin_d')
        cv.poly([(c - 15 + l, p + 5), (c - 8 + l, p + 3), (c - 6 + l, p + 8), (c - 14 + l, p + 9)], 'skin')
    elif left == 'up':        # fist in the air
        cv.poly([(c - 15, p + 7), (c - 8, p + 4), (c - 11, p - 6), (c - 19, p - 4)], 'skin')
        cv.rect(c - 20, p - 13, c - 14, p - 4, 'skin'); cv.rect(c - 16, p - 13, c - 14, p - 4, 'skin_d')
        cv.rect(c - 21, p - 18, c - 14, p - 13, 'skin_d')
    elif left == 'out':       # flung out and up
        cv.poly([(c - 15, p + 4), (c - 8, p + 3), (c - 7, p + 9), (c - 14, p + 9)], 'skin')
        cv.poly([(c - 14, p + 3), (c - 13, p + 9), (c - 23, p + 4), (c - 22, p - 2)], 'skin')
        cv.poly([(c - 13, p + 9), (c - 23, p + 4), (c - 23, p + 2), (c - 13, p + 7)], 'skin_d')
        cv.rect(c - 25, p - 5, c - 20, p, 'skin_d')
    elif left == 'head':      # hand clapped on the bald head
        cv.poly([(c - 15, p + 7), (c - 8, p + 4), (c - 10, p - 4), (c - 17, p - 2)], 'skin')
        cv.poly([(c - 17, p - 2), (c - 10, p - 4), (c - 5 + hx, p - 10 + hy), (c - 10 + hx, p - 13 + hy)], 'skin')
        front.append(lambda: cv.rect(c - 9 + hx, p - 12 + hy, c - 3 + hx, p - 8 + hy, 'skin_d'))
    elif left == 'dangle':    # limp over the rim
        cv.poly([(c - 15, p + 5), (c - 8, p + 4), (c - 7, p + 9), (c - 14, p + 10)], 'skin')
        front.append(lambda: (cv.rect(c - 19, p + 9, c - 14, p + 21, 'skin'), cv.rect(c - 19, p + 20, c - 14, p + 23, 'skin_d')))

    # ---- right arm (shadow side: skin_d / skin_dd) with the gang armband ----
    if right == 'lever':      # raised, gripping the tall lever
        cv.rect(c + 10, p - 7, c + 15, p + 4, 'skin_d'); cv.rect(c + 13, p - 7, c + 15, p + 4, 'skin_dd')
        cv.rect(c + 10, p - 2, c + 15, p, 'red')
        cv.rect(c + 10, p - 11, c + 16, p - 7, 'skin_dd')
        cv.poly([(c + 4 + l, p + 3), (c + 12, p + 1), (c + 16, p + 4), (c + 14, p + 9), (c + 6 + l, p + 8)], 'skin_d')
    elif right == 'pull':     # lever yanked forward: arm stretched out along it
        cv.poly([(c + 5, p + 3), (c + 12, p + 1), (c + 15, p + 5), (c + 13, p + 9), (c + 6, p + 8)], 'skin_d')
        cv.poly([(c + 10, p + 3), (c + 14, p + 8), (c + 23, p), (c + 20, p - 4)], 'skin_d')
        cv.poly([(c + 14, p + 8), (c + 23, p), (c + 22, p - 1), (c + 13, p + 7)], 'skin_dd')
        cv.poly([(c + 12, p + 2), (c + 14, p), (c + 18, p + 4), (c + 16, p + 6)], 'red')
        cv.rect(c + 20, p - 5, c + 25, p - 1, 'skin_dd')
    elif right == 'up':       # fist in the air (roar)
        cv.rect(c + 9, p - 9, c + 14, p + 4, 'skin_d'); cv.rect(c + 12, p - 9, c + 14, p + 4, 'skin_dd')
        cv.rect(c + 9, p - 3, c + 14, p - 1, 'red')
        cv.rect(c + 8, p - 15, c + 15, p - 9, 'skin_dd')
        cv.poly([(c + 4, p + 3), (c + 12, p + 1), (c + 16, p + 4), (c + 14, p + 9), (c + 6, p + 8)], 'skin_d')
    elif right == 'out':
        cv.poly([(c + 4, p + 3), (c + 12, p + 2), (c + 14, p + 9), (c + 6, p + 8)], 'skin_d')
        cv.poly([(c + 11, p + 3), (c + 12, p + 9), (c + 23, p + 13), (c + 24, p + 8)], 'skin_d')
        cv.poly([(c + 12, p + 9), (c + 23, p + 13), (c + 23, p + 11), (c + 12, p + 7)], 'skin_dd')
        cv.poly([(c + 14, p + 4), (c + 17, p + 5), (c + 16, p + 11), (c + 13, p + 10)], 'red')
        cv.rect(c + 22, p + 9, c + 26, p + 14, 'skin_dd')
    elif right == 'head':
        cv.poly([(c + 5, p + 4), (c + 12, p + 2), (c + 15, p - 3), (c + 9, p - 5)], 'skin_d')
        cv.poly([(c + 9, p - 5), (c + 15, p - 3), (c + 6 + hx, p - 13 + hy), (c + 1 + hx, p - 10 + hy)], 'skin_d')
        cv.poly([(c + 7, p), (c + 13, p - 1), (c + 14, p + 1), (c + 8, p + 2)], 'red')
        front.append(lambda: cv.rect(c - 1 + hx, p - 12 + hy, c + 5 + hx, p - 8 + hy, 'skin_dd'))
    elif right == 'dangle':
        cv.poly([(c + 4, p + 4), (c + 12, p + 3), (c + 14, p + 9), (c + 6, p + 9)], 'skin_d')
        front.append(lambda: (cv.rect(c + 11, p + 9, c + 16, p + 21, 'skin_d'), cv.rect(c + 11, p + 12, c + 16, p + 14, 'red'),
                              cv.rect(c + 11, p + 20, c + 16, p + 23, 'skin_dd')))

    def draw_head():
        x, y = c + hx, p + hy
        cv.rect(x - 5, y + 3, x + 3, y + 6, 'cloth_d')                       # neck shadow keeps the head off the torso
        cv.ell(x - 7, y - 7, x + 4, y + 4, 'skin')
        cv.poly([(x + 0, y - 6), (x + 4, y - 3), (x + 4, y + 1), (x + 1, y + 4), (x - 2, y + 4)], 'skin_d')
        cv.rect(x - 4, y + 4, x + 1, y + 4, 'skin_dd')
        if goggles == 'up':                                                  # pushed up on the crown
            cv.rect(x - 7, y - 5, x + 4, y - 5, 'leather')
            cv.rect(x - 5, y - 7, x - 2, y - 5, 'brass'); cv.rect(x - 1, y - 7, x + 1, y - 5, 'brass_d')
        else:                                                                # pulled down (phase 2): a brass band, no eyes
            cv.rect(x - 7, y - 4, x + 4, y - 4, 'leather')
            cv.rect(x - 7, y - 3, x + 4, y - 1, 'brass'); cv.rect(x - 1, y - 3, x + 4, y - 1, 'brass_d')

    slumped = hy >= 6
    if not slumped: draw_head()

    # bucket rim (tilted plank) - in front of the torso
    cv.poly([(c - 21, p + 17), (c + 19, p + 15), (c + 20, p + 21), (c - 20, p + 23)], 'wood')
    cv.poly([(c + 6, p + 16), (c + 19, p + 15), (c + 20, p + 21), (c + 6, p + 22)], 'wood_d')
    cv.rect(c - 18, p + 19, c - 17, p + 20, 'iron_d'); cv.rect(c + 15, p + 17, c + 16, p + 18, 'iron_dd')
    for f in front: f()
    if slumped: draw_head()                                                  # knocked out: head drops onto the rim
    return cv.im


PILOT_FRAMES = [
    ('idle0', dict()), ('idle1', dict(head=(0, 1))),
    ('pull0', dict(head=(1, 0), lean=1)), ('pull1', dict(right='pull', lever='pull', head=(2, 1), lean=2)),
    ('roar0', dict(left='up', right='up', head=(-1, -1))), ('roar1', dict(left='up', right='up', head=(-2, -2), lean=-1)),
    ('hit', dict(left='out', right='out', head=(3, 1))),
    ('panic0', dict(left='head', right='out', head=(-1, 0))), ('panic1', dict(left='out', right='head', head=(1, 1))),
    ('rage0', dict(goggles='down')), ('rage1', dict(goggles='down', head=(0, 1), right='pull', lever='pull', lean=1)),
    ('stun', dict(left='dangle', right='dangle', head=(-3, 9))),
]


# ------------------------------------------------------------------ gear (22 x 22), 4 frames = one tooth pitch
def gear(step):
    cv = Canvas(*GEAR_SIZE); gx = gy = 10; r = 8
    for k in range(8):
        a = k / 8 * 2 * math.pi + step / 4 * (2 * math.pi / 8)
        tx, ty = gx + round(math.cos(a) * (r + 1)), gy + round(math.sin(a) * (r + 1))
        cv.rect(tx - 1, ty - 1, tx + 1, ty + 1, 'brass_d')
    cv.ell(gx - r, gy - r, gx + r, gy + r, 'brass_d')
    cv.ell(gx - r, gy - r, gx + r - 2, gy + r - 2, 'brass')
    cv.ell(gx - 3, gy - 3, gx + 2, gy + 2, 'iron_dd')
    # one notch on the rim so the turn is readable even though the teeth repeat
    a = step / 4 * (2 * math.pi / 8) - 2.2
    nx, ny = gx + round(math.cos(a) * 5), gy + round(math.sin(a) * 5)
    cv.rect(nx, ny, nx + 1, ny + 1, 'brass_d')
    return cv.im


# ------------------------------------------------------------------ furnace light (28 x 10), 3 stages
def furnace(stage):
    """0 idle fire, 1 charging, 2 overheated. Drawn over the cold grate bars of the body."""
    cv = Canvas(*FURNACE_SIZE)
    for i, bx in enumerate(range(2, 24, 5)):
        if stage == 0:
            cv.rect(bx, 1, bx + 2, 6, 'red'); cv.rect(bx, 3, bx + 2, 6, 'glow')
        elif stage == 1:
            cv.rect(bx, 1, bx + 2, 6, 'glow'); cv.rect(bx, 4, bx + 2, 6, 'glow_h')
        else:
            cv.rect(bx, 0, bx + 2, 6, 'glow_h'); cv.rect(bx, 0, bx + 2, 1, 'glow')
    if stage == 2: cv.rect(1, 6, 24, 7, 'glow_h')
    if stage >= 1: cv.rect(4, 8, 20, 8, 'glow')      # spill on the lip of the mouth
    return cv.im


# ------------------------------------------------------------------ fist (30 x 26), drawn as the right-hand one
def fist(kind='clench'):
    cv = Canvas(30, 26)
    if kind == 'side':   # sweep: seen from the side, knuckles leading to the right, chain cuff at the back
        cv.rect(0, 8, 6, 17, 'iron_m'); cv.rect(0, 14, 6, 17, 'iron_d')
        cv.poly([(5, 5), (23, 4), (28, 8), (29, 18), (25, 23), (6, 23), (4, 19)], 'iron')
        cv.poly([(5, 15), (29, 14), (29, 18), (25, 23), (6, 23), (4, 19)], 'iron_m')
        cv.poly([(5, 5), (11, 5), (11, 23), (6, 23), (4, 19)], 'brass'); cv.poly([(5, 15), (11, 15), (11, 23), (6, 23), (4, 19)], 'brass_d')
        for ky in (6, 11, 16):                                               # finger plates stacked on the leading edge
            cv.rect(24, ky, 29, ky + 3, 'iron_m'); cv.rect(24, ky + 3, 28, ky + 4, 'iron_d')
        cv.poly([(13, 22), (21, 21), (22, 25), (14, 25)], 'iron_m'); cv.rect(14, 24, 21, 25, 'iron_d')   # thumb under
        cv.rect(15, 9, 16, 10, 'iron_d'); cv.rect(20, 12, 21, 13, 'iron_d')
        return cv.im
    sq = 1 if kind == 'slam' else 0   # landed: one pixel flatter, knuckles spread
    cv.rect(10, 0 + sq * 2, 19, 5 + sq, 'iron_m'); cv.rect(16, 0 + sq * 2, 19, 5 + sq, 'iron_d')
    cv.poly([(3 - sq, 6 + sq), (26 + sq, 5 + sq), (28 + sq, 9 + sq), (28 + sq, 20), (25 + sq, 23), (4 - sq, 23), (2 - sq, 19)], 'iron')
    cv.poly([(19, 5 + sq), (26 + sq, 5 + sq), (28 + sq, 9 + sq), (28 + sq, 20), (25 + sq, 23), (19, 23)], 'iron_m')
    cv.poly([(3 - sq, 6 + sq), (26 + sq, 5 + sq), (27 + sq, 8 + sq), (3 - sq, 9 + sq)], 'brass')
    cv.poly([(19, 5 + sq), (26 + sq, 5 + sq), (27 + sq, 8 + sq), (19, 8 + sq)], 'brass_d')
    for kx, k in ((4 - sq, 'iron'), (10, 'iron'), (16, 'iron_m'), (21 + sq, 'iron_m')):
        cv.rect(kx, 19, kx + 4, 25, k); cv.rect(kx + 3, 19, kx + 4, 25, 'iron_d')
    cv.poly([(0, 11 + sq), (4, 10 + sq), (5, 18), (1, 19)], 'iron_m'); cv.rect(1, 17, 4, 19, 'iron_d')
    cv.rect(8, 13, 9, 14, 'iron_d'); cv.rect(14, 12, 15, 13, 'iron_d')
    return cv.im


def chain_link():
    cv = Canvas(3, 5); cv.rect(0, 0, 2, 4, 'iron_d'); cv.rect(1, 1, 1, 3, 'iron_dd'); return cv.im


# ------------------------------------------------------------------ FX: flat two-tone chunks, no gradients
def blob(cv, parts, k):
    for x0, y0, x1, y1 in parts: cv.ell(x0, y0, x1, y1, k)


def fx_dust(i):          # 16 x 12: a ground puff that swells, lifts and breaks into chunks
    cv = Canvas(16, 12)
    if i == 0: blob(cv, [(4, 6, 11, 11)], 'steam_dd'); blob(cv, [(5, 6, 9, 9)], 'steam_d')
    if i == 1: blob(cv, [(1, 4, 9, 11), (6, 3, 14, 10)], 'steam_dd'); blob(cv, [(2, 4, 7, 8), (7, 3, 12, 7)], 'steam_d')
    if i == 2: blob(cv, [(0, 3, 6, 9), (5, 1, 11, 7), (10, 3, 15, 9)], 'steam_dd'); blob(cv, [(1, 3, 4, 6), (6, 1, 9, 4)], 'steam_d')
    if i == 3: blob(cv, [(0, 2, 3, 5), (6, 0, 9, 3), (12, 2, 15, 5)], 'steam_dd')
    return cv.im


def fx_shockwave(i):     # 20 x 12: a crest of torn earth running along the ground (facing right; flip for left)
    cv = Canvas(20, 12); h = (6, 9, 10, 7)[i]; x = (0, 2, 4, 6)[i]
    cv.poly([(x, 11), (x + 5, 11 - h), (x + 9, 11 - h + 2), (x + 13, 11)], 'leather')
    cv.poly([(x + 2, 11), (x + 5, 11 - h + 2), (x + 8, 11 - h + 3), (x + 9, 11)], 'glow')
    if i < 3: cv.rect(x + 4, 11 - h + 4, x + 6, 11 - h + 6, 'glow_h')
    if i >= 1: cv.rect(max(0, x - 3), 9, max(1, x - 1), 11, 'steam_dd')
    if i >= 2: cv.rect(0, 10, 1, 11, 'steam_dd')
    return cv.im


def fx_slag(i):          # 8 x 8: a glob of molten metal, two wobble frames
    cv = Canvas(8, 8)
    if i == 0: cv.ell(1, 1, 6, 6, 'glow'); cv.rect(2, 2, 4, 4, 'glow_h')
    else: cv.ell(0, 2, 7, 6, 'glow'); cv.rect(2, 3, 5, 4, 'glow_h')
    return cv.im


def fx_marker(i):        # 16 x 4: where slag will land - a flat mark on the ground that blinks
    cv = Canvas(16, 4)
    if i == 0: cv.ell(1, 0, 14, 3, 'red')
    else: cv.ell(0, 0, 15, 3, 'glow'); cv.ell(4, 1, 11, 2, 'glow_h')
    return cv.im


def fx_shadow(i):        # 32 x 5: the shadow under a fist that is about to drop; frame 1 = about to land
    cv = Canvas(32, 5)
    if i == 0: cv.ell(6, 1, 25, 4, 'iron_d')
    else: cv.ell(1, 0, 30, 4, 'iron_dd'); cv.rect(8, 2, 23, 2, 'iron_d')
    return cv.im


def fx_steam(i):         # 16 x 16: a puff rising from a smokestack
    cv = Canvas(16, 16)
    if i == 0: cv.rect(5, 10, 11, 15, 'steam_d'); cv.rect(6, 9, 10, 9, 'steam_d'); cv.rect(6, 10, 9, 13, 'steam')
    if i == 1: blob(cv, [(3, 5, 12, 14)], 'steam_d'); blob(cv, [(3, 5, 9, 11)], 'steam'); blob(cv, [(8, 11, 12, 14)], 'steam_dd')
    if i == 2: blob(cv, [(1, 2, 10, 11), (8, 5, 14, 11)], 'steam_d'); blob(cv, [(2, 2, 8, 8)], 'steam'); blob(cv, [(9, 8, 13, 11)], 'steam_dd')
    if i == 3: blob(cv, [(0, 1, 5, 6), (9, 3, 14, 8)], 'steam_d'); blob(cv, [(5, 0, 8, 3)], 'steam_dd')
    return cv.im


def fx_spark(i):         # 12 x 12: iron on iron - a hard star, then chunks flying off
    cv = Canvas(12, 12)
    if i == 0: cv.rect(5, 2, 6, 9, 'glow_h'); cv.rect(2, 5, 9, 6, 'glow_h'); cv.rect(4, 4, 7, 7, 'glow_h'); cv.rect(5, 5, 6, 6, 'steam')
    if i == 1:
        cv.rect(5, 5, 6, 6, 'glow_h')
        for x, y in ((1, 1), (9, 2), (2, 9), (9, 9)): cv.rect(x, y, x + 1, y + 1, 'glow_h')
        cv.rect(5, 0, 6, 1, 'glow'); cv.rect(0, 5, 1, 6, 'glow')
    if i == 2:
        for x, y in ((0, 0), (10, 1), (1, 10), (10, 10), (5, 0)): cv.rect(x, y, x + 1, y + 1, 'glow')
    return cv.im


def fx_explosion(i):     # 32 x 32: a steam-and-fire burst (rivets fly as dark chunks)
    cv = Canvas(32, 32)
    if i == 0: cv.ell(11, 11, 20, 20, 'glow_h'); cv.ell(13, 13, 18, 18, 'steam')
    if i == 1: cv.ell(6, 6, 25, 25, 'glow'); cv.ell(9, 8, 22, 21, 'glow_h'); cv.ell(12, 11, 19, 18, 'steam')
    if i == 2:
        blob(cv, [(2, 5, 17, 20), (12, 2, 28, 18), (8, 13, 26, 29)], 'steam_dd'); blob(cv, [(6, 7, 18, 19), (14, 9, 24, 19)], 'glow')
        blob(cv, [(10, 9, 17, 16)], 'glow_h')
        for x, y in ((1, 2), (28, 22), (4, 26)): cv.rect(x, y, x + 2, y + 2, 'iron_d')
    if i == 3:
        blob(cv, [(1, 3, 14, 16), (15, 1, 30, 15), (6, 15, 20, 29), (18, 14, 30, 26)], 'steam_dd'); blob(cv, [(3, 4, 10, 11), (17, 2, 25, 10)], 'steam_d')
        for x, y in ((0, 0), (29, 27), (1, 29)): cv.rect(x, y, x + 1, y + 1, 'iron_d')
    if i == 4: blob(cv, [(0, 2, 8, 10), (20, 0, 29, 8), (4, 20, 12, 28), (22, 18, 30, 26)], 'steam_dd'); blob(cv, [(1, 3, 5, 7), (21, 1, 25, 5)], 'steam_d')
    return cv.im


# ------------------------------------------------------------------ sheets
def strip(frames):
    w, h = frames[0].size
    im = Image.new('RGBA', (w * len(frames), h), (0, 0, 0, 0))
    for i, f in enumerate(frames): im.paste(f, (i * w, 0))
    return im


def build():
    """name -> (sheet image, frame width, frame names)"""
    sheets = {}
    sheets['Boss_Body'] = ([body(**kw) for _, kw in BODY_FRAMES], [n for n, _ in BODY_FRAMES])
    sheets['Boss_Pilot'] = ([pilot(**kw) for _, kw in PILOT_FRAMES], [n for n, _ in PILOT_FRAMES])
    sheets['Boss_Gear'] = ([gear(i) for i in range(4)], [f'turn{i}' for i in range(4)])
    sheets['Boss_Furnace'] = ([furnace(i) for i in range(3)], ['idle', 'charge', 'overheat'])
    sheets['Boss_Fist'] = ([fist('clench'), fist('slam'), fist('side')], ['clench', 'slam', 'side'])
    sheets['Boss_Chain'] = ([chain_link()], ['link'])
    sheets['BossFx_Dust'] = ([fx_dust(i) for i in range(4)], None)
    sheets['BossFx_Shockwave'] = ([fx_shockwave(i) for i in range(4)], None)
    sheets['BossFx_Slag'] = ([fx_slag(i) for i in range(2)], None)
    sheets['BossFx_Marker'] = ([fx_marker(i) for i in range(2)], None)
    sheets['BossFx_Shadow'] = ([fx_shadow(i) for i in range(2)], None)
    sheets['BossFx_Steam'] = ([fx_steam(i) for i in range(4)], None)
    sheets['BossFx_Spark'] = ([fx_spark(i) for i in range(3)], None)
    sheets['BossFx_Explosion'] = ([fx_explosion(i) for i in range(5)], None)
    return sheets


META = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 0
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 4096
  textureSettings:
    serializedVersion: 2
    filterMode: 0
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 1
  spriteExtrude: 1
  spriteMeshType: 0
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 11
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 0
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 8
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 4096
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 0
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    customData:
    physicsShape: []
    bones: []
    spriteID: 5e97eb03825dee720800000000000000
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    spriteCustomMetadata:
      entries: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def stable_guid(name):
    """Same file name -> same GUID on every run, so regenerating art never breaks references."""
    return hashlib.md5(('BehindTheGear/Boss/' + name).encode()).hexdigest()


def write_assets(sheets):
    os.makedirs(OUT, exist_ok=True)
    layout = {'pixelsPerUnit': 11, 'sheets': {}, 'body': {}}
    for name, (frames, names) in sheets.items():
        path = os.path.join(OUT, name + '.png')
        sheet = strip(frames)
        # Unity shrinks a texture wider than the importer's maxTextureSize (4096 in the .meta below): frames would break.
        assert sheet.size[0] <= 4096, f'{name} is {sheet.size[0]} px wide: split it or use a grid'
        sheet.save(path)
        meta = path + '.meta'
        if not os.path.exists(meta):                 # never overwrite: a hand-tuned import setting must survive
            with open(meta, 'w', encoding='utf-8', newline='\n') as f: f.write(META.format(guid=stable_guid(name + '.png')))
        layout['sheets'][name] = {'frameWidth': frames[0].size[0], 'frameHeight': frames[0].size[1], 'frames': names or len(frames)}
    for n, kw in BODY_FRAMES:
        a = {k: v for k, v in kw.items() if k != 'phase2'}
        layout['body'][n] = body_anchor(**a)
    layout['pilotOverheatDrop'] = 21                 # px the bucket rides down the rails while overheated (on top of slump2)
    return layout


# ------------------------------------------------------------------ review sheets
def assemble(body_name='idle0', pilot_name='idle0', gear_step=0, furnace_stage=0, pilot_drop=0, fists=True, fist_y=44):
    """A whole boss as the game will stack it (for review only)."""
    kw = dict(BODY_FRAMES)[body_name]
    pad = 34
    im = Image.new('RGBA', (BODY_W + pad * 2, BODY_H + 8), (0, 0, 0, 0))
    a = body_anchor(**{k: v for k, v in kw.items() if k != 'phase2'})
    im.alpha_composite(body(**kw), (pad, 8))
    if furnace_stage is not None: im.alpha_composite(furnace(furnace_stage), (pad + a['furnace'][0], 8 + a['furnace'][1]))
    im.alpha_composite(gear(gear_step), (pad + a['gear'][0], 8 + a['gear'][1]))
    im.alpha_composite(pilot(**dict(PILOT_FRAMES)[pilot_name]), (pad + a['pilot'][0], 8 + a['pilot'][1] + pilot_drop))
    if fists:
        link = chain_link()
        for side, key in ((-1, 'drumLeft'), (1, 'drumRight')):
            dx, dy = pad + a[key][0], 8 + a[key][1]
            fx = dx + side * 22 - 15
            f = fist('clench') if side > 0 else fist('clench').transpose(Image.FLIP_LEFT_RIGHT)
            n = 5
            for i in range(n):                       # chain: links spaced along the line drum -> fist cuff
                t = (i + .5) / n
                x = round(dx + (fx + 15 - dx) * t) - 1; y = round(dy + (fist_y - dy) * t) - 2
                im.alpha_composite(link, (x, y))
            im.alpha_composite(f, (fx, fist_y))
    return im


def review(sheets):
    os.makedirs(REVIEW, exist_ok=True)
    art = os.path.join(ROOT, 'Assets', '!Animation')
    eden = Image.open(os.path.join(art, 'Player', 'SpriteSheet', 'Non_Cloak', 'Player_Idle.png')).convert('RGBA').crop((0, 0, 32, 32))
    gang = Image.open(os.path.join(art, 'Monster', 'SpriteSheet', 'GangB_Idle.png')).convert('RGBA').crop((0, 0, 48, 48))
    layers = [Image.open(os.path.join(ROOT, 'Assets', '!Map', '#1_Forest', f'#1_Layer{i}.png')).convert('RGBA') for i in (5, 4, 3, 2, 1)]
    FOREST_GROUND = 97                               # feet line inside the 108 px tall forest art

    def row(bg, scenes):
        w = sum(s.size[0] for s in scenes) + 8 * (len(scenes) + 1); h = max(s.size[1] for s in scenes) + 10
        im = bg(w, h); x = 8
        for s in scenes:
            im.alpha_composite(s, (x, h - 4 - s.size[1])); x += s.size[0] + 8
        return im

    def scene(boss):
        w = boss.size[0] + 100
        im = Image.new('RGBA', (w, boss.size[1]), (0, 0, 0, 0))
        im.alpha_composite(boss, (0, 0))
        im.alpha_composite(gang, (boss.size[0] + 2, boss.size[1] - 48))
        im.alpha_composite(eden, (boss.size[0] + 56, boss.size[1] - 32))
        return im

    scenes = [scene(assemble()),
              scene(assemble('slump2', 'panic0', 2, 2, pilot_drop=21, fist_y=74)),
              scene(assemble('rage0', 'rage0', 1, 1))]
    grey = lambda w, h: Image.new('RGBA', (w, h), (120, 128, 120, 255))

    def woods(scenes_):
        # the forest at the game's own scale (1 art pixel = 1 sprite pixel); the front layer goes over the characters
        w = sum(sc.size[0] for sc in scenes_) + 8 * (len(scenes_) + 1); h = 108
        im = Image.new('RGBA', (w, h), (0, 0, 0, 255))
        def lay(layer, shift):
            for x in range(-shift, w, layer.size[0]): im.alpha_composite(layer, (x, 0)) if x >= 0 else im.alpha_composite(layer.crop((-x, 0, layer.size[0], h)), (0, 0))
        for i, layer in enumerate(layers[:4]): lay(layer, 60 + i * 37)
        x = 8
        for sc in scenes_:
            im.alpha_composite(sc, (x, FOREST_GROUND - sc.size[1])); x += sc.size[0] + 8
        lay(layers[4], 20)
        return im

    a, b = row(grey, scenes), woods(scenes)
    out = Image.new('RGBA', (a.size[0], a.size[1] + b.size[1] + 4), (20, 20, 20, 255))
    out.alpha_composite(a, (0, 0)); out.alpha_composite(b, (0, a.size[1] + 4))
    out = out.resize((out.size[0] * 4, out.size[1] * 4), Image.NEAREST)
    out.save(os.path.join(REVIEW, 'lineup.png'))

    # frames.png: every frame of every sheet, 3x
    rows = [strip(frames) for frames, _ in sheets.values()]
    w = max(r.size[0] for r in rows) + 8; h = sum(r.size[1] + 6 for r in rows) + 6
    fr = Image.new('RGBA', (w, h), (120, 128, 120, 255)); y = 6
    for r in rows:
        fr.alpha_composite(r, (4, y)); y += r.size[1] + 6
    fr = fr.resize((fr.size[0] * 3, fr.size[1] * 3), Image.NEAREST)
    fr.save(os.path.join(REVIEW, 'frames.png'))


# ------------------------------------------------------------------ C# layout (read by BossGoliathRig)
CODE = os.path.join(ROOT, 'Assets', 'Monster', 'Boss', 'BossSpriteLayout.cs')


def pascal(n): return n[0].upper() + n[1:]


def write_code(sheets, layout):
    """Frame sizes, frame names and part anchors as C# constants, so art and code can never drift apart."""
    L = []; a = L.append
    a('// <auto-generated> Written by Tools/BossArt/boss_sprites.py. Do not edit: change the script and run it again. </auto-generated>')
    a('using UnityEngine;')
    a('')
    a('/// <summary>Frame layout of the boss sprite sheets (Assets/!Animation/Boss). Pixel coordinates use the art convention:')
    a('/// origin at the top-left of a frame, y pointing down.</summary>')
    a('public static class BossSpriteLayout')
    a('{')
    a('    public const float PixelsPerUnit = 11f;')
    a(f"    public const int PilotOverheatDrop = {layout['pilotOverheatDrop']};")
    a('')
    for name, (frames, names) in sheets.items():
        short = name.replace('Boss_', '').replace('BossFx_', 'Fx')
        w, h = frames[0].size
        a(f'    public static readonly Vector2Int {short}Size = new({w}, {h});')
        a(f'    public const int {short}Frames = {len(frames)};')
        if names and len(names) > 1:
            a(f'    public enum {short} {{ ' + ', '.join(pascal(n) for n in names) + ' }')
    a('')
    a('    /// <summary>Top-left pixel of each part inside a body frame.</summary>')
    a('    public readonly struct BodyAnchors')
    a('    {')
    a('        public readonly Vector2Int Gear, Furnace, Pilot, DrumLeft, DrumRight, StackLeft, StackRight;')
    a('        public BodyAnchors(int gx, int gy, int fx, int fy, int px, int py, int lx, int ly, int rx, int ry, int slx, int sly, int srx, int sry)')
    a('        {')
    a('            Gear = new(gx, gy); Furnace = new(fx, fy); Pilot = new(px, py); DrumLeft = new(lx, ly); DrumRight = new(rx, ry);')
    a('            StackLeft = new(slx, sly); StackRight = new(srx, sry);')
    a('        }')
    a('    }')
    a('')
    a('    /// <summary>Indexed by (int)Body.</summary>')
    a('    public static readonly BodyAnchors[] Anchors =')
    a('    {')
    for n, _ in BODY_FRAMES:
        b = layout['body'][n]
        v = b['gear'] + b['furnace'] + b['pilot'] + b['drumLeft'] + b['drumRight'] + b['stackLeft'] + b['stackRight']
        a('        new(' + ', '.join(str(x) for x in v) + f'),   // {n}')
    a('    };')
    a('}')
    os.makedirs(os.path.dirname(CODE), exist_ok=True)
    with open(CODE, 'w', encoding='utf-8', newline='\n') as f: f.write('\n'.join(L) + '\n')


if __name__ == '__main__':
    sheets = build()
    layout = write_assets(sheets)
    with open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'boss_layout.json'), 'w', encoding='utf-8') as f:
        json.dump(layout, f, indent=1)
    write_code(sheets, layout)
    if '--review' in sys.argv: review(sheets)
    print('sheets:', ', '.join(f'{n}({len(fr)})' for n, (fr, _) in sheets.items()))
