using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>A short-lived sprite snapshot with no collider, combat logic, or owned material.</summary>
public sealed class PlayerDashAfterimage : MonoBehaviour
{
    private SpriteRenderer visual;
    private Color tint;
    private float lifetime, elapsed;

    public static PlayerDashAfterimage Create(SpriteRenderer source, float duration, Color color)
    {
        var go = new GameObject("Dash Afterimage", typeof(SpriteRenderer));
        if (source.gameObject.scene.GetPhysicsScene2D() != Physics2D.defaultPhysicsScene)
            SceneManager.MoveGameObjectToScene(go, source.gameObject.scene);
        go.transform.SetPositionAndRotation(source.transform.position, source.transform.rotation);
        go.transform.localScale = source.transform.lossyScale;
        var effect = go.AddComponent<PlayerDashAfterimage>();
        effect.visual = go.GetComponent<SpriteRenderer>();
        effect.visual.sprite = source.sprite;
        effect.visual.sharedMaterial = source.sharedMaterial;
        effect.visual.flipX = source.flipX; effect.visual.flipY = source.flipY;
        effect.visual.drawMode = source.drawMode; effect.visual.size = source.size;
        effect.visual.sortingLayerID = source.sortingLayerID;
        effect.visual.sortingOrder = source.sortingOrder - 1;
        effect.visual.maskInteraction = source.maskInteraction;
        color.a *= source.color.a;
        effect.tint = color; effect.visual.color = color;
        effect.lifetime = Mathf.Max(.01f, duration);
        return effect;
    }

    private void Update()
    {
        elapsed += Time.deltaTime;
        float remaining = 1f - Mathf.Clamp01(elapsed / lifetime);
        visual.color = new Color(tint.r, tint.g, tint.b, tint.a * remaining * remaining);
        if (elapsed >= lifetime) Destroy(gameObject);
    }
}
