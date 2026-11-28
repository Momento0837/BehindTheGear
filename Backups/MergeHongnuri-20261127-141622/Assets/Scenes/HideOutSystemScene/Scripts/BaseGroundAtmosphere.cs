using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BehindTheGear.HideOut
{
    // Classify unlit artwork, so dim 2D lights cannot turn walls into floors.
    [DisallowMultipleComponent]
    public sealed class BaseGroundAtmosphere : MonoBehaviour
    {
        [Serializable]
        public sealed class Settings
        {
            public bool enabled = true;
            [Range(0, 20)] public float blackSpreadArtworkPixels = 6;
            [Range(0, 1)] public float blackSpreadOpacity = .65f;
            [Range(.01f, .4f)] public float blackThreshold = .16f;
            [Range(0, .3f)] public float groundFogOpacity = .09f;
            [Min(.1f)] public float groundFogSize = 3.5f;
            [Min(0)] public float groundFogSpeed = .01f;
            [Range(0, .1f)] public float screenNoiseOpacity = .03f;
            [Tooltip("Unlit room-color pixels below this sRGB brightness are the authored floor, not the walls.")]
            [Range(.1f, .8f)] public float roomFloorBrightness = .42f;
        }
        public sealed class MaskSprite
        {
            public SpriteRenderer renderer;
            public Sprite sprite;
            public Mesh mesh;
            public float mode;
            public readonly MaterialPropertyBlock properties = new();
        }
        private static readonly Dictionary<Camera, BaseGroundAtmosphere> Active = new();
        private readonly List<MaskSprite> sprites = new();
        private HideOutSpriteEnvironment environment;
        private Camera viewCamera;
        private Material maskMaterial, compositeMaterial, noiseMaterial;
        private GameObject noiseCanvas;
        public Settings Configuration => environment.groundAtmosphere;
        public Material MaskMaterial => maskMaterial;
        public Material CompositeMaterial => compositeMaterial;
        public IReadOnlyList<MaskSprite> MaskSprites => sprites;
        public float ArtworkPixelSize { get; private set; } = .4f / 11f;

        public static bool TryGet(Camera camera, out BaseGroundAtmosphere atmosphere)
            => Active.TryGetValue(camera, out atmosphere) && atmosphere != null && atmosphere.isActiveAndEnabled
                && atmosphere.Configuration.enabled && atmosphere.compositeMaterial != null;

        public void Initialize(HideOutSpriteEnvironment owner, Camera camera)
        {
            if (environment != null) return;
            environment = owner; viewCamera = camera; owner.groundAtmosphere ??= new Settings();
            maskMaterial = MakeMaterial("Effects/BaseGroundMask");
            compositeMaterial = MakeMaterial("Effects/BaseGroundAtmosphere");
            noiseMaterial = MakeMaterial("Effects/BaseScreenNoise");
            if (maskMaterial == null || compositeMaterial == null || noiseMaterial == null) { enabled = false; return; }
            // Capture original artwork before runtime lock clouds; include inactive upgrade props as occluders.
            foreach (var renderer in owner.GetComponentsInChildren<SpriteRenderer>(true))
            {
                if (renderer.sprite == null || renderer.transform.parent.name.EndsWith(" Locked Fog", StringComparison.Ordinal)) continue;
                float mode = 0;
                if (renderer.transform.parent.name == "Floor_Group" && (renderer.name == "Floor" || renderer.name == "Path")) mode = 1;
                else if (renderer.transform.parent.name == "Room_Group" && renderer.name == "Color") mode = 2;
                if (mode == 1) ArtworkPixelSize = Mathf.Abs(renderer.transform.lossyScale.x) / renderer.sprite.pixelsPerUnit;
                sprites.Add(new MaskSprite { renderer = renderer, sprite = renderer.sprite, mesh = SpriteMesh(renderer.sprite), mode = mode });
            }
            sprites.Sort((a, b) =>
            {
                int layer = SortingLayer.GetLayerValueFromID(a.renderer.sortingLayerID).CompareTo(SortingLayer.GetLayerValueFromID(b.renderer.sortingLayerID));
                if (layer != 0) return layer;
                int order = a.renderer.sortingOrder.CompareTo(b.renderer.sortingOrder);
                return order != 0 ? order : b.renderer.transform.position.z.CompareTo(a.renderer.transform.position.z);
            });
            noiseCanvas = new GameObject("Base Monochrome Noise", typeof(RectTransform), typeof(Canvas));
            noiseCanvas.transform.SetParent(transform, false);
            var canvas = noiseCanvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32700; // Above UI/dialogue, below the scene transition.
            var imageObject = new GameObject("Noise 3 percent", typeof(RectTransform), typeof(RawImage));
            imageObject.transform.SetParent(noiseCanvas.transform, false);
            var rect = (RectTransform)imageObject.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            var image = imageObject.GetComponent<RawImage>();
            image.texture = Texture2D.whiteTexture; image.material = noiseMaterial; image.raycastTarget = false;
            Active[camera] = this; LateUpdate();
        }
        private static Material MakeMaterial(string resource)
        {
            var shader = Resources.Load<Shader>(resource);
            if (shader == null || !shader.isSupported) { Debug.LogError("Base atmosphere shader unavailable: " + resource); return null; }
            return new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
        private static Mesh SpriteMesh(Sprite sprite)
        {
            Vector2[] source = sprite.vertices;
            var vertices = new Vector3[source.Length];
            for (int i = 0; i < source.Length; i++) vertices[i] = source[i];
            ushort[] indices = sprite.triangles;
            var triangles = new int[indices.Length];
            for (int i = 0; i < indices.Length; i++) triangles[i] = indices[i];
            var mesh = new Mesh { name = "Ground mask " + sprite.name, hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = vertices; mesh.uv = sprite.uv; mesh.triangles = triangles;
            return mesh;
        }
        public void PrepareMask()
        {
            foreach (var entry in sprites)
            {
                if (entry.renderer == null || entry.renderer.sprite == null) continue;
                if (entry.sprite != entry.renderer.sprite)
                {
                    Destroy(entry.mesh); entry.sprite = entry.renderer.sprite; entry.mesh = SpriteMesh(entry.sprite);
                }
                entry.properties.SetTexture("_MainTex", entry.sprite.texture);
                entry.properties.SetFloat("_FloorMode", entry.mode);
                entry.properties.SetFloat("_FloorBrightness", Configuration.roomFloorBrightness);
                entry.properties.SetFloat("_Opacity", entry.renderer.color.a);
            }
        }
        public void ConfigureComposite(MaterialPropertyBlock properties, Camera camera)
        {
            float depth = Vector3.Dot(transform.position - camera.transform.position, camera.transform.forward);
            Vector3 origin = transform.InverseTransformPoint(camera.ViewportToWorldPoint(new Vector3(0, 0, depth)));
            Vector3 right = transform.InverseTransformPoint(camera.ViewportToWorldPoint(new Vector3(1, 0, depth))) - origin;
            Vector3 up = transform.InverseTransformPoint(camera.ViewportToWorldPoint(new Vector3(0, 1, depth))) - origin;
            properties.SetVector("_GroundOrigin", origin); properties.SetVector("_GroundAxisX", right); properties.SetVector("_GroundAxisY", up);
            float radius = Configuration.blackSpreadArtworkPixels * ArtworkPixelSize / (2 * camera.orthographicSize);
            properties.SetVector("_BlackSpread", new Vector4(radius / camera.aspect, radius, Configuration.blackSpreadOpacity, Configuration.blackThreshold));
            properties.SetVector("_GroundFog", new Vector4(Configuration.groundFogOpacity, Configuration.groundFogSize,
                Time.unscaledTime * Configuration.groundFogSpeed, 0));
        }
        private void LateUpdate()
        {
            if (noiseCanvas == null) return;
            noiseCanvas.SetActive(Configuration.enabled && Configuration.screenNoiseOpacity > 0 && viewCamera != null && viewCamera.isActiveAndEnabled);
            noiseMaterial.SetFloat("_Opacity", Configuration.screenNoiseOpacity);
            noiseMaterial.SetFloat("_NoiseFrame", Mathf.Floor(Time.unscaledTime * 12));
        }
        private void OnEnable() { if (viewCamera != null) Active[viewCamera] = this; }
        private void OnDisable()
        {
            if (viewCamera != null && Active.TryGetValue(viewCamera, out var current) && current == this) Active.Remove(viewCamera);
            if (noiseCanvas != null) noiseCanvas.SetActive(false);
        }
        private void OnDestroy()
        {
            OnDisable();
            foreach (var entry in sprites) if (entry.mesh != null) Destroy(entry.mesh);
            if (maskMaterial != null) Destroy(maskMaterial);
            if (compositeMaterial != null) Destroy(compositeMaterial);
            if (noiseMaterial != null) Destroy(noiseMaterial);
            if (noiseCanvas != null) Destroy(noiseCanvas);
        }
    }
}
