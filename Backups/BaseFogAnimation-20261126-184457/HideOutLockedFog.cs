using System;
using System.Collections.Generic;
using UnityEngine;

namespace BehindTheGear.HideOut
{
    // World-space sprite clouds. Orthographic cameras need explicit, depth-based parallax.
    public sealed class HideOutLockedFog : MonoBehaviour
    {
        // Base_Test's map artwork is displayed at 40% of its imported sprite size.
        // Match that shared scale; never resize individual clouds to fit an area's bounds.
        private const float BaseArtworkScale = .4f;
        private sealed class Cloud
        {
            public Transform transform;
            public Vector3 restPosition;
            public float depth;
        }
        private HideOutSpriteEnvironment environment;
        private Camera viewCamera;
        private Vector3 referenceCamera;
        private readonly Dictionary<Facility, GameObject> regions = new();
        private readonly List<Cloud> clouds = new();
        private readonly System.Random random = new();
        private int sortingLayer, sortingOrder;

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
                if (!regions.TryGetValue(area.facility, out var root) && locked)
                {
                    root = CreateRegion(area);
                    regions.Add(area.facility, root);
                }
                // Unlocking hides the big cloud and all satellites together, immediately.
                if (root != null && root.activeSelf != locked) root.SetActive(locked);
            }
        }

        private GameObject CreateRegion(HideOutSpriteEnvironment.Area area)
        {
            var root = new GameObject(area.facility + " Locked Fog");
            root.transform.SetParent(transform, false);
            Vector3 center = area.iconAnchor.position;
            center.y -= .2f;
            center.z = environment.worldBounds.center.z;
            int bigCount = random.Next(3, 6);
            for (int bigIndex = 0; bigIndex < bigCount; bigIndex++)
            {
                var sprite = environment.bigFogSprites[random.Next(environment.bigFogSprites.Length)];
                if (sprite == null) continue;
                // Use one shared scale in every area, preserving each sprite's authored size.
                float scale = BaseArtworkScale * environment.fogCoverage;
                Vector2 size = (Vector2)sprite.bounds.size * scale;
                Vector3 bigCenter = center + new Vector3(Range(-size.x * .02f, size.x * .02f), Range(-size.y * .02f, size.y * .02f), 0);
                CreateCloudWithSatellites(root.transform, sprite, bigCenter, scale, "BigFog " + (bigIndex + 1), "Fog " + (bigIndex + 1));
            }
            CreateBorderClouds(root.transform, area);
            return root;
        }

        private void CreateCloudWithSatellites(Transform parent, Sprite sprite, Vector3 center, float scale, string label, string detailLabel)
        {
            CreateCloud(parent, sprite, center, scale, label);
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
                bool side = random.Next(2) == 0;
                float sign = random.Next(2) == 0 ? -1 : 1;
                Vector3 offset = side
                    ? new Vector3(sign * edgeX, Range(-edgeY, edgeY), 0)
                    : new Vector3(Range(-edgeX, edgeX), sign * edgeY, 0);
                offset += new Vector3(Range(-jitter, jitter), Range(-jitter, jitter), 0);
                CreateCloud(parent, detail, center + offset, scale, detailLabel + "." + (i + 1));
            }
        }

        private void CreateBorderClouds(Transform parent, HideOutSpriteEnvironment.Area area)
        {
            var outline = AreaOutline(area);
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

        private void CreateCloud(Transform parent, Sprite sprite, Vector3 position, float scale, string label)
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
            clouds.Add(new Cloud { transform = cloud.transform, restPosition = position, depth = depth });
        }

        private float Range(float min, float max) => Mathf.Lerp(min, max, (float)random.NextDouble());

        private void LateUpdate()
        {
            if (viewCamera == null) return;
            Vector3 cameraOffset = viewCamera.transform.position - referenceCamera;
            cameraOffset.z = 0;
            foreach (var cloud in clouds)
            {
                if (cloud.transform == null || !cloud.transform.gameObject.activeInHierarchy) continue;
                Vector3 offset = Vector3.ClampMagnitude(cameraOffset * cloud.depth * environment.fogParallax, .2f);
                cloud.transform.position = cloud.restPosition + offset;
            }
        }
    }
}
