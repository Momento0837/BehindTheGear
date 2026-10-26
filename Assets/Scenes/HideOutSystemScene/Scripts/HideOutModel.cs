using System;
using System.Collections.Generic;

namespace BehindTheGear.HideOut
{
    public enum Facility { Clinic, Bed, Dummy, Storage, Potions, Gym, Dining, Workshop }

    [Serializable] public sealed class ItemStack
    {
        public string id = "";
        public int count;
        public ItemStack() { }
        public ItemStack(string id, int count) { this.id = id; this.count = count; }
        public bool Empty => string.IsNullOrEmpty(id) || count <= 0;
    }
    [Serializable] public sealed class MaterialCost { public string itemId = "hideout_scrap"; public int amount = 2; }
    [Serializable] public sealed class UpgradeCost
    {
        public int coins = 10, actionPoints = 1;
        public MaterialCost[] materials = { new MaterialCost() };
    }
    [Serializable] public sealed class FacilityDefinition
    {
        public Facility facility;
        public string title;
        public UpgradeCost[] upgrades = Array.Empty<UpgradeCost>();
    }
    [Serializable] public sealed class HideOutRules
    {
        public int baseHealing = 5, baseStorageSlots = 10, bedHPPerLevel = 10;
        public int inventorySlotsPerLevel = 2, storageSlotsPerLevel = 5;
        public double clinicCooldown = 600, potionCooldown = 900, potionReduction = 30, minimumPotionCooldown = 60;
        public float clinicPercentPerLevel = .01f, gymPercentPerLevel = .01f;
        public int baseMana = 100, potionCount = 2;
        public string potionId = "health_potion";
        public bool offlineCooldown = true;
        public FacilityDefinition[] facilities = Array.Empty<FacilityDefinition>();
    }
    [Serializable] public sealed class ExtensionState { public string key, json; }
    [Serializable] public sealed class HideOutSave
    {
        public int version = 1;
        public int[] levels = new int[8];
        public List<ItemStack> storage = new();
        public List<ItemStack> inventory = new();
        public List<ItemStack> questInventory = new();
        public string[] equipment = new string[6], quickSlots = new string[3];
        public string inventoryConfiguration, playerStats, healthConfiguration;
        public int hp = 10, coins, actionPoints, baseMana = 100, mana = 100;
        public int baseInventorySlots = 15;
        public bool selenaRecruited;
        public double clinicReadyAt, potionsReadyAt, activeClock;
        public string returnScene = "";
        public float returnX, returnY, returnZ;
        public List<ExtensionState> extensions = new();
    }
    public interface IHideOutItems
    {
        int MaxStack(string id);
        string Name(string id);
    }

    // Pure transaction logic: callers mutate a candidate snapshot and publish it only after saving.
    public static class HideOutModel
    {
        public static void Normalize(HideOutSave s, HideOutRules rules)
        {
            if (s.levels == null) s.levels = new int[8];
            if (s.levels.Length < 8) Array.Resize(ref s.levels, 8);
            for (int i = 0; i < s.levels.Length; i++) s.levels[i] = Math.Max(0, s.levels[i]);
            s.storage ??= new(); s.inventory ??= new(); s.questInventory ??= new(); s.extensions ??= new();
            Resize(s.storage, rules.baseStorageSlots + s.levels[3] * rules.storageSlotsPerLevel);
            Resize(s.inventory, s.baseInventorySlots + s.levels[3] * rules.inventorySlotsPerLevel);
            if (s.equipment == null) s.equipment = new string[6];
            if (s.quickSlots == null) s.quickSlots = new string[3];
            if (s.equipment.Length != 6) Array.Resize(ref s.equipment, 6);
            if (s.quickSlots.Length != 3) Array.Resize(ref s.quickSlots, 3);
        }
        public static void Resize(List<ItemStack> slots, int capacity)
        {
            // Only remove empty tail slots, including when loading a save with changed settings.
            while (slots.Count > capacity && slots[slots.Count - 1].Empty) slots.RemoveAt(slots.Count - 1);
            while (slots.Count < capacity) slots.Add(new ItemStack());
        }
        public static int Tier(int level) => Math.Min(3, Math.Max(0, level) / 2);
        public static int Healing(HideOutSave s, HideOutRules r) => Math.Max(1, (int)Math.Ceiling(r.baseHealing * (1d + s.levels[0] * (double)r.clinicPercentPerLevel)));
        public static int MaxMana(HideOutSave s, HideOutRules r) => s.baseMana + (int)Math.Floor(s.baseMana * s.levels[5] * (double)r.gymPercentPerLevel + .00001d);
        public static double PotionCooldown(HideOutSave s, HideOutRules r) => Math.Max(Math.Max(1, r.minimumPotionCooldown), r.potionCooldown - s.levels[4] * r.potionReduction);
        public static bool Locked(HideOutSave s, Facility facility) => facility >= Facility.Dining || facility == Facility.Potions && !s.selenaRecruited;
        public static int Count(List<ItemStack> slots, string id)
        {
            int result = 0; foreach (var slot in slots) if (!slot.Empty && slot.id == id) result += slot.count; return result;
        }
        public static int Space(List<ItemStack> slots, string id, IHideOutItems items)
        {
            int max = items.MaxStack(id), result = 0;
            if (max <= 0) return 0;
            foreach (var slot in slots) if (slot.Empty) result += max; else if (slot.id == id) result += Math.Max(0, max - slot.count);
            return result;
        }
        public static int Add(List<ItemStack> slots, string id, int amount, IHideOutItems items)
        {
            int remaining = Math.Max(0, amount), max = items.MaxStack(id);
            if (max <= 0) return 0;
            foreach (var slot in slots)
            {
                if (slot.Empty || slot.id != id) continue;
                int n = Math.Min(remaining, Math.Max(0, max - slot.count)); slot.count += n; remaining -= n;
            }
            foreach (var slot in slots)
            {
                if (remaining == 0) break;
                if (!slot.Empty) continue;
                int n = Math.Min(remaining, max); slot.id = id; slot.count = n; remaining -= n;
            }
            return amount - remaining;
        }
        public static int Move(List<ItemStack> source, List<ItemStack> target, int index, IHideOutItems items)
        {
            if (ReferenceEquals(source, target) || index < 0 || index >= source.Count || source[index].Empty) return 0;
            var slot = source[index];
            int moved = Add(target, slot.id, slot.count, items);
            slot.count -= moved; if (slot.count == 0) slot.id = "";
            return moved;
        }
        public static int DepositExisting(HideOutSave s, IHideOutItems items)
        {
            var ids = new HashSet<string>();
            foreach (var slot in s.storage) if (!slot.Empty) ids.Add(slot.id);
            int moved = 0;
            for (int i = 0; i < s.inventory.Count; i++) if (ids.Contains(s.inventory[i].id)) moved += Move(s.inventory, s.storage, i, items);
            return moved;
        }
        public static void Sort(List<ItemStack> slots, IHideOutItems items)
        {
            slots.Sort((a, b) => a.Empty ? b.Empty ? 0 : 1 : b.Empty ? -1 :
                string.Compare(items.Name(a.id), items.Name(b.id), StringComparison.CurrentCulture));
        }
        public static UpgradeCost Next(HideOutSave s, HideOutRules rules, Facility f)
        {
            foreach (var definition in rules.facilities)
                if (definition.facility == f && definition.upgrades != null && s.levels[(int)f] < definition.upgrades.Length)
                    return definition.upgrades[s.levels[(int)f]];
            return null;
        }
        public static bool Upgrade(HideOutSave s, HideOutRules rules, Facility f, out string message)
        {
            if (Locked(s, f)) { message = "잠겨 있거나 추후 구현할 시설입니다."; return false; }
            var cost = Next(s, rules, f);
            if (cost == null) { message = "최대 강화 단계입니다."; return false; }
            if (cost.coins < 0 || cost.actionPoints < 0) { message = "강화 비용 설정 오류"; return false; }
            var required = new Dictionary<string, int>();
            foreach (var material in cost.materials ?? Array.Empty<MaterialCost>())
            {
                if (material == null || string.IsNullOrEmpty(material.itemId) || material.amount < 0) { message = "재료 설정 오류"; return false; }
                required.TryGetValue(material.itemId, out int previous); required[material.itemId] = checked(previous + material.amount);
            }
            if (s.coins < cost.coins || s.actionPoints < cost.actionPoints) { message = "코인 또는 행동 포인트가 부족합니다."; return false; }
            foreach (var pair in required) if (Count(s.inventory, pair.Key) < pair.Value) { message = "강화 재료가 부족합니다."; return false; }
            foreach (var pair in required)
            {
                int left = pair.Value;
                foreach (var slot in s.inventory)
                {
                    if (slot.id != pair.Key) continue;
                    int n = Math.Min(left, slot.count); slot.count -= n; left -= n;
                    if (slot.count == 0) slot.id = "";
                }
            }
            s.coins -= cost.coins; s.actionPoints -= cost.actionPoints; s.levels[(int)f]++;
            Normalize(s, rules);
            message = "강화 완료"; return true;
        }
        public static bool Heal(HideOutSave s, HideOutRules rules, int maxHP, double now, out string message)
        {
            if (s.hp <= 0 || s.hp >= maxHP) { message = "회복할 수 없는 상태이거나 HP가 가득 찼습니다."; return false; }
            if (now < s.clinicReadyAt) { message = "치료 부스 쿨타임 중입니다."; return false; }
            int restored = Math.Min(maxHP - s.hp, Healing(s, rules));
            s.hp += restored; s.clinicReadyAt = now + Math.Max(1, rules.clinicCooldown);
            message = $"HP {restored} 회복"; return true;
        }
        public static bool ReceivePotions(HideOutSave s, HideOutRules rules, IHideOutItems items, double now, out string message)
        {
            if (!s.selenaRecruited) { message = "셀레나 영입이 필요합니다."; return false; }
            if (now < s.potionsReadyAt) { message = "배송 쿨타임 중입니다."; return false; }
            if (rules.potionCount <= 0 || Space(s.inventory, rules.potionId, items) < rules.potionCount)
            { message = "물약을 모두 받을 공간이 없습니다. 수령 기회는 유지됩니다."; return false; }
            Add(s.inventory, rules.potionId, rules.potionCount, items);
            s.potionsReadyAt = now + PotionCooldown(s, rules); message = "물약 수령 완료"; return true;
        }
    }
}
