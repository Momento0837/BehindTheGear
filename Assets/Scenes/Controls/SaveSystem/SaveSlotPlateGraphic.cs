using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pixel-stepped metalwork in canvas units: resizing never stretches the rounded corners or pins.
/// Corners are drawn as 2-unit stair steps along a circle (CornerRadius); a radius of half the height gives a capsule.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SaveSlotPlateGraphic : MaskableGraphic
{
    private Color rim, top, bottom, edge, pin;
    private bool pressed;
    private float cornerRadius = 16f;
    private bool showPin = true;

    /// <summary>Corner radius in canvas units (clamped to half the plate height, which makes a capsule).</summary>
    public float CornerRadius { get => cornerRadius; set { cornerRadius = Mathf.Max(0f, value); SetVerticesDirty(); } }
    /// <summary>The small rivet at the right end of the plate.</summary>
    public bool ShowPin { get => showPin; set { showPin = value; SetVerticesDirty(); } }

    public void SetAppearance(Color border, Color faceTop, Color faceBottom, Color highlight, Color hardware, bool down)
    {
        rim = border; top = faceTop; bottom = faceBottom; edge = highlight; pin = hardware; pressed = down;
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper mesh)
    {
        mesh.Clear();
        Rect r = GetPixelAdjustedRect();
        if (r.width < 28 || r.height < 28) return;
        Rect plate = new(r.xMin, r.yMin + 4, r.width, r.height - 4);
        float radius = Mathf.Min(cornerRadius, plate.height * .5f);
        // Drop shadow: the same rounded shape, peeking out 4 units under the plate.
        Rounded(mesh, r, radius, new Color(.035f, .025f, .02f, .9f), new Color(.035f, .025f, .02f, .9f));
        Rounded(mesh, plate, radius, rim, rim * new Color(.65f, .65f, .65f, 1));
        Rect face = Inset(plate, 2, 2);
        Rounded(mesh, face, Mathf.Max(0f, radius - 2f), top, bottom);
        // Thin top lip and inset lower shadow, kept clear of the rounded ends; no blurred/glowing outline.
        float lip = Mathf.Max(7f, Mathf.Round(radius * .8f));
        Quad(mesh, new Rect(face.xMin + lip, face.yMax - 2, face.width - 2 * lip, 2), pressed ? bottom : edge);
        Quad(mesh, new Rect(face.xMin + lip, face.yMin, face.width - 2 * lip, pressed ? 2 : 4),
            pressed ? edge * new Color(.5f, .5f, .5f, 1) : new Color(.055f, .04f, .03f));
        if (pressed) Quad(mesh, new Rect(face.xMin + lip - 2, face.yMax - 6, face.width - 2 * (lip - 2), 4), new Color(.045f, .03f, .025f, .8f));
        // Restrained machining lines remain fixed-width when the control is resized.
        float rule = Mathf.Max(25f, radius + 10f);
        for (int i = 0; i < 3; i++)
            Quad(mesh, new Rect(face.xMin + rule, Mathf.Round(face.yMin + face.height * (.26f + i * .2f)),
                face.width - 2 * rule, 1), new Color(1, .86f, .68f, .025f));
        if (showPin) Pin(mesh, face.xMax - Mathf.Max(13f, Mathf.Round(radius * .85f)), face.center.y);
    }

    private void Pin(VertexHelper mesh, float x, float y)
    {
        Quad(mesh, new Rect(x - 3, y - 3, 6, 6), new Color(.04f, .03f, .025f));
        Quad(mesh, new Rect(x - 2, y - 1, 4, 4), pin);
        Quad(mesh, new Rect(x - 1, y, 2, 1), new Color(.10f, .075f, .055f));
    }

    private static Rect Inset(Rect r, float x, float y) => new(r.xMin + x, r.yMin + y, r.width - 2 * x, r.height - 2 * y);

    // Horizontal 2-unit bands whose insets follow a circle: pixel-stepped rounded corners, never diagonal or blurred.
    private static void Rounded(VertexHelper mesh, Rect r, float radius, Color top, Color bottom)
    {
        const float step = 2f;
        radius = Mathf.Min(radius, r.height * .5f, r.width * .5f);
        if (radius < step) { Band(mesh, r, r.yMin, r.yMax, 0, top, bottom); return; }
        for (float y = 0f; y < radius; y += step)
        {
            float next = Mathf.Min(y + step, radius);
            float fromCentre = radius - (y + next) * .5f;
            float inset = radius - Mathf.Sqrt(Mathf.Max(0f, radius * radius - fromCentre * fromCentre));
            inset = Mathf.Round(inset / step) * step;
            Band(mesh, r, r.yMin + y, r.yMin + next, inset, top, bottom);
            Band(mesh, r, r.yMax - next, r.yMax - y, inset, top, bottom);
        }
        Band(mesh, r, r.yMin + radius, r.yMax - radius, 0, top, bottom);
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
