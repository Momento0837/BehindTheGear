using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BehindTheGear.HideOut
{
    [Serializable]
    public sealed class TrainingNumberStyle
    {
        public Color color = new(1f, .96f, .84f);
        [Min(8)] public float fontSize = 32;
        [Min(.1f)] public float scale = 1;
        [Min(1)] public float popScale = 1.22f;
        [Min(.1f)] public float duration = .78f;
        [Min(0)] public float rise = 70;
    }

    public sealed class ScareCrowHitFeedback : MonoBehaviour
    {
        public HideOutDummy dummy;
        public Transform visualRoot;
        public Transform numberAnchor;
        public Transform sparkRoot;
        public Canvas numberCanvas;
        public Camera worldCamera;
        public ScareCrowCameraImpact cameraImpact;
        public TMP_FontAsset font;
        [Header("Final damage presentation only; does not change combat")]
        [Min(1)] public int strongDamageThreshold = 20;
        public TrainingNumberStyle normal = new();
        public TrainingNumberStyle critical = new() { color = new Color(1f, .68f, .18f), fontSize = 43, popScale = 1.55f, duration = .95f, rise = 90 };
        public TrainingNumberStyle strong = new() { color = new Color(1f, .82f, .48f), fontSize = 39, popScale = 1.42f, duration = .88f, rise = 82 };
        [Min(0)] public float horizontalSpread = .34f;
        [Range(0, 1)] public float shadowOpacity = .9f;
        [Header("Visual Root response")]
        [Min(.01f)] public float reactionDuration = .22f;
        [Range(0, 15)] public float swayDegrees = 4;
        [Range(0, 1)] public float flashStrength = .7f;
        [Min(.01f)] public float sparkDuration = .13f;
        public Color sparkColor = new(1, .8f, .4f);
        public AudioSource hitAudio;
        public AudioClip hitClip;
        [Range(0, 1)] public float hitVolume = .25f;
        private sealed class Popup
        {
            public TMP_Text text;
            public float age, size, pop, duration, rise;
            public Vector3 world;
            public Color color;
        }
        private readonly List<Popup> pool = new();
        private SpriteRenderer[] bodySprites, sparkSprites;
        private Color[] bodyColors;
        private Vector3 restPosition;
        private Quaternion restRotation;
        private Vector3 sparkScale;
        private float reactionAge = 100;
        private int sequence;

        private void Awake()
        {
            restPosition = visualRoot.localPosition; restRotation = visualRoot.localRotation;
            sparkScale = sparkRoot.localScale;
            bodySprites = visualRoot.GetComponentsInChildren<SpriteRenderer>();
            bodyColors = new Color[bodySprites.Length];
            for (int i = 0; i < bodySprites.Length; i++) bodyColors[i] = bodySprites[i].color;
            sparkSprites = sparkRoot.GetComponentsInChildren<SpriteRenderer>();
            sparkRoot.gameObject.SetActive(false);
            for (int i = 0; i < 12; i++) CreatePopup();
        }
        private void OnEnable() => dummy.Hit += OnHit;
        private void OnDisable()
        {
            dummy.Hit -= OnHit;
            if (visualRoot != null) { visualRoot.localPosition = restPosition; visualRoot.localRotation = restRotation; }
            if (bodySprites != null)
                for (int i = 0; i < bodySprites.Length; i++) if (bodySprites[i] != null) bodySprites[i].color = bodyColors[i];
            if (sparkRoot != null) sparkRoot.gameObject.SetActive(false);
            foreach (var popup in pool) if (popup.text != null) popup.text.gameObject.SetActive(false);
        }

        private Popup CreatePopup()
        {
            var go = new GameObject("Pooled Damage Number", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
            go.transform.SetParent(numberCanvas.transform, false);
            var text = go.GetComponent<TextMeshProUGUI>();
            text.raycastTarget = false;
            UIFontTheme.Apply(text, font != null ? font : UIFontTheme.Bold);
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.rectTransform.sizeDelta = new Vector2(320, 110);
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(.025f, .018f, .012f, shadowOpacity);
            shadow.effectDistance = new Vector2(2, -2);
            go.SetActive(false);
            var popup = new Popup { text = text }; pool.Add(popup); return popup;
        }

        private void OnHit()
        {
            var hit = dummy.LastHit;
            bool powerful = hit.Damage >= strongDamageThreshold;
            var style = hit.IsCritical ? critical : powerful ? strong : normal;
            Popup popup = pool.Find(p => !p.text.gameObject.activeSelf) ?? CreatePopup();
            popup.age = 0; popup.color = style.color;
            popup.duration = Mathf.Max(.1f, style.duration); popup.rise = style.rise;
            popup.pop = Mathf.Max(1, style.popScale);
            popup.size = style.scale * (hit.IsCritical && powerful ? 1.08f : 1f);
            // Deterministic small lanes keep successive numbers near the target.
            int lane = sequence++ % 5;
            popup.world = numberAnchor.position + new Vector3((lane - 2) * horizontalSpread * .5f, (lane % 2) * .12f, 0);
            popup.text.text = hit.IsCritical ? $"<size=45%>CRITICAL</size>\n{hit.Damage}" : hit.Damage.ToString();
            popup.text.fontSize = style.fontSize;
            popup.text.gameObject.SetActive(true);
            AnimateNumber(popup);
            reactionAge = 0;
            sparkRoot.gameObject.SetActive(true);
            if (powerful || hit.IsCritical) cameraImpact?.Play();
            if (hitClip != null && hitAudio != null) hitAudio.PlayOneShot(hitClip, hitVolume);
        }

        private void LateUpdate()
        {
            foreach (var popup in pool)
            {
                if (!popup.text.gameObject.activeSelf) continue;
                popup.age += Time.unscaledDeltaTime;
                if (popup.age >= popup.duration) { popup.text.gameObject.SetActive(false); continue; }
                AnimateNumber(popup);
            }
            reactionAge += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(reactionAge / Mathf.Max(.01f, reactionDuration));
            visualRoot.localPosition = restPosition;
            visualRoot.localRotation = restRotation * Quaternion.Euler(0, 0, Mathf.Sin(t * Mathf.PI * 3) * (1 - t) * swayDegrees);
            for (int i = 0; i < bodySprites.Length; i++)
                bodySprites[i].color = Color.Lerp(bodyColors[i], Color.white, flashStrength * (1 - Mathf.Clamp01(t * 2)));
            if (sparkRoot.gameObject.activeSelf)
            {
                float s = Mathf.Clamp01(reactionAge / Mathf.Max(.01f, sparkDuration));
                sparkRoot.localScale = sparkScale * Mathf.Lerp(.4f, 1.2f, s);
                foreach (var sprite in sparkSprites) sprite.color = new Color(sparkColor.r, sparkColor.g, sparkColor.b, 1 - s);
                if (s >= 1) sparkRoot.gameObject.SetActive(false);
            }
        }

        private void AnimateNumber(Popup popup)
        {
            float t = Mathf.Clamp01(popup.age / popup.duration);
            Vector2 screen = worldCamera.WorldToScreenPoint(popup.world);
            RectTransformUtility.ScreenPointToLocalPointInRectangle((RectTransform)numberCanvas.transform, screen, null, out var local);
            popup.text.rectTransform.anchoredPosition = local + Vector2.up * (popup.rise * (1 - Mathf.Pow(1 - t, 2)));
            float pop = t < .12f ? Mathf.Lerp(.72f, popup.pop, t / .12f)
                : Mathf.Lerp(popup.pop, 1, Mathf.Clamp01((t - .12f) / .2f));
            popup.text.transform.localScale = Vector3.one * (pop * popup.size);
            popup.text.color = new Color(popup.color.r, popup.color.g, popup.color.b, 1 - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.5f, 1, t)));
        }
    }
}
