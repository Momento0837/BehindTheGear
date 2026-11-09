using TMPro;
using UnityEngine;

/// <summary>World-space portal hint and a soft additive light, independent of the portal's stretched scale.</summary>
public sealed class PortalPresentation2D : MonoBehaviour
{
    [SerializeField, Min(1f)] private float revealDistance = 6f;
    [SerializeField, Range(0f, 1f)] private float lightOpacity = .2f;
    private Collider2D area, playerCollider;
    private SpriteRenderer portalSprite;
    private TextMeshPro hint;
    private Transform lightTransform;
    private Material lightMaterial;
    private Mesh lightMesh;
    private GameObject visuals;
    private float alpha, nextPlayerSearch;
    private ScenePortal2D portal;

    private void Awake()
    {
        area = GetComponent<Collider2D>();
        portal = GetComponent<ScenePortal2D>();
        portalSprite = GetComponent<SpriteRenderer>();
        visuals = new GameObject("Portal Presentation (Runtime)");
        visuals.transform.SetParent(transform, false);
        var label = new GameObject("Portal [F]", typeof(TextMeshPro));
        label.transform.SetParent(visuals.transform, false);
        hint = label.GetComponent<TextMeshPro>();
        hint.font = Resources.Load<TMP_FontAsset>("UI/Fonts/PFStardustNormal");
        hint.text = "[F]";
        hint.fontSize = 5;
        hint.alignment = TextAlignmentOptions.Center;
        hint.rectTransform.sizeDelta = new Vector2(4, 1);
        hint.color = new Color(1, 1, 1, 0);
        hint.renderer.sortingLayerID = portalSprite != null ? portalSprite.sortingLayerID : 0;
        hint.renderer.sortingOrder = 100;

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
        renderer.sortingLayerID = hint.renderer.sortingLayerID;
        renderer.sortingOrder = portalSprite != null ? portalSprite.sortingOrder + 1 : 1;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private void OnEnable() { if (visuals != null) visuals.SetActive(true); }
    private void OnDisable()
    {
        alpha = 0;
        if (hint != null) hint.color = new Color(1, 1, 1, 0);
        if (visuals != null) visuals.SetActive(false);
    }

    private void LateUpdate()
    {
        if (playerCollider == null && Time.unscaledTime >= nextPlayerSearch)
        {
            nextPlayerSearch = Time.unscaledTime + 1f;
            var player = FindFirstObjectByType<PlayerInteractionController>();
            if (player != null) playerCollider = player.GetComponent<BoxCollider2D>();
        }
        // SetArea stretches the portal object; keep text and the light's world dimensions independent.
        Vector3 scale = transform.lossyScale;
        visuals.transform.localScale = new Vector3(1f / Mathf.Max(.001f, Mathf.Abs(scale.x)),
            1f / Mathf.Max(.001f, Mathf.Abs(scale.y)), 1f);
        Bounds bounds = area != null ? area.bounds : new Bounds(transform.position, new Vector3(3, 4, 0));
        Vector3 center = new Vector3(bounds.center.x, bounds.center.y, transform.position.z);
        float targetAlpha = 0;
        float labelY = bounds.max.y + .5f;
        if (playerCollider != null)
        {
            Bounds playerBounds = playerCollider.bounds;
            labelY = playerBounds.max.y + 1.05f;
            float distance = Vector2.Distance(playerBounds.center, bounds.ClosestPoint(playerBounds.center));
            targetAlpha = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(revealDistance, 1f, distance));
        }
        alpha = Mathf.MoveTowards(alpha, targetAlpha, Time.unscaledDeltaTime * 3f);
        hint.transform.position = new Vector3(center.x, labelY, center.z);
        hint.color = new Color(1, 1, 1, alpha);
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
