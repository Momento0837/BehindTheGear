using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace BehindTheGear.HideOut
{
    // A removable presentation layer; original facility meshes and materials stay intact.
    public sealed class HideOutFacilityLockCover : MonoBehaviour
    {
        private readonly Dictionary<MeshRenderer, MeshRenderer> covers = new();
        private readonly List<Material> materials = new();
        public void Refresh()
        {
            var shader = Resources.Load<Shader>("Effects/HideOutLockCover");
            if (shader == null) return;
            foreach (var source in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (source.name == "Temporary Gray Cover" || covers.ContainsKey(source)) continue;
                var filter = source.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                var layer = new GameObject("Temporary Gray Cover", typeof(MeshFilter), typeof(MeshRenderer));
                layer.layer = source.gameObject.layer;
                layer.transform.SetParent(source.transform, false);
                layer.GetComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var renderer = layer.GetComponent<MeshRenderer>();
                var original = source.sharedMaterials;
                var replacements = new Material[original.Length];
                for (int i = 0; i < replacements.Length; i++)
                {
                    var material = new Material(shader);
                    if (original[i] != null)
                    {
                        if (original[i].HasProperty("_BaseMap")) material.mainTexture = original[i].GetTexture("_BaseMap");
                        else if (original[i].HasProperty("_MainTex")) material.mainTexture = original[i].mainTexture;
                        if (original[i].HasProperty("_BaseColor")) material.color = original[i].GetColor("_BaseColor");
                        else if (original[i].HasProperty("_Color")) material.color = original[i].color;
                    }
                    replacements[i] = material; materials.Add(material);
                }
                renderer.sharedMaterials = replacements;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                covers.Add(source, renderer);
            }
        }
        private void LateUpdate()
        {
            foreach (var pair in covers)
                if (pair.Key != null && pair.Value != null) pair.Value.enabled = pair.Key.enabled;
        }
        private void OnDestroy() { foreach (var material in materials) if (material != null) Destroy(material); }
        public static void ShowLock(Button button, bool locked)
        {
            var existing = button.transform.Find("Temporary Facility Lock");
            if (existing != null) { existing.gameObject.SetActive(locked); return; }
            if (!locked) return;
            var root = new GameObject("Temporary Facility Lock", typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.SetParent(button.transform, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.85f, .75f);
            rect.sizeDelta = new Vector2(24, 28);
            Part(rect, "Body", new Vector2(0, -4), new Vector2(20, 16), new Color(.8f, .8f, .8f));
            Part(rect, "Left", new Vector2(-6, 7), new Vector2(4, 12), Color.white);
            Part(rect, "Right", new Vector2(6, 7), new Vector2(4, 12), Color.white);
            Part(rect, "Top", new Vector2(0, 12), new Vector2(16, 4), Color.white);
            Part(rect, "Keyhole", new Vector2(0, -4), new Vector2(3, 7), Color.black);
        }
        private static void Part(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            var image = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<Image>();
            image.transform.SetParent(parent, false);
            image.rectTransform.anchoredPosition = position; image.rectTransform.sizeDelta = size;
            image.color = color; image.raycastTarget = false;
        }
    }
}
