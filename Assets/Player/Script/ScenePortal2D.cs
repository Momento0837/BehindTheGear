using UnityEngine;

/// <summary>Loads a destination scene when the player presses F on this portal.</summary>
[RequireComponent(typeof(Interactable2D))]
public sealed class ScenePortal2D : MonoBehaviour
{
    [SerializeField] private string destinationSceneName = "Main";
    [SerializeField] private string destinationSpawnPointId = "FromDemoRightPortal";
    [SerializeField] private bool glowFacesRight = true;
    public bool GlowFacesRight => glowFacesRight;

    private void Awake()
    {
        GetComponent<Interactable2D>().AllowFromEitherSide = true;
        if (GetComponent<PortalPresentation2D>() == null) gameObject.AddComponent<PortalPresentation2D>();
    }

    public void SetArea(float left, float right, float bottom = -4.5f, float top = 1.5f, bool lightFacesRight = true)
    {
        glowFacesRight = lightFacesRight;
        transform.position = new Vector3((left + right) * .5f, (bottom + top) * .5f, transform.position.z);
        var box = GetComponent<BoxCollider2D>();
        var sprite = GetComponent<SpriteRenderer>();
        if (box == null || sprite == null || sprite.sprite == null) return;
        Vector2 size = sprite.sprite.bounds.size;
        Vector3 parentScale = transform.parent != null ? transform.parent.lossyScale : Vector3.one;
        transform.localScale = new Vector3((right - left) / size.x / Mathf.Abs(parentScale.x),
            (top - bottom) / size.y / Mathf.Abs(parentScale.y), 1f);
        box.offset = sprite.sprite.bounds.center;
        box.size = size;
        box.isTrigger = true;
        GetComponent<Interactable2D>().AllowFromEitherSide = true;
    }

    public void Travel(GameObject interactor)
    {
        if (interactor == null) return;
        PersistentPlayer2D player = interactor.GetComponent<PersistentPlayer2D>();
        if (player == null)
        {
            Debug.LogError("[Portal] The interacting player needs PersistentPlayer2D.", this);
            return;
        }

        player.TravelTo(destinationSceneName, destinationSpawnPointId);
    }
}
