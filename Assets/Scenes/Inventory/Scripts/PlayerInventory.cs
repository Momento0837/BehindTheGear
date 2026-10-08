using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerInventory : MonoBehaviour
{
    [Serializable]
    public sealed class Slot
    {
        [SerializeField] private InventoryItemDefinition item;
        [SerializeField] private int amount;

        public InventoryItemDefinition Item => item;
        public int Amount => amount;
        public bool IsEmpty => item == null || amount <= 0;
        public bool IsFull => !IsEmpty && amount >= item.MaxStack;

        internal void Set(InventoryItemDefinition value, int count)
        {
            item = count > 0 ? value : null;
            amount = item != null ? Mathf.Clamp(count, 0, item.MaxStack) : 0;
        }
    }

    [Header("Capacity")]
    [SerializeField, Range(1, 120)] private int capacity = 15;
    [SerializeField, Min(0)] private int slotsPerAttackStat = 2;
    [SerializeField, Range(1, 30)] private int questCapacity = 5;
    [SerializeField] private PlayerStats stats;
    [SerializeField] private Slot[] slots;
    [SerializeField] private Slot[] questSlots;
    [SerializeField] private InventoryItemDefinition[] quickSlots = new InventoryItemDefinition[3];
    [Header("Equipment")]
    [SerializeField] private InventoryItemDefinition equippedWeapon;
    [SerializeField] private InventoryItemDefinition equippedArmor;
    [SerializeField] private InventoryItemDefinition equippedAccessory;
    [SerializeField] private InventoryItemDefinition equippedHead;
    [SerializeField] private InventoryItemDefinition equippedShoes;
    [SerializeField] private InventoryItemDefinition equippedOther;

    public event Action Changed;
    public event Action<InventoryItemDefinition, int> ItemCollected;
    public event Action<InventoryItemDefinition> ItemUsed;
    public event Action InventoryFull;

    public PlayerStats Stats => stats;
    public int ConfiguredCapacity => Mathf.Clamp(capacity, 1, 120);
    public int Capacity { get { EnsureSlots(); return slots.Length; } }
    public int QuestCapacity { get { EnsureSlots(); return questSlots.Length; } }
    public int QuickSlotCount => 3;

    private void Awake()
    {
        if (stats == null) stats = GetComponent<PlayerStats>();
        if (stats == null) stats = gameObject.AddComponent<PlayerStats>();
        EnsureSlots();
    }

    private void OnEnable()
    {
        if (stats != null) stats.Changed += StatsChanged;
    }

    private void OnDisable()
    {
        if (stats != null) stats.Changed -= StatsChanged;
    }

    private void StatsChanged()
    {
        EnsureSlots();
        Changed?.Invoke();
    }

    private void EnsureSlots()
    {
        int generalCapacity = ConfiguredCapacity + (stats != null ? stats.Attack * slotsPerAttackStat : 0);
        ResizeSlots(ref slots, Mathf.Clamp(generalCapacity, 1, 120));
        ResizeSlots(ref questSlots, Mathf.Clamp(questCapacity, 1, 30));

        if (quickSlots == null || quickSlots.Length != QuickSlotCount)
            Array.Resize(ref quickSlots, QuickSlotCount);
    }

    private static void ResizeSlots(ref Slot[] target, int count)
    {
        if (target == null) target = Array.Empty<Slot>();
        if (target.Length != count) Array.Resize(ref target, count);
        for (int i = 0; i < target.Length; i++) target[i] ??= new Slot();
    }

    public Slot GetSlot(int index)
    {
        EnsureSlots();
        return index >= 0 && index < slots.Length ? slots[index] : null;
    }

    public Slot GetQuestSlot(int index)
    {
        EnsureSlots();
        return index >= 0 && index < questSlots.Length ? questSlots[index] : null;
    }

    public InventoryItemDefinition GetQuickSlot(int index)
    {
        EnsureSlots();
        return index >= 0 && index < quickSlots.Length ? quickSlots[index] : null;
    }

    public InventoryItemDefinition GetEquipped(InventoryItemDefinition.EquipmentSlot slot)
    {
        return slot switch
        {
            InventoryItemDefinition.EquipmentSlot.Weapon => equippedWeapon,
            InventoryItemDefinition.EquipmentSlot.Armor => equippedArmor,
            InventoryItemDefinition.EquipmentSlot.Accessory => equippedAccessory,
            InventoryItemDefinition.EquipmentSlot.Head => equippedHead,
            InventoryItemDefinition.EquipmentSlot.Shoes => equippedShoes,
            InventoryItemDefinition.EquipmentSlot.Other => equippedOther,
            _ => null
        };
    }

    public bool CanAddAny(InventoryItemDefinition item)
    {
        if (item == null) return false;
        EnsureSlots();
        Slot[] target = item.IsQuestItem ? questSlots : slots;
        foreach (Slot slot in target)
        {
            if (slot.IsEmpty) return true;
            if (slot.Item == item && slot.Amount < item.MaxStack) return true;
        }
        return false;
    }

    /// <summary>Returns the accepted count. Unaccepted items stay in the world.</summary>
    public int Add(InventoryItemDefinition item, int amount)
    {
        if (item == null || amount <= 0) return 0;
        EnsureSlots();
        Slot[] target = item.IsQuestItem ? questSlots : slots;
        int remaining = amount;

        if (item.MaxStack > 1)
        {
            foreach (Slot slot in target)
            {
                if (slot.IsEmpty || slot.Item != item) continue;
                int added = Mathf.Min(remaining, Mathf.Max(0, item.MaxStack - slot.Amount));
                slot.Set(item, slot.Amount + added);
                remaining -= added;
                if (remaining == 0) break;
            }
        }

        foreach (Slot slot in target)
        {
            if (remaining == 0) break;
            if (!slot.IsEmpty) continue;
            int added = Mathf.Min(remaining, item.MaxStack);
            slot.Set(item, added);
            remaining -= added;
        }

        int accepted = amount - remaining;
        if (accepted > 0)
        {
            AutoAssignQuickSlot(item);
            Changed?.Invoke();
            ItemCollected?.Invoke(item, accepted);
        }

        if (remaining > 0) NotifyInventoryFull();
        return accepted;
    }

    /// <summary>Moves into empty slots, merges matching stacks, or swaps different items.</summary>
    public bool Move(int from, int to)
    {
        Slot source = GetSlot(from);
        Slot target = GetSlot(to);
        if (from == to || source == null || target == null || source.IsEmpty) return false;
        if (!target.IsEmpty && source.Item == target.Item && target.Amount < target.Item.MaxStack)
        {
            int moved = Mathf.Min(source.Amount, Mathf.Max(0, target.Item.MaxStack - target.Amount));
            if (moved == 0) return false;
            target.Set(target.Item, target.Amount + moved);
            source.Set(source.Item, source.Amount - moved);
        }
        else
        {
            InventoryItemDefinition previousItem = target.Item;
            int previousAmount = target.Amount;
            target.Set(source.Item, source.Amount);
            source.Set(previousItem, previousAmount);
        }

        Changed?.Invoke();
        return true;
    }

    public bool UseSlot(int index, GameObject user)
    {
        Slot slot = GetSlot(index);
        if (slot == null || slot.IsEmpty || !slot.Item.IsConsumable) return false;
        InventoryItemDefinition used = slot.Item;
        if (!used.Use(user)) return false;
        slot.Set(used, slot.Amount - 1);
        RemoveQuickSlotIfMissing(used);
        Changed?.Invoke();
        ItemUsed?.Invoke(used);
        return true;
    }

    public bool EquipFromSlot(int index)
    {
        Slot slot = GetSlot(index);
        if (slot == null || slot.IsEmpty || !slot.Item.IsEquipment || slot.Item.Slot == InventoryItemDefinition.EquipmentSlot.None)
            return false;

        InventoryItemDefinition itemToEquip = slot.Item;
        InventoryItemDefinition previous = GetEquipped(itemToEquip.Slot);
        SetEquipped(itemToEquip.Slot, itemToEquip);
        slot.Set(previous, previous != null ? 1 : 0);
        Changed?.Invoke();
        return true;
    }

    public bool Unequip(InventoryItemDefinition.EquipmentSlot equipmentSlot)
    {
        InventoryItemDefinition equipped = GetEquipped(equipmentSlot);
        if (equipped == null) return false;

        Slot emptySlot = FindEmptySlot();
        if (emptySlot == null)
        {
            NotifyInventoryFull();
            return false;
        }

        emptySlot.Set(equipped, 1);
        SetEquipped(equipmentSlot, null);
        Changed?.Invoke();
        return true;
    }

    public bool UseQuickSlot(int quickIndex, GameObject user)
    {
        InventoryItemDefinition item = GetQuickSlot(quickIndex);
        if (item == null) return false;
        for (int i = 0; i < Capacity; i++)
        {
            Slot slot = GetSlot(i);
            if (slot != null && slot.Item == item) return UseSlot(i, user);
        }

        quickSlots[quickIndex] = null;
        Changed?.Invoke();
        return false;
    }

    public bool AssignQuickSlot(int slotIndex, int quickIndex)
    {
        Slot slot = GetSlot(slotIndex);
        if (slot == null || slot.IsEmpty || !slot.Item.IsConsumable || quickIndex < 0 || quickIndex >= QuickSlotCount)
            return false;
        quickSlots[quickIndex] = slot.Item;
        Changed?.Invoke();
        return true;
    }

    public void NotifyInventoryFull() => InventoryFull?.Invoke();

    private void AutoAssignQuickSlot(InventoryItemDefinition item)
    {
        if (item == null || !item.IsConsumable) return;
        for (int i = 0; i < QuickSlotCount; i++)
            if (quickSlots[i] == item) return;
        for (int i = 0; i < QuickSlotCount; i++)
        {
            if (quickSlots[i] != null) continue;
            quickSlots[i] = item;
            return;
        }
    }

    private void RemoveQuickSlotIfMissing(InventoryItemDefinition item)
    {
        bool stillOwned = false;
        for (int i = 0; i < Capacity; i++)
        {
            Slot slot = GetSlot(i);
            if (slot != null && slot.Item == item)
            {
                stillOwned = true;
                break;
            }
        }

        if (stillOwned) return;
        for (int i = 0; i < QuickSlotCount; i++)
            if (quickSlots[i] == item) quickSlots[i] = null;
    }

    private Slot FindEmptySlot()
    {
        EnsureSlots();
        foreach (Slot slot in slots)
            if (slot.IsEmpty) return slot;
        return null;
    }

    private void SetEquipped(InventoryItemDefinition.EquipmentSlot slot, InventoryItemDefinition item)
    {
        switch (slot)
        {
            case InventoryItemDefinition.EquipmentSlot.Weapon:
                equippedWeapon = item;
                break;
            case InventoryItemDefinition.EquipmentSlot.Armor:
                equippedArmor = item;
                break;
            case InventoryItemDefinition.EquipmentSlot.Accessory:
                equippedAccessory = item;
                break;
            case InventoryItemDefinition.EquipmentSlot.Head:
                equippedHead = item;
                break;
            case InventoryItemDefinition.EquipmentSlot.Shoes:
                equippedShoes = item;
                break;
            case InventoryItemDefinition.EquipmentSlot.Other:
                equippedOther = item;
                break;
        }
    }
}
