using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(10000)]
public sealed class PlayerCloakVisual : MonoBehaviour
{
    private PlayerInventory inventory;
    private SpriteRenderer body, cloak;
    private Animator source, follower;
    private PlayerCloakLibrary library;
    private Vector3 authoredPosition;
    public bool Equipped { get; private set; }
    public float OffsetY => cloak != null ? cloak.transform.localPosition.y : .15f;
    private float nextPreviewScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Register()
    { SceneManager.sceneLoaded -= Attach; SceneManager.sceneLoaded += Attach; }
    private static void Attach(Scene scene, LoadSceneMode mode)
    {
        foreach (var root in scene.GetRootGameObjects())
            foreach (var player in root.GetComponentsInChildren<PlayerInventory>(true))
                if (player.GetComponent<Animator>() != null && player.GetComponent<SpriteRenderer>() != null
                    && player.GetComponent<PlayerCloakVisual>() == null) player.gameObject.AddComponent<PlayerCloakVisual>();
    }
    private void Awake()
    {
        library = Resources.Load<PlayerCloakLibrary>("PlayerSkills/PlayerCloakLibrary");
        inventory = GetComponent<PlayerInventory>(); body = GetComponent<SpriteRenderer>(); source = GetComponent<Animator>();
        var existing = transform.Find("Cloak");
        if (existing == null)
        {
            existing = new GameObject("Cloak").transform;
            existing.SetParent(transform, false);
            existing.localPosition = new Vector3(0, .15f, 0);
            cloak = existing.gameObject.AddComponent<SpriteRenderer>();
            cloak.sortingLayerID = body.sortingLayerID;
            cloak.sharedMaterial = body.sharedMaterial;
        }
        else cloak = existing.GetComponent<SpriteRenderer>();
        if (cloak == null) cloak = existing.gameObject.AddComponent<SpriteRenderer>();
        // Preserve the authored Idle alignment; only depth must match the body.
        var position = existing.localPosition;
        position.z = 0;
        existing.localPosition = position;
        authoredPosition = position;
        cloak.sortingOrder = 2;
        follower = existing.GetComponent<Animator>();
        if (follower == null) follower = existing.gameObject.AddComponent<Animator>();
        if (library != null) follower.runtimeAnimatorController = library.controller;
        follower.speed = 0; follower.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        existing.gameObject.SetActive(false);
    }
    private void OnEnable() { if (inventory != null) { inventory.Changed += RefreshEquipment; RefreshEquipment(); } }
    private void OnDisable() { if (inventory != null) inventory.Changed -= RefreshEquipment; }
    private void RefreshEquipment()
    {
        Equipped = false;
        foreach (InventoryItemDefinition.EquipmentSlot slot in System.Enum.GetValues(typeof(InventoryItemDefinition.EquipmentSlot)))
            if (inventory.GetEquipped(slot)?.ItemId == "Tattered_Cloak") { Equipped = true; break; }
        if (cloak != null) cloak.gameObject.SetActive(Equipped && library != null);
    }
    private void LateUpdate()
    {
        if (library == null) return;
        if (Equipped && cloak != null)
        {
            if (source.runtimeAnimatorController != null && follower.runtimeAnimatorController != null)
            {
                var state = source.GetCurrentAnimatorStateInfo(0);
                if (source.IsInTransition(0) && source.GetAnimatorTransitionInfo(0).normalizedTime >= .5f)
                    state = source.GetNextAnimatorStateInfo(0);
                if (follower.HasState(0, state.fullPathHash)) { follower.Play(state.fullPathHash, 0, state.normalizedTime); follower.Update(0); }
            }
            // Sprite reference matching also covers hit-stop and discrete transition frames exactly.
            cloak.sprite = library.Frame(body.sprite);
            // Keep the scene's authored 0.15 alignment, compensating only for
            // the body's switch from its trimmed preview to animation canvases.
            cloak.transform.localPosition = authoredPosition + (Vector3)library.AnimationOffset;
            cloak.flipX = body.flipX; cloak.flipY = body.flipY; cloak.color = body.color;
            cloak.enabled = body.enabled; cloak.forceRenderingOff = body.forceRenderingOff;
        }
        if (Time.unscaledTime < nextPreviewScan) return;
        nextPreviewScan = Time.unscaledTime + .5f;
        foreach (var animator in FindObjectsByType<Animator>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (animator.GetComponent<UnityEngine.UI.Image>() == null || animator.runtimeAnimatorController == null) continue;
            bool standing = false;
            foreach (var clip in animator.runtimeAnimatorController.animationClips) if (clip.name == "PlayerStand") standing = true;
            if (!standing) continue;
            var preview = animator.GetComponent<InventoryCloakPreview>();
            if (preview == null) preview = animator.gameObject.AddComponent<InventoryCloakPreview>();
            preview.Bind(this, library);
        }
    }
}
