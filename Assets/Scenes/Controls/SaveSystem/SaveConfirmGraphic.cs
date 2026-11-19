using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pixel-stepped ironwork for the save confirmation dialog. Shares the main menu's
/// stepped-metal language but uses a cooler, ash-dark "warning notice" mood:
/// bracketed recessed buttons instead of copper plates, and a tone-tinted rim.
/// Presentation only; it never receives raycasts.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SaveConfirmGraphic : MaskableGraphic
{
    public enum Mode { Panel, Button }
    public enum Tone { Caution, Danger }

    private static readonly Color CautionAccent = new(.86f, .62f, .30f);
    private static readonly Color DangerAccent = new(.80f, .32f, .24f);

    private Mode mode;
    private Tone tone;
    private bool focused, pressed, confirmAction;
    private float introStart = -1f;
    private const float IntroSeconds = .16f;

    public static Color AccentFor(Tone value) => value == Tone.Danger ? DangerAccent : CautionAccent;

    public void Configure(Mode value, bool isConfirmAction = false)
    {
        mode = value; confirmAction = isConfirmAction; raycastTarget = false;
        SetVerticesDirty();
    }

    public void SetTone(Tone value)
    {
        if (tone == value) return;
        tone = value; SetVerticesDirty();
    }

    public void SetState(bool focus, bool down)
    {
        if (focused == focus && pressed == down) return;
        focused = focus; pressed = down; SetVerticesDirty();
    }

    /// <summary>Short scale/fade-in each time the dialog opens (unscaled time, panel only).</summary>
    public void PlayIntro()
    {
        if (mode != Mode.Panel) return;
        introStart = Time.unscaledTime;
        ApplyIntro(0f);
    }

    private void Update()
    {
        if (mode != Mode.Panel || introStart < 0f) return;
        float t = Mathf.Clamp01((Time.unscaledTime - introStart) / IntroSeconds);
        ApplyIntro(t);
        if (t >= 1f) introStart = -1f;
    }

    private void ApplyIntro(float t)
    {
        float eased = 1f - (1f - t) * (1f - t);
        rectTransform.localScale = Vector3.one * Mathf.Lerp(.94f, 1f, eased);
        var group = GetComponent<CanvasGroup>();
        if (group != null) group.alpha = Mathf.Lerp(0f, 1f, eased);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (mode != Mode.Panel) return;
        introStart = -1f;
        rectTransform.localScale = Vector3.one;
        var group = GetComponent<CanvasGroup>();
        if (group != null) group.alpha = 1f;
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect r = GetPixelAdjustedRect();
        if (r.width < 40 || r.height < 30) return;
        if (mode == Mode.Panel) Panel(mesh, r);
        else ButtonPlate(mesh, r);
    }

    private void Panel(VertexHelper mesh, Rect r)
    {
        Color accent = AccentFor(tone);
        // Drop shadow, then an ash-iron frame (cooler than the main menu's copper plates).
        Stepped(mesh, new Rect(r.xMin + 4, r.yMin - 8, r.width - 8, r.height), 10, new Color(0, 0, 0, .55f), new Color(0, 0, 0, .55f));
        Stepped(mesh, r, 10, new Color(.035f, .028f, .025f), new Color(.035f, .028f, .025f));
        Rect frame = Inset(r, 2, 2);
        Stepped(mesh, frame, 9, new Color(.19f, .165f, .15f), new Color(.10f, .088f, .08f));
        Rect rimRect = Inset(frame, 5, 5);
        Stepped(mesh, rimRect, 6, Mul(accent, .62f), Mul(accent, .38f));
        Rect face = Inset(rimRect, 2, 2);
        Stepped(mesh, face, 5, new Color(.085f, .068f, .062f), new Color(.042f, .034f, .031f));
        // Top lip highlight and lower inset shadow.
        Quad(mesh, new Rect(face.xMin + 8, face.yMax - 2, face.width - 16, 2), new Color(1, .9f, .75f, .06f));
        Quad(mesh, new Rect(face.xMin + 6, face.yMin, face.width - 12, 3), new Color(0, 0, 0, .35f));

        // Header rules flanking the gear seal (seal itself is an Image child).
        float ruleY = Mathf.Round(face.yMax - 36);
        float cx = Mathf.Round(face.center.x);
        Rule(mesh, face.xMin + 34, cx - 34, ruleY, accent);
        Rule(mesh, cx + 34, face.xMax - 34, ruleY, accent);

        // Divider above the action row.
        Quad(mesh, new Rect(face.xMin + 40, Mathf.Round(face.yMin + 96), face.width - 80, 1), Mul(accent, .32f, .9f));

        // Mood strip: hazard ticks for destructive actions, a quiet rivet line for cautions.
        float stripY = face.yMin + 10;
        if (tone == Tone.Danger)
        {
            for (float x = face.xMin + 18; x + 10 < face.xMax - 18; x += 16)
            {
                Quad(mesh, new Rect(x, stripY, 8, 2), Mul(accent, .55f));
                Quad(mesh, new Rect(x + 2, stripY + 2, 8, 2), Mul(accent, .38f));
            }
        }
        else
        {
            for (float x = face.xMin + 22; x + 2 < face.xMax - 20; x += 22)
                Quad(mesh, new Rect(x, stripY + 1, 2, 2), Mul(accent, .42f));
        }

        Rivet(mesh, frame.xMin + 12, frame.yMax - 12);
        Rivet(mesh, frame.xMax - 12, frame.yMax - 12);
        Rivet(mesh, frame.xMin + 12, frame.yMin + 12);
        Rivet(mesh, frame.xMax - 12, frame.yMin + 12);
    }

    private void ButtonPlate(VertexHelper mesh, Rect r)
    {
        Color accent = AccentFor(tone);
        bool hot = confirmAction && tone == Tone.Danger;
        Stepped(mesh, r, 4, new Color(.025f, .02f, .018f), new Color(.025f, .02f, .018f));
        Rect face = Inset(r, 2, 2);
        Color top = hot ? new Color(.15f, .085f, .07f) : new Color(.125f, .108f, .098f);
        Color bottom = hot ? new Color(.09f, .05f, .042f) : new Color(.075f, .064f, .058f);
        if (focused) { top = Color.Lerp(top, Mul(accent, .42f), .22f); bottom = Color.Lerp(bottom, Mul(accent, .22f), .15f); }
        if (pressed) (top, bottom) = (Mul(bottom, .8f), Mul(top, .85f));
        Stepped(mesh, face, 3, top, bottom);
        // Recessed edges: dark top inner shadow when pressed, faint lip otherwise.
        Quad(mesh, new Rect(face.xMin + 4, face.yMax - 2, face.width - 8, 2),
            pressed ? new Color(0, 0, 0, .45f) : new Color(1, .9f, .75f, focused ? .10f : .05f));

        // Neutral iron brackets at rest; a soft accent tint and slight outward step only while hovered.
        Color restBracket = new(.36f, .32f, .28f);
        Color bracket = focused ? Color.Lerp(restBracket, accent, .7f) : restBracket;
        float o = pressed ? 4 : focused ? 0 : 2;
        Bracket(mesh, r.xMin + o, r.yMax - o, 1, -1, bracket);
        Bracket(mesh, r.xMax - o, r.yMax - o, -1, -1, bracket);
        Bracket(mesh, r.xMin + o, r.yMin + o, 1, 1, bracket);
        Bracket(mesh, r.xMax - o, r.yMin + o, -1, 1, bracket);
    }

    private static void Rule(VertexHelper mesh, float x0, float x1, float y, Color accent)
    {
        if (x1 - x0 < 12) return;
        Quad(mesh, new Rect(x0 + 6, y, x1 - x0 - 12, 2), Mul(accent, .58f));
        Quad(mesh, new Rect(x0 + 6, y - 1, x1 - x0 - 12, 1), new Color(0, 0, 0, .4f));
        // Small stepped diamonds on both ends.
        foreach (float x in new[] { x0 + 2, x1 - 2 })
        {
            Quad(mesh, new Rect(x - 1, y - 2, 2, 6), Mul(accent, .8f));
            Quad(mesh, new Rect(x - 3, y, 6, 2), Mul(accent, .8f));
        }
    }

    private static void Bracket(VertexHelper mesh, float x, float y, int dx, int dy, Color color)
    {
        const float len = 10, t = 2;
        Quad(mesh, new Rect(dx > 0 ? x : x - len, dy > 0 ? y : y - t, len, t), color);
        Quad(mesh, new Rect(dx > 0 ? x : x - t, dy > 0 ? y : y - len, t, len), color);
    }

    private static void Rivet(VertexHelper mesh, float x, float y)
    {
        Quad(mesh, new Rect(x - 3, y - 3, 6, 6), new Color(.03f, .024f, .02f));
        Quad(mesh, new Rect(x - 2, y - 1, 4, 4), new Color(.36f, .31f, .27f));
        Quad(mesh, new Rect(x - 1, y + 1, 2, 1), new Color(.55f, .49f, .42f));
    }

    private static Color Mul(Color c, float k, float a = 1f) => new(c.r * k, c.g * k, c.b * k, a);
    private static Rect Inset(Rect r, float x, float y) => new(r.xMin + x, r.yMin + y, r.width - 2 * x, r.height - 2 * y);

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
