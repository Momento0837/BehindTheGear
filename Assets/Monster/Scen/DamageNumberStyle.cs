using System;
using UnityEngine;

/// <summary>
/// Look of the damage numbers that pop above monsters (Resources/Monsters/DamageNumberStyle).
/// Colour and size follow the damage dealt: small hits stay pale, heavy hits run warm and then red.
/// This is presentation only and never changes combat values.
/// </summary>
[CreateAssetMenu(menuName = "BehindTheGear/Damage Number Style", fileName = "DamageNumberStyle")]
public sealed class DamageNumberStyle : ScriptableObject
{
    public const string ResourcePath = "Monsters/DamageNumberStyle";

    [Serializable]
    public struct Stop
    {
        [Min(0)] public int damage;
        public Color color;
        [Min(.1f)] public float scale;
    }

    [Tooltip("Colour/size stops by damage, ascending. Values between two stops are blended.")]
    public Stop[] stops =
    {
        // Starts at a warm cream and only gradually warms toward a soft, muted red for the heaviest hits.
        new() { damage = 4, color = new Color(1f, .96f, .86f), scale = 1f },
        new() { damage = 10, color = new Color(1f, .88f, .74f), scale = 1.06f },
        new() { damage = 18, color = new Color(1f, .75f, .62f), scale = 1.15f },
        new() { damage = 30, color = new Color(.93f, .53f, .45f), scale = 1.3f },
    };

    [Header("Size and motion (world units, seconds)")]
    [Tooltip("Height of the digits for a scale-1 number.")]
    [Min(.05f)] public float digitHeight = .42f;
    [Tooltip("Start height above the top of the monster sprite (clears the HP bar).")]
    public float startAboveSprite = .38f;
    [Min(0f)] public float rise = .6f;
    [Min(.1f)] public float duration = .75f;
    [Tooltip("Extra lifetime for the heaviest hits.")]
    [Min(0f)] public float strongExtraDuration = .15f;
    [Tooltip("Pop overshoot for light hits / heavy hits.")]
    public Vector2 popScale = new(1.2f, 1.45f);
    [Tooltip("Sideways shake for heavy hits during the first moments.")]
    [Min(0f)] public float strongShake = .05f;
    [Tooltip("Seconds the number flashes white on impact before taking its colour.")]
    [Min(0f)] public float impactFlash = .06f;
    public Color shadowColor = new(.08f, .04f, .03f, .9f);

    [Header("Poison damage over time")]
    public Color dotColor = new(.84f, .93f, .74f);
    [Min(.1f)] public float dotScale = .78f;
    [Min(0f)] public float dotRise = .35f;
    [Min(.1f)] public float dotDuration = .6f;

    /// <summary>0 for the first stop or below, 1 for the last stop or above.</summary>
    public float Strength(int damage)
    {
        if (stops == null || stops.Length < 2) return 0f;
        return Mathf.InverseLerp(stops[0].damage, stops[stops.Length - 1].damage, damage);
    }

    public void Evaluate(int damage, out Color color, out float scale)
    {
        color = Color.white;
        scale = 1f;
        if (stops == null || stops.Length == 0) return;
        if (damage <= stops[0].damage) { color = stops[0].color; scale = stops[0].scale; return; }
        for (int i = 1; i < stops.Length; i++)
        {
            if (damage > stops[i].damage) continue;
            float t = Mathf.InverseLerp(stops[i - 1].damage, stops[i].damage, damage);
            color = Color.Lerp(stops[i - 1].color, stops[i].color, t);
            scale = Mathf.Lerp(stops[i - 1].scale, stops[i].scale, t);
            return;
        }
        color = stops[stops.Length - 1].color;
        scale = stops[stops.Length - 1].scale;
    }
}
