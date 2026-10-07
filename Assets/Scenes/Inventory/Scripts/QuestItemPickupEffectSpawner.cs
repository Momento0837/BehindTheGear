using UnityEngine;

[DisallowMultipleComponent]
public sealed class QuestItemPickupEffectSpawner : MonoBehaviour
{
    [SerializeField] private PlayerInventory inventory;
    [SerializeField] private GameObject questPickupEffectPrefab;
    [SerializeField] private Transform effectAnchor;
    [SerializeField] private Vector3 effectOffset;
    [SerializeField, Min(0f)] private float destroyAfterSeconds = 3f;
    [SerializeField] private bool parentToAnchor;

    private void Awake()
    {
        if (inventory == null) inventory = GetComponent<PlayerInventory>();
        if (effectAnchor == null) effectAnchor = transform;
    }

    private void OnEnable()
    {
        if (inventory != null) inventory.ItemCollected += OnItemCollected;
    }

    private void OnDisable()
    {
        if (inventory != null) inventory.ItemCollected -= OnItemCollected;
    }

    public void PlayQuestPickupEffect(InventoryItemDefinition item)
    {
        if (item == null || !item.IsQuestItem) return;
        SpawnQuestPickupEffect(GetEffectPosition());
    }

    public void SpawnQuestPickupEffect(Vector3 position)
    {
        if (questPickupEffectPrefab == null) return;

        Transform parent = parentToAnchor ? effectAnchor : null;
        GameObject instance = Instantiate(questPickupEffectPrefab, position, Quaternion.identity, parent);
        if (destroyAfterSeconds > 0f) Destroy(instance, destroyAfterSeconds);
    }

    private void OnItemCollected(InventoryItemDefinition item, int amount)
    {
        PlayQuestPickupEffect(item);
    }

    private Vector3 GetEffectPosition()
    {
        Transform anchor = effectAnchor != null ? effectAnchor : transform;
        return anchor.position + effectOffset;
    }
}
