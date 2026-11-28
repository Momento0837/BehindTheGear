using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace BehindTheGear.HideOut
{
    // One presentation per base scene instance. Never changes authored pivots, import settings or unlocks.
    public sealed class BaseEntrancePresentation : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            public bool enabled = true;
            [Range(.1f, .95f)] public float startingScale = .65f;
            [Min(.1f)] public float iconDuration = .38f;
            [Min(0)] public float iconStagger = .36f;
            [Min(.1f)] public float sweepDuration = .65f;
            [Min(0)] public float sweepInterval = .16f;
            [Range(0, 1)] public float iconStartAtRadialExpansion = .5f;
            public Color sweepColor = new Color32(31, 31, 31, 255);
            [Range(0, 1)] public float sweepOpacity = .65f;
            [Range(0, 1)] public float fogStartAtIconExpansion = .5f;
            [Tooltip("Total duration including the stagger between fog layers.")]
            [Min(.1f)] public float fogSequenceDuration = 4f;
            [Min(0)] public float fogLayerStagger = 1.2f;
            [Min(.1f)] public float mapDuration = .6f;
            [Min(0)] public float mapStagger = .22f;
            [Range(0, 1)] public float mapStartAtFogProgress = .65f;
        }

        private sealed class Visual
        {
            public Transform target;
            public Vector3 scale, position, pivotOffset;
            public SpriteRenderer[] sprites;
            public Color[] colors;
            public CanvasGroup group;
            public bool addedGroup;
            public float alpha, distance, delay;
            public Facility? facility;

            public void Apply(float progress, float initialScale)
            {
                if (target == null) return;
                float t = Mathf.Clamp01(progress);
                float eased = ZoomProgress(t);
                float factor = Mathf.Lerp(initialScale, 1, eased);
                target.localScale = scale * factor;
                // The exported sprites retain an 800x450 transparent canvas. Grow around visible artwork,
                // keeping layered shadows, colors and outlines of each object together.
                if (group == null) target.localPosition = position + pivotOffset * (1 - factor);
                float opacity = Mathf.SmoothStep(0, 1, t);
                if (group != null) group.alpha = alpha * opacity;
                for (int i = 0; i < sprites.Length; i++)
                    if (sprites[i] != null) { Color c = colors[i]; c.a *= opacity; sprites[i].color = c; }
            }

            public void Restore()
            {
                if (target != null)
                {
                    target.localScale = scale;
                    if (group == null) target.localPosition = position;
                }
                for (int i = 0; i < sprites.Length; i++) if (sprites[i] != null) sprites[i].color = colors[i];
                if (group != null)
                {
                    group.alpha = alpha;
                    if (addedGroup) Destroy(group);
                }
            }
        }

        private readonly List<Visual> icons = new(), clouds = new(), map = new();
        private readonly List<SpriteRenderer> sweeps = new();
        private readonly List<MaterialPropertyBlock> sweepProperties = new();
        private HideOutSpriteEnvironment environment;
        private HideOutLockedFog lockedFog;
        private HideOutCameraController controller;
        private Camera viewCamera;
        private GraphicRaycaster[] raycasters;
        private bool started, previousCameraLock;
        private Material sweepMaterial;
        private Action completed;
        private Settings config;
        private int firstDirection;
        public bool IsPlaying { get; private set; }
        public string Stage { get; private set; } = "Idle";

        public void Begin(HideOutSpriteEnvironment owner, Camera camera, HideOutCameraController cameraController,
            IEnumerable<KeyValuePair<Facility, RectTransform>> facilityIcons, Transform inputRoot, Action onCompleted)
        {
            if (started || owner == null || camera == null) return;
            started = true; config = owner.entrance ?? new Settings();
            if (!config.enabled) return;
            environment = owner; viewCamera = camera; controller = cameraController; completed = onCompleted;
            lockedFog = owner.GetComponent<HideOutLockedFog>();
            lockedFog?.SetEntranceSuspended(true);
            foreach (var pair in facilityIcons)
            {
                if (pair.Value == null) continue;
                var icon = Capture(pair.Value, true);
                icon.facility = pair.Key;
                icons.Add(icon);
            }
            foreach (Transform child in owner.transform)
            {
                if (child.name.EndsWith(" Locked Fog", StringComparison.Ordinal))
                {
                    foreach (Transform cloud in child)
                        if (cloud.gameObject.activeInHierarchy && cloud.GetComponent<SpriteRenderer>() != null) clouds.Add(Capture(cloud, false));
                }
                else if (child.name.EndsWith("_Objects", StringComparison.Ordinal))
                {
                    foreach (Transform prop in child) CaptureMap(prop);
                }
                else if (child.name == "Floor_Group" || child.GetComponent<SpriteRenderer>() != null) CaptureMap(child);
            }
            Sort(icons, config.iconStagger);
            Sort(clouds, Mathf.Min(config.fogLayerStagger, Mathf.Max(0, config.fogSequenceDuration - .1f)));
            Sort(map, config.mapStagger);
            // UI remains in its overlay Canvas, above temporary/locked fog, above the authored sprite order.
            CreateSweeps();
            previousCameraLock = controller != null && controller.PresentationLocked;
            if (controller != null) controller.PresentationLocked = true;
            raycasters = inputRoot.GetComponentsInChildren<GraphicRaycaster>(true).Where(r => r.enabled).ToArray();
            foreach (var raycaster in raycasters) raycaster.enabled = false;
            IsPlaying = true; Stage = "WaitingForFade";
            Apply(icons, -1, config.iconDuration); Apply(clouds, -1, config.fogSequenceDuration); Apply(map, -1, config.mapDuration);
            StartCoroutine(Play());
        }

        private void CaptureMap(Transform target)
        {
            if (target.gameObject.activeInHierarchy && target.GetComponentsInChildren<SpriteRenderer>().Any(r => r.enabled && r.sprite != null))
                map.Add(Capture(target, false));
        }

        private Visual Capture(Transform target, bool icon)
        {
            var sprites = icon ? Array.Empty<SpriteRenderer>() : target.GetComponentsInChildren<SpriteRenderer>(true);
            var result = new Visual { target = target, scale = target.localScale, position = target.localPosition,
                sprites = sprites, colors = sprites.Select(r => r.color).ToArray() };
            Vector3 center = target.position;
            if (icon)
            {
                result.group = target.GetComponent<CanvasGroup>();
                result.addedGroup = result.group == null;
                if (result.addedGroup) result.group = target.gameObject.AddComponent<CanvasGroup>();
                result.alpha = result.group.alpha;
                // Icons arrive from left to right; world objects still reveal outward from screen center.
                result.distance = RectTransformUtility.WorldToScreenPoint(null, target.position).x;
            }
            else
            {
                bool found = false; Bounds bounds = default;
                var points = new List<Vector2>();
                foreach (var sprite in sprites)
                {
                    if (!sprite.enabled || !sprite.gameObject.activeInHierarchy || sprite.sprite == null) continue;
                    int shapes = sprite.sprite.GetPhysicsShapeCount();
                    for (int i = 0; i < Mathf.Max(1, shapes); i++)
                    {
                        points.Clear();
                        if (shapes == 0) points.AddRange(sprite.sprite.vertices); else sprite.sprite.GetPhysicsShape(i, points);
                        foreach (var point in points)
                        {
                            Vector3 world = sprite.transform.TransformPoint(new Vector3(sprite.flipX ? -point.x : point.x, sprite.flipY ? -point.y : point.y, 0));
                            if (!found) { bounds = new Bounds(world, Vector3.zero); found = true; } else bounds.Encapsulate(world);
                        }
                    }
                }
                if (found) center = bounds.center;
                Vector3 localPivot = target.InverseTransformPoint(center);
                result.pivotOffset = target.localRotation * Vector3.Scale(target.localScale, localPivot);
                result.distance = Vector2.Distance(viewCamera.WorldToScreenPoint(center),
                    new Vector2(viewCamera.pixelWidth, viewCamera.pixelHeight) * .5f);
            }
            return result;
        }

        private static void Sort(List<Visual> visuals, float stagger)
        {
            visuals.Sort((a, b) => a.distance.CompareTo(b.distance));
            for (int i = 0; i < visuals.Count; i++) visuals[i].delay = stagger * i / Mathf.Max(1, visuals.Count - 1);
        }
        private void Apply(List<Visual> visuals, float elapsed, float duration)
        {
            foreach (var item in visuals)
            {
                float progress = (elapsed - item.delay) / Mathf.Max(.1f, duration);
                item.Apply(progress, config.startingScale);
                // Share the icon's stagger and fade curve with its named area's light.
                if (item.facility.HasValue)
                    environment.SetEntranceLighting(item.facility.Value, Mathf.SmoothStep(0, 1, progress));
            }
        }
        private static float ZoomProgress(float progress) => 1 - Mathf.Pow(1 - Mathf.Clamp01(progress), 3);

        private static float TimeAtMeanExpansion(List<Visual> visuals, float duration, float expansion)
        {
            if (visuals.Count == 0) return 0;
            duration = Mathf.Max(.1f, duration);
            float low = 0, high = visuals.Max(v => v.delay) + duration;
            // Use visible enlargement, including each icon's stagger, rather than alpha or elapsed time.
            for (int i = 0; i < 20; i++)
            {
                float middle = (low + high) * .5f;
                float mean = visuals.Average(v => ZoomProgress((middle - v.delay) / duration));
                if (mean < expansion) low = middle; else high = middle;
            }
            return high;
        }
        private void CreateSweeps()
        {
            var sprites = environment.bigFogSprites.Where(s => s != null).ToArray();
            var shader = Resources.Load<Shader>("Effects/BaseEntranceFog");
            if (sprites.Length == 0 || shader == null) return;
            sweepMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            Color tint = config.sweepColor;
            sweepMaterial.SetColor("_Tint", tint);
            var front = environment.GetComponentsInChildren<SpriteRenderer>(true)
                .OrderBy(r => SortingLayer.GetLayerValueFromID(r.sortingLayerID)).ThenBy(r => r.sortingOrder).LastOrDefault();
            firstDirection = UnityEngine.Random.value < .5f ? 1 : -1;
            for (int i = 0; i < 3; i++)
            {
                var instance = new GameObject("Entrance Fog Burst " + (i + 1), typeof(SpriteRenderer));
                instance.transform.SetParent(transform, false);
                var renderer = instance.GetComponent<SpriteRenderer>();
                renderer.sprite = sprites[i % sprites.Length]; renderer.sharedMaterial = sweepMaterial;
                renderer.sortingLayerID = front != null ? front.sortingLayerID : 0;
                renderer.sortingOrder = Mathf.Min(32760, (front != null ? front.sortingOrder : 0) + 20 + i);
                renderer.color = new Color(1, 1, 1, 0);
                sweeps.Add(renderer);
                sweepProperties.Add(new MaterialPropertyBlock());
            }
        }

        private void AnimateSweeps(float elapsed)
        {
            float depth = Vector3.Dot(environment.transform.position - viewCamera.transform.position, viewCamera.transform.forward);
            for (int i = 0; i < sweeps.Count; i++)
            {
                float t = Mathf.Clamp01((elapsed - i * config.sweepInterval) / Mathf.Max(.1f, config.sweepDuration));
                int direction = i % 2 == 0 ? firstDirection : -firstDirection;
                // Perspective-style fly-through: start near the vanishing point, enlarge toward the
                // viewer and leave along alternating radial lanes. Never cross the screen sideways.
                float approach = t * t;
                float x = .5f + direction * Mathf.Lerp(.04f, 1.3f, approach);
                float lane = i == 1 ? .38f : i == 0 ? -.25f : -.5f;
                float y = .5f + lane * approach;
                Vector3 center = viewCamera.ViewportToWorldPoint(new Vector3(x, y, depth));
                var renderer = sweeps[i];
                float width = 2 * viewCamera.orthographicSize * viewCamera.aspect * Mathf.Lerp(.18f, 1.35f, approach);
                float scale = width / Mathf.Max(.01f, renderer.sprite.bounds.size.x);
                renderer.transform.localScale = new Vector3(scale, scale * .68f, 1);
                renderer.transform.position = center - renderer.transform.TransformVector(renderer.sprite.bounds.center);
                float alpha = Mathf.SmoothStep(0, 1, t / .18f) * (1 - Mathf.SmoothStep(0, 1, (t - .65f) / .35f)) * config.sweepOpacity;
                renderer.color = new Color(1, 1, 1, alpha);
                sweepProperties[i].SetFloat("_Opacity", alpha);
                renderer.SetPropertyBlock(sweepProperties[i]);
            }
        }

        private IEnumerator Play()
        {
            while (SceneFadeTransition.IsRunning) yield return null;
            float iconStart = 2 * config.sweepInterval + config.sweepDuration * Mathf.Sqrt(config.iconStartAtRadialExpansion);
            float iconEnd = iconStart + config.iconStagger + config.iconDuration;
            float fogStart = iconStart + TimeAtMeanExpansion(icons, config.iconDuration, config.fogStartAtIconExpansion);
            float fogSequenceDuration = clouds.Count > 0 ? Mathf.Max(.1f, config.fogSequenceDuration) : 0;
            float fogLayerDuration = Mathf.Max(.1f, fogSequenceDuration - (clouds.Count > 0 ? clouds.Max(v => v.delay) : 0));
            float mapStart = fogStart + fogSequenceDuration * config.mapStartAtFogProgress;
            float finish = Mathf.Max(iconEnd, mapStart + config.mapDuration + config.mapStagger,
                fogStart + fogSequenceDuration);
            float elapsed = 0;
            while (elapsed < finish && environment != null && environment.gameObject.activeInHierarchy)
            {
                Stage = elapsed < iconStart ? "Sweeps" : elapsed < fogStart ? "Icons"
                    : elapsed < mapStart ? (elapsed < iconEnd ? "IconsAndFog" : "Fog") : "FogAndMap";
                Apply(icons, elapsed - iconStart, config.iconDuration);
                AnimateSweeps(elapsed);
                Apply(clouds, elapsed - fogStart, fogLayerDuration);
                Apply(map, elapsed - mapStart, config.mapDuration);
                yield return null;
                // First-use shader compilation / scene loading must not skip entire presentation stages.
                elapsed += Mathf.Min(Time.unscaledDeltaTime, 1f / 30f);
            }
            Finish();
        }
        private void Finish()
        {
            if (!IsPlaying) return;
            IsPlaying = false; Stage = "Complete";
            if (environment != null) environment.CompleteEntranceLighting();
            foreach (var visual in icons.Concat(clouds).Concat(map)) visual.Restore();
            icons.Clear(); clouds.Clear(); map.Clear();
            foreach (var sweep in sweeps) if (sweep != null) Destroy(sweep.gameObject);
            sweeps.Clear();
            sweepProperties.Clear();
            if (sweepMaterial != null) Destroy(sweepMaterial);
            lockedFog?.SetEntranceSuspended(false);
            if (controller != null) controller.PresentationLocked = previousCameraLock;
            if (raycasters != null) foreach (var raycaster in raycasters) if (raycaster != null) raycaster.enabled = true;
            var callback = completed; completed = null;
            if (isActiveAndEnabled) callback?.Invoke();
        }
        private void OnDisable() { StopAllCoroutines(); Finish(); }
    }
}
