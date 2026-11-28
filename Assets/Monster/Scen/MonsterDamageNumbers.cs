using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Damage numbers above monsters. MonsterFollowController.TakeDamage calls Show for every hit (melee, grenade,
/// shockwave, poison dagger impact and poison ticks). Numbers start at the monster's horizontal centre above its head,
/// flash white on impact, pop, then rise and fade. Colour/size come from DamageNumberStyle (heavier = redder, bigger).
/// One pooled instance per scene; presentation only.
/// </summary>
public sealed class MonsterDamageNumbers : MonoBehaviour
{
    private const int InitialPool = 16;
    private const int TopSortingOrder = 600; // above monsters, hit flashes and HP bars

    private sealed class Popup
    {
        public TextMeshPro text;
        public TextMeshPro shadow;
        public Color color;
        public Vector3 origin;
        public float age, duration, rise, scale, pop, shake, flash;
    }

    private static MonsterDamageNumbers instance;
    private readonly List<Popup> pool = new();
    private DamageNumberStyle style;
    private float unitScale = -1f;
    private int sequence;

    public static void Show(MonsterFollowController monster, int damage, bool damageOverTime)
    {
        if (monster == null || damage <= 0) return;
        EnsureInstance();
        instance.Spawn(monster, damage, damageOverTime);
    }

    /// <summary>Small floating label (e.g. "+2" when a coin is picked up), same pop/rise/fade as the damage numbers.</summary>
    public static void ShowLabel(Vector3 position, string label, Color color, float scale = .9f)
    {
        if (string.IsNullOrEmpty(label)) return;
        EnsureInstance();
        instance.SpawnLabel(position, label, color, scale);
    }

    private static void EnsureInstance()
    {
        if (instance != null) return;
        var host = new GameObject("Monster Damage Numbers");
        instance = host.AddComponent<MonsterDamageNumbers>();
    }

    private void SpawnLabel(Vector3 position, string label, Color color, float scale)
    {
        Popup popup = pool.Find(p => !p.text.gameObject.activeSelf) ?? CreatePopup();
        popup.origin = position;
        popup.age = 0f;
        popup.color = color;
        popup.scale = scale;
        popup.rise = style.dotRise + .15f;
        popup.duration = .8f;
        popup.pop = 1.15f;
        popup.shake = 0f;
        popup.flash = 0f;
        UnitScale(popup.text);
        int order = TopSortingOrder + (sequence++ % 50) * 2;
        Setup(popup.shadow, label, 0, order);
        Setup(popup.text, label, 0, order + 1);
        Animate(popup);
    }

    private void Awake()
    {
        style = Resources.Load<DamageNumberStyle>(DamageNumberStyle.ResourcePath);
        if (style == null) style = ScriptableObject.CreateInstance<DamageNumberStyle>(); // built-in defaults
        for (int i = 0; i < InitialPool; i++) CreatePopup();
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private Popup CreatePopup()
    {
        var popup = new Popup
        {
            shadow = CreateText("Damage Number Shadow"),
            text = CreateText("Damage Number"),
        };
        popup.text.gameObject.SetActive(false);
        popup.shadow.gameObject.SetActive(false);
        pool.Add(popup);
        return popup;
    }

    private TextMeshPro CreateText(string objectName)
    {
        var go = new GameObject(objectName);
        go.transform.SetParent(transform, false);
        var text = go.AddComponent<TextMeshPro>();
        UIFontTheme.Apply(text, UIFontTheme.ExtraBold);
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.fontSize = 10f;
        text.rectTransform.sizeDelta = new Vector2(10f, 3f);
        return text;
    }

    // World scale that makes the digits style.digitHeight tall, measured once from the real font metrics.
    // The pooled text is inactive here, so the mesh update must ignore the active state (otherwise bounds are empty).
    private float UnitScale(TextMeshPro sample)
    {
        if (unitScale > 0f) return unitScale;
        sample.text = "0";
        sample.ForceMeshUpdate(true, true);
        float height = sample.textBounds.size.y;
        // Fallback from TMP's 3D metrics: one em is fontSize * 0.1 world units and digits are about 0.7 em tall.
        if (height <= .0001f || float.IsNaN(height)) height = sample.fontSize * .1f * .7f;
        unitScale = style.digitHeight / height;
        return unitScale;
    }

    private void Spawn(MonsterFollowController monster, int damage, bool damageOverTime)
    {
        Popup popup = pool.Find(p => !p.text.gameObject.activeSelf) ?? CreatePopup();
        var body = monster.GetComponent<SpriteRenderer>();
        float top = body != null && body.sprite != null ? body.bounds.max.y : monster.transform.position.y + 1f;
        // Horizontal centre of the monster; quick successive hits stack in three small rows so they stay readable.
        int lane = sequence++ % 3;
        popup.origin = new Vector3(monster.transform.position.x, top + style.startAboveSprite + lane * .16f, 0f);
        popup.age = 0f;

        float strength = style.Strength(damage);
        if (damageOverTime)
        {
            popup.color = style.dotColor;
            popup.scale = style.dotScale;
            popup.rise = style.dotRise;
            popup.duration = style.dotDuration;
            popup.pop = 1.05f;
            popup.shake = 0f;
            popup.flash = 0f;
        }
        else
        {
            style.Evaluate(damage, out popup.color, out popup.scale);
            popup.rise = style.rise;
            popup.duration = style.duration + style.strongExtraDuration * strength;
            popup.pop = Mathf.Lerp(style.popScale.x, style.popScale.y, strength);
            popup.shake = style.strongShake * strength;
            popup.flash = style.impactFlash;
        }

        string label = damage.ToString();
        int sortingLayer = body != null ? body.sortingLayerID : 0;
        // Later (and heavier) numbers draw on top of earlier ones.
        int order = TopSortingOrder + (sequence % 50) * 2 + (strength >= .5f ? 100 : 0);
        UnitScale(popup.text); // measure before the label is written (it uses a temporary "0")
        Setup(popup.shadow, label, sortingLayer, order);
        Setup(popup.text, label, sortingLayer, order + 1);
        Animate(popup);
    }

    private static void Setup(TextMeshPro text, string label, int sortingLayer, int order)
    {
        text.text = label;
        text.sortingLayerID = sortingLayer;
        text.sortingOrder = order;
        text.gameObject.SetActive(true);
    }

    private void LateUpdate()
    {
        foreach (var popup in pool)
        {
            if (!popup.text.gameObject.activeSelf) continue;
            popup.age += Time.unscaledDeltaTime; // keeps moving through hit-stop
            if (popup.age >= popup.duration)
            {
                popup.text.gameObject.SetActive(false);
                popup.shadow.gameObject.SetActive(false);
                continue;
            }
            Animate(popup);
        }
    }

    private void Animate(Popup popup)
    {
        float t = Mathf.Clamp01(popup.age / popup.duration);
        float pop = t < .1f ? Mathf.Lerp(.6f, popup.pop, t / .1f) : Mathf.Lerp(popup.pop, 1f, Mathf.Clamp01((t - .1f) / .2f));
        float size = unitScale * popup.scale * pop;
        float shakeX = popup.shake > 0f && t < .2f ? Mathf.Sin(popup.age * 90f) * popup.shake * (1f - t / .2f) : 0f;
        Vector3 position = popup.origin + new Vector3(shakeX, popup.rise * (1f - (1f - t) * (1f - t)), 0f);

        float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.55f, 1f, t));
        Color color = popup.age < popup.flash ? Color.white : popup.color;
        popup.text.color = new Color(color.r, color.g, color.b, alpha);
        var shadowColor = style.shadowColor;
        popup.shadow.color = new Color(shadowColor.r, shadowColor.g, shadowColor.b, shadowColor.a * alpha);

        popup.text.transform.position = position;
        popup.text.transform.localScale = Vector3.one * size;
        // One sprite pixel down-right, scaled with the number.
        float offset = .09f * popup.scale * pop;
        popup.shadow.transform.position = position + new Vector3(offset, -offset, 0f);
        popup.shadow.transform.localScale = Vector3.one * size;
    }
}
