using System;
using System.Collections.Generic;
using UnityEngine;

namespace BehindTheGear.HideOut
{
    // World-space sprite clouds. Orthographic cameras need explicit, depth-based parallax.
    public sealed class HideOutLockedFog : MonoBehaviour
    {
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
            float spacing = float.MaxValue;
            foreach (var neighbor in environment.areas)
                if (neighbor?.iconAnchor != null && neighbor.facility != area.facility)
                    spacing = Mathf.Min(spacing, Vector2.Distance(area.iconAnchor.position, neighbor.iconAnchor.position));
            int bigCount = random.Next(3, 6);
            for (int bigIndex = 0; bigIndex < bigCount; bigIndex++)
            {
                var sprite = environment.bigFogSprites[random.Next(environment.bigFogSprites.Length)];
                if (sprite == null) continue;
                // Keep the 3-5 layers tightly overlapping instead of widening coverage into open areas.
                float width = Mathf.Min(sprite.bounds.size.x, spacing * 1.15f) * environment.fogCoverage;
                float scale = width / Mathf.Max(.001f, sprite.bounds.size.x);
                Vector2 size = (Vector2)sprite.bounds.size * scale;
                Vector3 bigCenter = center + new Vector3(Range(-size.x * .06f, size.x * .06f), Range(-size.y * .06f, size.y * .06f), 0);
                CreateCloud(root.transform, sprite, bigCenter, scale, "BigFog " + (bigIndex + 1));
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
                    // From a corner-based sprite origin this is width-10 / height-10, +/-5 pixels.
                    bool side = random.Next(2) == 0;
                    float sign = random.Next(2) == 0 ? -1 : 1;
                    Vector3 offset = side
                        ? new Vector3(sign * edgeX, Range(-edgeY, edgeY), 0)
                        : new Vector3(Range(-edgeX, edgeX), sign * edgeY, 0);
                    offset += new Vector3(Range(-jitter, jitter), Range(-jitter, jitter), 0);
                    CreateCloud(root.transform, detail, bigCenter + offset, scale, $"Fog {bigIndex + 1}.{i + 1}");
                }
            }
            return root;
        }

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
