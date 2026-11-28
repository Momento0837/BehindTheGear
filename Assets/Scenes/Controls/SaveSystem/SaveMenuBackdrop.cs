using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Backdrop of the save menu: the inside of the clock tower from the main menu, i.e. "behind the gear".
/// A gear train turns in clockwork steps on the left, the back of the tower clock glows on the right and shows the
/// real local time (mirrored, because it is seen from behind), dust drifts in its light and a pipe vents steam.
/// The centre column stays calm for the title and the buttons.
///
/// Pixel art (224x126, same palette as the team's main menu) lives in Resources/UI/SaveMenu; everything is laid out in
/// art pixels and scaled to cover the screen. ManualSaveMenu calls Build once; a missing texture only skips its layer.
/// </summary>
public sealed class SaveMenuBackdrop : MonoBehaviour
{
    private const string Folder = "UI/SaveMenu/";
    private const int Width = 224, Height = 126;       // art pixels (16:9)
    private const float StepsPerSecond = 6f;            // gear frames per second (frame-stepped on purpose)
    private const int ClockLeft = 170, ClockTop = 32, ClockSize = 49;
    private const float GlassFlicker = .07f;
    private const int DustCount = 10, SteamCount = 6;
    private static readonly Vector2 ClockCentre = new(194f, 56f);
    private static readonly Vector2 SteamVent = new(177f, 103f);
    private static readonly Color32 HandColor = new(29, 18, 17, 255);
    private static readonly Color32 SecondHandColor = new(110, 52, 40, 255);
    private static readonly Color DustColor = new(.95f, .78f, .6f);
    private static readonly Color SteamColor = new(.86f, .78f, .72f);

    // name, centre x / y (art pixels, y down), frame size, frame count, sheet columns: written by the art generator
    private static readonly GearSpec[] Gears =
    {
        new("SaveMenu_GearLarge", 16f, 66f, 85, 12, 5),
        new("SaveMenu_GearMedium", 43.69f, 21.69f, 37, 9, 6),
        new("SaveMenu_GearSmall", 46.53f, 102.39f, 27, 6, 6),
    };

    private readonly struct GearSpec
    {
        public readonly string Texture; public readonly float X, Y; public readonly int Size, Frames, Columns;
        public GearSpec(string texture, float x, float y, int size, int frames, int columns)
        { Texture = texture; X = x; Y = y; Size = size; Frames = frames; Columns = columns; }
    }

    private sealed class Speck
    {
        public RectTransform rect; public Image image;
        public Vector2 position, velocity; public float age, life, size, phase;
    }

    private RectTransform stage, middle, front;
    private Image glass;
    private Image[] gearImages;
    private Sprite[][] gearFrames;
    private Texture2D handsTexture;
    private Color32[] handsPixels;
    private Speck[] dust, steam;
    private float stepTimer, nextSteam, steamBurstLeft, nextPuff;
    private int step, lastSecond = -1;
    private Vector2 parallax;

    /// <summary>Adds the backdrop behind everything else in the save menu panel.</summary>
    public static SaveMenuBackdrop Build(Transform panel)
    {
        var go = new GameObject("Backdrop", typeof(RectTransform));
        go.transform.SetParent(panel, false);
        go.transform.SetAsFirstSibling();
        var rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
        return go.AddComponent<SaveMenuBackdrop>();
    }

    private void Awake()
    {
        stage = Group("Stage", transform);
        stage.anchorMin = stage.anchorMax = stage.pivot = new Vector2(.5f, .5f);
        stage.sizeDelta = new Vector2(Width, Height);

        Layer("Wall", stage, Whole(Load("SaveMenu_Back")), 0, 0, Width, Height);

        middle = Group("Gears", stage);
        Fill(middle);
        gearImages = new Image[Gears.Length];
        gearFrames = new Sprite[Gears.Length][];
        for (int i = 0; i < Gears.Length; i++)
        {
            var spec = Gears[i];
            gearFrames[i] = Slice(Load(spec.Texture), spec);
            if (gearFrames[i].Length == 0) continue;
            float half = (spec.Size - 1) * .5f;
            gearImages[i] = Layer(spec.Texture, middle, gearFrames[i][0],
                Mathf.Round(spec.X - half), Mathf.Round(spec.Y - half), spec.Size, spec.Size);
        }

        glass = Layer("Clock Glass", stage, Whole(Load("SaveMenu_Glass")), ClockLeft, ClockTop, ClockSize, ClockSize);
        BuildHands();
        dust = Specks("Dust", stage, DustCount);
        steam = Specks("Steam", stage, SteamCount);

        front = Group("Front", stage);
        Fill(front);
        var frontTexture = Load("SaveMenu_Front");
        if (frontTexture != null) // drawn 4 px wider on each side so the parallax never shows an edge
            Layer("Catwalk", front, Whole(frontTexture), -(frontTexture.width - Width) * .5f, 0, frontTexture.width, frontTexture.height);

        nextSteam = Time.unscaledTime + 1.5f;
        for (int i = 0; i < dust.Length; i++) { Respawn(dust[i]); dust[i].age = UnityEngine.Random.Range(0f, dust[i].life); }
        Fit();
    }

    private void OnDestroy()
    {
        if (handsTexture != null) Destroy(handsTexture);
    }

    // ---------- building ----------
    private static RectTransform Group(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static void Fill(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    // x / top are art pixels from the top-left corner of the stage.
    private static Image Layer(string name, Transform parent, Sprite sprite, float x, float top, float width, float height)
    {
        if (sprite == null) return null;
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var image = go.GetComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        var rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -top);
        rect.sizeDelta = new Vector2(width, height);
        return image;
    }

    private static Texture2D Load(string name)
    {
        var texture = Resources.Load<Texture2D>(Folder + name);
        if (texture == null) { Debug.LogWarning("[Save Menu] Backdrop art is missing: Resources/" + Folder + name); return null; }
        texture.filterMode = FilterMode.Point;
        return texture;
    }

    private static Sprite Whole(Texture2D texture) => texture == null ? null
        : Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .5f), 1f, 0, SpriteMeshType.FullRect);

    // Frames run left to right, top to bottom in the sheet.
    private static Sprite[] Slice(Texture2D texture, GearSpec spec)
    {
        if (texture == null) return Array.Empty<Sprite>();
        var frames = new Sprite[spec.Frames];
        for (int i = 0; i < spec.Frames; i++)
        {
            int column = i % spec.Columns, row = i / spec.Columns;
            var rect = new Rect(column * spec.Size, texture.height - (row + 1) * spec.Size, spec.Size, spec.Size);
            frames[i] = Sprite.Create(texture, rect, new Vector2(.5f, .5f), 1f, 0, SpriteMeshType.FullRect);
        }
        return frames;
    }

    private void BuildHands()
    {
        handsTexture = new Texture2D(ClockSize, ClockSize, TextureFormat.RGBA32, false)
        { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Clock Hands" };
        handsPixels = new Color32[ClockSize * ClockSize];
        Layer("Clock Hands", stage, Whole(handsTexture), ClockLeft, ClockTop, ClockSize, ClockSize);
    }

    private static Speck[] Specks(string name, Transform parent, int count)
    {
        var specks = new Speck[count];
        for (int i = 0; i < count; i++)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image)); // an Image without a sprite is a plain square
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            image.color = Color.clear;
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(.5f, .5f);
            rect.sizeDelta = Vector2.one;
            specks[i] = new Speck { rect = rect, image = image, life = 0f, age = 1f };
        }
        return specks;
    }

    // ---------- animation ----------
    private void Update()
    {
        float delta = Mathf.Min(Time.unscaledDeltaTime, .1f);
        Fit();
        TurnGears(delta);
        TickClock();
        DriftDust(delta);
        VentSteam(delta);
        Parallax(delta);
    }

    // Cover the whole panel, keeping the 16:9 art uncropped on a 16:9 screen.
    private void Fit()
    {
        Vector2 size = ((RectTransform)transform).rect.size;
        if (size.x <= 0f || size.y <= 0f) return;
        float scale = Mathf.Max(size.x / Width, size.y / Height);
        stage.localScale = new Vector3(scale, scale, 1f);
    }

    private void TurnGears(float delta)
    {
        stepTimer += delta * StepsPerSecond;
        if (stepTimer < 1f) return;
        step += Mathf.FloorToInt(stepTimer);
        stepTimer -= Mathf.Floor(stepTimer);
        for (int i = 0; i < gearImages.Length; i++)
            if (gearImages[i] != null) gearImages[i].sprite = gearFrames[i][step % gearFrames[i].Length];
    }

    private void TickClock()
    {
        if (glass != null)
        {
            float t = Time.unscaledTime;
            float light = 1f - GlassFlicker * (.5f + .35f * Mathf.Sin(t * 1.3f) + .15f * Mathf.Sin(t * 7.1f));
            glass.color = new Color(light, light, light, 1f);
        }
        DateTime now = DateTime.Now;
        if (now.Second == lastSecond) return;
        lastSecond = now.Second;
        Array.Clear(handsPixels, 0, handsPixels.Length);
        Hand(now.Second / 60f, 21f, SecondHandColor, false);
        Hand((now.Minute + now.Second / 60f) / 60f, 19f, HandColor, false);
        Hand((now.Hour % 12 + now.Minute / 60f) / 12f, 12f, HandColor, true);
        int centre = ClockSize / 2;
        for (int y = -1; y <= 1; y++)
            for (int x = -1; x <= 1; x++) handsPixels[(centre + y) * ClockSize + centre + x] = HandColor;
        handsTexture.SetPixels32(handsPixels);
        handsTexture.Apply(false);
    }

    // The clock is seen from behind, so its hands run counter-clockwise.
    private void Hand(float turn, float length, Color32 color, bool thick)
    {
        float angle = -turn * Mathf.PI * 2f;
        int centre = ClockSize / 2;
        int x1 = centre + Mathf.RoundToInt(Mathf.Sin(angle) * length), y1 = centre + Mathf.RoundToInt(Mathf.Cos(angle) * length);
        int x = centre, y = centre;
        int dx = Mathf.Abs(x1 - x), dy = -Mathf.Abs(y1 - y), sx = x < x1 ? 1 : -1, sy = y < y1 ? 1 : -1, error = dx + dy;
        while (true)
        {
            Plot(x, y, color);
            if (thick) { Plot(x + 1, y, color); Plot(x, y - 1, color); Plot(x + 1, y - 1, color); }
            if (x == x1 && y == y1) break;
            int doubled = 2 * error;
            if (doubled >= dy) { error += dy; x += sx; }
            if (doubled <= dx) { error += dx; y += sy; }
        }
    }

    private void Plot(int x, int y, Color32 color)
    {
        if (x >= 0 && y >= 0 && x < ClockSize && y < ClockSize) handsPixels[y * ClockSize + x] = color;
    }

    // Dust hangs in the light that falls from the clock toward the lower left.
    private void Respawn(Speck speck)
    {
        float angle = UnityEngine.Random.Range(9f, 39f) * Mathf.Deg2Rad, reach = UnityEngine.Random.Range(34f, 150f);
        speck.position = ClockCentre + new Vector2(-Mathf.Cos(angle), Mathf.Sin(angle)) * reach;
        speck.velocity = new Vector2(UnityEngine.Random.Range(-1.6f, -.4f), UnityEngine.Random.Range(.2f, 1.1f));
        speck.life = UnityEngine.Random.Range(4f, 8f);
        speck.phase = UnityEngine.Random.Range(0f, 6.28f);
        speck.age = 0f;
    }

    private void DriftDust(float delta)
    {
        foreach (var speck in dust)
        {
            speck.age += delta;
            if (speck.age >= speck.life) Respawn(speck);
            speck.position += speck.velocity * delta;
            float wobble = Mathf.Sin(speck.age * .9f + speck.phase) * 1.5f;
            Put(speck, speck.position + new Vector2(wobble, 0f), 1f);
            float fade = Mathf.Sin(Mathf.Clamp01(speck.age / speck.life) * Mathf.PI); // in and out
            speck.image.color = new Color(DustColor.r, DustColor.g, DustColor.b, .42f * fade);
        }
    }

    private void VentSteam(float delta)
    {
        float now = Time.unscaledTime;
        if (steamBurstLeft <= 0f && now >= nextSteam)
        {
            steamBurstLeft = .55f;
            nextPuff = 0f;
            nextSteam = now + UnityEngine.Random.Range(3.5f, 6.5f);
        }
        if (steamBurstLeft > 0f)
        {
            steamBurstLeft -= delta;
            nextPuff -= delta;
            if (nextPuff <= 0f)
            {
                nextPuff = .13f;
                foreach (var puff in steam)
                {
                    if (puff.age < puff.life) continue;
                    puff.position = SteamVent + new Vector2(UnityEngine.Random.Range(-.6f, .6f), 0f);
                    puff.velocity = new Vector2(UnityEngine.Random.Range(-2.2f, -.6f), UnityEngine.Random.Range(-9.5f, -6.5f));
                    puff.life = UnityEngine.Random.Range(1.3f, 1.9f);
                    puff.size = UnityEngine.Random.Range(1.6f, 2.4f);
                    puff.age = 0f;
                    break;
                }
            }
        }
        foreach (var puff in steam)
        {
            if (puff.age >= puff.life) { puff.image.color = Color.clear; continue; }
            puff.age += delta;
            float t = Mathf.Clamp01(puff.age / puff.life);
            puff.position += puff.velocity * delta * (1f - .55f * t); // slows as it rises
            Put(puff, puff.position, Mathf.Round(puff.size + 2.2f * t));
            puff.image.color = new Color(SteamColor.r, SteamColor.g, SteamColor.b, .3f * (1f - t) * (1f - t));
        }
    }

    // Art pixels from the top-left corner, snapped to the pixel grid.
    private static void Put(Speck speck, Vector2 position, float size)
    {
        speck.rect.anchoredPosition = new Vector2(Mathf.Round(position.x), -Mathf.Round(position.y));
        speck.rect.sizeDelta = new Vector2(size, size);
    }

    // The nearer layers shift a little against the pointer.
    private void Parallax(float delta)
    {
        Vector2 target = Vector2.zero;
        var mouse = Mouse.current;
        if (mouse != null && Screen.width > 0 && Screen.height > 0)
        {
            Vector2 pointer = mouse.position.ReadValue();
            target = new Vector2(Mathf.Clamp(pointer.x / Screen.width * 2f - 1f, -1f, 1f),
                Mathf.Clamp(pointer.y / Screen.height * 2f - 1f, -1f, 1f));
        }
        parallax = Vector2.Lerp(parallax, target, 1f - Mathf.Exp(-4f * delta));
        middle.anchoredPosition = new Vector2(-parallax.x * 1.2f, -parallax.y * .5f);
        front.anchoredPosition = new Vector2(-parallax.x * 3f, -parallax.y * .9f);
    }
}
