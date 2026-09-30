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
        internal void Set(InventoryItemDefinition value, int count)
        {
            item = count > 0 ? value : null;
            amount = item != null ? count : 0;
        }
    }

    [SerializeField, Range(6, 120)] private int capacity = 30;
    [SerializeField] private Slot[] slots;
    public event Action Changed;
    public event Action<InventoryItemDefinition, int> ItemCollected;
    public event Action InventoryFull;
    public int ConfiguredCapacity => Mathf.Clamp(capacity, 6, 120);
    public int Capacity { get { EnsureSlots(); return slots.Length; } }

    private void Awake() => EnsureSlots();

    private void EnsureSlots()
    {
        if (slots == null || slots.Length == 0) slots = new Slot[Mathf.Clamp(capacity, 6, 120)];
        for (int i = 0; i < slots.Length; i++) slots[i] ??= new Slot();
    }

    public Slot GetSlot(int index)
    {
        EnsureSlots();
        return index >= 0 && index < slots.Length ? slots[index] : null;
    }

    /// <summary>Returns the accepted count. Unaccepted items stay in the world.</summary>
    public int Add(InventoryItemDefinition item, int amount)
    {
        if (item == null || amount <= 0) return 0;
        EnsureSlots();
        int remaining = amount;
        foreach (Slot slot in slots)
        {
            if (slot.IsEmpty || slot.Item != item) continue;
            int added = Mathf.Min(remaining, Mathf.Max(0, item.MaxStack - slot.Amount));
            slot.Set(item, slot.Amount + added);
            remaining -= added;
            if (remaining == 0) break;
        }
        foreach (Slot slot in slots)
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
            Changed?.Invoke();
            ItemCollected?.Invoke(item, accepted);
        }
        if (remaining > 0) InventoryFull?.Invoke();
        return accepted;
    }

    /// <summary>Moves into empty slots, merges matching stacks, or swaps different items.</summary>
    public bool Move(int from, int to)
    {
        Slot source = GetSlot(from);
        Slot target = GetSlot(to);
        if (from == to || source == null || target == null || source.IsEmpty) return false;
        if (!target.IsEmpty && source.Item == target.Item)
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
}
