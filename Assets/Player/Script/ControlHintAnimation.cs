using System;
using System.Collections.Generic;
using UnityEngine;

// GIF frames are composited into generated atlases; timings retain the source GIF delays.
public sealed class ControlHintAnimation
{
    [Serializable] private sealed class Data { public int width, height, columns; public float[] durations; }
    private static readonly Dictionary<string, ControlHintAnimation> clips = new();
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache() => clips.Clear();
    private readonly Sprite[] frames;
    private readonly float[] ends;
    private readonly float duration;
    private const float PlaybackSpeed = 1.32f;
    private float TrimStart => Mathf.Min(.5f, Mathf.Max(0f, duration - .01f));
    public Vector2 Size { get; }
    public float Duration => (duration - TrimStart) / PlaybackSpeed;
    private ControlHintAnimation(string name)
    {
        var data = JsonUtility.FromJson<Data>(Resources.Load<TextAsset>("ControlHints/" + name).text.TrimStart('\uFEFF'));
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
        texture.name = name + " GIF";
        if (!texture.LoadImage(Resources.Load<TextAsset>("ControlHints/" + name + "_Frames").bytes))
            throw new InvalidOperationException("Cannot decode control hint atlas: " + name);
        texture.wrapMode = TextureWrapMode.Clamp;
        Size = new Vector2(data.width, data.height);
        frames = new Sprite[data.durations.Length];
        ends = new float[frames.Length];
        float total = 0;
        for (int i = 0; i < frames.Length; i++)
        {
            var rect = new Rect(i % data.columns * data.width,
                texture.height - (i / data.columns + 1) * data.height, data.width, data.height);
            frames[i] = Sprite.Create(texture, rect, new Vector2(.5f, .5f), 100);
            total += data.durations[i]; ends[i] = total;
        }
        duration = total;
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
        float phase = TrimStart + Mathf.Repeat(time * PlaybackSpeed, duration - TrimStart);
        for (int i = 0; i < ends.Length; i++) if (phase < ends[i]) return frames[i];
        return frames[0];
    }
}
