using System;
using UnityEngine;

public sealed class InventoryRewardGrant : MonoBehaviour
{
    [Serializable]
    private sealed class CatalogEntry
    {
        public string itemId = string.Empty;
        public InventoryItemDefinition item = null;
    }

    [Serializable]
    private sealed class RewardFile
    {
        public RewardEntry[] rewards = Array.Empty<RewardEntry>();
    }

    [Serializable]
    private sealed class RewardEntry
    {
        public string itemId = string.Empty;
        public int amount = 1;
    }

    [SerializeField] private TextAsset rewardJson;
    [SerializeField] private CatalogEntry[] itemCatalog;

    public void GrantConfiguredRewards(PlayerInventory inventory)
    {
        if (rewardJson == null)
        {
            Debug.LogWarning("[Inventory Reward] Reward JSON is not assigned.", this);
            return;
        }

        GrantRewardsFromJson(rewardJson.text, inventory);
    }

    public void GrantRewardsFromJson(string json, PlayerInventory inventory)
    {
        if (inventory == null || string.IsNullOrWhiteSpace(json)) return;
        RewardFile file = JsonUtility.FromJson<RewardFile>(json);
        if (file?.rewards == null) return;

        foreach (RewardEntry reward in file.rewards)
        {
            InventoryItemDefinition item = FindItem(reward.itemId);
            if (item == null)
            {
                Debug.LogWarning($"[Inventory Reward] Unknown item id '{reward.itemId}'.", this);
                continue;
            }

            inventory.Add(item, Mathf.Max(1, reward.amount));
        }
    }

    private InventoryItemDefinition FindItem(string itemId)
    {
        if (itemCatalog == null) return null;
        foreach (CatalogEntry entry in itemCatalog)
            if (entry != null && entry.item != null && entry.itemId == itemId) return entry.item;
        return null;
    }
}
