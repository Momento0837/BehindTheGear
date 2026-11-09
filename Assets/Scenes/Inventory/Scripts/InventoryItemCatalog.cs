using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Single source for item descriptions, rarity and signed equipment modifiers.</summary>
public static class InventoryItemCatalog
{
    [Serializable] public sealed class Display { public string name; public string description; }
    [Serializable] public sealed class StatValues
    {
        public int Evasion, Stealth, Attack, Defense, Persuasion;
        public int Get(PersonalStatType type) => Mathf.Max(0, type switch
        {
            PersonalStatType.Evasion => Evasion, PersonalStatType.Stealth => Stealth,
            PersonalStatType.Attack => Attack, PersonalStatType.Defense => Defense,
            PersonalStatType.Persuasion => Persuasion, _ => 0
        });
    }
    [Serializable] public sealed class Stats { public StatValues increase = new(); public StatValues decrease = new(); }
    [Serializable] public sealed class Entry
    {
        public string id;
        public Display display;
        public string rarity = "Normal";
        public Stats stats;
        [NonSerialized] public ItemStatModifier[] modifiers;
        public int Increase(PersonalStatType type) => stats?.increase?.Get(type) ?? 0;
        public int Decrease(PersonalStatType type) => stats?.decrease?.Get(type) ?? 0;
    }
    [Serializable] private sealed class FileData { public Entry[] items; }
    private static TextAsset source;
    private static string previousJson;
    private static readonly Dictionary<string, Entry> entries = new(StringComparer.OrdinalIgnoreCase);
    public static int Revision { get; private set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() { source = null; previousJson = null; entries.Clear(); Revision = 0; }
    public static void ReloadIfChanged()
    {
        if (source == null) source = Resources.Load<TextAsset>("Inventory/inventory_ui_text");
        if (source == null || source.text == previousJson) return;
        previousJson = source.text;
        try
        {
            var file = JsonUtility.FromJson<FileData>(previousJson);
            if (file?.items == null) throw new ArgumentException("items array is required");
            var replacement = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in file.items)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.id)) throw new ArgumentException("Each item needs an id");
                if (replacement.ContainsKey(entry.id)) throw new ArgumentException("Duplicate item id: " + entry.id);
                if (!Enum.TryParse<InventoryItemRarity>(entry.rarity, true, out var rarity) || !Enum.IsDefined(typeof(InventoryItemRarity), rarity))
                    throw new ArgumentException("Unknown rarity for " + entry.id + ": " + entry.rarity);
                if (entry.stats != null)
                {
                    var modifiers = new List<ItemStatModifier>();
                    foreach (PersonalStatType type in Enum.GetValues(typeof(PersonalStatType)))
                    {
                        int amount = entry.Increase(type) - entry.Decrease(type);
                        if (amount != 0) modifiers.Add(new ItemStatModifier(type, amount));
                    }
                    entry.modifiers = modifiers.ToArray();
                }
                replacement.Add(entry.id, entry);
            }
            entries.Clear();
            foreach (var pair in replacement) entries.Add(pair.Key, pair.Value);
            Revision++;
        }
        catch (Exception e) { Debug.LogError("[Item JSON] Keeping last valid settings: " + e.Message); }
    }
    public static Entry Find(string id) { ReloadIfChanged(); return id != null && entries.TryGetValue(id, out var entry) ? entry : null; }
}
public enum InventoryItemRarity { Normal, Advanced, Rare, Legendary }
