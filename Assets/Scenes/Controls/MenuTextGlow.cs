using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>A soft halo made from the label's glyph mesh; never samples the button frame.</summary>
[RequireComponent(typeof(TextMeshProUGUI))]
public sealed class MenuTextGlow : MonoBehaviour
{
    private TextMeshProUGUI label;
    private RectTransform glowRect;
    private CanvasRenderer glowRenderer;
    private Mesh glowMesh;
    private Material glowMaterial;
    private float radius = 15f, opacity = .15f;

    public void Configure(float spread, float alpha)
    {
        radius = spread;
        opacity = alpha;
        label.SetVerticesDirty();
    }

    private void Awake() => label = GetComponent<TextMeshProUGUI>();

    private void OnEnable()
    {
        label.OnPreRenderText += Rebuild;
        if (glowRect != null) glowRect.gameObject.SetActive(true);
        label.SetVerticesDirty();
    }

    private void Rebuild(TMP_TextInfo info)
    {
        if (!isActiveAndEnabled || info.meshInfo.Length == 0) return;
        if (glowRect == null)
        {
            var go = new GameObject("Button Text Glow (Runtime)", typeof(RectTransform), typeof(CanvasRenderer));
            go.hideFlags = HideFlags.DontSave;
            go.layer = gameObject.layer;
            glowRect = go.GetComponent<RectTransform>();
            glowRect.SetParent(transform.parent, false);
            glowRect.SetSiblingIndex(transform.GetSiblingIndex());
            glowRenderer = go.GetComponent<CanvasRenderer>();
            glowMesh = new Mesh { name = "Button Glyph Halo", hideFlags = HideFlags.HideAndDontSave };
            glowMaterial = new Material(label.fontSharedMaterial) { hideFlags = HideFlags.HideAndDontSave };
            if (glowMaterial.HasProperty("_FaceColor")) glowMaterial.SetColor("_FaceColor", Color.white);
            if (glowMaterial.HasProperty("_OutlineWidth")) glowMaterial.SetFloat("_OutlineWidth", 0f);
            glowMaterial.DisableKeyword("UNDERLAY_ON");
            glowMaterial.DisableKeyword("UNDERLAY_INNER");
            glowMaterial.DisableKeyword("GLOW_ON");
        }
        SyncTransform();
        var source = info.meshInfo[0];
        var positions = new List<Vector3>();
        var uv = new List<Vector4>();
        var uv2 = new List<Vector2>();
        var colors = new List<Color>();
        var triangles = new List<int>();
        const int taps = 16;
        // Multiple faint glyph silhouettes form a soft 15-unit halo without atlas-padding limits.
        for (int ring = 1; ring <= 3; ring++)
        {
            float weight = ring == 1 ? .55f : ring == 2 ? .3f : .15f;
            float alpha = 1f - Mathf.Pow(1f - Mathf.Clamp01(opacity), weight / taps);
            for (int tap = 0; tap < taps; tap++)
            {
                float angle = (tap + ring * .33f) * Mathf.PI * 2f / taps;
                Vector3 offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * (radius * ring / 3f);
                int start = positions.Count;
                for (int v = 0; v < source.vertexCount; v++)
                {
                    positions.Add(source.vertices[v] + offset);
                    uv.Add(source.uvs0[v]);
                    uv2.Add(source.uvs2[v]);
                    colors.Add(new Color(1f, 1f, 1f, alpha * source.colors32[v].a / 255f));
                }
                for (int v = 0; v + 3 < source.vertexCount; v += 4)
                {
                    triangles.Add(start + v); triangles.Add(start + v + 1); triangles.Add(start + v + 2);
                    triangles.Add(start + v + 2); triangles.Add(start + v + 3); triangles.Add(start + v);
                }
            }
        }
        glowMesh.Clear();
        glowMesh.SetVertices(positions);
        glowMesh.SetUVs(0, uv);
        glowMesh.SetUVs(1, uv2);
        glowMesh.SetColors(colors);
        glowMesh.SetTriangles(triangles, 0);
        glowRenderer.materialCount = 1;
        glowRenderer.SetMaterial(glowMaterial, 0);
        glowRenderer.SetTexture(label.fontSharedMaterial.mainTexture);
        glowRenderer.SetMesh(glowMesh);
    }

    private void LateUpdate() => SyncTransform();

    private void SyncTransform()
    {
        if (glowRect == null) return;
        var rect = label.rectTransform;
        glowRect.anchorMin = rect.anchorMin; glowRect.anchorMax = rect.anchorMax;
        glowRect.pivot = rect.pivot; glowRect.sizeDelta = rect.sizeDelta;
        glowRect.anchoredPosition3D = rect.anchoredPosition3D;
        glowRect.localRotation = rect.localRotation; glowRect.localScale = rect.localScale;
    }

    private void OnDisable()
    {
        label.OnPreRenderText -= Rebuild;
        if (glowRect != null) glowRect.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (glowRect != null) Destroy(glowRect.gameObject);
        if (glowMesh != null) Destroy(glowMesh);
        if (glowMaterial != null) Destroy(glowMaterial);
    }
}
