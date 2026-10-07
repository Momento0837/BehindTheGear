using System;
using UnityEngine;

public sealed class InventoryDropper : MonoBehaviour
{
    [Serializable]
    private sealed class CategoryPool
    {
        public InventoryItemDefinition.ItemCategory category = InventoryItemDefinition.ItemCategory.Misc;
        public InventoryItemDefinition[] items = Array.Empty<InventoryItemDefinition>();

        public InventoryItemDefinition Pick()
        {
            if (items == null || items.Length == 0) return null;
            return items[UnityEngine.Random.Range(0, items.Length)];
        }
    }

    [SerializeField] private WorldItemPickup pickupPrefab;
    [SerializeField] private CategoryPool[] pools;
    [SerializeField, Min(0f)] private float scatterRadius = .75f;

    public void DropMonsterLoot() => DropLoot(false);
    public void DropBossLoot() => DropLoot(true);

    public void DropShopOrCraftResult(InventoryItemDefinition item, int amount, PlayerInventory inventory)
    {
        if (inventory == null || item == null || amount <= 0) return;
        inventory.Add(item, amount);
    }

    private void DropLoot(bool boss)
    {
        InventoryItemDefinition.ItemCategory category = RollCategory(boss);
        InventoryItemDefinition item = Pick(category);
        if (item == null)
        {
            Debug.LogWarning($"[Inventory Dropper] No item pool configured for {category}.", this);
            return;
        }

        int amount = RollAmount(category, boss);
        SpawnPickup(item, amount);
    }

    private InventoryItemDefinition.ItemCategory RollCategory(bool boss)
    {
        float roll = UnityEngine.Random.value;
        if (boss)
        {
            if (roll < .5f) return InventoryItemDefinition.ItemCategory.Misc;
            if (roll < .8f) return InventoryItemDefinition.ItemCategory.Consumable;
            return InventoryItemDefinition.ItemCategory.Equipment;
        }

        if (roll < .6f) return InventoryItemDefinition.ItemCategory.Misc;
        if (roll < .99f) return InventoryItemDefinition.ItemCategory.Consumable;
        return InventoryItemDefinition.ItemCategory.Equipment;
    }

    private static int RollAmount(InventoryItemDefinition.ItemCategory category, bool boss)
    {
        if (category == InventoryItemDefinition.ItemCategory.Equipment)
            return boss ? UnityEngine.Random.Range(1, 3) : 1;
        if (category == InventoryItemDefinition.ItemCategory.Consumable)
            return boss ? UnityEngine.Random.Range(3, 5) : UnityEngine.Random.Range(1, 3);
        return boss ? UnityEngine.Random.Range(5, 9) : UnityEngine.Random.Range(1, 3);
    }

    private InventoryItemDefinition Pick(InventoryItemDefinition.ItemCategory category)
    {
        if (pools == null) return null;
        foreach (CategoryPool pool in pools)
            if (pool != null && pool.category == category) return pool.Pick();
        return null;
    }

    private void SpawnPickup(InventoryItemDefinition item, int amount)
    {
        Vector2 offset = UnityEngine.Random.insideUnitCircle * scatterRadius;
        Vector3 position = transform.position + new Vector3(offset.x, offset.y, 0f);
        WorldItemPickup pickup;
        if (pickupPrefab != null)
        {
            pickup = Instantiate(pickupPrefab, position, Quaternion.identity);
        }
        else
        {
            GameObject obj = new("Dropped " + item.DisplayName, typeof(SpriteRenderer), typeof(CircleCollider2D), typeof(WorldItemPickup));
            obj.transform.position = position;
            pickup = obj.GetComponent<WorldItemPickup>();
        }

        pickup.Configure(item, amount);
    }
}
