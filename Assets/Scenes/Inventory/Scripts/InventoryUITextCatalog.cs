/// <summary>Compatibility helpers for legacy UI; item content belongs to InventoryItemCatalog.</summary>
public sealed class InventoryUITextCatalog
{
    public static InventoryUITextCatalog Load(UnityEngine.TextAsset unused) { InventoryItemCatalog.ReloadIfChanged(); return new InventoryUITextCatalog(); }
    public string ItemName(InventoryItemDefinition item) => item != null ? item.DisplayName : string.Empty;
    public string ItemDescription(InventoryItemDefinition item) => item != null ? item.Description : string.Empty;
    public string StatName(PersonalStatType type, string fallback) => fallback;
    public string StatDescription(PersonalStatType type, string fallback = "") => fallback;
    public string EquipmentSlotName(InventoryItemDefinition.EquipmentSlot slot, string fallback) => fallback;
    public string CategoryName(InventoryItemDefinition.ItemCategory category, string fallback) => fallback;
    public string CategoryName(string id, string fallback) => fallback;
    public string Ui(string id, string fallback) => fallback;
}
