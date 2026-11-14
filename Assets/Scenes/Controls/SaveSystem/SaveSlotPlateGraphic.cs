using UnityEngine;
using UnityEngine.UI;

/// <summary>Pixel-stepped metalwork in canvas units: resizing never stretches the chamfers or pins.</summary>
[RequireComponent(typeof(CanvasRenderer))]
public sealed class SaveSlotPlateGraphic : MaskableGraphic
{
    private Color rim, top, bottom, edge, pin;
    private bool pressed;

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
        Stepped(mesh, Inset(r, 0, 0), 6, new Color(.035f, .025f, .02f), new Color(.035f, .025f, .02f));
        Rect plate = new(r.xMin, r.yMin + 4, r.width, r.height - 4);
        Stepped(mesh, plate, 6, rim, rim * new Color(.65f, .65f, .65f, 1));
        Rect face = Inset(plate, 2, 2);
        Stepped(mesh, face, 5, top, bottom);
        // Thin top lip, copper rules and inset lower shadow; no blurred/glowing outline.
        Quad(mesh, new Rect(face.xMin + 7, face.yMax - 2, face.width - 14, 2), pressed ? bottom : edge);
        Quad(mesh, new Rect(face.xMin + 7, face.yMin, face.width - 14, pressed ? 2 : 4),
            pressed ? edge * new Color(.5f, .5f, .5f, 1) : new Color(.055f, .04f, .03f));
        if (pressed) Quad(mesh, new Rect(face.xMin + 5, face.yMax - 6, face.width - 10, 4), new Color(.045f, .03f, .025f, .8f));
        // Restrained machining lines remain fixed-width when the control is resized.
        for (int i = 0; i < 3; i++)
            Quad(mesh, new Rect(face.xMin + 25, Mathf.Round(face.yMin + face.height * (.26f + i * .2f)),
                face.width - 50, 1), new Color(1, .86f, .68f, .025f));
        Pin(mesh, face.xMax - 13, face.center.y);
    }

    private void Pin(VertexHelper mesh, float x, float y)
    {
        Quad(mesh, new Rect(x - 3, y - 3, 6, 6), new Color(.04f, .03f, .025f));
        Quad(mesh, new Rect(x - 2, y - 1, 4, 4), pin);
        Quad(mesh, new Rect(x - 1, y, 2, 1), new Color(.10f, .075f, .055f));
    }

    private static Rect Inset(Rect r, float x, float y) => new(r.xMin + x, r.yMin + y, r.width - 2 * x, r.height - 2 * y);

    private static void Stepped(VertexHelper mesh, Rect r, float cut, Color top, Color bottom)
    {
        // Three horizontal bands make 2px steps rather than diagonal or rounded corners.
        Band(mesh, r, r.yMin, r.yMin + 2, cut, top, bottom);
        Band(mesh, r, r.yMin + 2, r.yMin + 4, cut - 2, top, bottom);
        Band(mesh, r, r.yMin + 4, r.yMax - 4, 0, top, bottom);
        Band(mesh, r, r.yMax - 4, r.yMax - 2, cut - 2, top, bottom);
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
