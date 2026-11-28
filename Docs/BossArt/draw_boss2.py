"""Boss concept (style anchor). Run: python draw_boss2.py <out_dir>
Iteration 2: break the symmetry and the ruler-straight geometry so it reads hand-placed like GangB.
Rules kept: no outline, flat fill + one hard shadow (right/lower), few highlight pixels, faceless head,
no dithering/AA, palette shared with Eden/GangB + iron/brass/furnace."""
from PIL import Image, ImageDraw
import math, sys
import os
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from draw_boss import P, Canvas

def body(heat=0.0, slump=0, gear_phase=0.0, lean=0):
    W, H = 112, 92
    cv = Canvas(W, H); cx = 56; g = H - 1
    s = slump
    # smokestacks: different heights, right one crooked
    cv.rect(cx - 33, 16 + s, cx - 27, 44 + s, 'iron_m'); cv.rect(cx - 29, 16 + s, cx - 27, 44 + s, 'iron_d')
    cv.rect(cx - 34, 14 + s, cx - 26, 16 + s, 'iron_d'); cv.rect(cx - 34, 25 + s, cx - 26, 26 + s, 'brass_d')
    cv.poly([(cx + 24, 44 + s), (cx + 24, 26 + s), (cx + 27, 22 + s), (cx + 27, 9 + s), (cx + 32, 9 + s), (cx + 32, 23 + s), (cx + 29, 27 + s), (cx + 29, 44 + s)], 'iron_m')
    cv.poly([(cx + 30, 9 + s), (cx + 32, 9 + s), (cx + 32, 23 + s), (cx + 29, 27 + s), (cx + 29, 44 + s), (cx + 28, 44 + s), (cx + 28, 26 + s), (cx + 30, 22 + s)], 'iron_d')
    cv.rect(cx + 26, 7 + s, cx + 33, 9 + s, 'iron_d')
    # legs (left one planted wider)
    for lx, w in ((cx - 31, 13), (cx + 15, 12)):
        cv.rect(lx + 2, g - 24, lx + w, g - 7, 'iron_m'); cv.rect(lx + w - 4, g - 24, lx + w, g - 7, 'iron_d')
        cv.rect(lx + 3, g - 20, lx + w - 1, g - 18, 'brass'); cv.rect(lx + w - 4, g - 20, lx + w - 1, g - 18, 'brass_d')
        cv.rect(lx - 3, g - 6, lx + w + 5, g, 'iron_d'); cv.rect(lx - 3, g - 6, lx + w + 5, g - 6, 'iron_m')
        cv.rect(lx + w, g - 5, lx + w + 5, g, 'iron_dd')
    # hip drum
    hy = g - 31 + s // 2
    cv.rect(cx - 33, hy, cx + 32, hy + 9, 'iron_d'); cv.rect(cx - 33, hy, cx + 32, hy, 'iron_m')
    # boiler: hand-shaped polygon (flatter top-left, sagging belly to the right)
    ty = 36 + s
    hull = [(cx - 34, ty + 6), (cx - 26, ty + 1), (cx - 8, ty - 1), (cx + 14, ty), (cx + 30, ty + 4), (cx + 38, ty + 12),
            (cx + 39, ty + 26), (cx + 34, ty + 34), (cx + 20, ty + 39), (cx - 6, ty + 40), (cx - 28, ty + 37), (cx - 37, ty + 30), (cx - 39, ty + 16)]
    cv.poly(hull, 'iron')
    cv.poly([(cx + 22, ty + 2), (cx + 30, ty + 4), (cx + 38, ty + 12), (cx + 39, ty + 26), (cx + 34, ty + 34), (cx + 20, ty + 39), (cx + 8, ty + 40), (cx + 20, ty + 31), (cx + 25, ty + 18)], 'iron_m')
    cv.poly([(cx + 35, ty + 13), (cx + 39, ty + 26), (cx + 34, ty + 34), (cx + 24, ty + 38), (cx + 33, ty + 27)], 'iron_d')
    cv.poly([(cx - 37, ty + 30), (cx - 28, ty + 37), (cx - 6, ty + 40), (cx + 20, ty + 39), (cx + 10, ty + 36), (cx - 20, ty + 35)], 'iron_m')
    # patch plates (hand repairs): bolted wooden plank + mismatched plate
    cv.poly([(cx - 37, ty + 15), (cx - 24, ty + 12), (cx - 23, ty + 19), (cx - 36, ty + 22)], 'wood')
    cv.poly([(cx - 30, ty + 16), (cx - 24, ty + 14), (cx - 23, ty + 19), (cx - 30, ty + 21)], 'wood_d')
    cv.px(cx - 34, ty + 17, 'iron_d'); cv.px(cx - 26, ty + 15, 'iron_d')
    cv.rect(cx + 12, ty + 5, cx + 21, ty + 11, 'iron_m'); cv.rect(cx + 19, ty + 5, cx + 21, ty + 11, 'iron_d')
    cv.px(cx + 13, ty + 6, 'iron_d'); cv.px(cx + 13, ty + 10, 'iron_d')
    # furnace mouth (lower, slightly off-centre)
    fy = ty + 25; hot = 'glow_h' if heat >= .5 else 'glow'
    cv.poly([(cx - 15, fy), (cx + 11, fy - 1), (cx + 12, fy + 8), (cx - 14, fy + 9)], 'iron_dd')
    for bx in range(cx - 12, cx + 10, 5):
        cv.rect(bx, fy + 2, bx + 2, fy + 7, 'red'); cv.rect(bx, fy + 4, bx + 2, fy + 7, hot)
    if heat >= .5: cv.rect(cx - 12, fy + 6, cx + 9, fy + 7, 'glow_h')
    # chest gear (off-centre left, rotating)
    gx, gy, r = cx - 6, ty + 12, 8
    for k in range(8):
        a = k / 8 * 2 * math.pi + gear_phase
        tx, tyy = gx + round(math.cos(a) * (r + 1)), gy + round(math.sin(a) * (r + 1))
        cv.rect(tx - 1, tyy - 1, tx + 1, tyy + 1, 'brass_d')
    cv.ell(gx - r, gy - r, gx + r, gy + r, 'brass_d')
    cv.ell(gx - r, gy - r, gx + r - 2, gy + r - 2, 'brass')
    cv.ell(gx - 3, gy - 3, gx + 2, gy + 2, 'iron_dd')
    cv.px(gx - 4, gy - 5, 'steam_d')
    # shoulder chain spools; right one carries the gang banner (red accent)
    for sx in (cx - 48, cx + 36):
        cv.rect(sx, ty + 1, sx + 11, ty + 12, 'iron_d'); cv.rect(sx + 2, ty + 3, sx + 8, ty + 10, 'wood')
        cv.rect(sx + 6, ty + 3, sx + 8, ty + 10, 'wood_d'); cv.rect(sx + 2, ty + 6, sx + 8, ty + 7, 'iron_dd')
    cv.poly([(cx + 40, ty + 12), (cx + 46, ty + 12), (cx + 46, ty + 27), (cx + 43, ty + 24), (cx + 40, ty + 28)], 'red')
    # cockpit rim (tilted plank)
    py = 17 + s + lean
    cv.poly([(cx - 21, py + 17), (cx + 19, py + 15), (cx + 20, py + 21), (cx - 20, py + 23)], 'wood')
    cv.poly([(cx + 6, py + 16), (cx + 19, py + 15), (cx + 20, py + 21), (cx + 6, py + 22)], 'wood_d')
    # pilot: hunched gang leader, head pushed forward/low like GangB, one arm up on a tall lever
    cv.poly([(cx - 14, py + 5), (cx + 10, py + 3), (cx + 13, py + 16), (cx - 13, py + 17)], 'cloth')
    cv.poly([(cx + 2, py + 4), (cx + 10, py + 3), (cx + 13, py + 16), (cx + 4, py + 16)], 'cloth_d')
    cv.rect(cx - 20, py + 6, cx - 14, py + 15, 'skin'); cv.rect(cx - 21, py + 13, cx - 15, py + 17, 'skin_d')   # left arm down to lever
    cv.rect(cx - 23, py + 9, cx - 22, py + 18, 'iron_d')
    cv.rect(cx + 10, py - 7, cx + 15, py + 4, 'skin_d'); cv.rect(cx + 13, py - 7, cx + 15, py + 4, 'skin_dd')   # right arm raised
    cv.rect(cx + 10, py - 2, cx + 15, py, 'red')                                                          # gang armband
    cv.rect(cx + 10, py - 11, cx + 16, py - 7, 'skin_dd')                                                     # fist on lever
    cv.rect(cx + 17, py - 14, cx + 18, py + 16, 'iron_d'); cv.rect(cx + 16, py - 15, cx + 19, py - 14, 'brass')
    # shoulders: the raised arm grows out of a solid deltoid so it never floats
    cv.poly([(cx + 4, py + 3), (cx + 12, py + 1), (cx + 16, py + 4), (cx + 14, py + 9), (cx + 6, py + 8)], 'skin_d')
    cv.poly([(cx - 15, py + 5), (cx - 8, py + 3), (cx - 6, py + 8), (cx - 14, py + 9)], 'skin')
    # head (bald, faceless) sunk low between the shoulders, pushed forward like GangB
    cv.rect(cx - 5, py + 3, cx + 3, py + 6, 'cloth_d')                                                        # neck shadow separates head from torso
    cv.ell(cx - 7, py - 7, cx + 4, py + 4, 'skin')
    cv.poly([(cx + 0, py - 6), (cx + 4, py - 3), (cx + 4, py + 1), (cx + 1, py + 4), (cx - 2, py + 4)], 'skin_d')
    cv.rect(cx - 4, py + 4, cx + 1, py + 4, 'skin_dd')
    cv.rect(cx - 7, py - 5, cx + 4, py - 5, 'leather')                                                        # goggles strap on crown
    cv.rect(cx - 5, py - 7, cx - 2, py - 5, 'brass'); cv.rect(cx - 1, py - 7, cx + 1, py - 5, 'brass_d')
    return cv.im

def fist():
    cv = Canvas(30, 26)
    cv.rect(10, 0, 19, 5, 'iron_d'); cv.rect(13, 0, 16, 1, 'iron_dd')
    cv.poly([(3, 6), (26, 5), (28, 9), (28, 20), (25, 23), (4, 23), (2, 19)], 'iron')
    cv.poly([(19, 5), (26, 5), (28, 9), (28, 20), (25, 23), (19, 23)], 'iron_m')
    cv.poly([(25, 8), (28, 9), (28, 20), (25, 23)], 'iron_d')
    cv.poly([(3, 6), (26, 5), (27, 8), (3, 9)], 'brass'); cv.poly([(19, 5), (26, 5), (27, 8), (19, 8)], 'brass_d')
    for kx, k in ((4, 'iron'), (10, 'iron'), (16, 'iron_m'), (21, 'iron_m')):
        cv.rect(kx, 19, kx + 4, 24, k); cv.rect(kx + 3, 19, kx + 4, 24, 'iron_d'); cv.rect(kx, 24, kx + 4, 25, 'iron_dd')
    cv.poly([(0, 11), (4, 10), (5, 18), (1, 19)], 'iron_m'); cv.rect(1, 17, 4, 19, 'iron_d')
    cv.px(8, 13, 'iron_d'); cv.px(14, 12, 'iron_d'); cv.px(21, 14, 'iron_dd')
    return cv.im

def chain_link(vertical=True):
    cv = Canvas(3, 5); cv.rect(0, 0, 2, 4, 'iron_d'); cv.rect(1, 1, 1, 3, 'iron_dd'); return cv.im

if __name__ == '__main__':
    o = sys.argv[1]
    body().save(f'{o}/v2_idle.png'); body(heat=1, slump=13).save(f'{o}/v2_overheat.png'); fist().save(f'{o}/v2_fist.png')
