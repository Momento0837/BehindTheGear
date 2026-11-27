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
            [Min(0)] public float iconStagger = .18f;
            [Min(.1f)] public float sweepDuration = .65f;
            [Min(0)] public float sweepInterval = .16f;
            public Color sweepColor = new Color32(31, 31, 31, 255);
            [Range(0, 1)] public float sweepOpacity = .65f;
            [Min(.1f)] public float fogDuration = .45f;
            [Min(0)] public float fogStagger = .18f;
            [Min(.1f)] public float mapDuration = .6f;
            [Min(0)] public float mapStagger = .22f;
            [Min(0)] public float mapDelayAfterFog = .18f;
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

            public void Apply(float progress, float initialScale)
            {
                if (target == null) return;
                float t = Mathf.Clamp01(progress);
                float eased = 1 - Mathf.Pow(1 - t, 3);
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
            IEnumerable<RectTransform> facilityIcons, Transform inputRoot, Action onCompleted)
        {
            if (started || owner == null || camera == null) return;
            started = true; config = owner.entrance ?? new Settings();
            if (!config.enabled) return;
            environment = owner; viewCamera = camera; controller = cameraController; completed = onCompleted;
            lockedFog = owner.GetComponent<HideOutLockedFog>();
            lockedFog?.SetEntranceSuspended(true);
            foreach (var rect in facilityIcons) if (rect != null) icons.Add(Capture(rect, true));
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
            Sort(icons, config.iconStagger); Sort(clouds, config.fogStagger); Sort(map, config.mapStagger);
            // UI remains in its overlay Canvas, above temporary/locked fog, above the authored sprite order.
            CreateSweeps();
            previousCameraLock = controller != null && controller.PresentationLocked;
            if (controller != null) controller.PresentationLocked = true;
            raycasters = inputRoot.GetComponentsInChildren<GraphicRaycaster>(true).Where(r => r.enabled).ToArray();
            foreach (var raycaster in raycasters) raycaster.enabled = false;
            IsPlaying = true; Stage = "WaitingForFade";
            Apply(icons, -1, config.iconDuration); Apply(clouds, -1, config.fogDuration); Apply(map, -1, config.mapDuration);
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
                result.distance = Vector2.Distance(RectTransformUtility.WorldToScreenPoint(null, target.position),
                    new Vector2(viewCamera.pixelWidth, viewCamera.pixelHeight) * .5f);
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
            foreach (var item in visuals) item.Apply((elapsed - item.delay) / Mathf.Max(.1f, duration), config.startingScale);
        }
        private void CreateSweeps()
        {
            var sprites = environment.bigFogSprites.Where(s => s != null).ToArray();
            var shader = Resources.Load<Shader>("Effects/BaseEntranceFog");
            if (sprites.Length == 0 || shader == null) return;
            sweepMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            Color tint = config.sweepColor;
            sweepMaterial.SetVector("_Tint", new Vector4(tint.r, tint.g, tint.b, tint.a));
            var front = environment.GetComponentsInChildren<SpriteRenderer>(true)
                .OrderBy(r => SortingLayer.GetLayerValueFromID(r.sortingLayerID)).ThenBy(r => r.sortingOrder).LastOrDefault();
            firstDirection = UnityEngine.Random.value < .5f ? 1 : -1;
            for (int i = 0; i < 3; i++)
            {
                var instance = new GameObject("Entrance Fog Sweep " + (i + 1), typeof(SpriteRenderer));
                instance.transform.SetParent(transform, false);
                var renderer = instance.GetComponent<SpriteRenderer>();
                renderer.sprite = sprites[i % sprites.Length]; renderer.sharedMaterial = sweepMaterial;
                renderer.sortingLayerID = front != null ? front.sortingLayerID : 0;
                renderer.sortingOrder = Mathf.Min(32760, (front != null ? front.sortingOrder : 0) + 20 + i);
                renderer.color = new Color(1, 1, 1, 0);
                float width = 2 * viewCamera.orthographicSize * viewCamera.aspect * .72f;
                float scale = width / Mathf.Max(.01f, renderer.sprite.bounds.size.x);
                instance.transform.localScale = new Vector3(scale, scale * .55f, 1);
                sweeps.Add(renderer);
            }
        }

        private void AnimateSweeps(float elapsed)
        {
            float depth = Vector3.Dot(environment.transform.position - viewCamera.transform.position, viewCamera.transform.forward);
            for (int i = 0; i < sweeps.Count; i++)
            {
                float t = Mathf.Clamp01((elapsed - i * config.sweepInterval) / Mathf.Max(.1f, config.sweepDuration));
                int direction = i % 2 == 0 ? firstDirection : -firstDirection;
                float x = Mathf.Lerp(-.4f, 1.4f, direction > 0 ? t : 1 - t);
                float y = .5f + (i - 1) * .13f;
                Vector3 center = viewCamera.ViewportToWorldPoint(new Vector3(x, y, depth));
                var renderer = sweeps[i];
                renderer.transform.position = center - renderer.transform.TransformVector(renderer.sprite.bounds.center);
                renderer.color = new Color(1, 1, 1, Mathf.Sin(t * Mathf.PI) * config.sweepOpacity);
            }
        }

        private IEnumerator Play()
        {
            while (SceneFadeTransition.IsRunning) yield return null;
            float sweepStart = config.iconDuration + config.iconStagger;
            float fogStart = sweepStart + config.sweepDuration + 2 * config.sweepInterval;
            float mapStart = fogStart + config.mapDelayAfterFog;
            float finish = Mathf.Max(mapStart + config.mapDuration + config.mapStagger,
                fogStart + config.fogDuration + config.fogStagger);
            float elapsed = 0;
            while (elapsed < finish && environment != null && environment.gameObject.activeInHierarchy)
            {
                Stage = elapsed < sweepStart ? "Icons" : elapsed < fogStart ? "Sweeps" : elapsed < mapStart ? "Fog" : "Map";
                Apply(icons, elapsed, config.iconDuration);
                AnimateSweeps(elapsed - sweepStart);
                Apply(clouds, elapsed - fogStart, config.fogDuration);
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
            foreach (var visual in icons.Concat(clouds).Concat(map)) visual.Restore();
            icons.Clear(); clouds.Clear(); map.Clear();
            foreach (var sweep in sweeps) if (sweep != null) Destroy(sweep.gameObject);
            sweeps.Clear();
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
