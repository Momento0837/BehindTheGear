using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Rendering.Universal;

/// <summary>Loads a destination scene when the player presses F on this portal.</summary>
[RequireComponent(typeof(Interactable2D))]
public sealed class ScenePortal2D : MonoBehaviour
{
    [SerializeField] private string destinationSceneName = "Main";
    [SerializeField] private string destinationSpawnPointId = "FromDemoRightPortal";
    [SerializeField] private bool glowFacesRight = true;
    public bool GlowFacesRight => glowFacesRight;
    private readonly List<GameObject> authoredLights = new();
    private ControlTutorialUI portalHint;
    private bool presentationReady, hintUsed;
    private Vector3 hintArea;

    private void Awake()
    {
        GetComponent<Interactable2D>().AllowFromEitherSide = true;
        // Include inactive authored lights, but never create or alter their lighting settings.
        foreach (var root in gameObject.scene.GetRootGameObjects())
            foreach (var candidate in root.GetComponentsInChildren<Transform>(true))
                if (candidate.name == "Portal" && candidate.gameObject != gameObject
                    && candidate.GetComponentInChildren<Light2D>(true) != null)
                {
                    authoredLights.Add(candidate.gameObject);
                    candidate.gameObject.SetActive(false);
                }
    }

    private void OnEnable() => StartCoroutine(ShowPresentation());
    private IEnumerator ShowPresentation()
    {
        // Story startup first chooses the map and whether its portal has been unlocked.
        yield return null;
        presentationReady = true;
        SyncLights();
        var player = FindFirstObjectByType<PlayerMovement2D>();
        while (player == null) { yield return null; player = FindFirstObjectByType<PlayerMovement2D>(); }
        if (hintUsed) yield break;
        foreach (var tutorial in FindObjectsByType<ControlTutorialUI>(FindObjectsSortMode.None))
            if (tutorial.HandlesPortal(gameObject)) yield break;
        portalHint = gameObject.AddComponent<ControlTutorialUI>();
        portalHint.ArmPortal(gameObject);
        portalHint.Begin(player, portalOnly: true);
    }
    private void OnDisable()
    {
        StopAllCoroutines();
        presentationReady = false;
        foreach (var light in authoredLights) if (light != null) light.SetActive(false);
        if (portalHint != null)
        {
            // The UI canvas belongs to this helper and must not survive a map change.
            if (portalHint.PresentationRoot != null) Destroy(portalHint.PresentationRoot);
            Destroy(portalHint); portalHint = null;
        }
    }
    private void SyncLights()
    {
        var collider = GetComponent<Collider2D>();
        var bounds = collider != null ? collider.bounds : new Bounds(transform.position, Vector3.one);
        foreach (var light in authoredLights)
        {
            if (light == null) continue;
            // Garbage and Market share a scene; only the light beside the current exit is lit.
            Vector2 point = light.transform.position;
            bool nearby = Vector2.Distance(point, bounds.ClosestPoint(light.transform.position)) <= 6f;
            light.SetActive(presentationReady && isActiveAndEnabled && nearby);
        }
    }
    public void CompleteHint()
    {
        hintUsed = true;
        portalHint?.CompletePortal();
    }

    public void SetArea(float left, float right, float bottom = -4.5f, float top = 1.5f, bool lightFacesRight = true)
    {
        glowFacesRight = lightFacesRight;
        transform.position = new Vector3((left + right) * .5f, (bottom + top) * .5f, transform.position.z);
        if (hintArea != transform.position) { hintUsed = false; hintArea = transform.position; }
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
        if (presentationReady) SyncLights();
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

        CompleteHint();
        player.TravelTo(destinationSceneName, destinationSpawnPointId);
    }
}
