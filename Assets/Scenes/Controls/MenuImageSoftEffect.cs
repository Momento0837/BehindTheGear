using UnityEngine;
using UnityEngine.UI;

/// <summary>Per-image UV-aware blur/glow; expands the title mesh so its halo is not clipped.</summary>
[RequireComponent(typeof(Image))]
public sealed class MenuImageSoftEffect : BaseMeshEffect
{
    private Material effectMaterial;
    private Material originalMaterial;
    private float blurRadius, glowRadius, opacity;

    public void Configure(Shader shader, float blur, float glow, float alpha)
    {
        originalMaterial = graphic.material;
        blurRadius = blur;
        glowRadius = glow;
        opacity = alpha;
        effectMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        graphic.material = effectMaterial;
        graphic.SetVerticesDirty();
    }

    public override void ModifyMesh(VertexHelper mesh)
    {
        if (!IsActive() || effectMaterial == null || mesh.currentVertCount != 4) return;
        UIVertex vertex = default;
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
        Vector2 uvMin = min, uvMax = max;
        for (int i = 0; i < 4; i++)
        {
            mesh.PopulateUIVertex(ref vertex, i);
            min = Vector2.Min(min, vertex.position);
            max = Vector2.Max(max, vertex.position);
            uvMin = Vector2.Min(uvMin, vertex.uv0);
            uvMax = Vector2.Max(uvMax, vertex.uv0);
        }
        Vector2 size = max - min;
        if (size.x <= 0f || size.y <= 0f) return;
        Vector2 uvPerUnit = new((uvMax.x - uvMin.x) / size.x, (uvMax.y - uvMin.y) / size.y);
        effectMaterial.SetVector("_UVRect", new Vector4(uvMin.x, uvMin.y, uvMax.x, uvMax.y));
        effectMaterial.SetVector("_UVStep", new Vector4(uvPerUnit.x, uvPerUnit.y, 0, 0));
        effectMaterial.SetFloat("_BlurRadius", blurRadius);
        effectMaterial.SetFloat("_GlowRadius", glowRadius);
        effectMaterial.SetFloat("_GlowOpacity", opacity);
        if (glowRadius <= 0f) return;
        Vector2 center = (min + max) * .5f;
        for (int i = 0; i < 4; i++)
        {
            mesh.PopulateUIVertex(ref vertex, i);
            Vector2 padding = new(vertex.position.x < center.x ? -glowRadius : glowRadius,
                vertex.position.y < center.y ? -glowRadius : glowRadius);
            vertex.position += (Vector3)padding;
            vertex.uv0 += new Vector4(padding.x * uvPerUnit.x, padding.y * uvPerUnit.y, 0, 0);
            mesh.SetUIVertex(vertex, i);
        }
    }

    protected override void OnDestroy()
    {
        if (graphic != null && effectMaterial != null) graphic.material = originalMaterial;
        if (effectMaterial != null) Destroy(effectMaterial);
        base.OnDestroy();
    }
}
