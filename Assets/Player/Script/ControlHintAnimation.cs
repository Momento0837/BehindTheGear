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
    public Vector2 Size { get; }
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
        if (!clips.TryGetValue(name, out var clip) || clip.frames[0] == null || clip.frames[0].texture == null)
            clips[name] = clip = new ControlHintAnimation(name);
        return clip;
    }
    public Sprite Frame(float time)
    {
        float phase = Mathf.Repeat(time, duration);
        for (int i = 0; i < ends.Length; i++) if (phase < ends[i]) return frames[i];
        return frames[0];
    }
}
