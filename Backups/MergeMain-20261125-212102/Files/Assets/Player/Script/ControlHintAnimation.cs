using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// GIF frames are composited into generated atlases; timings retain the source GIF delays.
public sealed class ControlHintAnimation
{
    [Serializable] private sealed class Data
    {
        public int width, height, columns;
        public float[] durations;
        public Vector2 pivot = new Vector2(.5f, .5f);
    }
    private static readonly Dictionary<string, ControlHintAnimation> clips = new();
    private static readonly string[] tutorialNames = { "WASDSpace", "MouseL", "F", "W", "Inventory_Tutorial" };
    private static bool IsLoaded(string name) => clips.TryGetValue(name, out var clip)
        && !Array.Exists(clip.frames, frame => frame == null || frame.texture == null);
    public static bool TutorialsLoaded => Array.TrueForAll(tutorialNames, IsLoaded);
    public static IEnumerator PreloadTutorials()
    {
        foreach (var name in tutorialNames)
        {
            if (IsLoaded(name)) continue;
            // Render the opaque loading cover before doing PNG decode/GPU upload.
            yield return null;
            Load(name);
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache() => clips.Clear();
    private readonly Sprite[] frames;
    private readonly float[] ends;
    private readonly float duration;
    private readonly float playbackSpeed;
    private readonly bool inventoryTutorial;
    private float TrimStart => inventoryTutorial ? 0 : Mathf.Min(.5f, Mathf.Max(0f, duration - .01f));
    public Vector2 Size { get; }
    public Vector2 Pivot { get; }
    public float Duration => (duration - TrimStart) / playbackSpeed;
    private ControlHintAnimation(string name)
    {
        inventoryTutorial = name == "Inventory_Tutorial";
        playbackSpeed = inventoryTutorial ? 1f : 1.32f;
        var data = JsonUtility.FromJson<Data>(Resources.Load<TextAsset>("ControlHints/" + name).text.TrimStart('\uFEFF'));
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.name = name + " GIF";
        if (!texture.LoadImage(Resources.Load<TextAsset>("ControlHints/" + name + "_Frames").bytes))
            throw new InvalidOperationException("Cannot decode control hint atlas: " + name);
        texture.wrapMode = TextureWrapMode.Clamp;
        Size = new Vector2(data.width, data.height);
        Pivot = data.pivot;
        frames = new Sprite[data.durations.Length];
        ends = new float[frames.Length];
        Color32[] atlasPixels = inventoryTutorial ? texture.GetPixels32() : null;
        float total = 0;
        for (int i = 0; i < frames.Length; i++)
        {
            var rect = new Rect(i % data.columns * data.width,
                texture.height - (i / data.columns + 1) * data.height, data.width, data.height);
            // UI frames are rectangular; avoid generating an alpha-traced tight mesh
            // for every large GIF frame during the initial load.
            Texture2D frameTexture = texture;
            if (inventoryTutorial)
            {
                // This GIF is displayed much smaller than its source. Isolate each frame
                // so filtered mip levels cannot sample a neighbouring animation frame.
                frameTexture = CreateFilteredFrame(atlasPixels, texture.width, rect, name + " " + i);
                rect = new Rect(0, 0, data.width, data.height);
            }
            frames[i] = Sprite.Create(frameTexture, rect, Pivot, 100, 0, SpriteMeshType.FullRect);
            total += data.durations[i]; ends[i] = total;
        }
        duration = total;
        if (inventoryTutorial) UnityEngine.Object.Destroy(texture);
    }

    private static Texture2D CreateFilteredFrame(Color32[] atlas, int atlasWidth, Rect rect, string name)
    {
        int width = (int)rect.width, height = (int)rect.height;
        var pixels = new Color32[width * height];
        for (int y = 0; y < height; y++)
            Array.Copy(atlas, ((int)rect.y + y) * atlasWidth + (int)rect.x, pixels, y * width, width);
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, true)
        { name = name, wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Point };
        for (int mip = 0; mip < texture.mipmapCount; mip++)
        {
            texture.SetPixels32(pixels, mip);
            if (mip + 1 == texture.mipmapCount) break;
            int nextWidth = Mathf.Max(1, width / 2), nextHeight = Mathf.Max(1, height / 2);
            var next = new Color32[nextWidth * nextHeight];
            for (int y = 0; y < nextHeight; y++)
                for (int x = 0; x < nextWidth; x++)
                {
                    int alpha = 0, count = 0;
                    int left = x * width / nextWidth, right = (x + 1) * width / nextWidth;
                    int bottom = y * height / nextHeight, top = (y + 1) * height / nextHeight;
                    float centerX = (left + right - 1) * .5f, centerY = (bottom + top - 1) * .5f;
                    float nearestDistance = float.PositiveInfinity;
                    Color32 nearest = default;
                    for (int sy = bottom; sy < top; sy++)
                        for (int sx = left; sx < right; sx++)
                        {
                            Color32 p = pixels[sy * width + sx];
                            alpha += p.a; count++;
                            float distance = (sx - centerX) * (sx - centerX) + (sy - centerY) * (sy - centerY);
                            if (p.a > 0 && distance <= nearestDistance)
                            { nearest = p; nearestDistance = distance; }
                        }
                    // Only average coverage. Keep a nearest visible source colour so the
                    // GIF's pixel-art palette and hard colour boundaries stay crisp.
                    if (alpha > 0) next[y * nextWidth + x] = new Color32(
                        nearest.r, nearest.g, nearest.b,
                        (byte)((alpha + count / 2) / count));
                }
            pixels = next; width = nextWidth; height = nextHeight;
        }
        texture.Apply(false, true);
        return texture;
    }

    public static ControlHintAnimation Load(string name)
    {
        if (!clips.TryGetValue(name, out var clip) || Array.Exists(clip.frames, frame => frame == null || frame.texture == null))
            clips[name] = clip = new ControlHintAnimation(name);
        return clip;
    }
    public Sprite Frame(float time)
    {
        // Skip the original first half-second on every loop, not just on first appearance.
        float phase = TrimStart + Mathf.Repeat(time * playbackSpeed, duration - TrimStart);
        for (int i = 0; i < ends.Length; i++) if (phase < ends[i]) return frames[i];
        return frames[0];
    }
}
