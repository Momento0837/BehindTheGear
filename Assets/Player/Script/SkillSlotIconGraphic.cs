using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Procedural pixel-art skill emblem drawn inside an authored HUD slot: iron bezel,
/// accent rim and a pixel-art glyph (16x16, or larger for art traced from a game sprite). Presentation only; it never receives raycasts and
/// never moves/resizes the slot it lives in. State is pushed by InGameSkillHUD.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SkillSlotIconGraphic : MaskableGraphic
{
    public enum Glyph { None, Stealth, Dagger, Grenade, Shockwave }
    public enum State { Idle, Ready, Cooldown, Active, Blocked }

    public const float WellInset = 12f;

    // '.' empty, '#' outline/guard, '+' main, '-' shade, 'o' deep shadow / handle, '*' accent glow, '=' wrap.
    private static readonly string[] StealthArt =
    {
        "................",
        "......####......",
        "....##++++##....",
        "...#++++++++#...",
        "..#+++oooo+++#..",
        "..#++oooooo++#..",
        ".#++oooooooo++#.",
        ".#+oo*oooo*oo+#.",
        ".#+oooooooooo+#.",
        ".#++oooooooo++#.",
        "#+++oooooooo+++#",
        "#+-++oooooo++-+#",
        "#+-+++oooo+++-+#",
        "#++-+++oo+++-++#",
        "################",
        "................",
    };

    private static readonly string[] DaggerArt =
    {
        "..........................ooo.",
        "........................oollo.",
        ".......................odlldo.",
        ".....................oslmmms..",
        "...................oollmmdds..",
        ".................oollmmmmds...",
        "...............oollmmmmmsds...",
        ".............oollmmmmmmsdso...",
        "............ollmmmmmmmssdso...",
        "............olmmmmmmlsssso....",
        "...........olmmmmmmlssssso....",
        "...........omdmmmmlsssdso.....",
        "..........omddddmlssssdso.....",
        "..........omdddddosssddo......",
        "..........odddddoossssdo......",
        "..........oddddoooossdo.......",
        "..........ocddooooosddo.......",
        ".........ockroooooddoo........",
        "........ockrkrooddoo..........",
        ".......ockrkrooooo............",
        "......ockrkro.................",
        ".....ockrkro..................",
        "...oockrkro...................",
        "..odddrkro....................",
        ".odssddro.....................",
        "odmoosdo......................",
        "odmoosdo......................",
        ".odmmdo.......................",
        "..oddo........................",
        "...oo.........................",
    };

    private static readonly string[] GrenadeArt =
    {
        "................",
        "......##...**...",
        ".....#--#.*..*..",
        "....######...*..",
        "...#++++++#.*...",
        "..#++-++-++#....",
        "..#++++++++#....",
        "..#+-++-+++#....",
        "..#++++++++#....",
        "..#++-++-++#....",
        "..#++++++++#....",
        "...#++++++#.....",
        "....######......",
        "................",
        "................",
        "................",
    };

    private static readonly string[] ShockwaveArt =
    {
        "................",
        "................",
        ".....######.....",
        "...##......##...",
        "..#...####...#..",
        ".#...#....#...#.",
        ".#..#..**..#..#.",
        ".#..#.*##*.#..#.",
        ".#..#.*##*.#..#.",
        ".#..#..**..#..#.",
        ".#...#....#...#.",
        "..#...####...#..",
        "...##......##...",
        ".....######.....",
        "................",
        "................",
    };

    private Glyph glyph;
    private State state;
    private Color accent = Color.white;
    private int pulseStep = -1, flashStep = -1, shimmerStep = -1;

    public static Color AccentFor(Glyph value) => value switch
    {
        Glyph.Stealth => new Color(.70f, .55f, 1f),
        Glyph.Dagger => new Color(.55f, .92f, .45f),
        Glyph.Grenade => new Color(1f, .74f, .36f),
        Glyph.Shockwave => new Color(.55f, .80f, 1f),
        _ => new Color(.8f, .75f, .65f),
    };

    protected override void Awake() { base.Awake(); raycastTarget = false; }

    /// <param name="pulse">0..1 ready breathing.</param>
    /// <param name="flash">0..1 "just became ready" burst.</param>
    public void SetVisual(Glyph value, State current, float pulse, float flash)
    {
        int p = Mathf.RoundToInt(Mathf.Clamp01(pulse) * 10f);
        int f = Mathf.RoundToInt(Mathf.Clamp01(flash) * 10f);
        int s = current == State.Active ? Mathf.FloorToInt(Time.unscaledTime * 8f) % 4 : 0;
        if (value == glyph && current == state && p == pulseStep && f == flashStep && s == shimmerStep) return;
        if (value != glyph) accent = AccentFor(value);
        glyph = value; state = current; pulseStep = p; flashStep = f; shimmerStep = s;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect r = GetPixelAdjustedRect();
        if (r.width < 48 || r.height < 48) return;
        float pulse = Mathf.Max(0, pulseStep) / 10f, flash = Mathf.Max(0, flashStep) / 10f;

        // Bezel: shadow, dark iron frame, accent rim, recessed well.
        Stepped(mesh, r, 8, new Color(.03f, .024f, .02f, .95f), new Color(.03f, .024f, .02f, .95f));
        Rect frame = Inset(r, 2);
        Stepped(mesh, frame, 7, new Color(.22f, .19f, .165f), new Color(.11f, .095f, .085f));
        Rect rimRect = Inset(frame, 5);
        Stepped(mesh, rimRect, 4, RimColor(pulse, flash), Mul(RimColor(pulse, flash), .62f));
        Rect well = Inset(r, WellInset);
        Color wellTop = new(.075f, .062f, .058f), wellBottom = new(.035f, .028f, .026f);
        if (state == State.Ready || state == State.Active)
            wellBottom = Color.Lerp(wellBottom, Mul(accent, .32f), state == State.Active ? .55f : .25f + .2f * pulse);
        Stepped(mesh, well, 3, wellTop, wellBottom);
        Quad(mesh, new Rect(well.xMin + 3, well.yMax - 3, well.width - 6, 3), new Color(0, 0, 0, .5f));
        Quad(mesh, new Rect(frame.xMin + 9, frame.yMax - 2, frame.width - 18, 2), new Color(1, .9f, .75f, .08f));

        Rivet(mesh, frame.xMin + 5, frame.yMax - 5);
        Rivet(mesh, frame.xMax - 5, frame.yMax - 5);
        Rivet(mesh, frame.xMin + 5, frame.yMin + 5);
        Rivet(mesh, frame.xMax - 5, frame.yMin + 5);

        DrawGlyph(mesh, well);

        if (flash > 0f)
            Stepped(mesh, Inset(well, -2), 3, new Color(1, .95f, .82f, .22f * flash), new Color(1, .95f, .82f, .05f * flash));
    }

    private Color RimColor(float pulse, float flash)
    {
        Color copper = new(.42f, .34f, .26f);
        Color c = state switch
        {
            State.Ready => Color.Lerp(Mul(accent, .62f), accent, pulse),
            State.Active => Color.Lerp(accent, Color.white, .25f),
            State.Blocked => new Color(.55f, .24f, .20f),
            State.Cooldown => copper,
            _ => Mul(copper, .75f),
        };
        c = Color.Lerp(c, new Color(1, .96f, .86f), flash * .8f);
        c.a = 1; return c;
    }

    private void DrawGlyph(VertexHelper mesh, Rect well)
    {
        string[] art = glyph switch
        {
            Glyph.Stealth => StealthArt, Glyph.Dagger => DaggerArt,
            Glyph.Grenade => GrenadeArt, Glyph.Shockwave => ShockwaveArt, _ => null,
        };
        if (art == null) return;
        int rows = art.Length, cols = 0;
        foreach (var l in art) cols = Mathf.Max(cols, l.Length);
        // Integer pixel size keeps every art cell the same on-screen size (no uneven nearest-neighbour cells).
        float px = Mathf.Max(1, Mathf.Floor(Mathf.Min(well.width, well.height) * .8f / Mathf.Max(rows, cols)));
        float x0 = Mathf.Round(well.center.x - px * cols * .5f), y0 = Mathf.Round(well.center.y + px * rows * .5f);
        float brightness = state switch { State.Cooldown => .55f, State.Blocked => .5f, State.Idle => .45f, _ => 1f };
        float desaturate = state == State.Blocked || state == State.Idle ? .7f : 0f;
        for (int row = 0; row < art.Length; row++)
        {
            float alpha = 1f;
            if (state == State.Active && glyph == Glyph.Stealth)
                alpha = (row + shimmerStep) % 4 == 0 ? .35f : .72f; // Half-there shimmer while cloaked.
            string line = art[row];
            for (int col = 0; col < line.Length; col++)
            {
                if (!Palette(line[col], out Color c)) continue;
                c = Mul(c, brightness);
                if (desaturate > 0) { float g = c.r * .3f + c.g * .59f + c.b * .11f; c = Color.Lerp(c, new Color(g, g, g), desaturate); }
                c.a = alpha;
                Quad(mesh, new Rect(x0 + col * px, y0 - (row + 1) * px, px, px), c);
            }
        }
    }

    private bool Palette(char key, out Color color)
    {
        switch (glyph)
        {
            case Glyph.Dagger:
                // Colours sampled from Resources/PlayerSkills/PoisonDagger.png (the sprite the E skill actually throws).
                // The navy outline is lifted slightly so it still reads against the dark slot well.
                color = key switch
                {
                    'o' => new Color32(0x2A, 0x30, 0x52, 0xFF), 'l' => new Color32(0xA4, 0xB4, 0xD0, 0xFF),
                    'm' => new Color32(0x88, 0x98, 0xB8, 0xFF), 'd' => new Color32(0x5C, 0x6A, 0x8C, 0xFF),
                    's' => new Color32(0x38, 0x40, 0x60, 0xFF), 'c' => new Color32(0xE8, 0xA8, 0x78, 0xFF),
                    'r' => new Color32(0xB8, 0x74, 0x54, 0xFF), 'k' => new Color32(0x78, 0x40, 0x38, 0xFF),
                    _ => Color.clear,
                };
                break;
            case Glyph.Grenade:
                color = key switch
                {
                    '+' => new Color(.42f, .48f, .30f), '-' => new Color(.24f, .28f, .17f),
                    '#' => new Color(.13f, .14f, .10f), '*' => accent, _ => Color.clear,
                };
                break;
            default: // Stealth / Shockwave share an accent-driven palette.
                color = key switch
                {
                    '#' => Mul(accent, .42f), '+' => Mul(accent, .78f), '-' => Mul(accent, .55f),
                    'o' => new Color(.035f, .025f, .045f), '*' => Color.Lerp(accent, Color.white, .55f),
                    _ => Color.clear,
                };
                break;
        }
        return key != '.' && color.a > 0f;
    }

    private static void Rivet(VertexHelper mesh, float x, float y)
    {
        Quad(mesh, new Rect(x - 2, y - 2, 4, 4), new Color(.04f, .03f, .025f));
        Quad(mesh, new Rect(x - 1, y - 1, 2, 2), new Color(.55f, .46f, .36f));
    }

    private static Color Mul(Color c, float k) => new(c.r * k, c.g * k, c.b * k, c.a);
    private static Rect Inset(Rect r, float v) => new(r.xMin + v, r.yMin + v, r.width - 2 * v, r.height - 2 * v);

    private static void Stepped(VertexHelper mesh, Rect r, float cut, Color top, Color bottom)
    {
        Band(mesh, r, r.yMin, r.yMin + 2, cut, top, bottom);
        Band(mesh, r, r.yMin + 2, r.yMin + 4, Mathf.Max(0, cut - 2), top, bottom);
        Band(mesh, r, r.yMin + 4, r.yMax - 4, 0, top, bottom);
        Band(mesh, r, r.yMax - 4, r.yMax - 2, Mathf.Max(0, cut - 2), top, bottom);
        Band(mesh, r, r.yMax - 2, r.yMax, cut, top, bottom);
    }

    private static void Band(VertexHelper mesh, Rect r, float minY, float maxY, float inset, Color top, Color bottom)
    {
        var low = Color.Lerp(bottom, top, (minY - r.yMin) / r.height);
        var high = Color.Lerp(bottom, top, (maxY - r.yMin) / r.height);
        AddQuad(mesh, new Rect(r.xMin + inset, minY, r.width - 2 * inset, maxY - minY), low, high);
    }

    private static void Quad(VertexHelper mesh, Rect r, Color color) => AddQuad(mesh, r, color, color);

    private static void AddQuad(VertexHelper mesh, Rect r, Color bottom, Color top)
    {
        if (r.width <= 0 || r.height <= 0) return;
        int start = mesh.currentVertCount;
        mesh.AddVert(new Vector3(r.xMin, r.yMin), bottom, Vector2.zero);
        mesh.AddVert(new Vector3(r.xMin, r.yMax), top, Vector2.zero);
        mesh.AddVert(new Vector3(r.xMax, r.yMax), top, Vector2.zero);
        mesh.AddVert(new Vector3(r.xMax, r.yMin), bottom, Vector2.zero);
        mesh.AddTriangle(start, start + 1, start + 2);
        mesh.AddTriangle(start + 2, start + 3, start);
    }
}
