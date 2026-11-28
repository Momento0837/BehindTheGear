"""Boss concept frames in the team's hand-pixel style:
flat fills, NO outline, one hard shadow tone per material (right/bottom), rare highlights, faceless head,
no dithering / anti-aliasing, palette shared with Eden + GangB, one saturated accent (gang red / furnace)."""
from PIL import Image, ImageDraw
import sys

P = {
 # shared with GangB / Eden (hand-drawn sprites)
 'skin': (216,190,159), 'skin_d': (189,163,132), 'skin_dd': (181,151,116),
 'cloth': (54,54,54), 'cloth_d': (39,39,39),
 'red': (158,0,0),
 'wood': (141,79,8), 'wood_d': (122,68,6),
 'leather': (83,59,45), 'leather_d': (60,41,30),
 'steam': (245,240,237), 'steam_d': (238,233,230), 'steam_dd': (203,196,199),
 # new, kept in the same muted family
 'iron': (92,88,84), 'iron_m': (70,67,64), 'iron_d': (47,45,44), 'iron_dd': (33,32,31),
 'brass': (176,137,63), 'brass_d': (138,106,46),
 'glow': (208,99,42), 'glow_h': (240,194,122),
}

class Canvas:
    def __init__(s, w, h):
        s.im = Image.new('RGBA', (w, h), (0,0,0,0)); s.d = ImageDraw.Draw(s.im); s.w, s.h = w, h
    def c(s, k): return P[k] + (255,)
    def rect(s, x0, y0, x1, y1, k): s.d.rectangle([x0, y0, x1, y1], fill=s.c(k))
    def poly(s, pts, k): s.d.polygon(pts, fill=s.c(k))
    def ell(s, x0, y0, x1, y1, k): s.d.ellipse([x0, y0, x1, y1], fill=s.c(k))
    def px(s, x, y, k): s.im.putpixel((x, y), s.c(k))
    def line(s, pts, k, w=1): s.d.line(pts, fill=s.c(k), width=w)

def body(heat=0.0, tilt=0, gear_phase=0, slump=0):
    """Front-facing siege frame, 120x100. slump lowers the cockpit (overheat window)."""
    W, H = 120, 100
    cv = Canvas(W, H)
    cx = W // 2
    g = H - 1  # ground line
    # --- smokestacks (behind) ---
    for sx in (cx - 34, cx + 27):
        cv.rect(sx, 14 + slump, sx + 7, 46 + slump, 'iron_m')
        cv.rect(sx + 5, 14 + slump, sx + 7, 46 + slump, 'iron_d')
        cv.rect(sx - 1, 12 + slump, sx + 8, 15 + slump, 'iron_d')       # cap ring
        cv.rect(sx - 1, 24 + slump, sx + 8, 25 + slump, 'brass_d')      # band
    # --- legs: thick piston legs with wide iron feet ---
    for lx, side in ((cx - 30, -1), (cx + 14, 1)):
        cv.rect(lx + 2, g - 26, lx + 14, g - 7, 'iron_m')
        cv.rect(lx + 10, g - 26, lx + 14, g - 7, 'iron_d')
        cv.rect(lx + 4, g - 22, lx + 12, g - 19, 'brass')              # piston collar
        cv.rect(lx + 10, g - 22, lx + 12, g - 19, 'brass_d')
        cv.rect(lx - 3, g - 7, lx + 19, g, 'iron_d')                    # foot
        cv.rect(lx - 3, g - 7, lx + 19, g - 6, 'iron_m')
        cv.rect(lx + 13, g - 6, lx + 19, g, 'iron_dd')
    # --- hip drum ---
    cv.rect(cx - 34, g - 34 + slump // 2, cx + 33, g - 24 + slump // 2, 'iron_d')
    cv.rect(cx - 34, g - 34 + slump // 2, cx + 33, g - 33 + slump // 2, 'iron_m')
    for rx in range(cx - 30, cx + 31, 8): cv.px(rx, g - 29 + slump // 2, 'iron_m')   # rivets
    # --- boiler torso ---
    ty = 38 + slump
    cv.ell(cx - 40, ty, cx + 39, ty + 34, 'iron')
    cv.rect(cx - 40, ty + 10, cx + 39, ty + 30, 'iron')
    cv.ell(cx - 40, ty + 20, cx + 39, ty + 44, 'iron_m')                # belly curve (shadow below)
    cv.rect(cx - 40, ty + 17, cx + 39, ty + 30, 'iron')
    cv.rect(cx + 24, ty + 6, cx + 39, ty + 38, 'iron_m')                # right-side shadow
    cv.rect(cx + 34, ty + 12, cx + 39, ty + 34, 'iron_d')
    cv.rect(cx - 36, ty + 6, cx - 26, ty + 7, 'iron_m')                 # plate seam
    cv.rect(cx - 40, ty + 20, cx + 39, ty + 20, 'iron_m')
    for rx in range(cx - 36, cx + 37, 6): cv.px(rx, ty + 22, 'iron_d')  # rivet row
    # --- furnace mouth (accent) ---
    fy = ty + 26
    hot = 'glow' if heat < .5 else 'glow_h'
    cv.rect(cx - 15, fy, cx + 14, fy + 9, 'iron_dd')
    for i, bx in enumerate(range(cx - 13, cx + 13, 5)):
        cv.rect(bx, fy + 2, bx + 2, fy + 8, 'red')
        cv.rect(bx, fy + 5, bx + 2, fy + 8, hot)
    cv.rect(cx - 15, fy, cx + 14, fy + 1, 'iron_d')
    # --- chest gear (brass, signature of 'Behind The Gear') ---
    gx, gy, r = cx - 1, ty + 12, 9
    import math
    for k in range(8):
        a = (k / 8) * 2 * math.pi + gear_phase
        tx, tyy = gx + round(math.cos(a) * (r + 1)), gy + round(math.sin(a) * (r + 1))
        cv.rect(tx - 1, tyy - 1, tx + 1, tyy + 1, 'brass_d')
    cv.ell(gx - r, gy - r, gx + r, gy + r, 'brass')
    cv.ell(gx - r + 3, gy - r + 3, gx + r, gy + r, 'brass_d')           # hard shadow lower-right
    cv.ell(gx - r + 2, gy - r + 2, gx + r - 2, gy + r - 2, 'brass')
    cv.ell(gx - 3, gy - 3, gx + 3, gy + 3, 'iron_d')                    # hub
    cv.px(gx - 5, gy - 6, 'glow_h') if heat > .7 else cv.px(gx - 5, gy - 6, 'steam_dd')
    # --- shoulder chain spools (fists hang from these) ---
    for sx in (cx - 50, cx + 38):
        cv.rect(sx, ty + 2, sx + 11, ty + 13, 'iron_d')
        cv.rect(sx + 2, ty + 4, sx + 9, ty + 11, 'wood')
        cv.rect(sx + 6, ty + 4, sx + 9, ty + 11, 'wood_d')
        cv.rect(sx + 2, ty + 7, sx + 9, ty + 8, 'iron_dd')              # chain wound
    # --- cockpit + pilot (weak point) ---
    py = 18 + slump
    cv.rect(cx - 20, py + 16, cx + 19, py + 22, 'wood')                 # cockpit rim
    cv.rect(cx + 8, py + 16, cx + 19, py + 22, 'wood_d')
    cv.rect(cx - 20, py + 16, cx + 19, py + 16, 'brass_d')
    # pilot: broad faceless gang leader (same build language as GangB)
    cv.rect(cx - 13, py + 3, cx + 11, py + 16, 'cloth')                 # tank top torso
    cv.rect(cx + 4, py + 3, cx + 11, py + 16, 'cloth_d')
    cv.rect(cx - 19, py + 4, cx - 13, py + 14, 'skin')                  # left arm
    cv.rect(cx + 11, py + 4, cx + 17, py + 14, 'skin_d')                # right arm (shadow side)
    cv.rect(cx + 11, py + 6, cx + 17, py + 8, 'red')                    # red armband (gang)
    cv.rect(cx - 20, py + 13, cx - 14, py + 16, 'skin_d')               # hands on levers
    cv.rect(cx + 12, py + 13, cx + 18, py + 16, 'skin_dd')
    cv.rect(cx - 22, py + 8, cx - 21, py + 16, 'iron_d')                # levers
    cv.rect(cx + 20, py + 8, cx + 21, py + 16, 'iron_d')
    # head: bald, faceless, slightly sunk into shoulders
    cv.ell(cx - 7, py - 9, cx + 6, py + 4, 'skin')
    cv.ell(cx - 2, py - 7, cx + 6, py + 4, 'skin_d')
    cv.ell(cx - 6, py - 8, cx + 3, py + 2, 'skin')
    cv.rect(cx - 7, py - 4, cx + 6, py - 2, 'leather')                  # goggle strap
    cv.rect(cx - 5, py - 5, cx - 1, py - 2, 'brass')                    # goggles pushed up
    cv.rect(cx + 1, py - 5, cx + 5, py - 2, 'brass_d')
    return cv.im

def fist(clenched=True):
    """Iron gauntlet fist, front view knuckles down, 32x28."""
    cv = Canvas(32, 28)
    cv.rect(10, 0, 21, 6, 'iron_d')                                     # wrist cuff / chain mount
    cv.rect(14, 0, 17, 2, 'iron_dd')
    cv.rect(4, 5, 27, 22, 'iron')
    cv.rect(20, 5, 27, 22, 'iron_m')
    cv.rect(25, 8, 27, 22, 'iron_d')
    cv.rect(4, 5, 27, 7, 'brass')                                       # brass knuckle band
    cv.rect(20, 5, 27, 7, 'brass_d')
    for i, kx in enumerate((4, 10, 16, 22)):                            # knuckles
        k = 'iron' if kx < 18 else 'iron_m'
        cv.rect(kx, 21, kx + 5, 26, k)
        cv.rect(kx + 4, 21, kx + 5, 26, 'iron_d')
        cv.rect(kx, 26, kx + 5, 27, 'iron_dd')
    cv.rect(0, 10, 4, 20, 'iron_m')                                     # thumb
    cv.rect(0, 18, 4, 20, 'iron_d')
    for rx in (7, 13, 19): cv.px(rx, 13, 'iron_d')
    return cv.im

def chain_link():
    cv = Canvas(4, 6)
    cv.rect(0, 0, 3, 5, 'iron_d'); cv.rect(1, 1, 2, 4, 'iron_dd')
    return cv.im

if __name__ == '__main__':
    out = sys.argv[1]
    body().save(f'{out}/boss_body_idle.png')
    body(heat=1, slump=14).save(f'{out}/boss_body_overheat.png')
    fist().save(f'{out}/boss_fist.png')
    chain_link().save(f'{out}/boss_chain.png')
