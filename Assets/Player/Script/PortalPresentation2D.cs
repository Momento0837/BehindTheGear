using UnityEngine;

/// <summary>World-space portal hint and a soft additive light, independent of the portal's stretched scale.</summary>
public sealed class PortalPresentation2D : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)] private float lightOpacity = .3f;
    private Collider2D area;
    private SpriteRenderer portalSprite;

    private Transform lightTransform;
    private Material lightMaterial;
    private Mesh lightMesh;
    private GameObject visuals;
    private ScenePortal2D portal;

    private void Awake()
    {
        area = GetComponent<Collider2D>();
        portal = GetComponent<ScenePortal2D>();
        portalSprite = GetComponent<SpriteRenderer>();
        visuals = new GameObject("Portal Presentation (Runtime)");
        visuals.transform.SetParent(transform, false);

        var shader = Resources.Load<Shader>("Effects/PortalRadiance");
        if (shader == null) return;
        lightMaterial = new Material(shader) { name = "Portal Radiance (Runtime)" };
        var light = new GameObject("Portal White Radiance", typeof(MeshFilter), typeof(MeshRenderer));
        lightTransform = light.transform;
        lightTransform.SetParent(visuals.transform, false);
        lightMesh = new Mesh { name = "Portal Radiance Quad" };
        lightMesh.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0), new Vector3(.5f, -.5f, 0) };
        lightMesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
        lightMesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        lightMesh.RecalculateBounds();
        light.GetComponent<MeshFilter>().sharedMesh = lightMesh;
        var renderer = light.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = lightMaterial;
        renderer.sortingLayerID = portalSprite != null ? portalSprite.sortingLayerID : 0;
        renderer.sortingOrder = portalSprite != null ? portalSprite.sortingOrder + 1 : 1;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private void OnEnable() { if (visuals != null) visuals.SetActive(true); }
    private void OnDisable()
    {
        if (visuals != null) visuals.SetActive(false);
    }

    private void LateUpdate()
    {
        // SetArea stretches the portal object; keep the light's world dimensions independent.
        Vector3 scale = transform.lossyScale;
        visuals.transform.localScale = new Vector3(1f / Mathf.Max(.001f, Mathf.Abs(scale.x)),
            1f / Mathf.Max(.001f, Mathf.Abs(scale.y)), 1f);
        Bounds bounds = area != null ? area.bounds : new Bounds(transform.position, new Vector3(3, 4, 0));
        Vector3 center = new Vector3(bounds.center.x, bounds.center.y, transform.position.z);
        if (lightTransform != null)
        {
            // Fill exactly the interaction bounds. The shader puts its slit on the outer edge,
            // then fades inward; no light geometry extends past the portal area.
            lightTransform.position = center;
            lightTransform.localScale = new Vector3(bounds.size.x, bounds.size.y, 1);
            lightMaterial.SetFloat("_Opacity", lightOpacity);
            lightMaterial.SetFloat("_Direction", portal == null || portal.GlowFacesRight ? 1f : -1f);
        }
    }

    private void OnDestroy()
    {
        if (lightMaterial != null) Destroy(lightMaterial);
        if (lightMesh != null) Destroy(lightMesh);
        if (visuals != null) Destroy(visuals);
    }
}
