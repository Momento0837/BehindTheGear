using System;
using System.Collections.Generic;
using UnityEngine;

namespace BehindTheGear.HideOut
{
    // World-space sprite clouds. Orthographic cameras need explicit, depth-based parallax.
    [DefaultExecutionOrder(1000)]
    public sealed class HideOutLockedFog : MonoBehaviour
    {
        // Base_Test's map artwork is displayed at 40% of its imported sprite size.
        // Match that shared scale; never resize individual clouds to fit an area's bounds.
        private const float BaseArtworkScale = .4f;
        private const float UnlockDuration = .5f, UnlockPixels = 5f;
        private const float ReplacementInterval = 2.3f, ReplacementFadeDuration = 1f;
        private const float PulseDuration = 1f, SwayPeriod = 5f, SwayPixels = 4f;
        private sealed class Cloud
        {
            public Transform transform;
            public SpriteRenderer renderer;
            public Vector3 restPosition;
            public Vector3 slotPosition;
            public float depth;
            public bool small;
            public float swayStart, swayDirection;
            public Vector3 exitPosition, exitParallax;
            public Vector2 exitDirection;
            public float exitAlpha;
        }
        private sealed class Region
        {
            public GameObject root;
            public Vector3 center;
            public List<Vector2> outline;
            public readonly List<Cloud> clouds = new();
            public bool locked = true;
            public float exitStart, nextReplacement, replacementStart, pulseStart;
            public Cloud incoming, outgoing, pulse;
        }
        private HideOutSpriteEnvironment environment;
        private Camera viewCamera;
        private Vector3 referenceCamera;
        private readonly Dictionary<Facility, Region> regions = new();
        private readonly Dictionary<Transform, Region> regionsByRoot = new();
        private readonly System.Random random = new();
        private int sortingLayer, sortingOrder;
        private bool entranceSuspended;
        private float entrancePauseTime;

        public void SetEntranceSuspended(bool suspended)
        {
            if (entranceSuspended == suspended) return;
            entranceSuspended = suspended;
            if (suspended) { entrancePauseTime = Time.unscaledTime; return; }
            // Resume ambient motion from the same phase after the one-time entrance.
            float pause = Time.unscaledTime - entrancePauseTime;
            foreach (var region in regions.Values)
            {
                region.nextReplacement += pause; region.replacementStart += pause; region.pulseStart += pause;
                if (region.exitStart >= 0) region.exitStart += pause;
                foreach (var cloud in region.clouds) cloud.swayStart += pause;
            }
        }

        public void Initialize(HideOutSpriteEnvironment owner, Camera camera)
        {
            environment = owner;
            viewCamera = camera;
            referenceCamera = owner.OverviewPosition(camera);
            int highestLayer = int.MinValue;
            foreach (var renderer in owner.GetComponentsInChildren<SpriteRenderer>(true))
            {
                int layerValue = SortingLayer.GetLayerValueFromID(renderer.sortingLayerID);
                if (layerValue > highestLayer)
                {
                    highestLayer = layerValue;
                    sortingLayer = renderer.sortingLayerID;
                    sortingOrder = renderer.sortingOrder;
                }
                else if (layerValue == highestLayer) sortingOrder = Mathf.Max(sortingOrder, renderer.sortingOrder);
            }
            sortingOrder += 10;
        }

        public void ApplyLocks(Func<Facility, bool> isLocked)
        {
            foreach (var area in environment.areas)
            {
                if (area?.iconAnchor == null) continue;
                bool locked = isLocked(area.facility);
                if (!regions.TryGetValue(area.facility, out var region) && locked)
                {
                    region = CreateRegion(area);
                    regions.Add(area.facility, region);
                }
                // Refresh runs repeatedly. Only a lock transition may restart an animation.
                if (region == null || region.locked == locked) continue;
                if (locked) RestoreLockedRegion(region);
                else BeginUnlock(region);
            }
        }

        private Region CreateRegion(HideOutSpriteEnvironment.Area area)
        {
            var root = new GameObject(area.facility + " Locked Fog");
            root.transform.SetParent(transform, false);
            Vector3 center = area.iconAnchor.position;
            center.y -= .2f;
            center.z = environment.worldBounds.center.z;
            var outline = AreaOutline(area);
            var region = new Region { root = root, center = center, outline = outline, nextReplacement = Time.unscaledTime + ReplacementInterval };
            if (outline.Count >= 3)
            {
                Vector2 roomCenter = Vector2.zero;
                foreach (var point in outline) roomCenter += point;
                roomCenter /= outline.Count;
                region.center = new Vector3(roomCenter.x, roomCenter.y, center.z);
            }
            regionsByRoot.Add(root.transform, region);
            int bigCount = random.Next(4, 7);
            for (int bigIndex = 0; bigIndex < bigCount; bigIndex++)
            {
                var sprite = environment.bigFogSprites[random.Next(environment.bigFogSprites.Length)];
                if (sprite == null) continue;
                // Use one shared scale in every area, preserving each sprite's authored size.
                float scale = BaseArtworkScale * environment.fogCoverage;
                Vector2 position = InteriorPosition(region, sprite, scale);
                Vector3 cloudCenter = new(position.x, position.y, center.z);
                // One extra LARGE cloud. Keep the original 3-5 groups' satellite count.
                if (bigIndex >= bigCount - 1)
                    CreateCloud(root.transform, sprite, cloudCenter, scale, "BigFog " + (bigIndex + 1));
                else CreateCloudWithSatellites(root.transform, sprite, cloudCenter, scale, "BigFog " + (bigIndex + 1), "Fog " + (bigIndex + 1));
            }
            CreateBorderClouds(root.transform, outline);
            SelectPulse(region, Time.unscaledTime);
            return region;
        }

        private void CreateCloudWithSatellites(Transform parent, Sprite sprite, Vector3 center, float scale, string label, string detailLabel)
        {
            var primary = CreateCloud(parent, sprite, center, scale, label);
            var region = regionsByRoot[parent];
            Vector2 size = (Vector2)sprite.bounds.size * scale;
            int satellites = random.Next(1, 3);
            float pixelSize = scale / Mathf.Max(1, sprite.pixelsPerUnit);
            float inset = environment.fogEdgeInsetPixels * pixelSize;
            float jitter = environment.fogEdgeJitterPixels * pixelSize;
            float edgeX = Mathf.Max(0, size.x * .5f - inset);
            float edgeY = Mathf.Max(0, size.y * .5f - inset);
            for (int i = 0; i < satellites; i++)
            {
                var detail = environment.smallFogSprites[random.Next(environment.smallFogSprites.Length)];
                if (detail == null) continue;
                Vector2 position = center;
                float best = -1;
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    bool side = random.Next(2) == 0;
                    float sign = random.Next(2) == 0 ? -1 : 1;
                    Vector2 offset = side
                        ? new Vector2(sign * edgeX, Range(-edgeY, edgeY))
                        : new Vector2(Range(-edgeX, edgeX), sign * edgeY);
                    offset += new Vector2(Range(-jitter, jitter), Range(-jitter, jitter));
                    Vector2 candidate = (Vector2)center + offset;
                    if (!Inside(region.outline, candidate)) continue;
                    float score = Spacing(region, candidate, detail, scale, primary);
                    if (score <= best) continue;
                    best = score; position = candidate;
                }
                // A detail cloud must stay at the parent's edge; never fall back to its dark core.
                if (best >= 0) CreateCloud(parent, detail, new Vector3(position.x, position.y, center.z), scale, detailLabel + "." + (i + 1));
            }
        }

        private void CreateBorderClouds(Transform parent, List<Vector2> outline)
        {
            if (outline.Count < 3) return;
            Vector2 center = Vector2.zero;
            Vector2 minimum = outline[0], maximum = outline[0];
            float perimeter = 0;
            for (int i = 0; i < outline.Count; i++)
            {
                center += outline[i];
                minimum = Vector2.Min(minimum, outline[i]);
                maximum = Vector2.Max(maximum, outline[i]);
                perimeter += Vector2.Distance(outline[i], outline[(i + 1) % outline.Count]);
            }
            center /= outline.Count;
            var region = regionsByRoot[parent];
            int count = random.Next(3, 6);
            float scale = BaseArtworkScale * environment.fogCoverage;
            float availableSpan = Mathf.Min(maximum.x - minimum.x, maximum.y - minimum.y, perimeter / count);
            // One cloud per perimeter interval, so a random roll cannot bunch them on one side.
            for (int i = 0; i < count; i++)
            {
                var sprite = environment.bigFogSprites[random.Next(environment.bigFogSprites.Length)];
                if (sprite == null) continue;
                Vector2 size = (Vector2)sprite.bounds.size * scale;
                bool useSmallFog = Mathf.Min(size.x, size.y) > availableSpan * 1.15f;
                if (useSmallFog)
                {
                    sprite = environment.smallFogSprites[random.Next(environment.smallFogSprites.Length)];
                    if (sprite == null) continue;
                    size = (Vector2)sprite.bounds.size * scale;
                }
                float distance = perimeter * (i + Range(.35f, .65f)) / count;
                Vector2 edge = outline[0];
                for (int j = 0; j < outline.Count; j++)
                {
                    Vector2 from = outline[j], to = outline[(j + 1) % outline.Count];
                    float length = Vector2.Distance(from, to);
                    if (distance <= length)
                    {
                        edge = Vector2.Lerp(from, to, distance / Mathf.Max(.001f, length));
                        break;
                    }
                    distance -= length;
                }
                // Put most of the cloud inside the room, leaving only its soft edge outside.
                Vector2 position = Vector2.MoveTowards(edge, center, Mathf.Min(size.x, size.y) * .3f);
                Vector3 worldPosition = new(position.x, position.y, environment.worldBounds.center.z);
                if (useSmallFog) CreateCloud(parent, sprite, worldPosition, scale, "Border Cloud " + (i + 1));
                else CreateCloudWithSatellites(parent, sprite, worldPosition, scale,
                    "Border Cloud " + (i + 1), "Border Detail " + (i + 1));
            }
        }

        private List<Vector2> AreaOutline(HideOutSpriteEnvironment.Area area)
        {
            var points = new List<Vector2>();
            // Room_Group contains the authored floor/walls; props and icon anchors do not define its boundary.
            var room = area.root != null ? area.root.Find("Room_Group") : null;
            if (room == null) return points;
            foreach (var renderer in room.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer.sprite == null) continue;
                // Layer exports retain the full transparent canvas. The generated physics outline
                // follows visible pixels even when the sprite's render mesh is a full rectangle.
                var vertices = new List<Vector2>();
                int shapeCount = renderer.sprite.GetPhysicsShapeCount();
                var shape = new List<Vector2>();
                for (int i = 0; i < shapeCount; i++)
                {
                    renderer.sprite.GetPhysicsShape(i, shape);
                    vertices.AddRange(shape);
                }
                if (vertices.Count == 0) vertices.AddRange(renderer.sprite.vertices);
                foreach (var vertex in vertices)
                {
                    Vector3 local = new(renderer.flipX ? -vertex.x : vertex.x, renderer.flipY ? -vertex.y : vertex.y, 0);
                    points.Add(renderer.transform.TransformPoint(local));
                }
            }
            points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            for (int i = points.Count - 1; i > 0; i--)
                if ((points[i] - points[i - 1]).sqrMagnitude < .000001f) points.RemoveAt(i);
            if (points.Count < 3) return points;
            var hull = new List<Vector2>();
            foreach (var point in points)
            {
                while (hull.Count >= 2 && Cross(hull[^1] - hull[^2], point - hull[^1]) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }
            int lowerCount = hull.Count;
            for (int i = points.Count - 2; i >= 0; i--)
            {
                while (hull.Count > lowerCount && Cross(hull[^1] - hull[^2], points[i] - hull[^1]) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(points[i]);
            }
            hull.RemoveAt(hull.Count - 1);
            // Shared rooms have independent facilities. Divide spawn positions between their anchors;
            // the sprites themselves remain unmasked, and each facility hides its own clouds on unlock.
            foreach (var neighbor in environment.areas)
            {
                if (neighbor == null || neighbor == area || neighbor.root != area.root || neighbor.iconAnchor == null) continue;
                Vector2 normal = neighbor.iconAnchor.position - area.iconAnchor.position;
                if (normal.sqrMagnitude < .000001f) continue;
                Vector2 middle = (neighbor.iconAnchor.position + area.iconAnchor.position) * .5f;
                var clipped = new List<Vector2>();
                for (int i = 0; i < hull.Count; i++)
                {
                    Vector2 from = hull[i], to = hull[(i + 1) % hull.Count];
                    float a = Vector2.Dot(from - middle, normal), b = Vector2.Dot(to - middle, normal);
                    if (a <= 0) clipped.Add(from);
                    if ((a <= 0) != (b <= 0)) clipped.Add(Vector2.Lerp(from, to, a / (a - b)));
                }
                hull = clipped;
            }
            return hull;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static bool Inside(List<Vector2> outline, Vector2 point, float inset = 0)
        {
            if (outline == null || outline.Count < 3) return true;
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 edge = outline[(i + 1) % outline.Count] - outline[i];
                if (Cross(edge, point - outline[i]) < inset * edge.magnitude) return false;
            }
            return true;
        }

        // Normalized center distance, so different source image sizes retain the same visual spacing.
        private static float Spacing(Region region, Vector2 point, Sprite sprite, float scale, Cloud ignore = null, bool ignoreSmall = false)
        {
            Vector2 size = (Vector2)sprite.bounds.size * scale;
            float nearest = float.MaxValue;
            foreach (var cloud in region.clouds)
            {
                if (cloud == ignore || ignoreSmall && cloud.small) continue;
                Vector2 otherSize = (Vector2)cloud.renderer.sprite.bounds.size * cloud.transform.localScale.x;
                Vector2 span = (size + otherSize) * .5f;
                Vector2 delta = point - (Vector2)cloud.restPosition;
                float distance = new Vector2(delta.x / Mathf.Max(.01f, span.x), delta.y / Mathf.Max(.01f, span.y)).magnitude;
                nearest = Mathf.Min(nearest, distance);
            }
            return nearest;
        }

        private Vector2 InteriorPosition(Region region, Sprite sprite, float scale)
        {
            Vector2 best = region.center;
            float spacing = Spacing(region, best, sprite, scale, ignoreSmall: true);
            if (region.outline.Count < 3) return best;
            Vector2 min = region.outline[0], max = min;
            foreach (var point in region.outline) { min = Vector2.Min(min, point); max = Vector2.Max(max, point); }
            Vector2 size = (Vector2)sprite.bounds.size * scale;
            float inset = Mathf.Min(size.x, size.y, max.x - min.x, max.y - min.y) * .18f;
            // Keep centers inside the visible room, while allowing the authored soft edges to overlap.
            for (int i = 0; i < 128; i++)
            {
                Vector2 candidate = new(Range(min.x, max.x), Range(min.y, max.y));
                if (!Inside(region.outline, candidate, inset)) continue;
                float score = Spacing(region, candidate, sprite, scale, ignoreSmall: true);
                if (score <= spacing) continue;
                spacing = score; best = candidate;
            }
            return best;
        }

        private Vector3 ReplacementPosition(Region region, Cloud outgoing, Sprite sprite)
        {
            Vector2 best = outgoing.slotPosition;
            float scale = outgoing.transform.localScale.x;
            float score = Spacing(region, best, sprite, scale, outgoing, !outgoing.small);
            Vector2 size = (Vector2)outgoing.renderer.sprite.bounds.size * scale;
            float radius = Mathf.Min(size.x, size.y) * .25f;
            for (int i = 0; i < 32; i++)
            {
                float angle = Range(0, 2 * Mathf.PI);
                Vector2 candidate = (Vector2)outgoing.slotPosition + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Range(0, radius);
                if (!Inside(region.outline, candidate)) continue;
                float candidateScore = Spacing(region, candidate, sprite, scale, outgoing, !outgoing.small);
                if (candidateScore <= score) continue;
                score = candidateScore; best = candidate;
            }
            return new Vector3(best.x, best.y, environment.worldBounds.center.z);
        }

        private Cloud CreateCloud(Transform parent, Sprite sprite, Vector3 position, float scale, string label)
        {
            var cloud = new GameObject(label, typeof(SpriteRenderer));
            cloud.transform.SetParent(parent, false);
            float depth = Range(Mathf.Max(.01f, environment.fogDepthRange.x), Mathf.Max(.02f, environment.fogDepthRange.y));
            float front = Mathf.Sign(viewCamera.transform.position.z - environment.worldBounds.center.z);
            position.z += front * depth;
            cloud.transform.position = position;
            cloud.transform.localScale = Vector3.one * scale;
            var renderer = cloud.GetComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingLayerID = sortingLayer;
            renderer.sortingOrder = sortingOrder + Mathf.RoundToInt(depth * 10);
            var state = new Cloud
            {
                transform = cloud.transform, renderer = renderer, restPosition = position, slotPosition = position, depth = depth,
                small = Array.IndexOf(environment.smallFogSprites, sprite) >= 0,
                swayStart = Time.unscaledTime + Range(0, SwayPeriod), swayDirection = random.Next(2) == 0 ? -1 : 1
            };
            regionsByRoot[parent].clouds.Add(state);
            return state;
        }

        private float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());

        private void LateUpdate()
        {
            if (viewCamera == null || entranceSuspended) return;
            float now = Time.unscaledTime;
            foreach (var region in regions.Values)
            {
                if (!region.root.activeInHierarchy) continue;
                if (!region.locked)
                {
                    if (region.exitStart < 0) BeginUnlock(region);
                    float progress = Mathf.Clamp01((now - region.exitStart) / UnlockDuration);
                    float eased = Smooth(progress);
                    foreach (var cloud in region.clouds)
                    {
                        cloud.transform.position = cloud.exitPosition + Parallax(cloud) - cloud.exitParallax
                            + ScreenOffset(cloud.exitDirection * (UnlockPixels * eased));
                        SetAlpha(cloud, cloud.exitAlpha * (1 - eased));
                    }
                    if (progress >= 1)
                    {
                        CompleteReplacement(region);
                        region.root.SetActive(false);
                    }
                    continue;
                }

                UpdateReplacement(region, now);
                if (region.pulse == null || now - region.pulseStart >= PulseDuration) SelectPulse(region, now);
                foreach (var cloud in region.clouds)
                {
                    cloud.transform.position = cloud.restPosition + Parallax(cloud) + Sway(cloud, now);
                    float alpha = 1;
                    if (cloud == region.incoming) alpha = Smooth((now - region.replacementStart) / ReplacementFadeDuration);
                    else if (cloud == region.outgoing) alpha = 1 - Smooth((now - region.replacementStart - ReplacementFadeDuration) / ReplacementFadeDuration);
                    if (cloud == region.pulse)
                    {
                        float wave = Mathf.Sin(Mathf.PI * Mathf.Clamp01((now - region.pulseStart) / PulseDuration));
                        alpha *= 1 - .05f * wave * wave;
                    }
                    SetAlpha(cloud, alpha);
                }
            }
        }

        private static float Smooth(float progress)
        {
            float t = Mathf.Clamp01(progress);
            return t * t * (3 - 2 * t);
        }

        private static void SetAlpha(Cloud cloud, float alpha)
        {
            Color color = cloud.renderer.color;
            color.a = Mathf.Clamp01(alpha);
            cloud.renderer.color = color;
        }

        private Vector3 ScreenOffset(Vector2 pixels)
        {
            float unitsPerPixel = 2 * viewCamera.orthographicSize / Mathf.Max(1, viewCamera.pixelHeight);
            return (viewCamera.transform.right * pixels.x + viewCamera.transform.up * pixels.y) * unitsPerPixel;
        }

        private Vector3 Parallax(Cloud cloud)
        {
            Vector3 movement = viewCamera.transform.position - referenceCamera;
            movement.z = 0;
            // Foreground clouds move against camera travel, more strongly at nearer Z depths.
            // A common clamp flattened near/far motion into the same offset during large pans.
            return -movement * (cloud.depth * environment.fogParallax * 4f);
        }

        private Vector3 Sway(Cloud cloud, float now)
        {
            float elapsed = now - cloud.swayStart;
            if (elapsed <= 0) return Vector3.zero;
            float amplitude = Smooth(elapsed / .5f); // Ease into each cloud's independently delayed start.
            return ScreenOffset(Vector2.right * (SwayPixels * cloud.swayDirection * amplitude
                * Mathf.Sin(elapsed * (2 * Mathf.PI / SwayPeriod))));
        }

        private void SelectPulse(Region region, float now)
        {
            Cloud previous = region.pulse;
            region.pulse = null;
            if (region.clouds.Count == 0) return;
            int first = random.Next(region.clouds.Count);
            for (int i = 0; i < region.clouds.Count; i++)
            {
                var candidate = region.clouds[(first + i) % region.clouds.Count];
                if (candidate == region.incoming || candidate == region.outgoing || candidate == previous) continue;
                region.pulse = candidate;
                break;
            }
            if (region.pulse == null && previous != region.incoming && previous != region.outgoing) region.pulse = previous;
            region.pulseStart = now;
        }

        private void UpdateReplacement(Region region, float now)
        {
            if (region.incoming != null)
            {
                if (now - region.replacementStart >= 2 * ReplacementFadeDuration) CompleteReplacement(region);
                else return;
            }
            if (now < region.nextReplacement || region.clouds.Count < 2) return;
            int first = random.Next(region.clouds.Count);
            Cloud outgoing = region.clouds[first];
            if (outgoing == region.pulse) outgoing = region.clouds[(first + 1) % region.clouds.Count];
            var sprites = outgoing.small ? environment.smallFogSprites : environment.bigFogSprites;
            var sprite = sprites[random.Next(sprites.Length)];
            region.nextReplacement = now + ReplacementInterval;
            if (sprite == null) return;
            // Pick the least crowded point around the original slot, never drifting across repeated replacements.
            Vector3 position = ReplacementPosition(region, outgoing, sprite);
            region.incoming = CreateCloud(region.root.transform, sprite, position, outgoing.transform.localScale.x,
                outgoing.transform.name + " Replacement");
            region.incoming.slotPosition = outgoing.slotPosition;
            region.incoming.transform.position = region.incoming.restPosition + Parallax(region.incoming);
            SetAlpha(region.incoming, 0);
            region.outgoing = outgoing;
            region.replacementStart = now;
        }

        private void CompleteReplacement(Region region)
        {
            if (region.incoming == null) return;
            region.incoming.transform.name = region.outgoing.transform.name;
            region.outgoing.transform.name += " Retired";
            region.clouds.Remove(region.outgoing);
            Destroy(region.outgoing.transform.gameObject);
            region.incoming = region.outgoing = null;
        }

        private void BeginUnlock(Region region)
        {
            region.locked = false;
            region.exitStart = region.root.activeInHierarchy ? Time.unscaledTime : -1;
            foreach (var cloud in region.clouds)
            {
                cloud.exitPosition = cloud.transform.position;
                cloud.exitParallax = Parallax(cloud);
                cloud.exitAlpha = cloud.renderer.color.a;
                Vector2 direction = viewCamera.WorldToScreenPoint(cloud.exitPosition) - viewCamera.WorldToScreenPoint(region.center);
                if (direction.sqrMagnitude < .0001f)
                {
                    float angle = Range(0, 2 * Mathf.PI);
                    direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                }
                cloud.exitDirection = direction.normalized;
            }
            region.pulse = null;
        }

        private void RestoreLockedRegion(Region region)
        {
            CompleteReplacement(region);
            region.locked = true;
            region.root.SetActive(true);
            region.nextReplacement = Time.unscaledTime + ReplacementInterval;
            foreach (var cloud in region.clouds)
            {
                cloud.swayStart = Time.unscaledTime + Range(0, SwayPeriod);
                cloud.transform.position = cloud.restPosition + Parallax(cloud);
                SetAlpha(cloud, 1);
            }
            SelectPulse(region, Time.unscaledTime);
        }
    }
}
